// Buckling lengths through the Grasshopper components themselves, in a running Rhino 8.
//
// A two-storey steel frame, two bays by one, built in code out of the real components with every line
// cut into 0.5 m beams: columns, floor beams, secondary beams, bracing in three walls and in each floor.
// Model View's Buckling menu draws the lengths worked out from it; what it hands out as Info and Values
// is set against the lengths worked out by hand, and a picture of the viewport is saved.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

using Alpaca4d.UIWidgets;

#pragma warning disable CS0612, CS0618

public static class GrasshopperCheck
{
    static StringBuilder report;
    static int fails;
    static string folder;

    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        report.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    public static string Run(string work, string license)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            if (!string.IsNullOrEmpty(license))
                Alpaca4d.License.License.Install(license);
            Frame();
        }
        catch (Exception e)
        {
            Check(false, "no exception: " + e);
        }
        report.AppendLine();
        report.AppendLine(fails == 0 ? "all grasshopper checks passed" : $"{fails} grasshopper check(s) failed");
        return report.ToString();
    }

    static readonly GH_Document Document = new GH_Document();

    static T Add<T>(string unit = null) where T : GH_Component, new()
    {
        var component = new T();
        component.CreateAttributes();
        if (unit != null)
            component.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) }).Invoke(component, new object[] { unit, false, false });
        Document.AddObject(component, false);
        return component;
    }

    static IGH_Param In(GH_Component c, string name) =>
        c.Params.Input.FirstOrDefault(p => p.Name == name || p.NickName == name)
        ?? throw new Exception($"{c.Name} has no input {name} ({string.Join(", ", c.Params.Input.Select(p => p.NickName))})");

    static IGH_Param Out(GH_Component c, string name) =>
        c.Params.Output.FirstOrDefault(p => p.Name == name || p.NickName == name)
        ?? throw new Exception($"{c.Name} has no output {name}");

    static void Set<T>(GH_Component c, string input, params T[] values) where T : class, IGH_Goo
    {
        var param = (GH_PersistentParam<T>)In(c, input);
        param.PersistentData.Clear();
        foreach (var value in values)
            param.PersistentData.Append(value);
        param.ExpireSolution(false);
    }

    static void Wire(GH_Component from, string output, GH_Component to, string input) =>
        In(to, input).AddSource(Out(from, output));

    static T Field<T>(object component, string name)
    {
        for (var type = component.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Any);
            if (field != null) return (T)field.GetValue(component);
        }
        throw new Exception($"{component.GetType().Name} has no field {name}");
    }

    static void Solve(GH_Component component = null)
    {
        Document.Enabled = true;
        if (component == null)
            Document.NewSolution(true, GH_SolutionMode.Silent);
        else
        {
            component.ExpireSolution(false);
            Document.NewSolution(false, GH_SolutionMode.Silent);
        }
    }

    static string Text(GH_Component c, string output) =>
        Out(c, output).VolatileData.AllData(true).Select(x => x.ToString()).FirstOrDefault() ?? "";

    static List<double> Values(GH_Component c) =>
        Out(c, "Values").VolatileData.AllData(true).Select(x => ((GH_Number)x).Value).ToList();

    static List<string> Messages(GH_Component c, GH_RuntimeMessageLevel level) => c.RuntimeMessages(level).ToList();

    /// <summary>A line cut into pieces of about 0.5 m, as lines.</summary>
    static IEnumerable<GH_Curve> Cut(Point3d a, Point3d b)
    {
        int n = Math.Max(1, (int)Math.Round(a.DistanceTo(b) / 0.5));
        for (int k = 0; k < n; k++)
            yield return new GH_Curve(new LineCurve(a + (b - a) * ((double)k / n), a + (b - a) * ((double)(k + 1) / n)));
    }

    static Point3d P(double x, double y, double z) => new Point3d(x, y, z);

    static void Frame()
    {
        report.AppendLine("A braced two-storey frame, 2 x 1 bays of 6 m, storeys of 3.5 m, all cut at 0.5 m");
        Document.FilePath = Path.Combine(folder, "frame.gh");

        var lines = new List<GH_Curve>();
        foreach (double x in new[] { 0.0, 6.0, 12.0 })
            foreach (double y in new[] { 0.0, 6.0 })
                lines.AddRange(Cut(P(x, y, 0), P(x, y, 7)));
        foreach (double z in new[] { 3.5, 7.0 })
        {
            foreach (double y in new[] { 0.0, 6.0 }) lines.AddRange(Cut(P(0, y, z), P(12, y, z)));
            foreach (double x in new[] { 0.0, 3.0, 6.0, 9.0, 12.0 }) lines.AddRange(Cut(P(x, 0, z), P(x, 6, z)));
            // The floor braced in its plane, a diagonal in each panel.
            foreach (double x in new[] { 0.0, 3.0, 6.0, 9.0 }) lines.AddRange(Cut(P(x, 0, z), P(x + 3, 6, z)));
            // A braced bay in each of three walls.
            lines.AddRange(Cut(P(0, 0, z - 3.5), P(6, 0, z)));
            lines.AddRange(Cut(P(6, 6, z - 3.5), P(12, 6, z)));
            lines.AddRange(Cut(P(0, 0, z - 3.5), P(0, 6, z)));
        }

        var steel = Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var rectangle = Add<Alpaca4d.Gh.RectangleCS>();
        Set(rectangle, "Width", new GH_Number(0.2));
        Set(rectangle, "Height", new GH_Number(0.3));
        Wire(steel, "Material", rectangle, "Material");
        var beams = Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beams, "Line", lines.ToArray());
        Wire(rectangle, "Section", beams, "Section");

        var support = Add<Alpaca4d.Gh.Support>();
        var planes = new List<GH_Plane>();
        foreach (double x in new[] { 0.0, 6.0, 12.0 })
            foreach (double y in new[] { 0.0, 6.0 })
                planes.Add(new GH_Plane(new Plane(P(x, y, 0), Vector3d.XAxis, Vector3d.YAxis)));
        Set(support, "Position", planes.ToArray());

        var assemble = Add<Alpaca4d.Gh.AssembleModel>();
        Wire(beams, "Element", assemble, "Elements");
        Wire(support, "Support", assemble, "Supports");

        var view = Add<Alpaca4d.Gh.ModelView>();
        Wire(assemble, "AlpacaModel", view, "AlpacaModel");
        Solve();

        var model = (Alpaca4d.Model)((IGH_Goo)Out(assemble, "AlpacaModel").VolatileData.AllData(true).First()).ScriptVariable();
        Check(model.Beams.Count == lines.Count, $"{lines.Count} beams assembled ({model.Beams.Count})");

        var clock = Stopwatch.StartNew();
        var lengths = Alpaca4d.Design.BucklingLengths.Of(model);
        clock.Stop();
        report.AppendLine($"  worked out in {clock.ElapsedMilliseconds} ms: {lengths.Members.Count} members");

        // By hand. Columns: held at the base and at both floors, which the walls and floors brace.
        // Floor beams along X: held up and down by the columns every 6 m, sideways at every panel point
        // by the braced floor, every 3 m. Secondary beams: carried up and down by the floor beams they sit
        // on, held sideways at their ends: 6 m either way. Braces: end to end.
        double Of(Point3d mid, Alpaca4d.Design.BucklingAxis axis)
        {
            for (int i = 0; i < model.Beams.Count; i++)
            {
                var c = model.Beams[i].Curve;
                if ((c.PointAtStart + c.PointAtEnd) / 2 == mid || ((c.PointAtStart + c.PointAtEnd) / 2).DistanceTo(mid) < 1e-6)
                    return lengths.Of(i, axis);
            }
            return double.NaN;
        }
        Check(Math.Abs(Of(P(6, 0, 1.75), Alpaca4d.Design.BucklingAxis.Y) - 3.5) < 1e-6 && Math.Abs(Of(P(6, 0, 1.75), Alpaca4d.Design.BucklingAxis.Z) - 3.5) < 1e-6,
              $"a column: a storey either way ({Of(P(6, 0, 1.75), Alpaca4d.Design.BucklingAxis.Y)}, {Of(P(6, 0, 1.75), Alpaca4d.Design.BucklingAxis.Z)})");
        Check(Math.Abs(Of(P(4.25, 0, 3.5), Alpaca4d.Design.BucklingAxis.Z) - 6.0) < 1e-6 && Math.Abs(Of(P(4.25, 0, 3.5), Alpaca4d.Design.BucklingAxis.Y) - 3.0) < 1e-6,
              $"a floor beam: 6 m about z, up and down; 3 m about y, held by the floor bracing ({Of(P(4.25, 0, 3.5), Alpaca4d.Design.BucklingAxis.Z)}, {Of(P(4.25, 0, 3.5), Alpaca4d.Design.BucklingAxis.Y)})");
        Check(Math.Abs(Of(P(3, 2.75, 7), Alpaca4d.Design.BucklingAxis.Z) - 6.0) < 1e-6 && Math.Abs(Of(P(3, 2.75, 7), Alpaca4d.Design.BucklingAxis.Y) - 6.0) < 1e-6,
              $"a secondary beam: 6 m either way, carried by the floor beams ({Of(P(3, 2.75, 7), Alpaca4d.Design.BucklingAxis.Z)}, {Of(P(3, 2.75, 7), Alpaca4d.Design.BucklingAxis.Y)})");

        // Model View, Buckling on, about z.
        var show = Field<MenuCheckBox>(view, "_ckBuckling");
        var axis = Field<MenuDropDown>(view, "_ddBuckling");
        show.Active = true;
        axis.Value = 1;
        view.GetType().GetMethod("UpdateVisibility", Any).Invoke(view, null);
        Solve(view);

        var problems = Messages(view, GH_RuntimeMessageLevel.Error).Concat(Messages(view, GH_RuntimeMessageLevel.Warning)).ToList();
        Check(problems.Count == 0, "Model View: no errors or warnings" + (problems.Count == 0 ? "" : ": " + string.Join(" | ", problems)));
        Check(Text(view, "Info").StartsWith("Buckling length about z [m]"), $"Info says what is drawn: {Text(view, "Info")}");
        var values = Values(view);
        double longest = 2 * Math.Sqrt(36 + 3.5 * 3.5);
        Check(values.Count > 1 && Math.Abs(values.First() - 3.5) < 1e-6,
              $"Values for Legend, from the shortest, a storey: {string.Join(", ", values.Select(v => v.ToString("0.##")))}");
        Check(Field<System.Collections.IList>(view, "_bucklingLabels").Count > 0 && Field<System.Collections.IList>(view, "_bucklingMarks").Count > 0,
              $"labels and marks to draw: {Field<System.Collections.IList>(view, "_bucklingLabels").Count} labels, {Field<System.Collections.IList>(view, "_bucklingMarks").Count} marks");

        axis.Value = 0;
        Solve(view);
        Check(Text(view, "Info").StartsWith("Buckling length about y") && Math.Abs(Values(view).First() - 3.0) < 1e-6,
              $"about y: the floor beams' 3 m the shortest ({Text(view, "Info")})");

        // Labels sized for a frame 12 m long.
        Field<MenuSlider>(view, "_slTextSize").Value = 0.25;
        axis.Value = 1;
        Solve(view);
        Picture(view, model, "buckling-about-z.png");
        axis.Value = 0;
        Solve(view);
        Picture(view, model, "buckling-about-y.png");
    }

    /// <summary>The frame drawn by Model View into the active viewport and saved as <paramref name="name"/>; the view put back after.</summary>
    static void Picture(GH_Component view, Alpaca4d.Model model, string name)
    {
        var rhino = Rhino.RhinoDoc.ActiveDoc?.Views?.ActiveView;
        if (rhino == null)
        {
            report.AppendLine("  [SKIP] this Rhino has no document open, so no viewport to draw into");
            return;
        }

        var viewport = rhino.ActiveViewport;
        var saved = new ViewportInfo(viewport);
        bool grid = viewport.ConstructionGridVisible, axes = viewport.ConstructionAxesVisible, world = viewport.WorldAxesVisible;
        var box = new BoundingBox(model.UniquePoints);
        box.Inflate(1.0);
        // The scene's extent, or Rhino clips what is drawn to an empty document's.
        var conduit = new Drawer(Document) { Box = box, Enabled = true };
        conduit.Draw.Add(view);
        try
        {
            viewport.ConstructionGridVisible = viewport.ConstructionAxesVisible = viewport.WorldAxesVisible = false;
            viewport.ChangeToPerspectiveProjection(true, 50);
            viewport.SetCameraLocations(box.Center, box.Center + new Vector3d(-16, -24, 13));
            viewport.ZoomBoundingBox(box);
            using (var bitmap = rhino.CaptureToBitmap(new Size(1600, 1100)))
                bitmap?.Save(Path.Combine(folder, name), System.Drawing.Imaging.ImageFormat.Png);
            Check(conduit.Frames > 0 && conduit.Failure == null, $"{name}: drawn into the viewport without an error" + (conduit.Failure == null ? "" : ": " + conduit.Failure));
        }
        finally
        {
            conduit.Enabled = false;
            viewport.ConstructionGridVisible = grid;
            viewport.ConstructionAxesVisible = axes;
            viewport.WorldAxesVisible = world;
            viewport.SetViewProjection(saved, true);
            rhino.Redraw();
        }
    }

    sealed class Drawer : DisplayConduit
    {
        public readonly List<GH_Component> Draw = new List<GH_Component>();
        public int Frames;
        public string Failure;
        readonly GH_Document document;

        public BoundingBox Box = BoundingBox.Empty;

        public Drawer(GH_Document document) { this.document = document; }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (Box.IsValid) e.IncludeBoundingBox(Box);
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (Draw.Count == 0) return;
            Frames++;
            var ctor = typeof(GH_PreviewArgs).GetConstructors(Any).First(c => c.GetParameters().Length == 9);
            var material = new DisplayMaterial(System.Drawing.Color.Red);
            var args = (IGH_PreviewArgs)ctor.Invoke(new object[] { document, e.Display, e.Viewport, 1, System.Drawing.Color.Black,
                System.Drawing.Color.Green, material, material, MeshingParameters.Default });
            try
            {
                foreach (var c in Draw)
                {
                    c.DrawViewportMeshes(args);
                    c.DrawViewportWires(args);
                }
            }
            catch (Exception ex)
            {
                Failure = Failure ?? ex.ToString();
            }
        }
    }
}
