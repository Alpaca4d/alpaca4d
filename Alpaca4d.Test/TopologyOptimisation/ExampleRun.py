#! python3
# Builds Tools/Examples/Topology/TopologyBenchmarks.py's definition inside a running Rhino 8 with this build of the
# plugin, runs both benchmarks, each by SIMP, BESO and Multi-Material, and checks SIMP's against the test bench.
# With ITERATIONS above 0 every run stops there, and SIMP's compliance is compared with the test bench's history at
# that iteration; at 0 they run to their ends, SIMP's last compliance is compared with what the definition's panels
# expect, and PICTURES draws each problem's three designs. example.sh fills in the placeholders and starts this with
# rhinocode; it writes what it found to result.txt.
#
# The definition is made by the example's own build(doc), with its make_object pointed at this build's
# components, loaded into a load context of their own beside the installed Alpaca4d - as
# GrasshopperRun.cs loads them - and at Grasshopper's component server for Grasshopper's own.
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
from Grasshopper.Kernel import GH_Document, GH_SolutionMode, IGH_DocumentObject
from Rhino.Geometry import BoundingBox, Plane, Point3d, Transform, Vector3d

WORK = "__WORK__"
EXAMPLES = "__EXAMPLES__"
OPENSEES = "__OPENSEES__"
ITERATIONS = int("__ITERATIONS__")
PICTURES = "__PICTURES__" == "1"
# Each problem's compliance at that iteration, from ./run.sh bench; 0 to skip the comparison.
EXPECTED = {"Cantilever": float("__CANTILEVER__"), "Simply supported": float("__SIMPLE__")}
# What the definition's panels expect after 200 iterations.
AFTER_200 = {"Cantilever": 2415.86, "Simply supported": 1203.026}

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
        if old.EnglishName == "Benchmarks grey":
            modes.DeleteDisplayMode(old.Id)
    mode = modes.GetDisplayMode(modes.CopyDisplayMode(shaded.Id, "Benchmarks grey"))
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


try:
    Rhino.RhinoApp.GetPlugInObject("Grasshopper")
    # Grasshopper loads its libraries when its component server is first used. Use it now, before this build goes
    # in: otherwise an installed Alpaca4d binds to this build's Core, with this build's Newtonsoft.Json, and its
    # Section Library fails ("Grasshopper Loading Errors", Method not found ... FindEntry).
    Grasshopper.Instances.ComponentServer.ObjectProxies.Count

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
    import importlib
    import TopologyBenchmarks as example
    example = importlib.reload(example)
    example.make_object = make_object

    doc = GH_Document()
    doc.FilePath = os.path.join(WORK, "TopologyBenchmarks.gh")
    example.build(doc)
    objects = [getattr(o, "__implementation__", o) for o in doc.Objects]
    check(len(objects) == 101, "the definition has its 101 objects ({0})".format(len(objects)))

    runs = dict((o.NickName, o) for o in objects if o.GetType().Name == "TopologyOptimisation")
    toggles = dict((o.NickName, o) for o in objects if o.GetType().Name == "GH_BooleanToggle")
    problems = ["Cantilever", "Simply supported"]
    names = sorted(p + suffix for p in problems for suffix in ("", ", BESO", ", Multi-Material"))
    check(sorted(runs) == names, "six Topology Optimisation components, three a problem ({0})".format(", ".join(sorted(runs))))
    methods = dict((n, c.ActiveUnit.Name if c.ActiveUnit is not None else "TopologyOptimisation") for n, c in runs.items())
    check(all(methods[p] == "TopologyOptimisation" and methods[p + ", BESO"] == "BesoTopology"
              and methods[p + ", Multi-Material"] == "MultiMaterialTopology" for p in problems), "each on its method")
    topologies = dict((p, runs[p]) for p in problems)

    if ITERATIONS > 0:
        for name, c in runs.items():
            iterations = c.Params.Input[4 if name.endswith("Multi-Material") else 3]
            iterations.PersistentData.Clear()
            iterations.PersistentData.Append(Grasshopper.Kernel.Types.GH_Integer(ITERATIONS))

    doc.Enabled = True
    doc.NewSolution(True, GH_SolutionMode.Silent)

    def messages(component, level):
        return list(component.RuntimeMessages(level))

    for name, c in runs.items():
        errors = messages(c, Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)
        warnings = messages(c, Grasshopper.Kernel.GH_RuntimeMessageLevel.Warning)
        check(not errors and any("Set Run to true" in w for w in warnings),
              "{0}, before Run: waits for it ({1})".format(name, "; ".join(errors + warnings)))

    for name in problems:
        toggle = toggles["Run " + name]
        toggle.Value = True
        toggle.ExpireSolution(False)
    doc.NewSolution(False, GH_SolutionMode.Silent)

    # Run and its IsRunning are internal to the plugin: read them by reflection.
    run_property = topologies["Cantilever"].GetType().GetProperty("Run", BindingFlags.NonPublic | BindingFlags.Instance)

    def running(c):
        run = run_property.GetValue(c)
        return run is None or run.GetType().GetProperty("IsRunning").GetValue(run)

    start = time.time()
    took = {}
    while time.time() - start < 2400 and len(took) < len(runs):
        for name, c in runs.items():
            if name not in took and not running(c):
                took[name] = time.time() - start
        time.sleep(0.5)
    say("  the runs took " + ", ".join("{0} {1:.0f} s".format(n, s) for n, s in sorted(took.items(), key=lambda x: x[1])))

    for c in runs.values():
        c.ExpireSolution(False)
    doc.NewSolution(False, GH_SolutionMode.Silent)

    found = {}
    for name, c in runs.items():
        errors = messages(c, Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)
        check(not errors, "{0}: no errors{1}".format(name, "" if not errors else " - " + "; ".join(errors)))
        multi = name.endswith("Multi-Material")
        compliance = values(c.Params.Output[6 if multi else 4])
        surface = values(c.Params.Output[0])
        found[name] = (len(compliance), compliance[-1] if compliance else float("nan"))
        if ITERATIONS > 0:
            check(len(compliance) == ITERATIONS, "{0}: {1} iterations ({2})".format(name, ITERATIONS, len(compliance)))
        say("  " + str(values(c.Params.Output[9 if multi else 7])[-1]))
        if multi:
            volume = values(c.Params.Output[7])
            shares = [volume[i * len(compliance) + len(compliance) - 1] for i in range(2)]
            check(len(surface) == 2 and abs(sum(shares) - 0.3) < 2e-3,
                  "{0}: a surface for each material, {1:.3f} and {2:.3f} of the volume".format(name, shares[0], shares[1]))
            continue
        # At 30 iterations the surface is one closed mesh; a finished design can come out in more than one piece.
        if ITERATIONS > 0:
            check(len(surface) == 1 and surface[0].IsClosed, "{0}: the surface is one closed mesh".format(name))
        else:
            check(len(surface) > 0 and all(m.Faces.Count > 0 for m in surface), "{0}: a surface ({1} mesh(es), {2} closed)".format(
                name, len(surface), sum(1 for m in surface if m.IsClosed)))
        density = values(c.Params.Output[2])
        volume = sum(density) / max(1, len(density))
        if name.endswith("BESO"):
            check(all(d == 1.0 or d < 1e-2 for d in density), "{0}: every brick solid or void".format(name))
            if ITERATIONS == 0:
                check(abs(values(c.Params.Output[5])[-1] - 0.3) < 2e-3 and values(c.Params.Output[6])[0],
                      "{0}: converged at 30% of the volume".format(name))
            continue
        check(abs(volume - 0.3) < 2e-3, "{0}: 30% of the volume ({1:.4f})".format(name, volume))
        if ITERATIONS > 0:
            expected = EXPECTED[name]
            if expected > 0 and compliance:
                gap = abs(compliance[-1] / expected - 1.0)
                check(gap < 1e-6, "{0}: compliance {1!r} at iteration {2}, the test bench's {3!r} ({4:.1e} apart)".format(
                    name, compliance[-1], ITERATIONS, expected, gap))
        else:
            # The cantilever's runs part by up to 0.2% around iteration 70 and come back together, to a few 1e-5 by
            # iteration 200, and how close depends on the OpenSees (2415.86 with 3.8.0, 2415.81 with the latest).
            gap = abs(compliance[-1] / AFTER_200[name] - 1.0)
            check(len(compliance) == 200 and gap < 1e-4, "{0}: compliance {1:.3f} after {2} iterations, the panel's {3} ({4:.1e} apart)".format(
                name, compliance[-1], len(compliance), AFTER_200[name], gap))
    with open(os.path.join(WORK, "benchmarks_compliance.csv"), "w") as f:
        for name, (n, c) in sorted(found.items()):
            f.write("{0},{1},{2!r},{3:.0f}\n".format(name, n, c, took.get(name, 0.0)))

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
    shown, end = scrub(300)
    check(shown == found["Cantilever"][0] and max(abs(a - b) for a, b in zip(end, latest)) == 0.0,
          "and at its end, the latest again ({0})".format(shown))

    # The panels the definition shows: the last compliance of each SIMP run.
    shown = [o for o in objects if o.GetType().Name == "GH_Panel" and o.NickName == "Compliance, last iteration"]
    check(len(shown) == 2 and all(p.VolatileData.DataCount == 1 for p in shown), "the 'Compliance, last iteration' panels show one value each")

    # Each problem's three designs one above the other, as the definition draws them, each named with its compliance.
    if PICTURES:
        rdoc = rhino_doc()
        blue = System.Drawing.Color.FromArgb(66, 136, 247)
        for problem, x0, span in (("Cantilever", 0, 60), ("Simply supported", example.SIMPLE_X, 120)):
            for o in list(rdoc.Objects):
                rdoc.Objects.Delete(o, True)
            for label, up in example.SHOWN:
                name = problem if label == "SIMP" else problem + ", " + label
                c = runs[name]
                multi = label == "Multi-Material"
                meshes = values(c.Params.Output[0])
                colours = values(c.Params.Output[1]) if multi else [blue] * len(meshes)
                baked = 0
                for mesh, colour in zip(meshes, colours):
                    mesh = mesh.DuplicateMesh()
                    mesh.Transform(Transform.Translation(0.0, 0.0, up))
                    # Rhino refuses a mesh that is not valid: degenerate faces go first.
                    if not mesh.IsValid:
                        mesh.Faces.CullDegenerateFaces()
                        mesh.Compact()
                    # Creased at 35 degrees, as the docs' topology pictures: the faces stay flat, the edges sharp.
                    mesh.Unweld(math.radians(35.0), True)
                    mesh.Normals.ComputeNormals()
                    if rdoc.Objects.AddMesh(mesh, attributes(colour), None, False, False) != System.Guid.Empty:
                        baked += 1
                check(baked == len(meshes) and baked > 0, "{0}: its {1} surface mesh(es) drawn".format(name, len(meshes)))
                text = "{0}    C = {1:.1f}".format(label, found[name][1])
                plane = Plane(Point3d(x0 + span / 2.0, -1.0, up - 4.5), Vector3d.XAxis, Vector3d.ZAxis)
                entity = Rhino.Geometry.TextEntity.Create(text, plane, rdoc.DimStyles.Current, False, 0.0, 0.0)
                # The document's style scales text for model space (0.15 in Rhino's default metres template): undo it.
                entity.TextHeight = 3.6 / (entity.DimensionScale if entity.DimensionScale > 0 else 1.0)
                entity.Justification = Rhino.Geometry.TextJustification.MiddleCenter
                rdoc.Objects.AddText(entity, attributes(System.Drawing.Color.FromArgb(42, 45, 49)))
            rdoc.Views.Redraw()
            stem = problem.lower().replace(" ", "-")
            capture(rdoc, os.path.join(WORK, stem + "-designs.png"), 4800)
            capture(rdoc, os.path.join(WORK, stem + "-designs-3d.png"), 4800, Vector3d(0.45, -1.2, 0.6))
        for o in list(rdoc.Objects):
            rdoc.Objects.Delete(o, True)
    doc.RemoveObjects(list(doc.Objects), False)
except Exception:
    say("  [FAIL] the script ran: " + traceback.format_exc())
    fails[0] += 1

lines.append("")
lines.append("all example checks passed" if fails[0] == 0 else "{0} example check(s) failed".format(fails[0]))
with open(os.path.join(WORK, "result.txt"), "w") as f:
    f.write("\n".join(lines) + "\n")
