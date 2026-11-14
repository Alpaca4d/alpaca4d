// J2Plasticity: what Alpaca4d writes for a metal that yields, what Deserialise reads back, and
// what OpenSees does with it on a brick, on shells and on ten node tetrahedra.
//
//   1. The material line, its plate form and its density comment; the shell sections naming the
//      plate form; the inputs refused; a deck read back.
//   2. With OpenSees, on decks this writes around Alpaca4d's own material and section lines:
//      - one brick pulled past yield holds at Fy;
//      - a clamped steel strip of ASD shells, quads and triangles, through a Layered Shell
//        section and a Plate Fiber one, levels off past the plastic load with no warnings;
//      - a clamped beam of ten node tetrahedra matches beam theory before yield and levels off
//        after it, where an SSP brick beam does not.
//
// OPENSEES (default OpenSees on PATH) runs the brick and the shells; OPENSEES_TET10 (default
// /Applications/OpenSeesLatest-Mumps/bin/OpenSees) the tetrahedra and an SSP brick beam to compare.
// Without them only the checks that need no solver run. Point3d is managed, so no RhinoCommon
// native library is needed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

using Alpaca4d;
using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Section;

class J2PlasticityTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    // What Alpaca4d itself writes a number as.
    static string T(double value) => Alpaca4d.TclNumber.Write(value);

    // Steel in kN and m, as Alpaca4d writes it.
    const double E = 210e6, Nu = 0.3, Fy = 355e3;

    static J2PlasticityMaterial Steel(double h = 0.0)
    {
        return new J2PlasticityMaterial("S355", E, Nu, Fy, h, 7850) { Id = 1, PlateId = 2 };
    }

    // ------------------------------------------------------------------------------------------
    // 1. What is written and read back
    // ------------------------------------------------------------------------------------------

    static void Writing()
    {
        Console.WriteLine("What Alpaca4d writes");

        var steel = Steel(2.1e4);
        var lines = steel.WriteTcl().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        double k = E / (3 * (1 - 2 * Nu)), g = E / (2 * (1 + Nu));

        Check(lines.Length == 3, $"three lines: the material, its plate form, its density ({lines.Length})");
        Check(lines[0] == $"nDMaterial J2Plasticity 1 {T(k)} {T(g)} {T(Fy)} {T(Fy)} 0.0 {T(2.1e4)}",
              $"J2Plasticity takes K and G, Fy twice and no saturation: {lines[0]}");
        Check(lines[1] == $"nDMaterial PlateFromPlaneStress 2 1 {T(g)}", $"the plate form wraps it under its own tag: {lines[1]}");
        Check(lines[2] == "# alpaca:density 1 7850", $"the density goes as a comment, which OpenSees skips: {lines[2]}");
        Check(new J2PlasticityMaterial(null, E, Nu, Fy).WriteTcl().Split('\n').Count(l => l.Length > 0) == 2,
              "no density, no comment");

        var layered = LayeredShellSection.Uniform("plate", 0.01, 4, steel);
        layered.Id = 7;
        Check(layered.WriteTcl() == "section LayeredShell 7 4 2 0.0025 2 0.0025 2 0.0025 2 0.0025\n",
              $"a Layered Shell section names the plate form: {layered.WriteTcl().Trim()}");

        var plateFiber = new PlateFiberSection("plate", 0.01, steel) { Id = 8 };
        Check(plateFiber.WriteTcl() == "section PlateFiber 8 2 0.01\n", $"so does a Plate Fiber section: {plateFiber.WriteTcl().Trim()}");

        var elastic = new ElasticIsotropicMaterial("S355", E, g, Nu, 7850) { Id = 5 };
        var elasticSection = new PlateFiberSection("plate", 0.01, elastic) { Id = 9 };
        Check(elasticSection.WriteTcl() == "section PlateFiber 9 5 0.01\n", "an elastic material is named as before");

        Check(Math.Abs(steel.G - g) < 1e-6 && Math.Abs(steel.K - k) < 1e-6, "G and K follow from E and ν");

        // ASD shells on a plastic section turn off the enhanced assumed strains and take the drilling
        // stiffness from the current tangent; on an elastic one they write what they always did.
        string q4Plastic = Shell(true, layered).WriteTcl(), q4Elastic = Shell(true, elasticSection).WriteTcl();
        string t3Plastic = Shell(false, layered).WriteTcl(), t3Elastic = Shell(false, elasticSection).WriteTcl();
        Check(q4Plastic.Contains(" -noeas -drillingNL"), $"a quad on a plastic section: {q4Plastic.Trim()}");
        Check(t3Plastic.Contains(" -drillingNL") && !t3Plastic.Contains("-noeas"), $"a triangle on one: {t3Plastic.Trim()}");
        Check(!q4Elastic.Contains("-noeas") && !q4Elastic.Contains("-drillingNL") && !t3Elastic.Contains("-drillingNL"),
              "on an elastic section neither");

        var refusals = new Dictionary<string, Action>
        {
            { "ν = 0.5", () => new J2PlasticityMaterial(null, E, 0.5, Fy) },
            { "E = 0", () => new J2PlasticityMaterial(null, 0.0, Nu, Fy) },
            { "Fy = 0", () => new J2PlasticityMaterial(null, E, Nu, 0.0) },
            { "H < 0", () => new J2PlasticityMaterial(null, E, Nu, Fy, -1.0) },
        };
        foreach (var refusal in refusals)
        {
            bool refused = false;
            try { refusal.Value(); } catch (ArgumentException) { refused = true; }
            Check(refused, $"{refusal.Key} is refused");
        }
    }

    /// <summary>An ASD shell with only what WriteTcl reads set: its constructor wants a RhinoCommon mesh.</summary>
    static ISerialize Shell(bool quad, IMultiDimensionSection section, int id = 1, IList<int> nodes = null)
    {
        if (quad)
        {
            var q4 = (Alpaca4d.Element.ASDShellQ4)FormatterServices.GetUninitializedObject(typeof(Alpaca4d.Element.ASDShellQ4));
            q4.Id = id;
            q4.Section = section;
            q4.IndexNodes = (nodes ?? new[] { 1, 2, 3, 4 }).Select(n => (int?)n).ToList();
            return q4;
        }
        var t3 = (Alpaca4d.Element.ASDShellT3)FormatterServices.GetUninitializedObject(typeof(Alpaca4d.Element.ASDShellT3));
        t3.Id = id;
        t3.Section = section;
        t3.IndexNodes = (nodes ?? new[] { 1, 2, 3 }).Select(n => (int?)n).ToList();
        return t3;
    }

    /// <summary>
    /// What Run Analysis keeps of a solver that prints without end: a diverging plastic shell printed
    /// 2.6 GB of one warning, and holding it all took Rhino down.
    /// </summary>
    static void Logs()
    {
        Console.WriteLine("A log that runs away");
        var log = new Model.CappedLog();
        log.AppendLine("Using ASDShellQ4 - Developed by: Massimo Petracca, Guido Camata, ASDEA Software Technology");
        for (int i = 0; i < 60000; i++)
            log.AppendLine("More than 25 iterations in constituive subroutine J2-plasticity ");
        log.AppendLine("ALPACA_ANALYZE_RESULT -3");
        string kept = log.ToString();
        Check(kept.Length < 3 * Model.CappedLog.Cap, $"{kept.Length / 1024} kB kept of {(log.Dropped * 64 + 2 * Model.CappedLog.Cap) / 1024} kB");
        Check(kept.StartsWith("Using ASDShellQ4") && kept.TrimEnd().EndsWith("ALPACA_ANALYZE_RESULT -3"), "its first and last lines are there");
        Check(log.Dropped > 0 && kept.Contains("lines (") && kept.Contains("left out"), $"and says how much is left out: {log.Dropped} lines");
        var small = new Model.CappedLog();
        small.AppendLine("one");
        small.AppendLine("two");
        Check(small.ToString() == "one" + Environment.NewLine + "two" + Environment.NewLine, "a short log is kept whole");
    }

    static void Reading()
    {
        Console.WriteLine("What Deserialise reads back");

        var steel = Steel(2.1e4);
        var deck = new List<string> { "model BasicBuilder -ndm 3 -ndf 6" };
        deck.AddRange(steel.WriteTcl().Split('\n'));
        deck.Add("section PlateFiber 8 2 0.01");
        deck.Add("nDMaterial J2Plasticity 20 1.75e8 8.0769e7 355000 400000 10.0 0.0");

        var reader = TclReader.ReadText(deck);
        var materials = (Dictionary<int, IMultiDimensionMaterial>)typeof(TclReader)
            .GetField("_ndMaterials", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(reader);
        var sections = (Dictionary<int, PlateFiberSection>)typeof(TclReader)
            .GetField("_plateSections", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(reader);

        var read = materials.TryGetValue(1, out var m) ? m as J2PlasticityMaterial : null;
        Check(read != null, "a J2Plasticity line reads as a J2Plasticity material");
        if (read != null)
        {
            Check(Math.Abs(read.E - E) < 1e-3 * E && Math.Abs(read.Nu - Nu) < 1e-9,
                  $"E and ν come back from K and G: {read.E:G8}, {read.Nu:G8}");
            Check(read.Fy == Fy && read.H == 2.1e4, $"Fy and H come back: {read.Fy}, {read.H}");
            Check(read.Rho == 7850, $"the density comes back from its comment: {read.Rho}");
        }
        Check(materials.TryGetValue(2, out var plate) && ReferenceEquals(plate, read),
              "the plate form reads as the material it wraps");
        Check(sections.TryGetValue(8, out var section) && ReferenceEquals(section.Material, read),
              "a section naming the plate form gets that material");
        Check(reader.Warnings.Any(w => w.Contains("saturation")), "saturation hardening it cannot keep is said");
        Check(!reader.Warnings.Any(w => w.Contains("PlateFromPlaneStress") || w.Contains("density")),
              "nothing else is: " + string.Join(" | ", reader.Warnings));
    }

    // ------------------------------------------------------------------------------------------
    // 2. OpenSees
    // ------------------------------------------------------------------------------------------

    // Drives a node to a displacement; a step that fails is tried with other algorithms, then
    // halved. Prints w and P at every step.
    const string Drive = @"
proc drive {node dof target step0} {
  set w 0.0; set step $step0
  while {$w < $target - 1e-12} {
    integrator DisplacementControl $node $dof $step
    set ok [analyze 1]
    foreach alg {NewtonLineSearch KrylovNewton} {
      if {$ok == 0} break
      algorithm $alg; set ok [analyze 1]; algorithm Newton
    }
    if {$ok != 0} {
      set step [expr $step/2.0]
      if {$step < $step0/1024.0} { puts ""GAVEUP $w [getTime]""; return }
      continue
    }
    set w [nodeDisp $node $dof]
    puts ""STEP $w [getTime]""
    if {$step < $step0} { set step [expr min($step*2.0, $step0)] }
  }
}
";

    static string Run(string openSees, string dir, string deck)
    {
        var info = new ProcessStartInfo(openSees, deck)
        {
            WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        using (var process = Process.Start(info))
        {
            var stderr = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return stdout + stderr.Result;
        }
    }

    /// <summary>The load at each step of a drive, by displacement.</summary>
    static List<(double W, double P)> Curve(string log)
    {
        return log.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("STEP "))
                  .Select(l => l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                  .Select(t => (double.Parse(t[1], CultureInfo.InvariantCulture), double.Parse(t[2], CultureInfo.InvariantCulture)))
                  .ToList();
    }

    /// <summary>The load at displacement <paramref name="w"/>, between the steps either side of it; NaN past the end.</summary>
    static double At(List<(double W, double P)> curve, double w)
    {
        for (int i = 1; i < curve.Count; i++)
        {
            if (curve[i].W >= w - 1e-9)
            {
                var a = curve[i - 1];
                var b = curve[i];
                double t = b.W > a.W ? (w - a.W) / (b.W - a.W) : 1.0;
                return a.P + Math.Max(0.0, Math.Min(1.0, t)) * (b.P - a.P);
            }
        }
        return double.NaN;
    }

    static void Brick(string openSees, string dir)
    {
        Console.WriteLine("One brick pulled past yield");

        var deck = new StringBuilder("wipe\nmodel BasicBuilder -ndm 3 -ndf 3\n");
        deck.Append(Steel().WriteTcl());
        deck.Append(@"
node 1 0 0 0; node 2 1 0 0; node 3 1 1 0; node 4 0 1 0
node 5 0 0 1; node 6 1 0 1; node 7 1 1 1; node 8 0 1 1
fixX 0 1 0 0
fixY 0 0 1 0
fixZ 0 0 0 1
element SSPbrick 1 1 2 3 4 5 6 7 8 1
timeSeries Linear 1
pattern Plain 1 1 { foreach n {2 3 6 7} { load $n 0.25 0 0 } }
constraints Plain; numberer Plain; system FullGeneral
test NormDispIncr 1e-12 50 0; algorithm Newton
analysis Static
" + Drive + "drive 2 1 0.005 0.0005\n");
        File.WriteAllText(Path.Combine(dir, "brick.tcl"), deck.ToString());

        var curve = Curve(Run(openSees, dir, "brick.tcl"));
        double before = At(curve, 0.0015), after = At(curve, 0.005);
        Check(Math.Abs(before - E * 0.0015) < 1e-6 * E * 0.0015, $"elastic below Fy: {before:G6} at a strain of 0.0015");
        Check(Math.Abs(after - Fy) < 1e-3 * Fy, $"holds at Fy past it: {after:G6} at 0.005, Fy {Fy}");
    }

    /// <summary>A clamped strip 1 m long, 0.1 m wide, 10 mm thick, pushed at its tip; quads or triangles.</summary>
    static string Strip(ISerialize section, bool triangles)
    {
        var deck = new StringBuilder("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
        deck.Append(Steel().WriteTcl());
        deck.Append(section.WriteTcl());
        const int nx = 20;
        for (int i = 0; i <= nx; i++)
            deck.Append($"node {2 * i + 1} {N(i / (double)nx)} 0 0\nnode {2 * i + 2} {N(i / (double)nx)} 0.1 0\n");
        deck.Append("fix 1 1 1 1 1 1 1\nfix 2 1 1 1 1 1 1\n");
        for (int i = 0; i < nx; i++)
        {
            int a = 2 * i + 1;
            if (triangles)
                deck.Append($"element ASDShellT3 {2 * i + 1} {a} {a + 2} {a + 3} 3\nelement ASDShellT3 {2 * i + 2} {a} {a + 3} {a + 1} 3\n");
            else
                deck.Append($"element ASDShellQ4 {i + 1} {a} {a + 2} {a + 3} {a + 1} 3\n");
        }
        int tip = 2 * nx + 1;
        deck.Append($"timeSeries Linear 1\npattern Plain 1 1 {{ load {tip} 0 0 0.5 0 0 0; load {tip + 1} 0 0 0.5 0 0 0 }}\n");
        deck.Append("constraints Transformation; numberer RCM; system UmfPack\ntest NormDispIncr 1e-8 50 0; algorithm Newton\nanalysis Static\n");
        deck.Append(Drive);
        deck.Append($"drive {tip} 3 0.6 0.005\n");
        return deck.ToString();
    }

    static void Shells(string openSees, string dir)
    {
        Console.WriteLine("A clamped steel strip of ASD shells");

        // Beam theory: first yield at the faces fy b t^2 / 6 / L, fully plastic fy b t^2 / 4 / L.
        double plastic = Fy * 0.1 * 0.01 * 0.01 / 4.0;
        var layered = LayeredShellSection.Uniform("plate", 0.01, 10, Steel());
        layered.Id = 3;
        var plateFiber = new PlateFiberSection("plate", 0.01, Steel()) { Id = 3 };

        var cases = new[]
        {
            // Over the plastic load: the clamped root holds the strip across its width.
            new { Name = "quads, Layered Shell", Section = (ISerialize)layered, Triangles = false, Low = 1.05, High = 1.15 },
            new { Name = "triangles, Layered Shell", Section = (ISerialize)layered, Triangles = true, Low = 1.05, High = 1.20 },
            // Five Lobatto points through the thickness read a fully plastic section 9% low.
            new { Name = "quads, Plate Fiber", Section = (ISerialize)plateFiber, Triangles = false, Low = 0.97, High = 1.06 },
        };
        foreach (var item in cases)
        {
            string name = item.Name;
            var section = item.Section;
            bool triangles = item.Triangles;
            double low = item.Low, high = item.High;
            string file = "strip_" + name.Replace(", ", "_").Replace(" ", "") + ".tcl";
            File.WriteAllText(Path.Combine(dir, file), Strip(section, triangles));
            string log = Run(openSees, dir, file);
            var curve = Curve(log);
            double end = At(curve, 0.6), before = At(curve, 0.4);

            Check(!log.Contains("GAVEUP") && !double.IsNaN(end), $"{name}: reaches 60 times the elastic limit's deflection");
            Check(end / plastic > low && end / plastic < high, $"{name}: levels off at {end / plastic:F3} of the plastic load");
            // Elastic, the load would rise by half over the last third; the triangles, tied across the
            // strip by their diagonals, keep a little more slope than the quads.
            Check(!double.IsNaN(before) && (end - before) / end < 0.03, $"{name}: levelled, {100 * (end - before) / end:F2}% over the last third (elastic: 50%)");
            Check(!log.Contains("More than 25 iterations"), $"{name}: no iteration warnings from the material");
        }
    }

    /// <summary>
    /// The Plasticity example's plate with a hole, 400 x 200 x 10 mm, a hole 60 mm across, its mesh
    /// next to the test: ASD shells written by Alpaca4d itself, pulled in their own plane. Without
    /// -noeas the shells stop converging just past first yield; with Alpaca4d's flags the plate levels
    /// off where a plane stress model of it does, 509.8 kN, 2.5% over the net section's Fy x A.
    /// </summary>
    static void PlateWithHole(string openSees, string dir)
    {
        Console.WriteLine("A plate with a hole pulled in its own plane, as ASD shells");
        var lines = File.ReadAllText(Path.Combine(dir, "PlasticityPlateHole.txt")).Split('\n');
        int n = int.Parse(lines[0].Split(' ')[1]);
        var xyz = lines.Skip(1).Take(n).Select(l => l.Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToList();
        int m = int.Parse(lines[1 + n].Split(' ')[1]);
        var faces = lines.Skip(2 + n).Take(m).Select(l => l.Split(' ').Skip(1).Select(int.Parse).ToArray()).ToList();

        var section = LayeredShellSection.Uniform("plate", 0.01, 10, Steel());
        section.Id = 3;
        var deck = new StringBuilder("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
        deck.Append(Steel().WriteTcl());
        deck.Append(section.WriteTcl());
        for (int i = 0; i < n; i++)
        {
            deck.Append($"node {i + 1} {N(xyz[i][0])} {N(xyz[i][1])} {N(xyz[i][2])}\n");
            bool left = Math.Abs(xyz[i][0]) < 1e-9, middle = left && Math.Abs(xyz[i][1] - 0.1) < 1e-9;
            deck.Append($"fix {i + 1} {(left ? 1 : 0)} {(middle ? 1 : 0)} 1 0 0 0\n");
        }
        for (int k = 0; k < m; k++)
            deck.Append(Shell(faces[k].Length == 4, section, k + 1, faces[k].Select(v => v + 1).ToList()).WriteTcl());
        var right = Enumerable.Range(0, n).Where(i => Math.Abs(xyz[i][0] - 0.4) < 1e-9).OrderBy(i => xyz[i][1]).ToList();
        deck.Append("timeSeries Linear 1\npattern Plain 1 1 {\n");
        for (int k = 0; k < right.Count; k++)
        {
            double lo = k > 0 ? (xyz[right[k]][1] - xyz[right[k - 1]][1]) / 2.0 : 0.0;
            double hi = k < right.Count - 1 ? (xyz[right[k + 1]][1] - xyz[right[k]][1]) / 2.0 : 0.0;
            deck.Append($"  load {right[k] + 1} {N((lo + hi) / 0.2)} 0 0 0 0 0\n");
        }
        deck.Append("}\nconstraints Transformation; numberer RCM; system UmfPack\ntest NormDispIncr 1e-8 30 0; algorithm Newton\nanalysis Static\n");
        deck.Append(Drive);
        int control = right.OrderBy(i => Math.Abs(xyz[i][1] - 0.1)).First();
        deck.Append($"drive {control + 1} 1 0.004 0.0001\n");
        File.WriteAllText(Path.Combine(dir, "plate_with_hole.tcl"), deck.ToString());
        string log = Run(openSees, dir, "plate_with_hole.tcl");
        var curve = Curve(log);
        double end = At(curve, 0.004), net = Fy * (0.2 - 0.06) * 0.01;
        Check(!log.Contains("GAVEUP") && !double.IsNaN(end), "reaches 4 mm, ten times its deflection at first yield");
        Check(Math.Abs(end / 509.76 - 1) < 0.01, $"levels off at {end:F1} kN: the plane stress model's 509.8, {end / net:F3} of the net section's Fy x A");
    }

    /// <summary>
    /// A clamped beam 1 m long, 0.1 by 0.1 m, of ten node tetrahedra (each cube of the grid split into
    /// six) or SSP bricks, with the tip load spread over the tip face. Units kN, m.
    /// </summary>
    static string Beam(int nz, bool tetrahedra)
    {
        int ny = nz, nx = 10 * nz;
        double L = 1.0, b = 0.1, h = 0.1;
        var deck = new StringBuilder("wipe\nmodel BasicBuilder -ndm 3 -ndf 3\n");
        deck.Append(Steel().WriteTcl());

        var nodes = new Dictionary<string, int>();
        var xyz = new List<double[]> { null };
        Func<double, double, double, int> node = (x, y, z) =>
        {
            string key = $"{Math.Round(x * 1e6)}_{Math.Round(y * 1e6)}_{Math.Round(z * 1e6)}";
            if (!nodes.TryGetValue(key, out int id))
            {
                id = xyz.Count;
                nodes[key] = id;
                xyz.Add(new[] { x, y, z });
                deck.Append($"node {id} {N(x)} {N(y)} {N(z)}\n");
                if (Math.Abs(x) < 1e-12) deck.Append($"fix {id} 1 1 1\n");
            }
            return id;
        };
        Func<int, int, int, int> corner = (i, j, k) => node(i * L / nx, j * b / ny, k * h / nz);
        Func<int, int, int> mid = (p, q) => node(0.5 * (xyz[p][0] + xyz[q][0]), 0.5 * (xyz[p][1] + xyz[q][1]), 0.5 * (xyz[p][2] + xyz[q][2]));

        var cube = new[] { (0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (0, 0, 1), (1, 0, 1), (0, 1, 1), (1, 1, 1) };
        var kuhn = new[] { new[] { 0, 1, 3, 7 }, new[] { 0, 3, 2, 7 }, new[] { 0, 2, 6, 7 }, new[] { 0, 6, 4, 7 }, new[] { 0, 4, 5, 7 }, new[] { 0, 5, 1, 7 } };
        var edges = new[] { (0, 1), (1, 2), (2, 0), (0, 3), (1, 3), (2, 3) };
        var tipFaceLoad = new Dictionary<int, double>();
        int element = 1;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                for (int k = 0; k < nz; k++)
                {
                    var ids = cube.Select(c => corner(i + c.Item1, j + c.Item2, k + c.Item3)).ToArray();
                    if (!tetrahedra)
                    {
                        deck.Append($"element SSPbrick {element++} {ids[0]} {ids[1]} {ids[3]} {ids[2]} {ids[4]} {ids[5]} {ids[7]} {ids[6]} 1\n");
                        continue;
                    }
                    foreach (var t in kuhn)
                    {
                        var c4 = t.Select(q => ids[q]).ToArray();
                        if (Volume(c4.Select(n => xyz[n]).ToArray()) < 0) { var s = c4[1]; c4[1] = c4[2]; c4[2] = s; }
                        var all = c4.Concat(edges.Select(e => mid(c4[e.Item1], c4[e.Item2]))).ToArray();
                        deck.Append($"element TenNodeTetrahedronStd {element++} {string.Join(" ", all)} 1\n");
                    }
                }

        // The tip face, evenly. A bilinear quad face shares a uniform traction out as a quarter per
        // corner. A six node triangle puts it all on its edge nodes, a third of its area each: every
        // grid square of the tip face is two triangles across the diagonal through its middle.
        var tip = nodes.Where(kv => Math.Abs(xyz[kv.Value][0] - L) < 1e-9).Select(kv => kv.Value).ToList();
        foreach (var n in tip)
        {
            double y = xyz[n][1] / (b / ny), z = xyz[n][2] / (h / nz);        // in cells
            bool yBoundary = Math.Abs(y) < 1e-6 || Math.Abs(y - ny) < 1e-6, zBoundary = Math.Abs(z) < 1e-6 || Math.Abs(z - nz) < 1e-6;
            if (!tetrahedra)
            {
                tipFaceLoad[n] = (yBoundary ? 0.5 : 1.0) * (zBoundary ? 0.5 : 1.0);
                continue;
            }
            bool yHalf = Math.Abs(y - Math.Floor(y) - 0.5) < 1e-6, zHalf = Math.Abs(z - Math.Floor(z) - 0.5) < 1e-6;
            double third = (b / ny) * (h / nz) / 2.0 / 3.0;
            if (yHalf && zHalf)
                tipFaceLoad[n] = 2 * third;                                      // a square's diagonal: two triangles
            else if (yHalf)
                tipFaceLoad[n] = (zBoundary ? 1 : 2) * third;                    // a grid edge: a triangle each side
            else if (zHalf)
                tipFaceLoad[n] = (yBoundary ? 1 : 2) * third;
        }
        double total = tipFaceLoad.Values.Sum();
        deck.Append("timeSeries Linear 1\npattern Plain 1 1 {\n");
        foreach (var kv in tipFaceLoad.Where(kv => kv.Value > 0))
            deck.Append($"  load {kv.Key} 0 0 {N(kv.Value / total)}\n");
        deck.Append("}\n");
        int control = corner(nx, 0, nz / 2);
        deck.Append("constraints Transformation; numberer RCM; system UmfPack\ntest NormDispIncr 1e-8 50 0; algorithm Newton\nanalysis Static\n");
        deck.Append(Drive);
        deck.Append($"drive {control} 3 0.1 0.002\n");
        return deck.ToString();
    }

    static double Volume(double[][] p)
    {
        double[] a = { p[1][0] - p[0][0], p[1][1] - p[0][1], p[1][2] - p[0][2] };
        double[] c = { p[2][0] - p[0][0], p[2][1] - p[0][1], p[2][2] - p[0][2] };
        double[] e = { p[3][0] - p[0][0], p[3][1] - p[0][1], p[3][2] - p[0][2] };
        return a[0] * (c[1] * e[2] - c[2] * e[1]) - a[1] * (c[0] * e[2] - c[2] * e[0]) + a[2] * (c[0] * e[1] - c[1] * e[0]);
    }

    static void Beams(string openSees, string dir)
    {
        Console.WriteLine("A clamped steel beam of solids");

        double plastic = Fy * 0.1 * 0.1 * 0.1 / 4.0;              // 88.75 kN
        double I = 0.1 * Math.Pow(0.1, 3) / 12.0, A = 0.01, G = E / (2 * (1 + Nu));
        double elasticAt = 0.01 / (1.0 / (3 * E * I) + 1.0 / (5.0 / 6.0 * G * A));   // with shear: 52.1 kN

        File.WriteAllText(Path.Combine(dir, "beam_tet10.tcl"), Beam(2, true));
        var tet = Curve(Run(openSees, dir, "beam_tet10.tcl"));
        double tetElastic = At(tet, 0.01), tetEnd = At(tet, 0.1), tetBefore = At(tet, 0.07);
        Check(Math.Abs(tetElastic / elasticAt - 1) < 0.02, $"ten node tetrahedra, before yield: {tetElastic:F2} kN at L/100, beam theory {elasticAt:F2}");
        Check(tetEnd / plastic > 1.05 && tetEnd / plastic < 1.20, $"ten node tetrahedra, after yield: {tetEnd:F2} kN at L/10, {tetEnd / plastic:F3} of the plastic load");
        Check((tetEnd - tetBefore) / tetEnd < 0.02, $"ten node tetrahedra: levelled, {100 * (tetEnd - tetBefore) / tetEnd:F2}% over the last 3 cm");

        File.WriteAllText(Path.Combine(dir, "beam_ssp.tcl"), Beam(6, false));
        var ssp = Curve(Run(openSees, dir, "beam_ssp.tcl"));
        double sspEnd = At(ssp, 0.1), sspBefore = At(ssp, 0.07);
        Check(sspEnd > 1.2 * tetEnd && (sspEnd - sspBefore) / sspEnd > 0.04,
              $"SSP bricks, what Assemble warns of: {sspEnd:F1} kN at L/10 and still rising {100 * (sspEnd - sspBefore) / sspEnd:F1}%");
    }

    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";

        Writing();
        Reading();
        Logs();

        string openSees = Environment.GetEnvironmentVariable("OPENSEES");
        if (string.IsNullOrEmpty(openSees)) openSees = "OpenSees";
        string tet10 = Environment.GetEnvironmentVariable("OPENSEES_TET10");
        if (string.IsNullOrEmpty(tet10)) tet10 = "/Applications/OpenSeesLatest-Mumps/bin/OpenSees";

        try
        {
            Brick(openSees, dir);
            Shells(openSees, dir);
            PlateWithHole(openSees, dir);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"  note: no OpenSees at \"{openSees}\" (set OPENSEES); the brick and shell decks are skipped");
        }

        if (File.Exists(tet10))
            Beams(tet10, dir);
        else
            Console.WriteLine($"  note: no OpenSees at \"{tet10}\" (set OPENSEES_TET10); the tetrahedron decks are skipped");

        Console.WriteLine(fails == 0 ? "All passed." : $"{fails} failed.");
        return fails == 0 ? 0 : 1;
    }
}
