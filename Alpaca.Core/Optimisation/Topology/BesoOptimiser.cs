using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Alpaca4d.Optimisation
{
    /// <summary>How a BESO run goes. The defaults are those of Huang and Xie's soft-kill BESO, as Xia et al. (2018) give it.</summary>
    public sealed class BesoSettings : ITopologyLimits
    {
        /// <summary>The share of the design elements' volume the result keeps, 0 to 1.</summary>
        public double VolumeFraction { get; set; } = 0.3;

        /// <summary>
        /// The evolutionary ratio: how much of the volume each iteration takes away, as a share of the
        /// volume before it, until the volume fraction is reached. Xia et al.'s 2%.
        /// </summary>
        public double EvolutionRate { get; set; } = 0.02;

        /// <summary>
        /// The penalty p of soft-kill BESO: a void element has x_min^p of its stiffness, and its sensitivity
        /// number is x_min^(p-1) times its strain energy - all but nothing, as in hard-kill BESO.
        /// </summary>
        public double Penalty { get; set; } = 3.0;

        /// <summary>The stiffness a void element keeps, as a fraction of its own: x_min^p, so x_min = this^(1/p).</summary>
        public double MinStiffness { get; set; } = 1e-9;

        /// <summary>
        /// The radius of the sensitivity filter, in model units. Zero or less takes two typical elements: wider, the
        /// energies of a thin member are averaged with the void around it, and a member can be cut through.
        /// </summary>
        public double FilterRadius { get; set; }

        /// <summary>How many iterations at most, counted from the first.</summary>
        public int MaxIterations { get; set; } = 200;

        /// <summary>
        /// Once the volume fraction is reached, the run stops when the compliance of the last five
        /// iterations sums to within this share of the five before them. Xia et al.'s 0.1%.
        /// </summary>
        public double Tolerance { get; set; } = 0.001;
    }

    /// <summary>
    /// Bi-directional evolutionary structural optimisation (BESO): the volume comes down a few percent an
    /// iteration from the whole block, each element solid or void, and each iteration keeps the elements
    /// with the highest sensitivity numbers - their strain energy per volume, filtered over the elements
    /// around and averaged with the iteration before - and lets the rest go, or brings void ones back.
    ///
    /// It is esoL.m of Xia, Xia, Huang and Xie (2018), "Bi-directional evolutionary structural optimization
    /// on advanced structures and materials: a comprehensive review", Arch Computat Methods Eng 25:437,
    /// step for step - the volume schedule, the filter, the averaging with the iteration before, the
    /// threshold found by bisection and the test of convergence - in soft-kill form (Huang and Xie 2009):
    /// a void element keeps x_min^p of its stiffness, so the model never changes. Generalised as
    /// <see cref="TopologyOptimiser"/> is: elements may differ in size, so a sensitivity number is per
    /// volume and volumes count by element, and some may be passive, kept solid.
    ///
    /// Where SIMP gives one design, BESO passes through every volume on the way: the design after each
    /// iteration is a solid-void design of the volume reached.
    /// </summary>
    public static class BesoOptimiser
    {
        /// <summary>
        /// How many times the compliance of the design before it a design's has to be for a member to have been cut
        /// through, leaving all but a mechanism; and how many times, at most, it has to come back to for the run to
        /// have mended it, bringing elements back. A run that would converge on a design it has not mended stops
        /// there, broken, not converged.
        /// </summary>
        public const double Breakdown = 10.0, Mended = 3.0;

        /// <summary>
        /// What a run needs to go on: the sensitivity numbers of its last iteration, after averaging, and a breakdown
        /// not mended yet - the iteration that found it and the compliance before it.
        /// </summary>
        private sealed class Memory
        {
            public double[] Numbers;
            public int BrokenAt;
            public double BeforeBreaking;
        }

        /// <summary>The volume the update of iteration <paramref name="iteration"/>, from 1, aims at: esoL's vol.</summary>
        public static double TargetVolume(BesoSettings settings, int iteration)
        {
            double volume = 1.0;
            for (int k = 1; k <= iteration; k++)
                volume = Math.Max(volume * (1.0 - settings.EvolutionRate), settings.VolumeFraction);
            return volume;
        }

        /// <param name="cells">The solid elements, design and passive.</param>
        /// <param name="analyse">Solves the model with the moduli given and returns each cell's energy.</param>
        /// <param name="progress">Called after every iteration, with each element's density after its update: 1 or x_min.</param>
        /// <param name="previous">A run of the same cells and settings to go on from.</param>
        /// <param name="frames">Where to keep the design after every iteration, as <see cref="TopologyOptimiser.Run"/> does.</param>
        public static TopologyResult Run(IReadOnlyList<SolidCell> cells, BesoSettings settings, CellEnergies analyse,
                                         Func<bool> stop = null, Action<TopologyIteration, double[]> progress = null,
                                         TopologyResult previous = null, DensityHistory frames = null)
        {
            if (cells == null || cells.Count == 0)
                throw new ArgumentException("There are no solid elements to optimise.");
            if (!(settings.VolumeFraction > 0.0 && settings.VolumeFraction <= 1.0))
                throw new ArgumentException("The volume fraction has to be more than 0 and at most 1.");
            if (!(settings.EvolutionRate > 0.0 && settings.EvolutionRate < 1.0))
                throw new ArgumentException("The evolutionary ratio has to be more than 0 and less than 1.");
            if (!(settings.Penalty >= 1.0))
                throw new ArgumentException("The penalty has to be at least 1.");
            if (!(settings.MinStiffness > 0.0 && settings.MinStiffness < 1.0))
                throw new ArgumentException("The minimum stiffness has to be more than 0 and less than 1.");

            int count = cells.Count;
            var design = Enumerable.Range(0, count).Where(i => !cells[i].Passive).ToArray();
            if (design.Length == 0)
                throw new ArgumentException("Every element is kept solid, so there is nothing to optimise.");

            // The filter works over every element, the kept ones too, as esoL's over its whole grid: a kept element
            // lends its strain energy to the elements around it, which holds a pad under a load to the structure.
            // It also draws material towards a kept part that works hard - a deck in bending - and a filter much
            // wider than two elements can then starve a member far from it until it is cut through.
            double radius = settings.FilterRadius > 0.0 ? settings.FilterRadius : DefaultRadius(cells);
            var filter = new DensityFilter(cells, radius);
            var result = new TopologyResult { FilterRadius = radius };

            double p = settings.Penalty;
            double xMin = Math.Pow(settings.MinStiffness, 1.0 / p);
            var e0 = cells.Select(cell => cell.E).ToArray();
            var volume = cells.Select(cell => cell.Volume).ToArray();
            double designVolume = design.Sum(i => volume[i]);

            double[] x;
            double[] numbersBefore = null;
            int brokenAt = 0;
            double beforeBreaking = 0.0;
            if (previous == null)
                x = Enumerable.Repeat(1.0, count).ToArray();
            else
            {
                if (previous.Design == null || previous.Design.Length != count || !(previous.State is Memory kept))
                    throw new ArgumentException($"The run to go on from is not one of these {count} elements.");
                x = (double[])previous.Design.Clone();
                numbersBefore = (double[])kept.Numbers?.Clone();
                brokenAt = kept.BrokenAt;
                beforeBreaking = kept.BeforeBreaking;
                result.History.AddRange(previous.History);
                result.Converged = previous.Converged;
            }

            result.Frames = frames;
            if (frames != null)
            {
                if (frames.Count == 0)
                    frames.Add(x);
                else if (frames.Count != result.History.Count + 1)
                    throw new ArgumentException($"{frames.Count} frames for a run of {result.History.Count} iterations; there should be one more.");
            }

            var clock = new Stopwatch();
            for (int iteration = result.History.Count + 1; iteration <= Math.Max(1, settings.MaxIterations); iteration++)
            {
                result.Converged = false;
                double target = TargetVolume(settings, iteration);
                bool reached = TargetVolume(settings, iteration - 1) <= settings.VolumeFraction;

                clock.Restart();
                var moduli = new double[count];
                for (int i = 0; i < count; i++)
                    moduli[i] = Math.Pow(x[i], p) * e0[i];
                var energies = analyse(moduli);
                if (energies == null || energies.Length != count)
                    throw new InvalidOperationException($"The analysis returned {energies?.Length ?? 0} energies for {count} elements.");
                double analysisSeconds = clock.Elapsed.TotalSeconds;
                clock.Restart();

                // A sensitivity number: x^(p-1) u.K0.u per volume, the energy over x and the volume - for a solid
                // element its strain energy density, for a void one x_min^(p-1) of it.
                var numbers = new double[count];
                for (int i = 0; i < count; i++)
                    numbers[i] = energies[i] / x[i] / volume[i];
                numbers = filter.Apply(numbers);
                if (numbersBefore != null)
                    for (int i = 0; i < count; i++)
                        numbers[i] = 0.5 * (numbers[i] + numbersBefore[i]);
                numbersBefore = numbers;

                double compliance = energies.Sum();

                // A member cut through - Xia et al.'s "breakdown" (section 3.4) - or mended since.
                if (brokenAt == 0 && result.History.Count > 0 && compliance > Breakdown * result.History[result.History.Count - 1].Compliance)
                {
                    brokenAt = iteration;
                    beforeBreaking = result.History[result.History.Count - 1].Compliance;
                }
                else if (brokenAt > 0 && compliance < Mended * beforeBreaking)
                    brokenAt = 0;

                double designed = design.Sum(i => x[i] == 1.0 ? volume[i] : 0.0) / designVolume;
                double change = Change(result.History, compliance);
                var record = new TopologyIteration
                {
                    Iteration = iteration,
                    Compliance = compliance,
                    Volume = designed,
                    Change = reached ? change : double.NaN,
                    AnalysisSeconds = analysisSeconds,
                };

                x = Update(x, numbers, design, volume, designVolume, target, xMin);
                frames?.Add(x);
                record.UpdateSeconds = clock.Elapsed.TotalSeconds;
                result.History.Add(record);
                progress?.Invoke(record, (double[])x.Clone());

                if (reached && change <= settings.Tolerance)
                {
                    // Settled on a design broken apart: Xia et al. stop there, and so does this, saying so.
                    if (brokenAt > 0)
                        result.BrokeAfter = brokenAt - 1;
                    else
                        result.Converged = true;
                    break;
                }

                if (stop != null && stop())
                {
                    result.Stopped = true;
                    break;
                }
            }

            result.Densities = (double[])x.Clone();
            result.Design = x;
            result.State = new Memory { Numbers = numbersBefore, BrokenAt = brokenAt, BeforeBreaking = beforeBreaking };
            return result;
        }

        /// <summary>
        /// esoL's test, |sum of the five compliances before the last five - sum of the last five| over the
        /// sum of the last five, with the compliance just found the last; NaN until the eleventh iteration,
        /// where esoL first works it out.
        /// </summary>
        private static double Change(List<TopologyIteration> history, double latest)
        {
            if (history.Count < 10)
                return double.NaN;
            var c = history.Select(it => it.Compliance).Concat(new[] { latest }).ToList();
            int last = c.Count - 1;
            double recent = 0.0, before = 0.0;
            for (int k = 0; k < 5; k++)
            {
                recent += c[last - k];
                before += c[last - 5 - k];
            }
            return Math.Abs(before - recent) / recent;
        }

        /// <summary>
        /// esoL's update: the threshold on the sensitivity numbers, found by bisection between the smallest and
        /// the largest, above which a design element is solid and below which it is void, so that the solid
        /// ones fill the volume aimed at.
        /// </summary>
        private static double[] Update(double[] x, double[] numbers, int[] design, double[] volume, double designVolume,
                                       double target, double xMin)
        {
            double low = design.Min(i => numbers[i]), high = design.Max(i => numbers[i]);
            var next = (double[])x.Clone();
            if (!(high > 0.0))
                throw new InvalidOperationException(
                    "The design elements carry no load, so nothing tells the optimisation where material is needed. " +
                    "Check that the loads reach the design domain and that the supports hold it.");
            while ((high - low) / (low + high) > 1e-9)
            {
                double threshold = 0.5 * (low + high);
                double filled = 0.0;
                foreach (int i in design)
                {
                    next[i] = numbers[i] > threshold ? 1.0 : xMin;
                    if (next[i] == 1.0)
                        filled += volume[i];
                }
                if (filled / designVolume - target > 0.0) low = threshold; else high = threshold;
            }
            return next;
        }

        /// <summary>Two typical elements: 2 times the edge of a cube of the elements' mean volume.</summary>
        public static double DefaultRadius(IReadOnlyList<SolidCell> cells) => DensityFilter.DefaultRadius(cells) * 2.0 / 1.5;
    }
}
