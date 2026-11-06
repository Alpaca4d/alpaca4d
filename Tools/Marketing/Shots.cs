using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

using Rhino;
using Rhino.Display;
using Rhino.Geometry;

// Marketing pictures of the Alpaca4d examples, from the real definitions on a live Grasshopper canvas:
// each scene opens an example, solves it (or runs it), adds Custom Previews in the brand colours where
// Grasshopper's own pink would be the only colour, and saves viewport captures on light grey, canvas
// pictures and animation frames. It drives the plugin installed in that Rhino, by GUID and reflection
// only. run.sh builds it, loads it into Rhino 8 and starts it; see README.md.
public static class Shots
{
    static readonly Guid TopologyId = new Guid("8F3C2A61-5D4E-4B7A-9C1E-2B6D7A0F4E93");
    static readonly Guid ModelViewId = new Guid("F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE");
    static readonly Guid RunAnalysisId = new Guid("8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452");
    static readonly Guid LegendId = new Guid("A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5");
    static readonly Guid StressLinesId = new Guid("E2C4E5D3-4A7F-4F8E-9A1D-39E0E7C0D4A2");
    static readonly Guid CustomPreviewId = new Guid("537B0419-BBC2-4FF4-BF08-AFE526367B2C");

    // The brand palette (Alpaca.Core/Utils/BrandColors.cs).
    static readonly Color LightBlue = Color.FromArgb(66, 136, 247);
    static readonly Color Orange = Color.FromArgb(235, 108, 63);
    static readonly Color Purple = Color.FromArgb(158, 53, 218);
    static readonly Color DarkTech = Color.FromArgb(42, 45, 49);

    // The pictures' background: a light grey rather than white, which post.py keeps.
    static readonly Color Background = Color.FromArgb(251, 251, 251);

    // Set by Run, from what run.sh passes.
    static string examples, here, licence;
    static bool quitWhenDone;

    static string outDir, logPath, donePath;
    static Queue<(string scene, string name, Func<bool> step)> steps;
    static readonly List<string> failed = new List<string>();
    static Stopwatch throttle, stepClock;
    static bool busy;
    static GH_Document doc;
    static GH_Document shownBefore;
    static Action cleanup;
    public static int Width = 2400, Height = 1500;

    static CultureInfo Inv => CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------ running the scenes

    /// <summary>
    /// Starts the scenes named in <paramref name="scenes"/>, comma separated, one after another, each
    /// saving into <paramref name="output"/>/scene. Returns at once: the scenes run step by step while
    /// Rhino is idle, and write log.txt as they go and done.txt, naming any scene that failed, at the end.
    /// </summary>
    public static string Run(string scenes, string output, string repo, string licenceFile, string quit)
    {
        // run.sh sends the script again when it seems dropped; a late copy must not start a second run.
        if (steps != null && steps.Count > 0) return "already running";
        logPath = Path.Combine(output, "log.txt");
        donePath = Path.Combine(output, "done.txt");
        Directory.CreateDirectory(output);
        File.WriteAllText(logPath, "");
        examples = Path.Combine(repo, "Tools", "Examples") + Path.DirectorySeparatorChar;
        here = Path.Combine(repo, "Tools", "Marketing") + Path.DirectorySeparatorChar;
        licence = licenceFile;
        quitWhenDone = quit == "1";
        failed.Clear();
        cleanup = null;
        try
        {
            Setup();
            steps = new Queue<(string, string, Func<bool>)>();
            foreach (var scene in scenes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                List<(string, Func<bool>)> list;
                switch (scene)
                {
                    case "cow": list = Cow(); break;
                    case "femur": list = Femur(); break;
                    case "dragon": list = Dragon(); break;
                    case "bridge": list = Bridge(); break;
                    case "benchmarks": list = Benchmarks(); break;
                    case "lines2d": list = Lines2d(); break;
                    case "bricks": list = Bricks(); break;
                    case "mixed": list = Mixed(); break;
                    case "tower": list = Tower(); break;
                    case "bridgelines": list = BridgeLines(); break;
                    case "twospan": list = TwoSpan(); break;
                    case "curvedbeam": list = CurvedBeam(); break;
                    case "beamgrid": list = BeamGrid(); break;
                    case "howto": list = HowTo(); break;
                    case "structures": list = Structures(); break;
                    case "earthquakes": list = Earthquakes(); break;
                    case "plates": list = Plates(); break;
                    case "kangaroo": list = Kangaroo(); break;
                    case "nerviflatslab": list = NerviFlatSlab(); break;
                    case "nerviumbrella": list = NerviUmbrella(); break;
                    case "musmeci": list = MusmeciBridge(); break;
                    default:
                        if (!Studies.TryGetValue(scene, out var study)) throw new Exception("no scene " + scene);
                        list = StudyScene(study);
                        break;
                }
                string folder = Path.Combine(output, scene);
                steps.Enqueue((scene, "=== " + scene, () =>
                {
                    outDir = folder;
                    Directory.CreateDirectory(outDir);
                    return true;
                }));
                foreach (var (name, step) in list) steps.Enqueue((scene, name, step));
            }
            steps.Enqueue(("finish", "settings put back", () => { Finish(); return true; }));
            throttle = Stopwatch.StartNew();
            stepClock = Stopwatch.StartNew();
            RhinoApp.Idle += Tick;
        }
        catch (Exception ex)
        {
            Log("FAIL " + ex);
            failed.Add("all");
            End();
        }
        return "started " + scenes;
    }

    static void Tick(object sender, EventArgs e)
    {
        if (busy || throttle.ElapsedMilliseconds < 400) return;
        throttle.Restart();
        busy = true;
        try
        {
            while (steps.Count > 0)
            {
                var (scene, name, step) = steps.Peek();
                try
                {
                    if (!step())
                    {
                        if (stepClock.Elapsed.TotalMinutes > 15) throw new Exception("timed out");
                        return;
                    }
                    Log(string.Format(Inv, "{0,6:F1} s  {1}", stepClock.Elapsed.TotalSeconds, name));
                    steps.Dequeue();
                }
                catch (Exception ex)
                {
                    // A scene that fails is closed and skipped; the ones after it still run.
                    Log($"FAIL {scene}, in {name}: {ex}");
                    failed.Add(scene);
                    CleanUp();
                    while (steps.Count > 0 && steps.Peek().scene == scene) steps.Dequeue();
                }
                stepClock.Restart();
            }
            RhinoApp.Idle -= Tick;
            End();
        }
        catch (Exception ex)
        {
            RhinoApp.Idle -= Tick;
            Log("FAIL " + ex);
            failed.Add("all");
            End();
        }
        finally { busy = false; }
    }

    static void CleanUp()
    {
        try { cleanup?.Invoke(); } catch (Exception ex) { Log("cleanup: " + ex.Message); }
        cleanup = null;
    }

    static void End()
    {
        CleanUp();
        File.WriteAllText(donePath, failed.Count == 0 ? "ok" : "failed: " + string.Join(" ", failed.Distinct()));
        // A Rhino killed from outside can start the next time with a dialog that stops its start-up
        // script, so the Rhino run.sh started closes itself, with nothing to save.
        if (quitWhenDone)
        {
            RhinoDoc.ActiveDoc.Modified = false;
            RhinoApp.Idle += Quit;
        }
    }

    static void Quit(object sender, EventArgs e)
    {
        RhinoApp.Idle -= Quit;
        // Not Exit(): a question about saving, which nobody is there to answer, cancels that.
        RhinoApp.Exit(false);
    }

    static void Log(string line) => File.AppendAllText(logPath, line + Environment.NewLine);

    static (string, Func<bool>) Do(string name, Action a) => (name, () => { a(); return true; });
    static (string, Func<bool>) Until(string name, Func<bool> f) => (name, f);

    // ------------------------------------------------------------------ Rhino and Grasshopper

    static bool meshEdgesBefore;

    static void Setup()
    {
        var gh = (Grasshopper.Plugin.GH_RhinoScriptInterface)RhinoApp.GetPlugInObject("Grasshopper");
        if (!gh.IsEditorLoaded()) gh.LoadEditor();
        gh.ShowEditor();
        var core = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Alpaca4d.Core");
        if (core == null) throw new Exception("Alpaca4d is not installed in this Rhino's Grasshopper");
        Log("Alpaca4d from " + core.Location);
        // A licence file, if one was given, in the Alpaca4d.Core that Grasshopper loaded: the examples
        // are past the free limit.
        if (!string.IsNullOrEmpty(licence))
        {
            core.GetType("Alpaca4d.License.License").GetMethod("Install", new[] { typeof(string) }).Invoke(null, new object[] { licence });
            Log("licence " + licence);
        }
        // Grasshopper's mesh edges would draw every brick; Finish turns them back on.
        meshEdgesBefore = CentralSettings.PreviewMeshEdges;
        CentralSettings.PreviewMeshEdges = false;
        RhinoDoc.ActiveDoc.GroundPlane.Enabled = false;
    }

    /// <summary>Puts back what Setup changed in Grasshopper's settings, and drops the pictures' display mode.</summary>
    static void Finish()
    {
        CentralSettings.PreviewMeshEdges = meshEdgesBefore;
        var mode = DisplayModeDescription.FindByName(ModeName);
        if (mode != null) DropMode(mode);
    }

    static void Open(string file)
    {
        string path = Path.IsPathRooted(file) ? file : examples + file;
        shownBefore = Instances.ActiveCanvas.Document;
        var io = new GH_DocumentIO();
        if (!io.Open(path)) throw new Exception("could not open " + path);
        doc = io.Document;
        Instances.DocumentServer.AddDocument(doc);
        Instances.ActiveCanvas.Document = doc;
        doc.Enabled = true;
        doc.NewSolution(true);
        Log($"opened {file}: {doc.ObjectCount} objects");
        // make.sh's panels have no colour of their own: Rhino draws them see-through, with white text.
        foreach (var panel in doc.Objects.OfType<GH_Panel>())
            if (panel.Properties.Colour.A < 255 || panel.Properties.Colour.ToArgb() == Color.White.ToArgb())
                panel.Properties.Colour = Color.FromArgb(255, 255, 250, 90);
        Unoverlap();
        Origin();
        cleanup = Close;
    }

    /// <summary>
    /// Components the file has on top of one another - Rhino draws some taller than the layout that
    /// wrote the file measured them - moved down until they clear. Only for the pictures: nothing is saved.
    /// </summary>
    static void Unoverlap()
    {
        var all = doc.Objects.Where(o => !(o is GH_Group)).ToList();
        foreach (var o in all) { o.Attributes.ExpireLayout(); o.Attributes.PerformLayout(); }
        var components = all.OfType<IGH_Component>().ToList();
        int moved = 0;
        for (int pass = 0; pass < 30; pass++)
        {
            var sorted = components.OrderBy(c => c.Attributes.Bounds.Top).ToList();
            RectangleF lower = RectangleF.Empty;
            float dy = 0;
            for (int i = 0; i < sorted.Count && dy == 0; i++)
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    var a = sorted[i].Attributes.Bounds;
                    var b = sorted[j].Attributes.Bounds;
                    if (!a.IntersectsWith(b) || b.Top < a.Top + 4) continue;
                    dy = a.Bottom - b.Top + 12;
                    lower = b;
                    break;
                }
            if (dy == 0) break;
            // Everything from the lower one's top down moves with it, so groups keep clear of each other.
            foreach (var o in all.Where(o => o.Attributes.Bounds.Top >= lower.Top - 0.5f))
            {
                var p = o.Attributes.Pivot;
                o.Attributes.Pivot = new PointF(p.X, p.Y + dy);
                o.Attributes.ExpireLayout();
                o.Attributes.PerformLayout();
            }
            moved++;
        }
        if (moved > 0)
        {
            foreach (var g in doc.Objects.OfType<GH_Group>()) { g.Attributes.ExpireLayout(); g.Attributes.PerformLayout(); }
            Log($"  moved components apart {moved} times");
        }
    }

    /// <summary>The definition moved onto positive canvas coordinates: the export starts at 0, 0.</summary>
    static void Origin()
    {
        var box = RectangleF.Empty;
        foreach (var o in doc.Objects)
        {
            o.Attributes.ExpireLayout();
            o.Attributes.PerformLayout();
            box = box.IsEmpty ? o.Attributes.Bounds : RectangleF.Union(box, o.Attributes.Bounds);
        }
        float dx = 60 - box.X, dy = 60 - box.Y;
        foreach (var o in doc.Objects.Where(o => !(o is GH_Group)))
        {
            var p = o.Attributes.Pivot;
            o.Attributes.Pivot = new PointF(p.X + dx, p.Y + dy);
            o.Attributes.ExpireLayout();
            o.Attributes.PerformLayout();
        }
        foreach (var g in doc.Objects.OfType<GH_Group>()) { g.Attributes.ExpireLayout(); g.Attributes.PerformLayout(); }
    }

    static void Close()
    {
        if (doc == null) return;
        doc.Enabled = false;
        Instances.ActiveCanvas.Document = shownBefore;
        Instances.DocumentServer.RemoveDocument(doc);
        doc = null;
    }

    static IEnumerable<IGH_Component> Components(Guid id) => doc.Objects.OfType<IGH_Component>().Where(c => c.ComponentGuid == id);

    /// <summary>Whether a Model View has any result ticked, rather than drawing the model alone.</summary>
    static bool DrawsResults(IGH_Component view)
    {
        foreach (var result in (Array)Field(view, "_results"))
        {
            var tick = result.GetType().GetField("Tick").GetValue(result);
            if ((bool)tick.GetType().GetProperty("Active").GetValue(tick)) return true;
        }
        return false;
    }

    /// <summary>The Model Views that draw a result, rather than the model alone.</summary>
    static IEnumerable<IGH_Component> ResultViews() => Components(ModelViewId).Where(DrawsResults);
    static IGH_Param Out(IGH_Component c, string name) => c.Params.Output.First(p => p.Name == name || p.NickName == name);
    static IGH_Param In(IGH_Component c, string name) => c.Params.Input.First(p => p.Name == name || p.NickName == name);
    static List<T> Values<T>(IGH_Param p) => p.VolatileData.AllData(true).OfType<T>().ToList();
    static T Find<T>(string nick) where T : class, IGH_DocumentObject =>
        doc.Objects.OfType<T>().FirstOrDefault(o => o.NickName == nick) ?? throw new Exception("no " + typeof(T).Name + " " + nick);

    static object Field(object o, string name)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) return f.GetValue(o);
        }
        throw new Exception($"{o.GetType().Name} has no {name}");
    }

    static void SetWidget(object o, string field, string property, object value)
    {
        var w = Field(o, field);
        w.GetType().GetProperty(property).SetValue(w, value);
    }

    static void Messages(IGH_ActiveObject c)
    {
        foreach (var level in new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning })
            foreach (var m in c.RuntimeMessages(level))
                Log($"  {c.Name} {level}: {m}");
    }

    static void ShowOnly(params IGH_DocumentObject[] shown)
    {
        foreach (var o in doc.Objects.OfType<IGH_PreviewObject>())
            o.Hidden = !shown.Contains((IGH_DocumentObject)o);
        doc.DeselectAll();
    }

    static void Slider(string nick, double value)
    {
        var s = Find<GH_NumberSlider>(nick);
        s.SetSliderValue((decimal)value);
        s.ExpireSolution(true);
    }

    /// <summary>Right of everything on the canvas, at the height of <paramref name="near"/>.</summary>
    static PointF Beside(IGH_DocumentObject near, float dx = 120, float dy = 0)
    {
        var b = near.Attributes.Bounds;
        float right = doc.Objects.Where(o => !(o is GH_Group)).Max(o => o.Attributes.Bounds.Right);
        return new PointF(right + dx, b.Top + b.Height / 2 + dy);
    }

    static T Place<T>(T o, PointF at) where T : IGH_DocumentObject
    {
        o.CreateAttributes();
        o.Attributes.Pivot = at;
        o.Attributes.ExpireLayout();
        o.Attributes.PerformLayout();
        doc.AddObject(o, false);
        return o;
    }

    /// <summary>Grasshopper's Weld Mesh on <paramref name="source"/>: smooth where faces meet at less than 35 degrees.</summary>
    static IGH_Param Welded(IGH_Param source, PointF at)
    {
        var proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Desc.Name == "Weld Mesh");
        if (proxy == null) { Log("  no Weld Mesh component"); return source; }
        var weld = Place((IGH_Component)proxy.CreateInstance(), at);
        weld.Params.Input[0].AddSource(source);
        if (weld.Params.Input.Count > 1 && weld.Params.Input[1] is GH_PersistentParam<GH_Number> angle)
        {
            angle.PersistentData.Clear();
            angle.PersistentData.Append(new GH_Number(35 * Math.PI / 180));
        }
        return weld.Params.Output[0];
    }

    /// <summary>A Custom Preview of <paramref name="source"/> in <paramref name="colour"/>, fed by a swatch.</summary>
    static IGH_Component Preview(IGH_Param source, Color colour, PointF at)
    {
        var swatch = Place(new GH_ColourSwatch { SwatchColour = colour }, new PointF(at.X, at.Y + 40));
        var preview = Place((IGH_Component)Instances.ComponentServer.EmitObject(CustomPreviewId), new PointF(at.X + 130, at.Y));
        preview.Params.Input[0].AddSource(source);
        preview.Params.Input[1].AddSource(swatch);
        preview.ExpireSolution(false);
        swatch.ExpireSolution(false);
        return preview;
    }

    // ------------------------------------------------------------------ the viewport

    const string ModeName = "Alpaca marketing";

    static DisplayModeDescription WhiteMode()
    {
        var mode = DisplayModeDescription.FindByName(ModeName);
        if (mode != null) return mode;
        var shaded = DisplayModeDescription.FindByName("Shaded");
        var id = DisplayModeDescription.CopyDisplayMode(shaded.Id, ModeName);
        mode = DisplayModeDescription.GetDisplayMode(id);
        mode.DisplayAttributes.FillMode = DisplayPipelineAttributes.FrameBufferFillMode.SolidColor;
        mode.DisplayAttributes.SetFill(Background);
        try { mode.DisplayAttributes.MeshSpecificAttributes.ShowMeshWires = false; } catch { }
        DisplayModeDescription.UpdateDisplayMode(mode);
        return DisplayModeDescription.GetDisplayMode(id);
    }

    // Every viewport goes back to Shaded before the mode goes: Rhino aborts on a Mac when a viewport
    // still shows a mode that is deleted.
    static void DropMode(DisplayModeDescription mode)
    {
        var shaded = DisplayModeDescription.FindByName("Shaded");
        foreach (var view in RhinoDoc.ActiveDoc.Views)
            if (view.ActiveViewport.DisplayMode.Id == mode.Id)
            {
                view.ActiveViewport.DisplayMode = shaded;
                view.Redraw();
            }
        RhinoApp.Wait();
        DisplayModeDescription.DeleteDisplayMode(mode.Id);
    }

    static bool fresh = true;

    static RhinoView View(string name)
    {
        fresh = true;
        var rdoc = RhinoDoc.ActiveDoc;
        var view = rdoc.Views.Find(name, false) ?? rdoc.Views.ActiveView;
        rdoc.Views.ActiveView = view;
        var vp = view.ActiveViewport;
        vp.ConstructionGridVisible = false;
        vp.ConstructionAxesVisible = false;
        vp.WorldAxesVisible = false;
        vp.DisplayMode = WhiteMode();
        return view;
    }

    /// <summary>The box of what Grasshopper draws: every preview object not hidden.</summary>
    static BoundingBox Drawn()
    {
        var box = BoundingBox.Empty;
        foreach (var o in doc.Objects.OfType<IGH_PreviewObject>())
            if (!o.Hidden && o.IsPreviewCapable) box.Union(o.ClippingBox);
        return box;
    }

    /// <summary>A perspective looking at <paramref name="box"/> from along <paramref name="eye"/>.</summary>
    static RhinoView Aim(BoundingBox box, Vector3d eye, Vector3d up, double lens = 50, double magnify = 1.0)
    {
        var view = View("Perspective");
        var vp = view.ActiveViewport;
        vp.ChangeToPerspectiveProjection(true, lens);
        vp.Camera35mmLensLength = lens;
        var c = box.Center;
        eye.Unitize();
        vp.SetCameraLocations(c, c + eye * box.Diagonal.Length * 3);
        vp.CameraUp = up;
        vp.ZoomBoundingBox(box);
        if (magnify != 1.0) vp.Magnify(magnify, false);
        return view;
    }

    static RhinoView Top(BoundingBox box)
    {
        var view = View("Top");
        view.ActiveViewport.ZoomBoundingBox(box);
        return view;
    }

    static Guid lastViewport = Guid.Empty;

    /// <summary>
    /// The view on the background grey, at Width x Height times <paramref name="scale"/>, with screen
    /// items scaled to the capture: a still for a page at 2, so that it stays sharp once trimmed to the
    /// model, and the frames of an animation at 1.
    /// </summary>
    static void Shoot(RhinoView view, string name, double scale = 1)
    {
        var mode = WhiteMode();
        view.ActiveViewport.DisplayMode = mode;
        doc.DeselectAll();
        view.Redraw();
        RhinoApp.Wait();
        var capture = new ViewCapture
        {
            Width = (int)(Width * scale), Height = (int)(Height * scale), ScaleScreenItems = true,
            DrawAxes = false, DrawGrid = false, DrawGridAxes = false, TransparentBackground = false,
        };
        // The first capture of a viewport can come before the mode takes hold: grey, not white.
        if (view.ActiveViewportID != lastViewport || fresh)
        {
            capture.CaptureToBitmap(view)?.Dispose();
            view.Redraw();
            RhinoApp.Wait();
            lastViewport = view.ActiveViewportID;
            fresh = false;
        }
        using (var bitmap = capture.CaptureToBitmap(view))
        {
            var path = Path.Combine(outDir, name + ".png");
            bitmap.Save(path, ImageFormat.Png);
            // Frames, saved into a folder of their own, are counted by Iterations instead.
            if (Path.GetDirectoryName(name) == "") Log($"  picture {name}.png {bitmap.Width}x{bitmap.Height}");
        }
    }

    // ------------------------------------------------------------------ the canvas

    /// <summary>The whole definition as Grasshopper draws it, at <paramref name="zoom"/>.</summary>
    static void Canvas(string name, float zoom = 1.5f, Func<IGH_DocumentObject, bool> only = null)
    {
        doc.DeselectAll();
        var canvas = Instances.ActiveCanvas;
        // The export starts where the canvas is looking, whatever the region says: a file saved
        // panned would come out shifted.
        canvas.Viewport.Zoom = 1f;
        canvas.Viewport.Tx = 0;
        canvas.Viewport.Ty = 0;
        canvas.Viewport.ComputeProjection();
        canvas.Refresh();
        // The whole definition is exported - the export reads the region's size but not where it is -
        // and a part of it, when asked for, cut out of that.
        RectangleF r = RectangleF.Empty, part = RectangleF.Empty;
        foreach (var o in doc.Objects)
        {
            o.Attributes.ExpireLayout();
            o.Attributes.PerformLayout();
            var b = o.Attributes.Bounds;
            r = r.IsEmpty ? b : RectangleF.Union(r, b);
            if (only != null && only(o)) part = part.IsEmpty ? b : RectangleF.Union(part, b);
        }
        // The export starts at the canvas's 0, 0 whatever the region says; Origin put the definition at 60, 60.
        r = new RectangleF(0, 0, r.Right + 60, r.Bottom + 60);
        part.Inflate(40, 40);
        part.Height += 30;
        // Grasshopper's own canvas colour: left alone, the background is transparent.
        var settings = new GH_Canvas.GH_ImageSettings { Zoom = zoom, BackColour = Grasshopper.GUI.Canvas.GH_Skin.canvas_back };
        var tiles = canvas.GenerateHiResImage(Rectangle.Round(r), settings, out Size size);
        var placed = new List<(int col, int row, Bitmap b)>();
        foreach (var t in tiles)
        {
            var parts = Path.GetFileNameWithoutExtension(t).Split(';');
            placed.Add((int.Parse(parts[0]), int.Parse(parts[1]), new Bitmap(t)));
        }
        var widths = placed.Where(p => p.row == 0).OrderBy(p => p.col).Select(p => p.b.Width).ToList();
        var heights = placed.Where(p => p.col == 0).OrderBy(p => p.row).Select(p => p.b.Height).ToList();
        using (var sheet = new Bitmap(widths.Sum(), heights.Sum()))
        {
            using (var g = Graphics.FromImage(sheet))
                foreach (var p in placed)
                    g.DrawImage(p.b, widths.Take(p.col).Sum(), heights.Take(p.row).Sum(), p.b.Width, p.b.Height);
            if (only == null)
            {
                sheet.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
                Log($"  canvas {name}.png {sheet.Width}x{sheet.Height} from {tiles.Count} tiles");
            }
            else
            {
                var cut = Rectangle.Round(new RectangleF((part.X - r.X) * zoom, (part.Y - r.Y) * zoom, part.Width * zoom, part.Height * zoom));
                cut.Intersect(new Rectangle(0, 0, sheet.Width, sheet.Height));
                using (var piece = new Bitmap(cut.Width, cut.Height))
                {
                    using (var g = Graphics.FromImage(piece))
                        g.DrawImage(sheet, new Rectangle(0, 0, cut.Width, cut.Height), cut, GraphicsUnit.Pixel);
                    piece.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
                }
                Log($"  canvas {name}.png {cut.Width}x{cut.Height}, cut from {sheet.Width}x{sheet.Height}");
            }
        }
        foreach (var p in placed) p.b.Dispose();
    }

    // ------------------------------------------------------------------ solids

    static List<(string, Func<bool>)> Cow() => new List<(string, Func<bool>)>
    {
        Do("open", () => Open("SpotTheCow.gh")),
        Do("cow", () =>
        {
            foreach (var c in doc.Objects.OfType<IGH_ActiveObject>()) Messages(c);
            var box = Drawn();
            Log($"  drawn {box}");
            Shoot(Aim(box, new Vector3d(0.9, -1.5, 0.55), Vector3d.ZAxis), "cow");
            Shoot(Aim(box, new Vector3d(-1.4, -0.8, 0.35), Vector3d.ZAxis), "cow-2");
            Canvas("cow-canvas");
        }),
        Do("close", Close),
    };

    static List<(string, Func<bool>)> Femur() => new List<(string, Func<bool>)>
    {
        // The working copy: the committed file's knee misses 3 edge nodes, and OpenSees does not converge.
        Do("open", () => Open("Femur.gh")),
        Do("displacement", () =>
        {
            foreach (var c in doc.Objects.OfType<IGH_ActiveObject>()) Messages(c);
            var box = Drawn();
            Log($"  drawn {box}");
            Shoot(Aim(box, new Vector3d(0.35, -1, 0.15), Vector3d.ZAxis), "femur");
            Canvas("femur-canvas");
        }),
        Do("stress lines", () => FemurLines()),
        Do("close", Close),
    };

    /// <summary>
    /// Principal Stress Lines on its Solids unit, added to the femur: a field grown from seeds in the
    /// bone's middle plane, tension (SL1) orange and compression (SL3) blue, inside the bone's skin,
    /// nearly clear.
    /// </summary>
    static void FemurLines()
    {
        var run = Components(RunAnalysisId).First();
        var results = ResultViews().First();
        var (nodes, tets) = ReadTets(examples + "Femur.txt");
        var skin = Skin(nodes, tets);
        var box = new BoundingBox(nodes);
        double yMid = 0.5 * (box.Min.Y + box.Max.Y);
        var seeds = new List<GH_Point>();
        for (double x = box.Min.X; x <= box.Max.X; x += 0.008)
            for (double z = 0.24; z <= box.Max.Z; z += 0.008)
                seeds.Add(new GH_Point(new Point3d(x, yMid, z)));

        var at = Beside(results, 80, 220);
        var lines = StressLines(run.Params.Output[0], seeds, false, 0.009, 0.0045, at);
        var skinParam = Place(new Param_Mesh { NickName = "Skin" }, new PointF(at.X, at.Y + 260));
        skinParam.PersistentData.Append(new GH_Mesh(skin));
        var right = lines.Attributes.Bounds.Right + 60;
        var p1 = Preview(Out(lines, "SL1"), Purple, new PointF(right, at.Y - 60));
        var p3 = Preview(Out(lines, "SL3"), LightBlue, new PointF(right, at.Y + 60));
        var ps = Preview(skinParam, Color.FromArgb(30, DarkTech), new PointF(right, at.Y + 200));
        var watch = Stopwatch.StartNew();
        doc.NewSolution(false);
        Messages(lines);
        Log($"  stress lines in {watch.Elapsed.TotalSeconds:F1} s: {Values<GH_Curve>(Out(lines, "SL1")).Count} + {Values<GH_Curve>(Out(lines, "SL2")).Count} + {Values<GH_Curve>(Out(lines, "SL3")).Count} lines");
        ShowOnly(p1, p3, ps);
        var proximal = new BoundingBox(new Point3d(box.Min.X - 0.004, box.Min.Y, 0.25), new Point3d(box.Max.X + 0.004, box.Max.Y, box.Max.Z + 0.004));
        Shoot(Aim(proximal, new Vector3d(0.9, -1.6, 0.7), Vector3d.ZAxis), "femur-stress-lines");
        var front = View("Front");
        front.ActiveViewport.ZoomBoundingBox(proximal);
        Shoot(front, "femur-stress-lines-front");
        Shoot(Aim(box, new Vector3d(0.35, -1, 0.15), Vector3d.ZAxis), "femur-stress-lines-whole");
        Canvas("femur-stress-lines-canvas");
    }


    static readonly Guid AnalysisSettingsId = new Guid("9AE62757-69CC-4023-92EB-E8E1AA164AD0");

    /// <summary>Principal Stress Lines on its Solids unit, its Advanced menu open, fed by a Point parameter of the seeds.</summary>
    static IGH_Component StressLines(IGH_Param model, List<GH_Point> seeds, bool single, double separation, double test, PointF at)
    {
        var seedParam = Place(new Param_Point { NickName = "Seeds" }, at);
        seedParam.PersistentData.AppendRange(seeds);
        var lines = Place((IGH_Component)Instances.ComponentServer.EmitObject(StressLinesId), new PointF(at.X + 200, at.Y));
        lines.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) }).Invoke(lines, new object[] { "PrincipalStressLines3D", false, false });
        var unit = lines.GetType().GetProperty("ActiveUnit")?.GetValue(lines);
        if (unit != null)
        {
            var context = unit.GetType().GetProperty("Context").GetValue(unit);
            var collection = context.GetType().GetProperty("Collection").GetValue(context);
            foreach (var menu in (System.Collections.IEnumerable)collection.GetType().GetProperty("Menus").GetValue(collection))
                menu.GetType().GetMethod("Expand").Invoke(menu, null);
        }
        lines.Attributes.ExpireLayout();
        lines.Attributes.PerformLayout();
        In(lines, "AlpacaModel").AddSource(model);
        In(lines, "Seed").AddSource(seedParam);
        void Set<T>(string name, T value) where T : class, IGH_Goo
        {
            var param = (GH_PersistentParam<T>)In(lines, name);
            param.PersistentData.Clear();
            param.PersistentData.Append(value);
        }
        Set("Single Line", new GH_Boolean(single));
        if (separation > 0) Set("Separation", new GH_Number(separation));
        if (test > 0) Set("Test Dist.", new GH_Number(test));
        return lines;
    }

    static (List<Point3d> nodes, List<int[]> tets) ReadTets(string file)
    {
        var lines = File.ReadAllLines(file);
        int count = int.Parse(lines[0].Split(' ')[1]);
        var nodes = lines.Skip(1).Take(count).Select(l => l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, Inv)).ToArray())
                         .Select(v => new Point3d(v[0], v[1], v[2])).ToList();
        int n = int.Parse(lines[1 + count].Split(' ')[1]);
        var tets = lines.Skip(2 + count).Take(n).Select(l => l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray()).ToList();
        return (nodes, tets);
    }

    /// <summary>The faces only one tetrahedron has, turned outward.</summary>
    static Mesh Skin(List<Point3d> nodes, List<int[]> tets)
    {
        var faces = new Dictionary<(int, int, int), (int a, int b, int c, int count)>();
        foreach (var t in tets)
        {
            int[][] sides = { new[] { t[0], t[1], t[2], t[3] }, new[] { t[0], t[1], t[3], t[2] }, new[] { t[0], t[2], t[3], t[1] }, new[] { t[1], t[2], t[3], t[0] } };
            foreach (var s in sides)
            {
                var key = new[] { s[0], s[1], s[2] }.OrderBy(i => i).ToArray();
                var k = (key[0], key[1], key[2]);
                // Turned away from the fourth node.
                var normal = Vector3d.CrossProduct(nodes[s[1]] - nodes[s[0]], nodes[s[2]] - nodes[s[0]]);
                bool outward = normal * (nodes[s[3]] - nodes[s[0]]) < 0;
                var face = outward ? (s[0], s[1], s[2], 1) : (s[0], s[2], s[1], 1);
                faces[k] = faces.TryGetValue(k, out var f) ? (f.a, f.b, f.c, f.count + 1) : face;
            }
        }
        var mesh = new Mesh();
        foreach (var p in nodes) mesh.Vertices.Add(p);
        foreach (var f in faces.Values.Where(f => f.count == 1)) mesh.Faces.AddFace(f.a, f.b, f.c);
        mesh.Compact();
        mesh.Normals.ComputeNormals();
        return mesh;
    }

    static List<(string, Func<bool>)> Dragon()
    {
        var list = new List<(string, Func<bool>)>
        {
            Do("open", () => Open("InigoTheDragon.gh")),
            Do("modes", () =>
            {
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>()) Messages(c);
                var results = ResultViews().First();
                Log($"  deform scale {Field(results, "_slDeformScale").GetType().GetProperty("Value").GetValue(Field(results, "_slDeformScale"))}");
                var box = Drawn();
                box.Inflate(box.Diagonal.Length * 0.04);
                var eye = new Vector3d(1, -1.4, 0.5);
                foreach (int m in new[] { 4, 5, 0, 1 })
                {
                    Slider("Mode, from 0", m);
                    doc.NewSolution(false);
                    Shoot(Aim(box, eye, Vector3d.ZAxis), "dragon-mode-" + m);
                }
                Slider("Mode, from 0", 4);
                doc.NewSolution(false);
                Canvas("dragon-canvas");
            }),
        };
        // Two modes swung across and back at the slider's full reach, one frame each: the wings, and the sway.
        foreach (int mode in new[] { 4, 0 })
            list.Add(Do("mode " + mode + " moving", () =>
            {
                var results = ResultViews().First();
                var scale = Field(results, "_slDeformScale");
                var value = scale.GetType().GetProperty("Value");
                double max = Convert.ToDouble(scale.GetType().GetProperty("MaxValue").GetValue(scale));
                double start = Convert.ToDouble(value.GetValue(scale));
                // The scale moves nothing unless the deformed shape is drawn.
                Log($"  deformed was {Field(results, "_ckDeformed").GetType().GetProperty("Active").GetValue(Field(results, "_ckDeformed"))}");
                SetWidget(results, "_ckDeformed", "Active", true);
                Slider("Mode, from 0", mode);
                doc.NewSolution(false);
                var box = Drawn();
                box.Inflate(box.Diagonal.Length * 0.12);
                string folder = "dragon-mode-" + mode + "-frames";
                Directory.CreateDirectory(Path.Combine(outDir, folder));
                var view = Aim(box, new Vector3d(1, -1.4, 0.5), Vector3d.ZAxis);
                // The file keeps the slider on 0 to 0.8, a range of its own: opened to the mode shapes'
                // -2 to 2 for the capture, so a mode swings through its rest.
                scale.GetType().GetProperty("MinValue").SetValue(scale, -2.0);
                scale.GetType().GetProperty("MaxValue").SetValue(scale, 2.0);
                max = mode == 4 ? 1.5 : 1.0;
                int frames = 36;
                for (int k = 0; k < frames; k++)
                {
                    value.SetValue(scale, max * Math.Sin(2 * Math.PI * k / frames));
                    results.ExpireSolution(false);
                    doc.NewSolution(false);
                    Shoot(view, Path.Combine(folder, k.ToString("D3")));
                }
                value.SetValue(scale, start);
                results.ExpireSolution(true);
                Log($"  mode {mode}: {frames} frames at scale +-{max}");
            }));
        list.Add(Do("close", Close));
        return list;
    }

    /// <summary>
    /// One of Model View's results ticked alone, at a component, as clicks in its Results menu tick
    /// it, and its menus showing the controls that go with it. A result is its place in the menu
    /// from 1 - Displacement, Beam forces, Beam stresses, Shell forces, Shell stresses, Brick
    /// stresses, Reactions - or 0 for none, the model alone.
    /// </summary>
    static void ShowResult(IGH_Component results, int show, int component)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        results.GetType().GetMethod("ShowResult", flags).Invoke(results, new object[] { show, component });
        results.ExpireSolution(false);
    }

    /// <summary>The Unit dropdown of one of Model View's results, by its place in the menu from 1, for a kind of quantity: 0 lengths, 3 stresses.</summary>
    static object Unit(IGH_Component results, int show, int quantity) =>
        results.GetType().GetMethod("UnitOf", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(results, new object[] { show, quantity });

    /// <summary>A Legend fed by a Model View's result, as the examples wire it.</summary>
    static IGH_Component AddLegend(IGH_Component results, string title)
    {
        var legend = Place((IGH_Component)Instances.ComponentServer.EmitObject(LegendId), Beside(results, 120, 0));
        var t = (GH_PersistentParam<GH_String>)In(legend, "Title");
        t.PersistentData.Clear();
        t.PersistentData.Append(new GH_String(title));
        In(legend, "Values").AddSource(Out(results, "Values"));
        In(legend, "Colors").AddSource(Out(results, "Colors"));
        legend.ExpireSolution(false);
        return legend;
    }

    // BrickSolverSpeed: an 8 x 8 x 6 m block of SSP Bricks on four columns, solved and drawn.
    static List<(string, Func<bool>)> Bricks() => new List<(string, Func<bool>)>
    {
        Do("open", () => Open("BrickSolverSpeed.gh")),
        Do("solve", () =>
        {
            var run = Components(RunAnalysisId).First();
            var results = ResultViews().First();
            run.Locked = false;
            results.Locked = false;
            var watch = Stopwatch.StartNew();
            run.ExpireSolution(false);
            doc.NewSolution(false);
            Log($"  solved in {watch.Elapsed.TotalSeconds:F1} s");
            ShowResult(results, 6, 6);
            // The column loads are points: their peaks would take the whole scale.
            var range = (GH_PersistentParam<GH_Interval>)In(results, "Range");
            range.PersistentData.Clear();
            range.PersistentData.Append(new GH_Interval(new Interval(0, 1.5)));
            range.ExpireSolution(false);
            var legend = AddLegend(results, "Von Mises stress [MPa]");
            doc.NewSolution(false);
            Messages(run);
            Messages(results);
            Log("  " + string.Join(" | ", Values<GH_String>(Out(results, "Info")).Select(v => v.Value)));
            ShowOnly(results, legend);
            var box = Drawn();
            Shoot(Aim(box, new Vector3d(1, -1.3, 0.8), Vector3d.ZAxis), "brick-block");

            Canvas("brick-block-canvas");
        }),
        Do("close", Close),
    };

    // MixedModelSpeed: a steel frame on a raft of bricks, with shell slabs - all in one model.
    static List<(string, Func<bool>)> Mixed() => new List<(string, Func<bool>)>
    {
        Do("open", () => Open("MixedModelSpeed.gh")),
        Do("solve", () =>
        {
            var run = Components(RunAnalysisId).First();
            var results = ResultViews().First();
            run.Locked = false;
            results.Locked = false;
            var watch = Stopwatch.StartNew();
            run.ExpireSolution(false);
            doc.NewSolution(false);
            Log($"  solved in {watch.Elapsed.TotalSeconds:F1} s");
            // Lengths in mm: the first of Model View's units.
            var length = Unit(results, 1, 0);
            length.GetType().GetProperty("Value").SetValue(length, 0);
            results.ExpireSolution(false);
            var legend = AddLegend(results, "Displacement [mm]");
            doc.NewSolution(false);
            Messages(run);
            Messages(results);
            Log("  " + string.Join(" | ", Values<GH_String>(Out(results, "Info")).Select(v => v.Value)));
            ShowOnly(results, legend);
            var box = Drawn();
            Shoot(Aim(box, new Vector3d(1, -1.3, 0.6), Vector3d.ZAxis), "mixed-model");
            Canvas("mixed-model-canvas");
        }),
        Do("close", Close),
    };

    // ------------------------------------------------------------------ topology

    static IGH_Component topology, modelView, surfacePreview;

    /// <summary>Runs the Topology Optimisation called <paramref name="nick"/> (any, when empty) to its end.</summary>
    static Func<bool> Finished(Func<IGH_Component> which)
    {
        int seen = -1;
        return () =>
        {
            var t = which();
            int iterations = Values<GH_Number>(Out(t, "Compliance")).Count;
            bool running = t.Message != null && t.Message.StartsWith("Iteration");
            bool settled = !running && iterations > 0 && iterations == seen;
            seen = running ? -1 : iterations;
            if (settled) Log($"  {t.NickName}: {iterations} iterations, {t.Message}");
            return settled;
        };
    }

    /// <summary>
    /// Model View drawing the supports and loads of the model <paramref name="source"/> carries. An
    /// ElementId no element has leaves every element out, and Model View greys what it leaves out:
    /// see-through, and with Element edges off, without its edges.
    /// </summary>
    static IGH_Component Symbols(IGH_Param source, PointF at, double loadScale, double supportScale)
    {
        var view = Place((IGH_Component)Instances.ComponentServer.EmitObject(ModelViewId), at);
        view.Params.Input[0].AddSource(source);
        ((GH_PersistentParam<GH_String>)In(view, "ElementId")).PersistentData.Append(new GH_String("-1"));
        SetWidget(view, "_ckWires", "Active", false);
        SetWidget(view, "_ckShowSupports", "Active", true);
        SetWidget(view, "_ckShowLoads", "Active", loadScale > 0);
        if (loadScale > 0) SetWidget(view, "_slLoadScale", "Value", loadScale);
        SetWidget(view, "_slSupportScale", "Value", supportScale);
        view.ExpireSolution(false);
        return view;
    }

    /// <summary>Model View's load arrows on or off.</summary>
    static void Loads(IGH_Component view, bool on)
    {
        SetWidget(view, "_ckShowLoads", "Active", on);
        view.ExpireSolution(false);
        doc.NewSolution(false);
    }

    /// <summary>
    /// The run again, design by design, dragged along the slider <paramref name="step"/> and saved as
    /// frames in <paramref name="folder"/>: one per iteration, or past the 40th, where the design hardly
    /// changes any more, one every <paramref name="every"/>. Leaves Step at its end, on the last design.
    /// </summary>
    static void Iterations(IGH_Component topology, string step, RhinoView view, string folder, int every = 1)
    {
        int last = Values<GH_Number>(Out(topology, "Compliance")).Count - 1;
        Directory.CreateDirectory(Path.Combine(outDir, folder));
        int k = 0;
        for (int s = 0; s <= last; s++)
        {
            if (s > 40 && s < last && (s - 40) % every != 0) continue;
            Slider(step, s);
            doc.NewSolution(false);
            Shoot(view, Path.Combine(folder, (k++).ToString("D3")));
        }
        var slider = Find<GH_NumberSlider>(step);
        Slider(step, (double)slider.Slider.Maximum);
        doc.NewSolution(false);
        Log($"  {k} frames to step {last}");
    }

    /// <summary>
    /// The design <paramref name="topology"/> found, analysed as a model of its own, as the bridge
    /// example does it: Run Analysis on its AlpacaModel, Model View on the bricks' Von Mises stress,
    /// in <paramref name="stressUnit"/> (0 kPa, the model's own unit; 1 MPa), and a Legend. Added only
    /// once the run is over: wired while Step moves, it would analyse every design.
    /// </summary>
    static (IGH_Component run, IGH_Component results, IGH_Component legend) Analyse(IGH_Component topology, string title, int stressUnit, PointF at)
    {
        var settings = Place((IGH_Component)Instances.ComponentServer.EmitObject(AnalysisSettingsId), at);
        var run = Place((IGH_Component)Instances.ComponentServer.EmitObject(RunAnalysisId), new PointF(at.X + 200, at.Y));
        run.Params.Input[0].AddSource(Out(topology, "AlpacaModel"));
        run.Params.Input[1].AddSource(settings.Params.Output[0]);
        var results = Place((IGH_Component)Instances.ComponentServer.EmitObject(ModelViewId), new PointF(at.X + 420, at.Y));
        results.Params.Input[0].AddSource(run.Params.Output[0]);
        SetWidget(results, "_ckExtruded", "Active", false);
        SetWidget(results, "_ckReleases", "Active", false);
        ShowResult(results, 6, 6);
        var stress = Unit(results, 6, 3);
        stress.GetType().GetProperty("Value").SetValue(stress, stressUnit);
        var legend = AddLegend(results, title);
        var watch = Stopwatch.StartNew();
        doc.NewSolution(false);
        Messages(run);
        Messages(results);
        Log($"  {topology.NickName} analysed in {watch.Elapsed.TotalSeconds:F1} s: " +
            string.Join(" | ", Values<GH_String>(Out(results, "Info")).Select(v => v.Value)) +
            "; legend " + string.Join(" ", Values<GH_Number>(Out(results, "Values")).Select(v => v.Value.ToString("G3", Inv))));
        return (run, results, legend);
    }

    static List<(string, Func<bool>)> Bridge()
    {
        var eye = new Vector3d(0.75, -1.25, 0.55);
        BoundingBox frame = BoundingBox.Empty;
        return new List<(string, Func<bool>)>
        {
            Do("open", () =>
            {
                Open("TopologyBridge.gh");
                topology = Components(TopologyId).First();
                ((IGH_PreviewObject)topology).Hidden = true;
                surfacePreview = Preview(Welded(Out(topology, "Surface"), Beside(topology, 120, 160)), LightBlue, Beside(topology, 300, 160));
                modelView = Symbols(topology.Params.Input[0].Sources[0], Beside(topology, 120, 300), 1, 1);
                Find<GH_BooleanToggle>("Run").Value = true;
                Find<GH_BooleanToggle>("Run").ExpireSolution(true);
            }),
            Until("run", Finished(() => topology)),
            Do("design", () =>
            {
                Messages(topology);
                ShowOnly(surfacePreview, modelView);
                frame = Drawn();
                Log($"  drawn {frame}");
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "bridge");
                Shoot(Aim(frame, new Vector3d(-0.2, -1, 0.12), Vector3d.ZAxis), "bridge-front");
                Shoot(Aim(frame, new Vector3d(0.5, -0.8, -0.35), Vector3d.ZAxis), "bridge-below");
                Canvas("bridge-canvas");
                Loads(modelView, false);
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "bridge-clean");
                Shoot(Aim(frame, new Vector3d(-0.2, -1, 0.12), Vector3d.ZAxis), "bridge-front-clean");
            }),
            Do("iterations", () =>
            {
                Iterations(topology, "Step", Aim(frame, eye, Vector3d.ZAxis), "bridge-frames");
                Loads(modelView, true);
            }),
            Do("analysed", () =>
            {
                var run = Components(RunAnalysisId).First();
                var results = ResultViews().First();
                var legend = Components(LegendId).First();
                run.Locked = false;
                legend.Locked = false;
                run.ExpireSolution(false);
                doc.NewSolution(false);
                Messages(run);
                Messages(results);
                ShowOnly(results, legend, modelView);
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "bridge-analysed");
                Canvas("bridge-analysed-canvas");
                Loads(modelView, false);
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "bridge-analysed-clean");
            }),
            Do("close", Close),
        };
    }

    static List<(string, Func<bool>)> Benchmarks()
    {
        IGH_Component cantilever = null, simple = null, pc = null, ps = null, vc = null, vs = null;
        var eye = new Vector3d(0.75, -1.25, 0.55);
        // Each design with its supports and loads: the frame of its run and of its analysis too.
        BoundingBox fc = BoundingBox.Empty, fs = BoundingBox.Empty;
        return new List<(string, Func<bool>)>
        {
            Do("open", () =>
            {
                Open("TopologyBenchmarks.gh");
                cantilever = Components(TopologyId).First(c => c.NickName == "Cantilever");
                simple = Components(TopologyId).First(c => c.NickName != "Cantilever");
                foreach (var t in new[] { cantilever, simple }) ((IGH_PreviewObject)t).Hidden = true;
                pc = Preview(Welded(Out(cantilever, "Surface"), Beside(cantilever, 120, 0)), LightBlue, Beside(cantilever, 300, 0));
                ps = Preview(Welded(Out(simple, "Surface"), Beside(simple, 120, 0)), LightBlue, Beside(simple, 300, 0));
                vc = Symbols(cantilever.Params.Input[0].Sources[0], Beside(cantilever, 120, 300), 1, 3);
                vs = Symbols(simple.Params.Input[0].Sources[0], Beside(simple, 120, 300), 1, 4);
                foreach (var t in doc.Objects.OfType<GH_BooleanToggle>().Where(t => t.NickName.StartsWith("Run")))
                {
                    t.Value = true;
                    t.ExpireSolution(true);
                }
            }),
            Until("cantilever", Finished(() => cantilever)),
            Until("simply supported", Finished(() => simple)),
            Do("pictures", () =>
            {
                // With a margin: the end supports' symbols reach past what Model View reports.
                ShowOnly(pc, vc);
                fc = Grown(Drawn(), 0.03);
                Shoot(Aim(fc, eye, Vector3d.ZAxis), "cantilever");
                ShowOnly(ps, vs);
                fs = Grown(Drawn(), 0.03);
                Shoot(Aim(fs, eye, Vector3d.ZAxis), "simply-supported");
                Canvas("benchmarks-canvas");
                ShowOnly(pc);
                Shoot(Aim(Drawn(), eye, Vector3d.ZAxis), "cantilever-clean");
                ShowOnly(ps);
                Shoot(Aim(Drawn(), eye, Vector3d.ZAxis), "simply-supported-clean");
            }),
            // 200 iterations each: every 4th past the 40th.
            Do("cantilever iterations", () =>
            {
                Loads(vc, false);
                ShowOnly(pc, vc);
                Iterations(cantilever, "Step Cantilever", Aim(fc, eye, Vector3d.ZAxis), "cantilever-frames", 4);
                Loads(vc, true);
            }),
            Do("simply supported iterations", () =>
            {
                Loads(vs, false);
                ShowOnly(ps, vs);
                Iterations(simple, "Step " + simple.NickName, Aim(fs, eye, Vector3d.ZAxis), "simply-supported-frames", 4);
                Loads(vs, true);
            }),
            Do("analysed", () =>
            {
                // Two Run Analysis in one definition write the same AlpacaResults files, so the two
                // designs are analysed one at a time. top3d's problems have no units: the stresses
                // in the model's own.
                var (runC, rc, lc) = Analyse(cantilever, "Von Mises stress", 0, Beside(cantilever, 120, 480));
                ShowOnly(rc, lc, vc);
                Shoot(Aim(fc, eye, Vector3d.ZAxis), "cantilever-analysed");
                Loads(vc, false);
                Shoot(Aim(fc, eye, Vector3d.ZAxis), "cantilever-analysed-clean");
                runC.Locked = true;
                var (runS, rs, ls) = Analyse(simple, "Von Mises stress", 0, Beside(simple, 120, 480));
                ShowOnly(rs, ls, vs);
                Shoot(Aim(fs, eye, Vector3d.ZAxis), "simply-supported-analysed");
                Loads(vs, false);
                Shoot(Aim(fs, eye, Vector3d.ZAxis), "simply-supported-analysed-clean");
                // Both on the canvas, as a definition would have them.
                runC.Locked = false;
                doc.NewSolution(false);
                Canvas("benchmarks-analysed-canvas", 1.0f);
            }),
            Do("close", Close),
        };
    }


    // A cube of 16 x 16 x 16 bricks on four corners, loaded at the middle of its top, optimised to 8%.
    static List<(string, Func<bool>)> Tower()
    {
        var eye = new Vector3d(1, -1.35, 0.6);
        BoundingBox frame = BoundingBox.Empty;
        return new List<(string, Func<bool>)>
        {
            Do("open", () =>
            {
                Open(here + "TopologyTower.gh");
                topology = Components(TopologyId).First();
                ((IGH_PreviewObject)topology).Hidden = true;
                surfacePreview = Preview(Welded(Out(topology, "Surface"), Beside(topology, 120, 160)), LightBlue, Beside(topology, 300, 160));
                modelView = Symbols(topology.Params.Input[0].Sources[0], Beside(topology, 120, 300), 1, 2);
                Find<GH_BooleanToggle>("Run").Value = true;
                Find<GH_BooleanToggle>("Run").ExpireSolution(true);
            }),
            Until("run", Finished(() => topology)),
            Do("design", () =>
            {
                Messages(topology);
                ShowOnly(surfacePreview, modelView);
                frame = Drawn();
                Log($"  drawn {frame}");
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "tower");
                Shoot(Aim(frame, new Vector3d(1, -0.4, -0.45), Vector3d.ZAxis), "tower-below");
                Canvas("tower-canvas");
                Loads(modelView, false);
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "tower-clean");
            }),
            Do("iterations", () => Iterations(topology, "Step", Aim(frame, eye, Vector3d.ZAxis), "tower-frames")),
            Do("analysed", () =>
            {
                // top3d's units, as the benchmarks: the stresses in the model's own.
                var (_, results, legend) = Analyse(topology, "Von Mises stress", 0, Beside(topology, 120, 480));
                Loads(modelView, true);
                ShowOnly(results, legend, modelView);
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "tower-analysed");
                Canvas("tower-analysed-canvas");
                Loads(modelView, false);
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "tower-analysed-clean");
            }),
            Do("close", Close),
        };
    }

    /// <summary>
    /// The bridge's design domain before any optimisation, solid, analysed as it is, with the stress lines
    /// through its middle plane: the arch the optimisation finds is already in them.
    /// </summary>
    static List<(string, Func<bool>)> BridgeLines() => new List<(string, Func<bool>)>
    {
        Do("open", () => Open("TopologyBridge.gh")),
        Do("lines", () =>
        {
            topology = Components(TopologyId).First();
            var model = topology.Params.Input[0].Sources[0];
            var at = new PointF(topology.Attributes.Bounds.Left, topology.Attributes.Bounds.Bottom + 420);
            var settings = Place((IGH_Component)Instances.ComponentServer.EmitObject(AnalysisSettingsId), at);
            var run = Place((IGH_Component)Instances.ComponentServer.EmitObject(RunAnalysisId), new PointF(at.X + 200, at.Y - 20));
            run.Params.Input[0].AddSource(model);
            run.Params.Input[1].AddSource(settings.Params.Output[0]);
            var watch = Stopwatch.StartNew();
            doc.NewSolution(false);
            Messages(run);
            Log($"  solved in {watch.Elapsed.TotalSeconds:F1} s");
            // The domain's box, from the full design at step 0.
            Slider("Step", 0);
            doc.NewSolution(false);
            var box = BoundingBox.Empty;
            var meshToBrick = new Guid("95F0E8F1-33B7-4A2D-ABAE-693DAA6AA8B9");
            foreach (var c in doc.Objects.OfType<IGH_Component>().Where(c => c.ComponentGuid == meshToBrick))
                foreach (var m in Values<GH_Mesh>(c.Params.Input[0]))
                    box.Union(m.Value.GetBoundingBox(true));
            Log($"  domain {box}");
            double yMid = 0.5 * (box.Min.Y + box.Max.Y);
            var seeds = new List<GH_Point>();
            for (double x = box.Min.X + 0.25; x < box.Max.X; x += 0.5)
                for (double z = box.Min.Z + 0.25; z < box.Max.Z; z += 0.5)
                    seeds.Add(new GH_Point(new Point3d(x, yMid, z)));
            var lines = StressLines(run.Params.Output[0], seeds, false, 0.33, 0.165, new PointF(at.X + 420, at.Y));
            var outline = Place(new Param_Mesh { NickName = "Domain" }, new PointF(at.X + 420, at.Y + 260));
            outline.PersistentData.Append(new GH_Mesh(Mesh.CreateFromBox(box, 1, 1, 1)));
            var right = lines.Attributes.Bounds.Right + 60;
            var p1 = Preview(Out(lines, "SL1"), Purple, new PointF(right, at.Y - 60));
            var p3 = Preview(Out(lines, "SL3"), LightBlue, new PointF(right, at.Y + 60));
            var pd = Preview(outline, Color.FromArgb(24, DarkTech), new PointF(right, at.Y + 200));
            var symbols = Symbols(model, new PointF(right, at.Y + 330), 1, 1);
            Slider("Step", 300);
            watch.Restart();
            doc.NewSolution(false);
            Messages(lines);
            Log($"  stress lines in {watch.Elapsed.TotalSeconds:F1} s: {Values<GH_Curve>(Out(lines, "SL1")).Count} + {Values<GH_Curve>(Out(lines, "SL3")).Count}");
            ShowOnly(p1, p3, pd, symbols);
            var front = View("Front");
            front.ActiveViewport.ZoomBoundingBox(Grown(box, 0.04));
            Shoot(front, "bridge-stress-lines-front");
            Shoot(Aim(box, new Vector3d(0.75, -1.25, 0.55), Vector3d.ZAxis), "bridge-stress-lines");
            Canvas("bridge-stress-lines-canvas", 1.0f);
        }),
        Do("close", Close),
    };

    // ------------------------------------------------------------------ section optimiser

    // TwoSpanBeamOptimiser: a curved beam 30 m long over two spans, a load on each, sized by the Section
    // Optimiser from a list of HEAs element by element. A picture of each thing the definition draws, on
    // its own: the sections with the loads, the deformed shape extruded, the bending moment and the
    // shear; the beam deflecting, as frames; then one section for the whole beam.
    static List<(string, Func<bool>)> TwoSpan()
    {
        IGH_Component sections = null, tags = null, deformed = null, legend = null, moment = null, shear = null, supports = null;
        var eye = new Vector3d(0.45, -1.6, 0.4);
        return new List<(string, Func<bool>)>
        {
            Do("open", () => Open("TwoSpanBeamOptimiser.gh")),
            Do("sections", () =>
            {
                Sized();
                sections = Components(ModelViewId).First(c => !DrawsResults(c));
                tags = Find<IGH_Component>("HEA");
                deformed = Find<IGH_Component>("Deformed shape");
                legend = Components(LegendId).First();
                moment = Find<IGH_Component>("Bending moment");
                shear = Find<IGH_Component>("Shear");
                Canvas("two-span-beam-canvas");
                ShowOnly(sections, tags);
                var box = Drawn();
                Shoot(Aim(Grown(box, 0.05), eye, Vector3d.ZAxis), "two-span-beam-sections");
                Shoot(Front(box), "two-span-beam-sections-front");
            }),
            Do("deflection", () =>
            {
                // The supports alone, from a Model View that draws no elements.
                supports = Symbols(Out(Find<IGH_Component>("Whole beam"), "AlpacaModel"), Beside(deformed), 0, 1);
                doc.NewSolution(false);
                foreach (var c in new[] { deformed, moment, shear }) Messages(c);
                Log("  " + Info(deformed));
                ShowOnly(deformed, legend, supports);
                var box = Drawn();
                Shoot(Aim(Grown(box, 0.05), eye, Vector3d.ZAxis), "two-span-beam-deflection");
                Shoot(Front(box), "two-span-beam-deflection-front");
            }),
            Do("moment and shear", () =>
            {
                // The diagrams' own colours, Alpaca4d.Gh's Palette: one for each sign of the force.
                var diagrams = new[]
                {
                    (moment, "two-span-beam-moment", "Bending moment", Color.FromArgb(175, 201, 48), Color.FromArgb(158, 53, 218)),
                    (shear, "two-span-beam-shear", "Shear", Color.FromArgb(66, 136, 247), Color.FromArgb(235, 108, 63)),
                };
                foreach (var (results, name, title, negative, positive) in diagrams)
                {
                    Log("  " + Info(results));
                    var key = DiagramLegend(results, title, negative, positive);
                    doc.NewSolution(false);
                    ShowOnly(results, supports, key);
                    Shoot(Front(Drawn()), name);
                }
            }),
            Do("deflecting", () =>
            {
                ShowOnly(deformed, legend, supports);
                Deforming(deformed, eye, "two-span-beam-deflecting-frames");
            }),
            Do("uniform", () =>
            {
                var uniform = Find<GH_BooleanToggle>("Uniform");
                uniform.Value = true;
                uniform.ExpireSolution(false);
                doc.NewSolution(false);
                Sized();
                ShowOnly(sections, tags);
                Shoot(Front(Drawn()), "two-span-beam-uniform-front");
            }),
            Do("close", Close),
        };
    }

    // CurvedBeamOptimiser: a 12 m beam curving in a vertical plane, built in at its start, held up at two
    // more points and cantilevering to a point load, sized element by element within the IPEs: the sized
    // beam, a colour for each section, from above at an angle and from the front.
    static List<(string, Func<bool>)> CurvedBeam()
    {
        var eye = new Vector3d(0.45, -1.6, 0.4);
        return new List<(string, Func<bool>)>
        {
            Do("open", () => Open("CurvedBeamOptimiser.gh")),
            Do("sections", () =>
            {
                Messages(Find<IGH_Component>("Each element"));
                Messages(Find<IGH_Component>("Whole beam"));
                var view = Components(ModelViewId).First();
                Messages(view);
                Canvas("curved-beam-canvas");
                ShowOnly(view);
                var box = Drawn();
                Shoot(Aim(Grown(box, 0.05), eye, Vector3d.ZAxis), "curved-beam-sections");
                Shoot(Front(box), "curved-beam-sections-front");
            }),
            Do("close", Close),
        };
    }

    // BeamGridOptimiser: a 20 m square grid of beams on four pinned supports, every element sized on its
    // own within its family: the sized grid, a colour for each section, and its displacement with the
    // Legend, both from above a corner.
    static List<(string, Func<bool>)> BeamGrid()
    {
        IGH_Component sections = null;
        var eye = new Vector3d(-1.0, -1.35, 0.95);
        return new List<(string, Func<bool>)>
        {
            Do("open", () => Open("BeamGridOptimiser.gh")),
            Do("sections", () =>
            {
                Messages(Find<IGH_Component>("Every element"));
                sections = Components(ModelViewId).First(c => !DrawsResults(c));
                Messages(sections);
                Canvas("beam-grid-canvas");
                ShowOnly(sections);
                Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), "beam-grid-sections");
            }),
            Do("displacement", () =>
            {
                var displacement = Find<IGH_Component>("Displacement");
                var legend = Components(LegendId).First();
                Messages(displacement);
                Log("  " + Info(displacement));
                ShowOnly(displacement, legend);
                Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), "beam-grid-displacement");
            }),
            Do("close", Close),
        };
    }

    // ------------------------------------------------------------------ the docs' how-to guides

    // HowToPictures.gh, here: four small models, each with a Model View of its own - the six kinds of
    // support with their names, a portal frame with its beam pinned at both ends, a box of shell walls
    // under wind, a portal frame's first buckling mode - each pictured alone, for its guide.
    static List<(string, Func<bool>)> HowTo()
    {
        (string view, string[] also, string name, Vector3d eye)[] shots =
        {
            ("Supports view", new[] { "Support names" }, "supports", new Vector3d(0.12, -1.0, 0.22)),
            ("Releases view", new string[0], "releases", new Vector3d(0.35, -1.0, 0.3)),
            ("Wind view", new string[0], "wind", new Vector3d(-1.0, -1.25, 0.65)),
            ("Buckling view", new[] { "Buckling at rest" }, "buckling", new Vector3d(0.35, -1.0, 0.3)),
            ("Every load view", new string[0], "loads", new Vector3d(-0.6, -1.0, 0.75)),
            ("Own weight view", new string[0], "own-weight", new Vector3d(-0.6, -1.0, 0.75)),
        };
        var list = new List<(string, Func<bool>)>
        {
            Do("open", () =>
            {
                Open(Path.Combine(here, "HowToPictures.gh"));
                // Every warning and error, to know the models are what the pictures say.
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                    Messages(c);
                Log("  load factors " + string.Join(", ", Values<GH_Number>(Out(Find<IGH_Component>("Linear Buckling"), "Load Factors")).Select(n => n.Value.ToString("G4", Inv))));
            }),
        };
        foreach (var (view, also, name, eye) in shots)
            list.Add(Do(name, () =>
            {
                var shown = new[] { Find<IGH_Component>(view) }.Concat(also.Select(n => Find<IGH_Component>(n))).ToArray();
                ShowOnly(shown);
                // Solved again, or Grasshopper's preview still draws what was shown before.
                doc.NewSolution(false);
                Shoot(Aim(Grown(Drawn(), 0.08), eye, Vector3d.ZAxis), name);
            }));
        list.Add(Do("close", Close));
        return list;
    }

    static RhinoView Front(BoundingBox box)
    {
        var view = View("Front");
        view.ActiveViewport.ZoomBoundingBox(Grown(box, 0.02));
        return view;
    }

    /// <summary>What a Model View says it draws: the result, its unit and its range.</summary>
    static string Info(IGH_Component results) => Values<GH_String>(Out(results, "Info")).Select(v => v.Value).FirstOrDefault() ?? "";

    /// <summary>
    /// A Legend for a beam force diagram, titled with the force and its unit from Model View's Info:
    /// the colour of each sign beside the end of the range it reaches.
    /// </summary>
    static IGH_Component DiagramLegend(IGH_Component results, string title, Color negative, Color positive)
    {
        // "Beam forces - Mz [kN·m], step 0, min -523.5, max 324.2"
        var parts = Info(results).Split(new[] { ", " }, StringSplitOptions.None);
        string quantity = parts[0].Substring(parts[0].IndexOf(" - ") + 3);
        double min = double.Parse(parts[2].Substring(4), Inv), max = double.Parse(parts[3].Substring(4), Inv);
        var legend = Place((IGH_Component)Instances.ComponentServer.EmitObject(LegendId), Beside(results, 120, 0));
        ((GH_PersistentParam<GH_String>)In(legend, "Title")).PersistentData.Append(new GH_String(title + " " + quantity));
        var values = (GH_PersistentParam<GH_Number>)In(legend, "Values");
        values.PersistentData.Append(new GH_Number(min));
        values.PersistentData.Append(new GH_Number(max));
        var colours = (GH_PersistentParam<GH_Colour>)In(legend, "Colors");
        colours.PersistentData.Append(new GH_Colour(negative));
        colours.PersistentData.Append(new GH_Colour(positive));
        legend.ExpireSolution(false);
        return legend;
    }

    /// <summary>Logs the sections the Section Optimiser gave the beam, start to end, and its messages.</summary>
    static void Sized()
    {
        var optimiser = Find<IGH_Component>("Whole beam");
        Messages(optimiser);
        foreach (var view in Components(ModelViewId)) Messages(view);
        var runs = new List<(string name, int count)>();
        foreach (var section in Values<IGH_Goo>(Out(optimiser, "Section")).Select(g => (g as GH_ObjectWrapper)?.Value?.ToString() ?? g.ToString()))
            if (runs.Count > 0 && runs[runs.Count - 1].name == section) runs[runs.Count - 1] = (section, runs[runs.Count - 1].count + 1);
            else runs.Add((section, 1));
        var utilisation = Values<GH_Number>(Out(optimiser, "Utilisation")).Select(n => n.Value).DefaultIfEmpty(0).Max();
        Log($"  {string.Join(", ", runs.Select(r => $"{r.name} x{r.count}"))}; largest utilisation {utilisation:F3}");
    }

    // ------------------------------------------------------------------ conventional structures

    /// <summary>
    /// The six conventional structures of Tools/Examples/Structures.py, one after another: each one's
    /// canvas, its loads, its deformed shape over the structure at rest and the GIF of it deforming -
    /// and the tower's first mode as well.
    /// </summary>
    /// <summary>
    /// How much larger than Width x Height a still with a Legend can be captured: past a size Rhino
    /// captures in tiles, and the Legend, drawn on the screen, comes out in each of them.
    /// </summary>
    const double WithLegend = 1.6;

    static List<(string, Func<bool>)> Structures()
    {
        (string file, string slug, string title, string deformed, Vector3d eye, double turn)[] all =
        {
            ("TrussRoofHall.gh", "truss-roof-hall", "Truss roof hall", "Hall deformed", new Vector3d(-1.0, -1.3, 0.8), 20),
            ("SteelTower.gh", "steel-tower", "Steel tower", "Tower deformed", new Vector3d(-1.0, -1.25, 0.4), 30),
            ("RoofCanopy.gh", "roof-canopy", "Roof canopy", "Canopy deformed", new Vector3d(-1.0, -1.4, 0.6), 20),
            ("VerticalTrussFacade.gh", "vertical-truss-facade", "Vertical truss facade", "Facade deformed", new Vector3d(1.0, 0.9, 0.35), 0),
            ("BarrelVault.gh", "barrel-vault", "Barrel vault", "Vault deformed", new Vector3d(-1.0, -1.3, 0.7), 20),
            ("HorizontalTrussFacade.gh", "horizontal-truss-facade", "Horizontal truss facade", "Wind truss deformed", new Vector3d(-0.7, 1.0, 0.9), 0),
        };
        var list = new List<(string, Func<bool>)>();
        foreach (var (file, slug, title, name, eye, turn) in all)
        {
            list.Add(Do(slug, () =>
            {
                Open(file);
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                    Messages(c);
                var deformed = Find<IGH_Component>(name);
                var legend = Find<IGH_Component>(name + " legend");
                // A shell's at rest would hide its deformed shape: the vault has none.
                var rest = doc.Objects.OfType<IGH_Component>().FirstOrDefault(c => c.NickName == title + " at rest");
                var loads = Find<IGH_Component>(title + " loads");
                Log("  " + Info(deformed));
                Canvas(slug + "-canvas");

                ShowOnly(loads);
                doc.NewSolution(false);
                Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), slug + "-loads", 2);

                var shown = new IGH_DocumentObject[] { deformed, legend, rest }.Where(o => o != null).ToArray();
                ShowOnly(shown);
                doc.NewSolution(false);
                Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), slug + "-deformed", WithLegend);
                Deforming(deformed, eye, slug + "-deforming-frames", 40, turn);

                var mode = doc.Objects.OfType<IGH_Component>().FirstOrDefault(c => c.NickName.EndsWith("first mode"));
                if (mode != null)
                {
                    var modes = doc.Objects.OfType<IGH_Component>().First(c => c.Name.StartsWith("Natural Vibration"));
                    Log("  frequencies " + string.Join(", ", Values<GH_Number>(Out(modes, "Frequencies")).Select(n => n.Value.ToString("G4", Inv))) + " Hz");
                    ShowOnly(new IGH_DocumentObject[] { mode, Find<IGH_Component>(mode.NickName + " legend"), rest }.Where(o => o != null).ToArray());
                    doc.NewSolution(false);
                    Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), slug + "-mode", WithLegend);
                    Deforming(mode, eye, slug + "-mode-frames", 40, turn);
                }
                Close();
            }));
        }
        return list;
    }

    // ------------------------------------------------------------------ earthquakes

    /// <summary>
    /// The five structures of Tools/Examples/Structures.py shaken by El Centro: each one's canvas, the
    /// still at the step it moves the most, with its charts on their own, and its frames through the
    /// record, a frame every five steps - 0.1 s, so that ten frames a second play it as it happened -
    /// each with its charts beside it in a folder of their own: the ground's acceleration, and how the
    /// points the geometry watches move along the shaking (for the city, every building's roof).
    /// </summary>
    static List<(string, Func<bool>)> Earthquakes()
    {
        // eye: where the camera looks from; magnify: how much closer than the box of what is drawn.
        (string file, string slug, string title, string view, Vector3d eye, double magnify, int along, string[] watched)[] all =
        {
            ("GirderBridgeEarthquake.gh", "girder-bridge-earthquake", "Girder bridge", "Girder shaking", new Vector3d(-1.4, -0.9, 0.6), 1.4, 1, new[] { "deck at mid-span" }),
            ("CableStayedBridgeEarthquake.gh", "cable-stayed-bridge-earthquake", "Cable-stayed bridge", "Cable-stayed shaking", new Vector3d(-0.9, -1.3, 0.5), 1.25, 1, new[] { "deck at mid-span", "top of the pylon" }),
            ("ArchBridgeEarthquake.gh", "arch-bridge-earthquake", "Arch bridge", "Arch shaking", new Vector3d(-1.3, -0.9, 0.5), 1.4, 1, new[] { "deck at the crown" }),
            ("BuildingEarthquake.gh", "building-earthquake", "Office building", "Building shaking", new Vector3d(0.9, -1.3, 0.6), 1.3, 0, new[] { "roof" }),
            ("EarthquakeCity.gh", "earthquake-city", "Tiny city", "City shaking", new Vector3d(0.8, -1.3, 0.75), 1.5, 0, null),
        };
        var list = new List<(string, Func<bool>)>();
        foreach (var (file, slug, title, name, eye, magnify, along, watched) in all)
        {
            list.Add(Do(slug, () =>
            {
                Open(file);
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                    Messages(c);
                var view = Find<IGH_Component>(name);
                var legend = Find<IGH_Component>(name + " legend");
                var rest = doc.Objects.OfType<IGH_Component>().FirstOrDefault(c => c.NickName == title + " at rest");
                var step = doc.Objects.OfType<GH_NumberSlider>().First(x => x.NickName == "Step");
                var record = Find<IGH_Component>("El Centro 1940");
                int steps = Values<GH_Integer>(Out(record, "Steps")).Select(n => n.Value).FirstOrDefault();
                Canvas(slug + "-canvas");

                // What the charts plot: the record, and the watched points' movement every step.
                var geometry = Find<IGH_Component>(title);
                var times = Values<GH_Number>(Out(record, "Times")).Select(n => n.Value).ToArray();
                var quake = new Quake
                {
                    Times = times,
                    Ground = Values<GH_Number>(Out(record, "Accelerations")).Select(n => n.Value / 9.81).ToArray(),
                    Along = "xyz"[along],
                    Labels = watched,
                    Dt = times.Length > 1 ? times[1] - times[0] : 0.02,
                    Moves = Histories(Values<GH_Point>(Out(geometry, "Watch")).Select(p => p.Value).ToList(), along),
                };
                if (watched == null)
                    quake.Storeys = Values<IGH_Goo>(Out(geometry, "Storeys")).Select(v => Convert.ToInt32(v.ScriptVariable(), Inv)).ToArray();
                // Model View's step k is the history's k-th; one fewer of them than the record's samples
                // means the first is the end of the first step.
                quake.Offset = quake.Moves[0].Length > steps ? 0 : 1;
                Log($"  {quake.Moves.Count} watched, {quake.Moves[0].Length} steps; the most along {quake.Along}, " +
                    string.Join(", ", quake.Moves.Select(m => m.Max(Math.Abs).ToString("G4", Inv))) + " mm");

                var shown = new IGH_DocumentObject[] { view, legend, rest }.Where(o => o != null).ToArray();
                ShowOnly(shown);
                // The step it moves the most, every fifth looked at. Info gives the Range when one is
                // set, not what is drawn: it is taken off for the search and put back after.
                var range = (GH_PersistentParam<GH_Interval>)In(view, "Range");
                var colours = range.PersistentData.AllData(true).OfType<GH_Interval>().FirstOrDefault();
                range.PersistentData.Clear();
                range.ExpireSolution(false);
                int worst = 0;
                double most = -1.0;
                for (int k = 0; k <= steps; k += 5)
                {
                    double largest = AtStep(step, k, view);
                    if (largest > most) { most = largest; worst = k; }
                }
                if (colours != null) range.PersistentData.Append(colours);
                range.ExpireSolution(false);
                Log($"  {steps} steps; the most, {most:G4} mm, at step {worst}, {quake.Time(worst):F2} s");
                AtStep(step, worst, view);
                var eyeView = Aim(Grown(Drawn(), 0.08), eye, Vector3d.ZAxis, magnify: magnify);
                Shoot(eyeView, slug, WithLegend);
                Charts(Path.Combine(outDir, slug + "-chart.png"), quake, worst, WithLegend);

                // The legend is made large enough for the still, captured larger than the screen; the
                // frames, at the screen's size, take it smaller.
                var legendScale = (GH_PersistentParam<GH_Number>)In(legend, "Scale");
                legendScale.PersistentData.Clear();
                legendScale.PersistentData.Append(new GH_Number(1.6));
                legendScale.ExpireSolution(false);

                Directory.CreateDirectory(Path.Combine(outDir, slug + "-frames"));
                Directory.CreateDirectory(Path.Combine(outDir, slug + "-charts"));
                int frame = 0;
                for (int k = 0; k <= steps; k += 5, frame++)
                {
                    AtStep(step, k, view);
                    Shoot(eyeView, Path.Combine(slug + "-frames", frame.ToString("D3")));
                    Charts(Path.Combine(outDir, slug + "-charts", frame.ToString("D3") + ".png"), quake, k, 1);
                }
                Log($"  {frame} frames and their charts, 0 to {steps * 0.02:F1} s, a frame every 0.1 s");
                Close();
            }));
        }
        return list;
    }

    /// <summary>An earthquake's charts: the record, and how the watched points move.</summary>
    class Quake
    {
        /// <summary>The record: its times, s, and the ground's acceleration then, g.</summary>
        public double[] Times, Ground;
        /// <summary>Each watched point's movement along the shaking, mm, a value a recorded step.</summary>
        public List<double[]> Moves;
        /// <summary>What each watched point is; none for the city, whose bars Storeys labels.</summary>
        public string[] Labels;
        public int[] Storeys;
        public char Along;
        public double Dt;
        public int Offset;
        public double Time(int k) => (k + Offset) * Dt;
    }

    /// <summary>
    /// The movement along axis (0 x, 1 y, 2 z), in mm, of the nodes nearest to points, every recorded
    /// step: read by a Nodal Displacements of the scene's own on Run Analysis's model, first unfiltered
    /// for where the nodes are, then for those nodes' history; taken out again after.
    /// </summary>
    static List<double[]> Histories(List<Point3d> points, int axis)
    {
        var run = doc.Objects.OfType<IGH_Component>().First(c => c.ComponentGuid == new Guid("8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452"));
        var nodes = (IGH_Component)Instances.ComponentServer.EmitObject(new Guid("25085EAB-6C66-487E-B83B-BAB5AB02B1BB"));
        nodes.CreateAttributes();
        doc.AddObject(nodes, false);
        try
        {
            nodes.Params.Input[0].AddSource(run.Params.Output[0]);
            doc.NewSolution(false);
            var at = Values<GH_Point>(Out(nodes, "Position")).Select(p => p.Value).ToList();
            var tags = points.Select(p => Enumerable.Range(0, at.Count).OrderBy(i => at[i].DistanceToSquared(p)).First() + 1).ToList();
            var history = (GH_PersistentParam<GH_Boolean>)In(nodes, "History");
            history.PersistentData.Clear();
            history.PersistentData.Append(new GH_Boolean(true));
            var filter = (GH_PersistentParam<GH_Integer>)In(nodes, "NodeTag");
            filter.PersistentData.Clear();
            foreach (int tag in tags) filter.PersistentData.Append(new GH_Integer(tag));
            // An input keeps the data it collected until it is expired itself.
            history.ExpireSolution(false);
            filter.ExpireSolution(false);
            doc.NewSolution(false);
            if (Out(nodes, "Displacement").VolatileData.PathCount < 2) throw new Exception("Nodal Displacements gave no history");
            var tree = Out(nodes, "Displacement").VolatileData;
            var moves = tags.Select(_ => new List<double>()).ToList();
            foreach (var path in tree.Paths)
            {
                var branch = tree.get_Branch(path);
                for (int i = 0; i < moves.Count && i < branch.Count; i++)
                    moves[i].Add(((GH_Vector)branch[i]).Value[axis] * 1000.0);
            }
            return moves.Select(m => m.ToArray()).ToList();
        }
        finally { doc.RemoveObject(nodes, false); }
    }

    const string ChartFont = "Helvetica Neue";
    static readonly Color ChartFaint = Color.FromArgb(208, 212, 217), ChartAxis = Color.FromArgb(226, 229, 232);
    static readonly Color[] ChartLines = { LightBlue, Purple };

    /// <summary>
    /// The charts of step k, 2400 px wide times scale: the ground's acceleration over the record and,
    /// under it, how the watched points move - for the city, a bar for each building's roof, lowest to
    /// tallest, against the most it moves either way. Each line is drawn up to now over its whole
    /// course in light grey, with now marked in orange.
    /// </summary>
    static void Charts(string path, Quake q, int k, double scale)
    {
        float s = (float)scale;
        int w = (int)(2400 * s), h = (int)(880 * s);
        int at = Math.Max(0, Math.Min(k, q.Moves[0].Length - 1));
        using (var bitmap = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Background);
            var ground = new RectangleF(220 * s, 90 * s, w - 300 * s, 220 * s);
            TimeChart(g, ground, q.Times, new List<double[]> { q.Ground }, q.Time(at), "Ground acceleration [g]",
                      new[] { DarkTech }, null, "g", "0.0#", s, q.Storeys != null);
            var below = new RectangleF(220 * s, 470 * s, w - 300 * s, 320 * s);
            if (q.Storeys == null)
            {
                var times = Enumerable.Range(0, q.Moves[0].Length).Select(q.Time).ToArray();
                TimeChart(g, below, times, q.Moves, q.Time(at), $"Displacement along {q.Along} [mm]", ChartLines, q.Labels, "mm", "0", s, true);
            }
            else
                Bars(g, below, q, at, s);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>Series over times in r, symmetric about zero, each drawn up to t; labels name them, top right, with their value now.</summary>
    static void TimeChart(Graphics g, RectangleF r, double[] times, List<double[]> series, double t, string title,
                          Color[] colours, string[] labels, string unit, string format, float s, bool seconds)
    {
        double end = times[times.Length - 1], top = Nice(series.Max(v => v.Max(Math.Abs)) * 1.05);
        Func<double, float> X = x => r.Left + (float)(x / end) * r.Width;
        Func<double, float> Y = y => r.Top + r.Height / 2 - (float)(y / top) * r.Height / 2;
        int now = Math.Max(0, Math.Min(times.Length - 1, (int)Math.Round((t - times[0]) / Math.Max(1e-9, times[1] - times[0]))));
        using (var big = new Font(ChartFont, 42 * s, GraphicsUnit.Pixel))
        using (var small = new Font(ChartFont, 32 * s, GraphicsUnit.Pixel))
        using (var axis = new Pen(ChartAxis, 2 * s))
        using (var ink = new SolidBrush(DarkTech))
        {
            Axes(g, r, top, format, Y, small, axis, ink, s);
            if (seconds)
                for (int second = 0; second <= (int)end; second += 2)
                {
                    var label = second + " s";
                    var size = g.MeasureString(label, small);
                    g.DrawString(label, small, ink, X(second) - size.Width / 2, r.Bottom + 12 * s);
                }
            g.DrawString(title, big, ink, r.Left - 8 * s, r.Top - 78 * s);
            for (int i = 0; i < series.Count; i++)
            {
                var all = times.Select((x, j) => new PointF(X(x), Y(series[i][j]))).ToArray();
                using (var faint = new Pen(ChartFaint, 3 * s)) g.DrawLines(faint, all);
                if (now > 0)
                    using (var pen = new Pen(colours[i % colours.Length], 5 * s) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                        g.DrawLines(pen, all.Take(now + 1).ToArray());
            }
            using (var pen = new Pen(Orange, 3 * s)) g.DrawLine(pen, X(times[now]), r.Top - 8 * s, X(times[now]), r.Bottom + 8 * s);
            for (int i = 0; i < series.Count; i++)
                using (var dot = new SolidBrush(colours[i % colours.Length]))
                    g.FillEllipse(dot, X(times[now]) - 10 * s, Y(series[i][now]) - 10 * s, 20 * s, 20 * s);
            // Top right: each series' name and its value now, or, with no names, the value and the time.
            float right = r.Right;
            for (int i = series.Count - 1; i >= 0; i--)
            {
                var value = series[i][now].ToString(format, Inv) + " " + unit;
                var text = labels == null ? value + "      t = " + times[now].ToString("0.0", Inv) + " s" : labels[i] + "  " + value;
                var size = g.MeasureString(text, big);
                right -= size.Width;
                using (var brush = new SolidBrush(labels == null ? DarkTech : colours[i % colours.Length]))
                    g.DrawString(text, big, brush, right, r.Top - 78 * s);
                right -= 60 * s;
            }
        }
    }

    /// <summary>A bar for each building's roof, lowest to tallest: where it is at step k, over the most it moves either way.</summary>
    static void Bars(Graphics g, RectangleF r, Quake q, int k, float s)
    {
        var order = Enumerable.Range(0, q.Storeys.Length).OrderBy(i => q.Storeys[i]).ThenBy(i => i).ToArray();
        var peak = q.Moves.Select(m => m.Max(Math.Abs)).ToArray();
        double top = Nice(peak.Max() * 1.05);
        Func<double, float> Y = y => r.Top + r.Height / 2 - (float)(y / top) * r.Height / 2;
        float slot = r.Width / order.Length, bar = slot * 0.56f;
        using (var big = new Font(ChartFont, 42 * s, GraphicsUnit.Pixel))
        using (var small = new Font(ChartFont, 32 * s, GraphicsUnit.Pixel))
        using (var axis = new Pen(ChartAxis, 2 * s))
        using (var ink = new SolidBrush(DarkTech))
        using (var faint = new SolidBrush(ChartFaint))
        using (var now = new SolidBrush(LightBlue))
        {
            Axes(g, r, top, "0", Y, small, axis, ink, s);
            g.DrawString($"Roof displacement along {q.Along} [mm], lowest building to tallest", big, ink, r.Left - 8 * s, r.Top - 78 * s);
            var key = "the most it moves";
            var keySize = g.MeasureString(key, big);
            g.FillRectangle(faint, r.Right - keySize.Width - 50 * s, r.Top - 66 * s, 34 * s, 34 * s);
            g.DrawString(key, big, ink, r.Right - keySize.Width, r.Top - 78 * s);
            for (int n = 0; n < order.Length; n++)
            {
                int i = order[n];
                float x = r.Left + slot * (n + 0.5f);
                g.FillRectangle(faint, x - bar / 2, Y(peak[i]), bar, Y(-peak[i]) - Y(peak[i]));
                float y0 = Y(0), y1 = Y(q.Moves[i][Math.Min(k, q.Moves[i].Length - 1)]);
                g.FillRectangle(now, x - bar / 2, Math.Min(y0, y1), bar, Math.Max(1, Math.Abs(y1 - y0)));
                var label = q.Storeys[i].ToString(Inv);
                var size = g.MeasureString(label, small);
                g.DrawString(label, small, ink, x - size.Width / 2, r.Bottom + 12 * s);
            }
            var storeys = g.MeasureString("storeys", small);
            g.DrawString("storeys", small, ink, r.Left - storeys.Width - 14 * s, r.Bottom + 12 * s);
        }
    }

    /// <summary>Zero and the top either side, as lines across r, labelled on the left.</summary>
    static void Axes(Graphics g, RectangleF r, double top, string format, Func<double, float> Y, Font font, Pen pen, Brush ink, float s)
    {
        foreach (double v in new[] { top, 0.0, -top })
        {
            g.DrawLine(pen, r.Left, Y(v), r.Right, Y(v));
            var label = v.ToString(format, Inv);
            var size = g.MeasureString(label, font);
            g.DrawString(label, font, ink, r.Left - size.Width - 14 * s, Y(v) - size.Height / 2);
        }
    }

    /// <summary>x, rounded up to 1, 1.5, 2, 2.5, 3, 4, 5, 6 or 8 times a power of ten.</summary>
    static double Nice(double x)
    {
        if (x <= 0) return 1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(x)));
        foreach (double m in new[] { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8 })
            if (m * p >= x * 0.9999) return m * p;
        return 10 * p;
    }

    /// <summary>Puts the Step slider at <paramref name="k"/>, solves, and gives the largest displacement Model View draws there.</summary>
    static double AtStep(GH_NumberSlider step, int k, IGH_Component view)
    {
        step.SetSliderValue((decimal)k);
        doc.NewSolution(false);
        // "Displacement - Magnitude [mm], step 12, min 0, max 4.996"
        var info = Info(view);
        int at = info.LastIndexOf("max ");
        return at >= 0 && double.TryParse(info.Substring(at + 4), System.Globalization.NumberStyles.Float, Inv, out double v) ? v : 0.0;
    }

    // ------------------------------------------------------------------ stress lines on shells

    static List<(string, Func<bool>)> Lines2d() => new List<(string, Func<bool>)>
    {
        Do("open", () => Open("StressLinesBenchmarks.gh")),
        Do("pictures", () =>
        {
            foreach (var c in Components(StressLinesId)) Messages(c);
            var all = Drawn();
            Shoot(Top(Grown(all, 0.03)), "shells-all");
            var visible = doc.Objects.OfType<IGH_PreviewObject>().Where(p => !p.Hidden).Cast<IGH_DocumentObject>().ToList();
            foreach (var g in doc.Objects.OfType<GH_Group>().Where(g => g.NickName.Length > 2 && g.NickName[1] == '7'))
            {
                var mine = visible.Where(o => g.ObjectIDs.Contains(o.InstanceGuid)).ToArray();
                if (mine.Length == 0) continue;
                ShowOnly(mine);
                Shoot(Top(Grown(Drawn(), 0.04)), "shells-" + char.ToLower(g.NickName[0]));
            }
            ShowOnly(visible.ToArray());
            Canvas("shells-canvas", 1.0f);
            // Kirsch's plate alone: its groups, and what they hold.
            var plate = new HashSet<Guid>();
            foreach (var g in doc.Objects.OfType<GH_Group>().Where(g => g.NickName.StartsWith("C")))
            {
                plate.Add(g.InstanceGuid);
                foreach (var id in g.ObjectIDs) plate.Add(id);
            }
            Canvas("shells-canvas-plate", 1.5f, o => plate.Contains(o.InstanceGuid));
        }),
        Do("close", Close),
    };

    // ------------------------------------------------------------------ stress lines: plates and a form-found shell

    static readonly Guid MeshPlaneId = new Guid("8ADBF481-7589-4A40-B490-006531EA001D");
    static readonly Guid Rectangle3PtId = new Guid("9BC98A1D-2ECC-407E-948A-09A09ED3E69D");
    static readonly Guid AssembleId = new Guid("B6978C42-7B88-4F1A-A5DF-85EF89F154F9");

    /// <summary>The groups whose names start with <paramref name="key"/> and a digit, and what they hold.</summary>
    static HashSet<Guid> Members(char key)
    {
        var ids = new HashSet<Guid>();
        foreach (var g in doc.Objects.OfType<GH_Group>().Where(g => g.NickName.Length > 1 && g.NickName[0] == key && char.IsDigit(g.NickName[1])))
        {
            ids.Add(g.InstanceGuid);
            foreach (var id in g.ObjectIDs) ids.Add(id);
        }
        return ids;
    }

    static IGH_Component Member(HashSet<Guid> ids, Guid type) =>
        doc.Objects.OfType<IGH_Component>().First(c => c.ComponentGuid == type && ids.Contains(c.InstanceGuid));

    /// <summary>What a plate or the shell draws in the pictures: its two families of lines, its surface and Model View's symbols.</summary>
    class Drawn2d
    {
        public IGH_Component Lines, Sl1, Sl2, Surface, Outline, Symbols;
        public IGH_Component[] Clean => new[] { Sl1, Sl2, Surface, Outline }.Where(c => c != null).ToArray();
        public IGH_Component[] All => Clean.Concat(new[] { Symbols }).Where(c => c != null).ToArray();
    }

    /// <summary>
    /// Previews of the stress lines <paramref name="lines"/> traced: SL1 orange, SL2 blue, over
    /// <paramref name="surface"/> in <paramref name="tint"/> and its <paramref name="outline"/>, if
    /// any, in DarkTech. Model View's symbols come later, with <see cref="AddSymbols"/>: after the
    /// canvas pictures, which they would otherwise be in.
    /// </summary>
    static Drawn2d Previews(IGH_Component lines, IGH_Param surface, Color tint, IGH_Param outline, bool matte = false)
    {
        ((IGH_PreviewObject)lines).Hidden = true;
        var at = Beside(lines, 120, 0);
        return new Drawn2d
        {
            Lines = lines,
            Sl1 = Preview(Out(lines, "SL1"), Purple, at),
            Sl2 = Preview(Out(lines, "SL2"), LightBlue, new PointF(at.X, at.Y + 90)),
            Surface = matte ? Matte(surface, tint, new PointF(at.X, at.Y + 180)) : Preview(surface, tint, new PointF(at.X, at.Y + 180)),
            Outline = outline == null ? null : Preview(outline, DarkTech, new PointF(at.X, at.Y + 270)),
        };
    }

    /// <summary>
    /// <see cref="ShowOnly"/>, and what it shows solved again: a preview added and hidden before
    /// Rhino ever drew it stays blank when shown, until it is solved again.
    /// </summary>
    static void Reveal(params IGH_DocumentObject[] shown)
    {
        ShowOnly(shown);
        foreach (var o in shown.OfType<IGH_ActiveObject>()) o.ExpireSolution(false);
        doc.NewSolution(false);
    }

    /// <summary>Model View's supports and loads of <paramref name="model"/>, beside the previews of <paramref name="d"/>.</summary>
    static void AddSymbols(Drawn2d d, IGH_Param model, double loadScale, double supportScale)
    {
        var b = d.Sl1.Attributes.Bounds;
        d.Symbols = Symbols(model, new PointF(b.Right + 80, b.Top), loadScale, supportScale);
        if (loadScale > 0) LoadScale(d.Symbols, loadScale);
    }

    /// <summary>
    /// Model View's load scale, a share of the arrows it fits to the model: below the slider's own
    /// 0.1 too.
    /// </summary>
    static void LoadScale(IGH_Component view, double scale)
    {
        var slider = Field(view, "_slLoadScale");
        slider.GetType().GetProperty("MinValue").SetValue(slider, Math.Min(0.001, scale));
        slider.GetType().GetProperty("Value").SetValue(slider, scale);
        view.ExpireSolution(false);
    }

    /// <summary>What is drawn, grown by <paramref name="f"/> of its diagonal, logged.</summary>
    static BoundingBox Framed(double f)
    {
        var box = Grown(Drawn(), f);
        Log($"  drawn {box.Min} to {box.Max}");
        return box;
    }

    /// <summary>
    /// The plate of StressLinesPlates.gh whose groups start with <paramref name="key"/>, drawn: a
    /// flat one see-through grey with its outline, the curved one, built by Construct Mesh, shaded.
    /// </summary>
    static Drawn2d PlatePreviews(char key)
    {
        var ids = Members(key);
        var curved = doc.Objects.OfType<IGH_Component>().FirstOrDefault(c => c.ComponentGuid == ConstructMeshId && ids.Contains(c.InstanceGuid));
        if (curved != null)
            return Previews(Member(ids, StressLinesId), curved.Params.Output[0], Concrete, null);
        return Previews(Member(ids, StressLinesId), Member(ids, MeshPlaneId).Params.Output[0], Color.FromArgb(26, DarkTech),
                        Member(ids, Rectangle3PtId).Params.Output[0]);
    }

    static readonly Guid ConstructMeshId = new Guid("E2C0F9DB-A862-4BD9-810C-EF2610E7A56F");
    static readonly Guid LoadPatternId = new Guid("C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B");
    static readonly Guid MeshLoadId = new Guid("C8514DEA-1B1B-45A6-ABC1-8004C1EFC617");

    /// <summary>A curved shell's surface, shaded: a light grey, as concrete reads on the background.</summary>
    static readonly Color Concrete = Color.FromArgb(226, 229, 234);

    static IGH_Param PlateModel(char key) => Member(Members(key), AssembleId).Params.Output[0];

    static void LinesLog(IGH_Component lines, Stopwatch watch = null) =>
        Log($"  {lines.NickName}: {Values<GH_Curve>(Out(lines, "SL1")).Count} + {Values<GH_Curve>(Out(lines, "SL2")).Count} lines" +
            (watch == null ? "" : $" in {watch.Elapsed.TotalSeconds:F1} s"));

    /// <summary>
    /// The lines drawn as the tracer grew them, out from the seed, a few more each frame: through two
    /// Curve parameters the previews take their lines from for the while. <paramref name="frames"/>
    /// frames into <paramref name="folder"/>.
    /// </summary>
    static void Growing(Drawn2d d, RhinoView view, string folder, int frames)
    {
        var all = new[] { Values<GH_Curve>(Out(d.Lines, "SL1")), Values<GH_Curve>(Out(d.Lines, "SL2")) };
        var previews = new[] { d.Sl1, d.Sl2 };
        var shown = new Param_Curve[2];
        for (int f = 0; f < 2; f++)
        {
            var b = previews[f].Attributes.Bounds;
            shown[f] = Place(new Param_Curve { NickName = "Drawn so far", Hidden = true }, new PointF(b.Left - 60, b.Top - 30));
            previews[f].Params.Input[0].RemoveAllSources();
            previews[f].Params.Input[0].AddSource(shown[f]);
        }
        Directory.CreateDirectory(Path.Combine(outDir, folder));
        for (int k = 0; k <= frames; k++)
        {
            for (int f = 0; f < 2; f++)
            {
                int n = (int)Math.Round(all[f].Count * (double)k / frames);
                shown[f].PersistentData.Clear();
                shown[f].PersistentData.AppendRange(all[f].Take(n));
                shown[f].ExpireSolution(false);
            }
            doc.NewSolution(false);
            Shoot(view, Path.Combine(folder, k.ToString("D3")));
        }
        for (int f = 0; f < 2; f++)
        {
            previews[f].Params.Input[0].RemoveAllSources();
            previews[f].Params.Input[0].AddSource(Out(d.Lines, f == 0 ? "SL1" : "SL2"));
            doc.RemoveObject(shown[f], false);
        }
        doc.NewSolution(false);
        Log($"  {frames + 1} frames, to {all[0].Count} + {all[1].Count} lines");
    }

    /// <summary>
    /// The camera taken once round <paramref name="box"/> about Z, from along <paramref name="eye"/>
    /// to start with, at the distance that frames it then: <paramref name="frames"/> frames into
    /// <paramref name="folder"/>. Zoomed afresh each frame, the model would breathe in and out.
    /// </summary>
    /// <summary>
    /// A structure deforming: <paramref name="results"/>, a Model View drawing it deformed, swung from
    /// at rest to its full deformation and back, one frame each into <paramref name="folder"/>, for
    /// publish.sh's anim to make a GIF and an MP4 of, looping. Framed from along <paramref name="eye"/>
    /// at full deformation, so that nothing leaves the picture; with <paramref name="turn"/> degrees,
    /// the camera also turns that far about the vertical through the middle, there and back. Show what
    /// else should be in it first: a Legend, the supports, the model at rest. See the
    /// deformation-pictures skill.
    /// </summary>
    static void Deforming(IGH_Component results, Vector3d eye, string folder, int frames = 40, double turn = 0)
    {
        var scale = Field(results, "_slDeformScale");
        var value = scale.GetType().GetProperty("Value");
        double full = Convert.ToDouble(value.GetValue(scale));
        doc.NewSolution(false);
        var view = Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis);
        var vp = view.ActiveViewport;
        var target = vp.CameraTarget;
        var from = vp.CameraLocation - target;
        Directory.CreateDirectory(Path.Combine(outDir, folder));
        for (int k = 0; k < frames; k++)
        {
            double swing = 0.5 * (1 - Math.Cos(2 * Math.PI * k / frames));
            value.SetValue(scale, full * swing);
            results.ExpireSolution(false);
            doc.NewSolution(false);
            if (turn != 0)
            {
                var turned = from;
                turned.Rotate(turn * Math.PI / 180 * swing, Vector3d.ZAxis);
                vp.SetCameraLocations(target, target + turned);
                vp.CameraUp = Vector3d.ZAxis;
            }
            Shoot(view, Path.Combine(folder, k.ToString("D3")));
        }
        value.SetValue(scale, full);
        results.ExpireSolution(false);
        doc.NewSolution(false);
        Log($"  {frames} frames, deformation from 0 to {full} times and back" + (turn != 0 ? $", turning {turn} degrees" : ""));
    }

    static void Turning(BoundingBox box, Vector3d eye, string folder, int frames)
    {
        var view = Aim(box, eye, Vector3d.ZAxis);
        var vp = view.ActiveViewport;
        var target = vp.CameraTarget;
        double distance = vp.CameraLocation.DistanceTo(target);
        var along = eye;
        along.Unitize();
        Directory.CreateDirectory(Path.Combine(outDir, folder));
        for (int k = 0; k < frames; k++)
        {
            var turned = along;
            turned.Rotate(2 * Math.PI * k / frames, Vector3d.ZAxis);
            vp.SetCameraLocations(target, target + turned * distance);
            vp.CameraUp = Vector3d.ZAxis;
            Shoot(view, Path.Combine(folder, k.ToString("D3")));
        }
        Log($"  {frames} frames round");
    }

    /// <summary>
    /// A load walked along its nodes by the slider <paramref name="node"/>, there and back, every
    /// <paramref name="every"/> node from <paramref name="from"/> to <paramref name="to"/>: the model
    /// solved and its lines traced at each, one frame each into <paramref name="folder"/>.
    /// </summary>
    static void Walking(string node, int from, int to, int every, RhinoView view, string folder)
    {
        var slider = Find<GH_NumberSlider>(node);
        double start = (double)slider.Slider.Value;
        var stops = new List<int>();
        for (int i = from; i <= to; i += every) stops.Add(i);
        stops.AddRange(stops.Skip(1).Reverse().Skip(1).ToList());
        Directory.CreateDirectory(Path.Combine(outDir, folder));
        var watch = Stopwatch.StartNew();
        for (int k = 0; k < stops.Count; k++)
        {
            Slider(node, stops[k]);
            doc.NewSolution(false);
            Shoot(view, Path.Combine(folder, k.ToString("D3")));
        }
        Slider(node, start);
        doc.NewSolution(false);
        Log($"  {stops.Count} frames, {node} {from} to {to} and back, in {watch.Elapsed.TotalSeconds:F0} s");
    }

    // StressLinesPlates: a cantilever wall and a deep beam in their own plane, a slab on four edges and
    // a cantilever slab in bending; each alone, with and without its supports and loads; the deep
    // beam's load and a point load on the slab walking along, and the wall's lines growing from their
    // seed, as frames.
    static List<(string, Func<bool>)> Plates()
    {
        var plates = new Dictionary<char, Drawn2d>();
        var slab = new Vector3d(0.55, -1.25, 0.95);
        var canopy = new Vector3d(1, -0.9, 0.55);
        return new List<(string, Func<bool>)>
        {
            Do("open", () =>
            {
                Open(here + "StressLinesPlates.gh");
                foreach (var c in Components(RunAnalysisId)) Messages(c);
                foreach (var c in Components(StressLinesId)) { Messages(c); LinesLog(c); }
            }),
            Do("canvas", () =>
            {
                foreach (var key in new[] { 'A', 'B', 'C', 'D' }) plates[key] = PlatePreviews(key);
                doc.NewSolution(false);
                Canvas("plates-canvas", 1.0f);
                foreach (var key in plates.Keys)
                {
                    var ids = Members(key);
                    Canvas("plate-" + char.ToLower(key) + "-canvas", 1.5f, o => ids.Contains(o.InstanceGuid));
                }
            }),
            Do("each plate", () =>
            {
                // Arrows about a metre long: 8 kN on each node of the wall, 500 kN on the deep
                // beam. The slabs' uniform load is drawn as a sheet over them, hiding the lines:
                // left out.
                AddSymbols(plates['A'], PlateModel('A'), 1, 0.6);
                AddSymbols(plates['B'], PlateModel('B'), 1, 0.8);
                AddSymbols(plates['C'], PlateModel('C'), 0, 0.5);
                AddSymbols(plates['D'], PlateModel('D'), 0, 0.5);
                doc.NewSolution(false);
                foreach (var key in new[] { 'A', 'B' })
                {
                    string name = key == 'A' ? "cantilever-wall" : "deep-beam";
                    Reveal(plates[key].All);
                    Shoot(Front(Framed(0.02)), name);
                    Reveal(plates[key].Clean);
                    Shoot(Front(Framed(0.02)), name + "-clean");
                }
                Reveal(plates['C'].All);
                var box = Framed(0.02);
                Shoot(Aim(box, slab, Vector3d.ZAxis), "slab");
                Reveal(plates['C'].Clean);
                Shoot(Top(Framed(0.03)), "slab-top");
                Shoot(Aim(box, slab, Vector3d.ZAxis), "slab-clean");
                Reveal(plates['D'].All);
                box = Framed(0.02);
                Shoot(Aim(box, canopy, Vector3d.ZAxis), "cantilever-shell");
                Shoot(Aim(box, new Vector3d(1, -0.3, 0.35), Vector3d.ZAxis), "cantilever-shell-tip");
                Reveal(plates['D'].Clean);
                Shoot(Aim(box, canopy, Vector3d.ZAxis), "cantilever-shell-clean");
                Reveal(plates.Values.SelectMany(p => p.All).ToArray());
                Shoot(Aim(Framed(0.02), new Vector3d(0.7, -1.3, 0.6), Vector3d.ZAxis), "all-four");
            }),
            Do("spins and walks", () =>
            {
                // Each plate turning round, and walked past - or under - at eye height.
                var cases = new (char key, string name, Vector3d spin, Point3d from, Point3d to, Vector3d ahead)[]
                {
                    ('A', "cantilever-wall", new Vector3d(0.3, -1, 0.35), new Point3d(-3, -6, 1.25), new Point3d(10, -6, 1.25), new Vector3d(6, 8, 0.2)),
                    ('B', "deep-beam", new Vector3d(0.3, -1, 0.35), new Point3d(8, -8, 1.6), new Point3d(24, -8, 1.6), new Vector3d(5, 9, 0.6)),
                    ('C', "slab", slab, new Point3d(-2, -10, -1.7), new Point3d(14, -10, -1.7), new Vector3d(6, 1.5, 1.2)),
                    ('D', "cantilever-shell", canopy, new Point3d(25, -11, 0.6), new Point3d(13, -11, 0.6), new Vector3d(-6, 0, 0.4)),
                };
                foreach (var c in cases)
                {
                    Reveal(plates[c.key].All);
                    Turning(Framed(0.06), c.spin, c.name + "-spin-frames", 72);
                    Walk(c.from, c.to, c.ahead, 24, c.name + "-walk-through-frames");
                }
            }),
            Do("deep beam, load walking", () =>
            {
                Reveal(plates['B'].All);
                Walking("B load node", 2, 48, 2, Front(Framed(0.02)), "deep-beam-walking-frames");
            }),
            Do("slab, point load walking", () =>
            {
                // A point load alone: the lines turn round wherever it is. The uniform load is
                // unwired from the load pattern rather than set to 0: Model View stops drawing at
                // an area load of 0, before the supports.
                var ids = Members('C');
                var pattern = Member(ids, LoadPatternId).Params.Input[1];
                var uniform = Member(ids, MeshLoadId).Params.Output[0];
                pattern.RemoveSource(uniform);
                pattern.ExpireSolution(false);
                Slider("C point load [kN]", 300);
                LoadScale(plates['C'].Symbols, 1);
                Loads(plates['C'].Symbols, true);
                Reveal(plates['C'].All);
                var view = Aim(Framed(0.02), slab, Vector3d.ZAxis);
                Shoot(view, "slab-point-load");
                Shoot(Top(Framed(0.03)), "slab-point-load-top");
                Walking("C load node", 2, 28, 1, view, "slab-walking-frames");
                Slider("C point load [kN]", 0);
                Loads(plates['C'].Symbols, false);
                pattern.AddSource(uniform);
                pattern.ExpireSolution(true);
            }),
            Do("cantilever wall, growing", () =>
            {
                Reveal(plates['A'].All);
                Growing(plates['A'], Front(Framed(0.02)), "cantilever-wall-growing-frames", 40);
            }),
            Do("close", Close),
        };
    }

    /// <summary>
    /// A Custom Preview of <paramref name="source"/> in a matte <paramref name="colour"/>, from
    /// Grasshopper's Create Material with no specular and no shine: a floor or a ceiling reads as
    /// concrete then, not as plastic with the display's lights on it. <paramref name="flat"/> lights it
    /// from within rather: the colour whatever the light, for a floor the display lights edge-on.
    /// </summary>
    static IGH_Component Matte(IGH_Param source, Color colour, PointF at, bool flat = false)
    {
        var proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Desc.Name == "Create Material");
        if (proxy == null) { Log("  no Create Material component"); return Preview(source, colour, at); }
        var material = Place((IGH_Component)proxy.CreateInstance(), at);
        var inputs = material.Params.Input;
        void Colour(int i, Color c)
        {
            if (i < inputs.Count && inputs[i] is GH_PersistentParam<GH_Colour> p) { p.PersistentData.Clear(); p.PersistentData.Append(new GH_Colour(c)); }
        }
        void Number(int i, double v)
        {
            if (i < inputs.Count && inputs[i] is GH_PersistentParam<GH_Number> p) { p.PersistentData.Clear(); p.PersistentData.Append(new GH_Number(v)); }
        }
        // Diffuse, Specular, Emission, Transparency, Shine.
        Colour(0, flat ? Color.Black : colour);
        Colour(1, Color.Black);
        Colour(2, flat ? colour : Color.Black);
        Number(3, 0);
        Number(4, 0);
        var preview = Place((IGH_Component)Instances.ComponentServer.EmitObject(CustomPreviewId), new PointF(at.X + 160, at.Y));
        preview.Params.Input[0].AddSource(source);
        preview.Params.Input[1].AddSource(material.Params.Output[0]);
        material.ExpireSolution(false);
        preview.ExpireSolution(false);
        return preview;
    }

    /// <summary>Grasshopper's Mesh Brep on <paramref name="source"/>, at its default settings.</summary>
    static IGH_Param Meshed(IGH_Param source, PointF at)
    {
        var proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Desc.Name == "Mesh Brep");
        if (proxy == null) { Log("  no Mesh Brep component"); return source; }
        var mesher = Place((IGH_Component)proxy.CreateInstance(), at);
        mesher.Params.Input[0].AddSource(source);
        mesher.ExpireSolution(false);
        return mesher.Params.Output[0];
    }

    /// <summary>
    /// A walk from <paramref name="from"/> to <paramref name="to"/>, looking along <paramref name="ahead"/>
    /// through a <paramref name="lens"/> mm lens, easing in and out as a walk starts and stops:
    /// <paramref name="frames"/> frames into <paramref name="folder"/>. Every case gets one, and a spin.
    /// </summary>
    static void Walk(Point3d from, Point3d to, Vector3d ahead, double lens, string folder, int frames = 72)
    {
        var view = Inside(from, from + ahead, lens);
        var vp = view.ActiveViewport;
        Directory.CreateDirectory(Path.Combine(outDir, folder));
        for (int k = 0; k < frames; k++)
        {
            double t = 0.5 - 0.5 * Math.Cos(Math.PI * k / (frames - 1));
            var at = from + (to - from) * t;
            vp.SetCameraLocations(at + ahead, at);
            vp.CameraUp = Vector3d.ZAxis;
            Shoot(view, Path.Combine(folder, k.ToString("D3")));
        }
        Log($"  {frames} frames, {from.DistanceTo(to):F0} m walked");
    }

    /// <summary>A perspective from <paramref name="eye"/> to <paramref name="target"/>, as someone standing there would see it.</summary>
    static RhinoView Inside(Point3d eye, Point3d target, double lens)
    {
        var view = View("Perspective");
        var vp = view.ActiveViewport;
        vp.ChangeToPerspectiveProjection(true, lens);
        vp.Camera35mmLensLength = lens;
        vp.SetCameraLocations(target, eye);
        vp.CameraUp = Vector3d.ZAxis;
        return view;
    }

    // StressLinesNerviFlatSlab, the Nervi flat slab: a flat slab on a 5 m grid of columns and its bending lines, from above; from
    // below, the lines as ribs, as Nervi built them, in the brand colours and in concrete alone; the
    // lines growing from their seed; a walk under the slab between two rows of columns.
    static List<(string, Func<bool>)> NerviFlatSlab()
    {
        Drawn2d d = null;
        IGH_Component[] coloured = null, concrete = null;
        // Standing between the second and third rows of columns, at eye height above the floor
        // 4 m down, looking along the rows and a little up.
        var eye = new Point3d(7.5, -3.5, -2.4);
        var ahead = new Vector3d(2.0, 22.0, 1.9);
        // As Nervi's floors photograph: the ribs light, the slab between them in their shadow.
        var light = Color.FromArgb(234, 232, 227);
        var recess = Color.FromArgb(160, 160, 157);
        var floorGrey = Color.FromArgb(226, 225, 221);
        return new List<(string, Func<bool>)>
        {
            Do("open", () =>
            {
                Open(here + "StressLinesNerviFlatSlab.gh");
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>()) Messages(c);
                LinesLog(Components(StressLinesId).First());
            }),
            Do("from above", () =>
            {
                var lines = Components(StressLinesId).First();
                var slab = Find<IGH_Component>("100 x 100 quads").Params.Output[0];
                d = Previews(lines, slab, Color.FromArgb(26, DarkTech), Find<IGH_Component>("25 x 25 m").Params.Output[0]);
                // From below: the ribs in the brand colours, or in concrete as the slab and columns.
                var ribs1 = Out(Find<IGH_Component>("Ribs SL1"), "Extrusion");
                var ribs2 = Out(Find<IGH_Component>("Ribs SL2"), "Extrusion");
                var shafts = Find<IGH_Component>("Column shafts").Params.Output[0];
                var floor = Find<IGH_Component>("Floor").Params.Output[0];
                var at = Beside(lines, 120, 420);
                // Meshes rather than Breps: Grasshopper draws a Brep's edges, one across a rib at
                // every span of its curve, and mesh edges are off for the pictures.
                ribs1 = Meshed(ribs1, new PointF(at.X - 140, at.Y + 360));
                ribs2 = Meshed(ribs2, new PointF(at.X - 140, at.Y + 480));
                shafts = Meshed(shafts, new PointF(at.X - 140, at.Y + 120));
                var underside = Matte(slab, recess, at);
                var columns = Matte(shafts, light, new PointF(at.X, at.Y + 120));
                var ground = Matte(floor, floorGrey, new PointF(at.X, at.Y + 240), true);
                coloured = new[] { Matte(ribs1, Purple, new PointF(at.X, at.Y + 360)), Matte(ribs2, LightBlue, new PointF(at.X, at.Y + 480)),
                                   underside, columns, ground };
                concrete = new[] { Matte(ribs1, light, new PointF(at.X, at.Y + 600)), Matte(ribs2, light, new PointF(at.X, at.Y + 720)),
                                   underside, columns, ground };
                doc.NewSolution(false);
                Canvas("nervi-flat-slab-canvas", 1.0f);
                AddSymbols(d, Components(AssembleId).First().Params.Output[0], 0, 0.6);
                doc.NewSolution(false);
                Reveal(d.All);
                Shoot(Top(Framed(0.02)), "nervi-flat-slab-top");
                Shoot(Aim(Framed(0.02), new Vector3d(0.6, -1.2, 0.9), Vector3d.ZAxis), "nervi-flat-slab");
                Reveal(d.Clean);
                Shoot(Top(Framed(0.02)), "nervi-flat-slab-top-clean");
            }),
            Do("from below", () =>
            {
                Reveal(coloured);
                Shoot(Inside(eye, eye + ahead, 20), "nervi-flat-slab-below");
                Shoot(Inside(new Point3d(-6, -6, -2.4), new Point3d(12.5, 12.5, -0.8), 20), "nervi-flat-slab-below-corner");
                Reveal(concrete);
                Shoot(Inside(eye, eye + ahead, 20), "nervi-flat-slab-below-concrete");
                Shoot(Inside(new Point3d(-6, -6, -2.4), new Point3d(12.5, 12.5, -0.8), 20), "nervi-flat-slab-below-corner-concrete");
            }),
            Do("growing", () =>
            {
                Reveal(d.Clean);
                Growing(d, Top(Framed(0.02)), "nervi-flat-slab-growing-frames", 40);
            }),
            Do("spin and walk", () =>
            {
                // Twelve metres along between the rows of columns; and the hall turning round.
                Reveal(coloured);
                Walk(eye, eye + new Vector3d(0, 12, 0), ahead, 20, "nervi-flat-slab-walk-through-frames");
                Turning(Framed(0.02), new Vector3d(0.8, -1.2, 0.45), "nervi-flat-slab-spin-frames", 72);
            }),
            Do("close", Close),
        };
    }

    // StressLinesNerviUmbrella, the Nervi umbrella: a square slab cantilevering from one column, its
    // bending lines from above; nine of them as a hall, from below, the lines as ribs in the brand
    // colours and in concrete; the hall spinning, and walked through between the columns.
    static List<(string, Func<bool>)> NerviUmbrella()
    {
        Drawn2d d = null;
        IGH_Component[] coloured = null, concrete = null;
        // Between a row of columns and the gap to the next row of umbrellas, at eye height above the
        // floor 8 m down, looking along the hall and a little up.
        var from = new Point3d(11.5, -24, -6.4);
        var to = new Point3d(11.5, 6, -6.4);
        var ahead = new Vector3d(1.5, 20, 3.5);
        var light = Color.FromArgb(234, 232, 227);
        var recess = Color.FromArgb(160, 160, 157);
        var floorGrey = Color.FromArgb(226, 225, 221);
        return new List<(string, Func<bool>)>
        {
            Do("open", () =>
            {
                Open(here + "StressLinesNerviUmbrella.gh");
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>()) Messages(c);
                LinesLog(Components(StressLinesId).First());
            }),
            Do("one umbrella", () =>
            {
                var lines = Components(StressLinesId).First();
                d = Previews(lines, Find<IGH_Component>("60 x 60 quads").Params.Output[0], Color.FromArgb(26, DarkTech),
                             Find<IGH_Component>("15 x 15 m").Params.Output[0]);
                var at = Beside(lines, 120, 420);
                var slabs = Matte(Find<IGH_Component>("Hall slabs").Params.Output[0], recess, at);
                var columns = Matte(Meshed(Find<IGH_Component>("Hall columns").Params.Output[0], new PointF(at.X - 140, at.Y + 120)), light, new PointF(at.X, at.Y + 120));
                var ground = Matte(Find<IGH_Component>("Floor").Params.Output[0], floorGrey, new PointF(at.X, at.Y + 240), true);
                var ribs1 = Meshed(Find<IGH_Component>("Hall ribs SL1").Params.Output[0], new PointF(at.X - 140, at.Y + 360));
                var ribs2 = Meshed(Find<IGH_Component>("Hall ribs SL2").Params.Output[0], new PointF(at.X - 140, at.Y + 480));
                coloured = new[] { Matte(ribs1, Purple, new PointF(at.X, at.Y + 360)), Matte(ribs2, LightBlue, new PointF(at.X, at.Y + 480)), slabs, columns, ground };
                concrete = new[] { Matte(ribs1, light, new PointF(at.X, at.Y + 600)), Matte(ribs2, light, new PointF(at.X, at.Y + 720)), slabs, columns, ground };
                doc.NewSolution(false);
                Canvas("nervi-umbrella-canvas", 1.0f);
                AddSymbols(d, Components(AssembleId).First().Params.Output[0], 0, 0.3);
                doc.NewSolution(false);
                Reveal(d.All);
                Shoot(Top(Framed(0.02)), "nervi-umbrella-top");
                Shoot(Aim(Framed(0.02), new Vector3d(0.6, -1.2, 0.9), Vector3d.ZAxis), "nervi-umbrella");
                Reveal(d.Clean);
                Shoot(Top(Framed(0.02)), "nervi-umbrella-top-clean");
            }),
            Do("the hall", () =>
            {
                Reveal(coloured);
                Shoot(Inside(from, from + ahead, 20), "nervi-umbrella-below");
                Shoot(Inside(new Point3d(-34, -34, -6.4), new Point3d(7.5, 7.5, -3), 24), "nervi-umbrella-hall");
                Reveal(concrete);
                Shoot(Inside(from, from + ahead, 20), "nervi-umbrella-below-concrete");
                Shoot(Inside(new Point3d(-34, -34, -6.4), new Point3d(7.5, 7.5, -3), 24), "nervi-umbrella-hall-concrete");
            }),
            Do("spin and walk", () =>
            {
                Reveal(coloured);
                Walk(from, to, ahead, 20, "nervi-umbrella-walk-through-frames");
                Turning(Framed(0.02), new Vector3d(0.8, -1.2, 0.45), "nervi-umbrella-spin-frames", 72);
            }),
            Do("growing", () =>
            {
                Reveal(d.Clean);
                Growing(d, Top(Framed(0.02)), "nervi-umbrella-growing-frames", 40);
            }),
            Do("close", Close),
        };
    }

    // StressLinesMusmeciBridge, the Musmeci bridge: a membrane form-found by Kangaroo on paired footings
    // over four spans, analysed as a concrete shell under its own weight, its lines in its surface, a
    // deck on its crowns; from three sides and from under it; the form being found; spinning; walked
    // through underneath.
    static List<(string, Func<bool>)> MusmeciBridge()
    {
        Drawn2d d = null;
        IGH_Component deck = null, ground = null;
        BoundingBox frame = BoundingBox.Empty;
        var eye = new Vector3d(0.55, -1.3, 0.38);
        int used = 0;
        return new List<(string, Func<bool>)>
        {
            Do("open", () => Open(here + "StressLinesMusmeciBridge.gh")),
            Do("bridge", () =>
            {
                var zombie = Find<IGH_Component>("Zombie Solver");
                used = Values<GH_Integer>(zombie.Params.Output[0]).Select(i => i.Value).FirstOrDefault();
                var form = Find<IGH_Component>("Form");
                Log($"  Kangaroo: {used} iterations; form {Values<GH_Mesh>(form.Params.Output[0]).FirstOrDefault()?.Value.GetBoundingBox(true)}");
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>()) Messages(c);
                var lines = Components(StressLinesId).First();
                LinesLog(lines);
                // Matte: shiny, the membrane reads white, its form lost.
                d = Previews(lines, form.Params.Output[0], Color.FromArgb(222, 221, 217), null, true);
                var at = Beside(lines, 120, 420);
                deck = Matte(Find<IGH_Component>("Deck").Params.Output[0], Color.FromArgb(234, 232, 227), at);
                ground = Matte(Find<IGH_Component>("Ground").Params.Output[0], Color.FromArgb(226, 225, 221), new PointF(at.X, at.Y + 120), true);
                doc.NewSolution(false);
                Canvas("musmeci-bridge-canvas", 1.0f);
                AddSymbols(d, Components(AssembleId).First().Params.Output[0], 0, 1.2);
                doc.NewSolution(false);
                Reveal(d.All.Concat(new[] { deck }).ToArray());
                frame = Framed(0.02);
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "musmeci-bridge");
                Shoot(Front(frame), "musmeci-bridge-elevation");
                Reveal(d.Clean.Concat(new[] { deck }).ToArray());
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "musmeci-bridge-clean");
                Reveal(d.Clean.Concat(new[] { deck, ground }).ToArray());
                Shoot(Inside(new Point3d(-14, 30, 1.7), new Point3d(30, 4, 6), 20), "musmeci-bridge-from-the-valley");
                Shoot(Inside(new Point3d(14, 6, 1.7), new Point3d(40, 5, 7), 18), "musmeci-bridge-below");
                Reveal(d.Clean);
                Shoot(Top(Framed(0.02)), "musmeci-bridge-top");
            }),
            Do("form finding", () =>
            {
                // As the Kangaroo shell's: evenly spaced iterations, ending where the net is all but up.
                Reveal(d.All.Concat(new[] { deck }).ToArray());
                var view = Aim(frame, eye, Vector3d.ZAxis);
                var form = Find<IGH_Component>("Form");
                double Rise() => Values<GH_Mesh>(form.Params.Output[0]).FirstOrDefault()?.Value.GetBoundingBox(true).Max.Z ?? 0;
                var run = Components(RunAnalysisId).First();
                run.Locked = true;
                double top = Rise();
                int settled = used;
                for (int i = used / 40; i < used; i += Math.Max(1, used / 40))
                {
                    Slider("Iterations", i);
                    if (Rise() >= 0.99 * top) { settled = i; break; }
                }
                run.Locked = false;
                int frames = 40;
                var stops = Enumerable.Range(0, frames).Select(k => 1 + (int)Math.Round((settled - 1) * (double)k / (frames - 1))).Distinct().ToList();
                Directory.CreateDirectory(Path.Combine(outDir, "musmeci-bridge-form-finding-frames"));
                var watch = Stopwatch.StartNew();
                for (int k = 0; k < stops.Count; k++)
                {
                    Slider("Iterations", stops[k]);
                    doc.NewSolution(false);
                    Shoot(view, Path.Combine("musmeci-bridge-form-finding-frames", k.ToString("D3")));
                }
                Slider("Iterations", 20000);
                doc.NewSolution(false);
                Log($"  {stops.Count} frames to iteration {stops.Last()} of {used}, in {watch.Elapsed.TotalSeconds:F0} s");
            }),
            Do("spin and walk", () =>
            {
                Reveal(d.All.Concat(new[] { deck }).ToArray());
                Turning(Grown(frame, 0.04), eye, "musmeci-bridge-spin-frames", 72);
                // Along the valley under the bridge, through two of its vaults.
                Reveal(d.Clean.Concat(new[] { deck, ground }).ToArray());
                Walk(new Point3d(-14, 6, 1.7), new Point3d(44, 6, 1.7), new Vector3d(14, 0, 2.2), 20, "musmeci-bridge-walk-through-frames");
            }),
            Do("close", Close),
        };
    }

    // ------------------------------------------------------------------ the studies

    /// <summary>
    /// A study of StressLinesStudies.py, Studies/<see cref="Name"/>.gh, and where to stand for its
    /// pictures: below it, and walking through it, at eye height.
    /// </summary>
    class Study
    {
        public string Name;
        public Vector3d Eye = new Vector3d(0.6, -1.2, 0.75), Spin = new Vector3d(0.8, -1.2, 0.45);
        public double SupportScale = 0.5, Lens = 20;
        public Point3d From, To;
        public Vector3d Ahead;
    }

    // Scene names to studies. The walks start outside each, at eye height above its floor, and go in.
    static readonly Dictionary<string, Study> Studies = new Dictionary<string, Study>
    {
        ["cornerslab"] = new Study { Name = "corner-slab", From = new Point3d(-4, -4, -2.4), To = new Point3d(8, 8, -2.4), Ahead = new Vector3d(9, 9, 2.2) },
        ["roundumbrella"] = new Study { Name = "round-umbrella", From = new Point3d(-13, -3, -4.4), To = new Point3d(9, -3, -4.4), Ahead = new Vector3d(10, 3, 2.6), SupportScale = 0.25 },
        ["roundslab"] = new Study { Name = "round-slab", From = new Point3d(-14, -1.5, -3.4), To = new Point3d(10, -1.5, -3.4), Ahead = new Vector3d(10, 2, 2.2) },
        ["candela"] = new Study { Name = "candela-umbrella", From = new Point3d(6.25, -26, -2.4), To = new Point3d(6.25, 4, -2.4), Ahead = new Vector3d(1.5, 20, 3.2), SupportScale = 0.25 },
        ["groinvault"] = new Study { Name = "groin-vault", From = new Point3d(0, -12, 1.6), To = new Point3d(0, 5, 1.6), Ahead = new Vector3d(0.8, 10, 1.4) },
        ["palazzetto"] = new Study { Name = "palazzetto", From = new Point3d(-21, -2, 1.6), To = new Point3d(6, -2, 1.6), Ahead = new Vector3d(10, 2, 2.4) },
        ["isler"] = new Study { Name = "isler-shell", From = new Point3d(0, -16, 1.6), To = new Point3d(0, 3, 1.6), Ahead = new Vector3d(0.6, 10, 2.2) },
        ["hexshell"] = new Study { Name = "hexagonal-shell", From = new Point3d(-17, 1, 1.6), To = new Point3d(4, 1, 1.6), Ahead = new Vector3d(10, 0.8, 2.2) },
    };

    static IGH_Component Optional(string nick) => doc.Objects.OfType<IGH_Component>().FirstOrDefault(c => c.NickName == nick);

    /// <summary>
    /// A study: its lines from above, with and without its supports; from below, standing where the
    /// walk starts - a slab's lines as ribs, in the brand colours and in concrete; its definition;
    /// a Kangaroo form being found; spinning; walked through; its lines growing.
    /// </summary>
    static List<(string, Func<bool>)> StudyScene(Study s)
    {
        string n = s.Name;
        Drawn2d d = null;
        IGH_Component[] coloured = null, concrete = null;
        IGH_Component floor = null;
        bool kangaroo = false;
        // The structure without its floor, framed: the floor reaches well past it, and framed with it
        // the structure would be a speck in the middle.
        BoundingBox Structure()
        {
            Reveal(coloured.Where(c => c != floor).ToArray());
            var box = Framed(0.02);
            Reveal(coloured);
            return box;
        }
        var light = Color.FromArgb(234, 232, 227);
        var recess = Color.FromArgb(160, 160, 157);
        var shellGrey = Color.FromArgb(222, 221, 217);
        return new List<(string, Func<bool>)>
        {
            Do("open", () =>
            {
                Open(here + "Studies" + Path.DirectorySeparatorChar + n + ".gh");
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>()) Messages(c);
                LinesLog(Components(StressLinesId).First());
                var zombie = Optional("Zombie Solver");
                kangaroo = zombie != null;
                if (kangaroo)
                    Log($"  Kangaroo: {Values<GH_Integer>(zombie.Params.Output[0]).Select(i => i.Value).FirstOrDefault()} iterations, " +
                        $"rise {Values<GH_Mesh>(Find<IGH_Component>("Surface").Params.Output[0]).FirstOrDefault()?.Value.GetBoundingBox(true).Max.Z:F2} m");
            }),
            Do("pictures", () =>
            {
                var lines = Components(StressLinesId).First();
                var surface = Find<IGH_Component>("Surface").Params.Output[0];
                bool slab = Optional("Ribs SL1") != null;
                d = slab ? Previews(lines, surface, Color.FromArgb(26, DarkTech), null) : Previews(lines, surface, shellGrey, null, true);
                IGH_Param Hall(string nick, IGH_Param single) => Optional(nick)?.Params.Output[0] ?? single;
                var at = Beside(lines, 120, 420);
                PointF Next() { at = new PointF(at.X, at.Y + 120); return new PointF(at.X + 160, at.Y); }
                floor = Matte(Find<IGH_Component>("Floor").Params.Output[0], Color.FromArgb(226, 225, 221), Next(), true);
                var shafts = Optional("Column shafts");
                var columns = shafts == null ? null : Matte(Meshed(Hall("Hall columns", shafts.Params.Output[0]), Next()), light, Next());
                if (slab)
                {
                    var ribs1 = Meshed(Hall("Hall SL1", Out(Find<IGH_Component>("Ribs SL1"), "Extrusion")), Next());
                    var ribs2 = Meshed(Hall("Hall SL2", Out(Find<IGH_Component>("Ribs SL2"), "Extrusion")), Next());
                    var underside = Matte(Hall("Hall surface", surface), recess, Next());
                    coloured = new[] { Matte(ribs1, Purple, Next()), Matte(ribs2, LightBlue, Next()), underside, columns, floor };
                    concrete = new[] { Matte(ribs1, light, Next()), Matte(ribs2, light, Next()), underside, columns, floor };
                }
                else
                {
                    var hall = Optional("Hall surface") != null;
                    coloured = hall
                        ? new[] { Preview(Hall("Hall SL1", null), Purple, Next()), Preview(Hall("Hall SL2", null), LightBlue, Next()),
                                  Matte(Hall("Hall surface", null), shellGrey, Next()), columns, floor }
                        : new[] { d.Sl1, d.Sl2, d.Surface, columns, floor };
                }
                coloured = coloured.Where(c => c != null).ToArray();
                concrete = concrete?.Where(c => c != null).ToArray();
                doc.NewSolution(false);
                Canvas(n + "-canvas", 1.0f);
                AddSymbols(d, Components(AssembleId).First().Params.Output[0], 0, s.SupportScale);
                doc.NewSolution(false);
                Reveal(d.All);
                Shoot(Top(Framed(0.02)), n + "-top");
                Shoot(Aim(Framed(0.02), s.Eye, Vector3d.ZAxis), n);
                Reveal(d.Clean);
                Shoot(Top(Framed(0.02)), n + "-top-clean");
                Shoot(Aim(Framed(0.02), s.Eye, Vector3d.ZAxis), n + "-clean");
                var structure = Structure();
                Shoot(Inside(s.From, s.From + s.Ahead, s.Lens), n + "-below");
                Shoot(Aim(structure, new Vector3d(0.9, -1.1, 0.3), Vector3d.ZAxis), n + "-outside");
                if (concrete != null)
                {
                    Reveal(concrete);
                    Shoot(Inside(s.From, s.From + s.Ahead, s.Lens), n + "-below-concrete");
                    Shoot(Aim(structure, new Vector3d(0.9, -1.1, 0.3), Vector3d.ZAxis), n + "-outside-concrete");
                }
            }),
            Do("form finding", () =>
            {
                if (!kangaroo) return;
                Reveal(d.All);
                FormFinding(n, Grown(Framed(0.02), 0.02), s.Eye);
            }),
            Do("spin and walk", () =>
            {
                Turning(Grown(Structure(), 0.04), s.Spin, n + "-spin-frames", 72);
                Walk(s.From, s.To, s.Ahead, s.Lens, n + "-walk-through-frames");
            }),
            Do("growing", () =>
            {
                Reveal(d.Clean);
                Growing(d, Top(Framed(0.02)), n + "-growing-frames", 40);
            }),
            Do("close", Close),
        };
    }

    /// <summary>
    /// A Kangaroo form being found, as frames into <paramref name="name"/>-form-finding-frames:
    /// Iterations moved evenly from 1 to where the form is all but up - found first with the analysis
    /// locked, Kangaroo alone - the model analysed and its lines traced at each. Its form is the
    /// Surface; its slider, Iterations, is put back to its end.
    /// </summary>
    static void FormFinding(string name, BoundingBox frame, Vector3d eye)
    {
        var view = Aim(frame, eye, Vector3d.ZAxis);
        var form = Find<IGH_Component>("Surface");
        var zombie = Find<IGH_Component>("Zombie Solver");
        int used = Values<GH_Integer>(zombie.Params.Output[0]).Select(i => i.Value).FirstOrDefault();
        double Rise() => Values<GH_Mesh>(form.Params.Output[0]).FirstOrDefault()?.Value.GetBoundingBox(true).Max.Z ?? 0;
        var slider = Find<GH_NumberSlider>("Iterations");
        double end = (double)slider.Slider.Value;
        var run = Components(RunAnalysisId).First();
        run.Locked = true;
        double top = Rise();
        int settled = used;
        for (int i = Math.Max(1, used / 40); i < used; i += Math.Max(1, used / 40))
        {
            Slider("Iterations", i);
            if (Rise() >= 0.99 * top) { settled = i; break; }
        }
        run.Locked = false;
        int frames = 40;
        var stops = Enumerable.Range(0, frames).Select(k => 1 + (int)Math.Round((settled - 1) * (double)k / (frames - 1))).Distinct().ToList();
        string folder = name + "-form-finding-frames";
        Directory.CreateDirectory(Path.Combine(outDir, folder));
        var watch = Stopwatch.StartNew();
        for (int k = 0; k < stops.Count; k++)
        {
            Slider("Iterations", stops[k]);
            doc.NewSolution(false);
            Shoot(view, Path.Combine(folder, k.ToString("D3")));
        }
        Slider("Iterations", end);
        doc.NewSolution(false);
        Log($"  {stops.Count} frames to iteration {stops.Last()} of {used}, in {watch.Elapsed.TotalSeconds:F0} s");
    }

    // StressLinesShell: a net held at its corners and pushed up, form-found by Kangaroo, analysed as a
    // concrete shell under its own weight, its lines in its surface: from three sides and from above;
    // the form found iteration by iteration with the lines on each; turning round; the lines growing.
    static List<(string, Func<bool>)> Kangaroo()
    {
        Drawn2d d = null;
        BoundingBox frame = BoundingBox.Empty;
        var eye = new Vector3d(1, -1.3, 0.55);
        int used = 0;
        return new List<(string, Func<bool>)>
        {
            Do("open", () => Open(here + "StressLinesShell.gh")),
            Do("shell", () =>
            {
                var zombie = Find<IGH_Component>("Zombie Solver");
                used = Values<GH_Integer>(zombie.Params.Output[0]).Select(i => i.Value).FirstOrDefault();
                var form = Find<IGH_Component>("Form");
                var mesh = Values<GH_Mesh>(form.Params.Output[0]).FirstOrDefault()?.Value;
                Log($"  Kangaroo: {used} iterations; form {mesh?.GetBoundingBox(true)}");
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>()) Messages(c);
                var lines = Components(StressLinesId).First();
                LinesLog(lines);
                d = Previews(lines, form.Params.Output[0], Color.FromArgb(255, 226, 229, 234), null);
                doc.NewSolution(false);
                Canvas("shell-canvas", 1.0f);
                AddSymbols(d, Components(AssembleId).First().Params.Output[0], 0, 0.8);
                doc.NewSolution(false);
                Reveal(d.All);
                frame = Grown(Drawn(), 0.02);
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "shell");
                Shoot(Aim(frame, new Vector3d(1, -0.25, 0.22), Vector3d.ZAxis), "shell-low");
                Shoot(Aim(frame, new Vector3d(0.3, -0.5, 1.4), Vector3d.ZAxis), "shell-above");
                Shoot(Top(frame), "shell-top");
                Reveal(d.Clean);
                Shoot(Aim(frame, eye, Vector3d.ZAxis), "shell-clean");
            }),
            Do("form finding", () =>
            {
                // Evenly spaced iterations, to the end: pushed so lightly, the net rises slowly
                // and steadily, not all at the start.
                Reveal(d.All);
                var view = Aim(frame, eye, Vector3d.ZAxis);
                var form = Find<IGH_Component>("Form");
                double Rise() => Values<GH_Mesh>(form.Params.Output[0]).FirstOrDefault()?.Value.GetBoundingBox(true).Max.Z ?? 0;
                // Where the net is all but up: found with the analysis locked, Kangaroo alone.
                var run = Components(RunAnalysisId).First();
                run.Locked = true;
                double top = Rise();
                int settled = used;
                for (int i = used / 40; i < used; i += Math.Max(1, used / 40))
                {
                    Slider("Iterations", i);
                    if (Rise() >= 0.99 * top) { settled = i; break; }
                }
                run.Locked = false;
                int frames = 40;
                var stops = Enumerable.Range(0, frames).Select(k => 1 + (int)Math.Round((settled - 1) * (double)k / (frames - 1))).Distinct().ToList();
                var rises = new List<string>();
                Directory.CreateDirectory(Path.Combine(outDir, "shell-form-finding-frames"));
                var watch = Stopwatch.StartNew();
                for (int k = 0; k < stops.Count; k++)
                {
                    Slider("Iterations", stops[k]);
                    doc.NewSolution(false);
                    var mesh = Values<GH_Mesh>(form.Params.Output[0]).FirstOrDefault()?.Value;
                    rises.Add(mesh == null ? "-" : mesh.GetBoundingBox(true).Max.Z.ToString("F2", Inv));
                    Shoot(view, Path.Combine("shell-form-finding-frames", k.ToString("D3")));
                }
                Slider("Iterations", 10000);
                doc.NewSolution(false);
                Log($"  {stops.Count} frames to iteration {stops.Last()} of {used}, in {watch.Elapsed.TotalSeconds:F0} s; rise {string.Join(" ", rises)}");
            }),
            Do("spin and walk", () =>
            {
                Reveal(d.All);
                Turning(Grown(frame, 0.04), eye, "shell-spin-frames", 72);
                // Under the crown, from one side of the shell to the other.
                Walk(new Point3d(2, 4.5, 1.4), new Point3d(8.5, 5.5, 1.4), new Vector3d(8, 1, 1.2), 24, "shell-walk-through-frames");
            }),
            Do("growing", () =>
            {
                Reveal(d.All);
                Growing(d, Aim(frame, eye, Vector3d.ZAxis), "shell-growing-frames", 40);
            }),
            Do("close", Close),
        };
    }

    static BoundingBox Grown(BoundingBox b, double f)
    {
        b.Inflate(b.Diagonal.Length * f);
        return b;
    }
}
