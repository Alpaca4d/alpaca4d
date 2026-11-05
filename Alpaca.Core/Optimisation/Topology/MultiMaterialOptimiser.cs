using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// How a multi-material topology optimisation runs. The defaults are those of Zheng, Yi, Peng and Yoon
    /// (2024), but for the filter radius, which they give in elements of a grid of their own.
    /// </summary>
    public sealed class MultiMaterialSettings : ITopologyLimits
    {
        /// <summary>The Young's modulus of each material, in the model's units.</summary>
        public double[] Moduli { get; set; }

        /// <summary>The share of the design elements' volume each material may fill, one per material.</summary>
        public double[] VolumeFractions { get; set; }

        /// <summary>The penalty n on each material's indicator: ψ^n of its stiffness. The paper's 3.</summary>
        public double Penalty { get; set; } = 3.0;

        /// <summary>
        /// The p of the p-norm in the interpolation: the larger, the more an element is pushed to one
        /// material alone. The paper's 6, which it found balances a clear result against a steady run.
        /// </summary>
        public double PNorm { get; set; } = 6.0;

        /// <summary>The radius of the density filter, in model units. Zero or less takes two typical elements.</summary>
        public double FilterRadius { get; set; }

        /// <summary>How many iterations at most. The paper runs 300.</summary>
        public int MaxIterations { get; set; } = 300;

        /// <summary>
        /// Once the projection is as sharp as it gets, the run stops when the compliances of the last five
        /// iterations add up to within this share of the five before - BESO's test (<see cref="BesoOptimiser"/>).
        /// Some density always moves by the full move limit, so a test on the densities would not stop it.
        /// </summary>
        public double Tolerance { get; set; } = 0.001;

        /// <summary>What a void element keeps of the stiffest material's modulus.</summary>
        public double MinStiffness { get; set; } = 1e-9;

        /// <summary>The most a design density can change in one iteration. The paper's 0.1.</summary>
        public double Move { get; set; } = 0.1;

        /// <summary>The lowest a design density goes. The paper's 0.001.</summary>
        public double MinDensity { get; set; } = 1e-3;

        /// <summary>Every material's density in every element at the start. The paper's 0.5.</summary>
        public double StartDensity { get; set; } = 0.5;

        /// <summary>
        /// The projection's sharpness β at the start, doubled every <see cref="SharpenEvery"/> iterations up
        /// to <see cref="MaxSharpness"/>. The paper's 1, every 50, which reaches 32 in its 300 iterations.
        /// </summary>
        public double StartSharpness { get; set; } = 1.0;
        public int SharpenEvery { get; set; } = 50;
        public double MaxSharpness { get; set; } = 32.0;

        /// <summary>
        /// What the compliance is multiplied by for MMA, which works best with an objective of tens to
        /// thousands. Null scales the first compliance to <see cref="MultiMaterialOptimiser.ObjectiveSize"/>,
        /// whatever the model's units.
        /// </summary>
        public double? ObjectiveScale { get; set; }
    }

    /// <summary>
    /// Multi-material topology optimisation for the least compliance, after Zheng, Yi, Peng and Yoon (2024),
    /// "An efficient code for the multi-material topology optimization of 2D/3D continuum structures written
    /// in Matlab", Applied Sciences 14:657, with the mapping-based interpolation of Yi et al.
    ///
    /// Every element has a density x_i of every material i. The densities are filtered (Bruns and Tortorelli
    /// 2001) and projected by a smoothed Heaviside step (Wang, Lazarov and Sigmund 2011) to r_i, and an
    /// element's modulus is
    ///
    ///     E = sum_i ψ_i^n (E_i - Evoid) + Evoid,   ψ_i = r_i ||r||_p / (||r||_1 + δ)
    ///
    /// - ψ_i is r_i when the element has material i alone, and the p-norm over the 1-norm penalises an
    /// element that mixes them. Each material has a volume constraint of its own, sum_e v_e r_ie, so the
    /// update is MMA (<see cref="Mma"/>), as in the paper.
    ///
    /// Generalised as <see cref="TopologyOptimiser"/> is: elements may differ in size, so volumes count by
    /// element, and some may be passive, kept as modelled and out of the design. The paper's code projects the
    /// densities with the old sharpness once after each doubling; here they are projected again with the new.
    /// </summary>
    public static class MultiMaterialOptimiser
    {
        /// <summary>What the first compliance is scaled to for MMA when <see cref="MultiMaterialSettings.ObjectiveScale"/> is null.</summary>
        public const double ObjectiveSize = 1000.0;

        /// <summary>The constraints' scale: a volume fraction off by 1e-4 counts 1. The paper's 3D code's.</summary>
        public const double ConstraintScale = 1e4;

        private const double Delta = 1e-9, Eta = 0.5;

        /// <summary>What a run needs to go on: MMA's history and the objective's scale.</summary>
        private sealed class Memory
        {
            public Mma Mma;
            public double Scale;
        }

        /// <summary>The projection's sharpness for iteration <paramref name="iteration"/>, counted from 1.</summary>
        public static double Sharpness(MultiMaterialSettings settings, int iteration)
        {
            double beta = settings.StartSharpness;
            for (int k = settings.SharpenEvery; k < iteration && beta < settings.MaxSharpness; k += settings.SharpenEvery)
                beta = Math.Min(2.0 * beta, settings.MaxSharpness);
            return beta;
        }

        /// <param name="cells">The solid elements, design and passive.</param>
        /// <param name="analyse">Solves the model with the moduli given and returns each cell's energy.</param>
        /// <param name="progress">
        /// Called after every iteration with each material's ψ, material by material, as
        /// <see cref="TopologyResult.Densities"/> holds them; a passive element's are all 0.
        /// </param>
        /// <param name="previous">A run of the same cells and settings to go on from.</param>
        /// <param name="frames">Where to keep ψ after every iteration, as <see cref="TopologyOptimiser.Run"/> keeps densities.</param>
        public static TopologyResult Run(IReadOnlyList<SolidCell> cells, MultiMaterialSettings settings, CellEnergies analyse,
                                         Func<bool> stop = null, Action<TopologyIteration, double[]> progress = null,
                                         TopologyResult previous = null, DensityHistory frames = null)
        {
            var problem = new Problem(cells, settings);
            int materials = problem.Materials, nd = problem.Nd;
            var result = new TopologyResult { FilterRadius = problem.Filter.Radius, Materials = materials };

            double[] x;
            Memory memory;
            if (previous == null)
            {
                x = Enumerable.Repeat(settings.StartDensity, materials * nd).ToArray();
                memory = new Memory { Mma = Mma.Standard(materials * nd, materials) };
            }
            else
            {
                if (previous.Design == null || previous.Design.Length != materials * nd || !(previous.State is Memory kept))
                    throw new ArgumentException("The run to go on from is not one of these elements and materials.");
                x = (double[])previous.Design.Clone();
                memory = new Memory { Mma = kept.Mma.Copy(), Scale = kept.Scale };
                result.History.AddRange(previous.History);
                result.Converged = previous.Converged;
            }

            int start = result.History.Count + 1;
            var state = problem.Evaluate(x, Sharpness(settings, start));
            result.Frames = frames;
            if (frames != null)
            {
                if (frames.Count == 0)
                    frames.Add(problem.Indicators(state));
                else if (frames.Count != result.History.Count + 1)
                    throw new ArgumentException($"{frames.Count} frames for a run of {result.History.Count} iterations; there should be one more.");
            }

            var clock = new Stopwatch();
            for (int iteration = start; iteration <= Math.Max(1, settings.MaxIterations); iteration++)
            {
                result.Converged = false;
                double beta = Sharpness(settings, iteration);
                if (beta != state.Beta)
                    state = problem.Evaluate(x, beta);

                clock.Restart();
                var elementModuli = problem.ElementModuli(state);
                var energies = analyse(elementModuli);
                if (energies == null || energies.Length != cells.Count)
                    throw new InvalidOperationException($"The analysis returned {energies?.Length ?? 0} energies for {cells.Count} elements.");
                double analysisSeconds = clock.Elapsed.TotalSeconds;
                clock.Restart();
                double compliance = energies.Sum();
                if (previous == null && iteration == 1)
                    memory.Scale = settings.ObjectiveScale ?? ObjectiveSize / compliance;

                var (dc, used, dv) = problem.Gradient(state, energies, elementModuli);
                var df0dx = dc.Select(d => memory.Scale * d).ToArray();
                var g = new double[materials];
                var dgdx = new double[materials][];
                for (int k = 0; k < materials; k++)
                {
                    g[k] = ConstraintScale * (used[k] - settings.VolumeFractions[k]);
                    dgdx[k] = dv[k].Select(d => ConstraintScale * d).ToArray();
                }

                var xmin = new double[x.Length];
                var xmax = new double[x.Length];
                for (int j = 0; j < x.Length; j++)
                {
                    xmin[j] = Math.Max(settings.MinDensity, x[j] - settings.Move);
                    xmax[j] = Math.Min(1.0, x[j] + settings.Move);
                }
                var xNew = memory.Mma.Update(x, xmin, xmax, memory.Scale * compliance, df0dx, g, dgdx);
                double change = Settling(result.History, compliance, state.Beta, settings.MaxSharpness);

                var record = new TopologyIteration
                {
                    Iteration = iteration,
                    Compliance = compliance,
                    Volume = used.Sum(),
                    MaterialVolumes = used,
                    Change = change,
                    Sharpness = state.Beta,
                    AnalysisSeconds = analysisSeconds,
                };

                x = xNew;
                state = problem.Evaluate(x, state.Beta);
                var indicators = problem.Indicators(state);
                frames?.Add(indicators);
                record.UpdateSeconds = clock.Elapsed.TotalSeconds;
                result.History.Add(record);
                progress?.Invoke(record, indicators);

                if (change <= settings.Tolerance)
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

            result.Densities = problem.Indicators(state);
            result.Design = x;
            result.State = memory;
            return result;
        }

        /// <summary>
        /// BESO's test of convergence, |sum of the five compliances before the last five - sum of the last five|
        /// over the sum of the last five, the compliance just found the last; NaN until all ten are of the
        /// sharpest projection.
        /// </summary>
        private static double Settling(List<TopologyIteration> history, double latest, double beta, double sharpest)
        {
            if (beta < sharpest || history.Count < 9 || history.Skip(history.Count - 9).Any(it => !(it.Sharpness >= sharpest)))
                return double.NaN;
            var c = history.Skip(history.Count - 9).Select(it => it.Compliance).Concat(new[] { latest }).ToList();
            double before = c.Take(5).Sum(), recent = c.Skip(5).Sum();
            return Math.Abs(before - recent) / recent;
        }

        /// <summary>
        /// The compliance of the design densities <paramref name="x"/> - material by material, as
        /// <see cref="TopologyResult.Design"/> holds them - at sharpness <paramref name="beta"/>, and its
        /// derivatives by them; and each material's share of the design volume with its derivatives. What
        /// each iteration works out, to check against finite differences.
        /// </summary>
        public static (double compliance, double[] dCompliance, double[] volumes, double[][] dVolumes) Derivatives(
            IReadOnlyList<SolidCell> cells, MultiMaterialSettings settings, CellEnergies analyse, double[] x, double beta)
        {
            var problem = new Problem(cells, settings);
            var state = problem.Evaluate(x, beta);
            var moduli = problem.ElementModuli(state);
            var energies = analyse(moduli);
            var (dc, used, dv) = problem.Gradient(state, energies, moduli);
            return (energies.Sum(), dc, used, dv);
        }

        /// <summary>One problem's elements, materials and filter, and the steps from design densities to moduli and back.</summary>
        private sealed class Problem
        {
            public readonly IReadOnlyList<SolidCell> Cells;
            public readonly int[] Design;
            public readonly int Materials, Nd;
            public readonly DensityFilter Filter;
            private readonly double[] _moduli, _volume;
            private readonly double _designVolume, _eVoid, _n, _p;

            public Problem(IReadOnlyList<SolidCell> cells, MultiMaterialSettings settings)
            {
                if (cells == null || cells.Count == 0)
                    throw new ArgumentException("There are no solid elements to optimise.");
                var moduli = settings.Moduli;
                var fractions = settings.VolumeFractions;
                if (moduli == null || moduli.Length == 0)
                    throw new ArgumentException("There are no materials to choose from.");
                if (fractions == null || fractions.Length != moduli.Length)
                    throw new ArgumentException($"{fractions?.Length ?? 0} volume fractions for {moduli.Length} materials: give one per material.");
                if (moduli.Any(e => !(e > 0.0)))
                    throw new ArgumentException("Every material needs a positive Young's modulus.");
                if (fractions.Any(f => !(f > 0.0 && f <= 1.0)))
                    throw new ArgumentException("Every volume fraction has to be more than 0 and at most 1.");
                if (!(fractions.Sum() <= 1.0 + 1e-12))
                    throw new ArgumentException($"The volume fractions add up to {fractions.Sum():0.###}: together they can fill the design volume once at most.");
                if (!(settings.Penalty >= 1.0))
                    throw new ArgumentException("The penalty has to be at least 1.");
                if (!(settings.PNorm >= 1.0))
                    throw new ArgumentException("The p of the p-norm has to be at least 1.");
                if (!(settings.MinStiffness > 0.0 && settings.MinStiffness < 1.0))
                    throw new ArgumentException("The minimum stiffness has to be more than 0 and less than 1.");
                if (!(settings.Move > 0.0))
                    throw new ArgumentException("The move limit has to be positive.");

                Cells = cells;
                Materials = moduli.Length;
                Design = Enumerable.Range(0, cells.Count).Where(i => !cells[i].Passive).ToArray();
                Nd = Design.Length;
                if (Nd == 0)
                    throw new ArgumentException("Every element is kept, so there is nothing to optimise.");

                // The filter works over the design elements alone: a kept element is of no material of the list.
                var designCells = Design.Select(i => cells[i]).ToList();
                double radius = settings.FilterRadius > 0.0 ? settings.FilterRadius : DefaultRadius(designCells);
                Filter = new DensityFilter(designCells, radius);
                _moduli = (double[])moduli.Clone();
                _volume = designCells.Select(cell => cell.Volume).ToArray();
                _designVolume = _volume.Sum();
                _eVoid = settings.MinStiffness * moduli.Max();
                _n = settings.Penalty;
                _p = settings.PNorm;
            }

            /// <summary>Every cell's modulus: the interpolation's for a design cell, its own for a passive one.</summary>
            public double[] ElementModuli(Physical state)
            {
                var moduli = Cells.Select(cell => cell.E).ToArray();
                for (int e = 0; e < Nd; e++)
                {
                    double modulus = _eVoid;
                    for (int i = 0; i < Materials; i++)
                        modulus += Math.Pow(state.Psi[i][e], _n) * (_moduli[i] - _eVoid);
                    moduli[Design[e]] = modulus;
                }
                return moduli;
            }

            /// <summary>
            /// The compliance's derivatives by the design densities, and each material's volume share with its
            /// derivatives, from the energies of the design analysed.
            /// </summary>
            public (double[] dc, double[] used, double[][] dv) Gradient(Physical state, double[] energies, double[] elementModuli)
            {
                int materials = Materials, nd = Nd;

                // dc/dr_k = -sum_i n ψ_i^(n-1) (E_i - Evoid) dψ_i/dr_k u.k0.u, with u.k0.u the energy over the modulus.
                var dcdr = new double[materials][];
                for (int k = 0; k < materials; k++)
                    dcdr[k] = new double[nd];
                var r = new double[materials];
                var dPsi = new double[materials];
                for (int e = 0; e < nd; e++)
                {
                    double unit = energies[Design[e]] / elementModuli[Design[e]];
                    for (int i = 0; i < materials; i++)
                    {
                        r[i] = state.R[i][e];
                        dPsi[i] = _n * Math.Pow(state.Psi[i][e], _n - 1.0) * (_moduli[i] - _eVoid);
                    }
                    double sum = state.Sum[e], norm = state.Norm[e];
                    for (int k = 0; k < materials; k++)
                    {
                        // dψ_i/dr_k = [δ_ik ||r||_p + r_i (r_k/||r||_p)^(p-1)] / S - r_i ||r||_p / S^2, S = ||r||_1 + δ.
                        double share = norm > 0.0 ? Math.Pow(r[k] / norm, _p - 1.0) : 0.0;
                        double d = 0.0;
                        for (int i = 0; i < materials; i++)
                            d += dPsi[i] * (((i == k ? norm : 0.0) + r[i] * share) / sum - r[i] * norm / (sum * sum));
                        dcdr[k][e] = -d * unit;
                    }
                }

                // Through the projection and the filter to the design densities; the volumes likewise.
                var dc = new double[materials * nd];
                var used = new double[materials];
                var dv = new double[materials][];
                for (int k = 0; k < materials; k++)
                {
                    var slope = state.Tilde[k].Select(t => ProjectionSlope(t, state.Beta)).ToArray();
                    var byDesign = Filter.Back(Enumerable.Range(0, nd).Select(e => dcdr[k][e] * slope[e]).ToArray());
                    var volumeByDesign = Filter.Back(Enumerable.Range(0, nd).Select(e => _volume[e] / _designVolume * slope[e]).ToArray());
                    used[k] = Enumerable.Range(0, nd).Sum(e => _volume[e] * state.R[k][e]) / _designVolume;
                    dv[k] = new double[materials * nd];
                    Array.Copy(byDesign, 0, dc, k * nd, nd);
                    Array.Copy(volumeByDesign, 0, dv[k], k * nd, nd);
                }
                return (dc, used, dv);
            }

            /// <summary>The filtered, projected and interpolated densities of the design densities x.</summary>
            public Physical Evaluate(double[] x, double beta)
            {
                int materials = Materials, nd = Nd;
                var state = new Physical
                {
                    Beta = beta,
                    Tilde = new double[materials][],
                    R = new double[materials][],
                    Psi = new double[materials][],
                    Sum = new double[nd],
                    Norm = new double[nd],
                };
                for (int i = 0; i < materials; i++)
                {
                    var own = new double[nd];
                    Array.Copy(x, i * nd, own, 0, nd);
                    state.Tilde[i] = Filter.Apply(own);
                    state.R[i] = state.Tilde[i].Select(t => Project(t, beta)).ToArray();
                    state.Psi[i] = new double[nd];
                }
                for (int e = 0; e < nd; e++)
                {
                    double sum = 0.0, largest = 0.0;
                    for (int i = 0; i < materials; i++)
                    {
                        sum += state.R[i][e];
                        largest = Math.Max(largest, state.R[i][e]);
                    }
                    // ||r||_p, worked out over the largest so that tiny densities do not underflow to 0.
                    double norm = 0.0;
                    if (largest > 0.0)
                    {
                        double powers = 0.0;
                        for (int i = 0; i < materials; i++)
                            powers += Math.Pow(state.R[i][e] / largest, _p);
                        norm = largest * Math.Pow(powers, 1.0 / _p);
                    }
                    state.Sum[e] = sum + Delta;
                    state.Norm[e] = norm;
                    for (int i = 0; i < materials; i++)
                        state.Psi[i][e] = state.R[i][e] * norm / state.Sum[e];
                }
                return state;
            }

            /// <summary>Each material's ψ for every cell, material by material, 0 on the passive cells.</summary>
            public double[] Indicators(Physical state)
            {
                var all = new double[Materials * Cells.Count];
                for (int i = 0; i < Materials; i++)
                    for (int e = 0; e < Nd; e++)
                        all[i * Cells.Count + Design[e]] = state.Psi[i][e];
                return all;
            }
        }

        /// <summary>Two typical elements: 2 times the edge of a cube of the elements' mean volume.</summary>
        public static double DefaultRadius(IReadOnlyList<SolidCell> cells) => DensityFilter.DefaultRadius(cells) * 2.0 / 1.5;

        /// <summary>The smoothed Heaviside step of Wang, Lazarov and Sigmund (2011), about η = 0.5.</summary>
        public static double Project(double tilde, double beta) =>
            (Math.Tanh(beta * Eta) + Math.Tanh(beta * (tilde - Eta))) / (Math.Tanh(beta * Eta) + Math.Tanh(beta * (1.0 - Eta)));

        public static double ProjectionSlope(double tilde, double beta)
        {
            double t = Math.Tanh(beta * (tilde - Eta));
            return beta * (1.0 - t * t) / (Math.Tanh(beta * Eta) + Math.Tanh(beta * (1.0 - Eta)));
        }

        /// <summary>The filtered, projected and interpolated densities of design densities, per material.</summary>
        private sealed class Physical
        {
            public double Beta;
            public double[][] Tilde, R, Psi;
            public double[] Sum, Norm;
        }
    }
}
