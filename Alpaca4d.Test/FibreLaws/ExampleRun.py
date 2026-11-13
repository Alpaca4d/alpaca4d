#! python3
# Builds the three definitions of Tools/Examples/Design/FibreSections.py (laws, hea, composite) inside a running Rhino 8
# with this build of the plugin, solves them, and reports every Moment Curvature and Interaction Domain: errors, the
# ultimate moment, ductility, what failed. example.sh fills in the placeholders and starts this with rhinocode; it
# writes what it found to result.txt. Loaded as ../TopologyMethods/ExampleRun.py loads the plugin.
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

    context = AssemblyLoadContext("FibreLaws " + os.path.basename(WORK), False)
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

    try:
        sl = Grasshopper.Instances.ComponentServer.EmitObject(System.Guid("0F82C248-4B58-4C25-9F85-6B7F7C4696C0"))
        check(sl is not None, "the plugin installed in Grasshopper loads its Section Library")
    except Exception as e:
        check(False, "the plugin installed in Grasshopper cannot make its Section Library: " + str(e)[:300])

    sys.path.insert(0, EXAMPLES)
    import importlib

    for case in ("laws", "hea", "composite"):
        System.Environment.SetEnvironmentVariable("SECTION_CASE", case)
        if "FibreSections" in sys.modules:
            del sys.modules["FibreSections"]
        example = __import__("FibreSections")
        example.make_object = make_object
        doc = GH_Document()
        doc.FilePath = os.path.join(WORK, case + ".gh")
        example.build(doc)
        objects = [getattr(o, "__implementation__", o) for o in doc.Objects]
        doc.Enabled = True
        doc.NewSolution(True, GH_SolutionMode.Silent)
        lines.append("{0}: {1} objects".format(case, len(objects)))
        bad = []
        for o in objects:
            if not hasattr(o, "RuntimeMessages"):
                continue
            for lvl in (GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning):
                for m in o.RuntimeMessages(lvl):
                    bad.append("{0} {1}: {2}".format(o.NickName or o.Name, "ERR" if lvl == GH_RuntimeMessageLevel.Error else "warn", m))
        check(not [b for b in bad if " ERR:" in b], "{0}: no component errors {1}".format(case, "; ".join(b for b in bad if " ERR:" in b)))
        for b in bad:
            if " warn:" in b:
                lines.append("    " + b)
        for o in objects:
            n = o.GetType().Name
            if n == "MomentCurvature":
                names = {p.Name: values(p) for p in o.Params.Output if p.Name in ("UltimateMoment", "YieldMoment", "Ductility", "Failure", "UltimateCurvature")}
                mu = names["UltimateMoment"]
                check(len(mu) == 1 and mu[0] > 0, "{0}: {1}: Mu {2}, My {3}, ductility {4}, ku {5} - {6}".format(
                    case, o.NickName, ["%.1f" % v for v in mu], ["%.1f" % v for v in names["YieldMoment"]],
                    ["%.2f" % v for v in names["Ductility"]], ["%.4f" % v for v in names["UltimateCurvature"]], names["Failure"]))
            elif n == "InteractionDomain":
                nc = values([p for p in o.Params.Output if p.Name == "Nc"][0])
                nt = values([p for p in o.Params.Output if p.Name == "Nt"][0])
                check(len(nc) == 1 and nc[0] < 0, "{0}: {1}: domain Nc {2}, Nt {3}".format(case, o.NickName, ["%.0f" % v for v in nc], ["%.0f" % v for v in nt]))
            elif n in ("NMDiagram", "MomentCurvatureDiagram", "MyMzDiagram"):
                c = values(o.Params.Output[0])
                check(len(c) > 0, "{0}: {1} {2} draws {3} curve(s)".format(case, n, o.NickName, len(c)))
except Exception:
    lines.append("  [FAIL] the script ran: " + traceback.format_exc())
    fails[0] += 1

lines.append("")
lines.append("all example checks passed" if fails[0] == 0 else "{0} example check(s) failed".format(fails[0]))
import io
with io.open(os.path.join(WORK, "result.txt"), "w", encoding="utf-8") as f:
    f.write(u"\n".join(l if isinstance(l, type(u"")) else l.decode("utf-8", "replace") for l in lines) + u"\n")
