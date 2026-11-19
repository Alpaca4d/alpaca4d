#! python3
# Builds Tools/Examples/Topology/TopologyNodes.py's two definitions inside a running Rhino 8 with this build of the plugin,
# runs the three methods of each - SIMP, BESO and Multi-Material - together and checks them against ./run.sh bench
# node's summaries; then, with PICTURES, draws the three designs side by side, and analyses each design as group 8
# does. nodes.sh fills in the placeholders and starts this with rhinocode; it writes what it found to result.txt.
# Loaded as ExampleRun.py loads the plugin.
import math
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
from Rhino.Geometry import BoundingBox, Plane, Point3d, Transform, Vector3d

WORK = "__WORK__"
EXAMPLES = "__EXAMPLES__"
OPENSEES = "__OPENSEES__"
PICTURES = "__PICTURES__" == "1"

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


def rhino_doc():
    """Rhino's document for the pictures: the active one when it has a window, else an empty one opened from the work
    folder - a Rhino with no window open has no document, and one a script makes has no window to capture."""
    rdoc = Rhino.RhinoDoc.ActiveDoc
    view = rdoc.Views.ActiveView if rdoc is not None else None
    if view is None or view.ActiveViewport.Size.Width == 0:
        path = os.path.join(WORK, "pictures.3dm")
        empty = Rhino.FileIO.File3dm()
        empty.Settings.ModelUnitSystem = Rhino.UnitSystem.Meters
        empty.Write(path, 8)
        rdoc = Rhino.RhinoDoc.Open(path)[0]
    return rdoc


def capture(rdoc, path, width, looking):
    """The active view, maximised, in perspective with the camera at looking (a direction from the middle), fitted to
    everything, on a light grey (251). The capture has the viewport's own shape: trim it to the drawn pixels after
    (Tools/Marketing/post.py trim)."""
    view = rdoc.Views.ActiveView
    view.Maximized = True
    vp = view.ActiveViewport
    box = BoundingBox.Empty
    for o in rdoc.Objects:
        box.Union(o.Geometry.GetBoundingBox(True))
    saved = Rhino.DocObjects.ViewportInfo(vp)
    # Rhino lights a shaded view from the camera, so a face seen at a slant comes out dark.
    vp.ChangeToPerspectiveProjection(True, 50.0)
    vp.SetCameraLocations(box.Center, box.Center + looking * box.Diagonal.Length * 2.0)
    box.Inflate(box.Diagonal.Length * 0.02)
    vp.ZoomBoundingBox(box)
    height = int(round(width * vp.Size.Height / float(vp.Size.Width)))
    modes = Rhino.Display.DisplayModeDescription
    shaded = modes.FindByName("Shaded")
    # Rhino keeps display modes in its settings: clear any a run that stopped left behind.
    for old in modes.GetDisplayModes():
        if old.EnglishName == "Nodes grey":
            modes.DeleteDisplayMode(old.Id)
    mode = modes.GetDisplayMode(modes.CopyDisplayMode(shaded.Id, "Nodes grey"))
    try:
        attrs = mode.DisplayAttributes
        attrs.FillMode = Rhino.Display.DisplayPipelineAttributes.FrameBufferFillMode.SolidColor
        attrs.SetFill(System.Drawing.Color.FromArgb(251, 251, 251))
        for name in ("ShowGrid", "ShowGridAxes", "ShowWorldAxes"):
            try:
                setattr(attrs, name, False)
            except Exception:
                pass
        try:
            attrs.MeshSpecificAttributes.ShowMeshWires = False
        except Exception:
            pass
        vp.ConstructionGridVisible = False
        vp.ConstructionAxesVisible = False
        vp.WorldAxesVisible = False
        modes.UpdateDisplayMode(mode)
        vp.DisplayMode = mode
        view.Redraw()
        Rhino.RhinoApp.Wait()
        # The first capture can come before the mode takes hold.
        view.CaptureToBitmap(System.Drawing.Size(width, height), mode).Dispose()
        view.Redraw()
        Rhino.RhinoApp.Wait()
        view.CaptureToBitmap(System.Drawing.Size(width, height), mode).Save(path)
    finally:
        vp.DisplayMode = shaded
        modes.DeleteDisplayMode(mode.Id)
        vp.SetViewProjection(saved, True)
    say("  drew " + os.path.basename(path))


def attributes(colour):
    a = Rhino.DocObjects.ObjectAttributes()
    a.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject
    a.ObjectColor = colour
    return a


try:
    Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    # Grasshopper loads its libraries when its component server is first used. Use it now, before this build goes
    # in: otherwise an installed Alpaca4d binds to this build's Core, with this build's Newtonsoft.Json, and its
    # Section Library fails ("Grasshopper Loading Errors", Method not found ... FindEntry).
    Grasshopper.Instances.ComponentServer.ObjectProxies.Count
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

    labels = ("SIMP", "BESO", "Multi-Material")
    method_of = {"SIMP": "simp", "BESO": "beso", "Multi-Material": "multi"}
    unit_of = {"SIMP": "TopologyOptimisation", "BESO": "BesoTopology", "Multi-Material": "MultiMaterialTopology"}
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
        runs = dict((o.NickName, o) for o in objects if o.GetType().Name == "TopologyOptimisation")
        methods = dict((n, c.ActiveUnit.Name if c.ActiveUnit is not None else "TopologyOptimisation") for n, c in runs.items())
        check(methods == unit_of and all(not c.Obsolete for c in runs.values()),
              "three Topology Optimisation components, not the retired ones, each on its method ({0})".format(methods))
        doc.Enabled = True
        doc.NewSolution(True, GH_SolutionMode.Silent)
        errors = [o.NickName + ": " + m for o in objects if hasattr(o, "RuntimeMessages") for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Error)]
        check(not errors, "it solves without errors{0}".format("" if not errors else ": " + "; ".join(errors)))
        assemble = [o for o in objects if o.GetType().Name == "AssembleModel"][0]
        model = values(assemble.Params.Output[0])[0]
        check(model.Bricks.Count == 8000 + kept, "8 000 bricks of the node and {0} of the members ({1})".format(kept, model.Bricks.Count))
        check(len(model.Supports) > 0 and len(model.LoadPatterns) == 1, "held and loaded ({0} supports)".format(len(model.Supports)))

        # All three at once, as a user setting the three Runs to true would.
        for label in labels:
            toggle = [o for o in objects if o.GetType().Name == "GH_BooleanToggle" and o.NickName == "Run " + label][0]
            toggle.Value = True
            toggle.ExpireSolution(False)
        doc.NewSolution(False, GH_SolutionMode.Silent)
        prop = runs["SIMP"].GetType().GetProperty("Run", BindingFlags.NonPublic | BindingFlags.Instance)
        start = time.time()
        took = {}
        while time.time() - start < 2400 and len(took) < 3:
            for label in labels:
                r = prop.GetValue(runs[label])
                if label not in took and r is not None and not r.GetType().GetProperty("IsRunning").GetValue(r):
                    took[label] = time.time() - start
            if not took and all(prop.GetValue(runs[label]) is None for label in labels) and time.time() - start > 60:
                break
            time.sleep(0.5)
        say("  the runs took " + ", ".join("{0} {1:.0f} s".format(n, s) for n, s in sorted(took.items(), key=lambda x: x[1])))
        for c in runs.values():
            c.ExpireSolution(False)
        doc.NewSolution(False, GH_SolutionMode.Silent)

        found = {}
        for label in labels:
            component = runs[label]
            multi = label == "Multi-Material"
            if label not in took:
                messages = [str(m) for level in (GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning) for m in component.RuntimeMessages(level)]
                check(False, "{0}: Run started a run that ended ({1})".format(label, "; ".join(messages)))
                continue
            errors = list(component.RuntimeMessages(GH_RuntimeMessageLevel.Error))
            compliance = values(component.Params.Output[6 if multi else 4])
            iterations, first, last = expected[method_of[label]]
            # Rhino keeps a mesh's vertices in single precision, so the definition's nodes sit at 0.4000000059604645 where the
            # deck's are at 0.4: the stiffness differs in the eighth digit, and so does the compliance.
            check(not errors and compliance and abs(compliance[0] / first - 1.0) < 1e-6,
                  "{0}: the first compliance is the hand-written deck's ({1!r} vs {2!r}){3}".format(
                      label, compliance[0] if compliance else None, first, "" if not errors else " - " + "; ".join(errors)))
            converged = values(component.Params.Output[8 if multi else 6])
            # SIMP stops on top3d's test, no density moving by 0.01, which a run often never meets: then at Iterations, 200.
            ended = (converged and converged[0]) or (label == "SIMP" and len(compliance) == 200)
            check(ended and abs(compliance[-1] / last - 1.0) < 0.03,
                  "{0}: {1} after {2} iterations at {3:.5g}, in {4:.0f} s (the deck's run: {5} at {6:.5g})".format(
                      label, "converged" if converged and converged[0] else "stopped", len(compliance), compliance[-1], took[label], iterations, last))
            found[label] = (len(compliance), compliance[-1], took[label])
            if multi:
                volume = values(component.Params.Output[7])
                n = len(compliance)
                check(volume[n - 1] <= shares[0] + 1e-3 and volume[2 * n - 1] <= shares[1] + 1e-3,
                      "Multi-Material: steel {0:.4f} of {1}, the other {2:.4f} of {3}".format(volume[n - 1], shares[0], volume[2 * n - 1], shares[1]))
        with open(os.path.join(WORK, case + "_compliance.csv"), "w") as f:
            for label, (n, c, s) in found.items():
                f.write("{0},{1},{2!r},{3:.0f}\n".format(label, n, c, s))

        # The three designs side by side, as the definition draws them, each named above it. Before any analysis: an
        # analysed design's Legend draws in Rhino's viewport, over any picture after.
        if PICTURES:
            rdoc = rhino_doc()
            for o in list(rdoc.Objects):
                rdoc.Objects.Delete(o, True)
            blue = System.Drawing.Color.FromArgb(66, 136, 247)
            for label, dx in example.SHOWN:
                c = runs[label]
                multi = label == "Multi-Material"
                meshes = values(c.Params.Output[0])
                colours = values(c.Params.Output[1]) if multi else [blue] * len(meshes)
                baked = 0
                for mesh, colour in zip(meshes, colours):
                    mesh = mesh.DuplicateMesh()
                    mesh.Transform(Transform.Translation(dx, 0.0, 0.0))
                    # Rhino refuses a mesh that is not valid: degenerate faces go first.
                    if not mesh.IsValid:
                        mesh.Faces.CullDegenerateFaces()
                        mesh.Compact()
                    # Creased at 35 degrees, as the docs' topology pictures: the faces stay flat, the edges sharp.
                    mesh.Unweld(math.radians(35.0), True)
                    mesh.Normals.ComputeNormals()
                    if rdoc.Objects.AddMesh(mesh, attributes(colour), None, False, False) != System.Guid.Empty:
                        baked += 1
                check(baked == len(meshes) and baked > 0, "{0}: its {1} surface mesh(es) drawn".format(label, len(meshes)))
                # Under the design, in front: above it, the name ran into the post or the column.
                plane = Plane(Point3d(dx + 0.5, -0.2, -0.5), Vector3d.XAxis, Vector3d.ZAxis)
                text = Rhino.Geometry.TextEntity.Create(label, plane, rdoc.DimStyles.Current, False, 0.0, 0.0)
                # The document's style scales text for model space (0.15 in Rhino's default metres template): undo it.
                text.TextHeight = 0.16 / (text.DimensionScale if text.DimensionScale > 0 else 1.0)
                text.Justification = Rhino.Geometry.TextJustification.MiddleCenter
                rdoc.Objects.AddText(text, attributes(System.Drawing.Color.FromArgb(42, 45, 49)))
            rdoc.Views.Redraw()
            capture(rdoc, os.path.join(WORK, case + "-designs.png"), 4800, Vector3d(0.35, -1.2, 0.55))
            for o in list(rdoc.Objects):
                rdoc.Objects.Delete(o, True)

        # Each design analysed, as group 8 does: its Run Analysis and Legend enabled, then locked again.
        for label in labels:
            if label not in found:
                continue
            run = [o for o in objects if o.GetType().Name == "RunAnalysis" and o.NickName == "Analyse " + label][0]
            results = [o for o in objects if o.GetType().Name == "ModelView" and o.NickName == "Stress " + label][0]
            legend = [o for o in objects if o.GetType().Name == "Legend" and o.NickName == "Legend " + label][0]
            for o in (run, legend):
                o.Locked = False
                o.ExpireSolution(False)
            doc.NewSolution(False, GH_SolutionMode.Silent)
            errors = [m for o in (run, results) for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Error)]
            output = dict((p.Name, p) for p in results.Params.Output)
            info = [str(i) for i in values(output["Info"])]
            shown = values(output["Values"])
            check(not errors and info and shown, "{0}: its design analysed - {1}{2}".format(
                label, "; ".join(info)[:120] if info else "nothing", "" if not errors else " - " + "; ".join(errors)))
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
