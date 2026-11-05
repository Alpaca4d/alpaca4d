#! python3
# Builds Tools/Examples/TopologyNodes.py's two definitions inside a running Rhino 8 with this build of the plugin,
# runs all three optimisations on each and checks them against ./run.sh bench node's summaries; then analyses
# each design as group 8 does. nodes.sh fills in the placeholders and starts this with rhinocode; it writes what
# it found to result.txt. Loaded as ExampleRun.py loads the plugin.
import os
import sys
import time
import traceback

import clr
import System
from System.Reflection import BindingFlags
from System.Runtime.Loader import AssemblyLoadContext

import Rhino
import Grasshopper
from Grasshopper.Kernel import GH_Document, GH_SolutionMode, GH_RuntimeMessageLevel, IGH_DocumentObject

WORK = "__WORK__"
EXAMPLES = "__EXAMPLES__"
OPENSEES = "__OPENSEES__"

lines = []
fails = [0]


def say(text):
    """A line of the report, written out at once: a run that hangs still shows how far it got."""
    lines.append(text)
    with open(os.path.join(WORK, "progress.txt"), "w") as f:
        f.write("\n".join(lines) + "\n")


def check(ok, what):
    if not ok:
        fails[0] += 1
    say("  [{0}] {1}".format("PASS" if ok else "FAIL", what))


def values(param):
    return [g.Value for g in param.VolatileData.AllData(True)]


def summary(case):
    """method -> (iterations, first, last), as ./run.sh bench node wrote them."""
    out = {}
    for line in open(os.path.join(WORK, "node-{0}-20_summary.csv".format(case))):
        method, iterations, first, last = line.strip().split(",")
        out[method] = (int(iterations), float(first), float(last))
    return out


try:
    Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    context = AssemblyLoadContext("TopologyNodes " + os.path.basename(WORK), False)
    loaded = []
    for name in sorted(os.listdir(WORK)):
        stem, extension = os.path.splitext(name)
        if extension not in (".dll", ".gha") or stem in ("RhinoCommon", "Grasshopper", "GH_IO", "Eto") or stem.startswith("System.") or stem.startswith("Microsoft."):
            continue
        loaded.append(context.LoadFromAssemblyPath(os.path.join(WORK, name)))
    gha = [a for a in loaded if a.GetName().Name == "Alpaca4d.Gh"][0]
    core = [a for a in loaded if a.GetName().Name == "Alpaca4d.Core"][0]
    core.GetType("Alpaca4d.Application").GetField("UserFolder").SetValue(None, WORK)
    core.GetType("Alpaca4d.AlpacaSettings").GetProperty("OpenSeesPath").SetValue(None, OPENSEES)
    core.GetType("Alpaca4d.License.License").GetField("licenseLocation").SetValue(None, os.path.join(WORK, "license.bin"))
    ours = {}
    for t in gha.GetTypes():
        if t.IsAbstract or t.GetConstructor(System.Type.EmptyTypes) is None or not clr.GetClrType(IGH_DocumentObject).IsAssignableFrom(t):
            continue
        try:
            ours[str(System.Activator.CreateInstance(t).ComponentGuid).lower()] = t
        except Exception:
            pass

    def make_object(guid):
        t = ours.get(guid.lower())
        return System.Activator.CreateInstance(t) if t is not None else Grasshopper.Instances.ComponentServer.EmitObject(System.Guid(guid))

    sys.path.insert(0, EXAMPLES)
    import importlib
    import TopologyNodes as example
    example = importlib.reload(example)   # a Rhino keeps modules: this one as the file has it now
    example.make_object = make_object

    cases = "__CASES__".split()
    for case, kept, shares in (("truss", 576, (0.04, 0.08)), ("joint", 1024, (0.06, 0.14))):
        if case not in cases:
            continue
        say("")
        say("The " + ("space-truss node" if case == "truss" else "beam-column joint"))
        expected = summary(case)
        example.CASE = case
        doc = GH_Document()
        doc.FilePath = os.path.join(WORK, "Topology" + case + ".gh")
        example.build(doc)
        objects = [getattr(o, "__implementation__", o) for o in doc.Objects]
        check(len(objects) == 120, "the definition has its 120 objects ({0})".format(len(objects)))
        doc.Enabled = True
        doc.NewSolution(True, GH_SolutionMode.Silent)
        errors = [o.NickName + ": " + m for o in objects if hasattr(o, "RuntimeMessages") for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Error)]
        check(not errors, "it solves without errors{0}".format("" if not errors else ": " + "; ".join(errors)))
        assemble = [o for o in objects if o.GetType().Name == "AssembleModel"][0]
        model = values(assemble.Params.Output[0])[0]
        check(model.Bricks.Count == 8000 + kept, "8 000 bricks of the node and {0} of the members ({1})".format(kept, model.Bricks.Count))
        check(len(model.Supports) > 0 and len(model.LoadPatterns) == 1, "held and loaded ({0} supports)".format(len(model.Supports)))

        by_kind = {"SIMP": "TopologyOptimisation", "BESO": "BesoTopology", "Multi-material": "MultiMaterialTopology"}
        method_of = {"SIMP": "simp", "BESO": "beso", "Multi-material": "multi"}
        for label in ("SIMP", "BESO", "Multi-material"):
            component = [o for o in objects if o.GetType().Name == by_kind[label]][0]
            toggle = [o for o in objects if o.GetType().Name == "GH_BooleanToggle" and o.NickName == "Run " + label][0]
            toggle.Value = True
            toggle.ExpireSolution(False)
            doc.NewSolution(False, GH_SolutionMode.Silent)
            prop = component.GetType().GetProperty("Run", BindingFlags.NonPublic | BindingFlags.Instance)
            start = time.time()
            started = False
            while time.time() - start < 1800:
                r = prop.GetValue(component)
                started = started or r is not None
                if r is not None and not r.GetType().GetProperty("IsRunning").GetValue(r):
                    break
                if not started and time.time() - start > 60:
                    break
                time.sleep(0.5)
            seconds = time.time() - start
            if not started:
                messages = [str(m) for level in (GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning) for m in component.RuntimeMessages(level)]
                check(False, "{0}: Run started a run ({1})".format(label, "; ".join(messages)))
                continue
            component.ExpireSolution(False)
            doc.NewSolution(False, GH_SolutionMode.Silent)
            errors = list(component.RuntimeMessages(GH_RuntimeMessageLevel.Error))
            compliance_out = 6 if label == "Multi-material" else 4
            compliance = values(component.Params.Output[compliance_out])
            iterations, first, last = expected[method_of[label]]
            # Rhino keeps a mesh's vertices in single precision, so the definition's nodes sit at 0.4000000059604645 where the
            # deck's are at 0.4: the stiffness differs in the eighth digit, and so does the compliance.
            check(not errors and compliance and abs(compliance[0] / first - 1.0) < 1e-6,
                  "{0}: the first compliance is the hand-written deck's ({1!r} vs {2!r}){3}".format(
                      label, compliance[0] if compliance else None, first, "" if not errors else " - " + "; ".join(errors)))
            converged = values(component.Params.Output[8 if label == "Multi-material" else 6])
            # SIMP stops on top3d's test, no density moving by 0.01, which a run often never meets: then at Iterations, 200.
            ended = (converged and converged[0]) or (label == "SIMP" and len(compliance) == 200)
            check(ended and abs(compliance[-1] / last - 1.0) < 0.03,
                  "{0}: {1} after {2} iterations at {3:.5g}, in {4:.0f} s (the deck's run: {5} at {6:.5g})".format(
                      label, "converged" if converged and converged[0] else "stopped", len(compliance), compliance[-1], seconds, iterations, last))
            if label == "Multi-material":
                volume = values(component.Params.Output[7])
                n = len(compliance)
                check(volume[n - 1] <= shares[0] + 1e-3 and volume[2 * n - 1] <= shares[1] + 1e-3,
                      "Multi-material: steel {0:.4f} of {1}, the other {2:.4f} of {3}".format(volume[n - 1], shares[0], volume[2 * n - 1], shares[1]))
            toggle.Value = False
            toggle.ExpireSolution(False)
            doc.NewSolution(False, GH_SolutionMode.Silent)

            # Its design analysed, as group 8 does: its Run Analysis and Legend enabled, then locked again.
            run = [o for o in objects if o.GetType().Name == "RunAnalysis" and o.NickName == "Analyse " + label][0]
            results = [o for o in objects if o.GetType().Name == "ViewResults" and o.NickName == "Stress " + label][0]
            legend = [o for o in objects if o.GetType().Name == "Legend" and o.NickName == "Legend " + label][0]
            for o in (run, legend):
                o.Locked = False
                o.ExpireSolution(False)
            doc.NewSolution(False, GH_SolutionMode.Silent)
            errors = [m for o in (run, results) for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Error)]
            info = values(results.Params.Output[0])
            check(not errors and info, "{0}: its design analysed - {1}{2}".format(label, info[0] if info else "nothing", "" if not errors else " - " + "; ".join(errors)))
            for o in (run, legend):
                o.Locked = True
                o.ExpireSolution(False)
            doc.NewSolution(False, GH_SolutionMode.Silent)
        doc.RemoveObjects(list(doc.Objects), False)
except Exception:
    lines.append("  [FAIL] the script ran: " + traceback.format_exc())
    fails[0] += 1

lines.append("")
lines.append("all node checks passed" if fails[0] == 0 else "{0} node check(s) failed".format(fails[0]))
with open(os.path.join(WORK, "result.txt"), "w") as f:
    f.write("\n".join(lines) + "\n")
