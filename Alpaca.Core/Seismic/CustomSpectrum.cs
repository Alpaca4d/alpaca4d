using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Seismic
{
    /// <summary>
    /// A spectrum given point by point: a site-specific study, a National Annex not built in, a spectrum
    /// copied from another program. Straight between the points, and flat past the first and the last,
    /// as OpenSees reads one.
    /// </summary>
    public class CustomSpectrum : ResponseSpectrum
    {
        public override string Code => "Custom";

        public IReadOnlyList<double> Periods { get; }

        /// <summary>One per period, in g.</summary>
        public IReadOnlyList<double> Accelerations { get; }

        public CustomSpectrum(IList<double> periods, IList<double> accelerations, double dampingPercent = 5.0)
        {
            if (periods == null || accelerations == null || periods.Count == 0)
                throw new ArgumentException("Give at least one period and its acceleration.");
            if (periods.Count != accelerations.Count)
                throw new ArgumentException($"{periods.Count} periods and {accelerations.Count} accelerations. Give one acceleration per period.");
            for (int i = 0; i < periods.Count; i++)
            {
                if (!(periods[i] >= 0.0))
                    throw new ArgumentException($"Period {i} is {Number(periods[i])}. Periods are in seconds, zero or more.");
                if (i > 0 && !(periods[i] > periods[i - 1]))
                    throw new ArgumentException($"Period {i} is {Number(periods[i])}, after {Number(periods[i - 1])}. List the periods from the shortest up, each once.");
                if (!(accelerations[i] >= 0.0))
                    throw new ArgumentException($"Acceleration {i} is {Number(accelerations[i])}. Accelerations are in g, zero or more.");
            }

            Periods = periods.ToList();
            Accelerations = accelerations.ToList();
            Damping = DampingRatio(dampingPercent);
        }

        public override IEnumerable<double> Corners => Periods;

        public override double At(double period)
        {
            int n = Periods.Count;
            if (n == 1 || period <= Periods[0])
                return Accelerations[0];
            if (period >= Periods[n - 1])
                return Accelerations[n - 1];

            int i = 1;
            while (Periods[i] < period)
                i++;
            double share = (period - Periods[i - 1]) / (Periods[i] - Periods[i - 1]);
            return Accelerations[i - 1] + share * (Accelerations[i] - Accelerations[i - 1]);
        }

        public override List<string> Parameters => new List<string>
        {
            $"Custom spectrum, {Periods.Count} point{(Periods.Count == 1 ? "" : "s")}, ξ = {Number(Damping * 100.0)}%",
            $"T from {Number(Periods[0])} to {Number(Periods[Periods.Count - 1])} s",
            $"Sa from {Number(Accelerations.Min())} to {Number(Accelerations.Max())} g",
        };
    }
}
