// Footfall Analysis end to end on shells: the simply supported beam of FootfallCheck.cs made again as a
// plate strip, out of the real Grasshopper components - ElasticIsotropic, Plate Fiber Section, ASD Shell,
// Support, Assemble Model, Natural Vibration, Footfall Analysis - in a running Rhino 8, and checked
// against the beam by hand (ByHand.cs). plate.sh loads this build of the plugin beside the installed one
// and calls Run by reflection. See README.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using static ByHand;

public static class PlateCheck
{
    static StringBuilder report;
    static int fails;
    static string folder;

    // The beam of FootfallCheck.cs: 10 m, E I = 2.1e8 kN/m² x 1.5e-4 m⁴, 0.5 t/m, simply supported.
    const double Span = 10.0, E = 2.1e8, I = 1.5e-4, MassPerLength = 0.5;

    // As a plate strip 1.8 m wide and 100 mm thick: b t³ / 12 = 1.5e-4 m⁴. With ν = 0 the plate's D b is
    // E I exactly, with no Poisson stiffening, and the strip bends the same all across its width. Its
    // density puts 0.5 t on each metre: 2 777.8 kg/m³.
    const double Width = 1.8, Thickness = 0.1;
    const int Nx = 20, Ny = 4;                 // 0.5 m along, 0.45 m across: nodes at midspan and quarter span
    static readonly double Density = MassPerLength / (Width * Thickness) * 1000.0;

    // Footfall Analysis with P354's defaults, as FootfallCheck.cs's beam: walking, 76 kg, Wb, 1.8 - 2.2 Hz
    // in 401 paces, cut-off 10 Hz, 3% damping, no footsteps given (ρ = 1).
    const int Paces = 401;
    const double Damping = 0.03, G = 9.81;

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
            File.AppendAllText(Path.Combine(folder, "progress.txt"), "plate\n");
            PlateStrip();
        }
        catch (Exception e)
        {
            Check(false, "no exception: " + e);
        }
        report.AppendLine();
        report.AppendLine(fails == 0 ? "all plate checks passed" : $"{fails} plate check(s) failed");
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

    static List<Point3d> Points(GH_Component c, string output) =>
        Out(c, output).VolatileData.AllData(true).OfType<GH_Point>().Select(p => p.Value).ToList();

    static string Text(GH_Component c, string output) =>
        string.Join("\n", Out(c, output).VolatileData.AllData(true).OfType<GH_String>().Select(s => s.Value));

    static string Show(IEnumerable<double> values) => string.Join(", ", values.Select(v => v.ToString("G6")));

    // ---------------------------------------------------------------- the plate strip

    sealed class Strip
    {
        public GH_Component Assemble, Vibration, Self, Full;
    }

    static Point3d Node(int i, int j) => new Point3d(Span * i / Nx, Width * j / Ny, 0.0);

    /// <summary>
    /// The strip, 10 x 1.8 m, 100 mm, in 20 x 4 ASD shells, held along X, Y and Z on its two short edges and
    /// free to turn there; Natural Vibration, then Footfall Analysis by self excitation on the nodes of the
    /// shells, and by full excitation felt at <paramref name="feel"/>. With <paramref name="halfAsLoad"/> half
    /// its mass comes from its density and half from a mesh load in a Load Pattern named "Finishes", which
    /// Mass from Loads names.
    /// </summary>
    static Strip Plate(Doc doc, bool halfAsLoad, Point3d feel)
    {
        var mesh = new Mesh();
        for (int j = 0; j <= Ny; j++)
            for (int i = 0; i <= Nx; i++)
                mesh.Vertices.Add(Node(i, j));
        for (int j = 0; j < Ny; j++)
            for (int i = 0; i < Nx; i++)
            {
                int a = j * (Nx + 1) + i;
                mesh.Faces.AddFace(a, a + 1, a + Nx + 2, a + Nx + 1);
            }

        var steel = doc.Add<SimplexGh.nD>("ElasticIsotropic (Alpaca4d)");
        Set(steel, "E", new GH_Number(E));
        Set(steel, "G", new GH_Number(E / 2));
        Set(steel, "ν", new GH_Number(0.0));
        Set(steel, "Rho", new GH_Number(halfAsLoad ? Density / 2 : Density));
        var section = doc.Add<Alpaca4d.Gh.PlateFiberSection>();
        Set(section, "Thickness", new GH_Number(Thickness));
        Wire(steel, "Material", section, "Material");
        var shell = doc.Add<Alpaca4d.Gh.ASDShell>();
        Set(shell, "Mesh", new GH_Mesh(mesh));
        Wire(section, "Section", shell, "Section");

        var support = doc.Add<Alpaca4d.Gh.Support>();
        var ends = Enumerable.Range(0, Ny + 1).SelectMany(j => new[] { Node(0, j), Node(Nx, j) });
        Set(support, "Position", ends.Select(p => new GH_Plane(new Plane(p, Vector3d.XAxis, Vector3d.YAxis))).ToArray());
        foreach (var name in new[] { "Rx", "Ry", "Rz" })
            Set(support, name, new GH_Boolean(false));

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        In(assemble, "Elements").AddSource(shell.Params.Output[0]);
        In(assemble, "Supports").AddSource(support.Params.Output[0]);
        if (halfAsLoad)
        {
            // The other 0.25 t/m as a load over the 1.8 m: 0.13889 t/m², its weight 1.3625 kN/m² down.
            var finishes = doc.Add<Alpaca4d.Gh.MeshLoad>();
            Wire(shell, "Element", finishes, "Shell|Id");
            Set(finishes, "Force", new GH_Vector(new Vector3d(0, 0, -MassPerLength / 2 / Width * G)));
            var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
            In(pattern, "Loads").AddSource(finishes.Params.Output[0]);
            Set(pattern, "Name", new GH_String("Finishes"));
            var mass = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
            var names = In(mass, "Load Patterns");
            ((GH_Structure<IGH_Goo>)names.GetType().GetProperty("PersistentData").GetValue(names)).Append(new GH_String("Finishes"));
            In(assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);
            In(assemble, "LoadPatterns").AddSource(Out(mass, "Mass"));
        }

        var vibration = doc.Add<Alpaca4d.Gh.NaturalVibration>("NaturalVibration");
        Wire(assemble, "AlpacaModel", vibration, "AlpacaModel");
        Set(vibration, "Vibration Modes", new GH_Integer(6));

        GH_Component Footfall(string unit)
        {
            var footfall = doc.Add<Alpaca4d.Gh.FootfallAnalysis>(unit);
            Wire(vibration, "AlpacaModel", footfall, "AlpacaModel");
            Set(footfall, "Frequency Steps", new GH_Integer(Paces));
            Set(footfall, "Damping", new GH_Number(Damping));
            return footfall;
        }
        var self = Footfall("SelfExcitation");
        var full = Footfall("FullExcitation");
        Set(full, "Feel", new GH_Point(feel));
        return new Strip { Assemble = assemble, Vibration = vibration, Self = self, Full = full };
    }

    /// <summary>R by hand for one mode of <paramref name="f"/> Hz and φ² = <paramref name="phi2"/> at the walker: the greater of the steady state and the transient at the worst of the paces.</summary>
    static (double r, double steady, double transient) ByHandR(double f, double phi2)
    {
        double q = 76 * G / 1000, qN = 76 * G;
        var paces = Enumerable.Range(0, Paces).Select(i => 1.8 + 0.4 * i / (Paces - 1)).ToArray();
        double steady = paces.Max(fp => SteadyByHand(fp, new[] { f }, new[] { phi2 }, Damping, null, q)) / 0.005;
        double transient = paces.Max(fp => TransientByHand(fp, new[] { f }, new[] { phi2 }, Damping, qN)) / 0.005;
        return (Math.Max(steady, transient), steady, transient);
    }

    static string Key(Point3d p) => $"{p.X:0.###} {p.Y:0.###} {p.Z:0.###}";

    static void PlateStrip()
    {
        Section("The beam of FootfallCheck.cs as a plate strip: 10 x 1.8 m, 100 mm, ν = 0, 20 x 4 ASD shells, 0.5 t/m, simply supported");
        var doc = new Doc("plate");
        var quarter = Node(Nx / 4, Ny / 2);
        var plate = Plate(doc, halfAsLoad: false, quarter);
        var split = Plate(doc, halfAsLoad: true, quarter);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        doc.Solve();
        clock.Stop();
        var said = doc.Messages(GH_RuntimeMessageLevel.Error).Concat(doc.Messages(GH_RuntimeMessageLevel.Warning)).ToList();
        Check(said.Count == 0, said.Count == 0 ? $"solved in {clock.Elapsed.TotalSeconds:0.#} s: no component has an error or a warning" : string.Join(" | ", said));

        // ---- the modes
        // A mesh keeps its vertices in single precision: 0.45 m across is 0.449999988, so the area is 1e-8 off.
        double mass = Numbers(plate.Assemble, "Mass").Single();
        Check(Near(mass, MassPerLength * Span * 1000, 1e-6), $"Assemble Model's Mass: 0.5 t/m over 10 m, {mass:0.######} kg");

        var f = Numbers(plate.Vibration, "Frequencies");
        double closed = Math.PI / 2 * Math.Sqrt(E * I / (MassPerLength * Math.Pow(Span, 4)));
        report.AppendLine($"  frequencies {Show(f)} Hz");
        Check(f.Count == 6 && Near(f[0], closed, 0.01), $"the first, {f[0]:0.####} Hz, is the beam's π/2 √(EI / m L⁴) = {closed:0.####} Hz within 1% (the beam in OpenSees: 3.943)");
        Check(f.Count == 6 && f[1] > 12 && f[1] > 2 * f[0], $"the next, {(f.Count > 1 ? f[1] : 0):0.##} Hz, is past 12 Hz and twice the first: the first alone counts in both responses");

        // ---- self excitation: the walker on every node of the shells
        var r = Numbers(plate.Self, "Response Factor");
        var at = Points(plate.Self, "Point");
        var steadyR = Numbers(plate.Self, "Steady-State Factor");
        var transientR = Numbers(plate.Self, "Transient Factor");
        var critical = Numbers(plate.Self, "Critical Frequency");
        string selfReport = Text(plate.Self, "Report");
        Check(r.Count == (Nx + 1) * (Ny + 1) && at.Count == r.Count && selfReport.Contains("none given, so the 105 nodes of the shell elements"),
              $"self excitation walks on the {r.Count} nodes of the shells");
        int Index(Point3d p) => at.FindIndex(q => q.DistanceTo(p) < 1e-6);

        var middle = Enumerable.Range(0, Ny + 1).Select(j => Index(Node(Nx / 2, j))).ToList();
        int centre = Index(Node(Nx / 2, Ny / 2));
        double rMid = r[centre];
        Check(middle.All(k => k >= 0) && middle.All(k => Near(r[k], rMid, 1e-3)),
              $"the same R all across midspan, {string.Join(", ", middle.Select(k => r[k].ToString("0.###")))}: it bends as the beam does");
        Check(Math.Abs(at[r.IndexOf(r.Max())].X - Span / 2) < 1e-6, $"midspan responds the most: R = {r.Max():0.##}");

        // By hand: the strip's own frequency, and φ² = 2 / m L at midspan, the beam's mass-normalised sine.
        double phi2 = 2 / (MassPerLength * Span);
        var hand = ByHandR(f[0], phi2);
        Check(Near(rMid, hand.r, 0.01) && Near(steadyR[centre], hand.steady, 0.01) && Near(transientR[centre], hand.transient, 0.01),
              $"midspan as by hand with its {f[0]:0.###} Hz and φ² = 2 / m L: R = {rMid:0.##} against {hand.r:0.##} " +
              $"(steady state {steadyR[centre]:0.##} / {hand.steady:0.##}, transient {transientR[centre]:0.##} / {hand.transient:0.##}), at {critical[centre]:0.###} Hz");
        var beam = ByHandR(closed, phi2);
        Check(Near(rMid, beam.r, 0.01), $"and the beam's R by hand at π/2 √(EI / m L⁴), {beam.r:0.##}, within 1% (the beam in OpenSees: 54.65)");

        var supports = Enumerable.Range(0, Ny + 1).SelectMany(j => new[] { Index(Node(0, j)), Index(Node(Nx, j)) }).ToList();
        Check(supports.All(k => k >= 0 && r[k] < 1e-9), "the supports do not respond");
        int atQuarter = Index(quarter);
        Check(Near(r[atQuarter], rMid * 0.5, 0.01), $"at quarter span, sin² 45° of midspan: R = {r[atQuarter]:0.##} against {rMid * 0.5:0.##}");

        // ---- full excitation: felt at quarter span, the walker anywhere on the shells
        var felt = Numbers(plate.Full, "Response Factor");
        var walker = Points(plate.Full, "Walker Point");
        Check(felt.Count == 1 && walker.Count == 1 && Math.Abs(walker[0].X - Span / 2) < 1e-6,
              $"full excitation felt at quarter span: the worst walker at midspan, {(walker.Count == 1 ? Key(walker[0]) : "none")}");
        Check(felt.Count == 1 && Near(felt[0], rMid * Math.Sin(Math.PI / 4), 0.01),
              $"R the midspan's times sin 45°: {(felt.Count == 1 ? felt[0] : 0):0.##} against {rMid * Math.Sin(Math.PI / 4):0.##}");

        // ---- half the mass from Mass from Loads: ASD shells lump their mass a quarter to each corner, as
        // Mass from Loads does a mesh load, so the model is the same one, to the solver's last digits.
        Section("Half the 0.5 t/m from the density, half from a mesh load in \"Finishes\" named by Mass from Loads");
        double splitMass = Numbers(split.Assemble, "Mass").Single();
        var fs = Numbers(split.Vibration, "Frequencies");
        var rs = Numbers(split.Self, "Response Factor");
        var ats = Points(split.Self, "Point");
        var fullS = Numbers(split.Full, "Response Factor");
        Check(Near(splitMass, mass, 1e-9), $"Assemble Model's Mass the same, {splitMass:0.##} kg");
        Check(fs.Count == f.Count && fs.Zip(f, (a, b) => Near(a, b, 1e-6)).All(ok => ok), $"the same frequencies to 1e-6: {Show(fs)} Hz");
        Check(rs.Count == r.Count && ats.Select(Key).SequenceEqual(at.Select(Key)) &&
              rs.Zip(r, (a, b) => Math.Abs(a - b) <= 1e-6 * Math.Max(1, Math.Abs(b))).All(ok => ok),
              $"the same R at every node to 1e-6: {rs[centre]:0.####} against {rMid:0.####} at midspan");
        Check(fullS.Count == 1 && felt.Count == 1 && Near(fullS[0], felt[0], 1e-6), "the same full excitation R");

        report.AppendLine();
        report.AppendLine("  " + string.Join("\n  ", selfReport.Trim().Split('\n')));
        doc.Document.Enabled = false;
    }
}
