#! python3
# Builds Tools/Examples/Topology/TopologyBridge.py's definition inside a running Rhino 8 with this build of the plugin,
# runs its three optimisations - SIMP, BESO and Multi-Material - together to their ends, analyses each design as the
# definition does, and checks what comes out. example.sh fills in the placeholders and starts this with rhinocode; it
# writes what it found to result.txt. Loaded as ../TopologyOptimisation/ExampleRun.py loads the plugin.
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
    lines.append(text)
    with open(os.path.join(WORK, "progress.txt"), "w") as f:
        f.write("\n".join(lines) + "\n")


def check(ok, what):
    if not ok:
        fails[0] += 1
    say("  [{0}] {1}".format("PASS" if ok else "FAIL", what))


def values(param):
    return [g.Value for g in param.VolatileData.AllData(True)]


def capture(rdoc, path, width, looking=None):
    """The active view, maximised, fitted to everything, on a light grey (251): front and parallel, or in perspective
    with the camera at looking (a direction from the middle) when given. The capture has the viewport's own shape:
    trim it to the drawn pixels after (Tools/Marketing/post.py trim)."""
    view = rdoc.Views.ActiveView
    view.Maximized = True
    vp = view.ActiveViewport
    box = BoundingBox.Empty
    for o in rdoc.Objects:
        box.Union(o.Geometry.GetBoundingBox(True))
    saved = Rhino.DocObjects.ViewportInfo(vp) if looking is not None else None
    if looking is None:
        vp.SetProjection(Rhino.Display.DefinedViewportProjection.Front, "Front", True)
    else:
        # Rhino lights a shaded view from the camera, so a face seen at a slant comes out dark: from lower down, the
        # deck's top was nearly navy.
        vp.ChangeToPerspectiveProjection(True, 50.0)
        vp.SetCameraLocations(box.Center, box.Center + looking * box.Diagonal.Length * 2.0)
    box.Inflate(box.Diagonal.Length * 0.02)
    vp.ZoomBoundingBox(box)
    height = int(round(width * vp.Size.Height / float(vp.Size.Width)))
    modes = Rhino.Display.DisplayModeDescription
    shaded = modes.FindByName("Shaded")
    # Rhino keeps display modes in its settings: clear any a run that stopped left behind.
    for old in modes.GetDisplayModes():
        if old.EnglishName == "Example grey":
            modes.DeleteDisplayMode(old.Id)
    mode = modes.GetDisplayMode(modes.CopyDisplayMode(shaded.Id, "Example grey"))
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
        if saved is not None:
            vp.SetViewProjection(saved, True)
    say("  drew " + os.path.basename(path))


def attributes(colour):
    a = Rhino.DocObjects.ObjectAttributes()
    a.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject
    a.ObjectColor = colour
    return a


def bake(rdoc, mesh, colour, move):
    """A design's surface in Rhino's document, as the docs' topology pictures have it: creased at 35 degrees, so the
    faces stay flat and the edges sharp. Rhino refuses a mesh that is not valid, so degenerate faces go first."""
    mesh = mesh.DuplicateMesh()
    mesh.Transform(move)
    if not mesh.IsValid:
        mesh.Faces.CullDegenerateFaces()
        mesh.Compact()
    mesh.Unweld(math.radians(35.0), True)
    mesh.Normals.ComputeNormals()
    return rdoc.Objects.AddMesh(mesh, attributes(colour), None, False, False) != System.Guid.Empty


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


def label(rdoc, text, at, height, justification=Rhino.Geometry.TextJustification.MiddleCenter):
    """A name, upright and facing -y, at at."""
    plane = Plane(at, Vector3d.XAxis, Vector3d.ZAxis)
    entity = Rhino.Geometry.TextEntity.Create(text, plane, rdoc.DimStyles.Current, False, 0.0, 0.0)
    # The document's style scales text for model space (0.15 in Rhino's default metres template): undo it.
    entity.TextHeight = height / (entity.DimensionScale if entity.DimensionScale > 0 else 1.0)
    entity.Justification = justification
    rdoc.Objects.AddText(entity, attributes(System.Drawing.Color.FromArgb(42, 45, 49)))


try:
    Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    # Grasshopper loads its libraries when its component server is first used. Use it now, before this build goes
    # in: otherwise an installed Alpaca4d binds to this build's Core, with this build's Newtonsoft.Json, and its
    # Section Library fails ("Grasshopper Loading Errors", Method not found ... FindEntry).
    Grasshopper.Instances.ComponentServer.ObjectProxies.Count

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
    import importlib
    import TopologyBridge as example
    example = importlib.reload(example)
    example.make_object = make_object

    doc = GH_Document()
    doc.FilePath = os.path.join(WORK, "TopologyBridge.gh")
    example.build(doc)
    objects = [getattr(o, "__implementation__", o) for o in doc.Objects]
    check(len(objects) == 102, "the definition has its 102 objects ({0})".format(len(objects)))
    runs = dict((o.NickName, o) for o in objects if o.GetType().Name == "TopologyOptimisation")
    check(sorted(runs) == ["BESO", "Multi-Material", "SIMP"] and all(not c.Obsolete for c in runs.values()),
          "three Topology Optimisation components, SIMP, BESO and Multi-Material ({0})".format(", ".join(sorted(runs))))
    methods = dict((n, c.ActiveUnit.Name if c.ActiveUnit is not None else "TopologyOptimisation") for n, c in runs.items())
    check(methods == {"SIMP": "TopologyOptimisation", "BESO": "BesoTopology", "Multi-Material": "MultiMaterialTopology"},
          "each on its method ({0})".format(methods))

    doc.Enabled = True
    doc.NewSolution(True, GH_SolutionMode.Silent)
    for name, c in runs.items():
        errors = list(c.RuntimeMessages(GH_RuntimeMessageLevel.Error))
        warnings = list(c.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
        check(not errors and any("Set Run to true" in w for w in warnings),
              "{0}, before Run: waits for it ({1})".format(name, "; ".join(errors + warnings)))
    assemble = [o for o in objects if o.GetType().Name == "AssembleModel"][0]
    check(values(assemble.Params.Output[0])[0].Bricks.Count == 3456, "3 456 bricks at 3 bricks per metre")

    # All three at once, as a user setting the three Runs to true would.
    for t in [o for o in objects if o.GetType().Name == "GH_BooleanToggle" and o.NickName.startswith("Run ")]:
        t.Value = True
        t.ExpireSolution(False)
    doc.NewSolution(False, GH_SolutionMode.Silent)
    prop = runs["SIMP"].GetType().GetProperty("Run", BindingFlags.NonPublic | BindingFlags.Instance)
    start = time.time()
    took = {}
    while time.time() - start < 1800 and len(took) < 3:
        for name, c in runs.items():
            r = prop.GetValue(c)
            if name not in took and r is not None and not r.GetType().GetProperty("IsRunning").GetValue(r):
                took[name] = time.time() - start
        time.sleep(0.5)
    say("  the runs took " + ", ".join("{0} {1:.0f} s".format(n, s) for n, s in sorted(took.items(), key=lambda x: x[1])))
    for c in runs.values():
        c.ExpireSolution(False)
    doc.NewSolution(False, GH_SolutionMode.Silent)
    for name, c in runs.items():
        errors = list(c.RuntimeMessages(GH_RuntimeMessageLevel.Error))
        check(not errors, "{0}: no errors{1}".format(name, "" if not errors else " - " + "; ".join(errors)))

    # SIMP: a fifth of the block below the deck.
    simp = runs["SIMP"]
    compliance = values(simp.Params.Output[4])
    density = values(simp.Params.Output[2])
    check(values(simp.Params.Output[6])[0] and compliance[-1] < 0.5 * compliance[0],
          "SIMP: converged after {0} iterations, the compliance from {1:.4g} to {2:.4g}".format(len(compliance), compliance[0], compliance[-1]))
    say("  " + str(values(simp.Params.Output[7])[-1]))
    found = {"SIMP": (len(compliance), compliance[-1])}

    # BESO: from the whole block down to a fifth.
    beso = runs["BESO"]
    compliance = values(beso.Params.Output[4])
    volume = values(beso.Params.Output[5])
    check(volume[0] == 1.0 and abs(volume[-1] - 0.2) < 0.002 and values(beso.Params.Output[6])[0],
          "BESO: converged after {0} iterations, the volume from 1 to {1:.4f}".format(len(volume), volume[-1]))
    density = values(beso.Params.Output[2])
    check(all(d == 1.0 or abs(d - 1e-3) < 1e-12 for d in density), "BESO: every brick solid or void")
    say("  " + str(values(beso.Params.Output[7])[-1]))
    found["BESO"] = (len(compliance), compliance[-1])

    # Multi-material: steel and concrete, at most 5% and 15%.
    mm = runs["Multi-Material"]
    compliance = values(mm.Params.Output[6])
    volume = [values(mm.Params.Output[7])[i * len(compliance):(i + 1) * len(compliance)] for i in range(2)]
    choice = values(mm.Params.Output[3])
    surfaces = values(mm.Params.Output[0])
    check(len(compliance) > 250 and compliance[-1] < 0.5 * compliance[0],
          "Multi-Material: {0} iterations, the compliance from {1:.4g} to {2:.4g}".format(len(compliance), compliance[0], compliance[-1]))
    check(volume[0][-1] <= 0.051 and volume[1][-1] <= 0.151 and volume[0][-1] > 0.04 and volume[1][-1] > 0.14,
          "Multi-Material: steel {0:.4f} of 0.05, concrete {1:.4f} of 0.15".format(volume[0][-1], volume[1][-1]))
    check(choice.count(0) > 0 and choice.count(1) > 0,
          "Multi-Material: {0} steel and {1} concrete bricks, deck included".format(choice.count(0), choice.count(1)))
    check(len(surfaces) == 2 and all(s.Faces.Count > 0 for s in surfaces), "Multi-Material: a surface for each")
    say("  " + str(values(mm.Params.Output[9])[-1]))
    found["Multi-Material"] = (len(compliance), compliance[-1])
    with open(os.path.join(WORK, "bridge_compliance.csv"), "w") as f:
        for name, (n, c) in found.items():
            f.write("{0},{1},{2!r}\n".format(name, n, c))

    # The designs one above the other, as the definition draws them, each in its box and named.
    if PICTURES:
        rdoc = rhino_doc()
        for o in list(rdoc.Objects):
            rdoc.Objects.Delete(o, True)
        blue = System.Drawing.Color.FromArgb(66, 136, 247)
        for name, up in example.SHOWN:
            c = runs[name]
            move = Transform.Translation(0.0, 0.0, up)
            meshes = values(c.Params.Output[0])
            colours = values(c.Params.Output[1]) if name == "Multi-Material" else [blue] * len(meshes)
            baked = sum(1 for m, colour in zip(meshes, colours) if bake(rdoc, m, colour, move))
            check(baked == len(meshes) and baked > 0, "{0}: its {1} surface mesh(es) drawn".format(name, len(meshes)))
            box = BoundingBox(Point3d(0, 0, up), Point3d(example.SPAN, example.WIDTH, up + example.HEIGHT))
            for edge in box.GetEdges():
                rdoc.Objects.AddLine(edge, attributes(System.Drawing.Color.FromArgb(185, 185, 185)))
            # To the left of its design, so that no name reads as the one under it.
            label(rdoc, name, Point3d(-0.8, 0.0, up + example.HEIGHT / 2.0), 0.8, Rhino.Geometry.TextJustification.MiddleRight)
        rdoc.Views.Redraw()
        # From in front, to the right and above: from higher up the bridges stand apart less, from lower down the
        # decks' tops come out navy.
        capture(rdoc, os.path.join(WORK, "bridge-designs-3d.png"), 4800, Vector3d(0.5, -1.2, 0.75))
        capture(rdoc, os.path.join(WORK, "bridge-designs-front.png"), 4800)
        for o in list(rdoc.Objects):
            rdoc.Objects.Delete(o, True)

    # Each design analysed, as the note says: its Run Analysis and Legend enabled. After the pictures: an analysed
    # design's Legend draws in Rhino's viewport, over any picture after.
    for name in ("SIMP", "BESO", "Multi-Material"):
        analysis = [o for o in objects if o.GetType().Name == "RunAnalysis" and o.NickName == "Analyse " + name][0]
        legend = [o for o in objects if o.GetType().Name == "Legend" and o.NickName == "Legend " + name][0]
        for o in (analysis, legend):
            o.Locked = False
            o.ExpireSolution(False)
        doc.NewSolution(False, GH_SolutionMode.Silent)
        results = [o for o in objects if o.GetType().Name == "ModelView" and o.NickName == "Stress " + name][0]
        errors = [m for o in (analysis, results) for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Error)]
        check(not errors, "{0}: the design analysed, no errors{1}".format(name, "" if not errors else " - " + "; ".join(errors)))
        output = dict((p.Name, p) for p in results.Params.Output)
        drawn = values(output["Geometry"])
        info = [str(i) for i in values(output["Info"])]
        shown = values(output["Values"])
        check(len(drawn) > 0 and len(shown) > 0, "{0}: Model View shows the results ({1})".format(name, "; ".join(info)[:120]))
        for o in (analysis, legend):
            o.Locked = True
    doc.RemoveObjects(list(doc.Objects), False)
except Exception:
    say("  [FAIL] the script ran: " + traceback.format_exc())
    fails[0] += 1

lines.append("")
lines.append("all example checks passed" if fails[0] == 0 else "{0} example check(s) failed".format(fails[0]))
with open(os.path.join(WORK, "result.txt"), "w") as f:
    f.write("\n".join(lines) + "\n")
