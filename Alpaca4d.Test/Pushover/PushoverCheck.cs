// Pushover: Alpaca4d.Pushover writes the analysis that follows a model's deck - its loads held, then a
// lateral pattern scaled under displacement control - and reads the capacity curve back; a
// FiberSection is a beam section, with an area, inertias and a stand-in material worked out from its
// fibres. Checked against hand calculations on a cantilever column. See README.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Alpaca4d;
using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Section;
using Rhino.Geometry;

class PushoverCheck
{
    static int fails = 0;
    static string OpenSees;
    static string Folder;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail == "" ? "" : "  " + detail)}");
        if (!ok) fails++;
    }

    static void Relative(string what, double got, double want, double tolerance)
    {
        bool ok = Math.Abs(got - want) <= tolerance * Math.Abs(want);
        Check(what, ok, $"{F(got)} against {F(want)}, {Pct(got, want)} (±{100 * tolerance:0.###}%)");
    }

    static string Ending(PushoverCurve curve) =>
        curve.ReachedTarget ? "reached" : curve.Cut ? "cut where a section was lost" : curve.Stopped ? "stopped" : "neither reached nor stopped";

    static string F(double v) => v.ToString("G6", CultureInfo.InvariantCulture);
    static string Pct(double got, double want) => (100 * (got / want - 1)).ToString("+0.000;-0.000", CultureInfo.InvariantCulture) + "%";

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

    // ------------------------------------------------------------------ kN and m

    const double Fy = 355000.0, E = 210000000.0, L = 3.0;
    const double FibreArea = 0.0025, Lever = 0.15, Side = 0.05;

    /// <summary>
    /// Four fibres, two a side at y = ±0.15 and z = ±0.05: a flange each side, bent about z by a push
    /// along local y. Drawn off the origin, at (dx, dy), to show the beam's axis goes through the
    /// centroid all the same.
    /// </summary>
    static FiberSection TwoFlanges(IMaterial steel, double dx = 0.0, double dy = 0.0)
    {
        var fibres = new List<PointFiber>();
        foreach (var y in new[] { Lever, -Lever })
            foreach (var z in new[] { Side, -Side })
                fibres.Add(new PointFiber(new Point3d(y + dx, z + dy, 0.0), FibreArea, steel));
        return new FiberSection(fibres, new List<Layer>(), new List<Patch>(), 1e8);
    }

    /// <summary>
    /// A cantilever column along global Z, 3 m, fixed at the bottom. The transformation's vecxz is global
    /// X, so local z is global X and local y is -global Y: a push along global Y bends it about local z,
    /// against the two flanges. <paramref name="axial"/> kN on top, compression negative, is the model's
    /// own load.
    /// </summary>
    static PushoverCurve Column(string name, FiberSection section, string transformation, double axial, double target,
                                int steps, out string log, out string deck)
    {
        string logPath = Path.Combine(Folder, name + ".log");
        var tcl = new List<string>
        {
            Pushover.LogTcl(logPath),
            "wipe\n",
            "model BasicBuilder -ndm 3 -ndf 6\n",
            "node 1 0 0 0\n",
            $"node 2 0 0 {L}\n",
            "fix 1 1 1 1 1 1 1\n",
        };
        tcl.AddRange(section.Materials.Select(m => m.WriteTcl()));
        tcl.Add(section.WriteTcl());
        tcl.Add($"geomTransf {transformation} 1 1 0 0\n");
        tcl.Add($"element forceBeamColumn 1 1 2 1 NewtonCotes {section.Id} 5\n");
        if (axial != 0.0)
        {
            tcl.Add("timeSeries Linear 7\n");
            tcl.Add($"pattern Plain 7 7 {{\n    load 2 0 0 {TclNumber.Write(axial)} 0 0 0\n}}\n");
        }

        int tag = Pushover.FreeTag(tcl);
        string curvePath = Path.Combine(Folder, name + ".out");
        var loads = new[] { new PushoverLoad(2, 6, 0.0, 1.0, 0.0) };
        tcl.Add(Pushover.AnalysisTcl(2, 2, target, steps, 10, loads, tag, curvePath, logPath));

        deck = string.Concat(tcl);
        string deckPath = Path.Combine(Folder, name + ".tcl");
        File.WriteAllText(deckPath, deck);
        var (output, error, exitCode) = Run(deckPath);
        var curve = Pushover.Read(curvePath, logPath, output + "\n" + error, 10, target);
        log = curve.Log;
        Check($"{name}: the deck ran to its end", exitCode == 0 && !log.Contains("invalid command") && (curve.ReachedTarget || curve.Stopped || curve.Cut),
              $"exit {exitCode}, {Ending(curve)}");
        Check($"{name}: what OpenSees printed was all sent to the log file", error.Length < 2000, $"{error.Length} characters on the screen");
        return curve;
    }

    static void Main(string[] args)
    {
        OpenSees = args.Length > 0 ? args[0] : "OpenSees";
        Folder = Path.Combine(Path.GetTempPath(), "alpaca-pushover-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Folder);

        SectionProperties();
        Tags();
        Plateau();
        PDelta();
        OtherWay();
        Failing();
        Stopping();

        Console.WriteLine(fails == 0 ? "All pushover checks passed." : $"{fails} pushover check(s) FAILED.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    // ------------------------------------------------------------------ the section on a beam

    static void SectionProperties()
    {
        Console.WriteLine("Fibre section as a beam section");

        var steel = new Steel01("S355", Fy, E, 0.0);
        var off = TwoFlanges(steel, 1.0, -2.0);
        Relative("area: the fibres'", off.Area, 4 * FibreArea, 1e-12);
        Relative("centroid y, at the drawing's x", off.CentroidY, 1.0, 1e-12);
        Relative("centroid z, at the drawing's y", off.CentroidZ, -2.0, 1e-12);
        Relative("Izz, about the centroid: 4 A 0.15²", off.Izz, 4 * FibreArea * Lever * Lever, 1e-9);
        Relative("Iyy, about the centroid: 4 A 0.05²", off.Iyy, 4 * FibreArea * Side * Side, 1e-9);
        Relative("its material's E: Steel01's E0", off.Material.E, E, 1e-12);
        Relative("its density: steel's, as none was given", off.Material.Rho ?? 0.0, 7850.0, 1e-12);
        Relative("G J gives back GJ", off.Material.G * off.J, 1e8, 1e-12);

        var lines = off.WriteTcl().Split('\n').Where(l => l.StartsWith("fiber")).ToList();
        var ys = lines.Select(l => double.Parse(l.Split(' ')[1], CultureInfo.InvariantCulture)).ToList();
        var zs = lines.Select(l => double.Parse(l.Split(' ')[2], CultureInfo.InvariantCulture)).ToList();
        Check("on a beam, the fibres are written about their centroid",
              Math.Abs(ys.Max() - Lever) < 1e-12 && Math.Abs(ys.Min() + Lever) < 1e-12 &&
              Math.Abs(zs.Max() - Side) < 1e-12 && Math.Abs(zs.Min() + Side) < 1e-12,
              string.Join(", ", lines.Select(l => l.Trim())));
        Check("for Moment Curvature, where they were drawn", off.WriteTcl(1).Contains("fiber 1.15 -1.95 "), off.WriteTcl(1).Split('\n')[1]);
        Check("its Id is its own, and the beam's section line names it", off.WriteTcl().StartsWith($"section Fiber {off.Id} -GJ "));

        // 300 x 500 of C25/30 with four bars of 314 mm² of B450C: counted in concrete.
        var concrete = Concrete01.C2530;
        var bars = ReinforcingSteel.B450C;
        var rc = new List<PointFiber>();
        int ny = 6, nz = 10;
        double b = 0.3, h = 0.5;
        for (int i = 0; i < ny; i++)
            for (int j = 0; j < nz; j++)
                rc.Add(new PointFiber(new Point3d(-b / 2 + (i + 0.5) * b / ny, -h / 2 + (j + 0.5) * h / nz, 0), b * h / (ny * nz), concrete));
        double barArea = 314e-6;
        foreach (var y in new[] { -0.1, 0.1 })
            foreach (var z in new[] { -0.2, 0.2 })
                rc.Add(new PointFiber(new Point3d(y, z, 0), barArea, bars));
        var section = new FiberSection(rc, new List<Layer>(), new List<Patch>(), 1e5);
        double ec = 2 * 25000.0 / 0.002;
        Relative("reinforced concrete: E is the concrete's, 2 fc / εc0", section.Material.E, ec, 1e-12);
        Relative("its area, the bars counted Es / Ec times", section.Area, b * h + 4 * barArea * bars.Es / ec, 1e-12);
        Relative("its mass per metre, 2 500 and 7 850 kg/m3", section.Area * (section.Material.Rho ?? 0.0), b * h * 2500.0 + 4 * barArea * 7850.0, 1e-12);
        Relative("its Iyy - z is the drawing's y, the 500 side - counted the same",
                 section.Iyy, rc.Take(ny * nz).Sum(f => f.Area * f.Pos.Y * f.Pos.Y) + 4 * barArea * bars.Es / ec * 0.04, 1e-12);
        Check("its materials: the concrete and the bars, each once",
              section.Materials.Count == 2 && section.Materials.Contains(concrete) && section.Materials.Contains(bars));
        Check("named after them", section.ToString() == "Fiber Section (C25/30 + B450C)", section.ToString());

    }

    static void Tags()
    {
        Console.WriteLine("Free tags");
        var deck = new[] { "timeSeries Linear 12\npattern Plain 12 12 {\n load 1 0 0 1\n}\n", "timeSeries Constant 3\n", "pattern Plain 40 3 -fact 1 {\n}\n" };
        Check("past every pattern and time series", Pushover.FreeTag(deck) == 41, Pushover.FreeTag(deck).ToString());
        Check("1 in a deck with none", Pushover.FreeTag(new[] { "node 1 0 0 0\n" }) == 1);

        Console.WriteLine("P-Delta");
        var beams = new[] { "geomTransf Linear 1 1 0 0\nelement forceBeamColumn 1 1 2 1 NewtonCotes 5 5\n",
                            "geomTransf Corotational 2 0 0 1\nelement forceBeamColumn 2 2 3 2 NewtonCotes 5 5\n",
                            "geomTransf Linear 3 0 0 1\n" };
        var pDelta = Pushover.WithPDelta(beams, out int changed);
        Check("Linear made PDelta, Corotational left as it is",
              changed == 2 && pDelta[0].StartsWith("geomTransf PDelta 1 1 0 0\n") && pDelta[1] == beams[1] && pDelta[2] == "geomTransf PDelta 3 0 0 1\n",
              string.Join(" | ", pDelta.Select(t => t.Split('\n')[0])));
    }

    // ------------------------------------------------------------------ the column

    static void Plateau()
    {
        Console.WriteLine("Cantilever column, Linear, no axial force: up to Mp / L");

        // Steel01 with next to no hardening: with none, a section whose every fibre has yielded has
        // no bending stiffness, and a force-based element inverts it.
        var steel = new Steel01("S355", Fy, E, 1e-4);
        var curve = Column("plateau", TwoFlanges(steel, 1.0, -2.0), "Linear", 0.0, 0.15, 100, out string log, out string deck);

        double inertia = 4 * FibreArea * Lever * Lever;
        double k = 3 * E * inertia / (L * L * L);
        double mp = 4 * FibreArea * Fy * Lever;

        Check("it reached the target", curve.ReachedTarget, curve.Stopped ? "stopped" : "");
        Check("a point per step, and the first at the end of the model's own loads",
              curve.Displacement.Count == 101 && curve.Step[0] == 9 && curve.Step[100] == 109,
              $"{curve.Displacement.Count} points, steps {curve.Step.FirstOrDefault()}..{curve.Step.LastOrDefault()}");
        Check("starting from nothing", Math.Abs(curve.Displacement[0]) < 1e-12 && Math.Abs(curve.BaseShear[0]) < 1e-6,
              $"{F(curve.Displacement[0])} m, {F(curve.BaseShear[0])} kN");
        Relative("the push ends at the target", curve.LastDisplacement, 0.15, 1e-9);
        Relative("the first slope is 3 E I / L³", curve.BaseShear[1] / curve.Displacement[1], k, 1e-6);
        Relative("the base shear is the load factor on a 1 kN load", curve.BaseShear[60], curve.LoadFactor[60], 1e-6);
        double last = curve.BaseShear.Last();
        Check("the plateau is Mp / L, a little hardening above it", last >= mp / L && last <= 1.005 * mp / L,
              $"{F(last)} against {F(mp / L)} kN, {Pct(last, mp / L)}");
        Relative("the peak is the last point's", curve.PeakBaseShear, last, 1e-12);
    }

    static void PDelta()
    {
        Console.WriteLine("Cantilever column, PDelta, 500 kN held on top: (Mp(N) - P Δ) / L");

        var steel = new Steel01("S355", Fy, E, 1e-4);
        double p = 500.0, target = 0.15;
        var curve = Column("pdelta", TwoFlanges(steel), "PDelta", -p, target, 100, out string log, out string deck);

        // Past the plateau one flange is at -fy and the other carries the rest of N: M = A (σ2 - σ1) 0.15
        // with A σ2 = -N + A fy over a flange of 2 fibres.
        double flange = 2 * FibreArea;
        double sigma2 = (-p + flange * Fy) / flange;
        double mpn = flange * Lever * (sigma2 + Fy);

        Check("the model's own loads converged", !curve.GravityFailed);
        Check("it reached the target", curve.ReachedTarget);
        Relative("the first slope is 3 E I / L³ softened by P: (3 E I / L³) - P / L, near enough",
                 curve.BaseShear[1] / curve.Displacement[1], 3 * E * 4 * FibreArea * Lever * Lever / (L * L * L) - p / L, 0.002);
        double last = curve.BaseShear.Last();
        double want = (mpn - p * target) / L;
        Check("at the end (Mp(N) - P Δ) / L, a little hardening above it", last >= want && last <= want + 0.01 * mpn / L,
              $"{F(last)} against {F(want)} kN, {Pct(last, want)}");
        Check("the peak comes before the end: P-Delta takes it down", Math.Abs(curve.PeakBaseShear) > Math.Abs(last) * 1.05,
              $"peak {F(curve.PeakBaseShear)} at {F(curve.PeakDisplacement)} m");
    }

    static void OtherWay()
    {
        Console.WriteLine("A negative target pushes the other way");

        var steel = new Steel01("S355", Fy, E, 1e-4);
        var curve = Column("otherway", TwoFlanges(steel), "Linear", 0.0, -0.15, 50, out _, out _);
        double mp = 4 * FibreArea * Fy * Lever;
        Relative("to -0.15 m", curve.LastDisplacement, -0.15, 1e-9);
        Check("the base shear negative, at -Mp / L", curve.BaseShear.Last() <= -mp / L && curve.BaseShear.Last() >= -1.005 * mp / L,
              F(curve.BaseShear.Last()));
        Check("the load factor negative too", curve.LoadFactor.Last() < 0.0);
    }

    static void Failing()
    {
        Console.WriteLine("Fibres that break: the curve comes down, and the deck ends cleanly");

        // Steel01 wrapped in MinMax: a fibre past ±1% strain carries nothing from then on.
        var steel = new Steel01("S355", Fy, E, 1e-4);
        steel.MinMax = new MinMax("S355", steel, -0.01, 0.01);
        var curve = Column("failing", TwoFlanges(steel), "Linear", 0.0, 0.3, 100, out string log, out _);
        double mp = 4 * FibreArea * Fy * Lever;

        bool ended = curve.ReachedTarget || curve.Stopped || curve.Cut;
        Check("it ended with a word on how", ended, Ending(curve));
        Check("the peak at Mp / L", Math.Abs(curve.PeakBaseShear) >= mp / L && Math.Abs(curve.PeakBaseShear) <= 1.005 * mp / L,
              F(curve.PeakBaseShear));
        Check("the curve comes down once the flanges break", Math.Abs(curve.BaseShear.Last()) < 0.1 * mp / L,
              $"{F(curve.BaseShear.Last())} kN at {F(curve.LastDisplacement)} m, {(curve.ReachedTarget ? "reached" : "stopped")}");
    }

    static void Stopping()
    {
        Console.WriteLine("Steel01 with no hardening on two flanges: the section yields through at once");

        // Every fibre at the same lever arm, nothing to harden: the base section goes from elastic to
        // no bending stiffness in one step, which a force-based element cannot invert. OpenSees goes
        // wrong a few steps before it says so - the base shear climbs past Mp / L - so this is not a
        // section to push, and Pushover Analysis says so of Steel01 with b = 0 before it runs. What
        // is checked is that the deck ends, and that the curve stops at the first step OpenSees
        // says lost a section, or before.
        var steel = new Steel01("S355", Fy, E, 0.0);
        var curve = Column("stopping", TwoFlanges(steel), "Linear", 0.0, 0.15, 100, out string log, out _);
        Check("it ended where a section was lost, and said so", curve.Cut && !curve.ReachedTarget && !curve.Stopped, Ending(curve));
        Check("cut, short of the target", curve.Cut && !curve.ReachedTarget && curve.LastDisplacement < 0.15,
              $"{curve.Displacement.Count} points to {F(curve.LastDisplacement)} m");
        Check("the push stopped at the first step that lost one: little was printed", curve.FlexibilityLostMessages > 0 && curve.FlexibilityLostMessages < 100000 && curve.Log.Length < 100000,
              $"{curve.FlexibilityLostMessages} messages of a lost section, {curve.Log.Length} characters of log");
        Check("Steel01 with b = 0 is named as a material with nothing to harden",
              Pushover.WithoutHardening(new IMaterial[] { steel, new Steel01("S355", Fy, E, 0.01), Concrete01.C2530 }).Single() == steel);

        Console.WriteLine("Steel01 with b = 0.001 on a section with depth, pushed far past yield");

        // 12 x 12 fibres over a 200 x 300 rectangle. With b = 0 the same section goes wrong too, only
        // later: once its innermost row has yielded, near 0.14 m, the base shear jumps 17% past Mp / L
        // without a word from OpenSees. With a little hardening it never loses its stiffness, and the
        // curve settles on Mp / L.
        var hardening = new Steel01("S355", Fy, E, 0.001);
        double bw = 0.2, hd = 0.3;
        int n = 12;
        var fibres = new List<PointFiber>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                fibres.Add(new PointFiber(new Point3d(-hd / 2 + (i + 0.5) * hd / n, -bw / 2 + (j + 0.5) * bw / n, 0), bw * hd / (n * n), hardening));
        var solid = new FiberSection(fibres, new List<Layer>(), new List<Patch>(), 1e8);
        double mp = fibres.Sum(f => f.Area * Math.Abs(f.Pos.X)) * Fy;
        var deep = Column("deep", solid, "Linear", 0.0, 0.3, 100, out string deepLog, out _);
        Check("it reached the target, nothing lost", deep.ReachedTarget && !deep.Cut && deep.FlexibilityLostMessages == 0);
        // The hardening adds about b E I κ at the base: κ near (0.3 m / L) over the 7/90 L the base
        // station stands for, 0.38 1/m, so b E I κ = 0.001 x 94 500 x 0.38 = 36 kN m, 2.2% of Mp.
        Check("the base shear settles on Mp / L, the hardening up to 3% above it",
              deep.BaseShear.Last() >= 0.99 * mp / L && deep.BaseShear.Last() <= 1.03 * mp / L,
              $"{F(deep.BaseShear.Last())} against {F(mp / L)} kN, {Pct(deep.BaseShear.Last(), mp / L)}");
        Check("and rises all the way", deep.BaseShear.Zip(deep.BaseShear.Skip(1), (a, b) => b >= a - 1e-9).All(x => x));
    }
}
