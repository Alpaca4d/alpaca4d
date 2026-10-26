// Response spectrum analysis, checked without Rhino.
//
//   1. the spectra Design Spectrum makes, against values worked out by hand from EN 1998-1 3.2.2 and
//      NTC 2018 3.2.3, and the errors it gives for what is not a spectrum;
//   2. the correlation of two modes and the combinations, against their formulas;
//   3. a real run: a column whose two sway modes are close and turned 30° off the axes, so each one
//      answers both directions and CQC is not SRSS. The deck is the Tcl SpectrumAnalysis writes after
//      a model written here; OpenSees solves it, and the results are read back through Read - the
//      reader every result component uses - as the analysis hands them on. The base reaction is
//      checked against the masses in the modal report, which reach it by a different road.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Loads;
using Alpaca4d.Result;
using Alpaca4d.Seismic;

class SpectrumCheck
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Near(double got, double want, string what, double tol = 1e-9)
    {
        bool ok = Math.Abs(got - want) <= tol * Math.Max(1.0, Math.Abs(want));
        Check(ok, $"{what}: {got:G8} (want {want:G8})");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    static string Failed(Action action)
    {
        try { action(); return null; }
        catch (Exception e) { return e.Message; }
    }

    static int Main(string[] args)
    {
        Spectra();
        Combinations();
        if (args.Length > 0 && File.Exists(args[0]))
            Run(args[0]);
        else
            Console.WriteLine("\nnote: OpenSees not found, the run was skipped");

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        return fails == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------------------------- spectra

    static void Spectra()
    {
        Section("1. EN 1998-1, recommended values");

        var elastic = new Ec8Spectrum(0.25, 1.0, "b", 1, SpectrumComponent.Horizontal);
        Near(elastic.At(0.0), 0.3, "elastic, type 1, B: at T = 0, ag S");
        Near(elastic.At(0.075), 0.525, "halfway to TB");
        Near(elastic.At(0.3), 0.75, "on the plateau, 2.5 ag S");
        Near(elastic.At(1.0), 0.375, "past TC, 2.5 ag S TC/T");
        Near(elastic.At(3.0), 0.0833333333333333, "past TD, 2.5 ag S TC TD/T²");
        Near(new Ec8Spectrum(0.25, 1.0, "B", 1, SpectrumComponent.Horizontal, null, 10.0).At(0.3), 0.6123724356957945,
             "10% damping scales the plateau by η = √(10/15)");
        Near(new Ec8Spectrum(0.2, 1.2, "C", 2, SpectrumComponent.Horizontal).At(0.8), 0.28124999999999994,
             "type 2, C, γI = 1.2: ag = γI agR");

        var design = new Ec8Spectrum(0.25, 1.0, "B", 1, SpectrumComponent.Horizontal, 3.9);
        Near(design.At(0.0), 0.2, "design, q = 3.9: at T = 0, 2/3 ag S");
        Near(design.At(0.3), 0.19230769230769232, "the plateau, 2.5 ag S / q");
        Near(design.At(1.0), 0.09615384615384616, "past TC");
        Near(design.At(4.0), 0.05, "held at β ag far out");

        var vertical = new Ec8Spectrum(0.25, 1.0, "B", 1, SpectrumComponent.Vertical);
        Near(vertical.At(0.0), 0.225, "vertical, type 1: avg = 0.9 ag at T = 0");
        Near(vertical.At(0.1), 0.675, "its plateau, 3.0 avg");
        Near(vertical.At(2.0), 0.0253125, "past TD = 1 s");
        Check(Math.Abs(elastic.MassRatioRequired - 0.90) < 1e-12, "EN 1998-1 asks for 90% of the mass");

        Section("2. NTC 2018");

        var ntc = new NtcSpectrum(0.18, 2.45, 0.30, "C", "T1", SpectrumComponent.Horizontal);
        Near(ntc.SS, 1.4354, "SS = 1.70 - 0.60 Fo ag/g, within 1.00 to 1.50");
        Near(ntc.CC, 1.5622095378895753, "CC = 1.05 Tc*^-0.33");
        Near(ntc.TB, 0.15622095378895753, "TB = TC/3");
        Near(ntc.TC, 0.4686628613668726, "TC = CC Tc*");
        Near(ntc.TD, 2.32, "TD = 4 ag/g + 1.6");
        Near(ntc.At(0.0), 0.258372, "elastic, C: at T = 0, ag S");
        Near(ntc.At(0.1), 0.4981857963656968, "on the way up");
        Near(ntc.At(0.3), 0.6330114, "the plateau, ag S η Fo");
        Near(ntc.At(1.0), 0.2966689340018499, "past TC");
        Near(ntc.At(3.0), 0.0764746585426991, "past TD");

        var ntcDesign = new NtcSpectrum(0.18, 2.45, 0.30, "C", "T3", SpectrumComponent.Horizontal, 3.0);
        Near(ntcDesign.ST, 1.2, "T3: ST = 1.2");
        Near(ntcDesign.At(0.0), 0.31004640000000006, "design, q = 3: at T = 0 still ag S");
        Near(ntcDesign.At(0.3), 0.25320456, "its plateau, η replaced by 1/q");
        Near(ntcDesign.At(1.0), 0.11866757360073998, "past TC");
        Near(ntcDesign.At(10.0), 0.036, "never below 0.2 ag");

        var damped = new NtcSpectrum(0.30, 2.40, 0.35, "D", "T1", SpectrumComponent.Horizontal, null, 10.0);
        Near(damped.SS, 1.32, "D: SS = 2.40 - 1.50 Fo ag/g");
        Near(damped.At(0.05), 0.47307773345435533, "D, 10% damping, before TB");
        Near(damped.At(0.5), 0.7759983505137108, "its plateau");

        var ntcVertical = new NtcSpectrum(0.18, 2.45, 0.30, "C", "T1", SpectrumComponent.Vertical);
        Near(ntcVertical.Fv, 1.35 * 2.45 * Math.Sqrt(0.18), "Fv = 1.35 Fo √(ag/g)");
        Near(ntcVertical.At(0.0), 0.10309616869699863, "vertical at T = 0: ag S Fv / Fo, [3.2.8] as printed");
        Near(ntcVertical.At(0.025), 0.17784089100232267, "halfway to TB = 0.05 s");
        Near(ntcVertical.At(0.1), 0.2525856133076467, "its plateau, ag S η Fv");
        Near(ntcVertical.At(2.0), 0.00947196049903675, "past TD = 1 s");
        Check(Math.Abs(ntc.MassRatioRequired - 0.85) < 1e-12, "NTC 2018 asks for 85% of the mass");

        Section("3. Custom, and what is not a spectrum");

        var custom = new CustomSpectrum(new[] { 0.1, 0.5, 2.0 }, new[] { 0.4, 0.8, 0.2 });
        Near(custom.At(0.0), 0.4, "flat before the first point");
        Near(custom.At(0.3), 0.6, "straight between points");
        Near(custom.At(1.25), 0.5, "straight between the next two");
        Near(custom.At(9.0), 0.2, "flat past the last");

        Check(Failed(() => new Ec8Spectrum(0.25, 1.0, "S1", 1, SpectrumComponent.Horizontal))?.Contains("A, B, C, D, E") == true,
              "ground S1 is refused, naming A to E");
        Check(Failed(() => new Ec8Spectrum(2.45, 1.0, "B", 1, SpectrumComponent.Horizontal))?.Contains("not m/s²") == true,
              "an agR in m/s² is caught");
        Check(Failed(() => new Ec8Spectrum(0.25, 1.0, "B", 3, SpectrumComponent.Horizontal)) != null, "type 3 is refused");
        Check(Failed(() => new Ec8Spectrum(0.25, 1.0, "B", 1, SpectrumComponent.Horizontal, 0.8)) != null, "q below 1 is refused");
        Check(Failed(() => new NtcSpectrum(0.18, 2.45, 0.30, "C", "T5", SpectrumComponent.Horizontal)) != null, "topography T5 is refused");
        Check(Failed(() => new NtcSpectrum(0.18, 2.45, 0.30, "C", "T1", SpectrumComponent.Horizontal, null, 0.05))?.Contains("not a ratio") == true,
              "damping given as a ratio is caught");
        Check(Failed(() => new CustomSpectrum(new[] { 0.5, 0.1 }, new[] { 1.0, 1.0 })) != null, "periods out of order are refused");
        Check(Failed(() => new CustomSpectrum(new[] { 0.1, 0.5 }, new[] { 1.0 })) != null, "one acceleration short is refused");

        var (periods, accelerations) = ntc.Sample(4.0);
        Check(periods.First() == 0.0 && Math.Abs(periods.Last() - 4.0) < 1e-12, $"sampled from 0 to 4 s ({periods.Count} points)");
        Check(periods.Contains(Math.Round(ntc.TC, 9)), "the samples pass through TC");
        Check(periods.Zip(periods.Skip(1), (a, b) => b > a).All(x => x), "and go up, each once");
        Check(accelerations.Max() <= ntc.At(0.3) + 1e-12, "and never above the plateau");
    }

    // ---------------------------------------------------------------------------------- combinations

    static void Combinations()
    {
        Section("4. Correlation and combinations");

        Near(SpectrumCases.Correlation(1.0, 0.5, 0.05), 0.018486451795726735, "ρ of periods 1 and 0.5 at 5%");
        Near(SpectrumCases.Correlation(0.5, 1.0, 0.05), 0.018486451795726735, "the same either way round");
        Near(SpectrumCases.Correlation(1.0, 0.9, 0.05), 0.47302768323848354, "periods 10% apart, 5%: correlated");
        Near(SpectrumCases.Correlation(1.0, 0.9, 0.02), 0.12569966407735927, "less so at 2%");
        Near(SpectrumCases.Correlation(0.7, 0.7, 0.05), 1.0, "equal periods: one");

        double rho = 0.47302768323848354;
        var r = new[] { 3.0, -2.0 };
        var correlations = new double[,] { { 1.0, rho }, { rho, 1.0 } };
        Near(SpectrumCases.CombineModes(r, correlations), Math.Sqrt(9.0 + 4.0 - 2.0 * rho * 6.0), "CQC: √(r1² + r2² + 2ρ r1 r2), opposite signs");
        Near(SpectrumCases.CombineModes(r, null), Math.Sqrt(13.0), "SRSS: √(r1² + r2²)");
        Near(SpectrumCases.CombineDirections(new[] { 1.0, 2.0 }, DirectionalCombination.Percent30), 2.3, "30%: max(1 + 0.6, 0.3 + 2)");
        Near(SpectrumCases.CombineDirections(new[] { 1.0, 2.0, 0.5 }, DirectionalCombination.Percent30), 2.0 + 0.3 * 1.5, "30% of the other two");
        Near(SpectrumCases.CombineDirections(new[] { 1.0, 2.0 }, DirectionalCombination.SRSS), Math.Sqrt(5.0), "SRSS of the directions");

        Section("5. The deck");

        var lines = SpectrumAnalysis.WriteTcl("-genBandArpack", 1, new[] { 0, 2 }, "r.txt").Split('\n');
        Check(lines.Contains("set lambdaN [eigen -genBandArpack 2]"), "one mode on ARPACK: two are solved, the zero ARPACK gives for one kept out");
        Check(lines.Count(l => l.StartsWith("responseSpectrumAnalysis")) == 2 &&
              lines.Contains("responseSpectrumAnalysis 1 -Tn {1.0} -Sa {1.0} -mode 1") &&
              lines.Contains("responseSpectrumAnalysis 3 -Tn {1.0} -Sa {1.0} -mode 1"),
              "and only mode 1 is run, along X and along Z");
        Check(SpectrumAnalysis.WriteTcl("-fullGenLapack", 1, new[] { 0 }, "r.txt").Contains("[eigen -fullGenLapack 1]"), "LAPACK is asked for just the one");
        Check(SpectrumAnalysis.ArpackShort("ArpackSolver::Error with _saupd info = -9999\n", 12)?.Contains("-fullGenLapack") == true,
              "ARPACK short of masses: the message points to fewer modes or -fullGenLapack");
        Check(SpectrumAnalysis.ArpackShort("some other error", 12) == null, "and stays quiet about any other failure");
    }

    // ------------------------------------------------------------------------------------------ a run

    const double H = 3.0;                       // storey height, m
    const double StoreyMass = 10000.0;          // kg
    static readonly double Turn = Math.PI / 6;  // the column's axes, 30° off X

    static void Run(string openSees)
    {
        Section("6. A run, through OpenSees and back through Read");

        string folder = Path.Combine(Directory.GetCurrentDirectory(), "run");
        Directory.CreateDirectory(folder);
        string deck = Path.Combine(folder, "spectrum.tcl");
        string mpco = Path.Combine(folder, "spectrum.mpco");
        string report = Path.Combine(folder, "report.txt");
        const int modes = 6;

        var beams = Column();
        var tcl = new StringBuilder();
        tcl.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
        for (int i = 1; i <= 4; i++)
            tcl.Append($"node {i} 0 0 {(i - 1) * H}\n");
        tcl.Append("fix 1 1 1 1 1 1 1\n");
        tcl.Append(beams[0].Section.Material.WriteTcl() + beams[0].Section.WriteTcl());
        foreach (var beam in beams)
            tcl.Append(beam.WriteTcl());
        for (int i = 2; i <= 4; i++)
            tcl.Append(new MassLoad(Point3d.Origin, new Vector3d(StoreyMass, StoreyMass, StoreyMass), Vector3d.Zero) { Id = i, Ndf = 6 }.WriteTcl());
        tcl.Append(Recorder.MpcoStatic(mpco).WriteTcl());
        tcl.Append(SpectrumAnalysis.WriteTcl("-fullGenLapack", modes, new[] { 0, 1 }, report));
        tcl.Append("wipe\n");
        File.WriteAllText(deck, tcl.ToString());
        File.Delete(mpco);

        var (stderr, exit) = Solve(openSees, deck, folder);
        Check(exit == 0, $"OpenSees ran the deck (exit {exit})");
        Check(stderr.Contains("Using ResponseSpectrumAnalysis"), "and ran responseSpectrumAnalysis");
        if (exit != 0) { Console.WriteLine(stderr); return; }

        var eigenvalues = Eigen.Read(stderr);
        Check(eigenvalues != null && eigenvalues.Count == modes, $"{modes} eigenvalues read back");
        var periods = eigenvalues.Select(lambda => 2.0 * Math.PI / Math.Sqrt(lambda)).ToList();
        Check(Math.Abs(periods[1] / periods[0] - 1.0) < 0.15, $"the first two periods are close: {periods[0]:0.000} and {periods[1]:0.000} s");

        var masses = SpectrumAnalysis.ReadMasses(File.ReadAllLines(report));
        Check(masses != null, "the modal report's masses are read");
        Near(masses.Free[0], 30.0 + FreeElementMass(), "free mass along X: the storeys and the column above the base", 1e-5);
        Check(masses.Ratio(0) > 0.99 && masses.Ratio(1) > 0.99, $"six modes move all of it: {masses.Ratio(0):P2} along X, {masses.Ratio(1):P2} along Y");
        Check(masses.Along(0)[0] > 1.0 && masses.Along(1)[0] > 1.0, "the first mode moves mass along both X and Y");

        var spectrumX = new Ec8Spectrum(0.25, 1.0, "B", 1, SpectrumComponent.Horizontal);
        var spectrumY = new NtcSpectrum(0.18, 2.45, 0.30, "C", "T1", SpectrumComponent.Horizontal, 1.5);
        var directions = new List<SpectrumDirection>
        {
            new SpectrumDirection(0, spectrumX, 0, periods),
            new SpectrumDirection(1, spectrumY, modes, periods),
        };
        var cqc = new SpectrumCases(periods, directions, ModalCombination.CQC, DirectionalCombination.Percent30);

        var nodes = Enumerable.Range(1, 4).Select(i => new Node(i, 0, 0, (i - 1) * H) { Ndf = 6 }).ToList();
        var plain = new Model
        {
            Nodes = nodes,
            Beams = beams.Cast<IBeam>().ToList(),
            Recorders = new List<IRecorder> { new Recorder { FileName = mpco } },
        };
        var model = plain.ShallowCopy();
        model.SpectrumCases = cqc;

        Check(Read.StepCount(plain) == 2 * modes, $"the file holds a step per mode and direction ({Read.StepCount(plain)})");
        Check(Read.StepCount(model) == 3, "the analysed model has three: E, Ex, Ey");
        Check(string.Join(" ", cqc.Names) == "E Ex Ey", "named E, Ex, Ey");

        // Each mode at a spectral acceleration of one: the base reaction is the mass the mode moves.
        double g = ResponseSpectrum.ModelG;
        for (int axis = 0; axis < 2; axis++)
        {
            bool all = true;
            for (int n = 0; n < modes; n++)
            {
                var reaction = Read.NodalOutput(plain, axis * modes + n, ResultType.REACTION_FORCE).First();
                double along = axis == 0 ? reaction.X : reaction.Y;
                all &= Math.Abs(Math.Abs(along) - masses.Along(axis)[n]) <= 1e-4 * Math.Max(1.0, masses.Along(axis)[n]);
            }
            Check(all, $"shaken along {"XY"[axis]}, each mode's base reaction is the mass it moves, at Sa = 1");
        }

        // Ex and Ey through the reader, against the participating masses combined here.
        for (int axis = 0; axis < 2; axis++)
        {
            var spectrum = axis == 0 ? (ResponseSpectrum)spectrumX : spectrumY;
            var forces = Enumerable.Range(0, modes).Select(n => masses.Along(axis)[n] * spectrum.At(periods[n]) * g).ToArray();
            var rho = new double[modes, modes];
            for (int i = 0; i < modes; i++)
                for (int j = 0; j < modes; j++)
                    rho[i, j] = Rho(periods[i], periods[j], 0.05);
            double cqcShear = Math.Sqrt(Enumerable.Range(0, modes).Sum(i => Enumerable.Range(0, modes).Sum(j => rho[i, j] * forces[i] * forces[j])));
            double srssShear = Math.Sqrt(forces.Sum(f => f * f));

            var read = Read.NodalOutput(model, 1 + axis, ResultType.REACTION_FORCE).First();
            Near(axis == 0 ? read.X : read.Y, cqcShear, $"E{"xy"[axis]}: the base reaction is the CQC of Meff Sa", 2e-5);
            Check(Math.Abs(cqcShear / srssShear - 1.0) > 0.02, $"and CQC is not SRSS here ({cqcShear:0.00} against {srssShear:0.00} kN)");
            Near(cqc.CombineScalar(axis, masses.Along(axis)), cqcShear, $"CombineScalar gives the same base shear along {"XY"[axis]}", 1e-9);
        }

        // A beam's moment, combined by the reader, against the modes read one by one and combined here.
        var perMode = Enumerable.Range(0, modes).Select(n => Read.ForceBeamColumn(plain, n).mz[0][0] * directions[0].Accelerations[n]).ToArray();
        double byHand = Math.Sqrt(Enumerable.Range(0, modes).Sum(i => Enumerable.Range(0, modes).Sum(j => Rho(periods[i], periods[j], 0.05) * perMode[i] * perMode[j])));
        Near(Read.ForceBeamColumn(model, 1).mz[0][0], byHand, "Ex: the base moment Mz, combined from the modes' own moments", 1e-9);

        // E: each node's peak, 100% of one direction and 30% of the other, whichever is more.
        var ex = Read.NodalOutput(model, 1, ResultType.DISPLACEMENT).ToList();
        var ey = Read.NodalOutput(model, 2, ResultType.DISPLACEMENT).ToList();
        var e = Read.NodalOutput(model, 0, ResultType.DISPLACEMENT).ToList();
        Near(e[3].X, Math.Max(ex[3].X + 0.3 * ey[3].X, 0.3 * ex[3].X + ey[3].X), "E: the top's X, 30% rule over Ex and Ey");
        Near(e[3].Y, Math.Max(ex[3].Y + 0.3 * ey[3].Y, 0.3 * ex[3].Y + ey[3].Y), "E: the top's Y");
        Check(ex.All(v => v.X >= 0.0 && v.Y >= 0.0 && v.Z >= 0.0), "every combined value is a peak, zero or more");

        var srss = model.ShallowCopy();
        srss.SpectrumCases = new SpectrumCases(periods, directions, ModalCombination.SRSS, DirectionalCombination.SRSS);
        var sx = Read.NodalOutput(srss, 1, ResultType.REACTION_FORCE).First().X;
        Near(sx, Math.Sqrt(Enumerable.Range(0, modes).Sum(n => Math.Pow(masses.Along(0)[n] * spectrumX.At(periods[n]) * g, 2))),
             "SRSS: the base reaction is √Σ (Meff Sa)²", 2e-5);
        var sEx = Read.NodalOutput(srss, 1, ResultType.DISPLACEMENT).ToList()[3];
        var sEy = Read.NodalOutput(srss, 2, ResultType.DISPLACEMENT).ToList()[3];
        Near(Read.NodalOutput(srss, 0, ResultType.DISPLACEMENT).ToList()[3].X, Math.Sqrt(sEx.X * sEx.X + sEy.X * sEy.X), "SRSS of the directions");

        Check(Failed(() => Read.NodalOutput(model, 3, ResultType.DISPLACEMENT))?.Contains("0 E, 1 Ex, 2 Ey") == true,
              "a step past Ey says which steps there are");
    }

    /// <summary>
    /// The column's own mass on the free nodes, in t. Each element puts half its mass on either end, so
    /// nodes 2 and 3 carry a whole element's worth, the top half of one, and the fixed base the rest.
    /// </summary>
    static double FreeElementMass()
    {
        double perMetre = 0.09 * 2.5;   // A ρ, t/m
        return perMetre * H * 2.5;
    }

    /// <summary>NTC 2018 [7.3.4], written out again here rather than called.</summary>
    static double Rho(double ti, double tj, double xi)
    {
        double b = tj / ti;
        return 8 * xi * xi * Math.Pow(b, 1.5) / ((1 + b) * ((1 - b) * (1 - b) + 4 * xi * xi * b));
    }

    /// <summary>Three columns, one per storey, square-ish and turned 30° off X.</summary>
    static List<ForceBeamColumn> Column()
    {
        var material = new Alpaca4d.Material.UniaxialMaterialElastic("C30", 3.0e7, 3.0e7, 0.0, 1.25e7, 0.2, 2500.0);
        var section = new Alpaca4d.Section.ElasticSection("column", 0.09, 6.75e-4, 7.4e-4, 1.0e-3, 1.0, 1.0, material);
        var vecxz = new Vector3d(Math.Cos(Turn), Math.Sin(Turn), 0.0);
        var beams = new List<ForceBeamColumn>();
        for (int i = 1; i <= 3; i++)
        {
            var beam = new ForceBeamColumn(null, section, new GeomTransf(GeomTransfType.Linear, null, vecxz)) { Id = i, INode = i, JNode = i + 1 };
            beam.SetTags();
            beams.Add(beam);
        }
        return beams;
    }

    static (string stderr, int exit) Solve(string openSees, string deck, string folder)
    {
        using (var process = new Process())
        {
            process.StartInfo.FileName = openSees;
            process.StartInfo.Arguments = "\"" + deck + "\"";
            process.StartInfo.WorkingDirectory = folder;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            var error = new StringBuilder();
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };
            process.OutputDataReceived += (s, e) => { };
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            process.WaitForExit();
            return (error.ToString(), process.ExitCode);
        }
    }
}
