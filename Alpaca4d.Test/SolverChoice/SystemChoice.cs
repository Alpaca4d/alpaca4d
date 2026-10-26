// Locks what Alpaca.Core writes for System Mumps and Auto, and runs it. On an OpenSees with Mumps
// the deck solves with Mumps, quietly; on one without, it falls back to BandSPD - quietly for Auto,
// with a warning for Mumps asked for by name. Every run has to give the exact answer.
//   OPENSEES                the OpenSees built with Mumps (Tools/OpenSees)
//   OPENSEES_WITHOUT_MUMPS  an official OpenSees, which has no Mumps
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Alpaca4d;

class SystemChoice
{
    const double E = 2.1e8, A = 0.002, L = 3.0, P = 60.0;
    const int N = 40;                                   // trusses in the chain
    const string Notice = "this OpenSees has no Mumps solver";
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
        if (!ok) fails++;
    }

    /// <summary>
    /// correctness.py's chain of trusses, whose tip moves P N L / E A exactly, solved with the
    /// settings Analysis Settings and the Section Optimiser build around <paramref name="system"/>.
    /// </summary>
    static string Deck(SystemEquation system)
    {
        var tcl = new StringBuilder("wipe\nmodel basic -ndm 2 -ndf 2\nnode 1 0.0 0.0\nfix 1 1 1\n");
        tcl.Append(FormattableString.Invariant($"uniaxialMaterial Elastic 1 {E}\n"));
        for (int i = 1; i <= N; i++)
            tcl.Append(FormattableString.Invariant($"node {i + 1} 0.0 {i * L}\nfix {i + 1} 1 0\nelement truss {i} {i} {i + 1} {A} 1\n"));
        tcl.Append(FormattableString.Invariant($"timeSeries Linear 1\npattern Plain 1 1 {{ load {N + 1} 0.0 {P} }}\n"));
        var settings = new Settings(new Constraint("Transformation"), new Numberer("RCM"), system, Test.EnergyIncr(),
                                    new Algorithm("Newton"), Integrator.LoadControl(1), new Analysis("Static"), new AnalysisStep(1));
        tcl.Append(settings.WriteTcl());
        tcl.Append($"puts \"TIP [nodeDisp {N + 1} 2]\"\n");
        return tcl.ToString();
    }

    /// <summary>
    /// The chain with a mass at every node, as Natural Vibration solves it: eigen -genBandArpack, its
    /// shift-invert solves done with Auto's system - or with none, OpenSees' own ProfileSPD.
    /// </summary>
    static string ModalDeck(bool auto)
    {
        var tcl = new StringBuilder("wipe\nmodel basic -ndm 2 -ndf 2\nnode 1 0.0 0.0\nfix 1 1 1\n");
        tcl.Append(FormattableString.Invariant($"uniaxialMaterial Elastic 1 {E}\n"));
        for (int i = 1; i <= N; i++)
            tcl.Append(FormattableString.Invariant(
                $"node {i + 1} 0.0 {i * L} -mass 0.0 {0.1 * (1 + i % 3)}\nfix {i + 1} 1 0\nelement truss {i} {i} {i + 1} {A} 1\n"));
        if (auto)
            tcl.Append(new SystemEquation("Auto").WriteTcl());
        tcl.Append(Eigen.WriteTcl("-genBandArpack", 4));
        return tcl.ToString();
    }

    /// <summary>Natural Vibration's eigenvalues with Auto are those it had with no system set.</summary>
    static void CheckModes(string what, string openSees, bool fallback)
    {
        string none = Run(openSees, ModalDeck(false)).error;
        string auto = Run(openSees, ModalDeck(true)).error;
        var expected = Eigen.Read(none);
        var got = Eigen.Read(auto);
        bool same = expected != null && got != null && expected.Count == 4 && got.Count == 4;
        for (int i = 0; same && i < 4; i++)
            same = Math.Abs(got[i] - expected[i]) <= 1e-9 * Math.Abs(expected[i]);
        Check($"{what}: Natural Vibration's 4 eigenvalues with Auto are those with no system", same,
              got == null ? "none read" : string.Join(", ", got.ConvertAll(v => v.ToString("G12"))));
        Check($"{what}: " + (fallback ? "the log says BandSPD solved" : "solved with Mumps"), auto.Contains(Notice) == fallback);
    }

    static (string output, string error) Run(string openSees, string deck)
    {
        string path = Path.Combine(Path.GetTempPath(), $"SystemChoice_{Guid.NewGuid():N}.tcl");
        File.WriteAllText(path, deck);
        try
        {
            var start = new ProcessStartInfo(openSees, $"\"{path}\"")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                var error = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return (output, error.Result);
            }
        }
        finally { File.Delete(path); }
    }

    /// <summary>The run solved, and its tip moved exactly as far as it should.</summary>
    static void CheckAnswer(string what, string output, string error)
    {
        string all = output + "\n" + error;
        double tip = double.NaN;
        foreach (var line in all.Split('\n'))
            if (line.StartsWith("TIP "))
                tip = double.Parse(line.Substring(4).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        double want = P * N * L / (E * A);
        Check($"{what}: analyze returned 0", all.Contains("ALPACA_ANALYZE_RESULT 0"));
        Check($"{what}: tip displacement exact", Math.Abs(tip - want) <= 1e-9 * want, $"{tip:G17} for {want:G17}");
    }

    static void Main()
    {
        Console.WriteLine("System Mumps and Auto, straight out of Alpaca.Core\n");

        string mumps = new SystemEquation("Mumps", 4).WriteTcl();
        string auto = new SystemEquation("auto").WriteTcl();
        Console.WriteLine(auto);
        Check("Mumps asks for symmetric Mumps on 4 cores", mumps.Contains("system Mumps -matrixType 1 -numThreads 4"));
        Check("Mumps falls back to BandSPD, with a warning", mumps.Contains("system BandSPD") && mumps.Contains("WARNING Alpaca4d"));
        Check("Auto asks for Mumps on every core", auto.Contains("system Mumps -matrixType 1 -numThreads 0"));
        Check("Auto falls back to BandSPD, without a warning", auto.Contains("system BandSPD") && !auto.Contains("WARNING"));
        Check("a negative core count is every core", new SystemEquation("Mumps", -3).WriteTcl().Contains("-numThreads 0"));
        Check("other systems are written as before", new SystemEquation("UmfPack", 8).WriteTcl() == "system UmfPack\n");

        string missing = "banner\n" + SystemEquation.MumpsMissing + "\nAlpaca4d: " + Notice + ", so BandSPD solves, on one core\n";
        Check("Auto: OpenSees' missing-Mumps warning is expected",
              !new SystemEquation("Auto").WithoutExpectedWarnings(missing).Contains("WARNING"));
        Check("Mumps by name: the same warning is kept",
              new SystemEquation("Mumps").WithoutExpectedWarnings(missing).Contains("WARNING"));
        Check("Auto: any other warning is kept",
              new SystemEquation("Auto").WithoutExpectedWarnings("WARNING something else\n").Contains("WARNING"));

        string withMumps = Environment.GetEnvironmentVariable("OPENSEES");
        if (!string.IsNullOrEmpty(withMumps))
        {
            Console.WriteLine($"\nOpenSees with Mumps: {withMumps}");
            var (output, error) = Run(withMumps, Deck(new SystemEquation("Auto")));
            CheckAnswer("Auto", output, error);
            Check("Auto: solved with Mumps, so nothing about falling back", !(output + error).Contains(Notice));
            Check("Auto: no warning for Run Analysis to show",
                  !new SystemEquation("Auto").WithoutExpectedWarnings(error).Contains("WARNING"));
            (output, error) = Run(withMumps, Deck(new SystemEquation("Mumps", 1)));
            CheckAnswer("Mumps on 1 core", output, error);
            Check("Mumps on 1 core: no warning", !error.Contains("WARNING"));
            CheckModes("Modes", withMumps, fallback: false);
        }
        else
            Console.WriteLine("\nOPENSEES not set: the decks are not run on an OpenSees with Mumps");

        string without = Environment.GetEnvironmentVariable("OPENSEES_WITHOUT_MUMPS");
        if (!string.IsNullOrEmpty(without))
        {
            Console.WriteLine($"\nOpenSees without Mumps: {without}");
            var (output, error) = Run(without, Deck(new SystemEquation("Auto")));
            CheckAnswer("Auto", output, error);
            Check("Auto: the log says BandSPD solved", (output + error).Contains(Notice));
            Check("Auto: no warning for Run Analysis to show",
                  !new SystemEquation("Auto").WithoutExpectedWarnings(error).Contains("WARNING"));
            (output, error) = Run(without, Deck(new SystemEquation("Mumps")));
            CheckAnswer("Mumps by name", output, error);
            Check("Mumps by name: Run Analysis warns that it did not get it",
                  new SystemEquation("Mumps").WithoutExpectedWarnings(error).Contains("WARNING"));
            CheckModes("Modes", without, fallback: true);
        }
        else
            Console.WriteLine("\nOPENSEES_WITHOUT_MUMPS not set: the fallback is not run");

        Console.WriteLine(fails == 0 ? "\nAll passed." : $"\n{fails} failed.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
