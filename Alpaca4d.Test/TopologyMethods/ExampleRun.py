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
PICTURES = "__PICTURES__" == "1"

lines = []
fails = [0]


def check(ok, what):
    if not ok:
        fails[0] += 1
    lines.append("  [{0}] {1}".format("PASS" if ok else "FAIL", what))


def values(param):
    return [g.Value for g in param.VolatileData.AllData(True)]


def picture(meshes, path, width=4800):
    """The design in 3D, as the topology pictures of the docs have it: each surface shaded in its colour, creased at
    35 degrees, the block it was carved from in light grey lines, seen in perspective from in front, to the right and
    above, on a light grey (251). Rhino's own document, emptied before and after. The capture has the maximised
    viewport's shape: trim it to the drawn pixels after (Tools/Marketing/post.py trim)."""
    import math
    from Rhino.Geometry import BoundingBox, Vector3d
    rdoc = Rhino.RhinoDoc.ActiveDoc
    for o in list(rdoc.Objects):
        rdoc.Objects.Delete(o, True)

    def attributes(colour):
        a = Rhino.DocObjects.ObjectAttributes()
        a.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject
        a.ObjectColor = colour
        return a

    box = BoundingBox.Empty
    for mesh, colour in meshes:
        mesh = mesh.DuplicateMesh()
        if not mesh.IsValid:
            mesh.Faces.CullDegenerateFaces()
            mesh.Compact()
        mesh.Unweld(math.radians(35.0), True)
        mesh.Normals.ComputeNormals()
        rdoc.Objects.AddMesh(mesh, attributes(colour), None, False, False)
        box.Union(mesh.GetBoundingBox(True))
    for edge in box.GetEdges():
        rdoc.Objects.AddLine(edge, attributes(System.Drawing.Color.FromArgb(185, 185, 185)))

    view = rdoc.Views.ActiveView
    view.Maximized = True
    vp = view.ActiveViewport
    vp.ChangeToPerspectiveProjection(True, 50.0)
    target = box.Center
    # Rhino lights a shaded view from the camera, so a face seen at a slant comes out dark: from lower down, the
    # deck's top was nearly navy. A display mode's AmbientColor changed nothing (2026-11-17).
    vp.SetCameraLocations(target, target + Vector3d(0.4, -1.1, 0.95) * box.Diagonal.Length * 2.0)
    framed = BoundingBox(box.Min, box.Max)
    framed.Inflate(box.Diagonal.Length * 0.02)
    vp.ZoomBoundingBox(framed)
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
        height = int(round(width * vp.Size.Height / float(vp.Size.Width)))
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
        for o in list(rdoc.Objects):
            rdoc.Objects.Delete(o, True)
    lines.append("  drew the design in 3D to " + os.path.basename(path))


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

    def run(module, kind):
        """Builds the example, runs its optimisation to its end, and returns the document and the component."""
        example = __import__(module)
        example.make_object = make_object
        doc = GH_Document()
        doc.FilePath = os.path.join(WORK, module + ".gh")
        example.build(doc)
        objects = [getattr(o, "__implementation__", o) for o in doc.Objects]
        # kind is the method: Topology Optimisation's unit (BesoTopology, MultiMaterialTopology). The retired
        # BESO and Multi-Material Topology components are subclasses with their method fixed and Obsolete on them.
        component = [o for o in objects if o.GetType().Name == kind + "_obsolete" or (
            o.GetType().Name == "TopologyOptimisation" and o.ActiveUnit is not None and o.ActiveUnit.Name == kind)][0]
        check(not component.Obsolete, "{0}: made with Topology Optimisation, not the retired {1} ({2})".format(
            module, component.Name, component.GetType().Name))
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
        """Enables Run Analysis and Legend, as the note says, and reads the Model View of the analysed design (View
        Results is part of Model View now)."""
        for o in objects:
            if o.GetType().Name in ("RunAnalysis", "Legend"):
                o.Locked = False
                o.ExpireSolution(False)
        doc.NewSolution(False, GH_SolutionMode.Silent)
        analysis = [o for o in objects if o.GetType().Name == "RunAnalysis"][0]
        results = [o for o in objects if o.GetType().Name == "ModelView" and any(
            s.Attributes.GetTopLevel.DocObject.InstanceGuid == analysis.InstanceGuid for s in o.Params.Input[0].Sources)][0]
        errors = [m for o in (analysis, results) for m in o.RuntimeMessages(GH_RuntimeMessageLevel.Error)]
        check(not errors, "{0}: the design analysed, no errors{1}".format(module, "" if not errors else " - " + "; ".join(errors)))
        output = dict((p.Name, p) for p in results.Params.Output)
        drawn = values(output["Geometry"])
        info = [str(i) for i in values(output["Info"])]
        legend = values(output["Values"])
        check(len(drawn) > 0 and len(legend) > 0, "{0}: Model View shows the results ({1})".format(module, "; ".join(info)[:120]))

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
    if PICTURES:
        picture(list(zip(surfaces, values(mm.Params.Output[1]))), os.path.join(WORK, "multi-material-bridge-3d.png"))
    # Analysed after the BESO picture: an analysed design's Legend draws in Rhino's viewport, over any picture after.
    mm_doc, mm_objects = doc, objects

    # BESO: from the whole block down to a fifth.
    doc, objects, beso = run("BesoBridge", "BesoTopology")
    compliance = values(beso.Params.Output[4])
    volume = values(beso.Params.Output[5])
    check(volume[0] == 1.0 and abs(volume[-1] - 0.2) < 0.002 and values(beso.Params.Output[6])[0],
          "BesoBridge: converged after {0} iterations, the volume from 1 to {1:.4f}".format(len(volume), volume[-1]))
    density = values(beso.Params.Output[2])
    check(all(d == 1.0 or abs(d - 1e-3) < 1e-12 for d in density), "BesoBridge: every brick solid or void")
    lines.append("  " + str(values(beso.Params.Output[7])[-1]))
    if PICTURES:
        blue = System.Drawing.Color.FromArgb(66, 136, 247)
        picture([(m, blue) for m in values(beso.Params.Output[0])], os.path.join(WORK, "beso-bridge-3d.png"))
    analyse("MultiMaterialBridge", mm_doc, mm_objects)
    analyse("BesoBridge", doc, objects)
except Exception:
    lines.append("  [FAIL] the script ran: " + traceback.format_exc())
    fails[0] += 1

lines.append("")
lines.append("all example checks passed" if fails[0] == 0 else "{0} example check(s) failed".format(fails[0]))
with open(os.path.join(WORK, "result.txt"), "w") as f:
    f.write("\n".join(lines) + "\n")
