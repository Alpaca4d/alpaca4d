// Ported from NLopt, src/algs/isres/isres.c.
//
// Copyright (c) 2010 Massachusetts Institute of Technology
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
using System.Globalization;

namespace Alpaca4d.Optimisation.NLopt
{
    /// <summary>
    /// Improved Stochastic Ranking Evolution Strategy (ISRES) algorithm
    /// for nonlinearly-constrained global optimization, based on
    /// the method described in:
    ///
    ///   Thomas Philip Runarsson and Xin Yao, "Search biases in constrained
    ///   evolutionary optimization," IEEE Trans. on Systems, Man, and Cybernetics
    ///   Part C: Applications and Reviews, vol. 35 (no. 2), pp. 233-243 (2005).
    ///
    /// It is a refinement of an earlier method described in:
    ///
    ///   Thomas P. Runarsson and Xin Yao, "Stochastic ranking for constrained
    ///   evolutionary optimization," IEEE Trans. Evolutionary Computation,
    ///   vol. 4 (no. 3), pp. 284-294 (2000).
    ///
    /// This is an independent implementation by S. G. Johnson (2009) based
    /// on the papers above.  Runarsson also has his own Matlab implementation
    /// available from his web page: http://www3.hi.is/~tpr
    /// </summary>
    internal static class Isres
    {
        private static int imax2(int a, int b) { return (a > b ? a : b); }

        public static OptimisationResult Minimize(int n, ObjectiveFunction f, int m, Constraint[] fc, int p, Constraint[] h, double[] lb, double[] ub, double[] x, ref double minf, Stopping stop, int population, MersenneTwister rng)
        {
            const double ALPHA = 0.2; // smoothing factor from paper
            const double GAMMA = 0.85; // step-reduction factor from paper
            const double PHI = 1.0; // expected rate of convergence, from paper
            const double PF = 0.45; // fitness probability, from paper
            const double SURVIVOR = 1.0 / 7.0; // survivor fraction, from paper
            int survivors;
            OptimisationResult ret = OptimisationResult.Success;
            double[] sigmas, xs; // population-by-n arrays (row-major)
            double[] fval; // population array of function vals
            double[] penalty; // population array of penalty vals
            double[] x0;
            int[] irank;
            int k, i, j, c;
            int mp = m + p;
            double minf_penalty = double.PositiveInfinity, minf_gpenalty = double.PositiveInfinity;
            double taup, tau;
            double[] results; // scratch space for mconstraint results
            int ires;
            double[] xk = new double[n]; // the n values of a row of xs, handed to f and the constraints

            minf = double.PositiveInfinity;

            if (population == 0) population = 20 * (n + 1);
            if (population < 1)
            {
                stop.StopMessage(string.Format(CultureInfo.InvariantCulture, "population {0} is too small", population));
                return OptimisationResult.InvalidArgs;
            }
            survivors = (int)Math.Ceiling(population * SURVIVOR);

            taup = PHI / Math.Sqrt(2 * n);
            tau = PHI / Math.Sqrt(2 * Math.Sqrt(n));

            // we don't handle unbounded search regions
            for (j = 0; j < n; ++j)
                if (Stopping.IsInf(lb[j]) || Stopping.IsInf(ub[j]))
                {
                    stop.StopMessage("isres requires a finite search region");
                    return OptimisationResult.InvalidArgs;
                }

            ires = imax2(Constraint.MaxDim(m, fc), Constraint.MaxDim(p, h));
            results = new double[ires];

            sigmas = new double[population * n];
            xs = new double[population * n];
            fval = new double[population];
            penalty = new double[population];
            x0 = new double[n];

            irank = new int[population];

            for (k = 0; k < population; ++k)
            {
                for (j = 0; j < n; ++j)
                {
                    sigmas[k * n + j] = (ub[j] - lb[j]) / Math.Sqrt(n);
                    xs[k * n + j] = rng.Urand(lb[j], ub[j]);
                }
            }
            Array.Copy(x, xs, n); // use input x for xs_0

            while (true)
            { // each loop body = one generation
                bool all_feasible = true;

                // evaluate f and constraint violations for whole population
                for (k = 0; k < population; ++k)
                {
                    bool feasible = true;
                    double gpenalty;
                    ++stop.NEvals;
                    Array.Copy(xs, k * n, xk, 0, n);
                    fval[k] = f(xk);
                    if (stop.StopForced())
                    {
                        ret = OptimisationResult.ForcedStop; goto done;
                    }
                    penalty[k] = 0;
                    for (c = 0; c < m; ++c)
                    { // inequality constraints
                        Array.Copy(xs, k * n, xk, 0, n);
                        Constraint.Eval(results, 0, fc[c], n, xk);
                        if (stop.StopForced())
                        {
                            ret = OptimisationResult.ForcedStop; goto done;
                        }
                        for (ires = 0; ires < fc[c].M; ++ires)
                        {
                            double gval = results[ires];
                            if (gval > fc[c].Tol[ires]) feasible = false;
                            if (gval < 0) gval = 0;
                            penalty[k] += gval * gval;
                        }
                    }
                    gpenalty = penalty[k];
                    for (c = m; c < mp; ++c)
                    { // equality constraints
                        Array.Copy(xs, k * n, xk, 0, n);
                        Constraint.Eval(results, 0, h[c - m], n, xk);
                        if (stop.StopForced())
                        {
                            ret = OptimisationResult.ForcedStop; goto done;
                        }
                        for (ires = 0; ires < h[c - m].M; ++ires)
                        {
                            double hval = results[ires];
                            if (Math.Abs(hval) > h[c - m].Tol[ires]) feasible = false;
                            penalty[k] += hval * hval;
                        }
                    }
                    if (penalty[k] > 0) all_feasible = false;

                    // convergence criteria (FIXME: improve?)

                    // FIXME: with equality constraints, how do
                    // we decide which solution is the "best" so far?
                    // ... need some total order on the solutions?

                    if ((penalty[k] <= minf_penalty || feasible)
                        && (fval[k] <= minf || minf_gpenalty > 0)
                        && ((feasible ? 0 : penalty[k]) != minf_penalty
                            || fval[k] != minf))
                    {
                        if (fval[k] < stop.MinfMax && feasible)
                            ret = OptimisationResult.StopValueReached;
                        else if (!Stopping.IsInf(minf))
                        {
                            if (stop.StopF(fval[k], minf)
                                && stop.StopF(feasible ? 0 : penalty[k],
                                              minf_penalty))
                                ret = OptimisationResult.FtolReached;
                            else if (stop.StopX(xs, k * n, x, 0))
                                ret = OptimisationResult.XtolReached;
                        }
                        Array.Copy(xs, k * n, x, 0, n);
                        minf = fval[k];
                        minf_penalty = feasible ? 0 : penalty[k];
                        minf_gpenalty = feasible ? 0 : gpenalty;
                        if (ret != OptimisationResult.Success) goto done;
                    }

                    if (stop.StopForced()) ret = OptimisationResult.ForcedStop;
                    else if (stop.StopEvals()) ret = OptimisationResult.MaxEvalReached;
                    else if (stop.StopTime()) ret = OptimisationResult.MaxTimeReached;
                    if (ret != OptimisationResult.Success) goto done;
                }

                // "selection" step: rank the population
                for (k = 0; k < population; ++k) irank[k] = k;
                if (all_feasible) // special case: rank by objective function
                    QSort.Sort(irank, population, (a, b) => fval[a] < fval[b] ? -1 : (fval[a] > fval[b] ? +1 : 0));
                else
                {
                    // Runarsson & Yao's stochastic ranking of the population
                    for (i = 0; i < population; ++i)
                    {
                        bool swapped = false;
                        for (j = 0; j < population - 1; ++j)
                        {
                            double u = rng.Urand(0, 1);
                            if (u < PF || (penalty[irank[j]] == 0
                                           && penalty[irank[j + 1]] == 0))
                            {
                                if (fval[irank[j]] > fval[irank[j + 1]])
                                {
                                    int irankj = irank[j];
                                    irank[j] = irank[j + 1];
                                    irank[j + 1] = irankj;
                                    swapped = true;
                                }
                            }
                            else if (penalty[irank[j]] > penalty[irank[j + 1]])
                            {
                                int irankj = irank[j];
                                irank[j] = irank[j + 1];
                                irank[j + 1] = irankj;
                                swapped = true;
                            }
                        }
                        if (!swapped) break;
                    }
                }

                // evolve the population:
                // differential evolution for the best survivors,
                // and standard mutation of the best survivors for the rest:
                for (k = survivors; k < population; ++k)
                { // standard mutation
                    double taup_rand = taup * rng.Nrand(0, 1);
                    int rk = irank[k], ri;
                    i = k % survivors;
                    ri = irank[i];
                    for (j = 0; j < n; ++j)
                    {
                        double sigmamax = (ub[j] - lb[j]) / Math.Sqrt(n);
                        sigmas[rk * n + j] = sigmas[ri * n + j]
                            * Math.Exp(taup_rand + tau * rng.Nrand(0, 1));
                        if (sigmas[rk * n + j] > sigmamax)
                            sigmas[rk * n + j] = sigmamax;
                        do
                        {
                            xs[rk * n + j] = xs[ri * n + j]
                                + sigmas[rk * n + j] * rng.Nrand(0, 1);
                        } while (xs[rk * n + j] < lb[j] || xs[rk * n + j] > ub[j]);
                        sigmas[rk * n + j] = sigmas[ri * n + j] + ALPHA * (sigmas[rk * n + j]
                                                                         - sigmas[ri * n + j]);
                    }
                }
                Array.Copy(xs, x0, n);
                for (k = 0; k < survivors; ++k)
                { // differential variation
                    double taup_rand = taup * rng.Nrand(0, 1);
                    int rk = irank[k];
                    for (j = 0; j < n; ++j)
                    {
                        double xi = xs[rk * n + j];
                        if (k + 1 < survivors)
                            xs[rk * n + j] += GAMMA * (x0[j] - xs[(k + 1) * n + j]);
                        if (k + 1 == survivors
                            || xs[rk * n + j] < lb[j] || xs[rk * n + j] > ub[j])
                        {
                            // standard mutation for last survivor and
                            // for any survivor components that are now
                            // outside the bounds
                            double sigmamax = (ub[j] - lb[j]) / Math.Sqrt(n);
                            double sigi = sigmas[rk * n + j];
                            sigmas[rk * n + j] *= Math.Exp(taup_rand
                                                           + tau * rng.Nrand(0, 1));
                            if (sigmas[rk * n + j] > sigmamax)
                                sigmas[rk * n + j] = sigmamax;
                            do
                            {
                                xs[rk * n + j] = xi
                                    + sigmas[rk * n + j] * rng.Nrand(0, 1);
                            } while (xs[rk * n + j] < lb[j] || xs[rk * n + j] > ub[j]);
                            sigmas[rk * n + j] = sigi
                                + ALPHA * (sigmas[rk * n + j] - sigi);
                        }
                    }
                }
            }

        done:
            return ret;
        }
    }
}
