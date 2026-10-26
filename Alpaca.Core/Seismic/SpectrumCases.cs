using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Seismic
{
    /// <summary>How the responses of the modes to one direction of shaking are put together.</summary>
    public enum ModalCombination
    {
        /// <summary>
        /// Complete quadratic combination: √(Σi Σj ρij ri rj). What NTC 2018 7.3.3.1 asks for, and what
        /// EN 1998-1 4.3.3.3.2 asks for when two periods are within 10% of each other.
        /// </summary>
        CQC,

        /// <summary>Square root of the sum of the squares, √(Σ ri²): CQC with every ρij off the diagonal at zero.</summary>
        SRSS,
    }

    /// <summary>How the responses to the directions of shaking are put together (EN 1998-1 4.3.3.5, NTC 2018 7.3.5).</summary>
    public enum DirectionalCombination
    {
        /// <summary>Each direction in full with 30% of the others, taking whichever direction in full gives most.</summary>
        Percent30,

        /// <summary>√(Ex² + Ey² + Ez²).</summary>
        SRSS,
    }

    /// <summary>One direction the ground shakes in: its spectrum, and where its modes are in the recorder file.</summary>
    public class SpectrumDirection
    {
        /// <summary>0, 1 or 2: X, Y or Z.</summary>
        public int Axis { get; }

        public string Name => "XYZ"[Axis].ToString();

        public ResponseSpectrum Spectrum { get; }

        /// <summary>The recorder step of this direction's first mode; mode n is FirstStep + n.</summary>
        public int FirstStep { get; }

        /// <summary>
        /// The spectral acceleration at each mode's period, in the model's acceleration unit. The
        /// analysis runs every mode at an acceleration of one, so a mode's response is its recorded
        /// step times this.
        /// </summary>
        public IReadOnlyList<double> Accelerations { get; }

        public SpectrumDirection(int axis, ResponseSpectrum spectrum, int firstStep, IEnumerable<double> periods)
        {
            if (axis < 0 || axis > 2)
                throw new ArgumentOutOfRangeException(nameof(axis), "0, 1 or 2 for X, Y or Z.");
            Axis = axis;
            Spectrum = spectrum ?? throw new ArgumentNullException(nameof(spectrum));
            FirstStep = firstStep;
            Accelerations = periods.Select(t => spectrum.At(t) * ResponseSpectrum.ModelG).ToList();
        }
    }

    /// <summary>
    /// The results of a response spectrum analysis, as the result components read them.
    ///
    /// OpenSees leaves one step in the recorder file per mode and direction - the model pushed into that
    /// mode's shape as far as the spectrum takes it - and combines nothing: a combination of displacements
    /// cannot give back the forces, so every quantity has to be combined on its own. This does that, on
    /// whatever array a reader hands it, so every result component reads a response spectrum analysis the
    /// way it reads any other.
    ///
    /// Its steps are the result cases: 0 is E, the directions combined, then one per direction, Ex, Ey and
    /// Ez, in the order given. Every value is a peak, positive, without a sign; each one peaks on its own,
    /// so a displacement and a force at the same step need not happen together.
    /// </summary>
    public class SpectrumCases
    {
        /// <summary>The period of each mode, in s, first mode first.</summary>
        public IReadOnlyList<double> Periods { get; }

        public IReadOnlyList<SpectrumDirection> Directions { get; }

        public ModalCombination Modal { get; }

        public DirectionalCombination Directional { get; }

        /// <summary>The correlation of each pair of modes, per direction: each has its own damping.</summary>
        private readonly List<double[,]> _correlations;

        public SpectrumCases(IReadOnlyList<double> periods, IReadOnlyList<SpectrumDirection> directions,
                             ModalCombination modal, DirectionalCombination directional)
        {
            if (periods == null || periods.Count == 0)
                throw new ArgumentException("A response spectrum analysis needs at least one mode.");
            if (directions == null || directions.Count == 0)
                throw new ArgumentException("A response spectrum analysis needs at least one direction.");
            if (directions.Any(direction => direction.Accelerations.Count != periods.Count))
                throw new ArgumentException("Every direction needs an acceleration for each mode.");

            Periods = periods.ToList();
            Directions = directions.ToList();
            Modal = modal;
            Directional = directional;
            _correlations = Directions.Select(direction => Correlations(Periods, direction.Spectrum.Damping, modal)).ToList();
        }

        /// <summary>How many result cases there are: E, then one per direction.</summary>
        public int Count => 1 + Directions.Count;

        /// <summary>What each result case is called, in step order.</summary>
        public IReadOnlyList<string> Names =>
            new[] { "E" }.Concat(Directions.Select(direction => "E" + direction.Name.ToLowerInvariant())).ToList();

        /// <summary>The recorder steps <paramref name="resultCase"/> is combined from.</summary>
        public IEnumerable<int> StepsOf(int resultCase)
        {
            var directions = resultCase == 0 ? Directions : new[] { Directions[resultCase - 1] };
            return directions.SelectMany(direction => Enumerable.Range(direction.FirstStep, Periods.Count));
        }

        /// <summary>
        /// Result case <paramref name="resultCase"/> of one recorded quantity, entry by entry, from its
        /// recorded steps. <paramref name="step"/> reads one step of it; every step has the same shape.
        /// </summary>
        public double[,] Combine(int resultCase, Func<int, double[,]> step)
        {
            if (resultCase < 0 || resultCase >= Count)
                throw new ArgumentOutOfRangeException(nameof(resultCase),
                    $"Step {resultCase} is not a result of this response spectrum analysis. Its steps are " +
                    string.Join(", ", Names.Select((name, i) => $"{i} {name}")) + ".");

            if (resultCase > 0)
                return Direction(resultCase - 1, step);

            var each = Enumerable.Range(0, Directions.Count).Select(d => Direction(d, step)).ToList();
            if (each.Count == 1)
                return each[0];

            int rows = each[0].GetLength(0), cols = each[0].GetLength(1);
            var combined = new double[rows, cols];
            var values = new double[each.Count];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    for (int d = 0; d < each.Count; d++)
                        values[d] = each[d][r, c];
                    combined[r, c] = CombineDirections(values, Directional);
                }
            }
            return combined;
        }

        /// <summary>One direction's modes, combined entry by entry.</summary>
        private double[,] Direction(int index, Func<int, double[,]> step)
        {
            var direction = Directions[index];
            int modes = Periods.Count;

            var responses = new double[modes][,];
            for (int n = 0; n < modes; n++)
                responses[n] = step(direction.FirstStep + n);

            int rows = responses[0].GetLength(0), cols = responses[0].GetLength(1);
            if (responses.Any(response => response.GetLength(0) != rows || response.GetLength(1) != cols))
                throw new InvalidOperationException("The modes of the response spectrum analysis were recorded with different shapes.");

            var rho = _correlations[index];
            var combined = new double[rows, cols];
            var r = new double[modes];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    for (int n = 0; n < modes; n++)
                        r[n] = responses[n][i, j] * direction.Accelerations[n];
                    combined[i, j] = CombineModes(r, rho);
                }
            }
            return combined;
        }

        /// <summary>
        /// A quantity known per mode rather than recorded - a base shear - combined for direction
        /// <paramref name="index"/>. <paramref name="perMode"/> is its value at a spectral acceleration of
        /// one, mode by mode.
        /// </summary>
        public double CombineScalar(int index, IReadOnlyList<double> perMode)
        {
            var direction = Directions[index];
            var r = perMode.Select((value, n) => value * direction.Accelerations[n]).ToArray();
            return CombineModes(r, _correlations[index]);
        }

        /// <summary>√(rᵀ ρ r), or √(Σ r²) with no correlations.</summary>
        public static double CombineModes(IReadOnlyList<double> r, double[,] rho)
        {
            int n = r.Count;
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                if (r[i] == 0.0)
                    continue;

                sum += r[i] * r[i];
                if (rho == null)
                    continue;

                double cross = 0.0;
                for (int j = i + 1; j < n; j++)
                    cross += rho[i, j] * r[j];
                sum += 2.0 * r[i] * cross;
            }

            // A sum of squares weighted by a positive definite matrix is never negative; rounding can
            // still take one that should be zero just below it.
            return Math.Sqrt(Math.Max(0.0, sum));
        }

        /// <summary>The directions' peaks, each zero or more, put together by <paramref name="rule"/>.</summary>
        public static double CombineDirections(IReadOnlyList<double> values, DirectionalCombination rule)
        {
            if (rule == DirectionalCombination.SRSS)
                return Math.Sqrt(values.Sum(value => value * value));

            double total = values.Sum();
            double most = 0.0;
            foreach (double value in values)
                most = Math.Max(most, value + 0.3 * (total - value));
            return most;
        }

        /// <summary>
        /// The correlation of two modes of periods <paramref name="ti"/> and <paramref name="tj"/> with
        /// damping ratio <paramref name="damping"/>, NTC 2018 [7.3.4]:
        /// ρ = 8ξ² β^(3/2) / ((1 + β)((1 - β)² + 4ξ²β)) with β = Tj/Ti - Der Kiureghian's, for equal
        /// damping. One for equal periods, and the same either way round.
        /// </summary>
        public static double Correlation(double ti, double tj, double damping)
        {
            if (!(ti > 0.0) || !(tj > 0.0))
                return ti == tj ? 1.0 : 0.0;

            double beta = tj / ti;
            double xi2 = damping * damping;
            return 8.0 * xi2 * Math.Pow(beta, 1.5) / ((1.0 + beta) * ((1.0 - beta) * (1.0 - beta) + 4.0 * xi2 * beta));
        }

        private static double[,] Correlations(IReadOnlyList<double> periods, double damping, ModalCombination modal)
        {
            if (modal == ModalCombination.SRSS)
                return null;

            int n = periods.Count;
            var rho = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    rho[i, j] = i == j ? 1.0 : Correlation(periods[i], periods[j], damping);
            return rho;
        }
    }
}
