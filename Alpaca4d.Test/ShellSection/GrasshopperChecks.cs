// Shell Section through the Grasshopper components themselves, in a running Rhino 8.
//
// A cantilever plate built out of the real components - nD Elastic Isotropic, Plate Fiber Section,
// ASD Shell, Support, Point Load, Linear Time Series, Load Pattern, Assemble Model, Analysis
// Settings, Run Analysis - solved the way the canvas solves it, then cut with Shell Section. The
// totals are checked against statics, the outputs' shape against what the component promises, and
// every component's runtime messages are looked at. grasshopper.sh loads this build of the plugin
// beside the installed one and calls Run by reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

public static class GrasshopperCheck
{
    static StringBuilder report;
    static int fails;
    static string folder;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        report.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Close(double got, double want, double tol, string what)
    {
        Check(Math.Abs(got - want) <= tol * Math.Max(1.0, Math.Abs(want)), $"{what}: got {got:G6}, want {want:G6}");
    }

    static void Section(string title)
    {
        report.AppendLine();
        report.AppendLine(title);
    }

    public static string Run(string work, string license)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            if (File.Exists(license))
                Alpaca4d.License.License.Install(license);

            Quads();
            Triangles();
        }
        catch (Exception e)
        {
            Check(false, "no exception: " + e);
        }
        report.AppendLine();
        report.AppendLine(fails == 0 ? "all grasshopper checks passed" : $"{fails} grasshopper check(s) failed");
        return report.ToString();
    }

    // ---------------------------------------------------------------- building a document

    sealed class Doc
    {
        public readonly GH_Document Document = new GH_Document();
        readonly List<GH_Component> components = new List<GH_Component>();

        public Doc(string name)
        {
            // Run Analysis writes its deck and results beside the document.
            Document.FilePath = Path.Combine(folder, name + ".gh");
        }

        public T Add<T>(string unit = null) where T : GH_Component, new()
        {
            var component = new T();
            component.CreateAttributes();
            if (unit != null)
                component.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) }).Invoke(component, new object[] { unit, false, false });
            Document.AddObject(component, false);
            components.Add(component);
            return component;
        }

        public void Solve()
        {
            Document.Enabled = true;
            Document.NewSolution(true, GH_SolutionMode.Silent);
        }

        public IEnumerable<string> Messages(GH_RuntimeMessageLevel level) =>
            components.SelectMany(c => c.RuntimeMessages(level).Select(m => $"{c.NickName}: {m}"));
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
    }

    static void Wire(GH_Component from, string output, GH_Component to, string input) =>
        In(to, input).AddSource(Out(from, output));

    static List<double> Numbers(IGH_Param p) =>
        p.VolatileData.AllData(true).Select(g => ((GH_Number)g).Value).ToList();

    static void NoMessages(Doc doc)
    {
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        var warnings = doc.Messages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(errors.Count == 0, "no errors" + (errors.Count > 0 ? ": " + string.Join(" | ", errors) : ""));
        Check(warnings.Count == 0, "no warnings" + (warnings.Count > 0 ? ": " + string.Join(" | ", warnings) : ""));
    }

    static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);

    /// <summary>
    /// A cantilever plate 4 by 1, fixed along x = 0, with 10 down on its tip, cut by
    /// <paramref name="curves"/>. Quads, or each cut into two triangles.
    /// </summary>
    static (Doc doc, GH_Component section) Cantilever(string name, bool triangles, params Curve[] curves)
    {
        var doc = new Doc(name);

        int nx = 8, ny = 2;
        var mesh = new Mesh();
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
                mesh.Vertices.Add(4.0 * i / nx, 1.0 * j / ny, 0.0);
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 2, d = a + nx + 1;
                if (triangles)
                {
                    mesh.Faces.AddFace(a, b, c);
                    mesh.Faces.AddFace(a, c, d);
                }
                else
                {
                    mesh.Faces.AddFace(a, b, c, d);
                }
            }

        var material = doc.Add<SimplexGh.nD>("ElasticIsotropic (Alpaca4d)");
        Set(material, "E", new GH_Number(30000000.0));
        Set(material, "G", new GH_Number(15000000.0));
        Set(material, "ν", new GH_Number(0.0));
        Set(material, "Rho", new GH_Number(0.0));

        var plate = doc.Add<Alpaca4d.Gh.PlateFiberSection>();
        Set(plate, "Thickness", new GH_Number(0.2));
        Wire(material, "Material", plate, "Material");

        var shell = doc.Add<Alpaca4d.Gh.ASDShell>();
        Set(shell, "Mesh", new GH_Mesh(mesh));
        Wire(plate, "Section", shell, "Section");

        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", Enumerable.Range(0, ny + 1).Select(j => new GH_Plane(new Plane(new Point3d(0, 1.0 * j / ny, 0), Vector3d.XAxis, Vector3d.YAxis))).ToArray());

        var load = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(load, "Point", Enumerable.Range(0, ny + 1).Select(j => new GH_Point(new Point3d(4, 1.0 * j / ny, 0))).ToArray());
        Set(load, "Force", Enumerable.Range(0, ny + 1).Select(j => new GH_Vector(new Vector3d(0, 0, -10.0 * (j == 0 || j == ny ? 0.5 : 1.0) / ny))).ToArray());

        var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(series, "TimeSeries", pattern, "TimeSeries");
        In(pattern, "Loads").AddSource(load.Params.Output[0]);

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        Wire(shell, "Element", assemble, "Elements");
        Wire(support, "Support", assemble, "Supports");
        In(assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);

        var run = doc.Add<Alpaca4d.Gh.RunAnalysis>();
        Wire(assemble, "AlpacaModel", run, "AlpacaModel");
        var settings = doc.Add<Alpaca4d.Gh.AnalysisSettings>();
        Wire(settings, "Settings", run, "Settings");

        var section = doc.Add<Alpaca4d.Gh.ShellSectionForces>();
        Wire(run, "AlpacaModel", section, "AlpacaModel");
        Set(section, "Curve", curves.Select(c => new GH_Curve(c)).ToArray());

        doc.Solve();
        return (doc, section);
    }

    // ---------------------------------------------------------------- the checks

    static void Quads()
    {
        Section("A cantilever of quads, cut twice: at x = 1.3 and along the line of the mesh at x = 2");

        var (doc, section) = Cantilever("quads", false,
            new LineCurve(new Point3d(1.3, -0.5, 0), new Point3d(1.3, 1.5, 0)),
            new LineCurve(new Point3d(2.0, -0.5, 0), new Point3d(2.0, 1.5, 0)));
        NoMessages(doc);

        // One total per curve, a branch per curve's stretch.
        var my = Numbers(Out(section, "My"));
        var vz = Numbers(Out(section, "Vz"));
        Check(my.Count == 2 && vz.Count == 2, $"one total per curve ({my.Count}, {vz.Count})");
        if (my.Count == 2)
        {
            // Without a Direction, swept along the shells' own z, up: the tip hogs, top in tension.
            Close(my[0], 27.0, 1e-6, "My at x = 1.3, smoothed, is the tip load times 2.7");
            Close(my[1], 20.0, 1e-6, "My at x = 2 is the tip load times 2");
            Close(vz[0], -10.0, 1e-6, "Vz is the tip load");
        }

        // The curves share a branch, {0;0} here, as their totals do; each curve's stretches go in a
        // branch under it, ending {...;curve;stretch}.
        var cuts = Out(section, "Cut").VolatileData;
        var paths = cuts.Paths.ToList();
        Check(paths.Count == 2 && paths.All(p => p[p.Length - 1] == 0) && paths[0][paths[0].Length - 2] == 0 && paths[1][paths[1].Length - 2] == 1,
              $"Cut has a branch per curve, {{...;curve;stretch}}: {string.Join(" ", paths.Select(p => p.ToString()))}");
        var length = cuts.AllData(true).Select(g => ((GH_Curve)g).Value.GetLength()).ToList();
        Check(length.All(l => Math.Abs(l - 1.0) < 1e-9), $"each one across the plate, 1 long ({string.Join(", ", length.Select(l => l.ToString("G6")))})");

        var points = Out(section, "Points").VolatileData;
        var mxx = Out(section, "mxx").VolatileData;
        Check(points.DataCount == mxx.DataCount && points.DataCount > 0, $"a value per point ({points.DataCount}, {mxx.DataCount})");

        var axes = Out(section, "Axes").VolatileData.AllData(true).Select(g => ((GH_Plane)g).Value).ToList();
        Check(axes.Count == 2 && (axes[0].Origin - new Point3d(1.3, 0.5, 0)).Length < 1e-9 && (axes[0].XAxis - Vector3d.XAxis).Length < 1e-9,
              "Axes at the centre of the cut, x to the tip");

        // Drawn: a diagram once the solution is done.
        Check(Field<Mesh>(section, "_diagram") != null, "a diagram is drawn");

        // Unsmoothed, each stretch reads its own element's moment, constant over it.
        var smooth = Field<object>(section, "_ckSmooth");
        smooth.GetType().GetProperty("Active").SetValue(smooth, false);
        section.ExpireSolution(false);
        doc.Solve();
        my = Numbers(Out(section, "My"));
        Check(my.Count == 2 && Math.Abs(my[0] - 27.5) < 1e-6 && Math.Abs(my[1] - 17.5) < 1e-6,
              $"unsmoothed, the elements' own moments: 27.5 and 17.5 ({string.Join(", ", my.Select(m => m.ToString("G6")))})");

        // Swept down by hand: z down, the hogging moment now negative.
        Set(section, "Direction", new GH_Vector(-Vector3d.ZAxis));
        section.ExpireSolution(false);
        doc.Solve();
        my = Numbers(Out(section, "My"));
        Check(my.Count == 2 && Math.Abs(my[0] + 27.5) < 1e-6, $"swept down, My turns with z ({(my.Count > 0 ? my[0] : double.NaN):G6})");

        // A curve that misses says so.
        Set(section, "Curve", new GH_Curve(new LineCurve(new Point3d(10, -0.5, 0), new Point3d(10, 1.5, 0))));
        section.ExpireSolution(false);
        doc.Solve();
        var warnings = section.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Any(w => w.Contains("crosses none of the shells")), "a curve beside the plate warns that it cuts nothing");
    }

    static void Triangles()
    {
        Section("A cantilever of triangles: the shears scaled back to the ones in equilibrium");

        var (doc, section) = Cantilever("triangles", true, new LineCurve(new Point3d(1.3, -0.5, 0), new Point3d(1.3, 1.5, 0)));
        NoMessages(doc);

        var my = Numbers(Out(section, "My"));
        var vz = Numbers(Out(section, "Vz"));
        Check(my.Count == 1 && vz.Count == 1, "one total");
        if (my.Count == 1)
        {
            Close(my[0], 27.0, 1e-2, "My to 1%");
            // As recorded, half as large again: -15.
            Close(vz[0], -10.0, 1e-1, "Vz near the tip load, not the recorded half as large again");
        }
    }
}
