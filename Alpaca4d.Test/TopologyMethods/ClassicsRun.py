#! python3
# Builds Tools/Examples/Topology/TopologyClassics.py's two definitions inside a running Rhino 8 with this build of the
# plugin, runs every optimisation of each at once and checks them against ./run.sh bench classic's summaries; then
# draws the designs side by side in Rhino's viewport and writes each design's densities, for the examples' pictures.
# classics.sh fills in the placeholders and starts this with rhinocode; it writes what it found to result.txt.
# Loaded as NodesRun.py loads the plugin.
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
from Rhino.Geometry import Point3d, Vector3d, Plane, Transform, BoundingBox

WORK = "__WORK__"
EXAMPLES = "__EXAMPLES__"
OPENSEES = "__OPENSEES__"
PICTURES = "__PICTURES__" == "1"

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


def values(param):
    return [g.Value if hasattr(g, "Value") else g for g in param.VolatileData.AllData(True)]


def summary(case, n):
    """method -> (iterations, first, last, grey), as ./run.sh bench classic wrote them."""
    out = {}
    for line in open(os.path.join(WORK, "classic-{0}-{1}_summary.csv".format(case, n))):
        method, iterations, first, last, grey = line.strip().split(",")
        out[method] = (int(iterations), float(first), float(last), grey)
    return out


def capture(doc, path, width, looking=None):
    """The active view, maximised, fitted to everything, on a light grey (251): front and parallel, or in perspective
    with the camera at looking (a direction from the middle, Vector3d) when given. The capture has the viewport's own
    shape, width pixels wide: a capture of another shape widens the frame round the fitted box, so the designs come out
    small. Trim it to the drawn pixels after (Tools/Marketing/post.py trim)."""
    view = doc.Views.ActiveView
    view.Maximized = True
    vp = view.ActiveViewport
    box = BoundingBox.Empty
    for o in doc.Objects:
        box.Union(o.Geometry.GetBoundingBox(True))
    # A perspective camera far off left behind spoils the next front view's framing (the L-bracket's came out four
    # times too close, 2026-11-17): the view goes back to how it was after a 3D capture.
    saved = Rhino.DocObjects.ViewportInfo(vp) if looking is not None else None
    if looking is None:
        vp.SetProjection(Rhino.Display.DefinedViewportProjection.Front, "Front", True)
    else:
        # Rhino lights a shaded view from the camera, so a face seen at a slant comes out dark.
        vp.ChangeToPerspectiveProjection(True, 50.0)
        vp.SetCameraLocations(box.Center, box.Center + looking * box.Diagonal.Length * 2.0)
    box.Inflate(box.Diagonal.Length * 0.02)
    vp.ZoomBoundingBox(box)
    height = int(round(width * vp.Size.Height / float(vp.Size.Width)))
    say("  the viewport is {0}x{1}; captured at {2}x{3}".format(vp.Size.Width, vp.Size.Height, width, height))
    modes = Rhino.Display.DisplayModeDescription
    shaded = modes.FindByName("Shaded")
    # Rhino keeps display modes in its settings: clear any a run that stopped left behind.
    for old in modes.GetDisplayModes():
        if old.EnglishName == "Classics grey":
            modes.DeleteDisplayMode(old.Id)
    mode = modes.GetDisplayMode(modes.CopyDisplayMode(shaded.Id, "Classics grey"))
    try:
        mode.DisplayAttributes.FillMode = Rhino.Display.DisplayPipelineAttributes.FrameBufferFillMode.SolidColor
        mode.DisplayAttributes.SetFill(System.Drawing.Color.FromArgb(251, 251, 251))
        for attribute in ("ShowGrid", "ShowGridAxes", "ShowWorldAxes"):
            try:
                setattr(mode.DisplayAttributes, attribute, False)
            except Exception:
                pass
        try:
            mode.DisplayAttributes.MeshSpecificAttributes.ShowMeshWires = False
        except Exception:
            pass
        vp.ConstructionGridVisible = False
        vp.ConstructionAxesVisible = False
        vp.WorldAxesVisible = False
        Rhino.Display.DisplayModeDescription.UpdateDisplayMode(mode)
        vp.DisplayMode = mode
        view.Redraw()
        Rhino.RhinoApp.Wait()
        # The first capture can come before the mode takes hold.
        view.CaptureToBitmap(System.Drawing.Size(width, height), mode).Dispose()
        view.Redraw()
        Rhino.RhinoApp.Wait()
        bitmap = view.CaptureToBitmap(System.Drawing.Size(width, height), mode)
        bitmap.Save(path)
    finally:
        vp.DisplayMode = shaded
        Rhino.Display.DisplayModeDescription.DeleteDisplayMode(mode.Id)
        if saved is not None:
            vp.SetViewProjection(saved, True)


try:
    Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    # Grasshopper loads its libraries when its component server is first used. Use it now, before this build goes
    # in: otherwise an installed Alpaca4d binds to this build's Core, with this build's Newtonsoft.Json, and its
    # Section Library fails ("Grasshopper Loading Errors", Method not found ... FindEntry).
    Grasshopper.Instances.ComponentServer.ObjectProxies.Count
    context = AssemblyLoadContext("TopologyClassics " + os.path.basename(WORK), False)
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
    import TopologyClassics as example
    example = importlib.reload(example)
    example.make_object = make_object

    for case, n, count, bricks in (("cantilever", 40, 73, 3200), ("lbracket", 60, 53, 2304)):
        say("")
        say("The " + ("cantilever" if case == "cantilever" else "L-bracket"))
        expected = summary(case, n)
        example.CASE = case
        doc = GH_Document()
        doc.FilePath = os.path.join(WORK, "Topology" + case + ".gh")
        example.build(doc)
        objects = [getattr(o, "__implementation__", o) for o in doc.Objects]
        check(len(objects) == count, "the definition has its {0} objects ({1})".format(count, len(objects)))
        doc.Enabled = True
        doc.NewSolution(True, GH_SolutionMode.Silent)
        errors = [o.NickName + ": " + m for o in objects if hasattr(o, "RuntimeMessages") for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Error)]
        check(not errors, "it solves without errors{0}".format("" if not errors else ": " + "; ".join(errors)))
        assemble = [o for o in objects if o.GetType().Name == "AssembleModel"][0]
        model = values(assemble.Params.Output[0])[0]
        check(model.Bricks.Count == bricks, "{0} bricks ({1})".format(bricks, model.Bricks.Count))

        runs = [o for o in objects if o.GetType().Name == "TopologyOptimisation"]
        for t in [o for o in objects if o.GetType().Name == "GH_BooleanToggle" and o.NickName.startswith("Run ")]:
            t.Value = True
            t.ExpireSolution(False)
        doc.NewSolution(False, GH_SolutionMode.Silent)
        prop = runs[0].GetType().GetProperty("Run", BindingFlags.NonPublic | BindingFlags.Instance)
        start = time.time()
        while time.time() - start < 1800:
            states = [prop.GetValue(c) for c in runs]
            if all(r is not None and not r.GetType().GetProperty("IsRunning").GetValue(r) for r in states):
                break
            time.sleep(1.0)
        seconds = time.time() - start
        for c in runs:
            c.ExpireSolution(False)
        doc.NewSolution(False, GH_SolutionMode.Silent)
        say("  all {0} runs done in {1:.0f} s".format(len(runs), seconds))

        found = {}
        for c in runs:
            label = c.NickName
            multi = label == "Multi-Material"
            method = "multi" if multi else "beso" if label == "BESO" else "simp" + label.split("= ")[-1]
            errors = list(c.RuntimeMessages(GH_RuntimeMessageLevel.Error))
            compliance = values(c.Params.Output[6 if multi else 4])
            converged = values(c.Params.Output[8 if multi else 6])
            iterations, first, last, grey = expected[method]
            check(not errors and compliance and abs(compliance[0] / first - 1.0) < 1e-6,
                  "{0}: the first compliance is the hand-written deck's ({1!r} vs {2!r}){3}".format(
                      label, compliance[0] if compliance else None, first, "" if not errors else " - " + "; ".join(errors)))
            check(converged and converged[0] and abs(compliance[-1] / last - 1.0) < 0.03,
                  "{0}: converged after {1} iterations at {2:.4f} (the deck's run: {3} at {4:.4f})".format(
                      label, len(compliance), compliance[-1] if compliance else float("nan"), iterations, last))
            found[label] = compliance[-1] if compliance else float("nan")

            # The design for the pictures: each element's centre and density (a density per material for Multi-Material).
            elements = values(c.Params.Output[5 if multi else 3])
            density = values(c.Params.Output[4 if multi else 2])
            with open(os.path.join(WORK, "{0}-{1}_densities.csv".format(case, method)), "w") as f:
                f.write("x,z,density\n" if not multi else "x,z,stiff,soft\n")
                for i, e in enumerate(elements):
                    centre = e.Mesh.GetBoundingBox(True).Center
                    ds = [density[i]] if not multi else [density[i], density[len(elements) + i]]
                    f.write("{0!r},{1!r},{2}\n".format(centre.X, centre.Z, ",".join(repr(d) for d in ds)))
        with open(os.path.join(WORK, case + "_compliance.csv"), "w") as f:
            for label, c in found.items():
                f.write("{0},{1!r}\n".format(label, c))

        if PICTURES:
            # The designs side by side, in Rhino's own document: SIMP's (and BESO's) surface in light blue,
            # Multi-Material's in its materials' colours.
            rdoc = Rhino.RhinoDoc.ActiveDoc
            for o in list(rdoc.Objects):
                rdoc.Objects.Delete(o, True)
            w = 80.0 if case == "cantilever" else 60.0
            h = 40.0 if case == "cantilever" else 60.0
            if case == "cantilever":
                places = dict(("SIMP p = %d" % p, ((p - 1) * (w + 16.0), 0.0)) for p in range(1, 6))
                places["BESO"] = (1.0 * (w + 16.0) + (w + 16.0) / 2.0, -(h + 22.0))
                places["Multi-Material"] = (2.0 * (w + 16.0) + (w + 16.0) / 2.0, -(h + 22.0))
            else:
                places = {"SIMP p = 3": (0.0, 0.0), "BESO": (w + 16.0, 0.0), "Multi-Material": (2.0 * (w + 16.0), 0.0)}
            light_blue = System.Drawing.Color.FromArgb(66, 136, 247)
            style = rdoc.DimStyles.Current
            for c in runs:
                at = places[c.NickName]
                move = Transform.Translation(at[0], 0.0, at[1])
                multi = c.NickName == "Multi-Material"
                meshes = values(c.Params.Output[0])
                colours = values(c.Params.Output[1]) if multi else [light_blue] * len(meshes)
                baked, invalid = 0, 0
                for mesh, colour in zip(meshes, colours):
                    mesh = mesh.DuplicateMesh()
                    mesh.Transform(move)
                    # Rhino's document refuses a mesh that is not valid: a few degenerate faces of an iso-surface
                    # are enough, and three of the five SIMP designs went missing that way.
                    if not mesh.IsValid:
                        invalid += 1
                        mesh.Faces.CullDegenerateFaces()
                        mesh.Compact()
                    # Creased at 35 degrees, as the docs' topology pictures: the faces stay flat, the edges sharp.
                    mesh.Unweld(math.radians(35.0), True)
                    mesh.Normals.ComputeNormals()
                    attributes = Rhino.DocObjects.ObjectAttributes()
                    attributes.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject
                    attributes.ObjectColor = colour if isinstance(colour, System.Drawing.Color) else colour.Value
                    if rdoc.Objects.AddMesh(mesh, attributes, None, False, False) != System.Guid.Empty:
                        baked += 1
                check(baked == len(meshes) and baked > 0,
                      "{0}: its {1} surface mesh(es) drawn ({2} drawn, {3} not valid as made)".format(c.NickName, len(meshes), baked, invalid))
                label = c.NickName.replace("SIMP p = ", "SIMP, p = ") + "    C = {0:.1f}".format(found[c.NickName])
                plane = Plane(Point3d(at[0] + w / 2.0, -1.0, at[1] - 6.0), Vector3d.XAxis, Vector3d.ZAxis)
                text = Rhino.Geometry.TextEntity.Create(label, plane, style, False, 0.0, 0.0)
                # The document's style scales text for model space (0.15 in Rhino's default metres template): undo it.
                text.TextHeight = 3.6 / (text.DimensionScale if text.DimensionScale > 0 else 1.0)
                text.Justification = Rhino.Geometry.TextJustification.MiddleCenter
                attributes = Rhino.DocObjects.ObjectAttributes()
                attributes.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject
                attributes.ObjectColor = System.Drawing.Color.FromArgb(42, 45, 49)
                rdoc.Objects.AddText(text, attributes)
            rdoc.Views.Redraw()
            capture(rdoc, os.path.join(WORK, case + "-designs.png"), 4800)
            say("  drew the designs to " + case + "-designs.png")
            capture(rdoc, os.path.join(WORK, case + "-designs-3d.png"), 4800, Vector3d(0.45, -1.2, 0.6))
            say("  drew them in 3D to " + case + "-designs-3d.png")
            for o in list(rdoc.Objects):
                rdoc.Objects.Delete(o, True)
        doc.RemoveObjects(list(doc.Objects), False)
except Exception:
    lines.append("  [FAIL] the script ran: " + traceback.format_exc())
    fails[0] += 1

lines.append("")
lines.append("all classic checks passed" if fails[0] == 0 else "{0} classic check(s) failed".format(fails[0]))
with open(os.path.join(WORK, "result.txt"), "w") as f:
    f.write("\n".join(lines) + "\n")
