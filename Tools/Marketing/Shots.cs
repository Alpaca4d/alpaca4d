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
// Grasshopper's own pink would be the only colour, and saves viewport captures on white, canvas
// pictures and animation frames. It drives the plugin installed in that Rhino, by GUID and reflection
// only. run.sh builds it, loads it into Rhino 8 and starts it; see README.md.
public static class Shots
{
    static readonly Guid TopologyId = new Guid("8F3C2A61-5D4E-4B7A-9C1E-2B6D7A0F4E93");
    static readonly Guid ModelViewId = new Guid("5EDB6364-D458-43D6-81E9-324DECA1FEA6");
    static readonly Guid RunAnalysisId = new Guid("8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452");
    static readonly Guid ViewResultsId = new Guid("4D8A0F26-3B71-4E59-9C84-A2F70D51B6E3");
    static readonly Guid LegendId = new Guid("A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5");
    static readonly Guid StressLinesId = new Guid("E2C4E5D3-4A7F-4F8E-9A1D-39E0E7C0D4A2");
    static readonly Guid CustomPreviewId = new Guid("537B0419-BBC2-4FF4-BF08-AFE526367B2C");

    // The brand palette (Alpaca.Core/Utils/BrandColors.cs).
    static readonly Color LightBlue = Color.FromArgb(66, 136, 247);
    static readonly Color Orange = Color.FromArgb(235, 108, 63);
    static readonly Color Purple = Color.FromArgb(158, 53, 218);
    static readonly Color DarkTech = Color.FromArgb(42, 45, 49);

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
                    default: throw new Exception("no scene " + scene);
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
        RhinoApp.Exit();
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

    /// <summary>Puts back what Setup changed in Grasshopper's settings, and drops the white display mode.</summary>
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
        mode.DisplayAttributes.SetFill(Color.White);
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

    /// <summary>The view on white, at Width x Height, with screen items scaled to the capture.</summary>
    static void Shoot(RhinoView view, string name)
    {
        var mode = WhiteMode();
        view.ActiveViewport.DisplayMode = mode;
        doc.DeselectAll();
        view.Redraw();
        RhinoApp.Wait();
        var capture = new ViewCapture
        {
            Width = Width, Height = Height, ScaleScreenItems = true,
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
        var results = Components(ViewResultsId).First();
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
        var p1 = Preview(Out(lines, "SL1"), Orange, new PointF(right, at.Y - 60));
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
                var results = Components(ViewResultsId).First();
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
                var results = Components(ViewResultsId).First();
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

    /// <summary>View Results' Result and Component, picked as the menu picks them.</summary>
    static void ShowResult(IGH_Component results, int family, int component)
    {
        var f = Field(results, "_ddFamily");
        f.GetType().GetProperty("Value").SetValue(f, family);
        results.GetType().GetMethod("SyncComponentList", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(results, null);
        var c = Field(results, "_ddComponent");
        c.GetType().GetProperty("Value").SetValue(c, component);
        results.ExpireSolution(false);
    }

    /// <summary>A Legend fed by View Results, as the examples wire it.</summary>
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
            var results = Components(ViewResultsId).First();
            run.Locked = false;
            results.Locked = false;
            var watch = Stopwatch.StartNew();
            run.ExpireSolution(false);
            doc.NewSolution(false);
            Log($"  solved in {watch.Elapsed.TotalSeconds:F1} s");
            ShowResult(results, 5, 6);
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
            var results = Components(ViewResultsId).First();
            run.Locked = false;
            results.Locked = false;
            var watch = Stopwatch.StartNew();
            run.ExpireSolution(false);
            doc.NewSolution(false);
            Log($"  solved in {watch.Elapsed.TotalSeconds:F1} s");
            // Lengths in mm: the first of View Results' units.
            var length = ((Array)Field(results, "_ddUnits")).GetValue(0);
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

    /// <summary>Model View drawing only the supports and loads of the model <paramref name="source"/> carries.</summary>
    static IGH_Component Symbols(IGH_Param source, PointF at, double loadScale, double supportScale)
    {
        var view = Place((IGH_Component)Instances.ComponentServer.EmitObject(ModelViewId), at);
        view.Params.Input[0].AddSource(source);
        ((GH_PersistentParam<GH_Integer>)In(view, "ElementIds")).PersistentData.Append(new GH_Integer(-1));
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
    /// example does it: Run Analysis on its AlpacaModel, View Results on the bricks' Von Mises stress,
    /// in <paramref name="stressUnit"/> (0 kPa, the model's own unit; 1 MPa), and a Legend. Added only
    /// once the run is over: wired while Step moves, it would analyse every design.
    /// </summary>
    static (IGH_Component run, IGH_Component results, IGH_Component legend) Analyse(IGH_Component topology, string title, int stressUnit, PointF at)
    {
        var settings = Place((IGH_Component)Instances.ComponentServer.EmitObject(AnalysisSettingsId), at);
        var run = Place((IGH_Component)Instances.ComponentServer.EmitObject(RunAnalysisId), new PointF(at.X + 200, at.Y));
        run.Params.Input[0].AddSource(Out(topology, "AlpacaModel"));
        run.Params.Input[1].AddSource(settings.Params.Output[0]);
        var results = Place((IGH_Component)Instances.ComponentServer.EmitObject(ViewResultsId), new PointF(at.X + 420, at.Y));
        results.Params.Input[0].AddSource(run.Params.Output[0]);
        ShowResult(results, 5, 6);
        var stress = ((Array)Field(results, "_ddUnits")).GetValue(3);
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
                var results = Components(ViewResultsId).First();
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
                vc = Symbols(cantilever.Params.Input[0].Sources[0], Beside(cantilever, 120, 300), 5, 3);
                vs = Symbols(simple.Params.Input[0].Sources[0], Beside(simple, 120, 300), 8, 4);
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
                modelView = Symbols(topology.Params.Input[0].Sources[0], Beside(topology, 120, 300), 2, 2);
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
            var p1 = Preview(Out(lines, "SL1"), Orange, new PointF(right, at.Y - 60));
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

    static BoundingBox Grown(BoundingBox b, double f)
    {
        b.Inflate(b.Diagonal.Length * f);
        return b;
    }
}
