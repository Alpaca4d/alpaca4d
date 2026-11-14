using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alpaca4d.Generic;
using Rhino.Geometry;

namespace Alpaca4d
{
    /// <summary>
    /// The frequency weighting curves of BS 6841, as SCI P354 gives them (Equations 21 to 23), which
    /// turn an acceleration at a frequency into what a person feels of it.
    /// </summary>
    public enum FrequencyWeighting
    {
        /// <summary>Vertical: offices, residential, wards, general laboratories, workshops, circulation. P354 Table 5.1.</summary>
        Wb,
        /// <summary>Vertical, critical working areas: operating theatres, precision laboratories. P354 Table 5.1.</summary>
        Wg,
        /// <summary>Horizontal.</summary>
        Wd,
        /// <summary>No weighting: every frequency counts in full, which P354 allows as conservative.</summary>
        None,
    }

    /// <summary>What the walker does, and so which of P354's Fourier coefficients load the structure.</summary>
    public enum FootfallActivity
    {
        /// <summary>Walking on a floor: P354 Table 3.1, four harmonics.</summary>
        Walking,
        /// <summary>Going up or down a stair: P354 Table 3.2, from ISO 10137, two harmonics.</summary>
        Stairs,
    }

    /// <summary>What a footfall analysis needs besides the modes. The defaults are SCI P354's for a floor.</summary>
    public sealed class FootfallSettings
    {
        public FootfallActivity Activity { get; set; } = FootfallActivity.Walking;

        /// <summary>
        /// How many steps a walker takes along the path: L_p f_p / v, the path over the stride. The
        /// steady-state response builds up over them, by P354's resonance build-up factor (Equation 37).
        /// None for a path long enough to reach the steady state, ρ = 1.
        /// </summary>
        public int? Footsteps { get; set; }

        /// <summary>The walker's mass, kg. P354 takes 76 kg, a weight Q of 746 N.</summary>
        public double WalkerMass { get; set; } = 76.0;

        public FrequencyWeighting Weighting { get; set; } = FrequencyWeighting.Wb;

        /// <summary>The slowest pace tried, footfalls per second, Hz. P354: 1.8 Hz walking (Equation 14).</summary>
        public double MinimumFrequency { get; set; } = 1.8;

        /// <summary>The fastest pace tried, Hz. P354: 2.2 Hz walking.</summary>
        public double MaximumFrequency { get; set; } = 2.2;

        /// <summary>How many paces are tried, evenly spaced from the slowest to the fastest, both included.</summary>
        public int FrequencySteps { get; set; } = 41;

        /// <summary>
        /// P354's low to high frequency cut-off, Hz (Table 6.1): 10 for general floors, 8 for enclosed
        /// spaces such as operating theatres and homes, 12 for stairs. A floor whose fundamental frequency
        /// is below it is checked for the steady-state and the transient response, one above it for the
        /// transient alone; the steady state takes the modes up to 2 Hz past it.
        /// </summary>
        public double CutOff { get; set; } = 10.0;

        /// <summary>The damping ratio of every mode: 0.03 is 3%. P354 Table 4.1.</summary>
        public double Damping { get; set; } = 0.03;

        /// <summary>The direction the walker pushes and the response is read along: 0 X, 1 Y, 2 Z.</summary>
        public int Direction { get; set; } = 2;

        /// <summary>The walker's weight Q, N.</summary>
        public double WalkerWeight => WalkerMass * Footfall.Gravity;

        /// <summary>The paces tried, Hz.</summary>
        public double[] Paces()
        {
            int steps = Math.Max(1, FrequencySteps);
            if (steps == 1 || MaximumFrequency <= MinimumFrequency)
                return new[] { MinimumFrequency };
            return Enumerable.Range(0, steps)
                .Select(i => MinimumFrequency + (MaximumFrequency - MinimumFrequency) * i / (steps - 1))
                .ToArray();
        }

        /// <summary>Why these settings cannot be used, or null when they can.</summary>
        public string Problem()
        {
            if (!(MinimumFrequency > 0.0)) return "The minimum frequency must be above 0 Hz.";
            if (MaximumFrequency < MinimumFrequency) return "The maximum frequency is below the minimum.";
            if (FrequencySteps < 1) return "Frequency Steps must be at least 1.";
            if (!(WalkerMass > 0.0)) return "The walker's mass must be above 0 kg.";
            if (!(Damping > 0.0) || Damping >= 1.0) return "The damping ratio must be between 0 and 1: 0.03 is 3%.";
            if (Footsteps.HasValue && Footsteps.Value < 1) return "The walker takes at least one step.";
            if (!(CutOff > 0.0)) return "The cut-off frequency must be above 0 Hz.";
            if (Direction < 0 || Direction > 2) return "The direction is X, Y or Z.";
            return null;
        }
    }

    /// <summary>
    /// The response of each node it was read at to a walker, at the pace - and in full excitation the
    /// place - that makes it worst, and how it was found. Every array has a value per response node, in the
    /// order of <see cref="ResponseNode"/>.
    /// </summary>
    public sealed class FootfallResult
    {
        /// <summary>The node each result is read at, by its place in the modes' nodes - those of Model.Nodes.</summary>
        public int[] ResponseNode { get; internal set; }

        /// <summary>The node the walker is at when <see cref="ResponseFactor"/> is reached: in self excitation, the response node itself.</summary>
        public int[] ExcitationNode { get; internal set; }

        /// <summary>Whether the walker was tried at every excitation node for each response node, rather than at the response node alone.</summary>
        public bool FullExcitation { get; internal set; }

        /// <summary>How many nodes the walker was tried at: in self excitation, one for each response node.</summary>
        public int WalkedNodes { get; internal set; }

        /// <summary>The response factor R: the weighted rms acceleration over P354's base value.</summary>
        public double[] ResponseFactor { get; internal set; }

        /// <summary>The pace that gives <see cref="ResponseFactor"/>, Hz.</summary>
        public double[] CriticalFrequency { get; internal set; }

        /// <summary>The response factor of the steady-state response alone, at its worst pace and walker.</summary>
        public double[] SteadyStateFactor { get; internal set; }

        /// <summary>The response factor of the transient response alone, at its worst pace and walker.</summary>
        public double[] TransientFactor { get; internal set; }

        /// <summary>The weighted rms acceleration of the steady-state response at its worst pace and walker, m/s².</summary>
        public double[] SteadyStateAcceleration { get; internal set; }

        /// <summary>The weighted rms acceleration of the transient response at its worst pace and walker, m/s².</summary>
        public double[] TransientAcceleration { get; internal set; }

        /// <summary>The fundamental frequency the modes were chosen by, Hz.</summary>
        public double Fundamental { get; internal set; }

        /// <summary>A fundamental frequency at or above the cut-off: only the transient response counts.</summary>
        public bool HighFrequency { get; internal set; }

        /// <summary>Whether the transient response was checked at all: not on a stair (SCI AD 406).</summary>
        public bool TransientChecked { get; internal set; }

        /// <summary>The modes of the steady-state response, by their number from 1: those up to the cut-off plus 2 Hz - on a stair above the cut-off, up to twice the fundamental if that is higher.</summary>
        public int[] SteadyStateModes { get; internal set; }

        /// <summary>The modes of the transient response, by their number from 1: those up to twice the fundamental.</summary>
        public int[] TransientModes { get; internal set; }
    }

    /// <summary>
    /// Footfall: how much a floor or a stair shakes under a person walking, after SCI P354, "Design of
    /// floors for vibration: a new approach" (Smith, Hicks and Devine, revised 2009), worked out mode by
    /// mode from the structure's natural modes. Equation and table numbers are P354's.
    ///
    /// The modes come mass-normalised, φᵀMφ = 1, so where P354 writes μₑ μᵣ / M - the mode's amplitude
    /// at the excitation and the response point over its modal mass - this writes φₑ φᵣ. In kN, m and
    /// tonnes, φₑ φᵣ F is an acceleration in m/s²; so forces go in kN and impulses in kN s.
    ///
    /// <list type="bullet">
    /// <item>Steady state (6.3.2): each harmonic h of the walking force, αₕ Q at h fp, drives each mode up
    /// to the cut-off plus 2 Hz by its dynamic magnification D (Equation 30), with its phase φ (Equation 32).
    /// Within a harmonic the modes are added with their phases, as P354's full time history does (6.4.1,
    /// Equation 31); the harmonics, at their own frequencies, by the root of the sum of their squares, which
    /// is that time history's rms over a pace (6.4.3, Equation 36); the whole scaled by the resonance
    /// build-up factor ρ (Equation 37). P354's Equation 36 adds the modes without their phases, which is
    /// the same or more where the walker is at the response node; where it is not, φₑ φᵣ can be negative,
    /// and adding without the phases can take away what in fact adds up.</item>
    /// <item>Transient (6.3.3): each footfall is an impulse F_I (Equation 18) that sets every mode up to
    /// twice the fundamental ringing, dying away with its damping (Equations 33, 34); the rms acceleration
    /// over one pace counts (Equation 12, T = 1 / fp).</item>
    /// </list>
    /// Below the cut-off both are checked and the greater counts; above it the transient alone (6.3.1).
    /// On a stair only the steady state is checked: SCI AD 406 finds the transient prediction does not
    /// apply to stairs. Above the cut-off it takes the modes the transient would have, up to twice the
    /// fundamental, so that the stair's first vertical modes are in it.
    ///
    /// Self excitation: the walker is at the node the response is read at, which P354 6.3.4 takes as the
    /// worst place in most cases. Full excitation: for each response node, the walker is tried at every
    /// excitation node, and the worst kept.
    /// </summary>
    public static class Footfall
    {
        public const double Gravity = 9.81;

        /// <summary>The weighted rms acceleration that is a response factor of 1, m/s²: 0.005 vertical (Equation 38), 0.00357 horizontal (Equation 39).</summary>
        public static double BaseAcceleration(int direction) => direction == 2 ? 0.005 : 0.00357;

        /// <summary>How many harmonics of the activity's force are taken: four for walking (Table 3.1), two on a stair (Table 3.2).</summary>
        public static int Harmonics(FootfallActivity activity) => activity == FootfallActivity.Stairs ? 2 : 4;

        /// <summary>
        /// The design Fourier coefficient αₕ of harmonic <paramref name="h"/> at a pace of
        /// <paramref name="pace"/> Hz. Walking, Table 3.1: a function of the harmonic's frequency h fp.
        /// Stairs, Table 3.2: 1.1 and 0.22.
        /// </summary>
        public static double FourierCoefficient(FootfallActivity activity, int h, double pace)
        {
            if (activity == FootfallActivity.Stairs)
                return h == 1 ? 1.1 : h == 2 ? 0.22 : 0.0;

            double f = h * pace;
            switch (h)
            {
                case 1: return Math.Max(0.0, 0.436 * (f - 0.95));
                case 2: return 0.006 * (f + 12.3);
                case 3: return 0.007 * (f + 5.2);
                case 4: return 0.007 * (f + 2.0);
                default: return 0.0;
            }
        }

        /// <summary>The paces the tables cover, Hz: walking 1.8 to 2.2 (Equation 14), stairs 1.2 to 4.5 (Table 3.2).</summary>
        public static (double Min, double Max) TablePaces(FootfallActivity activity) =>
            activity == FootfallActivity.Stairs ? (1.2, 4.5) : (1.8, 2.2);

        /// <summary>The weighting factor of <paramref name="curve"/> at <paramref name="frequency"/> Hz: P354 Equations 21 to 23.</summary>
        public static double Weight(FrequencyWeighting curve, double frequency)
        {
            double f = frequency;
            switch (curve)
            {
                case FrequencyWeighting.Wb:
                    if (f < 2.0) return 0.4;
                    if (f < 5.0) return f / 5.0;
                    if (f <= 16.0) return 1.0;
                    return 16.0 / f;
                case FrequencyWeighting.Wg:
                    if (f < 4.0) return 0.5 * Math.Sqrt(Math.Max(f, 0.0));
                    if (f <= 8.0) return 1.0;
                    return 8.0 / f;
                case FrequencyWeighting.Wd:
                    if (f < 2.0) return 1.0;
                    return 2.0 / f;
                default:
                    return 1.0;
            }
        }

        /// <summary>
        /// The resonance build-up factor ρ = 1 - e^(-2π ζ N), N the footsteps along the path - P354's
        /// L_p f_p / v (Equation 37). No footsteps given is a path long enough for the steady state: 1.
        /// </summary>
        public static double BuildUp(double damping, int? footsteps) =>
            footsteps.HasValue ? 1.0 - Math.Exp(-2.0 * Math.PI * damping * footsteps.Value) : 1.0;

        /// <summary>
        /// The dynamic magnification factor for acceleration of harmonic <paramref name="h"/> of a pace of
        /// <paramref name="pace"/> Hz on a mode of <paramref name="modeFrequency"/> Hz, P354 Equation 30:
        /// h²β² / √((1 - h²β²)² + (2hζβ)²), β = fp / fn.
        /// </summary>
        public static double Magnification(int h, double pace, double modeFrequency, double damping)
        {
            double r = h * pace / modeFrequency;
            double a = 1.0 - r * r, b = 2.0 * damping * r;
            return r * r / Math.Sqrt(a * a + b * b);
        }

        /// <summary>
        /// The dynamic magnification for acceleration of Equation 30 together with its phase φ of Equation 32,
        /// as a real and an imaginary part: D e^(iφ) = h²β² / (1 - h²β² + i 2hζβ), β = fp / fn, so that
        /// tan φ = -2hζβ / (1 - h²β²), -π ≤ φ ≤ 0. A mode below the harmonic answers it out of phase, one above
        /// it in phase.
        /// </summary>
        public static (double Real, double Imaginary) Transfer(int h, double pace, double modeFrequency, double damping)
        {
            double r = h * pace / modeFrequency;
            double a = 1.0 - r * r, b = 2.0 * damping * r;
            double scale = r * r / (a * a + b * b);
            return (scale * a, -scale * b);
        }

        /// <summary>
        /// The equivalent impulse of a footfall on a mode of <paramref name="modeFrequency"/> Hz at a pace
        /// of <paramref name="pace"/> Hz, by a walker of weight <paramref name="weight"/> N, in N s:
        /// P354 Equation 18, 60 fp^1.43 / fn^1.3 · Q / 700.
        /// </summary>
        public static double Impulse(double pace, double modeFrequency, double weight) =>
            60.0 * Math.Pow(pace, 1.43) / Math.Pow(modeFrequency, 1.3) * weight / 700.0;

        /// <summary>
        /// The weighted rms acceleration of the steady-state response, m/s², for a walker and a response node
        /// whose share of each mode - φₑ φᵣ, mass-normalised, in 1/t - is <paramref name="shares"/>, at a pace
        /// of <paramref name="pace"/> Hz: P354 Equations 29, 30 and 32, the modes added with their phases
        /// (Equation 31) and the harmonics by SRSS (Equation 36), times ρ.
        /// </summary>
        public static double SteadyStateAcceleration(double pace, double[] frequencies, double[] shares, FootfallSettings settings)
        {
            double q = settings.WalkerWeight / 1000.0; // kN
            double sumOfSquares = 0.0;

            for (int h = 1; h <= Harmonics(settings.Activity); h++)
            {
                double force = FourierCoefficient(settings.Activity, h, pace) * q * Weight(settings.Weighting, h * pace);
                double real = 0.0, imaginary = 0.0;
                for (int n = 0; n < frequencies.Length; n++)
                {
                    var (x, y) = Transfer(h, pace, frequencies[n], settings.Damping);
                    real += shares[n] * x;
                    imaginary += shares[n] * y;
                }
                sumOfSquares += force * force * (real * real + imaginary * imaginary);
            }

            return BuildUp(settings.Damping, settings.Footsteps) * Math.Sqrt(sumOfSquares / 2.0);
        }

        /// <summary>
        /// The weighted rms acceleration of the transient response over one pace, m/s², at a node whose
        /// share of each mode is <paramref name="shares"/>, at a pace of <paramref name="pace"/> Hz: each
        /// mode rings from P354 Equation 33's peak, the modes add (Equation 34), and the rms is taken over
        /// T = 1 / fp (Equation 12).
        /// </summary>
        public static double TransientAcceleration(double pace, double[] frequencies, double[] shares, FootfallSettings settings)
        {
            var ringing = Ringing(TransientPeaks(pace, frequencies, settings), TransientIntegrals(pace, frequencies, settings.Damping));
            return Math.Sqrt(Math.Max(0.0, Quadratic(ringing, shares) * pace));
        }

        /// <summary>
        /// gᵢ gⱼ Kᵢⱼ over the upper triangle, row by row, doubled off the diagonal: the mean square of the
        /// transient over a pace per pair of shares, so that it is Σᵢ sᵢ Σⱼ≥ᵢ Gᵢⱼ sⱼ times the pace.
        /// </summary>
        private static double[] Ringing(double[] g, double[,] k)
        {
            int n = g.Length;
            var packed = new double[n * (n + 1) / 2];
            int at = 0;
            for (int i = 0; i < n; i++)
                for (int j = i; j < n; j++)
                    packed[at++] = (i == j ? 1.0 : 2.0) * g[i] * g[j] * k[i, j];
            return packed;
        }

        /// <summary>Σᵢ sᵢ Σⱼ≥ᵢ Gᵢⱼ sⱼ, G packed by <see cref="Ringing"/>, starting at <paramref name="offset"/>.</summary>
        private static double Quadratic(double[] packed, double[] s, int offset = 0)
        {
            int n = s.Length, at = offset;
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double row = 0.0;
                for (int j = i; j < n; j++)
                    row += packed[at++] * s[j];
                sum += s[i] * row;
            }
            return sum;
        }

        /// <summary>Each mode's weighted peak acceleration per unit share, Equation 33 over φₑ φᵣ: 2π fn √(1 - ζ²) F_I W(fn), F_I in kN s.</summary>
        private static double[] TransientPeaks(double pace, double[] frequencies, FootfallSettings settings) =>
            frequencies.Select(f => 2.0 * Math.PI * f * Math.Sqrt(1.0 - settings.Damping * settings.Damping)
                                    * Impulse(pace, f, settings.WalkerWeight) / 1000.0
                                    * Weight(settings.Weighting, f)).ToArray();

        /// <summary>
        /// ∫₀ᵀ e^(-σᵢt) sin(ωᵢt) e^(-σⱼt) sin(ωⱼt) dt for every pair of modes, ω the damped circular
        /// frequency 2π fn √(1 - ζ²), σ = 2π fn ζ, T one pace: the mean square of Equation 34's sum,
        /// worked out exactly rather than sampled.
        /// </summary>
        private static double[,] TransientIntegrals(double pace, double[] frequencies, double damping)
        {
            int n = frequencies.Length;
            double period = 1.0 / pace, damped = Math.Sqrt(1.0 - damping * damping);
            var k = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = i; j < n; j++)
                {
                    double wi = 2.0 * Math.PI * frequencies[i], wj = 2.0 * Math.PI * frequencies[j];
                    double s = damping * (wi + wj);
                    double di = wi * damped, dj = wj * damped;
                    // sin a sin b = (cos(a - b) - cos(a + b)) / 2
                    double value = 0.5 * (DecayingCosine(s, di - dj, period) - DecayingCosine(s, di + dj, period));
                    k[i, j] = value;
                    k[j, i] = value;
                }
            return k;
        }

        /// <summary>∫₀ᵀ e^(-s t) cos(c t) dt.</summary>
        private static double DecayingCosine(double s, double c, double period)
        {
            double denominator = s * s + c * c;
            if (denominator == 0.0) return period;
            double decay = Math.Exp(-s * period);
            return (decay * (c * Math.Sin(c * period) - s * Math.Cos(c * period)) + s) / denominator;
        }

        /// <summary>
        /// The mode P354 calls the fundamental: the lowest that moves mostly along
        /// <paramref name="direction"/> - its largest translation along it at least as big as along either
        /// other axis - so a building's sway or a stair's sideways mode below the floor's first vertical
        /// mode is not taken for it. The first mode when none does. Index from 0; -1 when there are no modes.
        /// </summary>
        /// <param name="shapes">Each mode's translations, a row per node, X Y Z.</param>
        public static int Fundamental(IList<double> frequencies, IList<double[,]> shapes, int direction)
        {
            int first = -1;
            for (int m = 0; m < Math.Min(frequencies.Count, shapes.Count); m++)
            {
                if (!(frequencies[m] > 0.0)) continue;
                if (first < 0) first = m;

                var largest = new double[3];
                var shape = shapes[m];
                for (int i = 0; i < shape.GetLength(0); i++)
                    for (int j = 0; j < Math.Min(3, shape.GetLength(1)); j++)
                        largest[j] = Math.Max(largest[j], Math.Abs(shape[i, j]));

                if (largest[direction] > 0.0 && Enumerable.Range(0, 3).All(j => largest[direction] >= largest[j]))
                    return m;
            }
            return first;
        }

        /// <summary>
        /// Self excitation: each node's response to a walker at that node, at the worst pace of
        /// <see cref="FootfallSettings.Paces"/>.
        /// </summary>
        /// <param name="frequencies">The frequency of each mode, Hz, lowest first.</param>
        /// <param name="modes">Each mode's mass-normalised translation along the settings' direction at each node, [mode][node], 1/√t.</param>
        /// <param name="fundamental">The fundamental frequency, Hz - see <see cref="Fundamental"/>.</param>
        /// <param name="nodes">The nodes walked on and read, by their place in each mode's array; every node when null.</param>
        public static FootfallResult SelfExcitation(IList<double> frequencies, IList<double[]> modes, double fundamental, FootfallSettings settings,
                                                    IList<int> nodes = null)
        {
            var plan = new Responses(frequencies, modes, fundamental, settings);
            nodes = nodes ?? Enumerable.Range(0, plan.Nodes).ToArray();
            plan.Check(nodes);
            var result = plan.Result(nodes, full: false, walked: nodes.Count);

            Parallel.For(0, nodes.Count, () => plan.Workspace(), (slot, _, work) =>
            {
                // φ² for each mode: the walker and the response at the same node.
                plan.Shares(nodes[slot], nodes[slot], work, 0);
                plan.Respond(work, 1);
                plan.Keep(result, slot, nodes[slot], work, 0);
                return work;
            }, _ => { });

            return result;
        }

        /// <summary>
        /// Full excitation: each response node's response to a walker at each excitation node in turn, at
        /// every pace of <see cref="FootfallSettings.Paces"/>; the worst is kept, with the node the walker
        /// was at. Each pair costs what a node of <see cref="SelfExcitation"/> does, so it takes as long as
        /// self excitation at as many nodes as there are pairs.
        /// </summary>
        /// <param name="excitation">The nodes the walker is tried at, by their place in each mode's array.</param>
        /// <param name="response">The nodes the response is read at; the excitation nodes when null.</param>
        public static FootfallResult FullExcitation(IList<double> frequencies, IList<double[]> modes, double fundamental, FootfallSettings settings,
                                                    IList<int> excitation, IList<int> response = null)
        {
            if (excitation == null || excitation.Count == 0)
                throw new ArgumentException("Full excitation needs at least one node for the walker.");
            response = response ?? excitation;

            var plan = new Responses(frequencies, modes, fundamental, settings);
            plan.Check(excitation);
            plan.Check(response);
            var result = plan.Result(response, full: true, walked: excitation.Count);

            Parallel.For(0, response.Count, () => plan.Workspace(), (slot, _, work) =>
            {
                for (int first = 0; first < excitation.Count; first += Responses.Block)
                {
                    int count = Math.Min(Responses.Block, excitation.Count - first);
                    for (int w = 0; w < count; w++)
                        plan.Shares(excitation[first + w], response[slot], work, w);
                    plan.Respond(work, count);
                    for (int w = 0; w < count; w++)
                        plan.Keep(result, slot, excitation[first + w], work, w);
                }
                return work;
            }, _ => { });

            return result;
        }

        /// <summary>
        /// The nodes of the shell elements, by their place in <paramref name="model"/>'s Nodes, in that
        /// order: where people walk on a floor or a stair modelled as shells - its slabs, treads and
        /// landings - and not its beams and columns. Empty when it has none.
        /// </summary>
        public static int[] ShellNodes(Model model)
        {
            var place = new Dictionary<int, int>();
            for (int i = 0; i < model.Nodes.Count; i++)
                place[model.Nodes[i].Id ?? i + 1] = i;

            var on = new bool[model.Nodes.Count];
            foreach (var shell in model.Elements.OfType<IShell>())
            {
                if (shell.IndexNodes == null) continue;
                foreach (var tag in shell.IndexNodes)
                    if (tag.HasValue && place.TryGetValue(tag.Value, out int i))
                        on[i] = true;
            }
            return Enumerable.Range(0, on.Length).Where(i => on[i]).ToArray();
        }

        /// <summary>
        /// The node nearest each point, by its place in <paramref name="nodes"/>, and in
        /// <paramref name="farthest"/> how far the farthest point is from its node.
        /// </summary>
        public static int[] NearestNodes(IList<Point3d> nodes, IList<Point3d> points, out double farthest)
        {
            if (nodes.Count == 0)
                throw new ArgumentException("There are no nodes to take.");

            var nearest = new int[points.Count];
            var distance = new double[points.Count];
            Parallel.For(0, points.Count, i =>
            {
                var point = points[i];
                double best = double.PositiveInfinity;
                for (int n = 0; n < nodes.Count; n++)
                {
                    double d = point.DistanceToSquared(nodes[n]);
                    if (d < best)
                    {
                        best = d;
                        nearest[i] = n;
                    }
                }
                distance[i] = Math.Sqrt(best);
            });
            farthest = distance.Length == 0 ? 0.0 : distance.Max();
            return nearest;
        }

        /// <summary>
        /// The modes each response takes and their coefficients at every pace, worked out once for every
        /// pair of nodes: the steady state's αₕ Q W D e^(iφ) of each harmonic and mode, the transient's
        /// gᵢ gⱼ Kᵢⱼ of each pair of modes.
        /// </summary>
        private sealed class Responses
        {
            public readonly int Nodes;
            private readonly IList<double[]> modes;
            private readonly int[] steady, transient;
            private readonly double[] paces;
            private readonly int harmonics, packed;
            private readonly double[] real, imaginary, ringing;
            private readonly double buildUp, baseValue, fundamental;
            private readonly bool high, stairs, countSteady;

            /// <summary>
            /// How many walkers are worked out together for one response node. Their sums are kept apart and
            /// in the same order as one walker's, so the results are the same to the last bit; together, each
            /// coefficient is read once for all of them, and the sums do not wait on one another.
            /// </summary>
            public const int Block = 4;

            /// <summary>
            /// The shares of up to <see cref="Block"/> walkers with one response node - φₑ φᵣ of each mode each
            /// response takes, [mode * Block + walker] - and their responses at every pace, [walker * paces + pace].
            /// </summary>
            public sealed class Work
            {
                public double[] Steady, Transient, SteadyAcceleration, TransientAcceleration;
            }

            public Responses(IList<double> frequencies, IList<double[]> modes, double fundamental, FootfallSettings settings)
            {
                string problem = settings.Problem();
                if (problem != null)
                    throw new ArgumentException(problem);
                if (frequencies.Count != modes.Count)
                    throw new ArgumentException("A frequency is needed for every mode.");

                this.modes = modes;
                this.fundamental = fundamental;
                Nodes = modes.Count == 0 ? 0 : modes[0].Length;
                stairs = settings.Activity == FootfallActivity.Stairs;

                // A mode at 0 Hz is a mechanism, and is never taken.
                int[] Below(double limit) => Enumerable.Range(0, frequencies.Count)
                    .Where(m => frequencies[m] > 0.0 && frequencies[m] <= limit + 1e-9)
                    .ToArray();
                high = fundamental >= settings.CutOff;
                // A stair above the cut-off is still checked for its steady state, in place of the transient
                // SCI AD 406 drops: over the modes the transient would have taken, up to twice the fundamental,
                // or its own modes - the cut-off plus 2 Hz - would leave the stair's first bounce out.
                steady = Below(stairs && high ? Math.Max(settings.CutOff + 2.0, 2.0 * fundamental) : settings.CutOff + 2.0);
                transient = stairs ? new int[0] : Below(2.0 * fundamental);

                // A stair is checked for its steady state whatever its fundamental: the transient does not apply.
                countSteady = stairs || !high;
                baseValue = BaseAcceleration(settings.Direction);
                buildUp = BuildUp(settings.Damping, settings.Footsteps);
                paces = settings.Paces();
                harmonics = Harmonics(settings.Activity);

                int ns = steady.Length, nt = transient.Length;
                double q = settings.WalkerWeight / 1000.0; // kN
                real = new double[paces.Length * harmonics * ns];
                imaginary = new double[real.Length];
                for (int p = 0; p < paces.Length; p++)
                    for (int h = 1; h <= harmonics; h++)
                    {
                        double force = FourierCoefficient(settings.Activity, h, paces[p]) * q * Weight(settings.Weighting, h * paces[p]);
                        for (int n = 0; n < ns; n++)
                        {
                            var (x, y) = Transfer(h, paces[p], frequencies[steady[n]], settings.Damping);
                            int at = (p * harmonics + h - 1) * ns + n;
                            real[at] = force * x;
                            imaginary[at] = force * y;
                        }
                    }

                var fTransient = transient.Select(m => frequencies[m]).ToArray();
                packed = nt * (nt + 1) / 2;
                ringing = new double[paces.Length * packed];
                for (int p = 0; p < paces.Length; p++)
                    Array.Copy(Ringing(TransientPeaks(paces[p], fTransient, settings), TransientIntegrals(paces[p], fTransient, settings.Damping)),
                               0, ringing, p * packed, packed);
            }

            public void Check(IList<int> nodes)
            {
                foreach (int node in nodes)
                    if (node < 0 || node >= Nodes)
                        throw new ArgumentException($"Node {node} is not one of the {Nodes} the modes have.");
            }

            public FootfallResult Result(IList<int> response, bool full, int walked)
            {
                int count = response.Count;
                return new FootfallResult
                {
                    ResponseNode = response.ToArray(),
                    ExcitationNode = response.ToArray(),
                    FullExcitation = full,
                    WalkedNodes = walked,
                    ResponseFactor = Enumerable.Repeat(-1.0, count).ToArray(),
                    CriticalFrequency = new double[count],
                    SteadyStateFactor = new double[count],
                    TransientFactor = new double[count],
                    SteadyStateAcceleration = new double[count],
                    TransientAcceleration = new double[count],
                    Fundamental = fundamental,
                    HighFrequency = high,
                    TransientChecked = !stairs,
                    SteadyStateModes = steady.Select(m => m + 1).ToArray(),
                    TransientModes = transient.Select(m => m + 1).ToArray(),
                };
            }

            public Work Workspace() => new Work
            {
                Steady = new double[steady.Length * Block],
                Transient = new double[transient.Length * Block],
                SteadyAcceleration = new double[Block * paces.Length],
                TransientAcceleration = new double[Block * paces.Length],
            };

            /// <summary>φₑ φᵣ of each mode, signed, for walker <paramref name="w"/> of the block: a mode that moves the two nodes opposite ways takes away.</summary>
            public void Shares(int excitation, int response, Work work, int w)
            {
                for (int n = 0; n < steady.Length; n++)
                    work.Steady[n * Block + w] = modes[steady[n]][excitation] * modes[steady[n]][response];
                for (int n = 0; n < transient.Length; n++)
                    work.Transient[n * Block + w] = modes[transient[n]][excitation] * modes[transient[n]][response];
            }

            /// <summary>The steady-state and transient accelerations at every pace of the first <paramref name="count"/> walkers of the block.</summary>
            public void Respond(Work work, int count)
            {
                if (count == Block)
                    RespondFour(work);
                else
                    for (int w = 0; w < count; w++)
                        RespondOne(work, w);
            }

            private void RespondOne(Work work, int w)
            {
                int ns = steady.Length, nt = transient.Length;
                var s = work.Steady;
                var t = work.Transient;
                for (int p = 0; p < paces.Length; p++)
                {
                    double a = 0.0;
                    if (ns > 0)
                    {
                        double sumOfSquares = 0.0;
                        for (int h = 0; h < harmonics; h++)
                        {
                            int at = (p * harmonics + h) * ns;
                            double x = 0.0, y = 0.0;
                            for (int n = 0; n < ns; n++)
                            {
                                double share = s[n * Block + w];
                                x += share * real[at + n];
                                y += share * imaginary[at + n];
                            }
                            sumOfSquares += x * x + y * y;
                        }
                        a = buildUp * Math.Sqrt(sumOfSquares / 2.0);
                    }

                    double b = 0.0;
                    if (nt > 0)
                    {
                        int at = p * packed;
                        double sum = 0.0;
                        for (int i = 0; i < nt; i++)
                        {
                            double row = 0.0;
                            for (int j = i; j < nt; j++)
                                row += ringing[at++] * t[j * Block + w];
                            sum += t[i * Block + w] * row;
                        }
                        b = Math.Sqrt(Math.Max(0.0, sum * paces[p]));
                    }

                    work.SteadyAcceleration[w * paces.Length + p] = a;
                    work.TransientAcceleration[w * paces.Length + p] = b;
                }
            }

            /// <summary><see cref="RespondOne"/> for the four walkers of the block at once, each summed in the same order.</summary>
            private void RespondFour(Work work)
            {
                int ns = steady.Length, nt = transient.Length, count = paces.Length;
                var s = work.Steady;
                var t = work.Transient;
                for (int p = 0; p < count; p++)
                {
                    double a0 = 0.0, a1 = 0.0, a2 = 0.0, a3 = 0.0;
                    if (ns > 0)
                    {
                        double q0 = 0.0, q1 = 0.0, q2 = 0.0, q3 = 0.0;
                        for (int h = 0; h < harmonics; h++)
                        {
                            int at = (p * harmonics + h) * ns;
                            double x0 = 0.0, x1 = 0.0, x2 = 0.0, x3 = 0.0, y0 = 0.0, y1 = 0.0, y2 = 0.0, y3 = 0.0;
                            for (int n = 0; n < ns; n++)
                            {
                                double re = real[at + n], im = imaginary[at + n];
                                int k = n * Block;
                                double s0 = s[k], s1 = s[k + 1], s2 = s[k + 2], s3 = s[k + 3];
                                x0 += s0 * re; y0 += s0 * im;
                                x1 += s1 * re; y1 += s1 * im;
                                x2 += s2 * re; y2 += s2 * im;
                                x3 += s3 * re; y3 += s3 * im;
                            }
                            q0 += x0 * x0 + y0 * y0;
                            q1 += x1 * x1 + y1 * y1;
                            q2 += x2 * x2 + y2 * y2;
                            q3 += x3 * x3 + y3 * y3;
                        }
                        a0 = buildUp * Math.Sqrt(q0 / 2.0);
                        a1 = buildUp * Math.Sqrt(q1 / 2.0);
                        a2 = buildUp * Math.Sqrt(q2 / 2.0);
                        a3 = buildUp * Math.Sqrt(q3 / 2.0);
                    }

                    double b0 = 0.0, b1 = 0.0, b2 = 0.0, b3 = 0.0;
                    if (nt > 0)
                    {
                        int at = p * packed;
                        double m0 = 0.0, m1 = 0.0, m2 = 0.0, m3 = 0.0;
                        for (int i = 0; i < nt; i++)
                        {
                            double r0 = 0.0, r1 = 0.0, r2 = 0.0, r3 = 0.0;
                            for (int j = i; j < nt; j++)
                            {
                                double g = ringing[at++];
                                int k = j * Block;
                                r0 += g * t[k];
                                r1 += g * t[k + 1];
                                r2 += g * t[k + 2];
                                r3 += g * t[k + 3];
                            }
                            int l = i * Block;
                            m0 += t[l] * r0;
                            m1 += t[l + 1] * r1;
                            m2 += t[l + 2] * r2;
                            m3 += t[l + 3] * r3;
                        }
                        b0 = Math.Sqrt(Math.Max(0.0, m0 * paces[p]));
                        b1 = Math.Sqrt(Math.Max(0.0, m1 * paces[p]));
                        b2 = Math.Sqrt(Math.Max(0.0, m2 * paces[p]));
                        b3 = Math.Sqrt(Math.Max(0.0, m3 * paces[p]));
                    }

                    var steadyOut = work.SteadyAcceleration;
                    var transientOut = work.TransientAcceleration;
                    steadyOut[p] = a0; steadyOut[count + p] = a1; steadyOut[2 * count + p] = a2; steadyOut[3 * count + p] = a3;
                    transientOut[p] = b0; transientOut[count + p] = b1; transientOut[2 * count + p] = b2; transientOut[3 * count + p] = b3;
                }
            }

            /// <summary>
            /// Walker <paramref name="w"/>'s responses at every pace, the walker at <paramref name="excitation"/>:
            /// whatever is worse than the result in <paramref name="slot"/> so far replaces it.
            /// </summary>
            public void Keep(FootfallResult result, int slot, int excitation, Work work, int w)
            {
                for (int p = 0; p < paces.Length; p++)
                {
                    double a = work.SteadyAcceleration[w * paces.Length + p];
                    double b = work.TransientAcceleration[w * paces.Length + p];

                    if (a > result.SteadyStateAcceleration[slot])
                    {
                        result.SteadyStateAcceleration[slot] = a;
                        result.SteadyStateFactor[slot] = a / baseValue;
                    }
                    if (b > result.TransientAcceleration[slot])
                    {
                        result.TransientAcceleration[slot] = b;
                        result.TransientFactor[slot] = b / baseValue;
                    }

                    double r = Math.Max(countSteady ? a : 0.0, b) / baseValue;
                    if (r > result.ResponseFactor[slot])
                    {
                        result.ResponseFactor[slot] = r;
                        result.CriticalFrequency[slot] = paces[p];
                        result.ExcitationNode[slot] = excitation;
                    }
                }
            }
        }
    }
}
