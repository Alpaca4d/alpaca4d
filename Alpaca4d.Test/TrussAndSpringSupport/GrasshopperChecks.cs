// Truss and Spring Support through the Grasshopper components themselves, in a running Rhino 8.
//
// Each model is a Grasshopper document built in code out of the real components - Uniaxial,
// RectangleCS, the beam switcher on its Truss or ForceBeamColumn type, Support, Spring Support,
// Point Load, Linear Load, Linear Time Series, Load Pattern, Assemble Model, Analysis Settings, Run
// Analysis - solved the way the canvas solves it, and read back through Beam Forces, Reaction Forces
// and Nodal Displacements. Every number is checked against closed form, and every component's
// runtime messages are looked at. grasshopper.sh loads this build of the plugin beside the
// installed one and calls Run by reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    static void Section(string title)
    {
        report.AppendLine();
        report.AppendLine(title);
    }

    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol * Math.Max(1.0, Math.Abs(b));

    public static string Run(string work)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            Panel();
            Cable();
            LineLoadOnTruss();
            Bearing(compressionOnly: true);
            Bearing(compressionOnly: false);
            SpringHeldRigidly();
            LinearAlgorithm();
            ChosenEnergyTest();
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

    static void SetAny(GH_Component c, string input, params IGH_Goo[] values)
    {
        var param = In(c, input);
        var data = (GH_Structure<IGH_Goo>)param.GetType().GetProperty("PersistentData").GetValue(param);
        data.Clear();
        foreach (var value in values)
            data.Append(value);
    }

    static void Wire(GH_Component from, string output, GH_Component to, string input) =>
        In(to, input).AddSource(Out(from, output));

    static GH_Curve Line(double x0, double y0, double z0, double x1, double y1, double z1) =>
        new GH_Curve(new LineCurve(new Point3d(x0, y0, z0), new Point3d(x1, y1, z1)));

    static GH_Point Pt(double x, double y, double z) => new GH_Point(new Point3d(x, y, z));

    static List<double> Numbers(IGH_Param p, GH_Path path) =>
        p.VolatileData.get_Branch(path).Cast<object>().Select(o => o is GH_Number n ? n.Value : Convert.ToDouble(((IGH_Goo)o).ScriptVariable())).ToList();

    static List<Vector3d> Vectors(IGH_Param p) =>
        p.VolatileData.AllData(true).Select(g => ((GH_Vector)g).Value).ToList();

    static List<Point3d> Origins(IGH_Param p) =>
        p.VolatileData.AllData(true).Select(g => ((GH_Plane)g).Value.Origin).ToList();

    /// <summary>A material and a w x h rectangle of it, the section every model here uses.</summary>
    static GH_Component Section(Doc doc, double width, double height)
    {
        var material = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var section = doc.Add<Alpaca4d.Gh.RectangleCS>();
        Set(section, "Width", new GH_Number(width));
        Set(section, "Height", new GH_Number(height));
        Wire(material, "Material", section, "Material");
        return section;
    }

    static GH_Component Support(Doc doc, GH_Point at, bool tx, bool ty, bool tz, bool rx, bool ry, bool rz)
    {
        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", new GH_Plane(new Plane(at.Value, Vector3d.XAxis, Vector3d.YAxis)));
        var states = new[] { tx, ty, tz, rx, ry, rz };
        var names = new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" };
        for (int i = 0; i < 6; i++)
            Set(support, names[i], new GH_Boolean(states[i]));
        return support;
    }

    /// <summary>Loads into a plain pattern on a linear time series, a model assembled, settings, and a run.</summary>
    static (GH_Component assemble, GH_Component run, GH_Component settings) Analyse(Doc doc, IEnumerable<GH_Component> elements,
        IEnumerable<GH_Component> supports, IEnumerable<GH_Component> loads, string algorithm = null)
    {
        var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(series, "TimeSeries", pattern, "TimeSeries");
        foreach (var load in loads)
            In(pattern, "Loads").AddSource(load.Params.Output[0]);

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        foreach (var element in elements)
            In(assemble, "Elements").AddSource(element.Params.Output[0]);
        foreach (var support in supports)
            In(assemble, "Supports").AddSource(support.Params.Output[0]);
        In(assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);

        var settings = doc.Add<Alpaca4d.Gh.AnalysisSettings>();
        if (algorithm != null)
            Set(settings, "Algorithm", new GH_String(algorithm));

        var run = doc.Add<Alpaca4d.Gh.RunAnalysis>();
        Wire(assemble, "AlpacaModel", run, "AlpacaModel");
        Wire(settings, "Settings", run, "Settings");
        return (assemble, run, settings);
    }

    static GH_Component Reader<T>(Doc doc, GH_Component run) where T : GH_Component, new()
    {
        var reader = doc.Add<T>();
        Wire(run, "AlpacaModel", reader, "AlpacaModel");
        return reader;
    }

    static void NoErrors(Doc doc, string what)
    {
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Count == 0, errors.Count == 0 ? $"{what}: no component has an error" : $"{what}: {string.Join(" | ", errors)}");
    }

    static Vector3d ReactionAt(GH_Component reactions, Point3d at, int nth = 0)
    {
        var origins = Origins(Out(reactions, "SupportPosition"));
        var forces = Vectors(Out(reactions, "ReactionForce"));
        var rows = Enumerable.Range(0, origins.Count).Where(i => origins[i].DistanceTo(at) < 1e-9).ToList();
        return forces[rows[nth]];
    }

    // ---------------------------------------------------------------- the models

    static void Panel()
    {
        Section("An X-braced panel of trusses, tension-only diagonals, 10 kN across its top");
        var doc = new Doc("panel");
        var section = Section(doc, 0.1, 0.1);

        var chords = doc.Add<Alpaca4d.Gh.BeamBase>("Truss (Alpaca4d)");
        Set(chords, "Line", Line(0, 0, 0, 4, 0, 0), Line(4, 0, 0, 4, 0, 3), Line(4, 0, 3, 0, 0, 3), Line(0, 0, 3, 0, 0, 0));
        Wire(section, "Section", chords, "Section");

        var diagonals = doc.Add<Alpaca4d.Gh.BeamBase>("Truss (Alpaca4d)");
        Set(diagonals, "Line", Line(0, 0, 0, 4, 0, 3), Line(4, 0, 0, 0, 0, 3));
        Set(diagonals, "WorksIn", new GH_String("Tension"));
        Set(diagonals, "ElementId", new GH_String("diagonal"));
        Wire(section, "Section", diagonals, "Section");

        var supports = new[]
        {
            Support(doc, Pt(0, 0, 0), true, true, true, false, false, false),
            Support(doc, Pt(4, 0, 0), false, true, true, false, false, false),
            Support(doc, Pt(4, 0, 3), false, true, false, false, false, false),
            Support(doc, Pt(0, 0, 3), false, true, false, false, false, false),
        };
        var load = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(load, "Point", Pt(0, 0, 3));
        Set(load, "Force", new GH_Vector(new Vector3d(10, 0, 0)));

        var (assemble, run, _) = Analyse(doc, new[] { chords, diagonals }, supports, new[] { load });
        var forces = Reader<Alpaca4d.Gh.BeamForce>(doc, run);
        Set(forces, "ElementId", new GH_String("diagonal"));
        var reactions = Reader<Alpaca4d.Gh.ReactionForce>(doc, run);
        var displacements = Reader<Alpaca4d.Gh.NodalDisplacement>(doc, run);
        doc.Solve();

        NoErrors(doc, "the whole definition");
        Check(!assemble.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(), "Assemble Model has nothing to warn about");
        Check(run.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Concat(run.RuntimeMessages(GH_RuntimeMessageLevel.Warning)).All(m => !m.Contains("Linear")),
              "Run Analysis, on Newton, does not warn about the Linear algorithm");
        Check(chords.Message.StartsWith("Truss"), $"the beam component shows its type: {chords.Message.Replace("\n", " ")}");

        var n = Out(forces, "N");
        var paths = n.VolatileData.Paths.ToList();
        Check(paths.Count == 2, $"Beam Forces, filtered on ElementId \"diagonal\", gives the two diagonals ({paths.Count} branches)");
        if (paths.Count == 2)
        {
            var first = Numbers(n, paths[0]);
            var second = Numbers(n, paths[1]);
            Check(first.Count == 2 && Near(first[0], 12.5, 1e-3) && first[0] == first[1], $"the diagonal the load pulls carries H / cos = 12.5 kN at both ends ({string.Join(", ", first.Select(v => v.ToString("G6")))})");
            Check(second.All(v => v == 0.0), $"the other one is slack ({string.Join(", ", second)})");
            var shear = Out(forces, "Vy").VolatileData.AllData(true).Concat(Out(forces, "Mz").VolatileData.AllData(true))
                        .Select(g => ((GH_Number)g).Value).ToList();
            Check(shear.All(v => v == 0.0), "no shear and no moment in a truss");
        }

        var pinned = ReactionAt(reactions, new Point3d(0, 0, 0));
        var roller = ReactionAt(reactions, new Point3d(4, 0, 0));
        Check(Near(pinned.X, -10.0, 1e-4), $"Reaction Forces: the pinned support holds the 10 kN ({pinned.X:G6})");
        Check(Near(pinned.Z, -7.5, 1e-3) && Near(roller.Z, 7.5, 1e-3), $"and the overturning couple, 10 x 3 / 4 = 7.5 kN each way ({pinned.Z:G6}, {roller.Z:G6})");

        var nodes = Out(displacements, "Position").VolatileData.AllData(true).Count();
        Check(nodes == 4, $"Nodal Displacements reports the four nodes ({nodes})");
    }

    static void Cable()
    {
        Section("A cable prestressed to 50 kN between fixed ends, 2 kN at its middle");
        var doc = new Doc("cable");
        var section = Section(doc, 0.01, 0.01);   // 1 cm2

        var cable = doc.Add<Alpaca4d.Gh.BeamBase>("Truss (Alpaca4d)");
        Set(cable, "Line", Line(0, 0, 0, 5, 0, 0), Line(5, 0, 0, 10, 0, 0));
        Set(cable, "WorksIn", new GH_String("Tension"));
        Set(cable, "Prestress", new GH_Number(50.0));
        Wire(section, "Section", cable, "Section");

        var supports = new[]
        {
            Support(doc, Pt(0, 0, 0), true, true, true, true, true, true),
            Support(doc, Pt(10, 0, 0), true, true, true, true, true, true),
            Support(doc, Pt(5, 0, 0), false, true, false, false, false, false),
        };
        var load = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(load, "Point", Pt(5, 0, 0));
        Set(load, "Force", new GH_Vector(new Vector3d(0, 0, -2)));

        var (_, run, _) = Analyse(doc, new[] { cable }, supports, new[] { load });
        var forces = Reader<Alpaca4d.Gh.BeamForce>(doc, run);
        var displacements = Reader<Alpaca4d.Gh.NodalDisplacement>(doc, run);
        doc.Solve();
        NoErrors(doc, "the definition");

        var n = Out(forces, "N");
        double force = Numbers(n, n.VolatileData.Paths[0])[0];
        var moved = Out(displacements, "Displacement").VolatileData.AllData(true).Select(g => ((GH_Vector)g).Value).ToList();
        double sag = -moved.Min(v => v.Z);
        double length = Math.Sqrt(25.0 + sag * sag);
        double ea = 2.1e8 * 1e-4;
        Check(sag > 0.05 && sag < 0.15, $"it sags {sag:G4} m, held by its prestress");
        Check(Near(force, 50.0 + ea * (length - 5.0) / 5.0, 1e-3), $"Beam Forces: its force is the prestress and its stretch ({force:G6} kN)");
        Check(Near(2.0 * force * sag / length, 2.0, 1e-3), "and the middle node is in equilibrium where it has moved to");
    }

    static void LineLoadOnTruss()
    {
        Section("A line load of 3 kN/m on a 4 m truss");
        var doc = new Doc("lineload");
        var section = Section(doc, 0.1, 0.1);
        var bar = doc.Add<Alpaca4d.Gh.BeamBase>("Truss (Alpaca4d)");
        Set(bar, "Line", Line(0, 0, 0, 4, 0, 0));
        Wire(section, "Section", bar, "Section");
        var supports = new[]
        {
            Support(doc, Pt(0, 0, 0), true, true, true, false, false, false),
            Support(doc, Pt(4, 0, 0), false, true, true, false, false, false),
        };
        var load = doc.Add<Alpaca4d.Gh.LinearLoad>();
        In(load, load.Params.Input[0].Name).AddSource(Out(bar, "Element"));
        Set(load, "Force", new GH_Vector(new Vector3d(0, 0, -3)));

        var (_, run, _) = Analyse(doc, new[] { bar }, supports, new[] { load });
        var reactions = Reader<Alpaca4d.Gh.ReactionForce>(doc, run);
        doc.Solve();
        NoErrors(doc, "the definition");
        var a = ReactionAt(reactions, new Point3d(0, 0, 0));
        var b = ReactionAt(reactions, new Point3d(4, 0, 0));
        Check(Near(a.Z, 6.0, 1e-6) && Near(b.Z, 6.0, 1e-6), $"half of w L at each end, 6 kN ({a.Z:G6}, {b.Z:G6})");
    }

    static void Bearing(bool compressionOnly)
    {
        Section(compressionOnly
            ? "A two-span beam, the end of its unloaded span on a compression-only Spring Support"
            : "The same beam on a very stiff Elastic Spring Support");
        var doc = new Doc(compressionOnly ? "bearing-one-way" : "bearing-elastic");
        var section = Section(doc, 0.05, 0.1);

        var span1 = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(span1, "Line", Line(0, 0, 0, 4, 0, 0));
        Set(span1, "ElementId", new GH_String("span1"));
        Wire(section, "Section", span1, "Section");
        var span2 = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(span2, "Line", Line(4, 0, 0, 8, 0, 0));
        Wire(section, "Section", span2, "Section");

        var spring = doc.Add<Alpaca4d.Gh.SpringSupport>(compressionOnly ? "Spring Support Compression Only (Alpaca4d)" : "Spring Support Elastic (Alpaca4d)");
        Set(spring, "Position", new GH_Plane(new Plane(new Point3d(8, 0, 0), Vector3d.XAxis, Vector3d.YAxis)));
        if (!compressionOnly)
            Set(spring, "Kz", new GH_Number(1e12));

        var supports = new[]
        {
            Support(doc, Pt(0, 0, 0), true, true, true, true, false, true),
            Support(doc, Pt(4, 0, 0), false, true, true, true, false, true),
            Support(doc, Pt(8, 0, 0), false, true, false, true, false, true),
            spring,
        };
        var load = doc.Add<Alpaca4d.Gh.LinearLoad>();
        SetAny(load, load.Params.Input[0].Name, new GH_String("span1"));
        Set(load, "Force", new GH_Vector(new Vector3d(0, 0, -10)));

        var (assemble, run, _) = Analyse(doc, new[] { span1, span2 }, supports, new[] { load });
        var reactions = Reader<Alpaca4d.Gh.ReactionForce>(doc, run);
        var displacements = Reader<Alpaca4d.Gh.NodalDisplacement>(doc, run);
        doc.Solve();
        NoErrors(doc, "the definition");
        Check(spring.Message.StartsWith(compressionOnly ? "Compression Only" : "Elastic"), $"Spring Support shows its type: {spring.Message.Replace("\n", " ")}");
        Check(!assemble.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(), "Assemble Model has nothing to warn about: the fix beside the spring holds other directions");

        var origins = Origins(Out(reactions, "SupportPosition"));
        Check(origins.Count(o => o.DistanceTo(new Point3d(8, 0, 0)) < 1e-9) == 2, "Reaction Forces: two rows at the end, the fix and the spring");
        var first = ReactionAt(reactions, new Point3d(0, 0, 0));
        var middle = ReactionAt(reactions, new Point3d(4, 0, 0));
        var fix = ReactionAt(reactions, new Point3d(8, 0, 0), 0);
        var bearing = ReactionAt(reactions, new Point3d(8, 0, 0), 1);
        Check(Near(fix.Z, 0.0, 1e-6), $"the fix takes nothing vertical ({fix.Z:G6})");

        if (compressionOnly)
        {
            var remarks = run.RuntimeMessages(GH_RuntimeMessageLevel.Remark).ToList();
            Check(remarks.Any(r => r.Contains("NormDispAndUnbalance")), $"Run Analysis checks the unbalanced force too, and says so: {remarks.FirstOrDefault()}");
            Check(bearing.Z == 0.0, $"the end lifts off: the spring's row reads zero ({bearing.Z:G6})");
            Check(Near(first.Z, 20.0, 1e-6) && Near(middle.Z, 20.0, 1e-6), $"and the loaded span is simply supported, wL/2 = 20 kN each ({first.Z:G6}, {middle.Z:G6})");
            var moved = Vectors(Out(displacements, "Displacement"));
            Check(moved.Max(v => v.Z) > 0.0, $"Nodal Displacements: the end rises ({moved.Max(v => v.Z):G4} m)");
        }
        else
        {
            Check(Near(bearing.Z, -2.5, 2e-3), $"the spring holds the end down, -wL/16 = -2.5 kN ({bearing.Z:G6})");
            Check(Near(first.Z, 17.5, 2e-3) && Near(middle.Z, 25.0, 2e-3), $"7wL/16 and 5wL/8 at the others ({first.Z:G6}, {middle.Z:G6})");
        }
    }

    static void SpringHeldRigidly()
    {
        Section("A spring along an axis a Support already holds");
        var doc = new Doc("held");
        var section = Section(doc, 0.1, 0.2);
        var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beam, "Line", Line(0, 0, 0, 3, 0, 0));
        Wire(section, "Section", beam, "Section");
        var spring = doc.Add<Alpaca4d.Gh.SpringSupport>("Spring Support Elastic (Alpaca4d)");
        Set(spring, "Position", new GH_Plane(new Plane(new Point3d(3, 0, 0), Vector3d.XAxis, Vector3d.YAxis)));
        Set(spring, "Kz", new GH_Number(1000.0));
        var supports = new[]
        {
            Support(doc, Pt(0, 0, 0), true, true, true, true, true, true),
            Support(doc, Pt(3, 0, 0), true, true, true, false, false, false),
            spring,
        };
        var load = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(load, "Point", Pt(3, 0, 0));
        Set(load, "Force", new GH_Vector(new Vector3d(0, 0, -1)));
        var (assemble, _, _) = Analyse(doc, new[] { beam }, supports, new[] { load });
        doc.Solve();
        var warnings = assemble.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Any(w => w.Contains("does nothing")), $"Assemble Model warns: {warnings.FirstOrDefault()}");
    }

    static void ChosenEnergyTest()
    {
        Section("A compression-only Spring Support with an energy test picked on purpose");
        var doc = new Doc("energy");
        var section = Section(doc, 0.05, 0.1);
        var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beam, "Line", Line(0, 0, 0, 3, 0, 0));
        Wire(section, "Section", beam, "Section");
        var spring = doc.Add<Alpaca4d.Gh.SpringSupport>("Spring Support Compression Only (Alpaca4d)");
        Set(spring, "Position", new GH_Plane(new Plane(new Point3d(3, 0, 0), Vector3d.XAxis, Vector3d.YAxis)));
        var supports = new[] { Support(doc, Pt(0, 0, 0), true, true, true, true, true, true), spring };
        var load = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(load, "Point", Pt(3, 0, 0));
        Set(load, "Force", new GH_Vector(new Vector3d(0, 0, -1)));
        var (_, run, settings) = Analyse(doc, new[] { beam }, supports, new[] { load });
        var test = doc.Add<Alpaca4d.Gh.TestBase>("EnergyIncr");
        Wire(test, test.Params.Output[0].Name, settings, "Test");
        doc.Solve();
        var warnings = run.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Any(w => w.Contains("convergence test")), $"Run Analysis leaves the choice alone and warns: {warnings.FirstOrDefault()}");
    }

    static void LinearAlgorithm()
    {
        Section("Tension-only bars run with the Linear algorithm");
        var doc = new Doc("linear");
        var section = Section(doc, 0.1, 0.1);
        var bars = doc.Add<Alpaca4d.Gh.BeamBase>("Truss (Alpaca4d)");
        Set(bars, "Line", Line(0, 0, 0, 4, 0, 0));
        Set(bars, "WorksIn", new GH_String("Tension"));
        Wire(section, "Section", bars, "Section");
        var supports = new[]
        {
            Support(doc, Pt(0, 0, 0), true, true, true, false, false, false),
            Support(doc, Pt(4, 0, 0), false, true, true, false, false, false),
        };
        var load = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(load, "Point", Pt(4, 0, 0));
        Set(load, "Force", new GH_Vector(new Vector3d(1, 0, 0)));
        var (_, run, _) = Analyse(doc, new[] { bars }, supports, new[] { load }, algorithm: "Linear");
        doc.Solve();
        var warnings = run.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Any(w => w.Contains("Linear algorithm")), $"Run Analysis warns: {warnings.FirstOrDefault()}");
    }
}
