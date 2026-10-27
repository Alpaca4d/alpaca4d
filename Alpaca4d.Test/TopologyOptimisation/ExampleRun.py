#! python3
# Builds Tools/Examples/TopologyBenchmarks.py's definition inside a running Rhino 8 with this build of the
# plugin, runs both benchmarks for a few iterations and checks them against the test bench's histories.
# example.sh fills in the placeholders and starts this with rhinocode; it writes what it found to
# result.txt.
#
# The definition is made by the example's own build(doc), with its make_object pointed at this build's
# components, loaded into a load context of their own beside the installed Alpaca4d - as
# GrasshopperRun.cs loads them - and at Grasshopper's component server for Grasshopper's own.
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
from Grasshopper.Kernel import GH_Document, GH_SolutionMode, IGH_DocumentObject

WORK = "__WORK__"
EXAMPLES = "__EXAMPLES__"
OPENSEES = "__OPENSEES__"
ITERATIONS = int("__ITERATIONS__")
# Each problem's compliance at that iteration, from ./run.sh bench; 0 to skip the comparison.
EXPECTED = {"Cantilever": float("__CANTILEVER__"), "Simply supported": float("__SIMPLE__")}

lines = []
fails = [0]


def check(ok, what):
    if not ok:
        fails[0] += 1
    lines.append("  [{0}] {1}".format("PASS" if ok else "FAIL", what))


try:
    Rhino.RhinoApp.GetPlugInObject("Grasshopper")

    context = AssemblyLoadContext("TopologyBenchmarks " + os.path.basename(WORK), False)
    from_rhino = ("RhinoCommon", "Grasshopper", "GH_IO", "Eto")
    loaded = []
    for name in sorted(os.listdir(WORK)):
        stem, extension = os.path.splitext(name)
        if extension not in (".dll", ".gha") or stem in from_rhino or stem.startswith("System.") or stem.startswith("Microsoft."):
            continue
        loaded.append(context.LoadFromAssemblyPath(os.path.join(WORK, name)))
    gha = [a for a in loaded if a.GetName().Name == "Alpaca4d.Gh"][0]
    core = [a for a in loaded if a.GetName().Name == "Alpaca4d.Core"][0]

    # Settings and license in the test folder, not the user's.
    core.GetType("Alpaca4d.Application").GetField("UserFolder").SetValue(None, WORK)
    core.GetType("Alpaca4d.AlpacaSettings").GetProperty("OpenSeesPath").SetValue(None, OPENSEES)
    core.GetType("Alpaca4d.License.License").GetField("licenseLocation").SetValue(None, os.path.join(WORK, "license.bin"))

    # This build's objects by GUID; anything else from Grasshopper's component server.
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
    import TopologyBenchmarks as example
    example.make_object = make_object

    doc = GH_Document()
    doc.FilePath = os.path.join(WORK, "TopologyBenchmarks.gh")
    example.build(doc)
    objects = [getattr(o, "__implementation__", o) for o in doc.Objects]
    check(len(objects) == 62, "the definition has its 62 objects ({0})".format(len(objects)))

    topologies = dict((o.NickName, o) for o in objects if o.GetType().Name == "TopologyOptimisation")

    # They play In the Hall of the Mountain King unless told otherwise; the check runs silent.
    for topology in topologies.values():
        sound = topology.GetType().GetProperty("Sound", BindingFlags.NonPublic | BindingFlags.Instance)
        check(str(sound.GetValue(topology)) == "MountainKing", "{0} plays In the Hall of the Mountain King".format(topology.NickName))
        sound.SetValue(topology, System.Enum.Parse(sound.PropertyType, "Off"))
    toggles = dict((o.NickName, o) for o in objects if o.GetType().Name == "GH_BooleanToggle")
    check(sorted(topologies) == ["Cantilever", "Simply supported"], "with two Topology Optimisation components")

    for topology in topologies.values():
        iterations = topology.Params.Input[3]
        iterations.PersistentData.Clear()
        iterations.PersistentData.Append(Grasshopper.Kernel.Types.GH_Integer(ITERATIONS))

    doc.Enabled = True
    doc.NewSolution(True, GH_SolutionMode.Silent)

    def messages(component, level):
        return list(component.RuntimeMessages(level))

    for name, topology in topologies.items():
        errors = messages(topology, Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)
        warnings = messages(topology, Grasshopper.Kernel.GH_RuntimeMessageLevel.Warning)
        check(not errors and any("Set Run to true" in w for w in warnings),
              "{0}, before Run: waits for it ({1})".format(name, "; ".join(errors + warnings)))

    for name in topologies:
        toggle = toggles["Run " + name]
        toggle.Value = True
        toggle.ExpireSolution(False)
    doc.NewSolution(False, GH_SolutionMode.Silent)

    # Run and its IsRunning are internal to the plugin: read them by reflection.
    run_property = topologies["Cantilever"].GetType().GetProperty("Run", BindingFlags.NonPublic | BindingFlags.Instance)

    def running(topology):
        run = run_property.GetValue(topology)
        return run is None or run.GetType().GetProperty("IsRunning").GetValue(run)

    start = time.time()
    while time.time() - start < 600 and any(running(t) for t in topologies.values()):
        time.sleep(0.2)
    lines.append("  both runs took {0:.1f} s".format(time.time() - start))

    for topology in topologies.values():
        topology.ExpireSolution(False)
    doc.NewSolution(False, GH_SolutionMode.Silent)

    for name, topology in topologies.items():
        errors = messages(topology, Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)
        check(not errors, "{0}: no errors{1}".format(name, "" if not errors else " - " + "; ".join(errors)))
        compliance = [g.Value for g in topology.Params.Output[4].VolatileData.AllData(True)]
        density = [g.Value for g in topology.Params.Output[2].VolatileData.AllData(True)]
        surface = [g.Value for g in topology.Params.Output[0].VolatileData.AllData(True)]
        check(len(compliance) == ITERATIONS, "{0}: {1} iterations ({2})".format(name, ITERATIONS, len(compliance)))
        expected = EXPECTED[name]
        if expected > 0 and compliance:
            gap = abs(compliance[-1] / expected - 1.0)
            check(gap < 1e-6, "{0}: compliance {1!r} at iteration {2}, the test bench's {3!r} ({4:.1e} apart)".format(
                name, compliance[-1], ITERATIONS, expected, gap))
        check(len(surface) == 1 and surface[0].IsClosed, "{0}: the surface is one closed mesh".format(name))
        volume = sum(density) / max(1, len(density))
        check(abs(volume - 0.3) < 2e-3, "{0}: 30% of the volume ({1:.4f})".format(name, volume))

    # Its Step slider: dragged back, the design after that iteration; at its end, the latest again.
    cantilever = topologies["Cantilever"]
    step = [o for o in objects if o.GetType().Name == "GH_NumberSlider" and o.NickName == "Step Cantilever"][0]
    latest = [g.Value for g in cantilever.Params.Output[2].VolatileData.AllData(True)]

    def scrub(value):
        step.SetSliderValue(System.Decimal(value))
        step.ExpireSolution(False)
        doc.NewSolution(False, GH_SolutionMode.Silent)
        shown = [g.Value for g in cantilever.Params.Output[9].VolatileData.AllData(True)]
        return (shown[0] if shown else None), [g.Value for g in cantilever.Params.Output[2].VolatileData.AllData(True)]

    shown, start = scrub(0)
    check(shown == 0 and all(abs(d - 0.3) < 0.01 for d in start), "Step 0 shows the uniform design the run starts from")
    shown, tenth = scrub(10)
    moved = max(abs(a - b) for a, b in zip(tenth, latest))
    check(shown == 10 and moved > 0.1 and abs(sum(tenth) / len(tenth) - 0.3) < 0.01,
          "Step 10 shows the design after ten iterations, between the two ({0:.2f} from the latest at most)".format(moved))
    shown, end = scrub(200)
    check(shown == ITERATIONS and max(abs(a - b) for a, b in zip(end, latest)) == 0.0, "and at its end, the latest again ({0})".format(shown))

    # The panels the definition shows: the last compliance of each run.
    shown = [o for o in objects if o.GetType().Name == "GH_Panel" and o.NickName == "Compliance, last iteration"]
    check(len(shown) == 2 and all(p.VolatileData.DataCount == 1 for p in shown), "the 'Compliance, last iteration' panels show one value each")
except Exception:
    lines.append("  [FAIL] the script ran: " + traceback.format_exc())
    fails[0] += 1

lines.append("")
lines.append("all example checks passed" if fails[0] == 0 else "{0} example check(s) failed".format(fails[0]))
with open(os.path.join(WORK, "result.txt"), "w") as f:
    f.write("\n".join(lines) + "\n")
