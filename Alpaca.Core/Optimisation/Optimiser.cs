// What NLopt's nlopt_optimize does around an algorithm (src/api/optimize.c and src/api/options.c):
// maximising by minimising the negative, remembering COBYLA's best point, taking out variables
// whose bounds are equal, checking the bounds and picking the initial step.
//
// Copyright (c) 2007-2014 Massachusetts Institute of Technology
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Alpaca4d.Optimisation.NLopt;

namespace Alpaca4d.Optimisation
{
    /// <summary>The value to minimise or maximise, for one value per variable in x.</summary>
    public delegate double ObjectiveFunction(double[] x);

    /// <summary>Writes one value per constraint into result. A constraint is met when its value is &lt;= 0.</summary>
    public delegate void ConstraintFunction(double[] x, double[] result);

    /// <summary>
    /// A nonlinear optimisation, as an nlopt_opt is one: an algorithm, bounds, stopping criteria
    /// and constraints, then <see cref="Optimise"/>.
    ///
    /// The algorithms are NLopt's derivative-free ones, ported to C# so that Alpaca4d needs no
    /// native library: the same .gha runs on Windows and on macOS, on Intel and on Apple silicon.
    /// They are ported line for line, so that given the same settings and seed they evaluate the
    /// same points in the same order as NLopt does, which is how the port is tested.
    ///
    /// <see cref="Optimise"/> runs on the calling thread and calls the objective there; nothing
    /// in it touches Rhino. <see cref="ForceStop"/> may be called from any thread.
    /// </summary>
    public sealed class Optimiser
    {
        private readonly StopFlag _forceStop = new StopFlag();
        private readonly List<NLopt.Constraint> _constraints = new List<NLopt.Constraint>();

        public Optimiser(OptimisationAlgorithm algorithm, int dimension)
        {
            if (dimension < 0) throw new ArgumentOutOfRangeException(nameof(dimension));
            Algorithm = algorithm;
            Dimension = dimension;
            LowerBounds = Enumerable.Repeat(double.NegativeInfinity, dimension).ToArray();
            UpperBounds = Enumerable.Repeat(double.PositiveInfinity, dimension).ToArray();
        }

        public OptimisationAlgorithm Algorithm { get; }

        /// <summary>How many variables.</summary>
        public int Dimension { get; }

        /// <summary>One per variable. The global algorithms need finite bounds.</summary>
        public double[] LowerBounds { get; }

        public double[] UpperBounds { get; }

        public bool Maximise { get; set; }

        /// <summary>Stop as soon as the objective is this good. Null for no stop value.</summary>
        public double? StopValue { get; set; }

        /// <summary>Stop when a step changes the objective by less than this fraction of it. 0 for off.</summary>
        public double RelativeFunctionTolerance { get; set; }

        /// <summary>Stop when a step changes the objective by less than this. 0 for off.</summary>
        public double AbsoluteFunctionTolerance { get; set; }

        /// <summary>Stop when a step changes the variables by less than this fraction of them. 0 for off.</summary>
        public double RelativeVariableTolerance { get; set; }

        /// <summary>Stop when a step changes every variable by less than its value here. Null for off.</summary>
        public double[] AbsoluteVariableTolerance { get; set; }

        /// <summary>The most evaluations of the objective. 0 or less for no limit.</summary>
        public int MaxEvaluations { get; set; }

        /// <summary>The most seconds to run. 0 or less for no limit.</summary>
        public double MaxTime { get; set; }

        /// <summary>The first step of each variable, for the local algorithms. Null for NLopt's default.</summary>
        public double[] InitialStep { get; set; }

        /// <summary>The population of CRS2-LM and ISRES. 0 for NLopt's default.</summary>
        public int Population { get; set; }

        /// <summary>The seed of CRS2-LM and ISRES. Null for one from the clock, as NLopt does by default.</summary>
        public uint? Seed { get; set; }

        /// <summary>How many times the last <see cref="Optimise"/> evaluated the objective.</summary>
        public int Evaluations { get; private set; }

        /// <summary>What the algorithm had to say about why it stopped, when it said anything.</summary>
        public string Message { get; private set; }

        /// <summary>
        /// Adds <paramref name="tolerances"/>.Length constraints, computed together by
        /// <paramref name="constraints"/>, each met when it is &lt;= 0. Only COBYLA and ISRES take them.
        /// </summary>
        public void AddInequalityConstraints(ConstraintFunction constraints, double[] tolerances)
        {
            if (constraints == null) throw new ArgumentNullException(nameof(constraints));
            if (tolerances == null) throw new ArgumentNullException(nameof(tolerances));
            if (tolerances.Length == 0) return;
            _constraints.Add(new NLopt.Constraint { M = tolerances.Length, Mf = constraints, Tol = (double[])tolerances.Clone() });
        }

        /// <summary>Stops the optimisation at the algorithm's next check, keeping the best found so far.</summary>
        public void ForceStop() => _forceStop.Value = 1;

        /// <summary>
        /// Optimises from <paramref name="x"/>, and leaves the best point found in it, with its
        /// objective in <paramref name="best"/>.
        /// </summary>
        public OptimisationResult Optimise(ObjectiveFunction objective, double[] x, out double best)
        {
            best = double.NaN;
            Message = null;
            Evaluations = 0;
            if (objective == null || x == null || x.Length != Dimension)
            {
                Message = "NULL args to nlopt_optimize";
                return OptimisationResult.InvalidArgs;
            }
            if (_constraints.Count > 0 && !AlgorithmInfo.Of(Algorithm).TakesConstraints)
            {
                Message = $"{AlgorithmInfo.Of(Algorithm).Name} does not take constraints";
                return OptimisationResult.InvalidArgs;
            }
            if (InitialStep != null && (InitialStep.Length != Dimension || InitialStep.Any(step => step == 0)))
            {
                Message = "zero step size";
                return OptimisationResult.InvalidArgs;
            }

            // reset stopping flag
            _forceStop.Value = 0;

            int n = Dimension;
            var lb = (double[])LowerBounds.Clone();
            var ub = (double[])UpperBounds.Clone();
            for (int i = 0; i < n; ++i)
                if (lb[i] < ub[i] && Stopping.IsTiny(ub[i] - lb[i]))
                    lb[i] = ub[i];

            // for maximizing, just minimize the negative, flipping the stop value with it
            double stopval = StopValue ?? (Maximise ? double.PositiveInfinity : double.NegativeInfinity);
            ObjectiveFunction f = objective;
            if (Maximise)
            {
                f = v => -objective(v);
                stopval = -stopval;
            }

            // COBYLA does not return its best point but the last, so the best is remembered for it
            double memoMinf = double.MaxValue;
            double[] memoBestX = null;
            if (Algorithm == OptimisationAlgorithm.Cobyla && _constraints.Count == 0)
            {
                var unmemoised = f;
                memoBestX = new double[n];
                f = v =>
                {
                    double val = unmemoised(v);
                    bool feasible = true;
                    for (int i = 0; i < n; ++i)
                        if (v[i] < lb[i] || v[i] > ub[i]) feasible = false;
                    if (feasible && val < memoMinf)
                    {
                        memoMinf = val;
                        Array.Copy(v, memoBestX, n);
                    }
                    return val;
                };
            }

            // eliminate the variables with lb == ub: every algorithm here is one NLopt does this for
            int n0 = 0;
            for (int i = 0; i < n; ++i)
                if (lb[i] != ub[i]) n0++;
            bool elim = n0 < n;

            double[] x0 = x, lb0 = lb, ub0 = ub;
            double[] xtolAbs0 = AbsoluteVariableTolerance, dx0 = InitialStep;
            ObjectiveFunction f0 = f;
            NLopt.Constraint[] fc0 = _constraints.ToArray();
            if (elim)
            {
                x0 = Shrink(x, lb, ub, n0);
                lb0 = Shrink(lb, lb, ub, n0);
                ub0 = Shrink(ub, lb, ub, n0);
                xtolAbs0 = xtolAbs0 == null ? null : Shrink(xtolAbs0, lb, ub, n0);
                dx0 = dx0 == null ? null : Shrink(dx0, lb, ub, n0);
                var full = f;
                f0 = v => full(Expand(v, lb, ub));
                fc0 = _constraints.Select(c =>
                {
                    var mf = c.Mf;
                    return new NLopt.Constraint { M = c.M, Tol = c.Tol, Mf = (v, result) => mf(Expand(v, lb, ub), result) };
                }).ToArray();
            }

            var ret = OptimiseCore(n0, f0, fc0, lb0, ub0, x0, out double minf, stopval, xtolAbs0, dx0);

            if (elim)
            {
                var expanded = Expand(x0, lb, ub);
                Array.Copy(expanded, x, n);
            }

            if (memoBestX != null && memoMinf < double.MaxValue)
            {
                Array.Copy(memoBestX, x, n);
                minf = memoMinf;
            }

            if (Maximise) minf = -minf;
            best = minf;
            return ret;
        }

        /// <summary>nlopt_optimize_ for the algorithms ported, minimising only.</summary>
        private OptimisationResult OptimiseCore(int n, ObjectiveFunction f, NLopt.Constraint[] fc, double[] lb, double[] ub, double[] x,
            out double minf, double stopval, double[] xtolAbs, double[] dx)
        {
            if (n == 0)
            {
                // trivial case: no degrees of freedom
                minf = f(x);
                return OptimisationResult.Success;
            }

            minf = double.PositiveInfinity;

            var rng = new MersenneTwister(Seed ?? Stopping.TimeSeed());

            // check bound constraints
            for (int i = 0; i < n; ++i)
                if (lb[i] > ub[i] || x[i] < lb[i] || x[i] > ub[i])
                {
                    Message = string.Format(CultureInfo.InvariantCulture, "bounds {0} fail {1} <= {2} <= {3}", i, lb[i], x[i], ub[i]);
                    return OptimisationResult.InvalidArgs;
                }

            var stop = new Stopping
            {
                N = n,
                MinfMax = stopval,
                FtolRel = RelativeFunctionTolerance,
                FtolAbs = AbsoluteFunctionTolerance,
                XtolRel = RelativeVariableTolerance,
                XtolAbs = xtolAbs,
                XWeights = null,
                NEvals = 0,
                MaxEval = MaxEvaluations,
                MaxTime = MaxTime,
                Start = Stopping.Seconds(),
                ForceStop = _forceStop,
            };

            OptimisationResult ret;
            try
            {
                switch (Algorithm)
                {
                    case OptimisationAlgorithm.DirectL:
                        if (!FiniteDomain(n, lb, ub)) return Invalid("finite domain required for global algorithm", out minf);
                        // which_alg for GN_DIRECT_L: 1 + 3 * 1 + 9 * 1
                        ret = Cdirect.Minimize(n, f, lb, ub, x, ref minf, stop, 0.0, 13, rng);
                        break;

                    case OptimisationAlgorithm.Crs2Lm:
                        if (!FiniteDomain(n, lb, ub)) return Invalid("finite domain required for global algorithm", out minf);
                        ret = Crs.Minimize(n, f, lb, ub, x, ref minf, stop, Population, false, rng);
                        break;

                    case OptimisationAlgorithm.Isres:
                        if (!FiniteDomain(n, lb, ub)) return Invalid("finite domain required for global algorithm", out minf);
                        ret = Isres.Minimize(n, f, fc.Length, fc, 0, new NLopt.Constraint[0], lb, ub, x, ref minf, stop, Population, rng);
                        break;

                    case OptimisationAlgorithm.Cobyla:
                        ret = Cobyla.Minimize(n, f, fc.Length, fc, 0, new NLopt.Constraint[0], lb, ub, x, ref minf, stop, dx ?? DefaultInitialStep(n, lb, ub, x));
                        break;

                    case OptimisationAlgorithm.Bobyqa:
                        ret = Bobyqa.Minimize(n, 2 * n + 1, x, lb, ub, dx ?? DefaultInitialStep(n, lb, ub, x), stop, ref minf, f);
                        break;

                    case OptimisationAlgorithm.NelderMead:
                        ret = NelderMead.Minimize(n, f, lb, ub, x, ref minf, dx ?? DefaultInitialStep(n, lb, ub, x), stop);
                        break;

                    case OptimisationAlgorithm.Sbplx:
                        ret = Subplex.Minimize(n, f, lb, ub, x, ref minf, dx ?? DefaultInitialStep(n, lb, ub, x), stop);
                        break;

                    default:
                        ret = OptimisationResult.InvalidArgs;
                        break;
                }
            }
            finally
            {
                Evaluations = stop.NEvals;
                Message = Message ?? stop.Message;
            }
            return ret;
        }

        private OptimisationResult Invalid(string message, out double minf)
        {
            Message = message;
            minf = double.PositiveInfinity;
            return OptimisationResult.InvalidArgs;
        }

        private static bool FiniteDomain(int n, double[] lb, double[] ub)
        {
            for (int i = 0; i < n; ++i)
                if (Stopping.IsInf(ub[i] - lb[i]))
                    return false;
            return true;
        }

        /// <summary>
        /// nlopt_set_default_initial_step: NLopt's crude heuristics for the first step of the
        /// derivative-free local algorithms, a quarter of the range, less near a bound.
        /// </summary>
        internal static double[] DefaultInitialStep(int n, double[] lb, double[] ub, double[] x)
        {
            var dx = new double[n];
            for (int i = 0; i < n; ++i)
            {
                double step = double.PositiveInfinity;

                if (!Stopping.IsInf(ub[i]) && !Stopping.IsInf(lb[i]) && (ub[i] - lb[i]) * 0.25 < step && ub[i] > lb[i])
                    step = (ub[i] - lb[i]) * 0.25;
                if (!Stopping.IsInf(ub[i]) && ub[i] - x[i] < step && ub[i] > x[i])
                    step = (ub[i] - x[i]) * 0.75;
                if (!Stopping.IsInf(lb[i]) && x[i] - lb[i] < step && x[i] > lb[i])
                    step = (x[i] - lb[i]) * 0.75;

                if (Stopping.IsInf(step))
                {
                    if (!Stopping.IsInf(ub[i]) && Math.Abs(ub[i] - x[i]) < Math.Abs(step))
                        step = (ub[i] - x[i]) * 1.1;
                    if (!Stopping.IsInf(lb[i]) && Math.Abs(x[i] - lb[i]) < Math.Abs(step))
                        step = (x[i] - lb[i]) * 1.1;
                }
                if (Stopping.IsInf(step) || Stopping.IsTiny(step))
                    step = x[i];
                if (Stopping.IsInf(step) || step == 0.0)
                    step = 1;

                dx[i] = step;
            }
            return dx;
        }

        /// <summary>elimdim_shrink: v without the variables whose bounds are equal.</summary>
        private static double[] Shrink(double[] v, double[] lb, double[] ub, int n0)
        {
            var shrunk = new double[n0];
            for (int i = 0, j = 0; i < v.Length; ++i)
                if (lb[i] != ub[i]) shrunk[j++] = v[i];
            return shrunk;
        }

        /// <summary>elimdim_expand: the full point, the eliminated variables at their bound. A new array each time.</summary>
        private static double[] Expand(double[] v0, double[] lb, double[] ub)
        {
            var x = new double[lb.Length];
            for (int i = 0, j = 0; i < x.Length; ++i)
                x[i] = lb[i] == ub[i] ? lb[i] : v0[j++];
            return x;
        }
    }
}
