// Ported from NLopt, src/algs/crs/crs.c.
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
using System.Globalization;

namespace Alpaca4d.Optimisation.NLopt
{
    /// <summary>
    /// Controlled Random Search 2 (CRS2) with "local mutation", as defined by:
    ///     P. Kaelo and M. M. Ali, "Some variants of the controlled random
    ///     search algorithm for global optimization," J. Optim. Theory Appl.
    ///     130 (2), 253-264 (2006).
    /// </summary>
    internal static class Crs
    {
        /// <summary>
        /// A key of the red-black tree: the C keys are pointers to the rows [f(x), x] of the
        /// population array, so a key here is the index of its row in <see cref="CrsData.Ps"/>.
        /// Rows sit in memory in index order, so comparing indices compares the pointers.
        /// </summary>
        private sealed class Row
        {
            public readonly int Index;
            public Row(int index) { Index = index; }
        }

        private sealed class CrsData
        {
            public int N1;           // n + 1
            public int n;            // # dimensions
            public double[] Lb, Ub;
            public Stopping Stop;    // stopping criteria
            public ObjectiveFunction F;
            public MersenneTwister Rng;

            public int N;            // # points in population
            public double[] Ps;      // population array (N+1) x (n+1) of tuples [f(x), x]
            public int P;            // offset in Ps of the single point (length n+1), for temp use: row N
            public double[] Xn;      // n values handed to f
            public RedBlackTree<Row> T; // red-black tree of population, sorted by f(x)

            /// <summary>sort order in red-black tree: keys [f(x), x] are sorted by f(x)</summary>
            public int Compare(Row k1, Row k2)
            {
                double f1 = Ps[k1.Index * N1], f2 = Ps[k2.Index * N1];
                if (f1 < f2) return -1;
                if (f1 > f2) return +1;
                return (k1.Index - k2.Index) * N1; // tie-breaker: (int)(k1 - k2)
            }

            /// <summary>f(n, ps + off, ...), with the n values copied out first.</summary>
            public double Eval(int off)
            {
                Array.Copy(Ps, off, Xn, 0, n);
                return F(Xn);
            }
        }

        /// <summary>
        /// set x to a random trial value, as defined by CRS:
        ///   x = 2G - x_n,
        /// where x_0 ... x_n are distinct points in the population
        /// with x_0 the current best point and the other points are random,
        /// and G is the centroid of x_0...x_{n-1}
        ///
        /// x is d.Ps[x..x+n).
        /// </summary>
        private static void RandomTrial(CrsData d, int x, RedBlackTree<Row>.Node best)
        {
            int n = d.n, n1 = n + 1, i, k, i0, jn;
            double[] ps = d.Ps;
            int xi;

            // initialize x to x_0 = best point
            Array.Copy(ps, best.K.Index * n1 + 1, ps, x, n);
            i0 = best.K.Index;

            jn = d.Rng.Iurand(n); // which of remaining n points is "x_n",
                                  // i.e. which to reflect through ...
                                  // this is necessary since we generate
                                  // the remaining points in order, so
                                  // just picking the last point would not
                                  // be very random

            // use "method A" from
            //
            //     Jeffrey Scott Vitter, "An efficient algorithm for
            //     sequential random sampling," ACM Trans. Math. Soft. 13 (1),
            //     58--67 (1987).
            //
            // to randomly pick n distinct points out of the remaining N-1 (not
            // including i0!).  (The same as "method S" in Knuth vol. 2.)
            // This method requires O(N) time, which is fine in our case
            // (there are better methods if n << N).
            {
                int Nleft = d.N - 1, nleft = n;
                int Nfree = Nleft - nleft;
                i = 0; i += i == i0 ? 1 : 0;
                while (nleft > 1)
                {
                    double q = ((double)Nfree) / Nleft;
                    double v = d.Rng.Urand(0.0, 1.0);
                    while (q > v)
                    {
                        ++i; i += i == i0 ? 1 : 0;
                        --Nfree; --Nleft;
                        q = (q * Nfree) / Nleft;
                    }
                    xi = n1 * i + 1;
                    if (jn-- == 0) // point to reflect through
                        for (k = 0; k < n; ++k) ps[x + k] -= ps[xi + k] * (0.5 * n);
                    else // point to include in centroid
                        for (k = 0; k < n; ++k) ps[x + k] += ps[xi + k];
                    ++i; i += i == i0 ? 1 : 0;
                    --Nleft; --nleft;
                }
                i += d.Rng.Iurand(Nleft); i += i == i0 ? 1 : 0;
                xi = n1 * i + 1;
                if (jn-- == 0) // point to reflect through
                    for (k = 0; k < n; ++k) ps[x + k] -= ps[xi + k] * (0.5 * n);
                else // point to include in centroid
                    for (k = 0; k < n; ++k) ps[x + k] += ps[xi + k];
            }
            for (k = 0; k < n; ++k)
            {
                ps[x + k] *= 2.0 / n; // renormalize
                if (ps[x + k] > d.Ub[k]) ps[x + k] = d.Ub[k];
                else if (ps[x + k] < d.Lb[k]) ps[x + k] = d.Lb[k];
            }
        }

        private const int NUM_MUTATION = 1; // # "local mutation" steps to try if trial fails

        private static OptimisationResult CrsTrial(CrsData d)
        {
            RedBlackTree<Row>.Node best = d.T.Min();
            RedBlackTree<Row>.Node worst = d.T.Max();
            int mutation = NUM_MUTATION;
            int i, n = d.n, n1 = n + 1;
            double[] ps = d.Ps;
            int p = d.P;
            int bestK = best.K.Index * n1, worstK = worst.K.Index * n1;
            RandomTrial(d, p + 1, best);
            do
            {
                ps[p] = d.Eval(p + 1);
                d.Stop.NEvals++;
                if (d.Stop.StopForced()) return OptimisationResult.ForcedStop;
                if (ps[p] < ps[worstK]) break;
                if (d.Stop.StopEvals()) return OptimisationResult.MaxEvalReached;
                if (d.Stop.StopTime()) return OptimisationResult.MaxTimeReached;
                if (mutation != 0)
                {
                    for (i = 0; i < n; ++i)
                    {
                        double w = d.Rng.Urand(0.0, 1.0);
                        ps[p + 1 + i] = ps[bestK + 1 + i] * (1 + w) - w * ps[p + 1 + i];
                        if (ps[p + 1 + i] > d.Ub[i]) ps[p + 1 + i] = d.Ub[i];
                        else if (ps[p + 1 + i] < d.Lb[i]) ps[p + 1 + i] = d.Lb[i];
                    }
                    mutation--;
                }
                else
                {
                    RandomTrial(d, p + 1, best);
                    mutation = NUM_MUTATION;
                }
            } while (true);
            Array.Copy(ps, p, ps, worstK, n + 1);
            d.T.Resort(worst);
            return OptimisationResult.Success;
        }

        private static OptimisationResult CrsInit(CrsData d, int n, double[] x, double[] lb, double[] ub,
            Stopping stop, ObjectiveFunction f, int population, MersenneTwister rng)
        {
            int i;

            if (population == 0)
            {
                // TODO: how should we set the default population size?
                // the Kaelo and Ali paper suggests 10*(n+1), but should
                // we add more random points if maxeval is large, or... ?
                d.N = 10 * (n + 1); // heuristic initial population size
            }
            else
                d.N = population;
            if (d.N < n + 1)
            { // population must be big enough for a simplex
                stop.StopMessage(string.Format(CultureInfo.InvariantCulture,
                    "population {0} should be >= dimension + 1 = {1}", d.N, n + 1));
                return OptimisationResult.InvalidArgs;
            }

            d.n = n;
            d.N1 = n + 1;
            d.Stop = stop;
            d.F = f;
            d.Rng = rng;
            d.Ub = ub; d.Lb = lb;
            d.Ps = new double[(n + 1) * (d.N + 1)];
            d.P = d.N * (n + 1);
            d.Xn = new double[n];
            d.T = new RedBlackTree<Row>(d.Compare);

            // we can either use pseudorandom points, as in the original CRS
            // algorithm, or use a low-discrepancy Sobol' sequence ... I tried
            // the latter, however, and it doesn't seem to help, probably
            // because we are only generating a small number of random points
            // to start with
            // (only the pseudorandom points are ported)

            // generate initial points randomly, plus starting guess x
            Array.Copy(x, 0, d.Ps, 1, n);
            d.Ps[0] = f(x);
            stop.NEvals++;
            d.T.Insert(new Row(0));
            if (d.Ps[0] < stop.MinfMax) return OptimisationResult.StopValueReached;
            if (stop.StopEvals()) return OptimisationResult.MaxEvalReached;
            if (stop.StopTime()) return OptimisationResult.MaxTimeReached;
            for (i = 1; i < d.N; ++i)
            {
                int k = i * (n + 1);
                int j;
                for (j = 0; j < n; ++j)
                    d.Ps[k + 1 + j] = rng.Urand(lb[j], ub[j]);
                d.Ps[k] = d.Eval(k + 1);
                stop.NEvals++;
                d.T.Insert(new Row(i));
                if (d.Ps[k] < stop.MinfMax) return OptimisationResult.StopValueReached;
                if (stop.StopEvals()) return OptimisationResult.MaxEvalReached;
                if (stop.StopTime()) return OptimisationResult.MaxTimeReached;
            }

            return OptimisationResult.Success;
        }

        public static OptimisationResult Minimize(int n, ObjectiveFunction f, double[] lb, double[] ub, double[] x, ref double minf, Stopping stop, int population, bool lds, MersenneTwister rng)
        {
            if (lds) throw new NotSupportedException("CRS with a low-discrepancy sequence is not ported.");

            OptimisationResult ret;
            var d = new CrsData();
            RedBlackTree<Row>.Node best;
            int n1 = n + 1;

            ret = CrsInit(d, n, x, lb, ub, stop, f, population, rng);
            if (ret < 0) return ret;

            best = d.T.Min();
            minf = d.Ps[best.K.Index * n1];
            Array.Copy(d.Ps, best.K.Index * n1 + 1, x, 0, n);

            while (ret == OptimisationResult.Success)
            {
                if (OptimisationResult.Success == (ret = CrsTrial(d)))
                {
                    best = d.T.Min();
                    int bestK = best.K.Index * n1;
                    if (d.Ps[bestK] < minf)
                    {
                        if (d.Ps[bestK] < stop.MinfMax)
                            ret = OptimisationResult.StopValueReached;
                        else if (stop.StopF(d.Ps[bestK], minf))
                            ret = OptimisationResult.FtolReached;
                        else if (stop.StopX(d.Ps, bestK + 1, x, 0))
                            ret = OptimisationResult.XtolReached;
                        minf = d.Ps[bestK];
                        Array.Copy(d.Ps, bestK + 1, x, 0, n);
                    }
                    if (ret != OptimisationResult.Success)
                    {
                        if (stop.StopEvals())
                            ret = OptimisationResult.MaxEvalReached;
                        else if (stop.StopTime())
                            ret = OptimisationResult.MaxTimeReached;
                    }
                }
            }
            return ret;
        }
    }
}
