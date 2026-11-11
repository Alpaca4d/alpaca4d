// Footfall Analysis: Alpaca4d.Footfall against hand calculations of SCI P354, and the modes it works
// from, read mass-normalised from the recorder file, against a simply supported beam. See README.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Result;
using Rhino.Geometry;

class FootfallCheck
{
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail == "" ? "" : "  " + detail)}");
        if (!ok) fails++;
    }

    static bool Close(double got, double want, double tolerance) =>
        Math.Abs(got - want) <= tolerance * Math.Max(1e-300, Math.Abs(want));

    /// <summary>A shell with only its nodes: what Footfall.ShellNodes reads.</summary>
    class Quad : IShell
    {
        public Quad(int?[] nodes) { IndexNodes = nodes.ToList(); }
        public Mesh Mesh { get; set; }
        public List<int?> IndexNodes { get; set; }
        public IMultiDimensionSection Section { get; set; }
        public System.Drawing.Color Color { get; set; }
        public ElementClass ElementClass => ElementClass.ASDShellQ4;
        public Plane LocalPlane => Plane.WorldXY;
        public ElementType Type => ElementType.Shell;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public void SetTags() { }
        public void SetTopologyRTree(Model model) { }
        public string WriteTcl() => "";
    }

    // ---- P354 by hand, written out again rather than calling the code under test ----

    static double Alpha(int h, double fp)
    {
        double f = h * fp;
        return h == 1 ? 0.436 * (f - 0.95) : h == 2 ? 0.006 * (f + 12.3) : h == 3 ? 0.007 * (f + 5.2) : 0.007 * (f + 2.0);
    }

    static double Wb(double f) => f < 2 ? 0.4 : f < 5 ? f / 5 : f <= 16 ? 1.0 : 16 / f;

    /// <summary>Equation 30: h²β² / √((1 - h²β²)² + (2hζβ)²), β = fp / fn.</summary>
    static double D(int h, double fp, double fn, double zeta)
    {
        double hb = h * fp / fn;
        return hb * hb / Math.Sqrt(Math.Pow(1 - hb * hb, 2) + Math.Pow(2 * zeta * hb, 2));
    }

    /// <summary>
    /// Equation 31, the full time history, sampled over one pace for its rms, times ρ (Equation 37): each
    /// harmonic drives each mode at D (Equation 30) and its phase φ (Equation 32), tan φ = -2hβζ / (1 - h²β²),
    /// -π ≤ φ ≤ 0. The share of each mode, φₑ φᵣ, is given; Q in kN. The harmonics' own phases φₕ are left
    /// at 0: at their different frequencies they do not change the rms over a pace.
    /// </summary>
    static double SteadyByHand(double fp, double[] fn, double[] share, double zeta, int? steps, double q, int harmonics = 4,
                               Func<int, double, double> alpha = null)
    {
        alpha = alpha ?? Alpha;
        double rho = steps.HasValue ? 1 - Math.Exp(-2 * Math.PI * zeta * steps.Value) : 1;
        int samples = 720;
        double period = 1 / fp, sum = 0;
        for (int s = 0; s < samples; s++) // a periodic signal: the rectangle rule over a period is exact
        {
            double t = s * period / samples, a = 0;
            for (int h = 1; h <= harmonics; h++)
                for (int n = 0; n < fn.Length; n++)
                {
                    double beta = fp / fn[n];
                    double phase = Math.Atan2(-2 * h * beta * zeta, 1 - h * h * beta * beta);
                    a += share[n] * alpha(h, fp) * q * D(h, fp, fn[n], zeta) * Math.Sin(2 * Math.PI * h * fp * t + phase) * Wb(h * fp);
                }
            sum += a * a;
        }
        return rho * Math.Sqrt(sum / samples);
    }

    /// <summary>Equation 36 as P354 prints it: the modes added within a harmonic without their phases.</summary>
    static double Equation36(double fp, double[] fn, double[] share, double zeta, double q)
    {
        double sum = 0;
        for (int h = 1; h <= 4; h++)
        {
            double modes = 0;
            for (int n = 0; n < fn.Length; n++)
                modes += share[n] * Alpha(h, fp) * q * D(h, fp, fn[n], zeta) * Wb(h * fp);
            sum += modes * modes;
        }
        return Math.Sqrt(sum / 2);
    }

    /// <summary>Equations 18, 34 and 12 sampled: the modes' decaying sines added, and their rms over one pace.</summary>
    static double TransientByHand(double fp, double[] fn, double[] phi2, double zeta, double qNewtons, Func<double, double> w = null)
    {
        w = w ?? Wb;
        int samples = 200000;
        double period = 1 / fp, dt = period / samples, sum = 0;
        for (int s = 0; s <= samples; s++)
        {
            double t = s * dt, a = 0;
            for (int n = 0; n < fn.Length; n++)
            {
                double fi = 60 * Math.Pow(fp, 1.43) / Math.Pow(fn[n], 1.3) * qNewtons / 700 / 1000; // kN s
                double wd = 2 * Math.PI * fn[n] * Math.Sqrt(1 - zeta * zeta);
                a += wd * phi2[n] * fi * Math.Sin(wd * t) * Math.Exp(-zeta * 2 * Math.PI * fn[n] * t) * w(fn[n]);
            }
            double weight = s == 0 || s == samples ? 1 : s % 2 == 1 ? 4 : 2; // Simpson
            sum += weight * a * a;
        }
        return Math.Sqrt(sum * dt / 3 / period);
    }

    // ---- A simply supported beam in OpenSees ----

    // 10 m, 20 elastic beams, 0.5 t/m lumped at the nodes, moving in the X-Z plane only: kN, m and
    // tonnes, the units Alpaca4d writes in. The modes of the lumped beam are sines, so the first is
    // φ(x) = √(2 / m L) sin(π x / L) mass-normalised: 0.6325 at midspan, a modal mass of m L / 2 = 2.5 t
    // scaled to 1 there.
    const int Segments = 20;
    const double Span = 10.0, MassPerLength = 0.5, E = 2.1e8, I = 1.5e-4;

    static string Beam()
    {
        var deck = new System.Text.StringBuilder();
        deck.AppendLine("wipe\nmodel BasicBuilder -ndm 3 -ndf 6");
        double step = Span / Segments, lumped = MassPerLength * step;
        for (int i = 0; i <= Segments; i++)
            deck.AppendLine($"node {i + 1} {(i * step).ToString(CultureInfo.InvariantCulture)} 0.0 0.0");
        deck.AppendLine("fix 1 1 1 1 1 0 1");
        deck.AppendLine($"fix {Segments + 1} 0 1 1 1 0 1");
        for (int i = 2; i <= Segments; i++)
            deck.AppendLine($"fix {i} 0 1 0 1 0 1");
        // Local z along -Y makes local y global Z: the beam bends vertically on Iz, the second inertia.
        deck.AppendLine("geomTransf Linear 1 0 -1 0");
        for (int i = 1; i <= Segments; i++)
            deck.AppendLine($"element elasticBeamColumn {i} {i} {i + 1} 0.01 {E} 8.0e7 1.0e-4 {(I * 3).ToString("R", CultureInfo.InvariantCulture)} {I.ToString("R", CultureInfo.InvariantCulture)} 1");
        for (int i = 2; i <= Segments; i++)
            deck.AppendLine($"mass {i} {lumped.ToString("R", CultureInfo.InvariantCulture)} {lumped.ToString("R", CultureInfo.InvariantCulture)} {lumped.ToString("R", CultureInfo.InvariantCulture)} 0.0 0.0 0.0");
        return deck.ToString();
    }

    static string RunOpenSees(string openSees, string deck, string folder)
    {
        string file = Path.Combine(folder, "beam.tcl");
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
        var settings = new FootfallSettings(); // P354's: walking, 76 kg, Wb, 1.8 - 2.2 Hz, cut-off 10 Hz
        double q = 76 * 9.81 / 1000, qN = 76 * 9.81;

        Console.WriteLine("P354: Tables 3.1 and 3.2, the weighting, Equations 18, 30 and 37\n");

        Check("walker weight Q = 746 N", Close(settings.WalkerWeight, 745.56, 1e-9), $"{settings.WalkerWeight:0.##} N");
        double[] alphas = Enumerable.Range(1, 4).Select(h => Footfall.FourierCoefficient(FootfallActivity.Walking, h, 2.0)).ToArray();
        Check("Table 3.1 at 2 Hz: 0.4578, 0.0978, 0.0784, 0.0700",
              Close(alphas[0], 0.4578, 1e-9) && Close(alphas[1], 0.0978, 1e-9) && Close(alphas[2], 0.0784, 1e-9) && Close(alphas[3], 0.07, 1e-9),
              string.Join(" ", alphas.Select(a => a.ToString("0.####"))));
        Check("Table 3.2, stairs: 1.1 and 0.22 whatever the pace, two harmonics",
              Footfall.FourierCoefficient(FootfallActivity.Stairs, 1, 3.7) == 1.1 && Footfall.FourierCoefficient(FootfallActivity.Stairs, 2, 1.4) == 0.22 &&
              Footfall.Harmonics(FootfallActivity.Stairs) == 2 && Footfall.Harmonics(FootfallActivity.Walking) == 4);
        Check("Wb (Eq. 22): 0.4 below 2 Hz, f/5 to 5 Hz, 1 to 16 Hz, 16/f above",
              Footfall.Weight(FrequencyWeighting.Wb, 1.9) == 0.4 && Close(Footfall.Weight(FrequencyWeighting.Wb, 4.0), 0.8, 1e-12) &&
              Footfall.Weight(FrequencyWeighting.Wb, 10.0) == 1.0 && Close(Footfall.Weight(FrequencyWeighting.Wb, 32.0), 0.5, 1e-12));
        Check("Wg (Eq. 21): 0.5 √f below 4 Hz, 1 to 8 Hz, 8/f above",
              Close(Footfall.Weight(FrequencyWeighting.Wg, 1.0), 0.5, 1e-12) && Footfall.Weight(FrequencyWeighting.Wg, 6.0) == 1.0 &&
              Close(Footfall.Weight(FrequencyWeighting.Wg, 16.0), 0.5, 1e-12));
        Check("Wd (Eq. 23): 1 below 2 Hz, 2/f above", Footfall.Weight(FrequencyWeighting.Wd, 1.5) == 1.0 && Close(Footfall.Weight(FrequencyWeighting.Wd, 8.0), 0.25, 1e-12));
        Check("build-up (Eq. 37) after 17 steps at 3%: 1 - e^(-2π 0.03 17) = 0.9594; none given, 1",
              Close(Footfall.BuildUp(0.03, 17), 1 - Math.Exp(-2 * Math.PI * 0.03 * 17), 1e-12) && Footfall.BuildUp(0.03, null) == 1.0,
              $"{Footfall.BuildUp(0.03, 17):0.####}");
        Check("impulse (Eq. 18): 60 fp^1.43 / fn^1.3 Q / 700, 185 N s at 2.2 Hz on a 1 Hz mode",
              Close(Footfall.Impulse(2.2, 1.0, 700), 60 * Math.Pow(2.2, 1.43), 1e-12) && Close(Footfall.Impulse(2.2, 1.0, 700), 185, 0.002),
              $"{Footfall.Impulse(2.2, 1.0, 700):0.#} N s");
        Check("magnification (Eq. 30) 1 / 2ζ with a harmonic on the mode", Close(Footfall.Magnification(3, 2.0, 6.0, 0.03), 1 / 0.06, 1e-12));
        Check("and h²β² / √((1 - h²β²)² + (2hζβ)²) off it", Close(Footfall.Magnification(2, 1.9, 6.0, 0.03), D(2, 1.9, 6.0, 0.03), 1e-12));

        Console.WriteLine("\nP354's worked examples, one mode\n");

        // D.1, the composite floor of SCI's offices: 9.30 Hz, M = 10 226.8 kg, ζ = 4.68%, Wg. Equation 50,
        // P354's simplified steady state, puts a harmonic of coefficient 0.1 on the mode. The general method
        // does too once the pace reaches 9.30 / 4: Equation 50 with α₄ for 0.1, and a little of the other
        // harmonics off resonance.
        {
            var d1 = new FootfallSettings { Weighting = FrequencyWeighting.Wg, Damping = 0.0468, MinimumFrequency = 9.30 / 4, MaximumFrequency = 9.30 / 4, FrequencySteps = 1 };
            double m = 10.2268, f0 = 9.30;
            double eq50 = 0.1 * 0.746 * (8 / f0) / (2 * Math.Sqrt(2) * m * 0.0468);
            Check("D.1 as P354 works it, Eq. 50: 47.4e-3 m/s², R = 9.48 (W rounded to 0.86)", Close(eq50 * 0.86 / (8 / f0) / 0.005, 9.48, 0.002),
                  $"R = {eq50 * 0.86 / (8 / f0) / 0.005:0.##}");
            double general = Footfall.SteadyStateAcceleration(f0 / 4, new[] { f0 }, new[] { 1 / m }, d1);
            double expected = eq50 * Footfall.FourierCoefficient(FootfallActivity.Walking, 4, f0 / 4) / 0.1;
            Check("the general method there: Eq. 50 with α₄ = 0.0791 for 0.1, up to 2% more for harmonics 1 to 3",
                  general >= expected && general <= 1.02 * expected, $"{general * 1000:0.##}e-3 against {expected * 1000:0.##}e-3 m/s²");
        }

        // D.2, a lightweight residential floor: 13.6 Hz, M = 1 181.26 kg, Wg, walking at 2.2 Hz. Equation 60,
        // P354's simplified transient, takes the rms as the peak of Equation 33 over √2, with no decay:
        // 199.95e-3 m/s², R = 39.99. Undamped, the exact rms over one pace is the same, to the part of a
        // cycle that does not fit in it.
        {
            var d2 = new FootfallSettings { Weighting = FrequencyWeighting.Wg, Damping = 1e-9 };
            double m = 1.18126, f0 = 13.6, w = 8 / f0;
            double eq60 = 2 * Math.PI * 185 / (m * 1000 * Math.Pow(f0, 0.3)) * (746.0 / 700) / Math.Sqrt(2) * 0.59;
            Check("D.2 as P354 works it, Eq. 60: R = 39.99", Close(eq60 / 0.005, 39.99, 0.001), $"R = {eq60 / 0.005:0.##}");
            double exact = Footfall.TransientAcceleration(2.2, new[] { f0 }, new[] { 1 / m }, d2);
            // 6.18 cycles in a pace: the mean of sin² over them is ½ (1 - sin(4π f0 T) / (4π f0 T)).
            double cycles = 4 * Math.PI * f0 / 2.2, partial = Math.Sqrt(1 - Math.Sin(cycles) / cycles);
            Check("undamped, the transient rms over one pace is Eq. 60's, W = 8 / 13.6 for 0.59, less the part cycle",
                  Close(exact, eq60 * (w / 0.59) * (settings.WalkerWeight / 746) * (60 * Math.Pow(2.2, 1.43) / 185) * partial, 1e-6),
                  $"R = {exact / 0.005:0.##}, the part cycle {partial:0.####}");
            d2.Damping = 0.03;
            double damped = Footfall.TransientAcceleration(2.2, new[] { f0 }, new[] { 1 / m }, d2);
            Check("with 3% damping it dies away over the pace: less", damped < 0.7 * exact, $"R = {damped / 0.005:0.##}");
        }

        Console.WriteLine("\nOne mode, two modes, by hand\n");
        {
            double zeta = 0.03;
            settings.Damping = zeta;
            var one = new[] { 4.0 };
            var share = new[] { 1.0 / 20.0 };

            double steady = Footfall.SteadyStateAcceleration(2.0, one, share, settings);
            Check("steady state (Eq. 29, 30, 36) as written out", Close(steady, SteadyByHand(2.0, one, share, zeta, null, q), 1e-9), $"{steady:0.######} m/s²");
            settings.Footsteps = 17;
            Check("and times ρ for 17 footsteps", Close(Footfall.SteadyStateAcceleration(2.0, one, share, settings), Footfall.BuildUp(zeta, 17) * steady, 1e-12));
            settings.Footsteps = null;

            double transient = Footfall.TransientAcceleration(2.0, new[] { 9.0 }, share, settings);
            double sampled = TransientByHand(2.0, new[] { 9.0 }, share, zeta, qN);
            Check("transient (Eq. 33, 34, 12) as the sampled decaying sine", Close(transient, sampled, 1e-6), $"{transient:G6} and {sampled:G6} m/s²");

            settings.Damping = 0.011;
            var fm = new[] { 5.9, 6.3 };
            var phi2 = new[] { 1.0 / 15.0, 1.0 / 22.0 };
            double worst = settings.Paces().Max(fp => Footfall.SteadyStateAcceleration(fp, fm, phi2, settings));
            double worstByHand = settings.Paces().Max(fp => SteadyByHand(fp, fm, phi2, 0.011, null, q));
            Check("two modes: with their phases within a harmonic (Eq. 31, 32), the harmonics by SRSS", Close(worst, worstByHand, 1e-9), $"{worst:0.######} m/s²");
            double printed = settings.Paces().Max(fp => Equation36(fp, fm, phi2, 0.011, q));
            Check("which is no more than Eq. 36, the modes added without their phases, with the walker on the node",
                  worst <= printed * (1 + 1e-12), $"{worst:0.######} against {printed:0.######} m/s²");

            var high = new[] { 12.0, 14.5, 19.0 };
            var shares = new[] { 1.0 / 8.0, 1.0 / 30.0, 1.0 / 12.0 };
            double t = Footfall.TransientAcceleration(1.8, high, shares, settings), s = TransientByHand(1.8, high, shares, 0.011, qN);
            Check("three ringing modes, one past 16 Hz, added before the rms", Close(t, s, 1e-6), $"{t:G6} and {s:G6} m/s²");
        }

        Console.WriteLine("\nWhich modes, which response\n");
        {
            // The fundamental: the lowest mode that moves mostly along the direction.
            var sway = new double[,] { { 1.0, 0.0, 0.1 }, { 0.8, 0.0, 0.2 } };
            var floor = new double[,] { { 0.0, 0.1, 0.3 }, { 0.0, 0.0, 0.9 } };
            Check("a sideways first mode is not the fundamental", Footfall.Fundamental(new[] { 1.2, 7.5 }, new List<double[,]> { sway, floor }, 2) == 1);
            Check("the first mode is, when it moves vertically", Footfall.Fundamental(new[] { 7.5, 9.0 }, new List<double[,]> { floor, sway }, 2) == 0);
            Check("a mode at 0 Hz is skipped", Footfall.Fundamental(new[] { 0.0, 7.5 }, new List<double[,]> { floor, floor }, 2) == 1);

            settings.Damping = 0.03;
            var f = new[] { 0.0, 7.0, 11.5, 12.5, 14.5 };
            var modes = f.Select(_ => new[] { 0.3, 0.0 }).ToList();
            var low = Footfall.SelfExcitation(f, modes, 7.0, settings);
            Check("a 7 Hz floor is low-frequency: steady state to 12 Hz, transient to 14 Hz",
                  !low.HighFrequency && low.SteadyStateModes.SequenceEqual(new[] { 2, 3 }) && low.TransientModes.SequenceEqual(new[] { 2, 3, 4 }),
                  $"{string.Join(" ", low.SteadyStateModes)} / {string.Join(" ", low.TransientModes)}");
            Check("and R is the greater of the two at the worst pace",
                  Close(low.ResponseFactor[0], Math.Max(low.SteadyStateFactor[0], low.TransientFactor[0]), 1e-12) && low.ResponseFactor[0] > 0,
                  $"R = {low.ResponseFactor[0]:0.##} = max({low.SteadyStateFactor[0]:0.##}, {low.TransientFactor[0]:0.##})");
            Check("a node that does not move does not respond", low.ResponseFactor[1] == 0.0);

            var stiff = Footfall.SelfExcitation(f, modes, 11.5, settings);
            Check("an 11.5 Hz floor is high-frequency: R is the transient alone",
                  stiff.HighFrequency && Close(stiff.ResponseFactor[0], stiff.TransientFactor[0], 1e-12) && stiff.SteadyStateFactor[0] > 0,
                  $"R = {stiff.ResponseFactor[0]:0.##}, steady state {stiff.SteadyStateFactor[0]:0.##} not counted");

            var stair = new FootfallSettings { Activity = FootfallActivity.Stairs, CutOff = 12, Damping = 0.005, MinimumFrequency = 1.2, MaximumFrequency = 4.5, FrequencySteps = 331 };
            var stairs = Footfall.SelfExcitation(f, modes, 11.5, stair);
            Check("a stair: steady state only (SCI AD 406), modes to 14 Hz, R from it",
                  !stairs.TransientChecked && stairs.TransientModes.Length == 0 && stairs.SteadyStateModes.SequenceEqual(new[] { 2, 3, 4 }) &&
                  Close(stairs.ResponseFactor[0], stairs.SteadyStateFactor[0], 1e-12) && stairs.TransientFactor[0] == 0,
                  $"R = {stairs.ResponseFactor[0]:0.##} at {stairs.CriticalFrequency[0]:0.##} Hz");
            double stairByHand = stair.Paces().Max(fp => SteadyByHand(fp, new[] { 7.0, 11.5, 12.5 }, new[] { 0.09, 0.09, 0.09 }, 0.005, null, q, 2,
                                                                       (h, p) => h == 1 ? 1.1 : 0.22));
            Check("as by hand with Table 3.2", Close(stairs.SteadyStateAcceleration[0], stairByHand, 1e-9));

            // A stair above its 12 Hz cut-off: no transient either, so the steady state takes the modes the
            // transient would have, up to twice the fundamental - here every mode to 26 Hz, 14.5 Hz included.
            var stiffStair = Footfall.SelfExcitation(f, modes, 13.0, stair);
            Check("a stair above the cut-off: steady state over the modes to twice the fundamental, 26 Hz",
                  stiffStair.HighFrequency && !stiffStair.TransientChecked && stiffStair.SteadyStateModes.SequenceEqual(new[] { 2, 3, 4, 5 }) &&
                  Close(stiffStair.ResponseFactor[0], stiffStair.SteadyStateFactor[0], 1e-12),
                  string.Join(" ", stiffStair.SteadyStateModes));

            var paces = new FootfallSettings { MinimumFrequency = 1.8, MaximumFrequency = 2.2, FrequencySteps = 5 }.Paces();
            Check("5 paces from 1.8 to 2.2 Hz, both ends included", paces.Length == 5 && Close(paces[0], 1.8, 1e-12) && Close(paces[4], 2.2, 1e-12));
            Check("a damping of 3 rather than 0.03 is turned away", new FootfallSettings { Damping = 3 }.Problem() != null);
            Check("as is a maximum below the minimum", new FootfallSettings { MinimumFrequency = 2.2, MaximumFrequency = 1.8 }.Problem() != null);
        }

        Console.WriteLine("\nThe modes' phases, with the walker away from the response\n");
        {
            // Two modes either side of the third harmonic of 2.03 Hz, 6.09 Hz, the walker where they move the
            // same way and the response where they move opposite ways: φₑ φᵣ is + for one and - for the other.
            // The mode below the harmonic answers out of phase, the one above in phase, so the two add up.
            // Eq. 36 adds the shares as they are and takes one from the other.
            var phase = new FootfallSettings { Damping = 0.011, MinimumFrequency = 2.03, MaximumFrequency = 2.03, FrequencySteps = 1 };
            var fm = new[] { 5.95, 6.25 };
            var shares = new[] { 0.06, -0.05 };
            double got = Footfall.SteadyStateAcceleration(2.03, fm, shares, phase);
            double byHand = SteadyByHand(2.03, fm, shares, 0.011, null, q);
            double printed = Equation36(2.03, fm, shares, 0.011, q);
            Check("opposite shares either side of a harmonic: as the time history (Eq. 31, 32)", Close(got, byHand, 1e-9), $"{got:0.#####} m/s²");
            Check("which adds them: over three times what Eq. 36 gives, taking one from the other", got > 3 * printed,
                  $"{got:0.#####} against {printed:0.#####} m/s²");
            var (x, y) = Footfall.Transfer(3, 2.03, 5.95, 0.011);
            var (u, v) = Footfall.Transfer(3, 2.03, 6.25, 0.011);
            Check("D e^(iφ): out of phase below the harmonic, in phase above, |D e^(iφ)| = D (Eq. 30)",
                  x < 0 && u > 0 && y < 0 && v < 0 && Close(Math.Sqrt(x * x + y * y), D(3, 2.03, 5.95, 0.011), 1e-12));
        }

        Console.WriteLine("\nFull excitation\n");
        {
            // Six nodes, four modes: a 7 Hz floor, both responses. The node values are made up, signs and all.
            var f = new[] { 7.0, 8.2, 11.0, 13.5 };
            var modes = new List<double[]>
            {
                new[] { 0.10, 0.25, 0.32, 0.25, 0.10, 0.0 },
                new[] { 0.20, 0.28, 0.0, -0.28, -0.20, 0.0 },
                new[] { 0.22, -0.05, -0.30, -0.05, 0.22, 0.0 },
                new[] { 0.15, -0.25, 0.10, 0.25, -0.15, 0.0 },
            };
            var full = new FootfallSettings { Damping = 0.03 };
            var all = Enumerable.Range(0, 6).ToArray();
            var self = Footfall.SelfExcitation(f, modes, 7.0, full);
            var everyone = Footfall.FullExcitation(f, modes, 7.0, full, all, all);

            var diagonal = Footfall.FullExcitation(f, modes, 7.0, full, new[] { 3 }, new[] { 3 });
            Check("the walker on the response node alone: self excitation, exactly",
                  diagonal.ResponseFactor[0] == self.ResponseFactor[3] && diagonal.CriticalFrequency[0] == self.CriticalFrequency[3] &&
                  diagonal.SteadyStateFactor[0] == self.SteadyStateFactor[3] && diagonal.TransientFactor[0] == self.TransientFactor[3],
                  $"R = {diagonal.ResponseFactor[0]:0.###}");
            var some = Footfall.SelfExcitation(f, modes, 7.0, full, new[] { 4, 1 });
            Check("self excitation at some nodes: as at every node, in the order given",
                  some.ResponseFactor[0] == self.ResponseFactor[4] && some.ResponseFactor[1] == self.ResponseFactor[1] &&
                  some.ResponseNode.SequenceEqual(new[] { 4, 1 }) && some.ExcitationNode.SequenceEqual(new[] { 4, 1 }));

            var ab = Footfall.FullExcitation(f, modes, 7.0, full, new[] { 0 }, new[] { 3 });
            var ba = Footfall.FullExcitation(f, modes, 7.0, full, new[] { 3 }, new[] { 0 });
            Check("walking at A shakes B as walking at B shakes A", Close(ab.ResponseFactor[0], ba.ResponseFactor[0], 1e-12),
                  $"R = {ab.ResponseFactor[0]:0.###}");

            bool worstFound = true, noLess = true;
            for (int r = 0; r < 5; r++)
            {
                double best = -1;
                int at = -1;
                foreach (int e in all)
                {
                    double one = Footfall.FullExcitation(f, modes, 7.0, full, new[] { e }, new[] { r }).ResponseFactor[0];
                    if (one > best) { best = one; at = e; }
                }
                worstFound &= everyone.ResponseFactor[r] == best && everyone.ExcitationNode[r] == at;
                noLess &= everyone.ResponseFactor[r] >= self.ResponseFactor[r];
            }
            Check("each response node keeps its worst walker, and where it is", worstFound,
                  string.Join(" ", Enumerable.Range(0, 5).Select(r => $"{r}<-{everyone.ExcitationNode[r]}")));
            Check("never less than self excitation", noLess);
            Check("somewhere the worst walker is elsewhere", Enumerable.Range(0, 5).Any(r => everyone.ExcitationNode[r] != r));
            Check("a node that does not move does not respond, wherever the walker",
                  everyone.ResponseFactor[5] == 0.0 && everyone.SteadyStateFactor[5] == 0.0 && everyone.TransientFactor[5] == 0.0);
            Check("the transient by hand, the walker at 0 and the response at 2, signed shares",
                  Close(Footfall.FullExcitation(f, modes, 7.0, new FootfallSettings { Damping = 0.03, MinimumFrequency = 2.2, FrequencySteps = 1 },
                                                new[] { 0 }, new[] { 2 }).TransientAcceleration[0],
                        TransientByHand(2.2, f, Enumerable.Range(0, 4).Select(m => modes[m][0] * modes[m][2]).ToArray(), 0.03, qN), 1e-6));
            var only = Footfall.FullExcitation(f, modes, 7.0, full, new[] { 1, 2 });
            Check("with no response nodes given, read at the walker's", only.ResponseNode.SequenceEqual(new[] { 1, 2 }) && only.WalkedNodes == 2 && only.FullExcitation);
        }

        Console.WriteLine("\nWhere: the nodes points take, the nodes of the shells\n");
        {
            var nodes = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(2, 0, 0), new Point3d(0, 0, 3) };
            var points = new List<Point3d> { new Point3d(1.1, 0.05, 0), new Point3d(0, 0, 2.9), new Point3d(1.9, 0, 0), new Point3d(1.1, 0, 0) };
            int[] nearest = Footfall.NearestNodes(nodes, points, out double farthest);
            Check("each point takes its nearest node, in order", nearest.SequenceEqual(new[] { 1, 3, 2, 1 }), string.Join(" ", nearest));
            Check("and the farthest is 0.112 from it", Close(farthest, Math.Sqrt(0.1 * 0.1 + 0.05 * 0.05), 1e-12), $"{farthest:0.####}");

            var model = new Model
            {
                Nodes = Enumerable.Range(0, 6).Select(i => new Node(i + 1, i, 0, 0)).ToList(),
                Elements = new List<IElement> { new Quad(new int?[] { 2, 3, 5, 6 }) },
            };
            Check("the nodes of the shells, by their place in Nodes", Footfall.ShellNodes(model).SequenceEqual(new[] { 1, 2, 4, 5 }),
                  string.Join(" ", Footfall.ShellNodes(model)));
            Check("none in a model without shells", Footfall.ShellNodes(new Model { Nodes = model.Nodes }).Length == 0);
        }

        string openSees = args.Length > 0 ? args[0] : "";
        if (string.IsNullOrEmpty(openSees) || !File.Exists(openSees))
        {
            Console.WriteLine("\nNo OpenSees, so the beam is not solved.");
        }
        else
        {
            Console.WriteLine($"\nA simply supported beam, solved by {openSees}\n");
            string folder = Path.Combine(Path.GetTempPath(), "footfall-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string file = Path.Combine(folder, "modes.mpco");
                int modes = 4;
                string printed = RunOpenSees(openSees, Beam() + Recorder.MpcoEigen(file).WriteTcl() + Eigen.WriteTcl("-genBandArpack", modes) +
                                             "modalProperties -file \"ModalReport.txt\" -unorm\nrecord\nwipe\n", folder);
                var eigen = Eigen.Read(printed);
                Check($"{modes} eigenvalues", eigen != null && eigen.Count == modes, eigen == null ? printed : "");

                var nodes = Enumerable.Range(0, Segments + 1).Select(i => new Node(i + 1, i * Span / Segments, 0, 0)).ToList();
                var model = new Model
                {
                    Recorders = new List<IRecorder> { Recorder.MpcoEigen(file) },
                    IsModal = true,
                    NumberOfModes = modes,
                    Eigenvalues = eigen,
                    Nodes = nodes,
                };
                var shapes = Read.MassNormalisedModes(model);
                Check($"{modes} modes read, a row per node", shapes.Count == modes && shapes.All(s => s.GetLength(0) == nodes.Count), $"{shapes.Count} modes, eigen {string.Join(" ", eigen.Select(e => (Math.Sqrt(e) / 2 / Math.PI).ToString("0.###")))}; midspan " + string.Join(" ", shapes.Select(s => $"({s[10, 0]:0.###} {s[10, 1]:0.###} {s[10, 2]:0.###})")));

                var f = eigen.Select(e => Math.Sqrt(e) / (2 * Math.PI)).ToArray();
                double analytic = Math.PI / 2 * Math.Sqrt(E * I / (MassPerLength * Math.Pow(Span, 4)));

                // The first vertical mode: the lowest whose midspan moves in Z.
                int mid = Segments / 2;
                int first = Enumerable.Range(0, modes).First(m => Math.Abs(shapes[m][mid, 2]) > 1e-6);
                double phiMid = Math.Abs(shapes[first][mid, 2]);
                Check("the first mode near π/2 √(EI / m L⁴)", Close(f[first], analytic, 0.01), $"{f[first]:0.###} and {analytic:0.###} Hz");
                Check("mass-normalised at midspan: √(2 / m L) = 0.6325", Close(phiMid, Math.Sqrt(2 / (MassPerLength * Span)), 0.005), $"{phiMid:0.####}");
                Check("so a modal mass of m L / 2 = 2.5 t for the mode scaled to 1 there", Close(1 / (phiMid * phiMid), MassPerLength * Span / 2, 0.01),
                      $"{1 / (phiMid * phiMid):0.###} t");
                Check("a quarter span moves sin 45° of the midspan",
                      Close(Math.Abs(shapes[first][Segments / 4, 2]), phiMid * Math.Sin(Math.PI / 4), 0.005));

                // Self excitation, P354's defaults: a 3.94 Hz floor, below the 10 Hz cut-off, so both responses.
                // The steady state takes the modes to 12 Hz and the transient those to 7.9 Hz: the first
                // alone, at midspan φ² = 1 / (m L / 2).
                var footfall = new FootfallSettings { Damping = 0.03, FrequencySteps = 401 };
                var zModes = shapes.Select(s => Enumerable.Range(0, nodes.Count).Select(n => s[n, 2]).ToArray()).ToList();
                int fundamental = Footfall.Fundamental(f, shapes, 2);
                Check("the fundamental is the first vertical mode", fundamental == first, $"mode {fundamental + 1}");
                var result = Footfall.SelfExcitation(f, zModes, f[fundamental], footfall);
                Check("the first mode alone in both responses",
                      result.SteadyStateModes.SequenceEqual(new[] { first + 1 }) && result.TransientModes.SequenceEqual(new[] { first + 1 }),
                      string.Join(" ", result.SteadyStateModes));

                double phi2 = 1 / (MassPerLength * Span / 2);
                double steadyByHand = footfall.Paces().Max(fp => SteadyByHand(fp, new[] { f[first] }, new[] { phi2 }, 0.03, null, q));
                double transientByHand = footfall.Paces().Max(fp => TransientByHand(fp, new[] { f[first] }, new[] { phi2 }, 0.03, qN));
                Check("midspan steady state as by hand, with m L / 2", Close(result.SteadyStateAcceleration[mid], steadyByHand, 0.01),
                      $"{result.SteadyStateAcceleration[mid]:0.#####} m/s², R = {result.SteadyStateFactor[mid]:0.##}");
                Check("midspan transient as by hand", Close(result.TransientAcceleration[mid], transientByHand, 0.01),
                      $"{result.TransientAcceleration[mid]:0.#####} m/s², R = {result.TransientFactor[mid]:0.##}");
                Check("R the greater, at its pace", Close(result.ResponseFactor[mid], Math.Max(steadyByHand, transientByHand) / 0.005, 0.01),
                      $"R = {result.ResponseFactor[mid]:0.##} at {result.CriticalFrequency[mid]:0.###} Hz");
                Check("the supports do not respond", result.ResponseFactor[0] < 1e-9 && result.ResponseFactor[Segments] < 1e-9);
                Check("midspan responds the most", result.ResponseFactor.Max() == result.ResponseFactor[mid]);

                // Full excitation: the response at quarter span, the walker anywhere. One mode, so R goes as
                // φₑ φᵣ: the walker at midspan, R the midspan's times φ(L/4) / φ(L/2) = sin 45°, where self
                // excitation at quarter span gives sin² 45°.
                int quarter = Segments / 4;
                var every = Enumerable.Range(0, nodes.Count).ToArray();
                var felt = Footfall.FullExcitation(f, zModes, f[fundamental], footfall, every, new[] { quarter });
                double ratio = Math.Abs(shapes[first][quarter, 2] / shapes[first][mid, 2]);
                Check("full excitation at quarter span: the worst walker at midspan", felt.ExcitationNode[0] == mid,
                      $"node {felt.ExcitationNode[0] + 1}");
                Check("R the midspan's times φ(L/4) / φ(L/2), about sin 45°", Close(felt.ResponseFactor[0], result.ResponseFactor[mid] * ratio, 1e-9),
                      $"R = {felt.ResponseFactor[0]:0.##} against self {result.ResponseFactor[quarter]:0.##}, ratio {ratio:0.####}");
                Check("where self excitation there gives its square", Close(result.ResponseFactor[quarter], result.ResponseFactor[mid] * ratio * ratio, 1e-9));
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        Console.WriteLine(fails == 0 ? "\nAll checks passed." : $"\n{fails} check(s) FAILED.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
