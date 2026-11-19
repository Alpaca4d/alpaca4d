#! python3
# Builds Tools/Examples/Structures/WaveRoofHall.py's definition inside a running Rhino 8 with this build of the plugin,
# solves it over its eight load combinations, and reads its four checks: the largest utilisation of each over the ULS
# combinations, where and by which check, and k_mod of each combination for the timber. example.sh fills in the
# placeholders and starts this with rhinocode; it writes what it found to result.txt. The plugin is loaded as
# ../TopologyMethods/ExampleRun.py loads it.
import os
import sys
import traceback

import clr
import System
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
    lines.append(text)
    with open(os.path.join(WORK, "progress.txt"), "w") as f:
        f.write("\n".join(lines) + "\n")


def check(ok, what):
    if not ok:
        fails[0] += 1
    say("  [{0}] {1}".format("PASS" if ok else "FAIL", what))


def output(c, name):
    return [p for p in c.Params.Output if p.Name == name][0]


def branches(param):
    """Each branch of an output by the combination's place, its items as they script."""
    data = param.VolatileData
    found = {}
    for i in range(data.PathCount):
        found[data.Paths[i].Indices[0]] = [g.ScriptVariable() if g is not None else None for g in data.get_Branch(i)]
    return found


try:
    Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    Grasshopper.Instances.ComponentServer.ObjectProxies.Count

    context = AssemblyLoadContext("WaveRoofHall " + os.path.basename(WORK), False)
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
    import importlib
    import Structures
    Structures = importlib.reload(Structures)
    import WaveRoofHall as example
    example = importlib.reload(example)
    example.make_object = make_object

    doc = GH_Document()
    doc.FilePath = os.path.join(WORK, "WaveRoofHall.gh")
    example.build(doc)
    objects = [getattr(o, "__implementation__", o) for o in doc.Objects]
    say("The definition: {0} objects".format(len(objects)))

    doc.Enabled = True
    doc.NewSolution(True, GH_SolutionMode.Silent)

    for o in objects:
        if not hasattr(o, "RuntimeMessages"):
            continue
        for level in (GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning):
            for m in o.RuntimeMessages(level):
                say("  {0} {1}: {2}".format("ERROR" if level == GH_RuntimeMessageLevel.Error else "warning", o.NickName, m))

    run = [o for o in objects if o.GetType().Name == "RunAnalysis"][0]
    model = run.Params.Output[0].VolatileData.AllData(True).__iter__().__next__().ScriptVariable()
    say("Combinations solved: {0}".format(", ".join(str(m.Combination.Name) + ("" if m.CombinationFailure is None else " FAILED") for m in model.CombinationResults)))
    check(all(m.CombinationFailure is None for m in model.CombinationResults), "every combination solved")

    checks = [o for o in objects if o.GetType().Name == "Utilisation"]
    names = example.ULTIMATE
    for c in checks:
        maxima = branches(output(c, "Max"))
        governing = branches(output(c, "Governing"))
        elements = [g.ScriptVariable() for g in output(c, "Element").VolatileData.AllData(True)]
        types = [g.ScriptVariable() for g in output(c, "Type").VolatileData.AllData(True)]
        say("")
        say("{0}: {1} members, {2}".format(c.NickName, len(elements), ", ".join(sorted(set(str(t) for t in types)))))
        worst = (-1.0, None, None, None)
        for place in sorted(maxima):
            values = maxima[place]
            known = [(v, k) for k, v in enumerate(values) if v is not None]
            missing = len(values) - len(known)
            if not known:
                say("  {0}: nothing checked".format(names[place]))
                continue
            v, k = max(known)
            say("  {0}: largest {1:.3f} at element {2} ({3}), by {4}{5}".format(
                names[place], v, elements[k].Id, elements[k].ElementId, governing[place][k],
                "; {0} without a Max".format(missing) if missing else ""))
            if v > worst[0]:
                worst = (v, names[place], elements[k].Id, governing[place][k])
        if c.ActiveUnit.Name == "UtilisationTimber":
            kmod = branches(output(c, "kmod"))
            say("  k_mod: " + ", ".join("{0} {1:g}".format(names[p], kmod[p][0]) for p in sorted(kmod)))
        check(worst[0] > 0 and worst[0] <= 1.0, "{0}: largest {1:.3f}, under {2}, element {3}, by {4}".format(c.NickName, *worst))

    moved = [o for o in objects if o.GetType().Name == "GH_Panel" and o.NickName.startswith("SLS 1: largest displacement")][0]
    sag = [g.ScriptVariable() for g in moved.VolatileData.AllData(True)]
    say("")
    say("SLS 1, the largest displacement: {0} mm".format(", ".join(str(v) for v in sag)))
    check(len(sag) == 1 and 0 < float(sag[0]) < 50000 / 300.0, "under L/300 = 167 mm")

    # The sizing: each optimiser's sections by kind, utilisation and mass; and its plot.
    for o in [o for o in objects if o.GetType().Name == "SectionOptimiser"]:
        say("")
        elements = [g.ScriptVariable() for g in output(o, "Element").VolatileData.AllData(True)]
        sections = [g.ScriptVariable() for g in output(o, "Section").VolatileData.AllData(True)]
        used = [g.ScriptVariable() if g is not None else None for g in output(o, "Utilisation").VolatileData.AllData(True)]
        governing = [g.ScriptVariable() for g in output(o, "Governing").VolatileData.AllData(True)]
        mass = [g.ScriptVariable() for g in output(o, "Mass").VolatileData.AllData(True)]
        converged = [g.ScriptVariable() for g in output(o, "Converged").VolatileData.AllData(True)]
        for level in (GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark):
            for m in o.RuntimeMessages(level):
                say("  {0}: {1}".format(o.NickName, m))
        say("{0}: {1} beams, mass {2} kg, converged {3}".format(
            o.NickName, len(elements), ", ".join("{0:.1f}".format(m) for m in mass), converged))
        kinds = {}
        for e, s, u, g in zip(elements, sections, used, governing):
            k = kinds.setdefault(str(e.ElementId), {"sections": set(), "worst": (-1.0, None)})
            k["sections"].add(str(getattr(s, "SectionName", None) or s))
            if u is not None and u > k["worst"][0]:
                k["worst"] = (u, g)
        for kind in sorted(kinds):
            k = kinds[kind]
            say("  {0}: {1}, largest {2:.3f} by {3}".format(kind, ", ".join(sorted(k["sections"])), k["worst"][0], k["worst"][1]))
        known = [u for u in used if u is not None]
        missing = len(used) - len(known)
        if o.NickName == "Sized":
            check(known and max(known) <= 1.0 and missing == 0, "sized: every member at or below 1 ({0:.3f} at most, {1} unchecked)".format(max(known), missing))
            check(converged and converged[0], "sized: converged")
        plot = [c for c in objects if c.NickName == "Utilisation " + o.NickName.lower()][0]
        meshes = [g.ScriptVariable() for g in plot.Params.Output[1].VolatileData.AllData(True)]
        errors = list(plot.RuntimeMessages(GH_RuntimeMessageLevel.Error)) + list(plot.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
        check(len(meshes) == 1 and meshes[0].Faces.Count == 6 * len(elements) and not errors,
              "{0}: the plot draws every beam ({1} faces){2}".format(o.NickName, meshes[0].Faces.Count if meshes else 0, "; " + "; ".join(errors) if errors else ""))

    say("")
    say("Remarks:")
    for c in checks:
        for m in c.RuntimeMessages(GH_RuntimeMessageLevel.Remark):
            say("  {0}: {1}".format(c.NickName, m))
    doc.Enabled = False
except Exception:
    say("  [FAIL] the script ran: " + traceback.format_exc())

say("")
say("all checks passed" if fails[0] == 0 else "{0} check(s) failed".format(fails[0]))
with open(os.path.join(WORK, "result.txt"), "w") as f:
    f.write("\n".join(lines) + "\n")
