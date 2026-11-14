// Interaction Domain: Alpaca4d.Section.InteractionDomain sweeps the ultimate strain planes of a fibre
// section, integrates the fibres with the stress-strain curves OpenSees traces, and checks design
// forces against the result. Every case runs the component's own path - MaterialCurves through
// OpenSees, then the sweep - against a hand calculation, against OpenSees' own fibre section, or
// against Alpaca4d's Moment Curvature deck. See README.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Alpaca4d;
using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Section;
using Rhino.Geometry;

class InteractionDomainCheck
{
    static int fails = 0;
    static string OpenSees;
    static string Folder;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail == "" ? "" : "  " + detail)}");
        if (!ok) fails++;
    }

    static void Near(string what, double got, double want, double tolerance)
    {
        bool ok = Math.Abs(got - want) <= tolerance;
        Check(what, ok, $"{F(got)} against {F(want)} (±{F(tolerance)})");
    }

    static void Relative(string what, double got, double want, double tolerance)
    {
        bool ok = Math.Abs(got - want) <= tolerance * Math.Abs(want);
        Check(what, ok, $"{F(got)} against {F(want)}, {Pct(got, want)} (±{100 * tolerance:0.###}%)");
    }

    static string F(double v) => v.ToString("G6", CultureInfo.InvariantCulture);
    static string Pct(double got, double want) => (100 * (got / want - 1)).ToString("+0.000;-0.000", CultureInfo.InvariantCulture) + "%";
    static string T(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    static (string output, string error, int exitCode) Run(string deckPath)
    {
        var start = new ProcessStartInfo(OpenSees, "\"" + deckPath + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true, WorkingDirectory = Path.GetDirectoryName(deckPath),
        };
        using (var p = Process.Start(start))
        {
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (stdout.Result, stderr, p.ExitCode);
        }
    }

    static InteractionDomain Domain(SectionLayout layout, DomainSettings settings, string stem)
    {
        var clock = Stopwatch.StartNew();
        var domain = InteractionDomain.Analyse(layout, settings, Folder, stem, deck => Run(deck), out _, out _);
        Console.WriteLine($"        ({stem}: {layout.Fibres.Count} fibres, {domain.Levels.Length} levels x {domain.Thetas.Length} points, {clock.ElapsedMilliseconds} ms)");
        return domain;
    }

    // ------------------------------------------------------------------ sections, kN and m

    // Eurocode 2 design values: C30/37 and B500, gamma_c = 1.5, alpha_cc = 0.85, gamma_s = 1.15.
    // Concrete01 with its residual equal to its peak is the parabola-rectangle, and Steel01 with no
    // hardening is elastic-perfectly plastic.
    const double fcd = 0.85 * 30000 / 1.5, fyd = 500000 / 1.15, Es = 200e6;

    static Concrete01 Concrete() => new Concrete01("C30/37 design", -fcd, -fcd, -0.002, -0.0035, false);
    static Steel01 Bars() => new Steel01("B500 design", fyd, Es, 0.0);

    sealed class Section
    {
        public SectionLayout Layout = new SectionLayout();
        public List<PointFiber> Fibres = new List<PointFiber>();
        public double FilledArea, SteelArea;

        public void Fill(double y, double z, double area, IMaterial material)
        {
            Layout.AddFibre(y, z, area, material);
            Fibres.Add(new PointFiber(new Point3d(y, z, 0), area, material));
            FilledArea += area;
        }

        public void Bar(double y, double z, double diameter, IMaterial material)
        {
            double area = Math.PI * diameter * diameter / 4;
            Layout.AddBar(y, z, area, material);
            Fibres.Add(new PointFiber(new Point3d(y, z, 0), area, material));
            SteelArea += area;
        }

        public FiberSection FiberSection() =>
            new FiberSection(Fibres, new List<Layer>(), new List<Patch>(), 1e6);
    }

    /// <summary>300 x 500 mm, 3 phi 20 at the bottom and 2 phi 16 at the top, 50 mm to the bars' centres.</summary>
    static Section Rectangle(out Concrete01 concrete, out Steel01 steel)
    {
        concrete = Concrete();
        steel = Bars();
        var s = new Section();
        double b = 0.30, h = 0.50, c = 0.05;
        int ny = 30, nz = 50;
        for (int i = 0; i < ny; i++)
            for (int j = 0; j < nz; j++)
                s.Fill(-b / 2 + (i + 0.5) * b / ny, -h / 2 + (j + 0.5) * h / nz, b * h / (ny * nz), concrete);
        // The concrete's outline, which a patch's mesh vertices would give.
        foreach (var y in new[] { -b / 2, b / 2 })
            foreach (var z in new[] { -h / 2, h / 2 })
                s.Layout.AddCheckPoint(y, z, concrete);
        foreach (var y in new[] { -b / 2 + c, 0.0, b / 2 - c })
            s.Bar(y, -h / 2 + c, 0.020, steel);
        foreach (var y in new[] { -b / 2 + c, b / 2 - c })
            s.Bar(y, h / 2 - c, 0.016, steel);
        return s;
    }

    /// <summary>An L of two 250 x 800 mm legs, phi 20 bars round it: unsymmetric about both axes.</summary>
    static Section LShape()
    {
        var concrete = Concrete();
        var steel = Bars();
        var s = new Section();
        double t = 0.25, L = 0.80, c = 0.05;
        int n = 40;
        double d = L / n;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double y = (i + 0.5) * d, z = (j + 0.5) * d;
                if (y < t || z < t)
                    s.Fill(y, z, d * d, concrete);
            }
        foreach (var p in new[] { (0.0, 0.0), (L, 0.0), (L, t), (t, t), (t, L), (0.0, L) })
            s.Layout.AddCheckPoint(p.Item1, p.Item2, concrete);
        for (int k = 0; k < 7; k++)
        {
            double along = c + k * (L - 2 * c) / 6;
            s.Bar(c, along, 0.020, steel);
            if (along > t)
            {
                s.Bar(t - c, along, 0.020, steel);
                s.Bar(along, c, 0.020, steel);
                s.Bar(along, t - c, 0.020, steel);
            }
        }
        s.Bar(t - c, t - c, 0.020, steel);
        return s;
    }

    // HEA 300, height along z so that y is the strong axis, as in EN 1993.
    const double Hh = 0.290, Hb = 0.300, Htw = 0.0085, Htf = 0.014, Hr = 0.027, fy = 355000, E = 210e6;

    static void Rect(Section s, IMaterial material, double y0, double y1, double z0, double z1, int ny, int nz)
    {
        double dy = (y1 - y0) / ny, dz = (z1 - z0) / nz;
        for (int i = 0; i < ny; i++)
            for (int j = 0; j < nz; j++)
                s.Fill(y0 + (i + 0.5) * dy, z0 + (j + 0.5) * dz, dy * dz, material);
    }

    /// <summary>Flanges, web and the four root fillets, in fibres; held to its limits at the flange tips.</summary>
    static Section Hea300(Steel01 steel, bool outline = true)
    {
        var s = new Section();
        Rect(s, steel, -Hb / 2, Hb / 2, Hh / 2 - Htf, Hh / 2, 75, 5);
        Rect(s, steel, -Hb / 2, Hb / 2, -Hh / 2, -Hh / 2 + Htf, 75, 5);
        Rect(s, steel, -Htw / 2, Htw / 2, -Hh / 2 + Htf, Hh / 2 - Htf, 4, 66);
        double dr = 0.0005;
        int m = (int)Math.Round(Hr / dr);
        foreach (var sy in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double cy = sy * (Htw / 2 + Hr), cz = sz * (Hh / 2 - Htf - Hr);
                for (int i = 0; i < m; i++)
                    for (int j = 0; j < m; j++)
                    {
                        double y = sy * (Htw / 2 + (i + 0.5) * dr), z = sz * (Hh / 2 - Htf - (j + 0.5) * dr);
                        if ((y - cy) * (y - cy) + (z - cz) * (z - cz) > Hr * Hr)
                            s.Fill(y, z, dr * dr, steel);
                    }
            }
        if (outline)
            foreach (var y in new[] { -Hb / 2, Hb / 2 })
                foreach (var z in new[] { -Hh / 2, Hh / 2 })
                    s.Layout.AddCheckPoint(y, z, steel);
        return s;
    }

    // ------------------------------------------------------------------ OpenSees' own section

    /// <summary>
    /// Each plane imposed on a zeroLengthSection of the same fibres, and the section forces
    /// OpenSees gives back: P, Mz, My.
    /// </summary>
    static List<double[]> OpenSeesForces(Section section, IEnumerable<IMaterial> materials, IList<(double e0, double ky, double kz)> planes)
    {
        string output = Path.Combine(Folder, "planes.out");
        if (File.Exists(output)) File.Delete(output);

        var tcl = new System.Text.StringBuilder();
        tcl.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
        foreach (var material in materials)
            tcl.Append(material.WriteTcl());
        tcl.Append(section.FiberSection().WriteTcl(1));
        tcl.Append("node 1 0 0 0\nnode 2 0 0 0\nfix 1 1 1 1 1 1 1\nfix 2 0 1 1 0 0 0\n");
        tcl.Append("element zeroLengthSection 1 1 2 1\n");
        tcl.Append("system BandGeneral\nnumberer Plain\nconstraints Transformation\ntest NormDispIncr 1e-10 50\n");
        tcl.Append("algorithm Newton\nintegrator LoadControl 0.05\nanalysis Static\n");
        tcl.Append($"set out [open {TclPath.Write(output)} w]\n");
        for (int i = 0; i < planes.Count; i++)
        {
            var (e0, ky, kz) = planes[i];
            tcl.Append($"timeSeries Linear {i + 1}\n");
            tcl.Append($"pattern Plain {i + 1} {i + 1} {{\n\tsp 2 1 {T(e0)}\n\tsp 2 5 {T(ky)}\n\tsp 2 6 {T(kz)}\n}}\n");
            tcl.Append("analyze 20\nputs $out [eleResponse 1 section force]\n");
            tcl.Append($"remove loadPattern {i + 1}\nreset\nsetTime 0.0\n");
        }
        tcl.Append("close $out\nwipe\n");

        string deck = Path.Combine(Folder, "planes.tcl");
        File.WriteAllText(deck, tcl.ToString());
        Run(deck);

        return File.ReadAllLines(output)
            .Select(line => line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(TclNumber.Read).ToArray())
            .ToList();
    }

    static void AgainstOpenSees(string name, Section section, InteractionDomain domain, IEnumerable<IMaterial> materials)
    {
        var random = new Random(1);
        var planes = new List<(double, double, double)>();
        var ours = new List<DomainPoint>();
        for (int i = 0; i < 24; i++)
        {
            domain.Ultimate(random.NextDouble() * Math.PI, random.NextDouble() * 2 * Math.PI, out double e0, out double ky, out double kz);
            planes.Add((e0, ky, kz));
            ours.Add(domain.Forces(e0, ky, kz));
        }

        var theirs = OpenSeesForces(section, materials, planes);
        Check($"{name}: OpenSees answered every plane", theirs.Count == planes.Count, $"{theirs.Count} of {planes.Count}");
        if (theirs.Count != planes.Count)
            return;

        double worst = 0;
        for (int i = 0; i < planes.Count; i++)
        {
            worst = Math.Max(worst, Math.Abs(ours[i].N - theirs[i][0]));
            worst = Math.Max(worst, Math.Abs(ours[i].Mz - theirs[i][1]));
            worst = Math.Max(worst, Math.Abs(ours[i].My - theirs[i][2]));
        }
        Near($"{name}: 24 ultimate planes, N My Mz against a zeroLengthSection, worst difference", worst, 0.0, 0.05);
    }

    // ------------------------------------------------------------------ Alpaca4d's Moment Curvature

    /// <summary>
    /// Moment Curvature's own deck, N held and the curvature about y pushed, read up to where the
    /// first fibre reaches its limit: My there, with Mz free and so zero.
    /// </summary>
    static double MomentCurvatureAtLimit(Section section, InteractionDomain domain, double n, double maxPhi, int steps)
    {
        string previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Folder);
            Alpaca4d.Template.MomentCurvature.PrepareOutputFolder();
            string tcl = Alpaca4d.Template.MomentCurvature.Define(section.FiberSection(), n, "y", maxPhi, steps);
            string deck = Path.Combine(Folder, "MomentCurvature.tcl");
            File.WriteAllText(deck, tcl);
            Run(deck);

            var force = File.ReadAllLines(Path.Combine(Folder, Alpaca4d.Template.MomentCurvature.ForceFilePath))
                .Select(l => l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(TclNumber.Read).ToArray()).ToList();
            var deformation = File.ReadAllLines(Path.Combine(Folder, Alpaca4d.Template.MomentCurvature.DeformationFilePath))
                .Select(l => l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(TclNumber.Read).ToArray()).ToList();

            double lastRatio = 0, lastMy = 0;
            for (int i = 0; i < Math.Min(force.Count, deformation.Count); i++)
            {
                // Deformations come as e0, kz, ky; forces as P, Mz, My.
                double ratio = domain.UltimateRatio(deformation[i][0], deformation[i][2], deformation[i][1]);
                double my = force[i][2];
                if (ratio >= 1.0)
                    return lastMy + (my - lastMy) * (1.0 - lastRatio) / (ratio - lastRatio);
                lastRatio = ratio;
                lastMy = my;
            }
            return double.NaN;
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    /// <summary>
    /// Every point of the surface is on an ultimate plane: a fibre at its limit, none past it. A
    /// plane stopped short of its limits lies inside the domain, on the safe side, and so passes
    /// every comparison with a reading taken off the same planes.
    /// </summary>
    static void AtLimits(string name, InteractionDomain domain)
    {
        var ratios = domain.Grid.SelectMany(row => row)
            .Select(p => domain.UltimateRatio(p.Axial, p.CurvatureY, p.CurvatureZ)).ToList();
        int off = ratios.Count(r => Math.Abs(r - 1.0) > 1e-9);
        Check($"{name}: every point of the surface has a fibre at its limit", off == 0,
              $"{ratios.Count - off} of {ratios.Count}, ratios {F(ratios.Min())} to {F(ratios.Max())}");
    }

    static double FarMy(InteractionDomain domain, double n)
    {
        domain.LineHits(n, 0.0, out _, out var far);
        return far.My;
    }

    // ------------------------------------------------------------------ cases

    static void RectangleCases()
    {
        Console.WriteLine("Rectangle 300 x 500, C30/37 and B500, 3 phi 20 + 2 phi 16");
        var s = Rectangle(out var concrete, out var steel);
        var domain = Domain(s.Layout, new DomainSettings(), "rectangle");

        Relative("pure compression: fcd Ac + Es ec2 As", domain.Nc, -(fcd * s.FilledArea + Math.Min(fyd, Es * 0.002) * s.SteelArea), 1e-6);
        Relative("pure tension: fyd As", domain.Nt, fyd * s.SteelArea, 1e-9);
        Check("the concrete is held to ecu2 and ec2, the bars to eud",
              domain.Materials.Any(m => m.IsConcrete && Math.Abs(m.CompressionLimit - 0.0035) < 1e-12 && Math.Abs(m.PivotStrain - 0.002) < 1e-12) &&
              domain.Materials.Any(m => !m.IsConcrete && Math.Abs(m.TensionLimit - 0.01) < 1e-12),
              string.Join("; ", domain.Materials));

        AgainstOpenSees("rectangle", s, domain, new IMaterial[] { concrete, steel });

        // The bars are 50 mm inside the edge, so where they reach eud the concrete's edge is strained
        // further still - past the curves' end, which used to stop the plane short.
        AtLimits("rectangle", domain);
        domain.LineHits(0.0, 0.0, out _, out var bending);
        Check("N = 0, positive My: the bars in tension govern, at eud",
              domain.Governs(bending.Axial, bending.CurvatureY, bending.CurvatureZ).Contains("+0.01 in tension"),
              domain.Governs(bending.Axial, bending.CurvatureY, bending.CurvatureZ));

        foreach (var n in new[] { -1000.0, 0.0 })
            Near($"N = {n}: Moment Curvature's deck at its first limit against the Mz = 0 cut",
                 MomentCurvatureAtLimit(s, domain, n, 0.08, 4000), FarMy(domain, n), 0.2);

        // The rectangle is symmetric about z, so My alone bends it and the cut holds every level's
        // widest point: the Mz = 0 cut through a level meets it where the sweep's own points do.
        var level = domain.Contour(-1000.0);
        double widest = level.Max(p => p.My);
        Relative("the widest point of the N = -1000 level against the Mz = 0 cut", widest, FarMy(domain, -1000.0), 2e-3);
    }

    static void LCases()
    {
        Console.WriteLine("L of two 250 x 800 legs, unsymmetric about both axes");
        var s = LShape();
        var materials = s.Fibres.Select(f => f.Material).Distinct().ToList();
        var domain = Domain(s.Layout, new DomainSettings(), "lshape");

        Relative("pure compression", domain.Nc, -(fcd * s.FilledArea + Math.Min(fyd, Es * 0.002) * s.SteelArea), 1e-6);
        Relative("pure tension", domain.Nt, fyd * s.SteelArea, 1e-9);
        AgainstOpenSees("L", s, domain, materials);
        AtLimits("L", domain);

        // With Mz left free the neutral axis turns as the section bends; the deck ends on the
        // Mz = 0 cut, which is not where a plane curving about y alone ends.
        Near("N = -1000: Moment Curvature's deck, its neutral axis turning, against the Mz = 0 cut",
             MomentCurvatureAtLimit(s, domain, -1000.0, 0.05, 4000), FarMy(domain, -1000.0), 0.5);
    }

    static void SteelCases()
    {
        Console.WriteLine("HEA 300, S355, steel only");
        var steel = new Steel01("S355", fy, E, 0.0);
        var s = Hea300(steel);

        double A = s.FilledArea;
        double wply = s.Layout.Fibres.Sum(f => f.Area * Math.Abs(f.Z));
        double wplz = s.Layout.Fibres.Sum(f => f.Area * Math.Abs(f.Y));
        double wely = s.Layout.Fibres.Sum(f => f.Area * f.Z * f.Z) / (Hh / 2);
        double welz = s.Layout.Fibres.Sum(f => f.Area * f.Y * f.Y) / (Hb / 2);
        Relative("A against the tables, 112.5 cm2", A, 112.5e-4, 2e-3);
        Relative("Wpl,y against the tables, 1383 cm3", wply, 1383e-6, 2e-3);
        Relative("Wpl,z against the tables, 641.2 cm3", wplz, 641.2e-6, 2e-3);

        var plastic = Domain(s.Layout, new DomainSettings { SteelUltimateStrain = 0.05 }, "hea_plastic");
        Relative("eud = 5%: N = A fy", -plastic.Nc, A * fy, 1e-9);
        Relative("eud = 5%: My at N = 0 against Wpl,y fy", FarMy(plastic, 0.0), wply * fy, 3e-3);
        plastic.LineHits(0.0, Math.PI / 2, out _, out var mz);
        // About z the 8.5 mm web lies inside the elastic core, 5 mm either side of the axis at eud =
        // 5%, and carries two thirds of its plastic share: Mpl,z is approached from below.
        Check("eud = 5%: Mz at N = 0 within 0.5% under Wpl,z fy", mz.Mz <= wplz * fy && mz.Mz >= 0.995 * wplz * fy,
              $"{F(mz.Mz)} against {F(wplz * fy)}, {Pct(mz.Mz, wplz * fy)}");

        // EN 1993-1-1 6.2.9.1: no reduction of Mpl,y while n <= a/2; the exact plastic domain loses
        // the web's share at once, M = Mpl - N^2 / (4 tw fy) while the web carries all of N.
        double nWeb = -0.5 * (Hh - 2 * Htf) * Htw * fy * 0.5;
        Relative("eud = 5%: My at N in the web against Mpl,y - N^2/(4 tw fy)", FarMy(plastic, nWeb),
                 wply * fy - nWeb * nWeb / (4 * Htw * fy), 1e-3);

        var cut = plastic.Cut(0.0);
        Relative("the N-My cut reaches Mpl,y", cut.Max(p => p.My), wply * fy, 1e-3);
        Relative("and is symmetric", -cut.Min(p => p.My), cut.Max(p => p.My), 2e-3);

        var elastic = Domain(s.Layout, new DomainSettings { SteelUltimateStrain = fy / E }, "hea_elastic");
        Relative("eud = fy/E: My at N = 0 against Wel,y fy", FarMy(elastic, 0.0), wely * fy, 1e-3);
        elastic.LineHits(0.0, Math.PI / 2, out _, out var mzel);
        Relative("eud = fy/E: Mz at N = 0 against Wel,z fy", mzel.Mz, welz * fy, 1e-3);
        Relative("against the tables, Wel,y 1260 cm3", FarMy(elastic, 0.0), 1260e-6 * fy, 3e-3);

        double worst = 0;
        foreach (var p in elastic.Contour(-1000.0, 36))
            worst = Math.Max(worst, Math.Abs(-p.N / (A * fy) + Math.Abs(p.My) / (wely * fy) + Math.Abs(p.Mz) / (welz * fy) - 1));
        Near("eud = fy/E: N/Npl + My/Mel,y + Mz/Mel,z = 1 round the N = -1000 level", worst, 0.0, 2e-3);

        // Checked at its fibres instead of its outline, first yield is found half a fibre inside the
        // edge, and the elastic moment comes out high.
        var inner = Domain(Hea300(steel, outline: false).Layout, new DomainSettings { SteelUltimateStrain = fy / E }, "hea_fibres");
        Check("checked at the fibres alone, Mel,y comes out high - why patches are checked at their outline",
              FarMy(inner, 0.0) > 1.005 * wely * fy, $"{F(FarMy(inner, 0.0))} against {F(wely * fy)}");

        Console.WriteLine("Checks against the HEA 300 plastic domain");
        plastic.LineHits(-1000.0, Math.PI / 6, out _, out var edge);
        var half = plastic.Check(-1000.0, 0.5 * edge.My, 0.5 * edge.Mz, DomainCheckMethod.ConstantAxial);
        Relative("N held, half the moment of the boundary: safety factor 2", half.SafetyFactor, 2.0, 1e-6);
        Relative("and the capacity is the boundary point", half.CapacityMy, edge.My, 1e-6);

        var ray = plastic.Check(0.5 * edge.N, 0.5 * edge.My, 0.5 * edge.Mz, DomainCheckMethod.Proportional);
        Relative("all three halved from a boundary point: proportional safety factor 2", ray.SafetyFactor, 2.0, 1e-5);

        var over = plastic.Check(1.1 * plastic.Nc, 10.0, 0.0, DomainCheckMethod.ConstantAxial);
        Check("N past the axial resistance: factor 0, and a note", over.SafetyFactor == 0.0 && over.Note != null, over.Note ?? "");

        var axial = plastic.Check(0.5 * plastic.Nc, 0.0, 0.0, DomainCheckMethod.ConstantAxial);
        Relative("no moment, N held: the factor goes on N instead", axial.SafetyFactor, 2.0, 1e-5);
        Check("and the note says so", axial.Method == DomainCheckMethod.Proportional && axial.Note != null, axial.Note ?? "");

        var outside = plastic.Check(-1000.0, 1.2 * edge.My, 1.2 * edge.Mz, DomainCheckMethod.ConstantAxial);
        Relative("1.2 times the boundary moment: utilisation 1.2", outside.Utilisation, 1.2, 1e-6);
        Check("and not safe", !outside.IsSafe);
    }

    // ------------------------------------------------------------------ Moment Curvature, read against the domain

    static MomentCurvatureResult Mc(Section s, double n, double degrees, int steps, double? maxPhi = null,
                                    DomainSettings limits = null, bool stop = true)
    {
        var clock = Stopwatch.StartNew();
        var r = MomentCurvatureAnalysis.Run(s.FiberSection(), s.Layout, n, degrees * Math.PI / 180, steps, maxPhi,
                                            limits ?? new DomainSettings(), stop, Folder, Folder, "mc_materials", deck => Run(deck));
        Console.WriteLine($"        (N {n}, {degrees} deg: {r.N.Count} increments of {steps}, MaxPhi {F(r.MaxPhi)}{(r.MaxPhiFromDomain ? " from the domain" : "")}, {clock.ElapsedMilliseconds} ms)");
        return r;
    }

    static double Far(InteractionDomain domain, double n, double degrees)
    {
        double a = degrees * Math.PI / 180;
        domain.LineHits(n, a, out _, out var far);
        return Math.Cos(a) * far.My + Math.Sin(a) * far.Mz;
    }

    static void MomentCurvatureCases()
    {
        Console.WriteLine("Moment Curvature on the rectangle, stopped at its ultimate strain");
        var s = Rectangle(out _, out _);
        var domain = Domain(s.Layout, new DomainSettings(), "rectangle_mc");

        var r = Mc(s, -1000.0, 0.0, 400);
        Check("it stopped itself at the ultimate state", r.StoppedAtUltimate && !r.Incomplete);
        Check("MaxPhi from the domain leaves it most of its increments", r.N.Count > 300 && r.N.Count < 400, $"{r.N.Count} of 400");
        Relative("ultimate moment against the domain's N-My cut", r.UltimateMoment, Far(domain, -1000.0, 0.0), 3e-3);
        Check("the concrete governs, and says where", r.Failure != null && r.Failure.Contains("crushes"), r.Failure ?? "");
        // The right-hand rule about y and z, with x = y x z: positive My stretches +z and crushes -z.
        Check("positive My crushes the -z side", r.Failure != null && r.Failure.Contains("z = -0.25"), r.Failure ?? "");
        Check("first yield comes before the ultimate state", r.YieldCurvature > 0 && r.YieldCurvature < r.UltimateCurvature && r.Ductility > 1,
              $"κy {F(r.YieldCurvature)}, κu {F(r.UltimateCurvature)}, ductility {F(r.Ductility)}");

        foreach (var degrees in new[] { 30.0, 90.0 })
        {
            var t = Mc(s, -1000.0, degrees, 400);
            double a = degrees * Math.PI / 180;
            int last = t.N.Count - 1;
            double across = -Math.Sin(a) * t.My[last] + Math.Cos(a) * t.Mz[last];
            Near($"{degrees} deg: the moment stays in its direction, the moment across it at the end", across, 0.0, 1e-5 * Math.Abs(t.Moment[last]) + 1e-9);
            Relative($"{degrees} deg: ultimate moment against the domain's cut at {degrees} deg", t.UltimateMoment, Far(domain, -1000.0, degrees), 3e-3);
            if (degrees == 90.0)
                Check("positive Mz crushes the +y side", t.Failure != null && t.Failure.Contains("y = 0.15,"), t.Failure ?? "");
        }

        // The other way about y: the 2 phi 16 now in tension, the 3 phi 20 in compression.
        var hogging = Mc(s, -1000.0, 180.0, 400);
        Relative("180 deg: ultimate moment against the domain's cut at 180 deg", hogging.UltimateMoment, Far(domain, -1000.0, 180.0), 3e-3);
        Check("and it is not the moment the other way, the bars being unequal",
              Math.Abs(hogging.UltimateMoment / r.UltimateMoment - 1) > 0.02,
              $"{F(hogging.UltimateMoment)} against {F(r.UltimateMoment)} at 0 deg");
        var down = Mc(s, -1000.0, -90.0, 400);
        Relative("-90 deg: ultimate moment against the domain's cut at -90 deg", down.UltimateMoment, Far(domain, -1000.0, -90.0), 3e-3);

        var full = Mc(s, -1000.0, 0.0, 200, maxPhi: 2.0 * r.UltimateCurvature, stop: false);
        Check("told not to stop, it runs to MaxPhi and still reads the ultimate state",
              full.N.Count == 200 && !full.StoppedAtUltimate && Math.Abs(full.UltimateMoment / r.UltimateMoment - 1) < 5e-3,
              $"{full.N.Count} increments, Mu {F(full.UltimateMoment)}");

        bool threw = false;
        try { Mc(s, 1.1 * domain.Nc, 0.0, 100); } catch (InvalidOperationException) { threw = true; }
        Check("an axial force past the section's resistance is refused before anything runs", threw);

        Check("Direction reads y, z and degrees", Alpaca4d.Template.MomentCurvature.Direction("y") == 0.0 &&
              Math.Abs(Alpaca4d.Template.MomentCurvature.Direction("Z") - Math.PI / 2) < 1e-15 &&
              Math.Abs(Alpaca4d.Template.MomentCurvature.Direction("30") - Math.PI / 6) < 1e-15);
        threw = false;
        try { Alpaca4d.Template.MomentCurvature.Direction("x"); } catch (ArgumentException) { threw = true; }
        Check("and refuses anything else", threw);

        Console.WriteLine("Moment Curvature on the L, bent at 45 deg, its neutral axis turning");
        var l = LShape();
        var lDomain = Domain(l.Layout, new DomainSettings(), "lshape_mc");
        var lr = Mc(l, -1000.0, 45.0, 400);
        Relative("ultimate moment against the domain's cut at 45 deg", lr.UltimateMoment, Far(lDomain, -1000.0, 45.0), 3e-3);

        Console.WriteLine("Moment Curvature on the HEA 300, N = 0, eud = 5%");
        var steel = new Steel01("S355", fy, E, 0.0);
        var h = Hea300(steel);
        double iy = h.Layout.Fibres.Sum(f => f.Area * f.Z * f.Z);
        double wply = h.Layout.Fibres.Sum(f => f.Area * Math.Abs(f.Z));
        var hr = Mc(h, 0.0, 0.0, 500, limits: new DomainSettings { SteelUltimateStrain = 0.05 });
        Relative("first yield at Wel,y fy", hr.YieldMoment, fy * iy / (Hh / 2), 1e-4);
        Relative("at the curvature fy/E over half the depth", hr.YieldCurvature, fy / E / (Hh / 2), 2e-3);
        Relative("its secant stiffness is E Iy", hr.YieldStiffness, E * iy, 1e-4);
        Relative("ultimate at eud over half the depth", hr.UltimateCurvature, 0.05 / (Hh / 2), 2e-3);
        Relative("with Mpl,y", hr.UltimateMoment, wply * fy, 3e-3);
        Relative("ductility eud / ey", hr.Ductility, 0.05 / (fy / E), 3e-3);
        Check("the steel governs, in tension or compression at a flange", hr.Failure != null && hr.Failure.Contains("S355"), hr.Failure ?? "");
    }

    static void MinMaxCases()
    {
        Console.WriteLine("MinMax");
        var steel = Bars();
        steel.MinMax = new MinMax("bars", steel, -0.005, 0.005);
        var s = new Section();
        s.Bar(0.0, -0.1, 0.020, steel);
        s.Bar(0.0, 0.1, 0.020, steel);
        var domain = Domain(s.Layout, new DomainSettings(), "minmax");
        var limit = domain.Materials.Single().TensionLimit;
        Check("a MinMax bound tighter than eud is the limit, just inside it", limit < 0.005 && limit > 0.0049, F(limit * 1000) + " per mille");
        Relative("and the bars still yield before it", domain.Nt, fyd * s.SteelArea, 1e-9);
    }

    static int Main(string[] args)
    {
        OpenSees = args.Length > 0 ? args[0] : "OpenSees";
        Folder = Path.Combine(Path.GetTempPath(), "alpaca4d_domain_" + Process.GetCurrentProcess().Id);
        Directory.CreateDirectory(Folder);
        try
        {
            RectangleCases();
            LCases();
            SteelCases();
            MinMaxCases();
            MomentCurvatureCases();
        }
        finally
        {
            try { Directory.Delete(Folder, true); } catch { }
        }

        Console.WriteLine(fails == 0 ? "All passed." : $"{fails} failed.");
        return fails == 0 ? 0 : 1;
    }
}
