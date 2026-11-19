#! python3
# Builds the two gmsh examples - Tools/Examples/Meshing/PlateWithHoles.py and Tools/Examples/Solids/Femur.py - by
# their own build(doc) inside a running Rhino 8 with this build of the plugin, solves them, and checks what comes
# out: the plate's largest stress against Heywood's formula, the femur's tetrahedra, analysis and modes.
# example.sh fills in the placeholders and starts this with rhinocode; it writes what it found to result.txt.
import os
import sys
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
PICTURES = "__PICTURES__" == "1"
EDGES = System.Drawing.Color.FromArgb(160, 168, 178)     # light grey element edges, opaque

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


def errors_of(objects):
    found = []
    for o in objects:
        if hasattr(o, "RuntimeMessages"):
            found += ["{0}: {1}".format(o.NickName, m) for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Error)]
    return found


def remarks_of(o):
    return [str(m) for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Remark)] + \
           [str(m) for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Warning)]


def solve(module, name, prepare=None):
    import importlib
    example = importlib.import_module(module)
    example = importlib.reload(example)
    example.make_object = make_object
    doc = GH_Document()
    doc.FilePath = os.path.join(WORK, name + ".gh")
    example.build(doc)
    if PICTURES:
        # A Legend draws in Rhino's viewport for good once it has solved, over every picture after.
        for o in doc.Objects:
            o = getattr(o, "__implementation__", o)
            if o.GetType().Name == "Legend":
                o.Locked = True
        if prepare is not None:
            prepare([getattr(o, "__implementation__", o) for o in doc.Objects])
    doc.Enabled = True
    doc.NewSolution(True, GH_SolutionMode.Silent)
    return doc, [getattr(o, "__implementation__", o) for o in doc.Objects]


def by_type(objects, type_name):
    return [o for o in objects if o.GetType().Name == type_name]


def rhino_doc():
    """A document with a window to capture: the active one, or an empty one opened from the work folder."""
    rdoc = Rhino.RhinoDoc.ActiveDoc
    view = rdoc.Views.ActiveView if rdoc is not None else None
    if view is None or view.ActiveViewport.Size.Width == 0:
        path = os.path.join(WORK, "pictures.3dm")
        empty = Rhino.FileIO.File3dm()
        empty.Settings.ModelUnitSystem = Rhino.UnitSystem.Meters
        empty.Write(path, 8)
        rdoc = Rhino.RhinoDoc.Open(path)[0]
    return rdoc


def attributes(colour):
    a = Rhino.DocObjects.ObjectAttributes()
    a.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject
    a.ObjectColor = colour
    return a


def bake_view(rdoc, view, edges_of=None):
    """What a Model View draws - its coloured meshes - and the element edges in light grey: of the mesh
    edges_of when given - one mesh: its edges; several, a solid's tetrahedra: the edges of the faces only one
    of them has, the solid's outside - else of the drawn meshes, the same way."""
    output = dict((p.Name, p) for p in view.Params.Output)
    meshes = [getattr(g, "Value", None) for g in output["Geometry"].VolatileData.AllData(True)]
    meshes = [m for m in meshes if isinstance(m, Rhino.Geometry.Mesh)]
    for mesh in meshes:
        rdoc.Objects.AddMesh(mesh)
    if edges_of is not None and not isinstance(edges_of, list):
        edges = edges_of.TopologyEdges
        for i in range(edges.Count):
            line = edges.EdgeLine(i)
            line.Transform(Rhino.Geometry.Transform.Translation(0, 0, 0.002))
            rdoc.Objects.AddLine(line, attributes(EDGES))
        return
    def key(p):
        return (round(p.X, 6), round(p.Y, 6), round(p.Z, 6))
    faces = {}
    for mesh in (edges_of if edges_of is not None else meshes):
        v = mesh.Vertices.ToPoint3dArray()
        for f in mesh.Faces:
            corners = [v[f.A], v[f.B], v[f.C]] + ([v[f.D]] if f.IsQuad else [])
            k = tuple(sorted(key(c) for c in corners))
            faces[k] = None if k in faces else corners
    lines = set()
    for corners in faces.values():
        if corners is None:
            continue
        for i in range(len(corners)):
            a, b = key(corners[i]), key(corners[(i + 1) % len(corners)])
            lines.add((min(a, b), max(a, b)))
    for a, b in lines:
        rdoc.Objects.AddLine(Rhino.Geometry.Line(Rhino.Geometry.Point3d(*a), Rhino.Geometry.Point3d(*b)), attributes(EDGES))


def capture(rdoc, path, width, looking=None):
    """The active view, maximised, fitted to everything, on a light grey: front and parallel, or in perspective
    from looking (a direction from the middle)."""
    from Rhino.Geometry import BoundingBox
    view = rdoc.Views.ActiveView
    view.Maximized = True
    vp = view.ActiveViewport
    box = BoundingBox.Empty
    for o in rdoc.Objects:
        box.Union(o.Geometry.GetBoundingBox(True))
    if looking is None:
        vp.SetProjection(Rhino.Display.DefinedViewportProjection.Front, "Front", True)
    else:
        vp.ChangeToPerspectiveProjection(True, 50.0)
        vp.SetCameraLocations(box.Center, box.Center + looking * box.Diagonal.Length * 2.0)
    box.Inflate(box.Diagonal.Length * 0.02)
    vp.ZoomBoundingBox(box)
    height = int(round(width * vp.Size.Height / float(vp.Size.Width)))
    modes = Rhino.Display.DisplayModeDescription
    shaded = modes.FindByName("Shaded")
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
        view.CaptureToBitmap(System.Drawing.Size(width, height), mode).Dispose()
        view.Redraw()
        Rhino.RhinoApp.Wait()
        view.CaptureToBitmap(System.Drawing.Size(width, height), mode).Save(path)
    finally:
        vp.DisplayMode = shaded
        modes.DeleteDisplayMode(mode.Id)
    say("  drew " + path)


def clear(rdoc):
    for o in list(rdoc.Objects):
        rdoc.Objects.Delete(o, True)


try:
    Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    # Grasshopper loads its libraries when its component server is first used: before this build goes in.
    Grasshopper.Instances.ComponentServer.ObjectProxies.Count

    context = AssemblyLoadContext("GmshExamples " + os.path.basename(WORK), False)
    from_rhino = ("RhinoCommon", "Grasshopper", "GH_IO", "Eto")
    loaded = []
    # The DLLs before the .gha: a .gha loaded before its Core binds to the installed one.
    names = sorted(os.listdir(WORK), key=lambda n: (n.endswith(".gha"), n != "Alpaca4d.Core.dll", n))
    for name in names:
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

    for folder in ("Meshing", "Solids", "Structures"):
        sys.path.insert(0, os.path.join(EXAMPLES, folder))

    # ---------------------------------------------------------------- the slab
    say("")
    say("Slab with openings")
    def flat_slab(objects):
        # Undeflected for the picture, so that Mesh Surface's own quads can be drawn over the colours.
        for o in objects:
            if o.GetType().Name == "ModelView" and o.NickName == "Deflection":
                o.GetType().GetField("_ckDeformed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o).Active = False
    doc, objects = solve("SlabWithOpenings", "SlabWithOpenings", flat_slab)
    errors = errors_of(objects)
    check(not errors, "no errors" + ("" if not errors else ": " + " | ".join(errors)))
    mesher = by_type(objects, "MeshSurface")[0]
    mesh = values(mesher.Params.Output[0])[0]
    quads = sum(1 for f in mesh.Faces if f.IsQuad)
    say("  Mesh Surface: " + " | ".join(remarks_of(mesher)))
    check(quads > 0.9 * mesh.Faces.Count, "{0} of its {1} elements are quads".format(quads, mesh.Faces.Count))
    vertices = mesh.Vertices.ToPoint3dArray()
    columns = [Rhino.Geometry.Point3d(i * 4.0, j * 4.0, 0) for i in range(4) for j in range(3)]
    check(all(any(v.DistanceTo(c) < 1e-9 for v in vertices) for c in columns), "a node exactly on each of the 12 columns")
    panels = dict((o.NickName, o) for o in objects if o.GetType().Name == "GH_Panel")
    def panel_value(name):
        return float(str(list(panels[name].VolatileData.AllData(True))[0]))
    carried, expected = panel_value("The columns carry [kN]"), panel_value("Area times load [kN]")
    check(abs(expected - 923.3) < 0.1, "the slab's area times the load: {0:.2f} kN".format(expected))
    check(abs(carried - expected) < 1e-3 * expected,
          "the columns carry {0:.2f} kN of it ({1:+.3f}%)".format(carried, 100.0 * (carried / expected - 1.0)))
    for o in by_type(objects, "ModelView"):
        output = dict((p.Name, p) for p in o.Params.Output)
        info = [str(i) for i in values(output["Info"])] if "Info" in output else []
        say("  {0}: {1}".format(o.NickName, "; ".join(info)[:160]))
    if PICTURES:
        rdoc = rhino_doc()
        clear(rdoc)
        bake_view(rdoc, [o for o in by_type(objects, "ModelView") if o.NickName == "Deflection"][0], mesh)
        for c in columns:
            column = Rhino.Geometry.Box(Rhino.Geometry.Plane(c, Rhino.Geometry.Vector3d.ZAxis),
                                        Rhino.Geometry.Interval(-0.15, 0.15), Rhino.Geometry.Interval(-0.15, 0.15),
                                        Rhino.Geometry.Interval(-3.0, -0.125))
            rdoc.Objects.AddBrep(column.ToBrep(), attributes(System.Drawing.Color.FromArgb(200, 204, 210)))
        capture(rdoc, os.path.join(WORK, "slab-with-openings.png"), 2400, Rhino.Geometry.Vector3d(-0.45, -0.8, 1.3))
        clear(rdoc)
    doc.RemoveObjects(list(doc.Objects), False)

    # ---------------------------------------------------------------- the femur
    say("")
    say("Femur")
    def undeformed(objects):
        # Undeformed for the picture, so that the tetrahedra's own edges can be drawn over the colours.
        for o in objects:
            if o.GetType().Name == "ModelView":
                o.GetType().GetField("_ckDeformed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o).Active = False
    doc, objects = solve("Femur", "Femur", undeformed)
    errors = errors_of(objects)
    check(not errors, "no errors" + ("" if not errors else ": " + " | ".join(errors)))
    mesher = by_type(objects, "MeshSolid")[0]
    tets = list(mesher.Params.Output[0].VolatileData.AllData(True))
    say("  Mesh Solid: " + " | ".join(remarks_of(mesher)))
    check(len(tets) > 3000, "{0} tetrahedra".format(len(tets)))
    views = [o for o in by_type(objects, "ModelView") if o.NickName == "Model View" or o.NickName == ""]
    for o in by_type(objects, "ModelView"):
        output = dict((p.Name, p) for p in o.Params.Output)
        shown = [float(v) for v in values(output["Values"])] if "Values" in output else []
        info = [str(i) for i in values(output["Info"])] if "Info" in output else []
        say("  {0}: values {1} - {2}".format(o.NickName, ", ".join("{0:.3g}".format(v) for v in shown[:2] + shown[-1:]),
                                           "; ".join(info)[:160]))
    if PICTURES:
        rdoc = rhino_doc()
        clear(rdoc)
        shown = [o for o in by_type(objects, "ModelView") if o.NickName != "Mode shape" and
                 any("Displacement" in str(i) for i in values(dict((p.Name, p) for p in o.Params.Output)["Info"]))][0]
        bake_view(rdoc, shown, [g.Value for g in mesher.Params.Output[0].VolatileData.AllData(True)])
        capture(rdoc, os.path.join(WORK, "femur.png"), 4800)
        clear(rdoc)
    vibration = by_type(objects, "NaturalVibration")
    if vibration:
        frequencies = [str(v) for v in values(vibration[0].Params.Output[3])]
        say("  frequencies: " + " | ".join(frequencies)[:300])
    doc.RemoveObjects(list(doc.Objects), False)
except Exception:
    say("  [FAIL] the script ran: " + traceback.format_exc())
    fails[0] += 1

lines.append("")
lines.append("all example checks passed" if fails[0] == 0 else "{0} example check(s) failed".format(fails[0]))
with open(os.path.join(WORK, "result.txt"), "w") as f:
    f.write("\n".join(lines) + "\n")
