// The eigenvalues Natural Vibration reads back from OpenSees: Alpaca4d.Eigen, which writes the
// eigen command and picks its result out of everything OpenSees prints. The solvers print on the
// same stream - FullGenEigenSolver about its workspace or a complex eigenvalue - and that used to
// be read as eigenvalues too. See README.md.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Alpaca4d;

class EigenCheck
{
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail == "" ? "" : "  " + detail)}");
        if (!ok) fails++;
    }

    static bool Close(double got, double want, double tolerance) =>
        Math.Abs(got - want) <= tolerance * Math.Max(1.0, Math.Abs(want));

    // A 5 m steel cantilever, 100 x 100 mm, in 10 elastic beams with the mass lumped at the nodes:
    // kN, m and tonnes, the units Alpaca4d writes in.
    const string Cantilever = @"wipe
model BasicBuilder -ndm 3 -ndf 6
for {set i 0} {$i <= 10} {incr i} { node [expr $i + 1] [expr $i * 0.5] 0.0 0.0 }
fix 1 1 1 1 1 1 1
geomTransf Linear 1 0 0 1
for {set i 1} {$i <= 10} {incr i} { element elasticBeamColumn $i $i [expr $i + 1] 0.01 2.1e8 8.0769e7 1.406e-5 8.3333e-6 8.3333e-6 1 }
for {set i 2} {$i <= 11} {incr i} {
    if {$i == 11} { set m 0.019625 } else { set m 0.03925 }
    mass $i $m $m $m 0.0 0.0 0.0
}
";

    static string RunOpenSees(string openSees, string deck, string folder)
    {
        string file = Path.Combine(folder, "eigen.tcl");
        File.WriteAllText(file, deck);
        var start = new ProcessStartInfo(openSees, "\"" + file + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = folder,
        };
        using (var p = Process.Start(start))
        {
            var stdout = p.StandardOutput.ReadToEndAsync();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return stderr + "\n" + stdout.Result;
        }
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Reading what OpenSees prints\n");

        const string banner = "\n\n         OpenSees -- Open System For Earthquake Engineering Simulation\n" +
                              "                 Pacific Earthquake Engineering Research Center\n" +
                              "      (c) Copyright 1999-2024 The Regents of the University of California\n" +
                              "                              All Rights Reserved\n" +
                              "  (Copyright and Disclaimer @ http://www.berkeley.edu/OpenSees/copyright.html)\n" +
                              "  Developed by: ASDEA Software, Italy.\n";

        var plain = Eigen.Read(banner + Eigen.Label + " 438.36 17217.0 134678.5\nUsing DomainModalProperties - Developed by: Massimo Petracca, Guido Camata, ASDEA Software Technology\n");
        Check("the labelled line, and nothing else", plain != null && plain.SequenceEqual(new[] { 438.36, 17217.0, 134678.5 }),
              plain == null ? "null" : string.Join(" ", plain));

        // What FullGenEigenSolver says on the way; the old parser took these words for numbers.
        var noisy = Eigen.Read(banner +
            "FullGenEigenSolver::solve() - the eigenvalue 3 is complex with magnitude 12.5\n" +
            "FullGenEigenSolver::solve() - optimal workspace size 1460 is larger than provided workspace size 730 consider increasing workspace\n" +
            Eigen.Label + " 1.5 2.5\r\n" +
            "Using DomainModalProperties - Developed by: Massimo Petracca, Guido Camata, ASDEA Software Technology\n");
        Check("with the solver's warnings around it", noisy != null && noisy.SequenceEqual(new[] { 1.5, 2.5 }),
              noisy == null ? "null" : string.Join(" ", noisy));

        Check("no eigenvalues printed: null, not an exception", Eigen.Read(banner + "WARNING eigen - ArpackSolver failed\n") == null);

        // What -symmBandLapack does with a vibration problem: an empty list, and why.
        string refused = banner + "SymBandEigenSolver::solve() - only does standard problem\n" +
                         "WARNING DirectIntegrationAnalysis::eigen() - EigenSOE failed in solve()\n" + Eigen.Label + " \n";
        var none = Eigen.Read(refused);
        var said = Eigen.SolverMessages(refused);
        Check("an empty list, and the solver's reason", none != null && none.Count == 0 && said.Count == 2 && said[0].StartsWith("SymBandEigenSolver"),
              string.Join(" / ", said));
        Check("-symmBandLapack turned away before it runs", Eigen.Unsuitable("-symmBandLapack") != null &&
              Eigen.Unsuitable("-genBandArpack") == null && Eigen.Unsuitable("-fullGenLapack") == null && Eigen.Unsuitable("") == null);

        var odd = Eigen.Read(Eigen.Label + " -1.2e-10 Inf 3.0e+02");
        Check("round-off below zero, Tcl's Inf, an exponent",
              odd != null && odd.Count == 3 && odd[0] == -1.2e-10 && double.IsPositiveInfinity(odd[1]) && odd[2] == 300.0,
              odd == null ? "null" : string.Join(" ", odd));

        // A machine that writes one and a half as 1,5 still reads Tcl's 1.5 as one and a half.
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("it-IT");
            var italian = Eigen.Read(Eigen.Label + " 438.36 1.5e+03");
            bool wouldMisread = !double.TryParse("438.36", out double asItalian) || asItalian != 438.36;
            Check("under an Italian locale", italian != null && italian.SequenceEqual(new[] { 438.36, 1500.0 }),
                  (italian == null ? "null" : string.Join(" ", italian)) + (wouldMisread ? "  (a plain double.Parse would not)" : ""));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }

        string openSees = args.Length > 0 ? args[0] : "";
        if (!File.Exists(openSees))
        {
            Console.WriteLine("\n  note: OpenSees not found, the solver runs are skipped");
        }
        else
        {
            Console.WriteLine($"\nThe cantilever, solved by {openSees}\n");
            string folder = Path.Combine(Path.GetTempPath(), "alpaca4d-eigen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                // Euler-Bernoulli: f1 = 1.8751^2 / (2 pi L^2) sqrt(EI / m).
                double f1 = Math.Pow(1.875104069, 2) / (2 * Math.PI * 25.0) * Math.Sqrt(2.1e11 * 8.3333e-6 / 78.5);

                // The default solver, six modes: the first is the cantilever's, a little below
                // Euler-Bernoulli for the lumped mass.
                var arpack = Eigen.Read(RunOpenSees(openSees, Cantilever + Eigen.WriteTcl("-genBandArpack", 6), folder));
                bool six = arpack != null && arpack.Count == 6;
                double f = six ? Math.Sqrt(arpack[0]) / (2 * Math.PI) : double.NaN;
                Check($"-genBandArpack   six modes, f1 = {f:F4} Hz against Euler-Bernoulli's {f1:F4}", six && Close(f, f1, 0.01),
                      arpack == null ? "none read" : $"{arpack.Count} read");

                // FullGen for more modes than the 30 masses can give: past the 30th it prints
                // "numerically undetermined or infinite" among the output, which is what the old
                // reading choked on. The finite ones still have to come back, and match.
                string printed = RunOpenSees(openSees, Cantilever + Eigen.WriteTcl("-fullGenLapack", 36), folder);
                var full = Eigen.Read(printed);
                bool warned = printed.Contains("FullGenEigenSolver::solve()");
                Check("-fullGenLapack   warnings printed around the eigenvalues", warned);
                Check("-fullGenLapack   36 read, the first six those of -genBandArpack",
                      full != null && full.Count == 36 && six && full.Take(6).Zip(arpack, (a, b) => Close(a, b, 1e-6)).All(x => x),
                      full == null ? "none read" : $"{full.Count} read");

                // -symmBandLapack really does refuse, which is why Natural Vibration turns it away.
                printed = RunOpenSees(openSees, Cantilever + Eigen.WriteTcl("-symmBandLapack", 6), folder);
                var symm = Eigen.Read(printed);
                Check("-symmBandLapack  refuses: no eigenvalues, and says why", symm != null && symm.Count == 0 && Eigen.SolverMessages(printed).Count > 0,
                      string.Join(" / ", Eigen.SolverMessages(printed)));
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        Console.WriteLine(fails == 0 ? "\nall passed" : $"\n{fails} failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
