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
            Superposition();
            SavedDescriptions();
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

    /// <summary>
    /// Each load combination is solved in an OpenSees run of its own, so on a linear model their
    /// results have to add up: DL + LL gives what DL and LL give, added; 1.35 DL + 1.5 LL and
    /// DL + -1 LL the same with their factors; and DL + LL what the model without combinations
    /// gives, both patterns on. A table: four columns 3 m tall and two beams 4 m long, IPE200, fixed
    /// at the feet, under a slab of eight ASD shells, 200 mm. DL is self-weight, 1 kN/m on every beam
    /// and 2 kN/m² on every shell; LL a point load with a moment, a push sideways and 3 kN/m² on
    /// every shell. Compared: every node's displacement, rotation and reaction, the beams' forces at
    /// every station and the shells' forces; and each combination's reactions against its loads.
    /// </summary>
    static void Superposition()
    {
        Section("A table of beams under a slab of shells, DL and LL: the combinations' results add up");
        var doc = new Doc("superposition");

        var lines = new List<GH_Curve>();
        foreach (var (x, y) in new[] { (0.0, 0.0), (4.0, 0.0), (0.0, 2.0), (4.0, 2.0) })
            for (int i = 0; i < 3; i++)
                lines.Add(new GH_Curve(new LineCurve(new Point3d(x, y, i), new Point3d(x, y, i + 1))));
        foreach (var y in new[] { 0.0, 2.0 })
            for (int i = 0; i < 4; i++)
                lines.Add(new GH_Curve(new LineCurve(new Point3d(i, y, 3), new Point3d(i + 1, y, 3))));
        var beams = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beams, "Line", lines.ToArray());
        Wire(Ipe200(doc), "Section", beams, "Section");

        var mesh = new Mesh();
        for (int j = 0; j <= 2; j++)
            for (int i = 0; i <= 4; i++)
                mesh.Vertices.Add(new Point3d(i, j, 3));
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 4; i++)
                mesh.Faces.AddFace(j * 5 + i, j * 5 + i + 1, j * 5 + i + 6, j * 5 + i + 5);
        var concrete = doc.Add<SimplexGh.nD>("ElasticIsotropic (Alpaca4d)");
        Set(concrete, "E", new GH_Number(30e6));
        Set(concrete, "G", new GH_Number(12.5e6));
        Set(concrete, "ν", new GH_Number(0.2));
        Set(concrete, "Rho", new GH_Number(2500));
        var plate = doc.Add<Alpaca4d.Gh.PlateFiberSection>();
        Set(plate, "Thickness", new GH_Number(0.2));
        Wire(concrete, "Material", plate, "Material");
        var slab = doc.Add<Alpaca4d.Gh.ASDShell>();
        Set(slab, "Mesh", new GH_Mesh(mesh));
        Wire(plate, "Section", slab, "Section");

        var feet = doc.Add<Alpaca4d.Gh.Support>();
        Set(feet, "Position", new[] { (0.0, 0.0), (4.0, 0.0), (0.0, 2.0), (4.0, 2.0) }
            .Select(p => new GH_Plane(new Plane(new Point3d(p.Item1, p.Item2, 0), Vector3d.XAxis, Vector3d.YAxis))).ToArray());

        GH_Component Pattern(string name, params GH_Component[] loads)
        {
            var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
            Wire(doc.Add<Alpaca4d.Gh.LinearTimeSeries>(), "TimeSeries", pattern, "TimeSeries");
            foreach (var load in loads)
                In(pattern, "Loads").AddSource(load.Params.Output[0]);
            Set(pattern, "Name", new GH_String(name));
            return pattern;
        }
        var self = doc.Add<Alpaca4d.Gh.GravityLoad>();
        var onBeams = doc.Add<Alpaca4d.Gh.LinearLoad>();
        Set(onBeams, "Force", new GH_Vector(new Vector3d(0, 0, -1)));
        var finishes = doc.Add<Alpaca4d.Gh.MeshLoad>();
        Set(finishes, "Force", new GH_Vector(new Vector3d(0, 0, -2)));
        var dl = Pattern("DL", self, onBeams, finishes);

        var point = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(point, "Point", new GH_Point(new Point3d(2, 0, 3)));
        Set(point, "Force", new GH_Vector(new Vector3d(5, 3, -20)));
        Set(point, "Moment", new GH_Vector(new Vector3d(0, 0, 2)));
        var push = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(push, "Point", new GH_Point(new Point3d(4, 2, 3)));
        Set(push, "Force", new GH_Vector(new Vector3d(10, 0, 0)));
        var people = doc.Add<Alpaca4d.Gh.MeshLoad>();
        Set(people, "Force", new GH_Vector(new Vector3d(0, 0, -3)));
        var ll = Pattern("LL", point, push, people);

        GH_Component Assembled()
        {
            var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
            In(assemble, "Elements").AddSource(beams.Params.Output[0]);
            In(assemble, "Elements").AddSource(slab.Params.Output[0]);
            In(assemble, "Supports").AddSource(feet.Params.Output[0]);
            In(assemble, "LoadPatterns").AddSource(dl.Params.Output[0]);
            In(assemble, "LoadPatterns").AddSource(ll.Params.Output[0]);
            return assemble;
        }
        var plain = RunAnalysis(doc, Assembled());
        var combined = doc.Add<Alpaca4d.Gh.LoadCombinationComponent>();
        Wire(Assembled(), "AlpacaModel", combined, "AlpacaModel");
        string[] names = { "DL", "LL", "DL + LL", "ULS", "Opposite" };
        Set(combined, "Name", names.Select(n => new GH_String(n)).ToArray());
        Set(combined, "Combination", new[] { "DL", "LL", "DL + LL", "1.35 DL + 1.5*LL", "DL + -1 LL" }.Select(n => new GH_String(n)).ToArray());
        var run = RunAnalysis(doc, combined);

        // The result components, each with Combination left empty - every combination - and the
        // others as each check below sets them.
        T Reader<T>(GH_Component from, string unit = null) where T : GH_Component, new()
        {
            var reader = doc.Add<T>(unit);
            Wire(from, "AlpacaModel", reader, "AlpacaModel");
            return reader;
        }
        var displacement = Reader<Alpaca4d.Gh.NodalDisplacement>(run);
        var picked = Reader<Alpaca4d.Gh.NodalDisplacement>(run);
        Set(picked, "Combination", new GH_String("ll"), new GH_String("4"));
        var unknown = Reader<Alpaca4d.Gh.NodalDisplacement>(run);
        Set(unknown, "Combination", new GH_String("SLS 9"));
        var plainDisplacement = Reader<Alpaca4d.Gh.NodalDisplacement>(plain);
        var plainNamed = Reader<Alpaca4d.Gh.NodalDisplacement>(plain);
        Set(plainNamed, "Combination", new GH_String("DL"));
        var beamForces = Reader<Alpaca4d.Gh.BeamForce>(run);
        var beamHistory = Reader<Alpaca4d.Gh.BeamForce>(run);
        Set(beamHistory, "History", new GH_Boolean(true));
        var reactions = Reader<Alpaca4d.Gh.ReactionForce>(run);
        var shellForces = Reader<Alpaca4d.Gh.ShellForces>(run);
        var nodeForces = Reader<Alpaca4d.Gh.NodeForce>(run);
        var shellStresses = Reader<Alpaca4d.Gh.ShellStresses>(run);
        var beamStresses = Reader<Alpaca4d.Gh.BeamStresses>(run);
        var utilisation = Reader<Alpaca4d.Gh.Utilisation>(run, "Utilisation");
        doc.Solve();
        var otherErrors = doc.Document.Objects.OfType<GH_Component>().Where(c => c != unknown && c != plainNamed)
            .SelectMany(c => Said(c, GH_RuntimeMessageLevel.Error).Select(m => $"{c.Name}: {m}")).ToList();
        Check(otherErrors.Count == 0, otherErrors.Count == 0 ? "the table, with and without combinations: no component has an error" : string.Join(" | ", otherErrors));

        var handed = (Alpaca4d.Model)Items(run, "AlpacaModel").Single();
        var results = new Dictionary<string, Dictionary<string, List<double>>>();
        foreach (var name in names)
        {
            var one = handed.OfCombination(name, out string problem);
            Check(one != null && one.Combination?.Name == name, $"{name}: picked by name{(problem == null ? "" : ", " + problem)}");
            if (one == null) return;
            results[name] = Everything(one);
        }
        results["no combinations"] = Everything((Alpaca4d.Model)Items(plain, "AlpacaModel").Single());
        report.AppendLine($"  compared: {string.Join(", ", results["DL"].Select(r => $"{r.Key} ({r.Value.Count})"))}");

        void Adds(string what, string name, double a, string x, double b, string y)
        {
            var worst = results[name].Keys
                .Select(key => (key, misfit: Misfit(results[name][key], a, results[x][key], b, results[y][key])))
                .OrderByDescending(m => m.misfit).First();
            report.AppendLine($"  {what}: worst misfit {worst.misfit:E2} of the largest value, in {worst.key}");
            Check(worst.misfit < 1e-6, $"{name} = {what}, every result");
        }
        Adds("DL + LL", "DL + LL", 1, "DL", 1, "LL");
        Adds("1.35 DL + 1.5 LL", "ULS", 1.35, "DL", 1.5, "LL");
        Adds("DL - LL", "Opposite", 1, "DL", -1, "LL");
        Adds("DL + LL", "no combinations", 1, "DL + LL", 0, "LL");
        var apart = Misfit(results["DL"]["displacement"], 1, results["DL + LL"]["displacement"], 0, results["LL"]["displacement"]);
        Check(apart > 1e-2, $"and DL is not DL + LL: each combination reads a file of its own ({apart:E2} apart)");

        // Each combination's reactions against its loads: what the deck has on it, none dropped, none twice.
        var model = (Alpaca4d.Model)Items(plain, "AlpacaModel").Single();
        var ipe = model.Beams.First().Section;
        double weight = 20 * ipe.Area * ipe.Material.Rho.Value * 9.81 / 1000 + 8 * 0.2 * 2500 * 9.81 / 1000;
        var dead = new Vector3d(0, 0, -(weight + 20 * 1 + 8 * 2));
        var live = new Vector3d(5 + 10, 3, -20 - 8 * 3);
        foreach (var (name, a, b) in new[] { ("DL", 1.0, 0.0), ("LL", 0.0, 1.0), ("DL + LL", 1.0, 1.0), ("ULS", 1.35, 1.5), ("Opposite", 1.0, -1.0) })
        {
            var r = results[name]["reaction force"];
            var sum = new Vector3d(Enumerable.Range(0, r.Count / 3).Sum(i => r[3 * i]), Enumerable.Range(0, r.Count / 3).Sum(i => r[3 * i + 1]), Enumerable.Range(0, r.Count / 3).Sum(i => r[3 * i + 2]));
            var loads = a * dead + b * live;
            report.AppendLine($"  {name}: reactions {sum.X:F4}, {sum.Y:F4}, {sum.Z:F4} kN against loads {loads.X:F4}, {loads.Y:F4}, {loads.Z:F4}");
            Check((sum + loads).Length < 1e-6 * loads.Length, $"{name}: the reactions balance its loads");
        }

        ResultComponents(results, names, model, displacement, picked, unknown, plainDisplacement, plainNamed, beamForces, beamHistory,
                         reactions, shellForces, nodeForces, shellStresses, beamStresses, utilisation);
        doc.Document.Enabled = false;
    }

    /// <summary>
    /// Grasshopper saves each parameter's description in the file and reads it back, so a file from
    /// an older Alpaca4d shows its old tooltips. Opened, or pasted in, a component's parameters take
    /// the descriptions of this version - a switcher's those of the type it is set to - and another
    /// plugin's components are left alone.
    /// </summary>
    static void SavedDescriptions()
    {
        Section("Descriptions saved in a file by an older Alpaca4d: this version's on opening and pasting");
        const string Old = "an older version's description, as a file holds it";
        IGH_Param Named(GH_Component c, string name) => c.Params.Input.First(p => p.Name == name);
        T Placed<T>(string unit = null) where T : GH_Component, new()
        {
            var c = new T();
            c.CreateAttributes();
            if (unit != null)
                c.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) }).Invoke(c, new object[] { unit, false, false });
            return c;
        }

        string expectedName = Named(Placed<Alpaca4d.Gh.LoadCombinationComponent>(), "Name").Description;
        string expectedWorksIn = Named(Placed<Alpaca4d.Gh.BeamBase>("Truss (Alpaca4d)"), "WorksIn").Description;
        var combination = Placed<Alpaca4d.Gh.LoadCombinationComponent>();
        Named(combination, "Name").Description = Old;
        var truss = Placed<Alpaca4d.Gh.BeamBase>("Truss (Alpaca4d)");
        Named(truss, "WorksIn").Description = Old;
        var document = new GH_Document();
        document.AddObject(combination, false);
        document.AddObject(truss, false);

        var refresh = typeof(Alpaca4d.Gh.RunAnalysis).Assembly.GetType("Alpaca4d.Gh.ParameterDescriptions");
        refresh.GetMethod("Watch", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
        Grasshopper.Instances.DocumentServer.AddDocument(document);
        try
        {
            Check(Named(combination, "Name").Description == expectedName, "opened: Load Combination's Name shows this version's description");
            Check(Named(truss, "WorksIn").Description == expectedWorksIn && expectedWorksIn != Old, "and a Truss beam's WorksIn that of the Truss type");
            var pasted = Placed<Alpaca4d.Gh.LoadCombinationComponent>();
            Named(pasted, "Name").Description = Old;
            document.AddObject(pasted, false);
            Check(Named(pasted, "Name").Description == expectedName, "pasted into an open document: the same");
        }
        finally
        {
            Grasshopper.Instances.DocumentServer.RemoveDocument(document);
        }
    }

    /// <summary>An output's branches: each path, and its items as plain values.</summary>
    static List<(GH_Path Path, List<object> Items)> Branches(GH_Component c, string output)
    {
        var data = Out(c, output).VolatileData;
        return Enumerable.Range(0, data.PathCount)
            .Select(i => (data.Paths[i], data.get_Branch(i).Cast<IGH_Goo>().Select(goo => goo?.ScriptVariable()).ToList()))
            .ToList();
    }

    /// <summary>The numbers of the branches whose path starts with <paramref name="place"/>, in path order: vectors as x, y, z.</summary>
    static List<double> Of(List<(GH_Path Path, List<object> Items)> branches, int place) =>
        branches.Where(b => b.Path.Indices[0] == place)
                .SelectMany(b => b.Items)
                .SelectMany(item => item is Vector3d v ? new[] { v.X, v.Y, v.Z } : new[] { Convert.ToDouble(item) })
                .ToList();

    static string Paths(List<(GH_Path Path, List<object> Items)> branches, int show = 6) =>
        string.Join(" ", branches.Take(show).Select(b => b.Path.ToString())) + (branches.Count > show ? $" ... ({branches.Count})" : "");

    /// <summary>
    /// The result components on a model holding load combinations: a branch per combination, its
    /// place in the model first, holding what Core reads for it; the outputs that do not change
    /// with the combination one flat list; a combination picked by name or place; and a model
    /// without combinations read as before.
    /// </summary>
    static void ResultComponents(Dictionary<string, Dictionary<string, List<double>>> results, string[] names, Alpaca4d.Model model,
        GH_Component displacement, GH_Component picked, GH_Component unknown, GH_Component plainDisplacement, GH_Component plainNamed,
        GH_Component beamForces, GH_Component beamHistory, GH_Component reactions, GH_Component shellForces, GH_Component nodeForces,
        GH_Component shellStresses, GH_Component beamStresses, GH_Component utilisation)
    {
        Section("The result components on the table's combinations: a branch per combination, its place first");
        bool Matches(List<double> shown, List<double> read) => Misfit(shown, 1, read, 0, read) < 1e-9;
        var places = Enumerable.Range(0, names.Length).ToList();

        var disp = Branches(displacement, "Displacement");
        report.AppendLine($"  Nodal Displacement, Combination empty: Displacement {Paths(disp)}");
        Check(disp.Select(b => b.Path.ToString()).SequenceEqual(places.Select(k => $"{{{k}}}")),
              "Nodal Displacement: a branch {combination} for each of the five");
        Check(places.All(k => Matches(Of(disp, k), results[names[k]]["displacement"])), "each holding that combination's displacements");
        var position = Branches(displacement, "Position");
        Check(position.Count == 1 && position[0].Items.Count == results["DL"]["displacement"].Count / 3,
              $"Position stays one flat list ({position.Count} branch, {position.FirstOrDefault().Items?.Count} points)");
        var remark = Said(displacement, GH_RuntimeMessageLevel.Remark);
        report.AppendLine($"  its remark: {string.Join(" | ", remark)}");
        Check(remark.Any(r => r.Contains("{0} DL, {1} LL, {2} DL + LL, {3} ULS, {4} Opposite")), "and a remark names each branch");

        var two = Branches(picked, "Displacement");
        Check(two.Select(b => b.Path.ToString()).SequenceEqual(new[] { "{1}", "{4}" }) && Matches(Of(two, 1), results["LL"]["displacement"])
              && Matches(Of(two, 4), results["Opposite"]["displacement"]),
              $"\"ll\" and \"4\" pick LL and Opposite, on their own places: {Paths(two)}");
        var wrong = Said(unknown, GH_RuntimeMessageLevel.Error);
        Check(wrong.Count == 1 && wrong[0].Contains("SLS 9") && wrong[0].Contains("DL, LL, DL + LL, ULS, Opposite"),
              "a name the model has not is an error listing the names: " + string.Join(" | ", wrong));

        var plainBranches = Branches(plainDisplacement, "Displacement");
        report.AppendLine($"  without combinations: Displacement {Paths(plainBranches)}, remarks: {string.Join(" | ", Said(plainDisplacement, GH_RuntimeMessageLevel.Remark))}, " +
                          $"misfit {Misfit(Of(plainBranches, 0), 1, results["no combinations"]["displacement"], 0, results["no combinations"]["displacement"]):E2}");
        // Its one list at the path Grasshopper gives it, from the input's: straight through, as ever.
        Check(plainBranches.Count == 1 && Matches(Of(plainBranches, plainBranches[0].Path.Indices[0]), results["no combinations"]["displacement"])
              && Said(plainDisplacement, GH_RuntimeMessageLevel.Remark).All(r => !r.Contains("load combination")),
              "a model without combinations reads as before: one list, no remark");
        var notThere = Said(plainNamed, GH_RuntimeMessageLevel.Error);
        Check(notThere.Count == 1 && notThere[0].Contains("no load combinations"), "and a combination named on it is an error: " + string.Join(" | ", notThere));

        var beamN = Branches(beamForces, "N");
        int beams = model.Beams.Count;
        report.AppendLine($"  Beam Forces N: {Paths(beamN)}");
        Check(beamN.Count == 5 * beams && beamN.All(b => b.Path.Length == 2) && places.All(k => Matches(Of(beamN, k), results[names[k]]["beam N"])),
              $"Beam Forces: {{combination; tag}}, {beams} beams a combination, each its own forces");
        Check(Branches(beamForces, "Element").Count == 1, "its Element stays one flat list");
        var history = Branches(beamHistory, "Mz");
        Check(history.Count == 5 * beams && history.All(b => b.Path.Length == 3 && b.Path.Indices[1] == 0)
              && places.All(k => Matches(Of(history, k), results[names[k]]["beam Mz"])),
              $"with History on, {{combination; step; tag}}: {Paths(history, 3)}");

        var force = Branches(reactions, "ReactionForce");
        Check(force.Select(b => b.Path.ToString()).SequenceEqual(places.Select(k => $"{{{k}}}")) && force.All(b => b.Items.Count == 4)
              && Branches(reactions, "SupportPosition").Count == 1 && Branches(reactions, "NodeTag").Count == 1,
              "Reaction Force: {combination} with the four supports' reactions; SupportPosition and NodeTag flat");
        var dlZ = force[0].Items.Cast<Vector3d>().Sum(v => v.Z);
        var ulsZ = force[3].Items.Cast<Vector3d>().Sum(v => v.Z);
        Check(Math.Abs(dlZ - results["DL"]["reaction force"].Where((_, i) => i % 3 == 2).Sum()) < 1e-9
              && Math.Abs(ulsZ - results["ULS"]["reaction force"].Where((_, i) => i % 3 == 2).Sum()) < 1e-9,
              $"  and their sums are DL's {dlZ:F4} kN and ULS's {ulsZ:F4} kN");

        var fxx = Branches(shellForces, "pxx");
        Check(fxx.Count == 5 * model.Shells.Count && places.All(k => Matches(Of(fxx, k), results[names[k]]["shell Fxx"]))
              && Branches(shellForces, "Element").Count == 1,
              $"Shell Forces: {{combination; tag}}, each its own forces ({Paths(fxx, 2)}); Element flat");

        var nodeN = Branches(nodeForces, "N");
        var nodePosition = Branches(nodeForces, "Position");
        Check(nodeN.Count > 0 && nodeN.Count % 5 == 0 && nodeN.All(b => b.Path.Length == 2)
              && nodePosition.Select(b => b.Path.ToString()).SequenceEqual(nodeN.Select(b => b.Path.ToString())),
              $"Node Forces: {{combination; node}} on every output, Position too, so they still pair branch for branch ({Paths(nodeN, 2)})");

        var sigma = Branches(shellStresses, "VonMises");
        Check(sigma.Count == 5 * model.Shells.Count * 3 && sigma.All(b => b.Path.Length == 3) && Branches(shellStresses, "Layer").Count == 1,
              $"Shell Stresses: {{combination; element; layer}} ({Paths(sigma, 2)}); Layer flat");
        var sigmaN = Branches(beamStresses, "SigmaN");
        Check(sigmaN.Count == 5 * beams && sigmaN.All(b => b.Path.Length == 2), $"Beam Stresses: {{combination; tag}} ({Paths(sigmaN, 2)})");

        var max = Branches(utilisation, "Max");
        report.AppendLine($"  Utilisation Max: {string.Join("  ", max.Select(b => $"{b.Path} {b.Items.Where(x => x != null).Select(Convert.ToDouble).DefaultIfEmpty(0).Max():F3}"))}");
        Check(max.Select(b => b.Path.ToString()).SequenceEqual(places.Select(k => $"{{{k}}}")) && max.All(b => b.Items.Count == beams)
              && Branches(utilisation, "Element").Count == 1 && Branches(utilisation, "Type").Count == 1,
              "Utilisation: {combination} with a value per beam; Element and Type flat");
    }

    /// <summary>Every result of a static run, flattened, by name: nodal vectors as x, y, z; beam and shell forces station by station.</summary>
    static Dictionary<string, List<double>> Everything(Alpaca4d.Model m)
    {
        var all = new Dictionary<string, List<double>>();
        foreach (var (type, name) in new[]
        {
            (Alpaca4d.Result.ResultType.DISPLACEMENT, "displacement"), (Alpaca4d.Result.ResultType.ROTATION, "rotation"),
            (Alpaca4d.Result.ResultType.REACTION_FORCE, "reaction force"), (Alpaca4d.Result.ResultType.REACTION_MOMENT, "reaction moment"),
        })
            if (Alpaca4d.Result.Read.Holds(m, type))
                all[name] = Alpaca4d.Result.Read.NodalOutput(m, 0, type).SelectMany(v => new[] { v.X, v.Y, v.Z }).ToList();

        var (n, mz, vy, my, vz, t) = Alpaca4d.Result.Read.ForceBeamColumn(m, 0);
        foreach (var (name, values) in new[] { ("beam N", n), ("beam Mz", mz), ("beam Vy", vy), ("beam My", my), ("beam Vz", vz), ("beam T", t) })
            all[name] = values.SelectMany(v => v).ToList();

        var s = Alpaca4d.Result.Read.ASDQ4Forces(m, 0);
        foreach (var (name, values) in new[] { ("shell Fxx", s.Item1), ("shell Fyy", s.Item2), ("shell Fxy", s.Item3), ("shell Mxx", s.Item4),
                                               ("shell Myy", s.Item5), ("shell Mxy", s.Item6), ("shell Vxz", s.Item7), ("shell Vyz", s.Item8) })
            all[name] = values.SelectMany(v => v).ToList();
        return all;
    }

    /// <summary>
    /// How far <paramref name="c"/> is from a·x + b·y, at its worst, over the largest value of the
    /// three: 0 for results that add up exactly. Infinite when they do not have the same length.
    /// </summary>
    static double Misfit(IReadOnlyList<double> c, double a, IReadOnlyList<double> x, double b, IReadOnlyList<double> y)
    {
        if (c.Count == 0 || c.Count != x.Count || c.Count != y.Count) return double.PositiveInfinity;
        double scale = c.Concat(x).Concat(y).Max(v => Math.Abs(v));
        if (scale == 0) return 0;
        return Enumerable.Range(0, c.Count).Max(i => Math.Abs(c[i] - a * x[i] - b * y[i])) / scale;
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
