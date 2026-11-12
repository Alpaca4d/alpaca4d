#! python3
# Builds Tools/Examples/Topology/MultiMaterialBridge.py's and BesoBridge.py's definitions inside a running Rhino 8 with
# this build of the plugin, runs each optimisation to its end, analyses the design as the definition does,
# and checks what comes out. example.sh fills in the placeholders and starts this with rhinocode; it writes
# what it found to result.txt. Loaded as ../TopologyOptimisation/ExampleRun.py loads the plugin.
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


def check(ok, what):
    if not ok:
        fails[0] += 1
    lines.append("  [{0}] {1}".format("PASS" if ok else "FAIL", what))


def values(param):
    return [g.Value for g in param.VolatileData.AllData(True)]


try:
    Rhino.RhinoApp.GetPlugInObject("Grasshopper")

    context = AssemblyLoadContext("TopologyMethods " + os.path.basename(WORK), False)
    from_rhino = ("RhinoCommon", "Grasshopper", "GH_IO", "Eto")
    loaded = []
    for name in sorted(os.listdir(WORK)):
        stem, extension = os.path.splitext(name)
        if extension not in (".dll", ".gha") or stem in from_rhino or stem.startswith("System.") or stem.startswith("Microsoft."):
            continue
        loaded.append(context.LoadFromAssemblyPath(os.path.join(WORK, name)))
    gha = [a for a in loaded if a.GetName().Name == "Alpaca4d.Gh"][0]
    core = [a for a in loaded if a.GetName().Name == "Alpaca4d.Core"][0]

    core.GetType("Alpaca4d.Application").GetField("UserFolder").SetValue(None, WORK)
    core.GetType("Alpaca4d.AlpacaSettings").GetProperty("OpenSeesPath").SetValue(None, OPENSEES)
    core.GetType("Alpaca4d.License.License").GetField("licenseLocation").SetValue(None, os.path.join(WORK, "license.bin"))

    ours = {}
    for t in gha.GetTypes():
        if t.IsAbstract or t.GetConstructor(System.Type.EmptyTypes) is None:
            continue
        if not clr.GetClrType(IGH_DocumentObject).IsAssignableFrom(t):
            continue
        try:
            ours[str(System.Activator.CreateInstance(t).ComponentGuid).lower()] = t
        except Exception:
            pass

    def make_object(guid):
        t = ours.get(guid.lower())
        if t is not None:
            return System.Activator.CreateInstance(t)
        return Grasshopper.Instances.ComponentServer.EmitObject(System.Guid(guid))

    sys.path.insert(0, EXAMPLES)

    def run(module, kind):
        """Builds the example, runs its optimisation to its end, and returns the document and the component."""
        example = __import__(module)
        example.make_object = make_object
        doc = GH_Document()
        doc.FilePath = os.path.join(WORK, module + ".gh")
        example.build(doc)
        objects = [getattr(o, "__implementation__", o) for o in doc.Objects]
        component = [o for o in objects if o.GetType().Name == kind][0]
        doc.Enabled = True
        doc.NewSolution(True, GH_SolutionMode.Silent)
        errors = list(component.RuntimeMessages(GH_RuntimeMessageLevel.Error))
        warnings = list(component.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
        check(not errors and any("Set Run to true" in w for w in warnings),
              "{0}, before Run: waits for it ({1})".format(module, "; ".join(errors + warnings)))

        toggle = [o for o in objects if o.GetType().Name == "GH_BooleanToggle" and o.NickName == "Run"][0]
        toggle.Value = True
        toggle.ExpireSolution(False)
        doc.NewSolution(False, GH_SolutionMode.Silent)
        prop = component.GetType().GetProperty("Run", BindingFlags.NonPublic | BindingFlags.Instance)
        start = time.time()
        while time.time() - start < 1500:
            r = prop.GetValue(component)
            if r is not None and not r.GetType().GetProperty("IsRunning").GetValue(r):
                break
            time.sleep(0.5)
        lines.append("  {0}: the run took {1:.0f} s".format(module, time.time() - start))
        component.ExpireSolution(False)
        doc.NewSolution(False, GH_SolutionMode.Silent)
        errors = list(component.RuntimeMessages(GH_RuntimeMessageLevel.Error))
        check(not errors, "{0}: no errors{1}".format(module, "" if not errors else " - " + "; ".join(errors)))
        return doc, objects, component

    def analyse(module, doc, objects):
        """Enables Run Analysis and Legend, as the note says, and reads View Results."""
        for o in objects:
            if o.GetType().Name in ("RunAnalysis", "Legend"):
                o.Locked = False
                o.ExpireSolution(False)
        doc.NewSolution(False, GH_SolutionMode.Silent)
        results = [o for o in objects if o.GetType().Name == "ViewResults"][0]
        analysis = [o for o in objects if o.GetType().Name == "RunAnalysis"][0]
        errors = [m for o in (analysis, results) for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Error)]
        check(not errors, "{0}: the design analysed, no errors{1}".format(module, "" if not errors else " - " + "; ".join(errors)))
        drawn = values(results.Params.Output[0])
        legend = values(results.Params.Output[1])
        check(len(drawn) > 0 and len(legend) > 0, "{0}: View Results shows Von Mises stresses ({1})".format(module, drawn[:1]))

    # Multi-material: steel and concrete, at most 5% and 15%.
    doc, objects, mm = run("MultiMaterialBridge", "MultiMaterialTopology")
    compliance = values(mm.Params.Output[6])
    volume = [values(mm.Params.Output[7])[i * len(compliance):(i + 1) * len(compliance)] for i in range(2)]
    choice = values(mm.Params.Output[3])
    surfaces = values(mm.Params.Output[0])
    check(len(compliance) > 250 and compliance[-1] < 0.5 * compliance[0],
          "MultiMaterialBridge: {0} iterations, the compliance from {1:.4g} to {2:.4g}".format(len(compliance), compliance[0], compliance[-1]))
    check(volume[0][-1] <= 0.051 and volume[1][-1] <= 0.151 and volume[0][-1] > 0.04 and volume[1][-1] > 0.14,
          "MultiMaterialBridge: steel {0:.4f} of 0.05, concrete {1:.4f} of 0.15".format(volume[0][-1], volume[1][-1]))
    check(choice.count(0) > 0 and choice.count(1) > 0,
          "MultiMaterialBridge: {0} steel and {1} concrete bricks, deck included".format(choice.count(0), choice.count(1)))
    check(len(surfaces) == 2 and all(s.Faces.Count > 0 for s in surfaces), "MultiMaterialBridge: a surface for each")
    lines.append("  " + str(values(mm.Params.Output[9])[-1]))
    analyse("MultiMaterialBridge", doc, objects)

    # BESO: from the whole block down to a fifth.
    doc, objects, beso = run("BesoBridge", "BesoTopology")
    compliance = values(beso.Params.Output[4])
    volume = values(beso.Params.Output[5])
    check(volume[0] == 1.0 and abs(volume[-1] - 0.2) < 0.002 and values(beso.Params.Output[6])[0],
          "BesoBridge: converged after {0} iterations, the volume from 1 to {1:.4f}".format(len(volume), volume[-1]))
    density = values(beso.Params.Output[2])
    check(all(d == 1.0 or abs(d - 1e-3) < 1e-12 for d in density), "BesoBridge: every brick solid or void")
    lines.append("  " + str(values(beso.Params.Output[7])[-1]))
    analyse("BesoBridge", doc, objects)
except Exception:
    lines.append("  [FAIL] the script ran: " + traceback.format_exc())
    fails[0] += 1

lines.append("")
lines.append("all example checks passed" if fails[0] == 0 else "{0} example check(s) failed".format(fails[0]))
with open(os.path.join(WORK, "result.txt"), "w") as f:
    f.write("\n".join(lines) + "\n")
