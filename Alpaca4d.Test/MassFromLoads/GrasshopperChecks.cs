// Mass from Loads through the Grasshopper components themselves, in a running Rhino 8.
//
// A steel beam under a linear load and a concrete slab under two mesh loads, each built in code out of
// the real components - Uniaxial or nD material, Rectangle CS or Plate Fiber Section, ForceBeamColumn
// or ASD Shell, Support, Linear or Mesh Load, Load Pattern, Mass from Loads, Assemble Model, Natural
// Vibration and Footfall Analysis - once with Mass from Loads and once without. Assemble Model's Mass,
// the deck's "mass" lines and the frequencies are checked against closed form, and Mass from Loads'
// messages against the loads that weigh nothing. grasshopper.sh loads this build of the plugin beside
// the installed one and calls Run by reflection.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

public static class GrasshopperCheck
{
    static StringBuilder report;
    static int fails;
    static string folder;

    const double G = 9.81;

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

    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol * Math.Max(1e-12, Math.Abs(b));

    public static string Run(string work, string license)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            if (File.Exists(license))
                Alpaca4d.License.License.Install(license);

            // Each section leaves a line in progress.txt as it starts, so a run that stalls says where.
            foreach (var (name, section) in new (string, Action)[] {
                ("beam", BeamUnderLineLoad), ("slab", SlabUnderMeshLoads), ("messages", LoadsThatWeighNothing),
                ("by name", ByPatternName) })
            {
                File.AppendAllText(Path.Combine(folder, "progress.txt"), name + "\n");
                section();
            }
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
            // Natural Vibration writes its deck and results beside the document.
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

    static List<double> Numbers(GH_Component c, string output) =>
        Out(c, output).VolatileData.AllData(true).OfType<GH_Number>().Select(n => n.Value).ToList();

    static Alpaca4d.Model ModelOf(GH_Component c) =>
        (Alpaca4d.Model)Out(c, "AlpacaModel").VolatileData.AllData(true).Single().ScriptVariable();

    static string Show(IEnumerable<double> values) => string.Join(", ", values.Select(v => v.ToString("G6")));

    static void NoErrors(Doc doc, string what)
    {
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).Concat(doc.Messages(GH_RuntimeMessageLevel.Warning)).ToList();
        Check(errors.Count == 0, errors.Count == 0 ? $"{what}: no component has an error or a warning" : $"{what}: {string.Join(" | ", errors)}");
    }

    /// <summary>The deck's "mass" lines: node tag and the mass along X, in the deck's unit, the tonne.</summary>
    static List<(int node, double mass)> MassLines(Alpaca4d.Model model) =>
        model.Tcl.Where(line => line.StartsWith("mass "))
            .Select(line => line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(t => (int.Parse(t[1], CultureInfo.InvariantCulture), double.Parse(t[2], CultureInfo.InvariantCulture)))
            .ToList();

    static GH_Component Vibration(Doc doc, GH_Component assemble, int modes)
    {
        var vibration = doc.Add<Alpaca4d.Gh.NaturalVibration>("NaturalVibration");
        Wire(assemble, "AlpacaModel", vibration, "AlpacaModel");
        Set(vibration, "Vibration Modes", new GH_Integer(modes));
        return vibration;
    }

    static GH_Component Support(Doc doc, IEnumerable<Point3d> at, bool rotationsHeld)
    {
        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", at.Select(p => new GH_Plane(new Plane(p, Vector3d.XAxis, Vector3d.YAxis))).ToArray());
        var names = new[] { "Rx", "Ry", "Rz" };
        foreach (var name in names)
            Set(support, name, new GH_Boolean(rotationsHeld));
        return support;
    }

    // ---------------------------------------------------------------- the beam

    const double BeamLength = 8.0, BeamSide = 0.3, BeamLoad = 5.0; // m, m, kN/m
    const int BeamPieces = 16;

    /// <summary>
    /// A steel beam 8 m long, 300 x 300 mm, in 16 elements, held at both ends along X, Y and Z and about
    /// its own axis, under 5 kN/m down; with <paramref name="withMass"/> that load is also mass, in full.
    /// </summary>
    static (GH_Component assemble, GH_Component vibration) Beam(Doc doc, bool withMass)
    {
        var steel = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var section = doc.Add<Alpaca4d.Gh.RectangleCS>();
        Set(section, "Width", new GH_Number(BeamSide));
        Set(section, "Height", new GH_Number(BeamSide));
        Wire(steel, "Material", section, "Material");

        var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beam, "Line", Enumerable.Range(0, BeamPieces)
            .Select(i => new GH_Curve(new LineCurve(new Point3d(BeamLength * i / BeamPieces, 0, 0), new Point3d(BeamLength * (i + 1) / BeamPieces, 0, 0))))
            .ToArray());
        Wire(section, "Section", beam, "Section");

        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", new GH_Plane(Plane.WorldXY), new GH_Plane(new Plane(new Point3d(BeamLength, 0, 0), Vector3d.XAxis, Vector3d.YAxis)));
        Set(support, "Ry", new GH_Boolean(false));
        Set(support, "Rz", new GH_Boolean(false));

        var load = doc.Add<Alpaca4d.Gh.LinearLoad>();
        Wire(beam, "Element", load, "Beam|Id");
        Set(load, "Force", new GH_Vector(new Vector3d(0, 0, -BeamLoad)));

        var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(series, "TimeSeries", pattern, "TimeSeries");
        In(pattern, "Loads").AddSource(load.Params.Output[0]);

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        In(assemble, "Elements").AddSource(beam.Params.Output[0]);
        In(assemble, "Supports").AddSource(support.Params.Output[0]);
        In(assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);
        if (withMass)
        {
            var mass = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
            In(mass, "Load Patterns").AddSource(load.Params.Output[0]);
            In(assemble, "LoadPatterns").AddSource(Out(mass, "Mass"));
        }
        return (assemble, Vibration(doc, assemble, 3));
    }

    static void BeamUnderLineLoad()
    {
        Section("A steel beam, 8 m, 300 x 300, simply supported, 5 kN/m: Mass from Loads at Factor 1");
        var doc = new Doc("beam");
        var bare = Beam(doc, withMass: false);
        var loaded = Beam(doc, withMass: true);
        doc.Solve();
        NoErrors(doc, "both beams");

        double ownPerMetre = 7850.0 * BeamSide * BeamSide;   // kg/m
        double addedPerMetre = BeamLoad / G * 1000.0;         // kg/m
        double bareMass = Numbers(bare.assemble, "Mass").Single();
        double loadedMass = Numbers(loaded.assemble, "Mass").Single();
        report.AppendLine($"  Assemble Model's Mass: {bareMass:0.##} kg bare, {loadedMass:0.##} kg with Mass from Loads");
        Check(Near(bareMass, ownPerMetre * BeamLength, 1e-9), $"bare: the steel alone, {ownPerMetre * BeamLength:0.##} kg");
        Check(Near(loadedMass, (ownPerMetre + addedPerMetre) * BeamLength, 1e-9),
              $"with Mass from Loads: the steel and 5 kN/m over g, {(ownPerMetre + addedPerMetre) * BeamLength:0.##} kg");

        var model = ModelOf(loaded.assemble);
        var lines = MassLines(model);
        Check(lines.Count == BeamPieces + 1 && lines.Select(l => l.node).Distinct().Count() == lines.Count,
              $"one mass line per node, {lines.Count} for {BeamPieces + 1} nodes: the halves from two beams at a node are added up");
        double deckTonnes = lines.Sum(l => l.mass);
        Check(Near(deckTonnes, addedPerMetre * BeamLength / 1000.0, 1e-9), $"the mass lines add up to the load over g, {deckTonnes:0.######} t");
        double inner = addedPerMetre * BeamLength / BeamPieces / 1000.0;
        Check(lines.Count(l => Near(l.mass, inner, 1e-9)) == BeamPieces - 1 && lines.Count(l => Near(l.mass, inner / 2, 1e-9)) == 2,
              "a whole element's share on each inner node, half of one on each end");

        var f0 = Numbers(bare.vibration, "Frequencies");
        var f1 = Numbers(loaded.vibration, "Frequencies");
        report.AppendLine($"  frequencies {Show(f0)} Hz bare, {Show(f1)} Hz with Mass from Loads");
        double ratio = Math.Sqrt(ownPerMetre / (ownPerMetre + addedPerMetre));
        Check(f0.Count == 3 && f1.Count == 3 && f0.Zip(f1, (a, b) => Near(b / a, ratio, 1e-5)).All(ok => ok),
              $"every frequency falls by √(m / (m + w/g)) = {ratio:0.#####}: the load's mass lies where the steel's does");

        double ei = 210e6 * Math.Pow(BeamSide, 4) / 12.0;
        double closed = Math.PI / (2 * BeamLength * BeamLength) * Math.Sqrt(ei / ((ownPerMetre + addedPerMetre) / 1000.0));
        Check(Near(f1[0], closed, 0.01), $"the first, {f1[0]:0.###} Hz, is π/2L² √(EI/m) = {closed:0.###} Hz within 1%");

        var again = model.WithBeamSections(model.Beams.Select(b => b.Section).ToList());
        Check(Near(again.TotalMass ?? 0, model.TotalMass ?? 0, 1e-12) && MassLines(again).SequenceEqual(lines),
              "assembled again with its sections (as Section Optimiser does), the mass is the same, not doubled");
        doc.Document.Enabled = false;
    }

    // ---------------------------------------------------------------- the slab

    const double SlabX = 6.0, SlabY = 4.0, SlabThickness = 0.2;
    const int SlabNx = 12, SlabNy = 8;
    const double Finishes = 1.5, Imposed = 3.0, ImposedShare = 0.1; // kN/m², kN/m², P354's 10%

    /// <summary>
    /// A concrete slab 6 x 4 m, 200 mm, in 12 x 8 ASD shells, simply supported all round, under 1.5 kN/m²
    /// of finishes and 3 kN/m² imposed; with <paramref name="withMass"/> the finishes are mass in full and
    /// the imposed load at 10%, as SCI P354 takes them. Footfall Analysis after it.
    /// </summary>
    static (GH_Component assemble, GH_Component vibration, GH_Component footfall) Slab(Doc doc, bool withMass)
    {
        var mesh = new Mesh();
        for (int j = 0; j <= SlabNy; j++)
            for (int i = 0; i <= SlabNx; i++)
                mesh.Vertices.Add(SlabX * i / SlabNx, SlabY * j / SlabNy, 0.0);
        for (int j = 0; j < SlabNy; j++)
            for (int i = 0; i < SlabNx; i++)
            {
                int a = j * (SlabNx + 1) + i;
                mesh.Faces.AddFace(a, a + 1, a + SlabNx + 2, a + SlabNx + 1);
            }

        var concrete = doc.Add<SimplexGh.nD>("ElasticIsotropic (Alpaca4d)");
        Set(concrete, "E", new GH_Number(30e6));
        Set(concrete, "G", new GH_Number(12.5e6));
        Set(concrete, "ν", new GH_Number(0.2));
        Set(concrete, "Rho", new GH_Number(2500.0));
        var section = doc.Add<Alpaca4d.Gh.PlateFiberSection>();
        Set(section, "Thickness", new GH_Number(SlabThickness));
        Wire(concrete, "Material", section, "Material");
        var shell = doc.Add<Alpaca4d.Gh.ASDShell>();
        Set(shell, "Mesh", new GH_Mesh(mesh));
        Wire(section, "Section", shell, "Section");

        var edge = new List<Point3d>();
        for (int j = 0; j <= SlabNy; j++)
            for (int i = 0; i <= SlabNx; i++)
                if (i == 0 || j == 0 || i == SlabNx || j == SlabNy)
                    edge.Add(new Point3d(SlabX * i / SlabNx, SlabY * j / SlabNy, 0.0));
        var support = Support(doc, edge, rotationsHeld: false);

        var finishes = doc.Add<Alpaca4d.Gh.MeshLoad>();
        Wire(shell, "Element", finishes, "Shell|Id");
        Set(finishes, "Force", new GH_Vector(new Vector3d(0, 0, -Finishes)));
        var imposed = doc.Add<Alpaca4d.Gh.MeshLoad>();
        Wire(shell, "Element", imposed, "Shell|Id");
        Set(imposed, "Force", new GH_Vector(new Vector3d(0, 0, -Imposed)));

        var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(series, "TimeSeries", pattern, "TimeSeries");
        In(pattern, "Loads").AddSource(finishes.Params.Output[0]);
        In(pattern, "Loads").AddSource(imposed.Params.Output[0]);

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        In(assemble, "Elements").AddSource(shell.Params.Output[0]);
        In(assemble, "Supports").AddSource(support.Params.Output[0]);
        In(assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);
        if (withMass)
        {
            var permanent = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
            In(permanent, "Load Patterns").AddSource(finishes.Params.Output[0]);
            var variable = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
            In(variable, "Load Patterns").AddSource(imposed.Params.Output[0]);
            Set(variable, "Factor", new GH_Number(ImposedShare));
            In(assemble, "LoadPatterns").AddSource(Out(permanent, "Mass"));
            In(assemble, "LoadPatterns").AddSource(Out(variable, "Mass"));
        }

        var vibration = Vibration(doc, assemble, 8);
        var footfall = doc.Add<Alpaca4d.Gh.FootfallAnalysis>("SelfExcitation");
        Wire(vibration, "AlpacaModel", footfall, "AlpacaModel");
        return (assemble, vibration, footfall);
    }

    static void SlabUnderMeshLoads()
    {
        Section("A concrete slab, 6 x 4 m, 200 mm, simply supported, 1.5 kN/m² finishes + 3 kN/m² imposed: finishes in full, imposed at 10%");
        var doc = new Doc("slab");
        var bare = Slab(doc, withMass: false);
        var loaded = Slab(doc, withMass: true);
        doc.Solve();
        NoErrors(doc, "both slabs");

        double area = SlabX * SlabY;
        double own = 2500.0 * SlabThickness;                               // kg/m²
        double added = (Finishes + ImposedShare * Imposed) / G * 1000.0;    // kg/m²
        double bareMass = Numbers(bare.assemble, "Mass").Single();
        double loadedMass = Numbers(loaded.assemble, "Mass").Single();
        report.AppendLine($"  Assemble Model's Mass: {bareMass:0.##} kg bare, {loadedMass:0.##} kg with Mass from Loads");
        Check(Near(bareMass, own * area, 1e-9), $"bare: the concrete alone, {own * area:0.##} kg");
        Check(Near(loadedMass, (own + added) * area, 1e-9), $"with Mass from Loads: and (1.5 + 0.1 x 3) kN/m² over g, {(own + added) * area:0.##} kg");

        var lines = MassLines(ModelOf(loaded.assemble));
        int nodes = (SlabNx + 1) * (SlabNy + 1);
        Check(lines.Count == nodes && lines.Select(l => l.node).Distinct().Count() == nodes,
              $"one mass line per node, {lines.Count} for {nodes}: the quarters from up to four shells and two loads are added up");
        double cell = SlabX / SlabNx * SlabY / SlabNy;
        Check(Near(lines.Sum(l => l.mass), added * area / 1000.0, 1e-9) && Near(lines.Max(l => l.mass), added * cell / 1000.0, 1e-9),
              $"they add up to the loads' mass, and an inner node carries a whole cell's, {added * cell:0.###} kg");

        var f0 = Numbers(bare.vibration, "Frequencies");
        var f1 = Numbers(loaded.vibration, "Frequencies");
        report.AppendLine($"  frequencies {Show(f0)} Hz bare");
        report.AppendLine($"              {Show(f1)} Hz with Mass from Loads");
        double ratio = Math.Sqrt(own / (own + added));
        Check(f0.Count == 8 && f1.Count == 8 && Near(f1[0] / f0[0], ratio, 0.002),
              $"the first falls by √(m / (m + q/g)) = {ratio:0.#####} within 0.2%: {f1[0] / f0[0]:0.#####}");

        double d = 30e6 * Math.Pow(SlabThickness, 3) / (12 * (1 - 0.2 * 0.2));
        double closed = Math.PI / 2 * (1 / (SlabX * SlabX) + 1 / (SlabY * SlabY)) * Math.Sqrt(d / ((own + added) / 1000.0));
        Check(Near(f1[0], closed, 0.03), $"and is the plate's π/2 (1/a² + 1/b²) √(D/m) = {closed:0.##} Hz within 3%: {f1[0]:0.##} Hz");

        var r0 = Numbers(bare.footfall, "Response Factor");
        var r1 = Numbers(loaded.footfall, "Response Factor");
        report.AppendLine($"  Footfall Analysis, self excitation, largest R: {(r0.Count > 0 ? r0.Max() : double.NaN):0.###} bare, " +
                          $"{(r1.Count > 0 ? r1.Max() : double.NaN):0.###} with Mass from Loads (for information)");
        Check(r0.Count > 0 && r1.Count > 0, "Footfall Analysis reads both");
        doc.Document.Enabled = false;
    }

    // ---------------------------------------------------------------- messages

    static void LoadsThatWeighNothing()
    {
        Section("Mass from Loads given what weighs nothing");
        var doc = new Doc("messages");

        var gravity = doc.Add<Alpaca4d.Gh.GravityLoad>();
        var up = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(up, "Point", new GH_Point(Point3d.Origin));
        Set(up, "Force", new GH_Vector(new Vector3d(0, 0, 10)));
        var slanted = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(slanted, "Point", new GH_Point(Point3d.Origin));
        Set(slanted, "Force", new GH_Vector(new Vector3d(5, 0, -10)));

        var nothing = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
        In(nothing, "Load Patterns").AddSource(gravity.Params.Output[0]);
        In(nothing, "Load Patterns").AddSource(up.Params.Output[0]);
        var some = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
        In(some, "Load Patterns").AddSource(slanted.Params.Output[0]);
        var negative = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
        In(negative, "Load Patterns").AddSource(slanted.Params.Output[0]);
        Set(negative, "Factor", new GH_Number(-1));
        doc.Solve();

        string Said(GH_Component c, GH_RuntimeMessageLevel level) => string.Join(" | ", c.RuntimeMessages(level));
        report.AppendLine($"  Gravity + an upward load: remark \"{Said(nothing, GH_RuntimeMessageLevel.Remark)}\", warning \"{Said(nothing, GH_RuntimeMessageLevel.Warning)}\"");
        Check(Said(nothing, GH_RuntimeMessageLevel.Remark).Contains("Gravity Load is left out"), "Gravity Load is left out, with a remark");
        Check(Said(nothing, GH_RuntimeMessageLevel.Warning).Contains("no mass") && !Out(nothing, "Mass").VolatileData.AllData(true).Any(),
              "an upward load weighs nothing: a warning and no output");
        Check(Said(some, GH_RuntimeMessageLevel.Remark).Contains("Only the downward part weighs") && Out(some, "Mass").VolatileData.AllData(true).Any(),
              "a load pushing sideways and down: a remark, and its downward part is mass");
        Check(Said(negative, GH_RuntimeMessageLevel.Error).Contains("zero or more"), "a negative Factor is an error");
        doc.Document.Enabled = false;
    }

    // ---------------------------------------------------------------- by the names of load patterns

    /// <summary>A Load Pattern named <paramref name="name"/> of <paramref name="loads"/>, on a constant series.</summary>
    static GH_Component NamedPattern(Doc doc, string name, params GH_Component[] loads)
    {
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        foreach (var load in loads)
            In(pattern, "Loads").AddSource(load.Params.Output[0]);
        Set(pattern, "Name", new GH_String(name));
        return pattern;
    }

    static GH_Component MassOf(Doc doc, double factor, params IGH_Goo[] patterns)
    {
        var mass = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
        var param = In(mass, "Load Patterns");
        var data = (GH_Structure<IGH_Goo>)param.GetType().GetProperty("PersistentData").GetValue(param);
        foreach (var p in patterns)
            data.Append(p);
        Set(mass, "Factor", new GH_Number(factor));
        return mass;
    }

    /// <summary>
    /// The beam of <see cref="BeamUnderLineLoad"/> with two named patterns - "Imposed", 5 kN/m, and
    /// "Finishes", 2 kN/m with a Gravity Load - and Mass from Loads naming them, "imposed" at 0.1 and
    /// "Finishes" at 1, beside the same loads wired in; and a name no pattern has.
    /// </summary>
    static void ByPatternName()
    {
        Section("Mass from Loads by the names of Load Patterns: \"imposed\" (5 kN/m) at 0.1, \"Finishes\" (2 kN/m and Gravity Load) at 1");
        var doc = new Doc("named");

        var steel = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var section = doc.Add<Alpaca4d.Gh.RectangleCS>();
        Set(section, "Width", new GH_Number(BeamSide));
        Set(section, "Height", new GH_Number(BeamSide));
        Wire(steel, "Material", section, "Material");
        var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beam, "Line", Enumerable.Range(0, BeamPieces)
            .Select(i => new GH_Curve(new LineCurve(new Point3d(BeamLength * i / BeamPieces, 0, 0), new Point3d(BeamLength * (i + 1) / BeamPieces, 0, 0))))
            .ToArray());
        Wire(section, "Section", beam, "Section");
        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", new GH_Plane(Plane.WorldXY), new GH_Plane(new Plane(new Point3d(BeamLength, 0, 0), Vector3d.XAxis, Vector3d.YAxis)));
        Set(support, "Ry", new GH_Boolean(false));
        Set(support, "Rz", new GH_Boolean(false));

        GH_Component LineLoad(double q)
        {
            var load = doc.Add<Alpaca4d.Gh.LinearLoad>();
            Wire(beam, "Element", load, "Beam|Id");
            Set(load, "Force", new GH_Vector(new Vector3d(0, 0, -q)));
            return load;
        }
        var imposed = LineLoad(5.0);
        var finishes = LineLoad(2.0);
        var gravity = doc.Add<Alpaca4d.Gh.GravityLoad>();
        var imposedPattern = NamedPattern(doc, "Imposed", imposed);
        var finishesPattern = NamedPattern(doc, "Finishes", finishes, gravity);

        GH_Component Assemble(params GH_Component[] masses)
        {
            var a = doc.Add<Alpaca4d.Gh.AssembleModel>();
            In(a, "Elements").AddSource(beam.Params.Output[0]);
            In(a, "Supports").AddSource(support.Params.Output[0]);
            In(a, "LoadPatterns").AddSource(imposedPattern.Params.Output[0]);
            In(a, "LoadPatterns").AddSource(finishesPattern.Params.Output[0]);
            foreach (var m in masses)
                In(a, "LoadPatterns").AddSource(Out(m, "Mass"));
            return a;
        }

        var byName = Assemble(MassOf(doc, 0.1, new GH_String("imposed")), MassOf(doc, 1.0, new GH_String(" Finishes ")));
        var wiredImposed = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
        In(wiredImposed, "Load Patterns").AddSource(imposed.Params.Output[0]);
        Set(wiredImposed, "Factor", new GH_Number(0.1));
        var wiredFinishes = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
        In(wiredFinishes, "Load Patterns").AddSource(finishes.Params.Output[0]);
        var wired = Assemble(wiredImposed, wiredFinishes);
        var missing = Assemble(MassOf(doc, 1.0, new GH_String("Snow")));
        doc.Solve();

        string Said(GH_Component c, GH_RuntimeMessageLevel level) => string.Join(" | ", c.RuntimeMessages(level));
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Count == 0, errors.Count == 0 ? "no component has an error" : string.Join(" | ", errors));

        var pattern = (Alpaca4d.Loads.LoadPattern)Out(imposedPattern, "LoadPattern").VolatileData.AllData(true).Single().ScriptVariable();
        Check(pattern.Name == "Imposed", $"Load Pattern carries its Name: \"{pattern.Name}\"");

        double own = 7850.0 * BeamSide * BeamSide * BeamLength;
        double expected = own + (0.1 * 5.0 + 1.0 * 2.0) / G * 1000.0 * BeamLength;
        double named = Numbers(byName, "Mass").Single();
        report.AppendLine($"  Assemble Model's Mass by name: {named:0.##} kg, by the loads wired in: {Numbers(wired, "Mass").Single():0.##} kg");
        Check(Near(named, expected, 1e-9), $"by name, whatever the case and spaces: the steel, 0.1 x 5 and 1 x 2 kN/m over g, {expected:0.##} kg");
        Check(MassLines(ModelOf(byName)).SequenceEqual(MassLines(ModelOf(wired))), "the same mass lines as the loads wired in");
        report.AppendLine($"  Assemble by name says: remark \"{Said(byName, GH_RuntimeMessageLevel.Remark)}\"");
        Check(Said(byName, GH_RuntimeMessageLevel.Remark).Contains("leaves out the Gravity Load in \"Finishes\"") &&
              !Said(byName, GH_RuntimeMessageLevel.Warning).Contains("Mass from Loads"),
              "the Gravity Load in \"Finishes\" is left out, with a remark on Assemble, and nothing to warn of");

        string warned = Said(missing, GH_RuntimeMessageLevel.Warning);
        report.AppendLine($"  a name no pattern has: \"{warned}\"");
        Check(warned.Contains("\"Snow\"") && warned.Contains("\"Imposed\"") && warned.Contains("\"Finishes\"") &&
              Near(Numbers(missing, "Mass").Single(), own, 1e-9),
              "a name no pattern has is a warning on Assemble that lists the names there are, and makes no mass");
        doc.Document.Enabled = false;
    }
}
