// Natural Vibration and Linear Buckling on analysed models, in a running Rhino 8.
//
// An analysed model's deck ends in "wipe", so both used to refuse one, and the modes or the buckling
// of a Section Optimiser's last design took rebuilding the beams on its Section output and assembling
// them again. Each analysis now keeps the model it was given (Model.BeforeAnalysis), and both solve
// that. grasshopper.sh loads this build of the plugin beside the installed one and calls Run by
// reflection.
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

            AfterRunAnalysis();
            AfterSectionOptimiser();
            OneAfterTheOther();
            NothingToGoBackTo();
            Combinations();
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

        public IEnumerable<string> Messages(GH_RuntimeMessageLevel level) =>
            Document.Objects.OfType<IGH_ActiveObject>()
                .SelectMany(o => o.RuntimeMessages(level).Select(m => $"{o.Name}: {m}"));
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

    static List<double> Numbers(GH_Component c, string output) =>
        Out(c, output).VolatileData.AllData(true).OfType<GH_Number>().Select(n => n.Value).ToList();

    static List<object> Items(GH_Component c, string output) =>
        Out(c, output).VolatileData.AllData(true).Select(goo => goo.ScriptVariable()).ToList();

    static string Show(IEnumerable<double> values) => string.Join(", ", values.Select(v => v.ToString("G6")));

    /// <summary>Two lists alike, item for item, to <paramref name="tolerance"/> of each.</summary>
    static bool Same(IReadOnlyList<double> a, IReadOnlyList<double> b, double tolerance = 1e-9) =>
        a.Count > 0 && a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y) <= tolerance * Math.Max(Math.Abs(x), Math.Abs(y))).All(ok => ok);

    static void NoErrors(Doc doc, string what)
    {
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Count == 0, errors.Count == 0 ? $"{what}: no component has an error" : $"{what}: {string.Join(" | ", errors)}");
    }

    static readonly Point3d Foot = new Point3d(0, 0, 0), Head = new Point3d(0, 0, 4);

    /// <summary>
    /// A column 4 m tall in four beams, fixed at its foot, pushed down 200 kN and sideways 10 kN at its
    /// head: a load that buckles an IPE200. Four beams, as one free node has only three degrees of
    /// freedom with mass, and ARPACK fails when asked for more than about half of them. Its support
    /// and pattern, for Assemble.
    /// </summary>
    sealed class Column
    {
        public GH_Component Support, Pattern;

        public Column(Doc doc)
        {
            Support = doc.Add<Alpaca4d.Gh.Support>();
            Set(Support, "Position", new GH_Plane(new Plane(Foot, Vector3d.XAxis, Vector3d.YAxis)));

            var force = doc.Add<Alpaca4d.Gh.PointLoad>();
            Set(force, "Point", new GH_Point(Head));
            Set(force, "Force", new GH_Vector(new Vector3d(10, 0, -200)));
            var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
            Pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
            Wire(series, "TimeSeries", Pattern, "TimeSeries");
            In(Pattern, "Loads").AddSource(force.Params.Output[0]);
        }

        /// <summary>The column with beams of <paramref name="section"/>'s output - one, or one per beam - assembled.</summary>
        public GH_Component Assemble(Doc doc, GH_Component section, string output = "Section")
        {
            var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
            Set(beam, "Line", Enumerable.Range(0, 4).Select(i => new GH_Curve(new LineCurve(new Point3d(0, 0, i), new Point3d(0, 0, i + 1)))).ToArray());
            Wire(section, output, beam, "Section");

            var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
            In(assemble, "Elements").AddSource(beam.Params.Output[0]);
            In(assemble, "Supports").AddSource(Support.Params.Output[0]);
            In(assemble, "LoadPatterns").AddSource(Pattern.Params.Output[0]);
            return assemble;
        }
    }

    /// <summary>An IPE200 in S235: the steel check reads the strength off the grade, which only Material Library sets.</summary>
    static GH_Component Ipe200(Doc doc)
    {
        var steel = doc.Add<Alpaca4d.Gh.MaterialLibraryElastic>();
        Set(steel, "Name", new GH_String("S235"));
        var section = doc.Add<Alpaca4d.Gh.SectionLibrary>();
        Set(section, "Name", new GH_String("IPE200"));
        Wire(steel, "Material", section, "Material");
        return section;
    }

    static GH_Component RunAnalysis(Doc doc, GH_Component from)
    {
        var settings = doc.Add<Alpaca4d.Gh.AnalysisSettings>();
        var run = doc.Add<Alpaca4d.Gh.RunAnalysis>();
        Wire(from, "AlpacaModel", run, "AlpacaModel");
        Wire(settings, "Settings", run, "Settings");
        return run;
    }

    static GH_Component Vibration(Doc doc, GH_Component from)
    {
        var vibration = doc.Add<Alpaca4d.Gh.NaturalVibration>("NaturalVibration");
        Wire(from, "AlpacaModel", vibration, "AlpacaModel");
        Set(vibration, "Vibration Modes", new GH_Integer(3));
        return vibration;
    }

    static GH_Component Buckling(Doc doc, GH_Component from)
    {
        var buckling = doc.Add<Alpaca4d.Gh.LinearBuckling>();
        Wire(from, "AlpacaModel", buckling, "AlpacaModel");
        Set(buckling, "Buckling Modes", new GH_Integer(2));
        Set(buckling, "Divisions", new GH_Integer(4));
        return buckling;
    }

    static List<string> Said(GH_Component c, GH_RuntimeMessageLevel level) => c.RuntimeMessages(level).ToList();

    /// <summary>
    /// Linear Buckling and Pushover take the loads the user gives: without combinations, every
    /// pattern together, with a remark naming them; with combinations, the one named and no other -
    /// left empty, an error listing them.
    /// </summary>
    static void Combinations()
    {
        Section("The column with a second pattern, W: Linear Buckling and Pushover with and without load combinations");
        var doc = new Doc("combinations");
        var column = new Column(doc);
        Set(column.Pattern, "Name", new GH_String("P"));
        var wind = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(wind, "Point", new GH_Point(Head));
        Set(wind, "Force", new GH_Vector(new Vector3d(5, 0, 0)));
        var windPattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(doc.Add<Alpaca4d.Gh.LinearTimeSeries>(), "TimeSeries", windPattern, "TimeSeries");
        In(windPattern, "Loads").AddSource(wind.Params.Output[0]);
        Set(windPattern, "Name", new GH_String("W"));

        var section = Ipe200(doc);
        var alone = column.Assemble(doc, section);              // P only
        var both = column.Assemble(doc, section);
        In(both, "LoadPatterns").AddSource(windPattern.Params.Output[0]);

        var combined = doc.Add<Alpaca4d.Gh.LoadCombinationComponent>();
        Wire(both, "AlpacaModel", combined, "AlpacaModel");
        Set(combined, "Name", new GH_String("ULS 1"), new GH_String("ULS 2"));
        Set(combined, "Combination", new GH_String("1.5 P + W"), new GH_String("P"));

        var bucklingAlone = Buckling(doc, alone);
        var bucklingBoth = Buckling(doc, both);
        var bucklingEmpty = Buckling(doc, combined);
        var bucklingNamed = Buckling(doc, combined);
        Set(bucklingNamed, "Combination", new GH_String("ULS 2"));
        var bucklingPlace = Buckling(doc, combined);
        Set(bucklingPlace, "Combination", new GH_String("1"));
        var bucklingWrong = Buckling(doc, combined);
        Set(bucklingWrong, "Combination", new GH_String("SLS 1"));

        var pushEmpty = doc.Add<Alpaca4d.Gh.PushoverAnalysis>();
        Wire(combined, "AlpacaModel", pushEmpty, "AlpacaModel");
        Set(pushEmpty, "Control Point", new GH_Point(Head));
        Set(pushEmpty, "Target", new GH_Number(0.01));
        Set(pushEmpty, "Steps", new GH_Integer(5));
        doc.Solve();

        var remarks = Said(bucklingBoth, GH_RuntimeMessageLevel.Remark);
        report.AppendLine($"  without combinations, two patterns: {string.Join(" | ", remarks)}");
        Check(remarks.Any(r => r.Contains("P and W act together")) && Said(bucklingBoth, GH_RuntimeMessageLevel.Error).Count == 0,
              "without combinations both patterns act, and a remark names them");
        Check(!Said(bucklingAlone, GH_RuntimeMessageLevel.Remark).Any(r => r.Contains("act together")), "one pattern alone gets no such remark");

        var empty = Said(bucklingEmpty, GH_RuntimeMessageLevel.Error);
        report.AppendLine($"  with combinations, none named: {string.Join(" | ", empty)}");
        Check(empty.Count == 1 && empty[0].Contains("ULS 1, ULS 2") && Numbers(bucklingEmpty, "Load Factors").Count == 0,
              "with combinations and none named, Linear Buckling stops with an error listing them");

        var b0 = Numbers(bucklingAlone, "Load Factors");
        var b1 = Numbers(bucklingNamed, "Load Factors");
        var b2 = Numbers(bucklingPlace, "Load Factors");
        report.AppendLine($"  load factors {Show(b0)} for P alone, {Show(b1)} for ULS 2 = P, {Show(b2)} for place 1");
        Check(Said(bucklingNamed, GH_RuntimeMessageLevel.Error).Count == 0 && Same(b0, b1, 1e-6), "ULS 2, which is P alone, buckles as P alone does");
        Check(Same(b1, b2, 1e-9), "and its place, 1, picks it too");

        var wrong = Said(bucklingWrong, GH_RuntimeMessageLevel.Error);
        Check(wrong.Count == 1 && wrong[0].Contains("no load combination \"SLS 1\""), "a name the model has not is an error: " + string.Join(" | ", wrong));

        var pushed = Said(pushEmpty, GH_RuntimeMessageLevel.Error);
        report.AppendLine($"  Pushover, none named: {string.Join(" | ", pushed)}");
        Check(pushed.Count == 1 && pushed[0].Contains("name the one to push the model under") && pushed[0].Contains("ULS 1, ULS 2"),
              "Pushover with combinations and none named stops too, with the same list");
    }

    // ---------------------------------------------------------------- the checks

    /// <summary>The modes and the buckling of the column from Run Analysis, against those of the assembled column.</summary>
    static void AfterRunAnalysis()
    {
        Section("A column, IPE200: Natural Vibration and Linear Buckling after Run Analysis, against straight after Assemble Model");
        var doc = new Doc("after-run-analysis");
        var assemble = new Column(doc).Assemble(doc, Ipe200(doc));
        var run = RunAnalysis(doc, assemble);
        var vibrationAssembled = Vibration(doc, assemble);
        var vibrationAnalysed = Vibration(doc, run);
        var bucklingAssembled = Buckling(doc, assemble);
        var bucklingAnalysed = Buckling(doc, run);
        doc.Solve();
        NoErrors(doc, "Run Analysis, then both");

        var f0 = Numbers(vibrationAssembled, "Frequencies");
        var f1 = Numbers(vibrationAnalysed, "Frequencies");
        report.AppendLine($"  frequencies {Show(f0)} Hz from Assemble Model, {Show(f1)} Hz from Run Analysis");
        Check(f0.Count == 3 && Same(f0, f1), "Natural Vibration finds the same three modes after Run Analysis");

        var b0 = Numbers(bucklingAssembled, "Load Factors");
        var b1 = Numbers(bucklingAnalysed, "Load Factors");
        report.AppendLine($"  load factors {Show(b0)} from Assemble Model, {Show(b1)} from Run Analysis");
        Check(b0.Count == 2 && Same(b0, b1), "and Linear Buckling the same two load factors");

        var analysed = (Alpaca4d.Model)Items(run, "AlpacaModel").Single();
        var assembled = (Alpaca4d.Model)Items(assemble, "AlpacaModel").Single();
        Check(ReferenceEquals(analysed.BeforeAnalysis, assembled) && ReferenceEquals(analysed.Unanalysed(), assembled),
              "Run Analysis's model carries the one Assemble Model handed it");
        doc.Document.Enabled = false;
    }

    /// <summary>
    /// The modes and the buckling of Section Optimiser's last design, against the workaround: a beam on
    /// its Section output, assembled again.
    /// </summary>
    static void AfterSectionOptimiser()
    {
        Section("The column sized by Section Optimiser within the IPEs: its last design, against its sections assembled again");
        var doc = new Doc("after-section-optimiser");
        var column = new Column(doc);
        var assemble = column.Assemble(doc, Ipe200(doc));
        var optimiser = doc.Add<Alpaca4d.Gh.SectionOptimiser>("SectionOptimiser");
        Wire(assemble, "AlpacaModel", optimiser, "AlpacaModel");
        // A cantilever column buckles over twice its height.
        Set(optimiser, "LengthY", new GH_Number(8));
        Set(optimiser, "LengthZ", new GH_Number(8));
        var rebuilt = column.Assemble(doc, optimiser);

        var vibrationStart = Vibration(doc, assemble);
        var vibrationOptimised = Vibration(doc, optimiser);
        var vibrationRebuilt = Vibration(doc, rebuilt);
        var bucklingOptimised = Buckling(doc, optimiser);
        var bucklingRebuilt = Buckling(doc, rebuilt);
        doc.Solve();
        NoErrors(doc, "Section Optimiser, then both");

        var names = Items(optimiser, "Section").OfType<Alpaca4d.Generic.IUniaxialSection>().Select(x => x.SectionName ?? x.ToString()).ToList();
        report.AppendLine($"  Section Optimiser puts the column, foot to head, on {string.Join(", ", names)}");
        Check(names.Count == 4 && names.All(n => n != "IPE200"), "it leaves the IPE200, which fails");

        var start = Numbers(vibrationStart, "Frequencies");
        var optimised = Numbers(vibrationOptimised, "Frequencies");
        var again = Numbers(vibrationRebuilt, "Frequencies");
        report.AppendLine($"  frequencies {Show(start)} Hz on the IPE200, {Show(optimised)} Hz from Section Optimiser, {Show(again)} Hz assembled again");
        Check(optimised.Count == 3 && Same(optimised, again), "Natural Vibration on Section Optimiser's model finds the modes of its sections assembled again");
        Check(start.Count == 3 && Math.Abs(start[0] - optimised[0]) > 1e-3 * start[0], "and not those of the IPE200 it started from");

        var b1 = Numbers(bucklingOptimised, "Load Factors");
        var b2 = Numbers(bucklingRebuilt, "Load Factors");
        report.AppendLine($"  load factors {Show(b1)} from Section Optimiser, {Show(b2)} assembled again");
        Check(b1.Count == 2 && Same(b1, b2), "and Linear Buckling the same load factors as its sections assembled again");
        doc.Document.Enabled = false;
    }

    /// <summary>Natural Vibration after Linear Buckling - with split beams - and Linear Buckling after Natural Vibration.</summary>
    static void OneAfterTheOther()
    {
        Section("The column through Linear Buckling, its beams split in 4, then Natural Vibration, and the other way round");
        var doc = new Doc("one-after-the-other");
        var assemble = new Column(doc).Assemble(doc, Ipe200(doc));
        var vibration = Vibration(doc, assemble);
        var buckling = Buckling(doc, assemble);
        var vibrationAfterBuckling = Vibration(doc, buckling);
        var bucklingAfterVibration = Buckling(doc, vibration);
        doc.Solve();
        NoErrors(doc, "one after the other");

        var buckled = (Alpaca4d.Model)Items(buckling, "AlpacaModel").Single();
        var assembled = (Alpaca4d.Model)Items(assemble, "AlpacaModel").Single();
        Check(buckled.Beams.Count == 16 && assembled.Beams.Count == 4, $"Linear Buckling hands on the four beams in {buckled.Beams.Count} pieces");

        var f0 = Numbers(vibration, "Frequencies");
        var f1 = Numbers(vibrationAfterBuckling, "Frequencies");
        report.AppendLine($"  frequencies {Show(f0)} Hz, {Show(f1)} Hz after Linear Buckling");
        Check(Same(f0, f1), "Natural Vibration after Linear Buckling solves the model as assembled, its beams whole");

        var b0 = Numbers(buckling, "Load Factors");
        var b1 = Numbers(bucklingAfterVibration, "Load Factors");
        report.AppendLine($"  load factors {Show(b0)}, {Show(b1)} after Natural Vibration");
        Check(Same(b0, b1), "and Linear Buckling after Natural Vibration the same load factors");
        doc.Document.Enabled = false;
    }

    /// <summary>A model analysed by something that kept nothing: both still refuse it, and say so.</summary>
    static void NothingToGoBackTo()
    {
        Section("A model marked analysed, with no model it was analysed from");
        var lost = new Alpaca4d.Model { IsAnalysed = true };
        Check(lost.Unanalysed() == null, "Unanalysed has nothing to give");
        var fresh = new Alpaca4d.Model();
        Check(ReferenceEquals(fresh.Unanalysed(), fresh), "and gives a model not analysed back as it is");

        var doc = new Doc("nothing-to-go-back-to");
        var vibration = doc.Add<Alpaca4d.Gh.NaturalVibration>("NaturalVibration");
        Set<IGH_Goo>(vibration, "AlpacaModel", new GH_ObjectWrapper(lost));
        var buckling = doc.Add<Alpaca4d.Gh.LinearBuckling>();
        Set<IGH_Goo>(buckling, "AlpacaModel", new GH_ObjectWrapper(lost));
        doc.Solve();
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Count == 2 && errors.All(e => e.Contains("does not carry the model it was analysed from")),
              $"Natural Vibration and Linear Buckling both say why ({string.Join(" | ", errors)})");
        doc.Document.Enabled = false;
    }
}
