// The fibre laws of Concrete, Steel and Other Laws beside Concrete01, Steel01 and ReinforcingSteel: the Tcl
// each writes, and the same 300 x 500 rectangle run through Moment Curvature and Interaction Domain with each
// law in turn, in OpenSees, against the Concrete01 / Steel01 section they are set equal to.
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

class FibreLawsCheck
{
    static int fails = 0;
    static string OpenSees;
    static string Folder;

    static string F(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail == "" ? "" : "  " + detail)}");
        if (!ok) fails++;
    }

    static void Relative(string what, double got, double want, double tolerance) =>
        Check(what, Math.Abs(got - want) <= tolerance * Math.Abs(want), $"{F(got)} against {F(want)} (±{100 * tolerance:0.###}%)");

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

    static string Tcl(EntityBase material) => material.WriteTcl().Trim().Replace("  ", " ");

    // Tags are generated, so the checks read the line after the tag.
    static string After(string tcl) => string.Join(" ", tcl.Split(' ').Skip(3));

    static void TclCases()
    {
        Console.WriteLine("The Tcl each law writes");
        Check("Concrete02", After(Tcl(new Concrete02("c", -25000, -0.002, -20000, -0.0035, 0.1, 2600, 2e6, false))) == "-25000 -0.002 -20000 -0.0035 0.1 2600 2000000");
        Check("Concrete04 without tension", After(Tcl(new Concrete04("c", -25000, -0.002, -0.0035, 31e6, null, null, null, false))) == "-25000 -0.002 -0.0035 31000000");
        Check("Concrete04 with tension and beta", After(Tcl(new Concrete04("c", -25000, -0.002, -0.0035, 31e6, 2600, 0.0001, 0.5, false))) == "-25000 -0.002 -0.0035 31000000 2600 0.0001 0.5");
        bool threw = false;
        try { Tcl(new Concrete04("c", -25000, -0.002, -0.0035, 31e6, 2600, null, null, false)); } catch (InvalidOperationException) { threw = true; }
        Check("Concrete04 fct without et is refused", threw);
        Check("Steel02", After(Tcl(new Steel02("s", 355000, 210e6, 0.01, 18, 0.925, 0.15))) == "355000 210000000 0.01 18 0.925 0.15");
        Check("Steel02 with a1-a4", After(Tcl(new Steel02("s", 355000, 210e6, 0.01, 18, 0.925, 0.15) { A1 = 0.01, A2 = 1, A3 = 0.01, A4 = 1 })) == "355000 210000000 0.01 18 0.925 0.15 0.01 1 0.01 1");
        Check("Steel02 with sigInit alone writes a neutral a1-a4", After(Tcl(new Steel02("s", 355000, 210e6, 0.01, 18, 0.925, 0.15) { SigInit = 1000 })) == "355000 210000000 0.01 18 0.925 0.15 0 1 0 1 1000");
        Check("ElasticPP", After(Tcl(new ElasticPP("e", 210e6, 0.0017, null, null))) == "210000000 0.0017");
        Check("ElasticPP with epsyN", After(Tcl(new ElasticPP("e", 210e6, 0.0017, -0.002, null))) == "210000000 0.0017 -0.002");
        Check("Elastic", After(Tcl(new ElasticFibre("e", 160e6, null, null, null))) == "160000000");
        Check("Elastic with Eneg and rupture writes a MinMax", Tcl(new ElasticFibre("e", 160e6, 150e6, 0.017, null)).Contains("uniaxialMaterial MinMax") &&
              After(Tcl(new ElasticFibre("e", 160e6, 150e6, 0.017, null)).Split('\n')[0]) == "160000000 0 150000000");
        Check("Hysteretic mirrors the tension side", After(Tcl(new Hysteretic("h", new[] { 0.0017, 0.05 }, new[] { 355000.0, 450000.0 }))) ==
              "355000 0.0017 450000 0.05 -355000 -0.0017 -450000 -0.05 1 1 0 0");
        Check("Hysteretic trilinear", After(Tcl(new Hysteretic("h", new[] { 0.001, 0.01, 0.05 }, new[] { 100.0, 150.0, 160.0 }))) ==
              "100 0.001 150 0.01 160 0.05 -100 -0.001 -150 -0.01 -160 -0.05 1 1 0 0");
    }

    // ------------------------------------------------------------------ the rectangle

    const double fcd = 0.85 * 30000 / 1.5, fyd = 500000 / 1.15, Es = 200e6;

    sealed class Section
    {
        public SectionLayout Layout = new SectionLayout();
        public List<PointFiber> Fibres = new List<PointFiber>();

        public void Fill(double y, double z, double area, IMaterial m)
        {
            Layout.AddFibre(y, z, area, m);
            Fibres.Add(new PointFiber(new Point3d(y, z, 0), area, m));
        }

        public void Bar(double y, double z, double diameter, IMaterial m)
        {
            double area = Math.PI * diameter * diameter / 4;
            Layout.AddBar(y, z, area, m);
            Fibres.Add(new PointFiber(new Point3d(y, z, 0), area, m));
        }

        public FiberSection FiberSection() => new FiberSection(Fibres, new List<Layer>(), new List<Patch>(), 1e6);
    }

    static Section Rectangle(IMaterial concrete, IMaterial steel)
    {
        var s = new Section();
        double b = 0.30, h = 0.50, c = 0.05;
        int ny = 20, nz = 30;
        for (int i = 0; i < ny; i++)
            for (int j = 0; j < nz; j++)
                s.Fill(-b / 2 + (i + 0.5) * b / ny, -h / 2 + (j + 0.5) * h / nz, b * h / (ny * nz), concrete);
        foreach (var y in new[] { -b / 2, b / 2 })
            foreach (var z in new[] { -h / 2, h / 2 })
                s.Layout.AddCheckPoint(y, z, concrete);
        foreach (var y in new[] { -b / 2 + c, 0.0, b / 2 - c })
            s.Bar(y, -h / 2 + c, 0.020, steel);
        foreach (var y in new[] { -b / 2 + c, b / 2 - c })
            s.Bar(y, h / 2 - c, 0.016, steel);
        return s;
    }

    static MomentCurvatureResult Mc(Section s, string stem)
    {
        var r = MomentCurvatureAnalysis.Run(s.FiberSection(), s.Layout, -1000.0, 0.0, 300, null, new DomainSettings(), true, Folder, Folder, stem, deck => Run(deck));
        return r;
    }

    static double Ultimate(IMaterial concrete, IMaterial steel, string stem, out MomentCurvatureResult result)
    {
        result = Mc(Rectangle(concrete, steel), stem);
        return result.UltimateMoment;
    }

    static void Cases()
    {
        Console.WriteLine("The rectangle in Moment Curvature, each law in place of the Concrete01 / Steel01 pair");
        var concrete = new Concrete01("c01", -fcd, -fcd, -0.002, -0.0035, false);
        var steel = new Steel01("s01", fyd, Es, 0.0);
        double baseline = Ultimate(concrete, steel, "base", out var b);
        Check("the baseline stops at its ultimate state", b.StoppedAtUltimate && baseline > 0, $"{F(baseline)} kNm");

        var ok = new Action<string, IMaterial, IMaterial, double, double>((name, c, s, tolerance, want) =>
        {
            double m = Ultimate(c, s, name.Replace(' ', '_'), out var r);
            Check($"{name}: stops at its ultimate state, with a yield point", r.StoppedAtUltimate && r.YieldCurvature > 0, (r.Failure ?? "") + " " + string.Join("; ", r.Warnings) + " stopped=" + r.StoppedAtUltimate + " incomplete=" + r.Incomplete);
            Relative($"{name}: ultimate moment against Concrete01 / Steel01", m, want > 0 ? want : baseline, tolerance);
        });

        // Concrete02 with no tension strength and Concrete01's compression: the same concrete.
        var softening = new Concrete01("c01s", -fcd, -0.8 * fcd, -0.002, -0.0035, false);
        double softBase = Ultimate(softening, steel, "base_soft", out _);
        ok("Concrete02 with ft = 0", new Concrete02("c02", -fcd, -0.002, -0.8 * fcd, -0.0035, 0.2, 0.0, 1e6, false), steel, 0.01, softBase);
        ok("Concrete02 with a tensile strength", new Concrete02("c02", -fcd, -0.002, -0.8 * fcd, -0.0035, 0.2, 2600.0, 2e6, false), steel, 0.03, softBase);
        ok("Concrete04", new Concrete04("c04", -fcd, -0.002, -0.0035, 2 * fcd / 0.002, null, null, null, false), steel, 0.08, 0);
        ok("Steel02", concrete, new Steel02("s02", fyd, Es, 0.0, 18, 0.925, 0.15), 0.02, 0);
        ok("ElasticPP", concrete, new ElasticPP("epp", Es, fyd / Es, null, null), 0.001, 0);
        // A very stiff linear steel is the same section while the steel stays in range: ductility no longer reads, but the moment does.
        double mElastic = Ultimate(concrete, new ElasticFibre("el", Es, null, 0.02, 0.02), "elastic_fibre", out var re);
        Check("Elastic with a rupture strain: stops at its ultimate state", re.StoppedAtUltimate, re.Failure ?? "");
        Check("and carries more than the yielding steel (it never yields)", mElastic > baseline, F(mElastic) + " against " + F(baseline));
        ok("Hysteretic", concrete, new Hysteretic("hys", new[] { fyd / Es, 0.05 }, new[] { fyd, fyd * 1.0001 }), 0.001, 0);
        ok("Hysteretic, trilinear", concrete, new Hysteretic("hys3", new[] { fyd / Es, 0.02, 0.05 }, new[] { fyd, fyd * 1.001, fyd * 1.002 }), 0.001, 0);

        Console.WriteLine("Interaction Domain with a law of each kind");
        var layout = Rectangle(new Concrete02("c02", -fcd, -0.002, -fcd, -0.0035, 0.2, 0.0, 1e6, false), new Steel02("s02", fyd, Es, 0.0, 18, 0.925, 0.15)).Layout;
        var domain = InteractionDomain.Analyse(layout, new DomainSettings(), Folder, "domain", deck => Run(deck), out _, out _);
        var reference = InteractionDomain.Analyse(Rectangle(concrete, steel).Layout, new DomainSettings(), Folder, "domain_ref", deck => Run(deck), out _, out _);
        Check("the domain's materials: Concrete02 held to its crushing strain, Steel02 to the steel strain",
              domain.Materials.Any(m => m.IsConcrete && Math.Abs(m.CompressionLimit - 0.0035) < 1e-12) && domain.Materials.Any(m => !m.IsConcrete && m.YieldStrain > 0));
        Relative("the domain's pure compression, against Concrete01 / Steel01", domain.Nc, reference.Nc, 0.01);
    }

    static int Main(string[] args)
    {
        OpenSees = args.Length > 0 ? args[0] : "OpenSees";
        Folder = Path.Combine(Path.GetTempPath(), "alpaca4d_fibre_laws_" + Process.GetCurrentProcess().Id);
        Directory.CreateDirectory(Folder);
        try
        {
            TclCases();
            Cases();
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("KEEP") == null) { try { Directory.Delete(Folder, true); } catch { } } else Console.WriteLine("kept " + Folder);
        }
        Console.WriteLine(fails == 0 ? "All passed." : $"{fails} failed.");
        return fails == 0 ? 0 : 1;
    }
}
