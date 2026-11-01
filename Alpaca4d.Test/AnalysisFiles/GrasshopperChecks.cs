// Two analyses in one Grasshopper document, each with files of its own, in a running Rhino 8.
//
// The analysis components write their deck and recorder into AlpacaResults, next to the .gh file.
// The names used to say only which run of the component wrote them, so two Run Analysis components
// in one document wrote the same recorder_0.mpco, and a result component reading the first model
// again - after either solved anew - got the second model's results. grasshopper.sh loads this build
// of the plugin beside the installed one and calls Run by reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Grasshopper.Kernel;
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

    public static string Run(string work, string license)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            if (File.Exists(license))
                Alpaca4d.License.License.Install(license);

            TwoCantilevers();
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

        public Doc(string name)
        {
            Document.FilePath = Path.Combine(folder, name + ".gh");
        }

        public T Add<T>(string unit = null) where T : GH_Component, new()
        {
            var component = new T();
            component.CreateAttributes();
            if (unit != null)
                component.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) }).Invoke(component, new object[] { unit, false, false });
            Document.AddObject(component, false);
            return component;
        }

        public void Solve()
        {
            Document.Enabled = true;
            Document.NewSolution(true, GH_SolutionMode.Silent);
        }

        /// <summary>Solves only what has been expired since, as Grasshopper does after a change.</summary>
        public void Update() => Document.NewSolution(false, GH_SolutionMode.Silent);

        public IEnumerable<string> Errors() =>
            Document.Objects.OfType<IGH_ActiveObject>()
                .SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => $"{o.Name}: {m}"));
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

    /// <summary>
    /// A cantilever 4 m along x from <paramref name="root"/>, fixed there, with <paramref name="load"/>
    /// kN down at its tip, analysed by a Run Analysis of its own and read by a Nodal Displacement.
    /// </summary>
    static (GH_Component run, GH_Component reader) Cantilever(Doc doc, Point3d root, double load)
    {
        var tip = root + new Vector3d(4, 0, 0);
        var material = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var section = doc.Add<Alpaca4d.Gh.RectangleCS>();
        Set(section, "Width", new GH_Number(0.3));
        Set(section, "Height", new GH_Number(0.5));
        Wire(material, "Material", section, "Material");
        var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beam, "Line", new GH_Curve(new LineCurve(root, tip)));
        Wire(section, "Section", beam, "Section");

        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", new GH_Plane(new Plane(root, Vector3d.XAxis, Vector3d.YAxis)));
        foreach (var dof in new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" })
            Set(support, dof, new GH_Boolean(true));

        var force = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(force, "Point", new GH_Point(tip));
        Set(force, "Force", new GH_Vector(new Vector3d(0, 0, -load)));
        var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(series, "TimeSeries", pattern, "TimeSeries");
        In(pattern, "Loads").AddSource(force.Params.Output[0]);

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        In(assemble, "Elements").AddSource(beam.Params.Output[0]);
        In(assemble, "Supports").AddSource(support.Params.Output[0]);
        In(assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);
        var settings = doc.Add<Alpaca4d.Gh.AnalysisSettings>();
        var run = doc.Add<Alpaca4d.Gh.RunAnalysis>();
        Wire(assemble, "AlpacaModel", run, "AlpacaModel");
        Wire(settings, "Settings", run, "Settings");

        var reader = doc.Add<Alpaca4d.Gh.NodalDisplacement>();
        Wire(run, "AlpacaModel", reader, "AlpacaModel");
        return (run, reader);
    }

    static Alpaca4d.Model Model(GH_Component run) =>
        (Alpaca4d.Model)((IGH_Goo)Out(run, "AlpacaModel").VolatileData.AllData(true).First()).ScriptVariable();

    /// <summary>The largest downward displacement the reader read, in its own unit.</summary>
    static double Deflection(GH_Component reader) =>
        Out(reader, "Displacement").VolatileData.AllData(true).OfType<GH_Vector>().Max(v => -v.Value.Z);

    // ---------------------------------------------------------------- the checks

    /// <summary>
    /// Two cantilevers alike but for their load, 10 kN and 30 kN, so the second deflects three times
    /// as far, each with a Run Analysis of its own, in one document.
    /// </summary>
    static void TwoCantilevers()
    {
        Section("Two cantilevers in one document, 10 kN and 30 kN at the tip, each with a Run Analysis of its own");
        var doc = new Doc("two-cantilevers");
        var (runA, readerA) = Cantilever(doc, new Point3d(0, 0, 0), 10);
        var (runB, readerB) = Cantilever(doc, new Point3d(0, 5, 0), 30);
        doc.Solve();
        var errors = doc.Errors().ToList();
        Check(errors.Count == 0, errors.Count == 0 ? "no component has an error" : string.Join(" | ", errors));

        var a = Model(runA);
        var b = Model(runB);
        string recorderA = a.Recorders.Single().FileName, recorderB = b.Recorders.Single().FileName;
        report.AppendLine($"  recorders {Path.GetFileName(recorderA)} and {Path.GetFileName(recorderB)}, decks {Path.GetFileName(a.FileName)} and {Path.GetFileName(b.FileName)}");
        Check(recorderA != recorderB, "the two write different recorder files");
        Check(a.FileName != b.FileName, "and different decks");
        Check(Path.GetFileName(recorderA).Contains(runA.InstanceGuid.ToString("N").Substring(0, 8)) &&
              Path.GetFileName(recorderB).Contains(runB.InstanceGuid.ToString("N").Substring(0, 8)),
              "each name carries the start of its own component's InstanceGuid");
        Check(Path.GetDirectoryName(recorderA) == Path.Combine(folder, "AlpacaResults") && File.Exists(recorderA) && File.Exists(recorderB),
              "both in AlpacaResults beside the .gh file");

        double first = Deflection(readerA), second = Deflection(readerB);
        report.AppendLine($"  tip deflections {first:G4} and {second:G4}");
        Check(first > 0 && Math.Abs(second / first - 3) < 1e-6, "the 30 kN tip deflects three times as far as the 10 kN one");

        // Read again, after both have solved: what a result component reads is whatever file its model
        // points at, so a shared file gave the first the second's results here.
        readerA.ExpireSolution(false);
        doc.Update();
        double again = Deflection(readerA);
        report.AppendLine($"  the 10 kN tip read again: {again:G4}");
        Check(Math.Abs(again - first) < 1e-9 * Math.Max(1, first), "the first model's results read again are still its own");

        // The second analysed anew, then the first read again.
        runB.ExpireSolution(false);
        doc.Update();
        readerA.ExpireSolution(false);
        doc.Update();
        double afterB = Deflection(readerA);
        report.AppendLine($"  and after the 30 kN one is analysed again: {afterB:G4}");
        Check(Math.Abs(afterB - first) < 1e-9 * Math.Max(1, first), "and still its own after the second analysis has run again");
        doc.Document.Enabled = false;
    }
}
