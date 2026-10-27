using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// For each element, u_e . K_e u_e with every element at the modulus given for it: twice its strain
    /// energy. The sum over the elements is the compliance, F . u.
    /// </summary>
    public delegate double[] CellEnergies(double[] moduli);

    /// <summary>One iteration of a topology optimisation: the design it analysed and how far it moved.</summary>
    public sealed class TopologyIteration
    {
        public int Iteration { get; set; }

        /// <summary>F . u of the design analysed, summed over the solid elements.</summary>
        public double Compliance { get; set; }

        /// <summary>The design analysed's share of the design elements' volume.</summary>
        public double Volume { get; set; }

        /// <summary>The most any design density moved in the update.</summary>
        public double Change { get; set; }

        public double AnalysisSeconds { get; set; }
        public double UpdateSeconds { get; set; }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "It. {0}: compliance {1:G6}, volume {2:0.000}, change {3:0.000}, analysis {4:0.00} s, update {5:0.00} s",
            Iteration, Compliance, Volume, Change, AnalysisSeconds, UpdateSeconds);
    }

    public sealed class TopologyResult
    {
        /// <summary>Each element's physical density in the last design, 0 to 1, in the order of the cells.</summary>
        public double[] Densities { get; set; }

        /// <summary>
        /// Each element's design density after the last update - what the filter makes the physical
        /// densities of, and all the optimisation needs, with <see cref="History"/>, to go on from here.
        /// </summary>
        public double[] Design { get; set; }

        /// <summary>Every iteration from the first, those of the run this one went on from included.</summary>
        public List<TopologyIteration> History { get; } = new List<TopologyIteration>();

        /// <summary>The design after every iteration, frame 0 the one it started from; null when none were kept.</summary>
        public DensityHistory Frames { get; set; }

        /// <summary>True when the last iteration moved no density by more than the tolerance.</summary>
        public bool Converged { get; set; }

        /// <summary>True when the stop callback ended the run early.</summary>
        public bool Stopped { get; set; }

        /// <summary>The filter radius used, in model units.</summary>
        public double FilterRadius { get; set; }
    }

    /// <summary>
    /// Minimum compliance topology optimisation by SIMP: each element has a density x between 0 and 1
    /// and a modulus E = Emin + x^p (E0 - Emin), and the densities are found that make the structure
    /// stiffest for the volume allowed. Each iteration analyses the design, takes the derivative of
    /// the compliance by every density from the elements' strain energies, and moves the densities
    /// by the optimality criteria update.
    ///
    /// It is top3d (Liu and Tovar 2014, "An efficient 3D topology optimization code written in
    /// Matlab", SMO 50:1175) step for step - density filter, move limit, bisection on the Lagrange
    /// multiplier, the stopping test - generalised in two ways: elements may differ in size, so
    /// volumes count by element, and some may be passive, kept solid and out of the design.
    ///
    /// Removed material is never removed: a void element keeps Emin, a billionth of its stiffness by
    /// default, so the model never changes its connectivity and the analysis never meets a loose node.
    /// </summary>
    public static class TopologyOptimiser
    {
        /// <param name="cells">The solid elements, design and passive.</param>
        /// <param name="settings">
        /// How to run. MaxIterations counts from the first iteration, those of <paramref name="previous"/>
        /// included, and it and Tolerance are read again every iteration, so they may be changed while
        /// the run goes on.
        /// </param>
        /// <param name="analyse">Solves the model with the moduli given and returns each cell's energy.</param>
        /// <param name="stop">Asked between iterations; true ends the run with the design reached.</param>
        /// <param name="progress">Called after every iteration, with the physical densities after its update.</param>
        /// <param name="previous">
        /// A run of the same cells and settings to go on from: the next iteration is the one it would have
        /// made next, so going on from a run stopped after 200 iterations gives what one run of all of
        /// them gives.
        /// </param>
        /// <param name="frames">
        /// Where to keep the design after every iteration, to look back at; null keeps none. Empty, it gets
        /// the starting design as frame 0 first. Going on from <paramref name="previous"/>, pass a copy of
        /// its frames, and the new ones go on after them.
        /// </param>
        public static TopologyResult Run(IReadOnlyList<SolidCell> cells, TopologySettings settings, CellEnergies analyse,
                                         Func<bool> stop = null, Action<TopologyIteration, double[]> progress = null,
                                         TopologyResult previous = null, DensityHistory frames = null)
        {
            if (cells == null || cells.Count == 0)
                throw new ArgumentException("There are no solid elements to optimise.");
            if (!(settings.VolumeFraction > 0.0 && settings.VolumeFraction <= 1.0))
                throw new ArgumentException("The volume fraction has to be more than 0 and at most 1.");
            if (!(settings.Penalty >= 1.0))
                throw new ArgumentException("The penalty has to be at least 1.");
            if (!(settings.MinStiffness > 0.0 && settings.MinStiffness < 1.0))
                throw new ArgumentException("The minimum stiffness has to be more than 0 and less than 1.");
            if (!(settings.Move > 0.0))
                throw new ArgumentException("The move limit has to be positive.");

            int n = cells.Count;
            var design = Enumerable.Range(0, n).Where(i => !cells[i].Passive).ToArray();
            if (design.Length == 0)
                throw new ArgumentException("Every element is kept solid, so there is nothing to optimise.");

            double radius = settings.FilterRadius > 0.0 ? settings.FilterRadius : DensityFilter.DefaultRadius(cells);
            var filter = new DensityFilter(cells, radius);
            var result = new TopologyResult { FilterRadius = radius };

            double p = settings.Penalty;
            var e0 = cells.Select(cell => cell.E).ToArray();
            var eMin = e0.Select(e => settings.MinStiffness * e).ToArray();
            var volume = cells.Select(cell => cell.Volume).ToArray();
            double designVolume = design.Sum(i => volume[i]);
            double target = settings.VolumeFraction * designVolume;

            // The derivative of the volume by the physical densities, which never changes.
            var dVolume = new double[n];
            foreach (int i in design)
                dVolume[i] = volume[i];
            var dv = filter.Back(dVolume);

            double[] x;
            if (previous == null)
                x = cells.Select(cell => cell.Passive ? 1.0 : settings.VolumeFraction).ToArray();
            else
            {
                if (previous.Design == null || previous.Design.Length != n)
                    throw new ArgumentException($"The run to go on from has {previous.Design?.Length ?? 0} design densities for {n} elements.");
                x = (double[])previous.Design.Clone();
                result.History.AddRange(previous.History);
                result.Converged = previous.Converged;
            }
            var physical = Physical(filter, x, cells);

            result.Frames = frames;
            if (frames != null)
            {
                if (frames.Count == 0)
                    frames.Add(physical);
                else if (frames.Count != result.History.Count + 1)
                    throw new ArgumentException($"{frames.Count} frames for a run of {result.History.Count} iterations; there should be one more.");
            }

            var clock = new Stopwatch();
            for (int iteration = result.History.Count + 1; iteration <= Math.Max(1, settings.MaxIterations); iteration++)
            {
                result.Converged = false;
                clock.Restart();
                var moduli = new double[n];
                for (int i = 0; i < n; i++)
                    moduli[i] = cells[i].Passive ? e0[i] : eMin[i] + Math.Pow(physical[i], p) * (e0[i] - eMin[i]);

                var energies = analyse(moduli);
                if (energies == null || energies.Length != n)
                    throw new InvalidOperationException($"The analysis returned {energies?.Length ?? 0} energies for {n} elements.");
                double analysisSeconds = clock.Elapsed.TotalSeconds;
                clock.Restart();

                // dc/dx~_e = -p x~^(p-1) (E0 - Emin) u_e.k0_e u_e, and u_e.k0_e u_e is the energy over the modulus.
                var dc = new double[n];
                foreach (int i in design)
                    dc[i] = -p * Math.Pow(physical[i], p - 1.0) * (e0[i] - eMin[i]) * energies[i] / moduli[i];
                dc = filter.Back(dc);

                var update = OptimalityCriteria(filter, cells, design, x, dc, dv, volume, target, settings.Move);
                double change = design.Max(i => Math.Abs(update.x[i] - x[i]));

                var record = new TopologyIteration
                {
                    Iteration = iteration,
                    Compliance = energies.Sum(),
                    Volume = design.Sum(i => volume[i] * physical[i]) / designVolume,
                    Change = change,
                    AnalysisSeconds = analysisSeconds,
                };

                x = update.x;
                physical = update.physical;
                frames?.Add(physical);
                record.UpdateSeconds = clock.Elapsed.TotalSeconds;
                result.History.Add(record);
                progress?.Invoke(record, physical);

                if (change < settings.Tolerance)
                {
                    result.Converged = true;
                    break;
                }

                if (stop != null && stop())
                {
                    result.Stopped = true;
                    break;
                }
            }

            result.Densities = physical;
            result.Design = x;
            return result;
        }

        /// <summary>The physical densities: the filtered design densities, and 1 on the passive elements.</summary>
        private static double[] Physical(DensityFilter filter, double[] x, IReadOnlyList<SolidCell> cells)
        {
            var physical = filter.Apply(x);
            for (int i = 0; i < cells.Count; i++)
                if (cells[i].Passive)
                    physical[i] = 1.0;
            return physical;
        }

        /// <summary>
        /// top3d's update: each design density times the square root of how much compliance its volume
        /// buys, within the move limit and 0..1, with the multiplier found by bisection so the physical
        /// densities fill the volume allowed.
        ///
        /// top3d bisects between 0 and 1e9, which fits its unit modulus and unit loads. A model in
        /// N and mm, or kN and m, has derivatives orders of magnitude away from those, so they are
        /// scaled first to a largest ratio of one. The scaling changes nothing but where the bisection
        /// starts.
        /// </summary>
        private static (double[] x, double[] physical) OptimalityCriteria(DensityFilter filter, IReadOnlyList<SolidCell> cells,
            int[] design, double[] x, double[] dc, double[] dv, double[] volume, double target, double move)
        {
            var ratio = new double[x.Length];
            double largest = 0.0;
            foreach (int i in design)
            {
                ratio[i] = dv[i] > 0.0 ? Math.Max(0.0, -dc[i] / dv[i]) : 0.0;
                largest = Math.Max(largest, ratio[i]);
            }

            if (!(largest > 0.0) || double.IsInfinity(largest))
                throw new InvalidOperationException(
                    "The design elements carry no load, so nothing tells the optimisation where material is needed. " +
                    "Check that the loads reach the design domain and that the supports hold it.");

            foreach (int i in design)
                ratio[i] /= largest;

            double l1 = 0.0, l2 = 1e9;
            var xNew = (double[])x.Clone();
            double[] physical = null;
            while ((l2 - l1) / (l1 + l2) > 1e-3)
            {
                double mid = 0.5 * (l1 + l2);
                foreach (int i in design)
                {
                    double candidate = x[i] * Math.Sqrt(ratio[i] / mid);
                    xNew[i] = Math.Max(0.0, Math.Max(x[i] - move, Math.Min(1.0, Math.Min(x[i] + move, candidate))));
                }

                physical = Physical(filter, xNew, cells);
                double filled = design.Sum(i => volume[i] * physical[i]);
                if (filled > target) l1 = mid; else l2 = mid;
            }

            return (xNew, physical);
        }
    }
}
