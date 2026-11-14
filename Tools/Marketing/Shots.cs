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
                    case "slave": list = Slave(); break;
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
                    case "shell": list = Structures("compressed-shell"); break;
                    case "footbridge": list = Footbridge(); break;
                    case "footfallstair": list = FootfallStairScene("FootfallStair.gh", "footfall-stair", new Vector3d(-1, -1.3, 0.9), new Vector3d(-0.25, -0.45, 1.6)); break;
                    // Their sweeps gave the examples' pages their tables; name the slider and its values again
                    // (Ribbon [mm] 10-40, Stringer depth [mm] 300-1000) to make them again.
                    case "footfallhelical": list = FootfallStairScene("FootfallHelicalStair.gh", "footfall-helical-stair", new Vector3d(-0.55, -0.85, 1.25), new Vector3d(-0.2, -0.35, 1.6), 1.8); break;
                    // The helical stair from more sides, and its fundamental mode swinging as the camera goes round
                    // it: it climbs from +X anticlockwise, 300 degrees, so its open side faces +X -Y.
                    case "footfallhelicalviews":
                        list = FootfallStairScene("FootfallHelicalStair.gh", "footfall-helical-stair", new Vector3d(-0.55, -0.85, 1.25),
                                                  new Vector3d(-0.2, -0.35, 1.6), 1.8,
                                                  responseViews: new[]
                                                  {
                                                      ("plan", new Vector3d(0, 0, 1), 1.05),
                                                      ("far-side", new Vector3d(0.55, 0.85, 1.25), 1.5),
                                                      ("opening", new Vector3d(0.87, -0.5, 0.6), 1.4),
                                                  },
                                                  modeViews: new[]
                                                  {
                                                      ("plan", new Vector3d(0, 0, 1), 1.05),
                                                      ("opening", new Vector3d(0.87, -0.5, 0.6), 1.4),
                                                      ("side", new Vector3d(-0.5, -0.87, 0.2), 1.4),
                                                      ("far-side", new Vector3d(0.55, 0.85, 1.25), 1.5),
                                                  },
                                                  spin: new Vector3d(0.87, -0.5, 0.6))
                            .Where(s => s.Item1 == "open" || s.Item1 == "canvas" || s.Item1 == "views" || s.Item1 == "spin" || s.Item1 == "close").ToList();
                        break;
                    case "footfallstraight": list =FootfallStairScene("FootfallStraightStair.gh", "footfall-straight-stair", new Vector3d(-0.55, -1, 0.5), new Vector3d(-0.15, -0.5, 1.6), 1.3); break;
                    case "tank": list = Tank(); break;
                    case "tankstills": list = Tank().Where(x => x.Item1 != "swinging").ToList(); break;
                    case "spectrum": list = Spectrum(); break;
                    case "momentcurvature": list = MomentCurvatureScene(); break;
                    case "pushover": list = PushoverScene(); break;
                    case "reciprocalvault": list = VaultScene(); break;
                    case "perforatedbeam": list = PerforatedBeamScene(); break;
                    case "plasticity":
                        // The T-joint at step 17, 544 kN, the yielding spread over the chord face before the chord
                        // starts to yield at its held ends too; the plate at 1 105 kN/m2, its yield lines an X.
                        list = PlasticityScene("PlasticityTJoint.gh", "pipe-t-joint", new Vector3d(0.55, -1, 0.45), false,
                                               "Load", "kN", "Brace end moves down [mm]", "Brace end", 16)
                            .Concat(PlasticityScene("PlasticityYieldLines.gh", "yield-lines", new Vector3d(0.45, -1, 0.75), false,
                                               "Pressure", "kN/m²", "Deflection at the middle [mm]", "Middle", 16))
                            .Concat(PlasticityScene("PlasticityPlateHole.gh", "plate-with-a-hole", Vector3d.ZAxis, true,
                                               "Load", "kN", "Pulled end moves [mm]", "Pulled end"))
                            .Concat(PlasticityScene("PlasticityPipe.gh", "thick-pipe", Vector3d.ZAxis, true,
                                               "Pressure", "MPa", "Bore widens [mm]", "Bore"))
                            .ToList();
                        break;
                    case "earthquakes": list = Earthquakes("earthquakes"); break;
                    // The buildings that are not straight, and the towers of a hundred storeys and more.
                    case "quakebuildings": list = Earthquakes("buildings"); break;
                    case "quaketowers": list = Earthquakes("towers"); break;
                    case "twisting": case "leaning": case "curving": case "crescent":
                        list = Earthquakes("buildings", scene); break;
                    case "bundled": case "braced": case "buttressed": case "outrigger": case "diagrid":
                        list = Earthquakes("towers", scene); break;
                    case "plates": list = Plates(); break;
                    case "kangaroo": list = Kangaroo(); break;
                    case "nerviflatslab": list = NerviFlatSlab(); break;
                    case "nerviumbrella": list = NerviUmbrella(); break;
                    case "musmeci": list = MusmeciBridge(); break;
                    case "printing": list = Printing(); break;
                    case "printedstructure": case "printedtwisted": case "printedwall": case "printeddome":
                    case "printedleaning": case "printedleaningflat":
                        var p = Prints.First(x => x.scene == scene);
                        list = PrintedShape(p.file, p.prefix, p.eye, p.flat, p.label);
                        break;
                    case "printingbuckling": list = PrintedBuckling(); break;
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
    static object edgesBefore;

    /// <summary>
    /// Model View's element edges in grey rather than black, for as long as the scenes run: a tower of
    /// thousands of thin members turns black under black edges. Through reflection, as this file is
    /// built without Alpaca4d; a build without the setting is left as it is.
    /// </summary>
    static void SoftEdges(Color grey)
    {
        var core = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Alpaca4d.Core" && a.GetType("Alpaca4d.AlpacaSettings") != null);
        var settings = core?.GetType("Alpaca4d.AlpacaSettings");
        var item = core?.GetType("Alpaca4d.DisplayItem");
        if (settings == null || item == null || !Enum.GetNames(item).Contains("Edges")) { Log("  this Alpaca4d has no edge colour: edges stay black"); return; }
        var edges = Enum.Parse(item, "Edges");
        if (edgesBefore == null) edgesBefore = settings.GetMethod("Colour").Invoke(null, new[] { edges });
        settings.GetMethod("SetColour").Invoke(null, new[] { edges, (object)grey });
    }

    static void HardEdges()
    {
        if (edgesBefore == null) return;
        var core = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Alpaca4d.Core" && a.GetType("Alpaca4d.AlpacaSettings") != null);
        var item = core.GetType("Alpaca4d.DisplayItem");
        core.GetType("Alpaca4d.AlpacaSettings").GetMethod("SetColour").Invoke(null, new[] { Enum.Parse(item, "Edges"), edgesBefore });
        edgesBefore = null;
    }

    static void Finish()
    {
        HardEdges();
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
        // Only what has not been solved yet: shown on the canvas, the document has often solved
        // already, and solving it all again ran every analysis twice - minutes, for an earthquake.
        doc.NewSolution(false);
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
    /// <summary>
    /// Moves the camera and its target sideways together, so that what is in the middle of the view
    /// ends up fraction of the view's width left of it.
    /// </summary>
    static void Slide(RhinoView view, double fraction)
    {
        var vp = view.ActiveViewport;
        vp.GetCameraAngle(out double halfDiagonal, out double halfVertical, out double halfHorizontal);
        var target = vp.CameraTarget;
        var location = vp.CameraLocation;
        var ahead = target - location;
        var right = Vector3d.CrossProduct(ahead, vp.CameraUp);
        right.Unitize();
        // A fraction of the width a capture shows, which can be wider than the viewport.
        var move = right * fraction * 2 * ahead.Length * Math.Tan(halfHorizontal) * Captured(vp).Width / vp.Size.Width;
        vp.SetCameraLocations(target + move, location + move);
    }

    // The viewport's size when it was last logged: logged again whenever it changes, which shifts what
    // a capture shows.
    static Size loggedSize;

    static Bitmap Shrunk(Bitmap source, int w, int h)
    {
        var small = new Bitmap(w, h);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, w, h);
        }
        return small;
    }

    /// <summary>
    /// Zooms out until every corner of box is inside the view, left of it no nearer its left side than
    /// left times its width - where the legend is - and a margin from the others.
    /// </summary>
    static void FitInside(RhinoView view, BoundingBox box, double left)
    {
        var vp = view.ActiveViewport;
        var field = Captured(vp);
        for (int i = 0; i < 30; i++)
        {
            var toScreen = vp.GetTransform(Rhino.DocObjects.CoordinateSystem.World, Rhino.DocObjects.CoordinateSystem.Screen);
            bool inside = box.GetCorners().All(c =>
            {
                var p = c;
                p.Transform(toScreen);
                return p.X >= field.Left + left * field.Width && p.X <= field.Left + 0.97 * field.Width &&
                       p.Y >= field.Top + 0.04 * field.Height && p.Y <= field.Top + 0.96 * field.Height;
            });
            if (inside) return;
            vp.Magnify(0.94, false);
        }
    }

    /// <summary>
    /// What a capture of Width by Height shows, in the viewport's screen coordinates: the viewport
    /// widened, or made taller, to the capture's shape about its middle. The viewport Rhino gives the
    /// scenes need not have that shape: on 2026-11-07 it stood upright, 591 x 788.
    /// </summary>
    static RectangleF Captured(RhinoViewport vp)
    {
        double w = vp.Size.Width, h = vp.Size.Height, shape = (double)Width / Height;
        double cw = w / h < shape ? h * shape : w, ch = w / h < shape ? h : w / shape;
        return new RectangleF((float)((w - cw) / 2), (float)((h - ch) / 2), (float)cw, (float)ch);
    }

    static void Shoot(RhinoView view, string name, double scale = 1)
    {
        var mode = WhiteMode();
        view.ActiveViewport.DisplayMode = mode;
        doc.DeselectAll();
        view.Redraw();
        RhinoApp.Wait();
        // Rhino for Mac frames a small capture differently from a large one: a frame at 2400 x 1500
        // came out 1.6 times closer than the still at 3840 x 2400 and cut off. So everything is
        // captured at the still's size at least, which holds the whole view - the viewport's height,
        // widened to the capture's shape - and made smaller after.
        int w = (int)(Width * scale), h = (int)(Height * scale);
        var points = view.ActiveViewport.Size;
        double up = Math.Max(1.0, Math.Max(Width * WithLegend / w, Height * WithLegend / h));
        if (points != loggedSize)
        {
            Log($"  the viewport is {points.Width}x{points.Height}; a capture of {w}x{h} is made {up:0.##} times larger and shrunk");
            loggedSize = points;
        }
        var capture = new ViewCapture
        {
            Width = (int)Math.Ceiling(w * up), Height = (int)Math.Ceiling(h * up), ScaleScreenItems = true,
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
        using (var captured = capture.CaptureToBitmap(view))
        using (var bitmap = up > 1.0 ? Shrunk(captured, w, h) : new Bitmap(captured))
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

    // ------------------------------------------------------------------ a footbridge's modes

    /// <summary>
    /// FootbridgeModes.gh, from Tools/Examples/Structures.py: its canvas, and its first three modes, each
    /// a still with its Legend over the bridge at rest and a loop of it swinging through its rest.
    /// </summary>
    static List<(string, Func<bool>)> Footbridge()
    {
        var eye = new Vector3d(-0.45, -1.4, 0.45);
        var list = new List<(string, Func<bool>)>
        {
            Do("open", () => Open("FootbridgeModes.gh")),
            Do("canvas", () =>
            {
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                    Messages(c);
                var modes = doc.Objects.OfType<IGH_Component>().First(c => c.Name.StartsWith("Natural Vibration"));
                Log("  frequencies " + string.Join(", ", Values<GH_Number>(Out(modes, "Frequencies")).Select(n => n.Value.ToString("G4", Inv))) + " Hz");
                Canvas("footbridge-canvas");
            }),
        };
        foreach (int m in new[] { 0, 1, 2 })
            list.Add(Do("mode " + m, () =>
            {
                var view = Find<IGH_Component>("Footbridge mode");
                ShowOnly(view, Find<IGH_Component>("Footbridge mode legend"), Find<IGH_Component>("Footbridge at rest"));
                Slider("Mode", m);
                doc.NewSolution(false);
                Log("  " + Info(view));
                Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), "footbridge-mode-" + m, WithLegend);
                Vibrating(view, eye, "footbridge-mode-" + m + "-frames", 36, 1.0);
            }));
        list.Add(Do("close", Close));
        return list;
    }

    // ------------------------------------------------------------------ footfall on stairs

    /// <summary>
    /// A footfall stair from Tools/Examples/FootfallStair.py - FootfallStair.gh, FootfallHelicalStair.gh or
    /// FootfallStraightStair.gh: its canvas; when <paramref name="sweep"/> names a slider, the fundamental
    /// frequency and the largest response factor at each of <paramref name="values"/>, for the example's
    /// page; the response factor where people walk, with its Legend, the largest R and its marker, from a
    /// corner and from above, the map's mesh edges light grey; and the fundamental mode the report names, a
    /// still with its legend (titled with its frequency) and a loop of it swinging.
    /// </summary>
    static List<(string, Func<bool>)> FootfallStairScene(string file, string name, Vector3d eye, Vector3d above,
                                                         double zoom = 1.3, string sweep = null, double[] values = null,
                                                         (string, Vector3d, double)[] responseViews = null,
                                                         (string, Vector3d, double)[] modeViews = null, Vector3d? spin = null)
    {
        int fundamental = 0;
        var list = new List<(string, Func<bool>)>
        {
            Do("open", () => Open(file)),
            Do("canvas", () =>
            {
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                    Messages(c);
                var modes = doc.Objects.OfType<IGH_Component>().First(c => c.Name.StartsWith("Natural Vibration"));
                var f = Values<GH_Number>(Out(modes, "Frequencies")).Select(n => n.Value).ToList();
                Log("  frequencies " + string.Join(", ", f.Select(v => v.ToString("G4", Inv))) + " Hz");
                var footfall = doc.Objects.OfType<IGH_Component>().First(c => c.Name.StartsWith("Footfall Analysis"));
                string report = Values<GH_String>(Out(footfall, "Report")).Select(v => v.Value).FirstOrDefault() ?? "";
                foreach (var line in report.Split('\n').Where(l => l.Trim().Length > 0))
                    Log("  " + line.TrimEnd());
                var label = Values<GH_String>(Out(Find<IGH_Component>("Response map"), "Label")).Select(v => v.Value).FirstOrDefault();
                Log("  on the walking surface: " + label);
                // The mode the report names as the fundamental, for the pictures of the mode.
                const string Marker = "Fundamental frequency ";
                int at = report.IndexOf(Marker, StringComparison.Ordinal);
                if (at >= 0 && f.Count > 0)
                {
                    var number = new string(report.Substring(at + Marker.Length).TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
                    if (double.TryParse(number, NumberStyles.Float, Inv, out double f0))
                        fundamental = Enumerable.Range(0, f.Count).OrderBy(i => Math.Abs(f[i] - f0)).First();
                }
                Log("  the fundamental is mode " + fundamental);
                Canvas(name + "-canvas");
            }),
        };
        if (sweep != null)
            list.Add(Do("sweep", () =>
            {
                // The fundamental and the largest response factor where people walk as the slider moves,
                // for the example's page; the slider is put back where the definition opens at.
                var slider = Find<GH_NumberSlider>(sweep);
                double opened = (double)slider.Slider.Value;
                var footfall = doc.Objects.OfType<IGH_Component>().First(c => c.Name.StartsWith("Footfall Analysis"));
                var map = Find<IGH_Component>("Response map");
                foreach (double v in values)
                {
                    Slider(sweep, v);
                    doc.NewSolution(false);
                    string report = Values<GH_String>(Out(footfall, "Report")).Select(x => x.Value).FirstOrDefault() ?? "";
                    string Line(string start) => report.Split('\n').FirstOrDefault(l => l.StartsWith(start))?.Trim() ?? "";
                    string label = Values<GH_String>(Out(map, "Label")).Select(x => x.Value).FirstOrDefault() ?? "";
                    Log(string.Format(Inv, "  {0} {1}: {2} | walking surface {3} | {4}", sweep, v, Line("Fundamental"), label, Line("Largest")));
                }
                Slider(sweep, opened);
                doc.NewSolution(false);
            }));
        list.Add(Do("response", () =>
        {
            ShowOnly(Find<IGH_DocumentObject>("Response factor map"), Find<IGH_DocumentObject>("Peak marker"),
                     Find<IGH_Component>("Response factor"), Find<IGH_Component>("Largest R"), Find<IGH_Component>("Stair at rest"));
            MapEdges(true);
            Shoot(Aim(Grown(Drawn(), 0.03), eye, Vector3d.ZAxis, 50, zoom), name + "-response", WithLegend);
            Shoot(Aim(Grown(Drawn(), 0.03), above, Vector3d.ZAxis, 50, zoom), name + "-response-above", WithLegend);
            MapEdges(false);
        }));
        list.Add(Do("mode", () =>
        {
            var view = Find<IGH_Component>("Stair mode");
            ShowOnly(view, Find<IGH_Component>("Stair mode legend"));
            Slider("Mode", fundamental);
            doc.NewSolution(false);
            Log("  " + Info(view));
            Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis, 50, zoom * 0.88), name + "-mode", WithLegend);
            Vibrating(view, eye, name + "-mode-frames", 36, 1.0);
        }));
        if (responseViews != null || modeViews != null)
            list.Add(Do("views", () =>
            {
                // The response factor from each of responseViews and the fundamental mode from each of
                // modeViews: (its name, the eye, the zoom). Straight down is turned with +Y up.
                RhinoView At(BoundingBox box, Vector3d at, double z) =>
                    Aim(box, at, Math.Abs(at.Z) > 0.999 * at.Length ? Vector3d.YAxis : Vector3d.ZAxis, 50, z);
                if (responseViews != null)
                {
                    var tag = Find<IGH_Component>("Largest R");
                    ShowOnly(Find<IGH_DocumentObject>("Response factor map"), Find<IGH_DocumentObject>("Peak marker"),
                             Find<IGH_Component>("Response factor"), tag, Find<IGH_Component>("Stair at rest"));
                    MapEdges(true);
                    // The label lies flat on the point it is given, in world XY, and reads upside down from
                    // the +Y side: for each view it is laid in a level plane turned to face the camera, and
                    // wired back after (the definition is closed unsaved).
                    var location = (GH_PersistentParam<GH_Plane>)tag.Params.Input[0];
                    var sources = location.Sources.ToList();
                    var label = location.VolatileData.AllData(true).OfType<GH_Plane>().Select(p => p.Value.Origin).FirstOrDefault();
                    foreach (var (suffix, at, z) in responseViews)
                    {
                        var right = Vector3d.CrossProduct(-at, Vector3d.ZAxis);
                        if (!right.Unitize()) right = Vector3d.XAxis;
                        location.RemoveAllSources();
                        location.PersistentData.Clear();
                        location.PersistentData.Append(new GH_Plane(new Plane(label, right, Vector3d.CrossProduct(Vector3d.ZAxis, right))));
                        location.ExpireSolution(false);
                        doc.NewSolution(false);
                        Shoot(At(Grown(Drawn(), 0.03), at, z), name + "-response-" + suffix, WithLegend);
                    }
                    location.PersistentData.Clear();
                    foreach (var s in sources) location.AddSource(s);
                    location.ExpireSolution(false);
                    doc.NewSolution(false);
                    MapEdges(false);
                }
                if (modeViews != null)
                {
                    var view = Find<IGH_Component>("Stair mode");
                    ShowOnly(view, Find<IGH_Component>("Stair mode legend"));
                    Slider("Mode", fundamental);
                    doc.NewSolution(false);
                    foreach (var (suffix, at, z) in modeViews)
                        Shoot(At(Grown(Drawn(), 0.05), at, z * 0.88), name + "-mode-" + suffix, WithLegend);
                }
            }));
        if (spin != null)
            list.Add(Do("spin", () =>
            {
                // The fundamental mode swinging while the camera goes once round the stair, from spin.
                var view = Find<IGH_Component>("Stair mode");
                ShowOnly(view, Find<IGH_Component>("Stair mode legend"));
                Slider("Mode", fundamental);
                doc.NewSolution(false);
                SpinningVibrating(view, spin.Value, name + "-mode-spin-frames", 96, 4, 1.0, 1.5, 0.12);
            }));
        list.Add(Do("close", Close));
        return list;
    }

    /// <summary>
    /// A mode swinging <paramref name="swings"/> times, reach times the scale it is drawn at, while the
    /// camera goes once round it about the vertical through its middle, from along <paramref name="eye"/>:
    /// <paramref name="frames"/> frames into folder, a loop. Framed once, on what is drawn at both ends of
    /// the swing, as seen from eye, magnified <paramref name="zoom"/> times and slid <paramref name="slide"/>
    /// of the width left, towards the legend, and turned whole after, so it does not breathe: a model about
    /// as wide from every side, a stair round a well, stays in the picture. The colours fade with the swing
    /// (<see cref="FadeWithSwing"/>).
    /// </summary>
    static void SpinningVibrating(IGH_Component results, Vector3d eye, string folder, int frames, int swings, double reach,
                                  double zoom = 1.0, double slide = 0.0)
    {
        var scale = Field(results, "_slDeformScale");
        var value = scale.GetType().GetProperty("Value");
        double start = Convert.ToDouble(value.GetValue(scale));
        SetWidget(results, "_ckDeformed", "Active", true);
        scale.GetType().GetProperty("MinValue").SetValue(scale, -2.0);
        scale.GetType().GetProperty("MaxValue").SetValue(scale, 2.0);
        value.SetValue(scale, reach);
        results.ExpireSolution(false);
        doc.NewSolution(false);
        var box = Drawn();
        var own = ((IGH_PreviewObject)results).ClippingBox;
        value.SetValue(scale, -reach);
        results.ExpireSolution(false);
        doc.NewSolution(false);
        box.Union(Drawn());
        own.Union(((IGH_PreviewObject)results).ClippingBox);
        var view = Aim(Grown(box, 0.06), eye, Vector3d.ZAxis, 50, zoom);
        if (slide != 0) Slide(view, slide);
        var vp = view.ActiveViewport;
        // The camera and its target turned together about the vertical through the middle of the model,
        // so that it turns where it stands: the target is off that middle once slid, and turning round
        // the target would carry the model across the picture.
        var location = vp.CameraLocation;
        var target = vp.CameraTarget;
        var middle = own.Center;
        Directory.CreateDirectory(Path.Combine(outDir, folder));
        FadeWithSwing(results, reach);
        try
        {
            for (int k = 0; k < frames; k++)
            {
                value.SetValue(scale, reach * Math.Sin(2 * Math.PI * swings * k / frames));
                results.ExpireSolution(false);
                doc.NewSolution(false);
                var round = Transform.Rotation(2 * Math.PI * k / frames, Vector3d.ZAxis, middle);
                Point3d at = location, to = target;
                at.Transform(round);
                to.Transform(round);
                vp.SetCameraLocations(to, at);
                vp.CameraUp = Vector3d.ZAxis;
                Shoot(view, Path.Combine(folder, k.ToString("D3")));
            }
        }
        finally
        {
            FadeWithSwing(results, 0);
        }
        value.SetValue(scale, start);
        results.ExpireSolution(false);
        doc.NewSolution(false);
        Log($"  {frames} frames once round, swinging {swings} times +-{reach} times the mode's drawn size");
    }

    /// <summary>
    /// The edges of Grasshopper's meshes drawn, light grey and opaque (160, 168, 178), as every meshed
    /// model in the pictures has them; off, the default red preview colour back.
    /// </summary>
    static void MapEdges(bool on)
    {
        CentralSettings.PreviewMeshEdges = on;
        var colour = doc.GetType().GetProperty("PreviewColour");
        if (colour != null && colour.CanWrite)
            colour.SetValue(doc, on ? Color.FromArgb(255, 160, 168, 178) : Color.FromArgb(150, 255, 0, 0));
        doc.NewSolution(false);
    }

    /// <summary>
    /// Model View's colours faded with the swing, as its Animate fades them: all the colour of zero at
    /// rest and the result's own at <paramref name="reach"/>, the scale of the full swing. 0 puts them
    /// back. Every deforming animation does it, mode shapes and buckling modes too (the user,
    /// 2026-11-07); the still at full deformation is the same either way.
    /// </summary>
    static void FadeWithSwing(IGH_Component results, double reach)
    {
        for (var t = results.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField("_swingReach", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) { f.SetValue(results, reach); return; }
        }
        if (reach > 0) Log($"  {results.NickName}: the Alpaca4d loaded cannot fade the colours with the swing - build and deploy the plugin first");
    }

    /// <summary>
    /// A mode swinging through its rest and back, reach times the scale it is drawn at, one frame each
    /// into folder: Model View's deformation scale opened to -2 to 2 for it, and put back after. The
    /// camera is set once, on what is drawn at the full swing, so the frames do not jitter. The colours
    /// fade with the swing (<see cref="FadeWithSwing"/>).
    /// </summary>
    static void Vibrating(IGH_Component results, Vector3d eye, string folder, int frames, double reach, RhinoView camera = null)
    {
        var scale = Field(results, "_slDeformScale");
        var value = scale.GetType().GetProperty("Value");
        double start = Convert.ToDouble(value.GetValue(scale));
        SetWidget(results, "_ckDeformed", "Active", true);
        scale.GetType().GetProperty("MinValue").SetValue(scale, -2.0);
        scale.GetType().GetProperty("MaxValue").SetValue(scale, 2.0);
        value.SetValue(scale, reach);
        results.ExpireSolution(false);
        doc.NewSolution(false);
        var box = Drawn();
        value.SetValue(scale, -reach);
        results.ExpireSolution(false);
        doc.NewSolution(false);
        box.Union(Drawn());
        var view = camera ?? Aim(Grown(box, 0.04), eye, Vector3d.ZAxis);
        Directory.CreateDirectory(Path.Combine(outDir, folder));
        FadeWithSwing(results, reach);
        try
        {
            for (int k = 0; k < frames; k++)
            {
                value.SetValue(scale, reach * Math.Sin(2 * Math.PI * k / frames));
                results.ExpireSolution(false);
                doc.NewSolution(false);
                Shoot(view, Path.Combine(folder, k.ToString("D3")));
            }
        }
        finally
        {
            FadeWithSwing(results, 0);
        }
        value.SetValue(scale, start);
        results.ExpireSolution(false);
        doc.NewSolution(false);
        Log($"  {frames} frames, swinging +-{reach} times the mode's drawn size");
    }

    // ------------------------------------------------------------------ the tank's hundred modes

    /// <summary>
    /// TankModes.gh, from Tools/Examples/Structures.py: its canvas; the hundred modes on their grid, a
    /// close look at some of them, as the cover (and again with Grasshopper's mesh edges drawing the
    /// shells' edges, dark), the whole grid receding, and the grid from above with each mode's number
    /// and frequency; and the close look swinging, every mode at once.
    /// </summary>
    static List<(string, Func<bool>)> Tank() => new List<(string, Func<bool>)>
    {
        Do("open", () => Open("TankModes.gh")),
        Do("modes", () =>
        {
            foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                Messages(c);
            var modes = doc.Objects.OfType<IGH_Component>().First(c => c.Name.StartsWith("Natural Vibration"));
            var f = Values<GH_Number>(Out(modes, "Frequencies")).Select(n => n.Value).ToList();
            Log($"  {f.Count} modes: " + string.Join(", ", f.Take(12).Select(x => x.ToString("F2", Inv))) + $" ... {f.LastOrDefault():F2} Hz");
            var view = Find<IGH_Component>("Mode shapes");
            foreach (var name in new[] { "Geometry", "Values", "Colors" })
                Log($"  {name} paths " + string.Join(" ", Out(view, name).VolatileData.Paths.Take(3).Select(x => x.ToString())) +
                    $" ... {Out(view, name).VolatileData.PathCount} branches");
            Messages(Find<IGH_Component>("Mode shapes legend"));
            Canvas("tank-canvas");
        }),
        Do("stills", () =>
        {
            var every = Find<IGH_Param>("Every mode");
            var meshes = Values<GH_Mesh>(every).Select(m => m.Value).ToList();
            Log($"  {meshes.Count} meshes drawn, {meshes.FirstOrDefault()?.Faces.Count} faces each");
            // The cover: some of the grid, close, from the front and above, cropped at its sides as
            // the tanks go on past them; and the same with the shells' edges drawn.
            ShowOnly(every);
            Shoot(TankCover(meshes), "tank-modes", 2);
            TankEdges(true);
            Shoot(TankCover(meshes), "tank-modes-edges", 2);
            TankEdges(false);
            // The whole grid, receding.
            Shoot(Aim(Drawn(), new Vector3d(0, -1, 0.6), Vector3d.ZAxis, 50, 1.6), "tank-modes-field", 2);
            // From above and in front, with each mode's number and frequency, and the legend.
            var tags = Find<IGH_Component>("Mode and frequency");
            var legend = Find<IGH_Component>("Mode shapes legend");
            foreach (var c in new[] { tags, legend })
            {
                Messages(c);
                Log($"  {c.NickName}: " + string.Join(", ", c.Params.Input.Select(p => $"{p.NickName} {p.VolatileDataCount}")));
            }
            // Solved again once shown: Text Tag 3D, hidden when it was last solved, draws nothing until then.
            ShowOnly(every, tags, legend);
            tags.ExpireSolution(false);
            doc.NewSolution(false);
            Shoot(Aim(Drawn(), new Vector3d(0, -0.55, 1), Vector3d.ZAxis), "tank-modes-grid", WithLegend);
        }),
        Do("swinging", () =>
        {
            var every = Find<IGH_Param>("Every mode");
            var meshes = Values<GH_Mesh>(every).Select(m => m.Value).ToList();
            ShowOnly(every);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Vibrating(Find<IGH_Component>("Mode shapes"), Vector3d.ZAxis, "tank-modes-frames", 36, 1.0, TankCover(meshes));
            Log($"  {watch.Elapsed.TotalSeconds:F0} s for the frames");
        }),
        Do("close", Close),
    };

    /// <summary>The cover's camera: rows 2 to 5 of the grid, columns 2 to 7, from the front and above.</summary>
    static RhinoView TankCover(List<Mesh> meshes)
    {
        var box = BoundingBox.Empty;
        for (int row = 2; row <= 5; row++)
            for (int col = 2; col <= 7; col++)
            {
                int k = row * 10 + col;
                if (k < meshes.Count) box.Union(meshes[k].GetBoundingBox(true));
            }
        return Aim(box, new Vector3d(0.1, -1, 0.9), Vector3d.ZAxis, 50, 1.3);
    }

    /// <summary>Grasshopper's mesh edges on, dark, for the tanks' shells; or off again.</summary>
    static void TankEdges(bool on)
    {
        CentralSettings.PreviewMeshEdges = on;
        var colour = doc.GetType().GetProperty("PreviewColour");
        if (colour != null && colour.CanWrite)
            colour.SetValue(doc, on ? Color.FromArgb(255, 42, 45, 49) : Color.FromArgb(150, 255, 0, 0));
        else Log("  no PreviewColour on the document");
        doc.NewSolution(false);
    }

    // ------------------------------------------------------------------ the response spectrum sticks

    /// <summary>
    /// ResponseSpectrumSticks.gh, from Tools/Examples/Structures.py: its canvas; the sticks at the step
    /// they move the most, all together, with the spectrum they draw standing behind them; and a frame
    /// every 0.1 s through the record, the spectrum growing behind the sticks as each reaches further,
    /// each with its charts: the ground's acceleration, and the spectrum so far over the whole one.
    /// </summary>
    static List<(string, Func<bool>)> Spectrum() => new List<(string, Func<bool>)>
    {
        Do("open", () => Open("ResponseSpectrumSticks.gh")),
        Do("sticks", () =>
        {
            foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                Messages(c);
            Canvas("spectrum-canvas");
            var view = Find<IGH_Component>("Sticks shaking");
            var legend = Find<IGH_Component>("Sticks shaking legend");
            var step = doc.Objects.OfType<GH_NumberSlider>().First(x => x.NickName == "Step");
            var record = Find<IGH_Component>("El Centro 1940");
            var chart = Find<IGH_Component>("Response spectrum");
            var history = Find<IGH_Component>("Tops, every step");

            // The record, each stick's period and peak, and each top's movement along x at every step, mm.
            var s = new Sticks
            {
                Times = Values<GH_Number>(Out(record, "Times")).Select(n => n.Value).ToArray(),
                Ground = Values<GH_Number>(Out(record, "Accelerations")).Select(n => n.Value / 9.81).ToArray(),
                Periods = Values<GH_Number>(Out(chart, "Period")).Select(n => n.Value).ToArray(),
                Spectrum = Values<GH_Number>(Out(chart, "PSa")).Select(n => n.Value).ToArray(),
            };
            var tree = Out(history, "Displacement").VolatileData;
            s.Moves = s.Periods.Select(_ => new List<double>()).ToList();
            foreach (var path in tree.Paths)
            {
                var branch = tree.get_Branch(path);
                for (int i = 0; i < s.Moves.Count && i < branch.Count; i++)
                    s.Moves[i].Add(((GH_Vector)branch[i]).Value.X * 1000.0);
            }
            int steps = s.Moves[0].Count;
            s.Dt = s.Times[s.Times.Length - 1] / steps;
            Log($"  {s.Periods.Length} sticks, {steps} steps of {s.Dt:G3} s; periods {s.Periods.First():F3} to {s.Periods.Last():F3} s; " +
                $"PSa most {s.Spectrum.Max():F3} g at {s.Periods[Array.IndexOf(s.Spectrum, s.Spectrum.Max())]:F2} s");

            // The spectrum, drawn behind the sticks: the definition's own line and grid, in the brand
            // purple and light grey, its Python component's own preview put away.
            var tops = Values<GH_Point>(Out(Find<IGH_Component>("Response spectrum sticks"), "Tops")).Select(p => p.Value).ToList();
            var spectrum = Place(new Param_Mesh(), Beside(chart, 160, 0));
            foreach (var c in Values<GH_Curve>(Out(chart, "Spectrum"))) spectrum.PersistentData.Append(Pipe(c.Value));
            var line = Preview(spectrum, Purple, Beside(chart, 160, 60));
            var grid = Preview(Out(chart, "Grid"), Color.FromArgb(208, 212, 217), Beside(chart, 160, 120));
            // Model View draws its last branch only: the definition's Every stick draws them all.
            var allSticks = Find<IGH_Param>("Every stick");
            // Each stick's mass, a ball on its top, moved as Model View moves the top: the deformation
            // scale times how far it has moved along x.
            var masses = Place(new Param_Mesh(), Beside(chart, 160, 360));
            var massesPreview = Preview(masses, DarkTech, Beside(chart, 160, 420));
            var scaleWidget = Field(view, "_slDeformScale");
            double drawn = Convert.ToDouble(scaleWidget.GetType().GetProperty("Value").GetValue(scaleWidget));

            // The still: the step the sticks reach furthest together, each against its own most.
            int best = 0;
            double most = -1;
            for (int k = 0; k < steps; k++)
            {
                double sum = 0;
                for (int i = 0; i < s.Moves.Count; i++) sum += Math.Abs(s.Moves[i][k]) / Math.Max(1e-9, s.Moves[i].Max(Math.Abs));
                if (sum > most) { most = sum; best = k; }
            }
            ShowOnly(allSticks, legend, line, grid, massesPreview);
            Heads(masses, tops, s, best, drawn);
            AtStep(step, best, view);
            var eye = new Vector3d(-0.45, -1.4, 0.32);
            var still = Aim(Grown(Drawn(), 0.06), eye, Vector3d.ZAxis);
            Shoot(still, "response-spectrum", WithLegend);
            Log($"  still at step {best}, {(best + 1) * s.Dt:F2} s");
            SticksCharts(Path.Combine(outDir, "response-spectrum-chart.png"), s, steps - 1, WithLegend);

            // The frames: the spectrum so far, a curve of the definition's own, growing behind the sticks.
            var sofar = Place(new Param_Mesh(), Beside(chart, 160, 240));
            var sofarPreview = Preview(sofar, Purple, Beside(chart, 160, 300));
            ShowOnly(allSticks, legend, sofarPreview, grid, massesPreview);
            // The frames keep the still's legend: Shoot captures them at the still's size and shrinks them.
            Directory.CreateDirectory(Path.Combine(outDir, "response-spectrum-frames"));
            Directory.CreateDirectory(Path.Combine(outDir, "response-spectrum-charts"));
            int every = (int)Math.Round(0.1 / s.Dt), frame = 0;
            for (int k = 0; k < steps; k += every, frame++)
            {
                var reached = s.Reached(k);
                sofar.PersistentData.Clear();
                sofar.PersistentData.Append(Pipe(new Polyline(tops.Select((p, i) =>
                    new Point3d(p.X, 3.0, reached[i] * 8.0))).ToNurbsCurve()));
                sofar.ExpireSolution(false);
                Heads(masses, tops, s, k, drawn);
                AtStep(step, k, view);
                Shoot(still, Path.Combine("response-spectrum-frames", frame.ToString("D3")));
                SticksCharts(Path.Combine(outDir, "response-spectrum-charts", frame.ToString("D3") + ".png"), s, k, 1);
            }
            Log($"  {frame} frames and their charts, a frame every 0.1 s");
        }),
        Do("close", Close),
    };

    /// <summary>A flat frame round box in plan, thick wide and lifted up: a section's boundary, drawn heavier than a line.</summary>
    static Mesh Frame(BoundingBox box, double thick, double up)
    {
        var m = new Mesh();
        double x0 = box.Min.X, x1 = box.Max.X, y0 = box.Min.Y, y1 = box.Max.Y, t = thick;
        foreach (var (a, b, c, d) in new[] {
            (x0 - t, y0 - t, x1 + t, y0), (x0 - t, y1, x1 + t, y1 + t), (x0 - t, y0, x0, y1), (x1, y0, x1 + t, y1) })
        {
            int i = m.Vertices.Count;
            m.Vertices.Add(a, b, up); m.Vertices.Add(c, b, up); m.Vertices.Add(c, d, up); m.Vertices.Add(a, d, up);
            m.Faces.AddFace(i, i + 1, i + 2, i + 3);
        }
        m.Normals.ComputeNormals();
        return m;
    }

    static readonly Guid TextTagId = new Guid("5A41528B-12B9-40DC-A3F2-842034D267C4");

    /// <summary>Grasshopper's Text Tag 3D: text in the XY plane at at, size tall in model units, in colour.</summary>
    static IGH_Component Tag(string text, Point3d at, double size, Color colour, PointF canvasAt)
    {
        var tag = Place((IGH_Component)Instances.ComponentServer.EmitObject(TextTagId), canvasAt);
        foreach (var p in tag.Params.Input)
        {
            if (p is Param_Plane plane) { plane.PersistentData.Clear(); plane.PersistentData.Append(new GH_Plane(new Plane(at, Vector3d.ZAxis))); }
            else if (p is Param_String s) { s.PersistentData.Clear(); s.PersistentData.Append(new GH_String(text)); }
            else if (p is Param_Number n) { n.PersistentData.Clear(); n.PersistentData.Append(new GH_Number(size)); }
            else if (p is Param_Colour c) { c.PersistentData.Clear(); c.PersistentData.Append(new GH_Colour(colour)); }
            p.ExpireSolution(false);
        }
        Log("  tag inputs: " + string.Join(", ", tag.Params.Input.Select(p => p.Name + ":" + p.GetType().Name)));
        return tag;
    }

    /// <summary>A curve as a pipe 16 cm thick, so that it reads in a picture as a line drawn on screen does not.</summary>
    static GH_Mesh Pipe(Curve curve) => new GH_Mesh(Mesh.CreateFromCurvePipe(curve, 0.08, 8, 1, MeshPipeCapStyle.Dome, false));

    /// <summary>A ball on each stick's top at step k, where Model View draws the top: scale times its movement along x.</summary>
    static void Heads(Param_Mesh masses, List<Point3d> tops, Sticks s, int k, double scale)
    {
        masses.PersistentData.Clear();
        for (int i = 0; i < tops.Count && i < s.Moves.Count; i++)
        {
            var at = tops[i] + new Vector3d(scale * s.Moves[i][Math.Min(k, s.Moves[i].Count - 1)] / 1000.0, 0, 0);
            masses.PersistentData.Append(new GH_Mesh(Mesh.CreateFromSphere(new Sphere(at, 0.22), 16, 12)));
        }
        masses.ExpireSolution(false);
    }

    /// <summary>The sticks' charts: the record, and the spectrum they draw.</summary>
    class Sticks
    {
        /// <summary>The record: its times, s, and the ground's acceleration then, g.</summary>
        public double[] Times, Ground;
        /// <summary>Each stick's period, s, and its pseudo-acceleration at the end, g: the spectrum.</summary>
        public double[] Periods, Spectrum;
        /// <summary>Each stick's top's movement along x, mm, at every step of Dt.</summary>
        public List<List<double>> Moves;
        public double Dt;

        /// <summary>Each stick's pseudo-acceleration so far, g: the most it has moved up to step k, times (2 pi / T) squared.</summary>
        public double[] Reached(int k) => Periods.Select((T, i) =>
            Math.Pow(2 * Math.PI / T, 2) * Moves[i].Take(k + 1).Max(Math.Abs) / 1000.0 / 9.81).ToArray();
    }

    /// <summary>
    /// The sticks' charts at step k, 2400 px wide times scale: the ground's acceleration over the record
    /// and, under it, the spectrum so far - each stick's pseudo-acceleration up to now, at its period -
    /// over the whole spectrum in light grey.
    /// </summary>
    static void SticksCharts(string path, Sticks s, int k, double scale)
    {
        float f = (float)scale;
        int w = (int)(2400 * f), h = (int)(1000 * f);
        double t = (k + 1) * s.Dt;
        using (var bitmap = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Background);
            var ground = new RectangleF(220 * f, 90 * f, w - 300 * f, 220 * f);
            TimeChart(g, ground, s.Times, new List<double[]> { s.Ground }, t, "Ground acceleration [g]",
                      new[] { DarkTech }, null, "g", "0.0#", f, true);
            SpectrumChart(g, new RectangleF(220 * f, 480 * f, w - 300 * f, 400 * f), s, s.Reached(k), f);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>The spectrum so far over the whole one, period along, pseudo-acceleration up.</summary>
    static void SpectrumChart(Graphics g, RectangleF r, Sticks s, double[] sofar, float f)
    {
        double right = Math.Ceiling(s.Periods.Max() * 2) / 2, top = Nice(s.Spectrum.Max() * 1.05);
        Func<double, float> X = x => r.Left + (float)(x / right) * r.Width;
        Func<double, float> Y = y => r.Bottom - (float)(y / top) * r.Height;
        using (var big = new Font(ChartFont, 42 * f, GraphicsUnit.Pixel))
        using (var small = new Font(ChartFont, 32 * f, GraphicsUnit.Pixel))
        using (var axis = new Pen(ChartAxis, 2 * f))
        using (var ink = new SolidBrush(DarkTech))
        {
            for (double v = 0; v <= top + 1e-9; v += top / 4)
            {
                g.DrawLine(axis, r.Left, Y(v), r.Right, Y(v));
                var label = v.ToString("0.0#", Inv);
                var size = g.MeasureString(label, small);
                g.DrawString(label, small, ink, r.Left - size.Width - 14 * f, Y(v) - size.Height / 2);
            }
            for (double p = 0; p <= right + 1e-9; p += 0.5)
            {
                var label = p.ToString("0.0", Inv) + " s";
                var size = g.MeasureString(label, small);
                g.DrawString(label, small, ink, X(p) - size.Width / 2, r.Bottom + 12 * f);
            }
            g.DrawString("Response spectrum, 5% damping: pseudo-acceleration [g] against period", big, ink, r.Left - 8 * f, r.Top - 78 * f);
            var whole = s.Periods.Select((T, i) => new PointF(X(T), Y(s.Spectrum[i]))).ToArray();
            using (var faint = new Pen(ChartFaint, 4 * f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                g.DrawLines(faint, whole);
            var now = s.Periods.Select((T, i) => new PointF(X(T), Y(sofar[i]))).ToArray();
            using (var pen = new Pen(Purple, 5 * f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                g.DrawLines(pen, now);
            using (var dot = new SolidBrush(Purple))
                foreach (var p in now) g.FillEllipse(dot, p.X - 8 * f, p.Y - 8 * f, 16 * f, 16 * f);
        }
    }

    // ------------------------------------------------------------------ the Rebellious Slave

    /// <summary>
    /// The tetrahedra's edges, light grey: without them the statue reads as a scan, not as a model;
    /// darker, they bury its colours. Not see-through: Rhino draws see-through wires over the shaded
    /// mesh, the far side's too.
    /// </summary>
    static readonly Color SlaveEdges = Color.FromArgb(160, 168, 178);

    /// <summary>
    /// RebelliousSlave.gh: its canvas, the vertical stress from in front and from its left, and its first
    /// two modes, each a still and a loop of it swaying, with the tetrahedra's edges drawn.
    /// </summary>
    static List<(string, Func<bool>)> Slave()
    {
        var front = new Vector3d(0.45, -1.4, 0.2);
        var left = new Vector3d(1.4, -0.35, 0.2);
        var list = new List<(string, Func<bool>)>
        {
            Do("open", () => { SoftEdges(SlaveEdges); Open("RebelliousSlave.gh"); }),
            Do("stress", () =>
            {
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                    Messages(c);
                var stress = Find<IGH_Component>("Vertical stress");
                ShowOnly(stress, Find<IGH_Component>("Vertical stress legend"));
                doc.NewSolution(false);
                Log("  " + Info(stress));
                Shoot(Aim(Grown(Drawn(), 0.04), front, Vector3d.ZAxis), "slave-stress", WithLegend);
                Shoot(Aim(Grown(Drawn(), 0.04), left, Vector3d.ZAxis), "slave-stress-left", WithLegend);
                Canvas("slave-canvas");
            }),
        };
        foreach (int m in new[] { 0, 1 })
            list.Add(Do("mode " + m, () =>
            {
                var modal = Find<IGH_Component>("Mode shape");
                ShowOnly(modal, Find<IGH_Component>("Mode shape legend"));
                Slider("Mode, from 0", m);
                doc.NewSolution(false);
                Log("  " + Info(modal));
                var eye = m == 0 ? left : front;
                Shoot(Aim(Grown(Drawn(), 0.04), eye, Vector3d.ZAxis), "slave-mode-" + m, WithLegend);
                Vibrating(modal, eye, "slave-mode-" + m + "-frames", 36, 1.5);
            }));
        list.Add(Do("close", () => { Close(); HardEdges(); }));
        return list;
    }

    // ------------------------------------------------------------------ moment curvature

    static readonly Guid MomentCurvatureId = new Guid("5A456D82-1C30-4EE1-AAC7-876846328C8F");
    static readonly Guid FiberPointId = new Guid("934021CE-8D12-4FCA-8CF1-9158EB08208A");
    static readonly Guid DiagramId = new Guid("B7297C55-C18D-43C1-A082-979958576A8F");

    /// <summary>
    /// MomentCurvature.gh: its canvas, and the section beside its moment-curvature diagram from above -
    /// the concrete as a light grey mesh and each bar as a disc of its own diameter, where Fiber
    /// Section's own preview would draw dots on a wire mesh.
    /// </summary>
    static List<(string, Func<bool>)> MomentCurvatureScene() => new List<(string, Func<bool>)>
    {
        Do("open", () => Open("MomentCurvature.gh")),
        Do("pictures", () =>
        {
            foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                Messages(c);
            var mc = Components(MomentCurvatureId).First();
            foreach (var name in new[] { "YieldMoment", "UltimateMoment", "UltimateCurvature", "Ductility", "YieldStiffness", "Failure" })
                Log($"  {name}: " + string.Join(", ", Out(mc, name).VolatileData.AllData(true).Select(v => v.ToString())));
            Canvas("moment-curvature-canvas");

            // The section as a cover reads it: the concrete filled, its fibres a faint grid, its boundary
            // a dark frame, each bar a disc of its own diameter - Fiber Section's own preview draws dots on
            // a wire mesh - beside the diagram Moment Curvature Diagram draws.
            var fibreMesh = Values<GH_Mesh>(Out(Find<IGH_Component>("Fibres"), "Mesh")).First().Value;
            var concrete = Preview(Out(Find<IGH_Component>("Fibres"), "Mesh"), Color.FromArgb(172, 184, 200), Beside(mc, 160, 0));
            var grid = Place(new Param_Line(), Beside(mc, 160, 60));
            for (int e = 0; e < fibreMesh.TopologyEdges.Count; e++)
            {
                var line = fibreMesh.TopologyEdges.EdgeLine(e);
                line.Transform(Transform.Translation(0, 0, 0.0005));
                grid.PersistentData.Append(new GH_Line(line));
            }
            var gridPreview = Preview(grid, Color.FromArgb(200, 208, 219), Beside(mc, 160, 90));
            var box = fibreMesh.GetBoundingBox(true);
            var frame = Place(new Param_Mesh(), Beside(mc, 160, 120));
            frame.PersistentData.Append(new GH_Mesh(Frame(box, 0.005, 0.001)));
            var framePreview = Preview(frame, DarkTech, Beside(mc, 160, 150));
            var bars = Place(new Param_Mesh(), Beside(mc, 160, 180));
            var fibres = Components(FiberPointId).First();
            var points = ((GH_PersistentParam<GH_Point>)In(fibres, "Point")).PersistentData.AllData(true).OfType<GH_Point>();
            foreach (var p in points)
            {
                var disc = Mesh.CreateFromPlanarBoundary(new Circle(new Plane(p.Value + new Vector3d(0, 0, 0.0015), Vector3d.ZAxis), 0.012).ToNurbsCurve(),
                                                         MeshingParameters.Default, 0.0001);
                bars.PersistentData.Append(new GH_Mesh(disc));
            }
            var barsPreview = Preview(bars, DarkTech, Beside(mc, 160, 210));

            // What the cover says, in text drawn in the model, so that it scales with the picture as the
            // diagram's own labels, a fixed number of pixels tall, do not.
            double First(string name) => Values<GH_Number>(Out(mc, name)).Select(n => n.Value).First();
            var diagram = Components(DiagramId).First();
            double kScale = Values<GH_Number>(Out(diagram, "CurvatureScale")).First().Value;
            double mScale = Values<GH_Number>(Out(diagram, "MomentScale")).First().Value;
            // Where the diagram is drawn: its Plane's origin, κ along its X, M along its Y.
            var origin = Values<GH_Plane>(In(diagram, "Plane")).Select(x => x.Value.Origin).First();
            double mY = First("YieldMoment"), mU = First("UltimateMoment"), kU = First("UltimateCurvature"), ei = First("YieldStiffness");
            Point3d At(double k, double m) => origin + new Vector3d(k * kScale, m * mScale, 0);
            var yieldAt = At(mY / ei, mY);
            var ultimateAt = At(kU, mU);
            var tags = new List<IGH_DocumentObject>
            {
                Tag("300 x 500 mm", new Point3d(box.Min.X, box.Min.Y - 0.07, 0), 0.03, DarkTech, Beside(mc, 160, 260)),
                Tag("8 + 3 \u03c624 bars, N = \u2212400 kN", new Point3d(box.Min.X, box.Min.Y - 0.115, 0), 0.02, Color.FromArgb(93, 100, 112), Beside(mc, 160, 300)),
                Tag($"first yield, {mY:0} kNm", yieldAt + new Vector3d(-0.05, -0.075, 0), 0.022, Orange, Beside(mc, 160, 340)),
                Tag($"concrete crushes, {mU:0} kNm", ultimateAt + new Vector3d(-0.36, 0.04, 0), 0.022, Color.FromArgb(209, 36, 47), Beside(mc, 160, 380)),
            };
            Log($"  yield at {yieldAt}, ultimate at {ultimateAt}");
            ShowOnly(new IGH_DocumentObject[] { diagram, concrete, gridPreview, framePreview, barsPreview }.Concat(tags).ToArray());
            doc.NewSolution(false);
            // From straight above with a long lens, on the pictures' grey.
            Shoot(Aim(Grown(Drawn(), 0.05), new Vector3d(0, 0, 1), Vector3d.YAxis, 200), "moment-curvature", 0.8);
        }),
        Do("close", Close),
    };

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
            Do("load moving", () =>
            {
                // The load walked from the start to the tip, a node at a time: 30 elements, the slider a
                // fraction of them, so node k is at k/30. The beam sized again at each.
                CurvedBeamFrames();
                var load = Find<GH_NumberSlider>("Load [kN]").CurrentValue;
                Sizing(Find<IGH_Component>("Each element"), Components(ModelViewId).First(), "Load at",
                       Enumerable.Range(2, 29).Select(k => k / 30.0).ToList(), "curved-beam-moving-load", "IPE section",
                       () => $"{load:0} kN at {NodeX("Loaded", 0):0.0} m", new Vector3d(0.35, -1, 0.3), 0, 1);
            }),
            Do("support moving", () =>
            {
                // The roller walked from near the start to past the load, the load staying where it is.
                Sizing(Find<IGH_Component>("Each element"), Components(ModelViewId).First(), "Third support at",
                       Enumerable.Range(6, 23).Select(k => k / 30.0).ToList(), "curved-beam-moving-support", "IPE section",
                       () => $"Support at {NodeX("Supported", 0):0.0} m", new Vector3d(0.35, -1, 0.3), 0, 1);
            }),
            Do("close", Close),
        };
    }

    /// <summary>
    /// The curved beam set up for its frames: the Section Optimiser that gives the whole beam one section
    /// locked, so that only the one sizing each element runs; two supports, not the example's three;
    /// Model View drawing the supports and the load.
    /// </summary>
    static void CurvedBeamFrames()
    {
        Find<IGH_Component>("Whole beam").Locked = true;
        // Built in at the start and one roller, at the third support's place: the second support's slider
        // taken off the nodes the rollers stand on.
        var nodes = Find<IGH_Component>("Support nodes");
        nodes.Params.Input[0].RemoveSource(Find<GH_NumberSlider>("Second support at"));
        nodes.ExpireSolution(false);
        // The two supports along the beam hold it up only, Tz, and Model View draws a support that is none
        // of its seven kinds as a label of what it holds. Held sideways as well, Ty and Rz, they are
        // rollers along X, which it draws as such: out of the beam's plane, which no load is, so nothing
        // in the beam changes.
        var propped = Find<IGH_Component>("Held up");
        var held = Find<GH_BooleanToggle>("Held");
        foreach (var name in new[] { "Ty", "Rz" })
        {
            var p = In(propped, name);
            p.RemoveAllSources();
            p.AddSource(held);
        }
        propped.ExpireSolution(false);
        var view = Components(ModelViewId).First();
        SetWidget(view, "_ckShowSupports", "Active", true);
        SetWidget(view, "_ckShowLoads", "Active", true);
        SetWidget(view, "_ckShowLoadValues", "Active", false);
        // The supports at half their size: at the full size they stand taller than this slender beam is deep.
        SetWidget(view, "_slSupportScale", "Value", 0.5);
        LoadScale(view, 0.4);
        doc.NewSolution(false);
    }

    /// <summary>How far along X the node a List Item named <paramref name="nick"/> hands out is, the <paramref name="i"/>th of them.</summary>
    static double NodeX(string nick, int i) => Values<GH_Point>(Find<IGH_Component>(nick).Params.Output[0])[i].Value.X;

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
            Do("supports moving", () =>
            {
                // The supports walked in from the corners a bay at a time, as far as the slider goes - four
                // bays, of ten - and back, a second at each, the camera turning a quarter round over the
                // loop: the grid and its supports look the same from a quarter round, so the loop does not
                // jump. The pinned supports drawn, the element edges not: they make the lightest sections
                // black lines.
                SetWidget(sections, "_ckShowSupports", "Active", true);
                SetWidget(sections, "_ckWires", "Active", false);
                sections.ExpireSolution(false);
                double bay = (double)(Find<GH_NumberSlider>("Size [m]").CurrentValue / Find<GH_NumberSlider>("Bays").CurrentValue);
                Sizing(Find<IGH_Component>("Every element"), sections, "Supports, corner to centre",
                       new List<double> { 0, 0.25, 0.5, 0.75, 1 }, "beam-grid-moving-supports", "Section [kg/m]",
                       () =>
                       {
                           double k = Values<GH_Number>(Find<IGH_Component>("Bays in").Params.Output[0]).First().Value;
                           return k == 0 ? "Supports at the corners" : $"Supports {k * bay:0} m in from the edges";
                       },
                       eye, 90, 10, byMass: true);
            }),
            Do("close", Close),
        };
    }

    // ------------------------------------------------------------------ sections sized as something moves

    // A section's colour by its size, smallest to largest, through the brand's light blue, purple and orange.
    static readonly Color[] SizeStops = { LightBlue, Purple, Orange };

    /// <summary>The colour at <paramref name="t"/>, 0 to 1, along <see cref="SizeStops"/>.</summary>
    static Color SizeColour(double t)
    {
        t = Math.Max(0, Math.Min(1, t)) * (SizeStops.Length - 1);
        int k = Math.Min((int)Math.Floor(t), SizeStops.Length - 2);
        double f = t - k;
        Color a = SizeStops[k], b = SizeStops[k + 1];
        int Mix(int x, int y) => (int)Math.Round(x + (y - x) * f);
        return Color.FromArgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }

    /// <summary>
    /// The scale the sizes from <paramref name="smallest"/> to <paramref name="largest"/> are coloured
    /// over: round ends and a round step, ten swatches at most. Legend labels its swatches evenly from
    /// the least of its Values to the most, so they read 80, 120, 160 only when the scale is that.
    /// </summary>
    static (double low, double high, int count) SizeScale(int smallest, int largest)
    {
        foreach (var step in new[] { 10, 20, 25, 40, 50, 100, 200, 250, 500 })
        {
            int low = smallest / step * step, high = (largest + step - 1) / step * step;
            if ((high - low) / step + 1 <= 10) return (low, Math.Max(high, low + step), Math.Max(high, low + step) / step - low / step + 1);
        }
        return (smallest, largest, 10);
    }

    /// <summary>The number in a section's name, its size in its family: IPE240 is 240, HEA1000 1000.</summary>
    static int SizeOf(string name)
    {
        var digits = new string(name.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out int n) ? n : 0;
    }

    /// <summary>The section the optimiser gave each beam, by name, in the model's order.</summary>
    static List<string> SectionNames(IGH_Component optimiser) =>
        Values<IGH_Goo>(Out(optimiser, "Section")).Select(g => (g as GH_ObjectWrapper)?.Value?.ToString() ?? g.ToString()).ToList();

    /// <summary>A public property of <paramref name="o"/>, from its own type down: a section's Color is declared once.</summary>
    static PropertyInfo Property(object o, string name)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (p != null) return p;
        }
        return null;
    }

    /// <summary>
    /// Gives every section of the model the optimiser sized the colour its name has in
    /// <paramref name="colours"/>, as a Section Library's Colour input would. Model View, colouring by
    /// section, otherwise takes the next colour of its palette for each section in the order it meets
    /// them, and a section would change colour from one frame to the next.
    /// </summary>
    static int ColourSections(IGH_Component optimiser, Dictionary<string, Color> colours)
    {
        // Every model it hands out: an input with more than one item runs it more than once.
        int coloured = 0;
        foreach (var goo in Values<IGH_Goo>(Out(optimiser, "AlpacaModel")))
        {
            var model = (goo as GH_ObjectWrapper)?.Value ?? goo.ScriptVariable();
            foreach (var beam in (System.Collections.IEnumerable)Property(model, "Beams").GetValue(model))
            {
                var section = Property(beam, "Section").GetValue(beam);
                var name = section == null ? null : Property(section, "SectionName")?.GetValue(section) as string ?? section.ToString();
                var colour = Property(section, "Color");
                if (name == null || colour == null || !colours.TryGetValue(name, out var c)) continue;
                colour.SetValue(section, (Color?)c);
                coloured++;
            }
        }
        return coloured;
    }

    /// <summary>The colour of the first mesh Model View hands out, for the log: whether the colours took.</summary>
    static string FirstColour(IGH_Component view)
    {
        var mesh = Values<GH_Mesh>(Out(view, "Geometry")).Select(m => m.Value).FirstOrDefault(m => m != null && m.VertexColors.Count > 0);
        var c = mesh?.VertexColors[0];
        return c.HasValue ? $"{c.Value.R},{c.Value.G},{c.Value.B}" : "none";
    }

    /// <summary>Whether every corner of <paramref name="box"/> is inside the shares of a capture's width and height given.</summary>
    static bool Inside(RhinoViewport vp, BoundingBox box, double left, double right, double top, double bottom)
    {
        var field = Captured(vp);
        var toScreen = vp.GetTransform(Rhino.DocObjects.CoordinateSystem.World, Rhino.DocObjects.CoordinateSystem.Screen);
        return box.GetCorners().All(corner =>
        {
            var p = corner;
            p.Transform(toScreen);
            return p.X >= field.Left + left * field.Width && p.X <= field.Left + right * field.Width &&
                   p.Y >= field.Top + top * field.Height && p.Y <= field.Top + bottom * field.Height;
        });
    }

    /// <summary>
    /// A structure sized again by its Section Optimiser at every stop of the slider <paramref name="slider"/>,
    /// out along <paramref name="stops"/> and back: Model View <paramref name="view"/>, coloured by section,
    /// captured at each, and a caption under it - where the thing that moves is, from
    /// <paramref name="place"/>, and the steel's mass - for publish.sh's animcharts.
    ///
    /// Every stop is solved twice. First to learn which sections come up anywhere: each gets a colour by
    /// its size, lightest light blue, largest orange, kept in every frame, and a Legend lists them all.
    /// Then again, coloured, for the frames: <paramref name="hold"/> frames at each stop, the camera
    /// turning <paramref name="turn"/> degrees about the vertical over the whole loop. Without a turn, a
    /// stop is captured once and its frames copied.
    /// </summary>
    static void Sizing(IGH_Component optimiser, IGH_Component view, string slider, List<double> stops, string prefix,
                       string title, Func<string> place, Vector3d eye, double turn, int hold, bool byMass = false)
    {
        // A section's size: the number in its name, IPE240's 240 - or, for sections whose name holds no
        // one size, an RHS 300x200x10, its mass in kg a metre, from its area and steel's 7850 kg/m3.
        var perMetre = new Dictionary<string, double>();
        double Size(string n) => byMass ? perMetre[n] : SizeOf(n);
        double start = (double)Find<GH_NumberSlider>(slider).CurrentValue;
        var watch = Stopwatch.StartNew();
        ShowOnly(view);

        // 1 - Which sections, and the box of everything drawn at any stop.
        var used = new HashSet<string>();
        var box = BoundingBox.Empty;
        var said = new List<string>();
        foreach (var stop in stops)
        {
            var clock = Stopwatch.StartNew();
            Slider(slider, stop);
            doc.NewSolution(false);
            Messages(optimiser);
            var names = SectionNames(optimiser);
            used.UnionWith(names);
            foreach (var section in Values<IGH_Goo>(Out(optimiser, "Section")).Select(g => (g as GH_ObjectWrapper)?.Value).Where(v => v != null))
                perMetre[section.ToString()] = Convert.ToDouble(Property(section, "Area").GetValue(section)) * 7850;
            box.Union(Drawn());
            double mass = Values<GH_Number>(Out(optimiser, "Mass")).Last().Value;
            bool converged = Values<GH_Boolean>(Out(optimiser, "Converged")).Select(b => b.Value).DefaultIfEmpty(false).First();
            double utilisation = Values<GH_Number>(Out(optimiser, "Utilisation")).Select(n => n.Value).DefaultIfEmpty(0).Max();
            var counts = names.GroupBy(n => n).OrderBy(g => Size(g.Key)).Select(g => $"{g.Key} x{g.Count()}");
            said.Add(Said(place(), mass));
            Log($"  {stop:0.###}: {said.Last()}; {string.Join(", ", counts)}; largest utilisation {utilisation:F3}" +
                (converged ? "" : ", NOT converged") + $"; {clock.Elapsed.TotalSeconds:F1} s");
            if (utilisation > 1) Log("    over 1: " + Worst(optimiser, 4));
        }
        var sizes = used.OrderBy(Size).ToList();
        var (low, high, count) = SizeScale((int)Math.Floor(Size(sizes.First())), (int)Math.Ceiling(Size(sizes.Last())));
        var colours = sizes.ToDictionary(n => n, n => SizeColour((Size(n) - low) / (high - low)));
        Log($"  {sizes.Count} sections: {string.Join(", ", sizes)}; coloured over {low} to {high}, {count} swatches");

        // The Legend, under the model: the scale, its swatches labelled evenly from low to high.
        var legend = Place((IGH_Component)Instances.ComponentServer.EmitObject(LegendId), Beside(view, 120, 0));
        var t = (GH_PersistentParam<GH_String>)In(legend, "Title");
        t.PersistentData.Clear();
        t.PersistentData.Append(new GH_String(title));
        var values = (GH_PersistentParam<GH_Number>)In(legend, "Values");
        var swatches = (GH_PersistentParam<GH_Colour>)In(legend, "Colors");
        for (int i = 0; i < count; i++)
        {
            values.PersistentData.Append(new GH_Number(low + (high - low) * i / (count - 1)));
            swatches.PersistentData.Append(new GH_Colour(SizeColour((double)i / (count - 1))));
        }
        var position = (GH_PersistentParam<GH_Integer>)In(legend, "Position");
        position.PersistentData.Clear();
        position.PersistentData.Append(new GH_Integer(2));
        var size = (GH_PersistentParam<GH_Number>)In(legend, "Scale");
        size.PersistentData.Clear();
        size.PersistentData.Append(new GH_Number(2.5));
        legend.ExpireSolution(false);
        ShowOnly(view, legend);
        doc.NewSolution(false);

        // The camera: as close as it can be with what is drawn, at every angle of the turn, clear of the
        // picture's sides and of the legend under it.
        var camera = Aim(box, eye, Vector3d.ZAxis);
        var vp = camera.ActiveViewport;
        var target = box.Center;
        var along = eye;
        along.Unitize();
        int slots = 2 * stops.Count - 2, frames = slots * hold;
        double distance = box.Diagonal.Length * 3;
        void Turn(double share)
        {
            var turned = along;
            turned.Rotate(turn * Math.PI / 180 * share, Vector3d.ZAxis);
            vp.SetCameraLocations(target, target + turned * distance);
            vp.CameraUp = Vector3d.ZAxis;
        }
        bool Fits(double d)
        {
            distance = d;
            for (int a = 0; a <= (turn == 0 ? 0 : 12); a++)
            {
                Turn(a / 12.0);
                if (!Inside(vp, box, 0.03, 0.97, 0.05, 0.8)) return false;
            }
            return true;
        }
        double near = box.Diagonal.Length * 0.6, far = box.Diagonal.Length * 10;
        for (int i = 0; i < 40; i++)
        {
            double mid = Math.Sqrt(near * far);
            if (Fits(mid)) far = mid; else near = mid;
        }
        distance = far;
        // Then as low in the picture as it goes, just over the legend, the camera and what it looks at
        // raised together: the same at every angle of a turn about the vertical.
        var middle = target;
        double raised = 0, tooHigh = box.Diagonal.Length;
        for (int i = 0; i < 30; i++)
        {
            double up = (raised + tooHigh) / 2;
            target = middle + Vector3d.ZAxis * up;
            if (Fits(distance)) raised = up; else tooHigh = up;
        }
        target = middle + Vector3d.ZAxis * raised;
        Turn(0);
        Log($"  camera {distance:0.0} m from the middle, raised {raised:0.00} m");

        // 2 - The frames: out along the stops and back, each stop solved once for every slot it fills.
        string folder = prefix + "-frames", captions = Path.Combine(outDir, prefix + "-captions");
        Directory.CreateDirectory(Path.Combine(outDir, folder));
        Directory.CreateDirectory(captions);
        var order = Enumerable.Range(0, stops.Count).Concat(Enumerable.Range(1, stops.Count - 2).Reverse()).ToList();
        for (int s = 0; s < stops.Count; s++)
        {
            Slider(slider, stops[s]);
            doc.NewSolution(false);
            int coloured = ColourSections(optimiser, colours);
            view.ExpireSolution(false);
            doc.NewSolution(false);
            if (s == 0)
            {
                var many = optimiser.Params.Input.Where(p => p.VolatileDataCount > 1).Select(p => $"{p.NickName} {p.VolatileDataCount}");
                Log($"  {Out(optimiser, "AlpacaModel").VolatileDataCount} model(s) out of the optimiser, {coloured} sections coloured; " +
                    $"inputs with more than one item: {string.Join(", ", many)}; first mesh drawn in {FirstColour(view)}, " +
                    $"first section {SectionNames(optimiser).FirstOrDefault()}");
            }
            string first = null;
            for (int slot = 0; slot < slots; slot++)
            {
                if (order[slot] != s) continue;
                for (int h = 0; h < hold; h++)
                {
                    int k = slot * hold + h;
                    string name = k.ToString("D3");
                    if (turn != 0 || first == null)
                    {
                        Turn((double)k / frames);
                        Shoot(camera, Path.Combine(folder, name));
                        Label(Path.Combine(captions, name + ".png"), said[s], 1);
                        first = first ?? name;
                    }
                    else
                    {
                        File.Copy(Path.Combine(outDir, folder, first + ".png"), Path.Combine(outDir, folder, name + ".png"), true);
                        File.Copy(Path.Combine(captions, first + ".png"), Path.Combine(captions, name + ".png"), true);
                    }
                }
            }
        }

        doc.RemoveObject(legend, false);
        Slider(slider, start);
        doc.NewSolution(false);
        Log($"  {frames} frames, {slider} through {stops.Count} stops and back" + (turn != 0 ? $", turning {turn} degrees" : "") +
            $", in {watch.Elapsed.TotalSeconds:F0} s");
    }

    /// <summary>The beams the optimiser could not bring under 1, worst first: their utilisation, check and section, for the log.</summary>
    static string Worst(IGH_Component optimiser, int howMany)
    {
        var u = Out(optimiser, "Utilisation").VolatileData.AllData(false).Select(g => (g as GH_Number)?.Value ?? double.NaN).ToList();
        var why = Out(optimiser, "Governing").VolatileData.AllData(false).Select(g => (g as GH_String)?.Value ?? "").ToList();
        var names = Out(optimiser, "Section").VolatileData.AllData(false).Select(g => (g as GH_ObjectWrapper)?.Value?.ToString() ?? "").ToList();
        var worst = Enumerable.Range(0, u.Count).Where(i => u[i] > 1).OrderByDescending(i => u[i]).Take(howMany)
            .Select(i => $"beam {i} {(i < names.Count ? names[i] : "")} {u[i]:0.00} {(i < why.Count ? why[i] : "")}");
        return string.Join("; ", worst);
    }

    /// <summary>A frame's caption: where the thing that moves is, and the mass of the steel the optimiser chose.</summary>
    static string Said(string place, double kg) =>
        place + "   ·   " + (kg >= 1000 ? $"{kg / 1000:0.0} t" : $"{kg:0} kg") + " of steel";

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
    /// <summary>The earthquakes' element edges: dark enough to draw each member, light enough not to blacken a tower.</summary>
    static readonly Color EdgeGrey = Color.FromArgb(104, 110, 118);
    /// <summary>A tower's: see-through, so that each of its thousands of thin members is outlined and still shows its colour.</summary>
    static readonly Color TowerEdges = Color.FromArgb(35, 42, 45, 49);

    static List<(string, Func<bool>)> Structures(string only = null)
    {
        (string file, string slug, string title, string deformed, Vector3d eye, double turn)[] all =
        {
            ("TrussRoofHall.gh", "truss-roof-hall", "Truss roof hall", "Hall deformed", new Vector3d(-1.0, -1.3, 0.8), 20),
            ("SteelTower.gh", "steel-tower", "Steel tower", "Tower deformed", new Vector3d(-1.0, -1.25, 0.4), 30),
            ("RoofCanopy.gh", "roof-canopy", "Roof canopy", "Canopy deformed", new Vector3d(-1.0, -1.4, 0.6), 20),
            ("VerticalTrussFacade.gh", "vertical-truss-facade", "Vertical truss facade", "Facade deformed", new Vector3d(1.0, 0.9, 0.35), 0),
            ("BarrelVault.gh", "barrel-vault", "Barrel vault", "Vault deformed", new Vector3d(-1.0, -1.3, 0.7), 20),
            ("HorizontalTrussFacade.gh", "horizontal-truss-facade", "Horizontal truss facade", "Wind truss deformed", new Vector3d(-0.7, 1.0, 0.9), 0),
            ("CompressedShell.gh", "compressed-shell", "Compressed shell", "Shell deformed", new Vector3d(-1.0, -1.25, 0.6), 20),
        };
        var list = new List<(string, Func<bool>)>();
        foreach (var (file, slug, title, name, eye, turn) in all.Where(a => only == null || a.slug == only))
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
    static List<(string, Func<bool>)> Earthquakes(string set, string only = null)
    {
        // eye: where the camera looks from; magnify: how much closer than the box of what is drawn.
        // A tall one - tall - stands left of the middle of its pictures, by the legend, its charts beside
        // it rather than under it; the step it moves the most is found from its watched points' history,
        // as drawing a tower of twenty thousand members at every fifth step to find it takes too long.
        var sets = new Dictionary<string, (string file, string slug, string title, string view, Vector3d eye, double magnify, int along, string[] watched, bool tall)[]>
        {
            ["earthquakes"] = new[]
            {
                ("GirderBridgeEarthquake.gh", "girder-bridge-earthquake", "Girder bridge", "Girder shaking", new Vector3d(-1.4, -0.9, 0.6), 1.4, 1, new[] { "deck at mid-span" }, false),
                ("CableStayedBridgeEarthquake.gh", "cable-stayed-bridge-earthquake", "Cable-stayed bridge", "Cable-stayed shaking", new Vector3d(-0.9, -1.3, 0.5), 1.25, 1, new[] { "deck at mid-span", "top of the pylon" }, false),
                ("ArchBridgeEarthquake.gh", "arch-bridge-earthquake", "Arch bridge", "Arch shaking", new Vector3d(-1.3, -0.9, 0.5), 1.4, 1, new[] { "deck at the crown" }, false),
                ("BuildingEarthquake.gh", "building-earthquake", "Office building", "Building shaking", new Vector3d(0.9, -1.3, 0.6), 1.3, 0, new[] { "roof" }, false),
                ("EarthquakeCity.gh", "earthquake-city", "Tiny city", "City shaking", new Vector3d(0.8, -1.3, 0.75), 1.5, 0, (string[])null, false),
            },
            ["buildings"] = new[]
            {
                ("TwistingTowerEarthquake.gh", "twisting-tower-earthquake", "Twisting tower", "Twisting shaking", new Vector3d(0.9, -1.3, 0.35), 1.35, 0, new[] { "roof" }, true),
                ("LeaningTowersEarthquake.gh", "leaning-towers-earthquake", "Leaning towers", "Leaning shaking", new Vector3d(0.25, -1.3, 0.35), 1.4, 0, new[] { "west roof", "east roof" }, false),
                ("CurvingTowerEarthquake.gh", "curving-tower-earthquake", "Curving tower", "Curving shaking", new Vector3d(0.3, -1.3, 0.3), 1.35, 0, new[] { "roof" }, true),
                ("CrescentEarthquake.gh", "crescent-earthquake", "Crescent", "Crescent shaking", new Vector3d(0.5, -1.3, 0.7), 1.3, 0, new[] { "tip of the roof" }, false),
            },
            ["towers"] = new[]
            {
                ("BundledTubeEarthquake.gh", "bundled-tube-earthquake", "Bundled tube", "Bundled shaking", new Vector3d(0.8, -1.3, 0.3), 1.2, 0, new[] { "roof", "50th floor" }, true),
                ("BracedTubeEarthquake.gh", "braced-tube-earthquake", "Braced tube", "Braced shaking", new Vector3d(0.8, -1.3, 0.3), 1.2, 0, new[] { "roof" }, true),
                ("ButtressedCoreEarthquake.gh", "buttressed-core-earthquake", "Buttressed core", "Buttressed shaking", new Vector3d(0.6, -1.3, 0.25), 1.2, 0, new[] { "spire", "roof" }, true),
                ("OutriggerTowerEarthquake.gh", "outrigger-tower-earthquake", "Outrigger tower", "Outrigger shaking", new Vector3d(0.8, -1.3, 0.3), 1.2, 0, new[] { "roof", "damper" }, true),
                ("DiagridTowerEarthquake.gh", "diagrid-tower-earthquake", "Diagrid tower", "Diagrid shaking", new Vector3d(0.8, -1.3, 0.3), 1.25, 0, new[] { "roof" }, true),
            },
        };
        var list = new List<(string, Func<bool>)>();
        foreach (var (file, slug, title, name, eye, magnify, along, watched, tall) in sets[set])
        {
            // One of them alone, by the first word of its slug.
            if (only != null && !slug.StartsWith(only + "-")) continue;
            list.Add(Do(slug, () =>
            {
                SoftEdges(set == "towers" ? TowerEdges : EdgeGrey);
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
                int worst = 0;
                double most = -1.0;
                if (slug.StartsWith("leaning"))
                {
                    // Two towers leaning over a street: the moment they lean towards each other the
                    // most, the west one's top moving east as the east one's moves west.
                    for (int k = 0; k < quake.Moves[0].Length; k++)
                    {
                        double closing = quake.Moves[0][k] - quake.Moves[1][k];
                        if (closing > most) { most = closing; worst = k; }
                    }
                    worst = Math.Min(worst, steps);
                    Log($"  the gap between their tops closes the most, {most:G4} mm, at step {worst}, {quake.Time(worst):F2} s");
                }
                else if (tall)
                {
                    // The step the roof moves the most, from its history: not a damper's, which swings
                    // the most at another time, nor the buttressed core's spire's - when it whips the
                    // most, the tower under it has hardly moved and is all the colour of nothing.
                    int lead = watched != null && watched[0] == "spire" ? 1 : 0;
                    for (int k = 0; k < quake.Moves[lead].Length; k++)
                    {
                        double largest = Math.Abs(quake.Moves[lead][k]);
                        if (largest > most) { most = largest; worst = k; }
                    }
                    worst = Math.Min(worst, steps);
                    Log($"  the {(watched != null ? watched[lead] : "first watched point")} moves the most, {most:G4} mm, at step {worst}, " +
                        $"{quake.Time(worst):F2} s; drawn there, {AtStep(step, worst, view):G4} mm at the most");
                }
                else
                {
                    // The step it moves the most, every fifth looked at. Info gives the Range when one is
                    // set, not what is drawn: it is taken off for the search and put back after.
                    var range = (GH_PersistentParam<GH_Interval>)In(view, "Range");
                    var colours = range.PersistentData.AllData(true).OfType<GH_Interval>().FirstOrDefault();
                    range.PersistentData.Clear();
                    range.ExpireSolution(false);
                    for (int k = 0; k <= steps; k += 5)
                    {
                        double largest = AtStep(step, k, view);
                        if (largest > most) { most = largest; worst = k; }
                    }
                    if (colours != null) range.PersistentData.Append(colours);
                    range.ExpireSolution(false);
                    Log($"  {steps} steps; the most, {most:G4} mm, at step {worst}, {quake.Time(worst):F2} s");
                }
                // Framed on this step and on the steps every watched point moves the most each way, so
                // that nothing leaves the picture as it shakes - not the spire whipping 5 m either side,
                // nor the leaning towers' tops as the gap between them opens.
                var extremes = new SortedSet<int>();
                foreach (var m in quake.Moves)
                {
                    int n = Math.Min(m.Length, steps + 1), high = 0, low = 0;
                    for (int k = 1; k < n; k++)
                    {
                        if (m[k] > m[high]) high = k;
                        if (m[k] < m[low]) low = k;
                    }
                    extremes.Add(high);
                    extremes.Add(low);
                }
                extremes.Remove(worst);
                AtStep(step, worst, view);
                var frame = Drawn();
                foreach (int k in extremes)
                {
                    AtStep(step, k, view);
                    frame.Union(Drawn());
                }
                if (extremes.Count > 0) AtStep(step, worst, view);
                // Framed for the viewport as it is. Its size can change half way: after the screen was
                // unlocked on 2026-11-07 the frames came out about a third closer, the top of the tower
                // cut off. So it is framed again whenever the size changes.
                RhinoView Framed()
                {
                    var v = Aim(Grown(frame, 0.06), eye, Vector3d.ZAxis, magnify: magnify);
                    // A tower stands a quarter of the way across, by the legend, rather than in the middle.
                    if (tall) Slide(v, 0.25);
                    FitInside(v, frame, 0.17);
                    return v;
                }
                var eyeView = Framed();
                var framedFor = eyeView.ActiveViewport.Size;
                Shoot(eyeView, slug, WithLegend);
                Charts(Path.Combine(outDir, slug + "-chart.png"), quake, worst, WithLegend, tall);

                // The frames keep the still's legend: Shoot captures them at the still's size too, and
                // shrinks them after.

                Directory.CreateDirectory(Path.Combine(outDir, slug + "-frames"));
                Directory.CreateDirectory(Path.Combine(outDir, slug + "-charts"));
                int frames = 0;
                // Model View has a step for each of the history's entries and draws nothing past the
                // last - only the supports, its legend a lone 0: the Step slider goes one further, to the
                // record's count of steps, and that frame flashed empty at the end of every video.
                int last = Math.Min(steps, quake.Moves[0].Length - 1);
                for (int k = 0; k <= last; k += 5, frames++)
                {
                    AtStep(step, k, view);
                    if (eyeView.ActiveViewport.Size != framedFor)
                    {
                        Log($"  the viewport changed from {framedFor.Width}x{framedFor.Height} to " +
                            $"{eyeView.ActiveViewport.Size.Width}x{eyeView.ActiveViewport.Size.Height} at frame {frames}; framed again");
                        eyeView = Framed();
                        framedFor = eyeView.ActiveViewport.Size;
                    }
                    Shoot(eyeView, Path.Combine(slug + "-frames", frames.ToString("D3")));
                    Charts(Path.Combine(outDir, slug + "-charts", frames.ToString("D3") + ".png"), quake, k, 1, tall);
                }
                Log($"  {frames} frames and their charts, 0 to {quake.Time(last - last % 5):F1} s, a frame every 0.1 s");
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
            // A filter hands its nodes back in the model's order, not in the order they were asked
            // for: each point's history is the one of the node it is nearest to.
            var kept = Values<GH_Point>(Out(nodes, "Position")).Select(p => p.Value).ToList();
            var item = points.Select(p => Enumerable.Range(0, kept.Count).OrderBy(i => kept[i].DistanceToSquared(p)).First()).ToList();
            var moves = points.Select(_ => new List<double>()).ToList();
            foreach (var path in tree.Paths)
            {
                var branch = tree.get_Branch(path);
                for (int i = 0; i < moves.Count; i++)
                    if (item[i] < branch.Count) moves[i].Add(((GH_Vector)branch[item[i]]).Value[axis] * 1000.0);
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
    static void Charts(string path, Quake q, int k, double scale, bool tall = false)
    {
        float s = (float)scale;
        // A tower's charts stand beside it: narrower, and as tall as its picture.
        int w = (int)((tall ? 1400 : 2400) * s), h = (int)((tall ? 1500 : 880) * s);
        int at = Math.Max(0, Math.Min(k, q.Moves[0].Length - 1));
        using (var bitmap = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Background);
            var ground = tall ? new RectangleF(200 * s, 160 * s, w - 260 * s, 360 * s) : new RectangleF(220 * s, 90 * s, w - 300 * s, 220 * s);
            TimeChart(g, ground, q.Times, new List<double[]> { q.Ground }, q.Time(at), "Ground acceleration [g]",
                      new[] { DarkTech }, null, "g", "0.0#", s, q.Storeys != null);
            var below = tall ? new RectangleF(200 * s, 760 * s, w - 260 * s, 620 * s) : new RectangleF(220 * s, 470 * s, w - 300 * s, 320 * s);
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
            // A second label every 2 s, or every 5 s where they would crowd.
            int every = r.Width / end < 70 * s ? 5 : 2;
            if (seconds)
                for (int second = 0; second <= (int)end; second += every)
                {
                    var label = second + " s";
                    var size = g.MeasureString(label, small);
                    g.DrawString(label, small, ink, X(second) - size.Width / 2, r.Bottom + 12 * s);
                }
            // The title, and right of it each series' name and value now; where they would run into
            // each other, as on a tower's narrow charts, the title goes up a line.
            float named = 0;
            for (int i = 0; i < series.Count; i++)
            {
                var value = series[i][Math.Max(0, Math.Min(series[i].Length - 1, now))].ToString(format, Inv) + " " + unit;
                named += g.MeasureString(labels == null ? value + "      t = 00.0 s" : labels[i] + "  " + value, big).Width + 60 * s;
            }
            float titleY = g.MeasureString(title, big).Width + named > r.Width + 60 * s ? r.Top - 130 * s : r.Top - 78 * s;
            g.DrawString(title, big, ink, r.Left - 8 * s, titleY);
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

    // ------------------------------------------------------------------ a building pushed over

    /// <summary>
    /// The irregular concrete building of Tools/Examples/Structures.py pushed over, PushoverBuilding.gh:
    /// its canvas; the building at the end of the push with its plastic hinges, from a corner, and the
    /// chart of its capacity curve on its own; the building beside Grasshopper's own drawing of the curve,
    /// as the example opens; and a frame every second point of the curve, the building pushed a little
    /// further each time and its hinges forming, with the chart of the curve so far in a folder of its own,
    /// for publish.sh's animcharts.
    /// </summary>
    static List<(string, Func<bool>)> PushoverScene() => new List<(string, Func<bool>)>
    {
        Do("frame", () =>
        {
            SoftEdges(EdgeGrey);
            Open("PushoverBuilding.gh");
            foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                Messages(c);
            var push = Find<IGH_Component>("Pushover");
            foreach (var m in push.RuntimeMessages(GH_RuntimeMessageLevel.Remark)) Log("  Pushover: " + m);
            var view = Find<IGH_Component>("Frame pushed");
            var rest = Find<IGH_Component>("Irregular building at rest");
            var curve = Find<IGH_Param>("Capacity curve drawn");
            var labels = Find<IGH_Component>("Curve labels");
            var point = Find<GH_NumberSlider>("Point on the curve");
            var d = Values<GH_Number>(Out(push, "Displacement")).Select(n => n.Value * 1000.0).ToArray();
            var v = Values<GH_Number>(Out(push, "Base Shear")).Select(n => n.Value).ToArray();
            var hinges = Values<GH_Integer>(Out(push, "Yields At")).Select(n => n.Value).ToArray();
            if (d.Length < 2) throw new Exception("the push gave no curve");
            Log($"  {hinges.Length} hinges, the first at point {(hinges.Length > 0 ? hinges.Min() : -1)}");
            foreach (var line in Values<GH_String>(Out(push, "Hinges")).Take(12)) Log("    " + line.Value);
            SwayByFloor(push);
            int last = d.Length - 1, peak = Array.IndexOf(v, v.Max());
            Log($"  {d.Length} points to {d[last]:F0} mm; the peak {v[peak]:F0} kN at {d[peak]:F0} mm; {v[last]:F0} kN at the end");
            Canvas("pushover-building-canvas");

            // Framed at the end of the push, where it sways the most, with the frame at rest under it.
            ShowOnly(view, rest);
            double most = AtStep(point, last, view);
            Log($"  drawn at the last point: {most:G4} mm at the most");
            var frame = Drawn();
            var framed = Aim(Grown(frame, 0.06), new Vector3d(0.55, -1.3, 0.65), Vector3d.ZAxis, magnify: 1.2);
            FitInside(framed, frame, 0.17);
            Shoot(framed, "pushover-building", WithLegend);
            var ductility = Ductilities(push);
            var failedAt = FailedAt(push);
            PushoverChart(Path.Combine(outDir, "pushover-building-chart.png"), d, v, last, WithLegend, hinges, ductility: ductility, failedAt: failedAt);

            Directory.CreateDirectory(Path.Combine(outDir, "pushover-building-frames"));
            Directory.CreateDirectory(Path.Combine(outDir, "pushover-building-charts"));
            int frames = 0;
            var points_ = Enumerable.Range(0, last + 1).Where(k => k % 3 == 0 || k == last).ToList();
            foreach (int k in points_)
            {
                AtStep(point, k, view);
                Shoot(framed, Path.Combine("pushover-building-frames", frames.ToString("D3")));
                PushoverChart(Path.Combine(outDir, "pushover-building-charts", frames.ToString("D3") + ".png"), d, v, k, 1, hinges, ductility: ductility, failedAt: failedAt);
                frames++;
            }
            Log($"  {frames} frames and their charts, every third point of the curve");

            // As the example opens: the curve Grasshopper draws beside the frame, at the last point. Last,
            // as it aims the one viewport the frames are shot in somewhere else.
            ShowOnly(view, rest, curve, labels);
            AtStep(point, last, view);
            var both = Drawn();
            var side = Aim(Grown(both, 0.05), new Vector3d(0.15, -1, 0.15), Vector3d.ZAxis, magnify: 1.15);
            FitInside(side, both, 0.17);
            Shoot(side, "pushover-building-and-curve", WithLegend);
            Close();
        }),
    };

    /// <summary>
    /// The colours Model View draws a plastic hinge in by the ductility it has reached, from 1, 2, 4 and 8 times
    /// its yield strain (Alpaca4d.Result.PlasticHinges.BandColours).
    /// </summary>
    static readonly Color[] HingeBands =
    {
        Color.FromArgb(242, 201, 76), Color.FromArgb(235, 108, 63), Color.FromArgb(214, 40, 40), Color.FromArgb(122, 14, 28),
    };

    static int HingeBand(double ductility) => ductility >= 8 ? 3 : ductility >= 4 ? 2 : ductility >= 2 ? 1 : 0;

    /// <summary>
    /// The point of the curve each of Pushover Analysis' Hinges failed at - its concrete crushed, a bar at its ultimate
    /// strain - read from its line ("...; C25/30 crushed at point 124: ..."), -1 for none.
    /// </summary>
    static int[] FailedAt(IGH_Component push) =>
        Values<GH_String>(Out(push, "Hinges")).Select(h =>
        {
            var failed = h.Value.Split(';').Skip(1).FirstOrDefault(part => part.Contains(" at point ") && !part.Contains("ductility"));
            var m = failed == null ? null : System.Text.RegularExpressions.Regex.Match(failed, @" at point (\d+)");
            return m != null && m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }).ToArray();

    /// <summary>Pushover Analysis' Ductility: a branch a hinge, a value a point of the curve.</summary>
    static List<double[]> Ductilities(IGH_Component push)
    {
        var tree = Out(push, "Ductility").VolatileData;
        return tree.Paths.Select(path => tree.get_Branch(path).OfType<GH_Number>().Select(n => n.Value).ToArray()).ToList();
    }

    /// <summary>
    /// The reciprocal plywood vault of Tools/Examples/ReciprocalVault.py pressed down until it gives way,
    /// ReciprocalVault.gh: its canvas; the vault at the peak of its curve, and at the end of the push, with
    /// their charts; a frame every second point of the curve, the vault pressed a little further each time,
    /// its colours over the whole push's range, with the chart of the curve so far in a folder of its own,
    /// for publish.sh's animcharts; and the vault beside Grasshopper's own drawing of the curve.
    /// </summary>
    static List<(string, Func<bool>)> VaultScene() => new List<(string, Func<bool>)>
    {
        Do("vault", () =>
        {
            Open("ReciprocalVault.gh");
            foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                Messages(c);
            var push = Find<IGH_Component>("Pushover");
            var view = Find<IGH_Component>("Vault pushed");
            var legend = Find<IGH_Component>("Vault legend");
            var curve = Find<IGH_Param>("Capacity curve drawn");
            var labels = Find<IGH_Component>("Curve labels");
            var point = Find<GH_NumberSlider>("Point on the curve");
            var d = Values<GH_Number>(Out(push, "Displacement")).Select(n => Math.Abs(n.Value) * 1000.0).ToArray();
            var f = Values<GH_Number>(Out(push, "Load Factor")).Select(n => Math.Abs(n.Value)).ToArray();
            var hinges = Values<GH_Integer>(Out(push, "Yields At")).Select(n => n.Value).ToArray();
            if (d.Length < 2) throw new Exception("the push gave no curve");
            int last = d.Length - 1, peak = Array.IndexOf(f, f.Max());
            Log($"  {d.Length} points to {d[last]:F0} mm; the peak {f[peak]:F2} times its weight at {d[peak]:F0} mm; {f[last]:F2} at the end");
            Log($"  {hinges.Length} hinges, the first at point {(hinges.Length > 0 ? hinges.Min() : -1)}");
            foreach (var line in Values<GH_String>(Out(push, "Hinges")).Take(8)) Log("    " + line.Value);
            Canvas("reciprocal-vault-canvas");

            // The planks grey, the hinges coloured by the ductility they have reached: the rings on the curve too.
            var ductility = Ductilities(push);
            Log($"  ductility by the end: {ductility.Count(x => x.Length > 0 && x.Last() >= 8)} hinges past 8, " +
                $"{ductility.Count(x => x.Length > 0 && x.Last() >= 4 && x.Last() < 8)} from 4, {ductility.Count(x => x.Length > 0 && x.Last() >= 2 && x.Last() < 4)} from 2, " +
                $"the most {ductility.Select(x => x.Length > 0 ? x.Last() : 0).DefaultIfEmpty(0).Max():0.#}");
            Func<int, int, string> now = (k, formed) =>
                $"Crown  {d[k]:0} mm      {f[k]:0.00} × its weight      Hinges  {formed}";
            const string up = "Pressed down [× its own weight]", along = "Crown moves down [mm]";

            ShowOnly(view, legend);
            // How many balls Model View draws at a few points, how many of them have unloaded - faded - and how
            // many would be left were only the points past yield at that step drawn, as before 2026-11-09.
            foreach (int k in new[] { peak, (peak + last) / 2, last })
            {
                AtStep(point, k, view);
                var stations = ((System.Collections.IEnumerable)Field(view, "_hinges") ?? new object[0]).Cast<object>().ToList();
                Func<object, string, object> Get = (o, n) => o.GetType().GetProperty(n).GetValue(o);
                int balls = stations.Count(o => Get(o, "Reached").ToString() != "Elastic");
                int faded = stations.Count(o => (bool)Get(o, "Unloaded"));
                int pastNow = stations.Count(o => Get(o, "State").ToString() != "Elastic");
                Log($"  point {k}: {balls} balls, {faded} of them faded; past yield at that step alone: {pastNow}");
            }
            AtStep(point, last, view);
            var vault = Drawn();
            var framed = Aim(Grown(vault, 0.06), new Vector3d(0.9, -1.3, 0.55), Vector3d.ZAxis, magnify: 1.15);
            FitInside(framed, vault, 0.17);
            Shoot(framed, "reciprocal-vault", WithLegend);
            PushoverChart(Path.Combine(outDir, "reciprocal-vault-chart.png"), d, f, last, WithLegend, hinges, up, along, now, ductility);
            AtStep(point, peak, view);
            Shoot(framed, "reciprocal-vault-peak", WithLegend);
            PushoverChart(Path.Combine(outDir, "reciprocal-vault-peak-chart.png"), d, f, peak, WithLegend, hinges, up, along, now, ductility);

            Directory.CreateDirectory(Path.Combine(outDir, "reciprocal-vault-frames"));
            Directory.CreateDirectory(Path.Combine(outDir, "reciprocal-vault-charts"));
            int frames = 0;
            foreach (int k in Enumerable.Range(0, last + 1).Where(k => k % 2 == 0 || k == last))
            {
                AtStep(point, k, view);
                Shoot(framed, Path.Combine("reciprocal-vault-frames", frames.ToString("D3")));
                PushoverChart(Path.Combine(outDir, "reciprocal-vault-charts", frames.ToString("D3") + ".png"), d, f, k, 1, hinges, up, along, now, ductility);
                frames++;
            }
            Log($"  {frames} frames and their charts, every second point of the curve");

            // As the example opens: the curve Grasshopper draws beside the vault, at the last point.
            ShowOnly(view, legend, curve, labels);
            AtStep(point, last, view);
            var both = Drawn();
            var side = Aim(Grown(both, 0.05), new Vector3d(0.15, -1, 0.2), Vector3d.ZAxis, magnify: 1.1);
            FitInside(side, both, 0.17);
            Shoot(side, "reciprocal-vault-and-curve", WithLegend);

            // Pushed sideways instead, Direction Y, to 1 000 mm: the curve into the log, and the vault at the end.
            var direction = Find<GH_Panel>("Direction");
            direction.SetUserText("Y");
            direction.ExpireSolution(false);
            Find<GH_NumberSlider>("Push to [mm]").SetSliderValue(1000m);
            doc.NewSolution(false);
            var dy = Values<GH_Number>(Out(push, "Displacement")).Select(n => Math.Abs(n.Value) * 1000.0).ToArray();
            var fy = Values<GH_Number>(Out(push, "Load Factor")).Select(n => Math.Abs(n.Value)).ToArray();
            var hy = Values<GH_Integer>(Out(push, "Yields At")).Select(n => n.Value).ToArray();
            foreach (var m in push.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Concat(push.RuntimeMessages(GH_RuntimeMessageLevel.Warning)))
                Log("  sideways: " + m);
            for (int k = 0; k < dy.Length; k += Math.Max(1, dy.Length / 10))
                Log($"  sideways: {dy[k]:0} mm, {fy[k]:0.00} g");
            if (dy.Length > 1)
            {
                int end = dy.Length - 1;
                Log($"  sideways: {dy.Length} points to {dy[end]:0} mm at {fy[end]:0.00} g; {hy.Length} hinges, the first at point {(hy.Length > 0 ? hy.Min() : -1)}");
                ShowOnly(view, legend);
                AtStep(point, end, view);
                var leaning = Drawn();
                var leaned = Aim(Grown(leaning, 0.06), new Vector3d(1, -0.25, 0.35), Vector3d.ZAxis, magnify: 1.15);
                FitInside(leaned, leaning, 0.17);
                Shoot(leaned, "reciprocal-vault-sideways", WithLegend);
                PushoverChart(Path.Combine(outDir, "reciprocal-vault-sideways-chart.png"), dy, fy, end, WithLegend, hy,
                              "Pushed sideways [g]", "Crown moves sideways [mm]",
                              (k, formed) => $"Crown  {dy[k]:0} mm      {fy[k]:0.00} g      Hinges  {formed}", Ductilities(push));
            }
            Close();
        }),
    };

    /// <summary>
    /// How far each floor of a pushed model moved along X at the end of the push, its least and its most,
    /// into the log: read by a Nodal Displacements of the scene's own, taken out again after.
    /// </summary>
    static void SwayByFloor(IGH_Component push)
    {
        var steps = Values<GH_Integer>(Out(push, "Step")).Select(n => n.Value).ToList();
        if (steps.Count == 0) return;
        var nodes = (IGH_Component)Instances.ComponentServer.EmitObject(new Guid("25085EAB-6C66-487E-B83B-BAB5AB02B1BB"));
        nodes.CreateAttributes();
        doc.AddObject(nodes, false);
        try
        {
            nodes.Params.Input[0].AddSource(push.Params.Output[0]);
            var step = (GH_PersistentParam<GH_Integer>)In(nodes, "Step");
            step.PersistentData.Clear();
            step.PersistentData.Append(new GH_Integer(steps.Last()));
            doc.NewSolution(false);
            var at = Values<GH_Point>(Out(nodes, "Position")).Select(p => p.Value).ToList();
            var moved = Values<GH_Vector>(Out(nodes, "Displacement")).Select(v => v.Value.X * 1000.0).ToList();
            foreach (var floor in Enumerable.Range(0, Math.Min(at.Count, moved.Count)).GroupBy(i => Math.Round(at[i].Z, 3)).OrderBy(g => g.Key))
                Log($"    floor at {floor.Key:0.0} m: {floor.Min(i => moved[i]):0} to {floor.Max(i => moved[i]):0} mm along X");
        }
        finally { doc.RemoveObject(nodes, false); }
    }

    /// <summary>
    /// The capacity curve at point k, 2400 px wide times scale: base shear up, the roof's displacement
    /// along, the whole curve in light grey, the curve so far in light blue, the point now in orange, and
    /// the two values now, top right, with how many plastic hinges have formed - a small orange ring on
    /// the curve where each did, from the Pushover's Yields At.
    /// </summary>
    static void PushoverChart(string path, double[] d, double[] v, int k, double scale, int[] hinges,
                              string upTitle = "Base shear [kN]", string alongTitle = "Roof displacement [mm]",
                              Func<int, int, string> nowText = null, List<double[]> ductility = null, int[] failedAt = null)
    {
        float s = (float)scale;
        int w = (int)(2400 * s), h = (int)(820 * s);
        var r = new RectangleF(260 * s, 130 * s, w - 380 * s, 520 * s);
        double right = Nice(d.Max() * 1.02), top = Nice(v.Max() * 1.03);
        Func<double, float> X = x => r.Left + (float)(x / right) * r.Width;
        Func<double, float> Y = y => r.Bottom - (float)(y / top) * r.Height;
        k = Math.Max(0, Math.Min(k, d.Length - 1));
        using (var bitmap = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bitmap))
        using (var big = new Font(ChartFont, 42 * s, GraphicsUnit.Pixel))
        using (var small = new Font(ChartFont, 32 * s, GraphicsUnit.Pixel))
        using (var axis = new Pen(ChartAxis, 2 * s))
        using (var ink = new SolidBrush(DarkTech))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Background);
            // A short axis - a load factor of 2.5 - in round ticks with decimals: 0.5 apart, not 0.625 rounded to 1.
            int up = top < 10 ? Divisions(top) : 4;
            for (int i = 0; i <= up; i++)
            {
                double y = top * i / up;
                g.DrawLine(axis, r.Left, Y(y), r.Right, Y(y));
                var label = y.ToString(top < 10 ? "0.##" : "0", Inv);
                var size = g.MeasureString(label, small);
                g.DrawString(label, small, ink, r.Left - size.Width - 14 * s, Y(y) - size.Height / 2);
            }
            for (int i = 0; i <= 4; i++)
            {
                double x = right * i / 4;
                g.DrawLine(axis, X(x), r.Bottom, X(x), r.Bottom + 10 * s);
                var label = x.ToString("0", Inv);
                var size = g.MeasureString(label, small);
                g.DrawString(label, small, ink, X(x) - size.Width / 2, r.Bottom + 16 * s);
            }
            g.DrawString(upTitle, big, ink, r.Left - 8 * s, r.Top - 100 * s);
            var along = alongTitle;
            var alongSize = g.MeasureString(along, small);
            g.DrawString(along, small, ink, r.Left + (r.Width - alongSize.Width) / 2, r.Bottom + 66 * s);

            var all = d.Select((x, i) => new PointF(X(x), Y(v[i]))).ToArray();
            using (var faint = new Pen(ChartFaint, 4 * s) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                g.DrawLines(faint, all);
            if (k > 0)
                using (var pen = new Pen(LightBlue, 7 * s) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                    g.DrawLines(pen, all.Take(k + 1).ToArray());
            // Where hinges formed, so far: one ring a point of the curve, however many formed at it - in the
            // colour of the furthest any of them has gone by now, as Model View colours the hinges, given their
            // Ductility; orange without.
            foreach (var atPoint in Enumerable.Range(0, hinges.Length).Where(i => hinges[i] <= k && hinges[i] < all.Length).GroupBy(i => hinges[i]))
            {
                var colour = Orange;
                if (ductility != null)
                {
                    double most = atPoint.Max(i => i < ductility.Count && k < ductility[i].Length ? ductility[i][k] : 1.0);
                    colour = HingeBands[HingeBand(most)];
                }
                // Purple once one of them has failed by now - concrete crushed, a bar at its ultimate strain - as its ball is.
                if (failedAt != null && atPoint.Any(i => i < failedAt.Length && failedAt[i] >= 0 && failedAt[i] <= k))
                    colour = Purple;
                int at = atPoint.Key;
                using (var ring = new Pen(colour, 4 * s))
                    g.DrawEllipse(ring, all[at].X - 9 * s, all[at].Y - 9 * s, 18 * s, 18 * s);
            }
            using (var dot = new SolidBrush(Orange))
                g.FillEllipse(dot, all[k].X - 13 * s, all[k].Y - 13 * s, 26 * s, 26 * s);

            // Rounded first, and a zero written as one: -0.0001 kN would read "-0".
            double roof = Math.Round(d[k]), shear = Math.Round(v[k]);
            if (roof == 0) roof = 0;
            if (shear == 0) shear = 0;
            int formed = hinges.Count(h => h <= k);
            var now = nowText != null ? nowText(k, formed) : $"Roof  {roof:0} mm      Base shear  {shear:0} kN      Hinges  {formed}";
            var nowSize = g.MeasureString(now, big);
            g.DrawString(now, big, ink, r.Right - nowSize.Width, r.Top - 100 * s);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    // ------------------------------------------------------------------ a steel beam pushed past yield

    /// <summary>
    /// The steel beam with holes of Tools/Examples/PerforatedBeam.py, J2Plasticity in ten node
    /// tetrahedra, pushed past first yield: its canvas; the beam at the last step in its Von Mises
    /// bands, the yielding steel orange, and the chart of its load-deflection curve; the beam beside the
    /// curve Grasshopper draws, as the example opens; and a frame for every step, the beam pushed a
    /// little further each time, with the chart of the curve so far in a folder of its own.
    /// </summary>
    static List<(string, Func<bool>)> PerforatedBeamScene() => new List<(string, Func<bool>)>
    {
        Do("beam", () =>
        {
            SoftEdges(SlaveEdges);
            Open("PerforatedBeam.gh");
            foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                Messages(c);
            var view = Find<IGH_Component>("Beam pushed");
            var legend = Find<IGH_Component>("Beam pushed legend");
            var step = Find<GH_NumberSlider>("Step");
            var curve = Find<IGH_Component>("Load-deflection");
            var elastic = Find<IGH_Component>("Elastic");
            var yielding = Find<IGH_Component>("Yielding");
            var axes = Find<IGH_Component>("Axes");
            var d = Values<GH_Number>(Out(curve, "Deflection")).Select(n => n.Value).ToArray();
            var p = Values<GH_Number>(Out(curve, "Load")).Select(n => n.Value).ToArray();
            double yd = Values<GH_Number>(Out(curve, "YieldDeflection")).Select(n => n.Value).DefaultIfEmpty(double.NaN).First();
            double yl = Values<GH_Number>(Out(curve, "YieldLoad")).Select(n => n.Value).DefaultIfEmpty(double.NaN).First();
            if (d.Length < 2) throw new Exception("the beam gave no curve");
            int steps = d.Length - 1;
            Log($"  {steps} steps to {p[steps]:F0} kN and {d[steps]:F1} mm; first yield at {yl:F0} kN and {yd:F1} mm");
            Canvas("perforated-beam-canvas");

            // The beam at the last step, from in front and a little above, its holes square on.
            ShowOnly(view, legend);
            AtStep(step, steps - 1, view);
            var beam = Drawn();
            var framed = Aim(Grown(beam, 0.04), new Vector3d(0.25, -1, 0.3), Vector3d.ZAxis, magnify: 1.15);
            FitInside(framed, beam, 0.17);
            Shoot(framed, "perforated-beam", WithLegend);
            LoadDeflectionChart(Path.Combine(outDir, "perforated-beam-chart.png"), d, p, yd, yl, steps, WithLegend);

            Directory.CreateDirectory(Path.Combine(outDir, "perforated-beam-frames"));
            Directory.CreateDirectory(Path.Combine(outDir, "perforated-beam-charts"));
            for (int k = 0; k < steps; k++)
            {
                AtStep(step, k, view);
                Shoot(framed, Path.Combine("perforated-beam-frames", k.ToString("D3")));
                LoadDeflectionChart(Path.Combine(outDir, "perforated-beam-charts", k.ToString("D3") + ".png"), d, p, yd, yl, k + 1, 1);
            }
            Log($"  {steps} frames and their charts, a step each");

            // As the example opens: the curve Grasshopper draws under the beam, at the last step. Last,
            // as it aims the one viewport the frames are shot in somewhere else.
            ShowOnly(view, legend, elastic, yielding, axes);
            AtStep(step, steps - 1, view);
            var both = Drawn();
            var side = Aim(Grown(both, 0.05), new Vector3d(0.1, -1, 0.1), Vector3d.ZAxis, magnify: 1.1);
            FitInside(side, both, 0.17);
            Shoot(side, "perforated-beam-and-curve", WithLegend);
            Close();
        }),
    };

    /// <summary>
    /// One of Tools/Examples/Plasticity.py's definitions - steel that yields, pushed past first yield:
    /// its canvas; the model at the last step in its Von Mises bands, the yielding steel orange, from
    /// along eye (from above when top), and the chart of its load-displacement curve; the model beside
    /// the curve Grasshopper draws; and a frame for every step with the chart of the curve so far. The
    /// stills are at step still, the last when it is -1: a plate's yield lines show before the whole
    /// of its face has yielded.
    /// </summary>
    static List<(string, Func<bool>)> PlasticityScene(string file, string slug, Vector3d eye, bool top,
                                                     string loadName, string unit, string alongTitle, string alongName,
                                                     int still = -1) =>
        new List<(string, Func<bool>)>
    {
        Do(slug, () =>
        {
            SoftEdges(SlaveEdges);
            Open(file);
            foreach (var c in doc.Objects.OfType<IGH_ActiveObject>().Where(o => o.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank))
                Messages(c);
            var view = Find<IGH_Component>("Steel pushed");
            var legend = Find<IGH_Component>("Steel pushed legend");
            var step = Find<GH_NumberSlider>("Step");
            var curve = Find<IGH_Component>("Load-displacement");
            var elastic = Find<IGH_Component>("Elastic");
            var yielding = Find<IGH_Component>("Yielding");
            var axes = Find<IGH_Component>("Axes");
            var d = Values<GH_Number>(Out(curve, "Deflection")).Select(n => n.Value).ToArray();
            var p = Values<GH_Number>(Out(curve, "Load")).Select(n => n.Value).ToArray();
            double yd = Values<GH_Number>(Out(curve, "YieldDeflection")).Select(n => n.Value).DefaultIfEmpty(double.NaN).First();
            double yl = Values<GH_Number>(Out(curve, "YieldLoad")).Select(n => n.Value).DefaultIfEmpty(double.NaN).First();
            if (d.Length < 2) throw new Exception("no curve: did the analysis run?");
            int steps = d.Length - 1;
            Log($"  {steps} steps to {p[steps]:G4} {unit} and {d[steps]:G3} mm; first yield at {yl:G4} {unit} and {yd:G3} mm");
            Canvas(slug + "-canvas");
            int at = still < 0 ? steps - 1 : Math.Min(still, steps - 1);

            ShowOnly(view, legend);
            AtStep(step, at, view);
            var model = Drawn();
            var framed = top ? Top(Grown(model, 0.06)) : Aim(Grown(model, 0.04), eye, Vector3d.ZAxis, magnify: 1.15);
            FitInside(framed, model, 0.17);
            Shoot(framed, slug, WithLegend);
            LoadDeflectionChart(Path.Combine(outDir, slug + "-chart.png"), d, p, yd, yl, at + 1, WithLegend, loadName, unit, alongTitle, alongName);

            Directory.CreateDirectory(Path.Combine(outDir, slug + "-frames"));
            Directory.CreateDirectory(Path.Combine(outDir, slug + "-charts"));
            for (int k = 0; k < steps; k++)
            {
                AtStep(step, k, view);
                Shoot(framed, Path.Combine(slug + "-frames", k.ToString("D3")));
                LoadDeflectionChart(Path.Combine(outDir, slug + "-charts", k.ToString("D3") + ".png"), d, p, yd, yl, k + 1, 1, loadName, unit, alongTitle, alongName);
            }
            Log($"  {steps} frames and their charts, a step each");

            ShowOnly(view, legend, elastic, yielding, axes);
            AtStep(step, at, view);
            var both = Drawn();
            var side = top ? Top(Grown(both, 0.05)) : Aim(Grown(both, 0.05), new Vector3d(0.1, -1, 0.1), Vector3d.ZAxis, magnify: 1.1);
            FitInside(side, both, 0.17);
            Shoot(side, slug + "-and-curve", WithLegend);
            Close();
        }),
    };

    /// <summary>
    /// The load-deflection curve at point k, 2400 px wide times scale: the load up, the deflection at
    /// midspan along, the whole curve in light grey, the curve so far light blue while the beam is
    /// elastic and orange from first yield, first yield marked, the point now a dot, and the two values
    /// now, top right.
    /// </summary>
    static void LoadDeflectionChart(string path, double[] d, double[] p, double yd, double yl, int k, double scale,
                                    string loadName = "Load", string unit = "kN", string alongTitle = "Deflection at midspan [mm]",
                                    string alongName = "Deflection")
    {
        float s = (float)scale;
        int w = (int)(2400 * s), h = (int)(820 * s);
        var r = new RectangleF(260 * s, 130 * s, w - 380 * s, 520 * s);
        double right = Nice(d.Max() * 1.02), top = Nice(p.Max() * 1.08);
        // As many intervals on each axis as make round ticks: 15 mm in five of 3, not four of 3.75;
        // 1 500 kN/m2 in five of 300.
        int along = Divisions(right), up = Divisions(top);
        Func<double, float> X = x => r.Left + (float)(x / right) * r.Width;
        Func<double, float> Y = y => r.Bottom - (float)(y / top) * r.Height;
        k = Math.Max(0, Math.Min(k, d.Length - 1));
        bool yields = !double.IsNaN(yl);
        // The curve with first yield put in it, and the points at or below it - the elastic part.
        var all = new List<PointF>();
        int bend = -1;
        for (int i = 0; i < d.Length; i++)
        {
            if (yields && bend < 0 && p[i] > yl)
            {
                bend = all.Count;
                all.Add(new PointF(X(yd), Y(yl)));
            }
            all.Add(new PointF(X(d[i]), Y(p[i])));
        }
        int now = k + (yields && bend >= 0 && k >= bend ? 1 : 0);
        using (var bitmap = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bitmap))
        using (var big = new Font(ChartFont, 42 * s, GraphicsUnit.Pixel))
        using (var small = new Font(ChartFont, 32 * s, GraphicsUnit.Pixel))
        using (var axis = new Pen(ChartAxis, 2 * s))
        using (var ink = new SolidBrush(DarkTech))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Background);
            for (int i = 0; i <= up; i++)
            {
                double y = top * i / up;
                g.DrawLine(axis, r.Left, Y(y), r.Right, Y(y));
                var label = y.ToString("0.##", Inv);
                var size = g.MeasureString(label, small);
                g.DrawString(label, small, ink, r.Left - size.Width - 14 * s, Y(y) - size.Height / 2);
            }
            for (int i = 0; i <= along; i++)
            {
                double x = right * i / along;
                g.DrawLine(axis, X(x), r.Bottom, X(x), r.Bottom + 10 * s);
                var label = x.ToString("0.##", Inv);
                var size = g.MeasureString(label, small);
                g.DrawString(label, small, ink, X(x) - size.Width / 2, r.Bottom + 16 * s);
            }
            g.DrawString($"{loadName} [{unit}]", big, ink, r.Left - 8 * s, r.Top - 100 * s);
            var title = alongTitle;
            var titleSize = g.MeasureString(title, small);
            g.DrawString(title, small, ink, r.Left + (r.Width - titleSize.Width) / 2, r.Bottom + 66 * s);

            using (var faint = new Pen(ChartFaint, 4 * s) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                g.DrawLines(faint, all.ToArray());
            // First yield: a short dashed line to the load axis, and its load beside it.
            if (yields)
            {
                using (var dash = new Pen(ChartFaint, 3 * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    g.DrawLine(dash, r.Left, Y(yl), X(yd), Y(yl));
                var mark = $"first yield {yl:0} {unit}";
                g.DrawString(mark, small, new SolidBrush(Orange), r.Left + 16 * s, Y(yl) - 46 * s);
            }
            int elasticEnd = yields && bend >= 0 ? Math.Min(now, bend) : now;
            if (elasticEnd > 0)
                using (var pen = new Pen(LightBlue, 7 * s) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                    g.DrawLines(pen, all.Take(elasticEnd + 1).ToArray());
            if (yields && bend >= 0 && now > bend)
                using (var pen = new Pen(Orange, 7 * s) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                    g.DrawLines(pen, all.Skip(bend).Take(now - bend + 1).ToArray());
            using (var dot = new SolidBrush(DarkTech))
                g.FillEllipse(dot, all[now].X - 12 * s, all[now].Y - 12 * s, 24 * s, 24 * s);

            var text = $"{loadName}  {Math.Round(p[k]):0} {unit}      {alongName}  {d[k].ToString(d.Max() < 3 ? "0.00" : "0.0", Inv)} mm";
            var textSize = g.MeasureString(text, big);
            g.DrawString(text, big, ink, r.Right - textSize.Width, r.Top - 100 * s);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>How many intervals split range into ticks of 1, 2, 2.5, 3 or 5 times a power of ten: five if they can, then four, six, three.</summary>
    static int Divisions(double range)
    {
        foreach (int n in new[] { 5, 4, 6, 3 })
        {
            double step = range / n, scale = Math.Pow(10, Math.Floor(Math.Log10(step))), m = step / scale;
            if (new[] { 1.0, 2.0, 2.5, 3.0, 5.0, 10.0 }.Any(x => Math.Abs(m - x) < 1e-9))
                return n;
        }
        return 4;
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
    /// deformation-pictures skill. The colours fade with the swing (<see cref="FadeWithSwing"/>).
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
        FadeWithSwing(results, Math.Abs(full));
        try
        {
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
        }
        finally
        {
            FadeWithSwing(results, 0);
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

    // ------------------------------------------------------------------ 3D printing

    /// <summary>
    /// PrintedCylinder.gh: TU/e's cylinder of fresh concrete, as it is printed - its bricks, ages and
    /// materials from the example's GhPython components. Its canvas; the load
    /// factor on its own weight every third layer, for the chart; the cylinder growing a layer a frame,
    /// the nozzle half way round, coloured by the age of its concrete - Linear Buckling locked, so that
    /// a frame is quick - each with the chart under it; at 30 layers, the ages alone; and, at the layer
    /// it buckles, the still of its buckling mode and the GIF of it swinging.
    /// </summary>
    static List<(string, Func<bool>)> Printing()
    {
        IGH_Component buckling = null, print = null, wall = null, printed = null;
        var layers = new List<double>();
        var factors = new List<double>();
        double lap = 0, critical = 0;
        var eye = new Vector3d(1.0, -1.25, 0.55);
        // Laps of the path, a hair over so that the slider's two decimals never leave the last brick out.
        Action<double> at = n => { Slider("Time [min]", Math.Ceiling(n * lap * 100 + 0.5) / 100); doc.NewSolution(false); };
        Func<double> factor = () => Values<GH_Number>(Out(buckling, "Load Factors")).Select(v => v.Value).DefaultIfEmpty(double.NaN).First();
        return new List<(string, Func<bool>)>
        {
            Do("open", () =>
            {
                Open("PrintedCylinder.gh");
                buckling = Find<IGH_Component>("Buckling");
                print = Find<IGH_Component>("The print");
                wall = Find<IGH_Component>("The wall");
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>()) Messages(c);
                printed = doc.Objects.OfType<IGH_Component>().First(c => c.NickName == "Printed Layers");
                int count = (int)Math.Round(Find<GH_NumberSlider>("Layers").CurrentValue);
                lap = Values<GH_Number>(Out(printed, "Duration")).Single().Value / count;
                Log($"  a lap every {lap:0.0000} min");
                Canvas("printed-cylinder-canvas");
            }),
            Do("factors", () =>
            {
                for (int n = 3; n <= 54; n += 3)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    at(n);
                    layers.Add(n);
                    factors.Add(factor());
                    Log($"  {n} layers: load factor {factors.Last():0.000}, {clock.Elapsed.TotalSeconds:0.0} s");
                    if (factors.Last() < 0.9) break;
                }
                int k = factors.FindIndex(f => f < 1.0);
                critical = k > 0 ? Math.Exp(Math.Log(layers[k - 1]) + Math.Log(factors[k - 1]) / (Math.Log(factors[k - 1]) - Math.Log(factors[k]))
                                          * (Math.Log(layers[k]) - Math.Log(layers[k - 1]))) : layers.Last();
                Log($"  it buckles at {critical:0.0} layers");
                File.WriteAllText(Path.Combine(outDir, "printed-cylinder-factors.txt"),
                    string.Join("\n", layers.Select((n, i) => n.ToString(Inv) + " " + factors[i].ToString("0.0000", Inv))) + "\ncritical " + critical.ToString("0.00", Inv) + "\n");
            }),
            Do("printing", () =>
            {
                // The nozzle: a dark cone, its tip on the bead it is laying, put where Printed Layers
                // has it after each solution.
                var nozzle = Place(new Param_Mesh { NickName = "Nozzle" }, Beside(print, 120, 260));
                var nozzlePreview = Preview(nozzle, DarkTech, Beside(print, 260, 260));
                Action place = () =>
                {
                    var tip = Values<GH_Point>(Out(printed, "Nozzle")).Select(p => p.Value).FirstOrDefault();
                    nozzle.PersistentData.Clear();
                    nozzle.PersistentData.Append(new GH_Mesh(Mesh.CreateFromCone(new Cone(new Plane(tip, Vector3d.ZAxis), 0.07, 0.022), 1, 32)));
                    nozzle.ExpireSolution(false);
                    doc.NewSolution(false);
                };

                // Framed once, round the wall as tall as it gets and the nozzle over it, so that it
                // grows inside the picture.
                buckling.Locked = true;
                ShowOnly(print, nozzlePreview);
                at(Math.Ceiling(critical));
                place();
                var view = Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis);
                Directory.CreateDirectory(Path.Combine(outDir, "printed-cylinder-printing-frames"));
                Directory.CreateDirectory(Path.Combine(outDir, "printed-cylinder-printing-charts"));
                // Eight frames a lap: the nozzle goes round, laying each layer.
                const int PerLayer = 8;
                int frames = (int)Math.Ceiling(critical * PerLayer);
                for (int f = 0; f < frames; f++)
                {
                    double n = Math.Min((f + 1.0) / PerLayer, critical);
                    at(n);
                    place();
                    Shoot(view, Path.Combine("printed-cylinder-printing-frames", f.ToString("D3")));
                    FactorChart(Path.Combine(outDir, "printed-cylinder-printing-charts", f.ToString("D3") + ".png"), layers, factors, critical, n, 1);
                }
                Log($"  {frames} frames printing, {PerLayer} a layer");
                ShowOnly(print);
                at(30);
                Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), "printed-cylinder-ages", 2);
                buckling.Locked = false;
            }),
            Do("buckled", () =>
            {
                at(critical);
                ShowOnly(wall);
                doc.NewSolution(false);
                Log($"  at {critical:0.0} layers the load factor is {factor():0.000}");
                Messages(buckling);
                Messages(wall);
                Log("  " + Info(wall) + $", {Out(wall, "Geometry").VolatileDataCount} pieces drawn");
                Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), "printed-cylinder-buckled", 2);
                FactorChart(Path.Combine(outDir, "printed-cylinder-chart.png"), layers, factors, critical, critical, 1);
                BucklingFrames(wall, eye, "printed-cylinder");
            }),
            Do("close", Close),
        };
    }

    /// <summary>
    /// The load factor on the wall's own weight against the layers printed, 2400 px wide times scale:
    /// the factors found every few layers, in grey, light blue up to <paramref name="now"/>, an orange line at
    /// 1, where the wall buckles, and the value now top right, interpolated on log against log.
    /// </summary>
    /// <summary>A title as wide as a FactorChart, to go over a print shown beside another.</summary>
    static void Label(string path, string text, double scale)
    {
        float s = (float)scale;
        using (var bitmap = new Bitmap((int)(2400 * s), (int)(150 * s)))
        using (var g = Graphics.FromImage(bitmap))
        using (var font = new Font(ChartFont, 64 * s, GraphicsUnit.Pixel))
        using (var ink = new SolidBrush(DarkTech))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Background);
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, ink, (bitmap.Width - size.Width) / 2, (bitmap.Height - size.Height) / 2);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    static void FactorChart(string path, List<double> layers, List<double> factors, double critical, double now, double scale, double end = 0)
    {
        float s = (float)scale;
        int w = (int)(2400 * s), h = (int)(620 * s);
        // The layers axis to the next ten past where it buckles, or past end - the whole print - when given.
        double right = Math.Ceiling(Math.Max(critical, end) / 10) * 10, top = 6;
        Func<double, double> at = n =>
        {
            if (n < layers[0]) return double.NaN;
            int found = layers.FindIndex(x => x >= n);
            int i = Math.Max(1, found < 0 ? layers.Count - 1 : found);
            double t = (Math.Log(n) - Math.Log(layers[i - 1])) / (Math.Log(layers[i]) - Math.Log(layers[i - 1]));
            return Math.Exp(Math.Log(factors[i - 1]) + t * (Math.Log(factors[i]) - Math.Log(factors[i - 1])));
        };
        using (var bitmap = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bitmap))
        using (var big = new Font(ChartFont, 42 * s, GraphicsUnit.Pixel))
        using (var small = new Font(ChartFont, 32 * s, GraphicsUnit.Pixel))
        using (var axis = new Pen(ChartAxis, 2 * s))
        using (var ink = new SolidBrush(DarkTech))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Background);
            var r = new RectangleF(220 * s, 110 * s, w - 300 * s, 400 * s);
            Func<double, float> X = n => r.Left + (float)(n / right) * r.Width;
            Func<double, float> Y = f => r.Bottom - (float)(Math.Min(f, top) / top) * r.Height;
            for (int f = 0; f <= top; f += 2)
            {
                g.DrawLine(axis, r.Left, Y(f), r.Right, Y(f));
                var label = f.ToString(Inv);
                var size = g.MeasureString(label, small);
                g.DrawString(label, small, ink, r.Left - size.Width - 16 * s, Y(f) - size.Height / 2);
            }
            for (int n = 0; n <= right; n += 10)
            {
                var label = n + (n == right ? " layers" : "");
                var size = g.MeasureString(label, small);
                g.DrawString(label, small, ink, X(n) - (n == right ? size.Width : size.Width / 2), r.Bottom + 12 * s);
            }
            using (var pen = new Pen(Orange, 3 * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                g.DrawLine(pen, r.Left, Y(1), r.Right, Y(1));
            using (var orange = new SolidBrush(Orange))
                g.DrawString("it buckles", small, orange, r.Left + 12 * s, Y(1) - 44 * s);
            g.DrawString("Load factor on its own weight", big, ink, r.Left - 8 * s, r.Top - 90 * s);

            var curve = new List<PointF>();
            for (double n = layers[0]; n <= Math.Min(right, layers.Last()) + 1e-9; n += 0.25)
                if (at(n) <= top) curve.Add(new PointF(X(n), Y(at(n))));
            using (var faint = new Pen(ChartFaint, 3 * s)) if (curve.Count > 1) g.DrawLines(faint, curve.ToArray());
            var done = curve.Where(p => p.X <= X(now)).ToArray();
            using (var pen = new Pen(LightBlue, 6 * s) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                if (done.Length > 1) g.DrawLines(pen, done);
            double value = at(now);
            if (value <= top)
                using (var dot = new SolidBrush(LightBlue))
                    g.FillEllipse(dot, X(now) - 12 * s, Y(value) - 12 * s, 24 * s, 24 * s);
            var text = now.ToString("0.0", Inv) + " layers      " + (double.IsNaN(value) ? "" : "λ = " + value.ToString(value < 10 ? "0.00" : "0", Inv));
            var measured = g.MeasureString(text, big);
            using (var brush = new SolidBrush(!double.IsNaN(value) && value < 1.0 ? Orange : DarkTech))
                g.DrawString(text, big, brush, r.Right - measured.Width, r.Top - 90 * s);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>
    /// A shape of PrintedStructure.py - <paramref name="file"/>, its pictures named from
    /// <paramref name="prefix"/>, seen from <paramref name="eye"/> - printed in fresh concrete, as Printing
    /// does for the cylinder: its canvas; the load factor at twenty moments over the whole print, for the
    /// chart; the whole print, a frame every 2.5 s of printing or more (200 frames at most) with the nozzle
    /// on it, coloured by the age of its concrete, the chart under each; the ages at the end of the print;
    /// the settling under its own weight and the buckling mode where the factor first reaches 1, or where
    /// it is lowest if it never does. Stepped by Time, since layers take different times, and charted by
    /// the layers the component says are printed.
    /// </summary>
    static List<(string, Func<bool>)> PrintedShape(string file, string prefix, Vector3d eye, bool flat = false, string label = null)
    {
        IGH_Component buckling = null, print = null, wall = null, printed = null, settle = null, run = null;
        var layers = new List<double>();
        var factors = new List<double>();
        var times = new List<double>();
        double duration = 0, critical = 0, criticalTime = 0;
        Action<double> at = t => { Slider("Time [min]", Math.Ceiling(t * 100) / 100); doc.NewSolution(false); };
        Func<double> factor = () => Values<GH_Number>(Out(buckling, "Load Factors")).Select(v => v.Value).DefaultIfEmpty(double.NaN).First();
        Func<double> printedLayers = () => Values<GH_Number>(Out(printed, "Printed")).Select(v => v.Value).DefaultIfEmpty(0).First();
        return new List<(string, Func<bool>)>
        {
            Do("open", () =>
            {
                Open(file);
                buckling = Find<IGH_Component>("Buckling");
                print = Find<IGH_Component>("The print");
                wall = Find<IGH_Component>("The wall");
                settle = Find<IGH_Component>("The settling");
                run = doc.Objects.OfType<IGH_Component>().First(c => c.Name.Contains("Run Analysis"));
                printed = doc.Objects.OfType<IGH_Component>().First(c => c.NickName == "Printed Layers");
                if (flat)
                {
                    // Its Path unplugged: the shape is printed in flat layers.
                    printed.Params.Input.First(p => p.NickName == "Path").RemoveAllSources();
                    printed.ExpireSolution(false);
                    doc.NewSolution(false);
                }
                duration = Values<GH_Number>(Out(printed, "Duration")).Single().Value;
                Log($"  the print takes {duration:0.00} min");
                foreach (var c in doc.Objects.OfType<IGH_ActiveObject>()) Messages(c);
                Canvas(prefix + "-canvas");
                if (label != null) Label(Path.Combine(outDir, prefix + "-label.png"), label, 1);
            }),
            Do("factors", () =>
            {
                run.Locked = true;
                // The whole print: the factor goes on falling after it reaches 1.
                for (int i = 1; i <= 20; i++)
                {
                    // at() rounds Time up to the slider's 0.01 min, so the last is the print finished.
                    double t = duration * i / 20.0;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    at(t);
                    times.Add(t);
                    layers.Add(printedLayers());
                    factors.Add(factor());
                    Log($"  {t:0.00} min, {layers.Last():0.0} layers: load factor {factors.Last():0.000}, {clock.Elapsed.TotalSeconds:0.0} s");
                    foreach (var m in buckling.RuntimeMessages(GH_RuntimeMessageLevel.Error)) Log("    " + m);
                }
                int k = factors.FindIndex(f => f < 1.0);
                if (k > 0)
                {
                    double w = Math.Log(factors[k - 1]) / (Math.Log(factors[k - 1]) - Math.Log(factors[k]));
                    critical = layers[k - 1] + w * (layers[k] - layers[k - 1]);
                    criticalTime = times[k - 1] + w * (times[k] - times[k - 1]);
                    Log($"  it buckles at {critical:0.0} layers, {criticalTime:0.00} min");
                }
                else
                {
                    // It stands to the end: its closest call, where the factor is lowest, is the moment shown.
                    int low = factors.IndexOf(factors.Min());
                    critical = layers[low];
                    criticalTime = times[low];
                    Log($"  it stands to the end, at {factors.Last():0.000}; the factor is lowest at {critical:0.0} layers, {criticalTime:0.00} min: {factors[low]:0.000}");
                }
                File.WriteAllText(Path.Combine(outDir, prefix + "-factors.txt"),
                    string.Join("\n", layers.Select((n, i) => times[i].ToString("0.00", Inv) + " " + n.ToString("0.00", Inv) + " " + factors[i].ToString("0.0000", Inv))) +
                    // The moment as at() sets the slider, rounded up, so that printingbuckling comes back to the same.
                    "\ncritical " + critical.ToString("0.00", Inv) + " layers " + (Math.Ceiling(criticalTime * 100) / 100).ToString("0.00", Inv) + " min\n");
            }),
            Do("printing", () =>
            {
                var nozzle = Place(new Param_Mesh { NickName = "Nozzle" }, Beside(print, 120, 260));
                var nozzlePreview = Preview(nozzle, DarkTech, Beside(print, 260, 260));
                Action place = () =>
                {
                    var tip = Values<GH_Point>(Out(printed, "Nozzle")).Select(p => p.Value).FirstOrDefault();
                    // Tilted with the layer it lays, along its Axis, where the shape is printed along a Path.
                    var axis = printed.Params.Output.Any(o => o.NickName == "Axis")
                        ? Values<GH_Vector>(Out(printed, "Axis")).Select(v => v.Value).DefaultIfEmpty(Vector3d.ZAxis).First()
                        : Vector3d.ZAxis;
                    nozzle.PersistentData.Clear();
                    nozzle.PersistentData.Append(new GH_Mesh(Mesh.CreateFromCone(new Cone(new Plane(tip, axis), 0.07, 0.022), 1, 32)));
                    nozzle.ExpireSolution(false);
                    doc.NewSolution(false);
                };
                buckling.Locked = true;
                ShowOnly(print, nozzlePreview);
                double end = times.Last();
                at(end);
                place();
                var view = Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis);
                Directory.CreateDirectory(Path.Combine(outDir, prefix + "-printing-frames"));
                Directory.CreateDirectory(Path.Combine(outDir, prefix + "-printing-charts"));
                // A frame for every 2.5 s of printing, or fewer for a long print: 200 at most.
                int frames = Math.Min(200, (int)Math.Ceiling(end * 60 / 2.5));
                for (int f = 0; f < frames; f++)
                {
                    double t = Math.Min((f + 1.0) / frames * end, end);
                    at(t);
                    place();
                    Shoot(view, Path.Combine(prefix + "-printing-frames", f.ToString("D3")));
                    FactorChart(Path.Combine(outDir, prefix + "-printing-charts", f.ToString("D3") + ".png"), layers, factors, critical, printedLayers(), 1, layers.Last());
                }
                Log($"  {frames} frames printing, to {printedLayers():0.0} layers");
                ShowOnly(print);
                at(end);
                Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), prefix + "-ages", 2);
                buckling.Locked = false;
                run.Locked = false;
                at(criticalTime);
                ShowOnly(settle);
                doc.NewSolution(false);
                foreach (var m in run.RuntimeMessages(GH_RuntimeMessageLevel.Error)) Log("    " + m);
                Log("  settling: " + Info(settle));
                Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), prefix + "-settling", 2);
                run.Locked = true;
            }),
            Do("buckled", () =>
            {
                at(criticalTime);
                ShowOnly(wall);
                doc.NewSolution(false);
                Log($"  at {printedLayers():0.0} layers the load factor is {factor():0.000}");
                Messages(buckling);
                Messages(wall);
                Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), prefix + "-buckled", 2);
                FactorChart(Path.Combine(outDir, prefix + "-chart.png"), layers, factors, critical, critical, 1, layers.Last());
                BucklingFrames(wall, eye, prefix);
            }),
            Do("close", Close),
        };
    }

    /// <summary>
    /// The shapes of PrintedStructure.py, each a scene: its definition, the prefix of its pictures, the
    /// direction it is seen from, whether its Path is unplugged so that it is printed in flat layers, and
    /// the label it has beside another.
    /// </summary>
    static readonly (string scene, string file, string prefix, Vector3d eye, bool flat, string label)[] Prints =
    {
        ("printedstructure", "PrintedStructure.gh", "printed-structure", new Vector3d(1.0, -1.15, 0.45), false, null),
        ("printedtwisted", "PrintedTwistedColumn.gh", "twisted-column", new Vector3d(1.0, -1.15, 0.5), false, null),
        ("printedwall", "PrintedWavyWall.gh", "wavy-wall", new Vector3d(0.45, -1.0, 0.45), false, null),
        ("printeddome", "PrintedDome.gh", "printed-dome", new Vector3d(1.0, -1.2, 0.75), false, null),
        // Printed along its axis, the nozzle tilting; and the same column printed flat, its Path unplugged.
        ("printedleaning", "PrintedLeaningColumn.gh", "leaning-column", new Vector3d(0.3, -1.0, 0.3), false, "Printed along its axis"),
        ("printedleaningflat", "PrintedLeaningColumn.gh", "leaning-column-flat", new Vector3d(0.3, -1.0, 0.3), true, "Printed in flat layers"),
    };

    /// <summary>A jet gradient, dark blue to dark red, nine colours evenly along it.</summary>
    static readonly Color[] Jet =
    {
        Color.FromArgb(0, 0, 128), Color.FromArgb(0, 0, 255), Color.FromArgb(0, 128, 255), Color.FromArgb(0, 255, 255),
        Color.FromArgb(128, 255, 128), Color.FromArgb(255, 255, 0), Color.FromArgb(255, 128, 0), Color.FromArgb(255, 0, 0),
        Color.FromArgb(128, 0, 0),
    };

    /// <summary>
    /// A printed shape's buckling mode swinging, into <paramref name="prefix"/>-buckling-frames: <paramref name="wall"/>,
    /// the Model View of Linear Buckling, colours the mode by how far each point moves in it, on a jet gradient,
    /// in place of the age of the concrete, and the camera stays where it is. The user's choice, 2026-11-07: a
    /// mode is read better still than turning.
    /// </summary>
    static void BucklingFrames(IGH_Component wall, Vector3d eye, string prefix)
    {
        ShowResult(wall, 1, 0);
        var colours = (GH_PersistentParam<GH_Colour>)In(wall, "Colors");
        colours.PersistentData.Clear();
        foreach (var c in Jet) colours.PersistentData.Append(new GH_Colour(c));
        colours.ExpireSolution(false);
        doc.NewSolution(false);
        Messages(wall);
        Log("  " + Info(wall));
        string folder = Path.Combine(outDir, prefix + "-buckling-frames");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Deforming(wall, eye, prefix + "-buckling-frames", 40);
    }

    /// <summary>
    /// The buckling modes alone of the 3D printing scenes, each into its own scene's folder, at the moment
    /// that scene found it buckles - read from its factors file, so run the scene once first. For a new look
    /// of the modes without printing every shape again: publish.sh's printingbuckling makes their GIFs.
    /// </summary>
    static List<(string, Func<bool>)> PrintedBuckling()
    {
        var cases = new List<(string scene, string file, string prefix, Vector3d eye, bool flat)>
        {
            ("printing", "PrintedCylinder.gh", "printed-cylinder", new Vector3d(1.0, -1.25, 0.55), false),
        };
        cases.AddRange(Prints.Select(p => (p.scene, p.file, p.prefix, p.eye, p.flat)));
        var list = new List<(string, Func<bool>)>();
        foreach (var c in cases)
        {
            IGH_Component wall = null;
            list.Add(Do("open " + c.scene, () =>
            {
                outDir = Path.Combine(Path.GetDirectoryName(outDir.TrimEnd(Path.DirectorySeparatorChar)), c.scene);
                Open(c.file);
                wall = Find<IGH_Component>("The wall");
                var printed = doc.Objects.OfType<IGH_Component>().First(x => x.NickName == "Printed Layers");
                if (c.flat)
                {
                    printed.Params.Input.First(x => x.NickName == "Path").RemoveAllSources();
                    printed.ExpireSolution(false);
                    doc.NewSolution(false);
                }
                // "critical 46.93" layers for the cylinder, "critical 52.46 layers 9.86 min" for the others.
                var words = File.ReadAllLines(Path.Combine(outDir, c.prefix + "-factors.txt")).Last(l => l.StartsWith("critical")).Split(' ');
                double time;
                // The slider's value already, as PrintedShape writes it - rounded, not up again: 9.21 * 100 is a hair over 921.
                if (words.Length > 3) time = Math.Round(double.Parse(words[3], Inv) * 100) / 100;
                else
                {
                    // As Printing steps it: laps of the path, a hair over.
                    double lap = Values<GH_Number>(Out(printed, "Duration")).Single().Value / (double)Math.Round(Find<GH_NumberSlider>("Layers").CurrentValue);
                    time = Math.Ceiling(double.Parse(words[1], Inv) * lap * 100 + 0.5) / 100;
                }
                Slider("Time [min]", time);
                doc.NewSolution(false);
                double factor = Values<GH_Number>(Out(Find<IGH_Component>("Buckling"), "Load Factors")).Select(v => v.Value).DefaultIfEmpty(double.NaN).First();
                Log($"  {time:0.00} min: load factor {factor:0.000}");
            }));
            list.Add(Do("buckling " + c.scene, () =>
            {
                ShowOnly(wall);
                BucklingFrames(wall, c.eye, c.prefix);
            }));
            list.Add(Do("close " + c.scene, Close));
        }
        return list;
    }


    static BoundingBox Grown(BoundingBox b, double f)
    {
        b.Inflate(b.Diagonal.Length * f);
        return b;
    }
}
