using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Alpaca4d.Seismic
{
    /// <summary>Which component of the ground motion a spectrum describes.</summary>
    public enum SpectrumComponent
    {
        Horizontal,
        Vertical,
    }

    /// <summary>
    /// A response spectrum: the peak acceleration of a single oscillator, by its period. What Design
    /// Spectrum hands to Response Spectrum Analysis, which reads it at the period of every mode.
    ///
    /// Ordinates are in g, the way the codes and the hazard maps give them; the analysis turns them into
    /// the model's own acceleration. Each code is a class of its own, which works out its corner periods
    /// once and is read as often as there are modes.
    /// </summary>
    public abstract class ResponseSpectrum
    {
        /// <summary>
        /// The acceleration of gravity a spectrum's g stands for, in m/s². NTC 2018 sets 9.81 (under
        /// Tab. 3.2.IV), and Alpaca4d's self-weight uses the same.
        /// </summary>
        public const double G = 9.81;

        /// <summary>One g in the model's own acceleration unit: 9.81 with lengths in m.</summary>
        public static double ModelG => G * 1000.0 / ModelLength.MillimetresPerUnit;

        /// <summary>The code the spectrum is drawn to, for messages and the Parameters output.</summary>
        public abstract string Code { get; }

        /// <summary>
        /// The viscous damping ratio of the structure, 0.05 for 5%. The analysis correlates the modes
        /// with it (CQC); an elastic spectrum is also scaled by it.
        /// </summary>
        public double Damping { get; protected set; } = 0.05;

        /// <summary>The spectral acceleration at <paramref name="period"/> seconds, in g.</summary>
        public abstract double At(double period);

        /// <summary>
        /// How much of the mass the modes should move together, as a ratio: EN 1998-1 4.3.3.3.1 asks
        /// for 90%, NTC 2018 7.3.3.1 for 85%. Response Spectrum Analysis warns below it.
        /// </summary>
        public virtual double MassRatioRequired => 0.90;

        /// <summary>The periods where the curve changes shape, which a drawing of it must pass through.</summary>
        public virtual IEnumerable<double> Corners => Enumerable.Empty<double>();

        /// <summary>What the spectrum was worked out with, one "name = value" per line.</summary>
        public abstract List<string> Parameters { get; }

        /// <summary>
        /// The curve at a period every <paramref name="step"/> seconds up to <paramref name="until"/>,
        /// and at every corner: periods ascending, accelerations in g. For drawing and reading off; the
        /// analysis reads <see cref="At"/> at each mode instead.
        /// </summary>
        public (List<double> Periods, List<double> Accelerations) Sample(double until, double step = 0.01)
        {
            if (!(until > 0.0)) until = 4.0;
            if (!(step > 0.0)) step = 0.01;

            int count = (int)Math.Round(until / step);
            var periods = Enumerable.Range(0, count + 1).Select(i => Math.Min(until, i * step))
                .Concat(Corners.Where(t => t > 0.0 && t < until))
                .Select(t => Math.Round(t, 9))
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            return (periods, periods.Select(At).ToList());
        }

        public override string ToString() => $"Response Spectrum ({Code})";

        /// <summary>
        /// The damping ratio from a percentage: positive, and as a ratio from here on. A value at or
        /// below 1 was almost certainly meant as a ratio already, so it is refused rather than read as
        /// a damping of 0.05%.
        /// </summary>
        protected static double DampingRatio(double percent)
        {
            if (!(percent > 0.0))
                throw new ArgumentException($"Damping is {Number(percent)}%. It is a percentage greater than zero: 5 for 5%.");
            if (percent <= 1.0)
                throw new ArgumentException($"Damping is {Number(percent)}%. It is a percentage, 5 for 5%, not a ratio.");
            return percent / 100.0;
        }

        /// <summary>
        /// The factor that scales an elastic spectrum for damping other than 5%: η = √(10 / (5 + ξ)),
        /// ξ in percent, never below 0.55. The same in EN 1998-1 (3.6) and NTC 2018 [3.2.4].
        /// </summary>
        public static double Eta(double damping) => Math.Max(0.55, Math.Sqrt(10.0 / (5.0 + 100.0 * damping)));

        /// <summary>A behaviour factor, checked: one or more, or none for the elastic spectrum.</summary>
        protected static double? BehaviourFactor(double? q)
        {
            if (q.HasValue && !(q.Value >= 1.0))
                throw new ArgumentException($"q is {Number(q.Value)}. The behaviour factor is 1 or more; leave it empty for the elastic spectrum.");
            return q;
        }

        /// <summary>A peak ground acceleration in g, checked: zero or more, and below 2 g.</summary>
        protected static double GroundAcceleration(double ag, string name)
        {
            if (!(ag >= 0.0))
                throw new ArgumentException($"{name} is {Number(ag)}. It is an acceleration in g, zero or more.");
            if (ag >= 2.0)
                throw new ArgumentException($"{name} is {Number(ag)} g. It is in g, not m/s²: 0.25 for a quarter of g.");
            return ag;
        }

        /// <summary>One of <paramref name="allowed"/>, ignoring case and spaces, or an error naming them.</summary>
        protected static string OneOf(string text, string name, params string[] allowed)
        {
            string value = (text ?? "").Trim();
            string match = allowed.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw new ArgumentException($"{name} is \"{text}\". It is one of {string.Join(", ", allowed)}.");
            return match;
        }

        protected static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        protected static string Line(string name, double value, string unit = "") =>
            $"{name} = {value.ToString("0.####", CultureInfo.InvariantCulture)}{(unit.Length > 0 ? " " + unit : "")}";
    }
}
