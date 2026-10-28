// Ported from NLopt, src/algs/neldermead/sbplx.c.
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

namespace Alpaca4d.Optimisation.NLopt
{
    /// <summary>
    /// Sbplx: Rowan's Subplex as re-implemented by Steven G. Johnson. Nelder-Mead
    /// (<see cref="NelderMead.Minimize_"/>) on a sequence of subspaces of x, picked by how much
    /// each variable moved in the last pass.
    ///
    /// The C's sbplx_verbose debugging printout is left out; it is off in NLopt.
    /// </summary>
    internal static class Subplex
    {
        // subplex strategy constants:
        private const double psi = 0.25, omega = 0.1;
        private const int nsmin = 2, nsmax = 5;

        private sealed class SubspaceData
        {
            public int[] P; // subspace index permutation
            public int Is; // starting index for this subspace
            public int N; // dimension of underlying space
            public double[] X; // current x vector
            public ObjectiveFunction F; // the "actual" underlying function

            /// <summary>
            /// subspace_func: wrapper around objective function for subspace optimization. The
            /// Nelder-Mead calls it with exactly ns values, so xs.Length is the C's ns.
            /// </summary>
            public double Func(double[] xs)
            {
                int i, @is = Is;
                int[] p = P;
                double[] x = X;
                int ns = xs.Length;

                for (i = @is; i < @is + ns; ++i) x[p[i]] = xs[i - @is];
                return F(x);
            }
        }

        /// <summary>C's copysign: the magnitude of x with the sign bit of y.</summary>
        private static double CopySign(double x, double y)
        {
            long bits = (BitConverter.DoubleToInt64Bits(x) & long.MaxValue) | (BitConverter.DoubleToInt64Bits(y) & long.MinValue);
            return BitConverter.Int64BitsToDouble(bits);
        }

        /// <summary>sbplx_minimize.</summary>
        public static OptimisationResult Minimize(int n, ObjectiveFunction f, double[] lb, double[] ub, double[] x, ref double minf, double[] xstep0, Stopping stop)
        {
            OptimisationResult ret = OptimisationResult.Success;
            double[] xstep, xprev, dx, xs, lbs, ubs, xsstep, scratch;
            int[] p; // permuted indices of x sorted by decreasing magnitude |dx|
            int i;
            SubspaceData sd;
            ObjectiveFunction subspaceFunc;
            Func<int, int, int> pCompare;

            minf = f(x);
            stop.NEvals++;
            if (stop.StopForced()) return OptimisationResult.ForcedStop;
            if (minf < stop.MinfMax) return OptimisationResult.StopValueReached;
            if (stop.StopEvals()) return OptimisationResult.MaxEvalReached;
            if (stop.StopTime()) return OptimisationResult.MaxTimeReached;

            xstep = new double[n];
            xprev = new double[n];
            dx = new double[n];
            xs = new double[nsmax];
            xsstep = new double[nsmax];
            lbs = new double[nsmax];
            ubs = new double[nsmax];
            scratch = new double[(nsmax + 1) * (nsmax + 1) + 2 * nsmax];
            p = new int[n];

            Array.Copy(xstep0, xstep, n);
            for (i = 0; i < n; ++i) dx[i] = 0;

            sd = new SubspaceData();
            sd.P = p;
            sd.N = n;
            sd.X = x;
            sd.F = f;
            subspaceFunc = sd.Func;

            // qsort_r comparison function for sorting indices into delta-x array
            pCompare = (pi, pj) =>
            {
                double dxi = Math.Abs(dx[pi]), dxj = Math.Abs(dx[pj]);
                return dxi > dxj ? -1 : (dxi < dxj ? +1 : 0);
            };

            while (true)
            {
                double normi = 0;
                double normdx = 0;
                int ns, nsubs = 0;
                double fdiff, fdiffMax = 0;

                Array.Copy(x, xprev, n);

                // sort indices into the progress vector dx by decreasing order of magnitude |dx|
                for (i = 0; i < n; ++i) p[i] = i;
                QSort.Sort(p, n, pCompare);

                // find the subspaces, and perform nelder-mead on each one
                for (i = 0; i < n; ++i) normdx += Math.Abs(dx[i]); // L1 norm
                for (i = 0; i + nsmin < n; i += ns)
                {
                    // find subspace starting at index i
                    int k, nk;
                    double nsGoodness = double.NegativeInfinity, norm = normi;
                    nk = i + nsmax > n ? n : i + nsmax; // max k for this subspace
                    for (k = i; k < i + nsmin - 1; ++k) norm += Math.Abs(dx[p[k]]);
                    ns = nsmin;
                    for (k = i + nsmin - 1; k < nk; ++k)
                    {
                        double goodness;
                        norm += Math.Abs(dx[p[k]]);
                        // remaining subspaces must be big enough to partition
                        if (n - (k + 1) < nsmin) continue;
                        // maximize figure of merit defined by Rowan thesis:
                        // look for sudden drops in average |dx|
                        if (k + 1 < n)
                            goodness = norm / (k + 1) - (normdx - norm) / (n - (k + 1));
                        else
                            goodness = normdx / n;
                        if (goodness > nsGoodness)
                        {
                            nsGoodness = goodness;
                            ns = (k + 1) - i;
                        }
                    }
                    for (k = i; k < i + ns; ++k) normi += Math.Abs(dx[p[k]]);
                    // do nelder-mead on subspace of dimension ns starting w/i
                    sd.Is = i;
                    for (k = i; k < i + ns; ++k)
                    {
                        xs[k - i] = x[p[k]];
                        xsstep[k - i] = xstep[p[k]];
                        lbs[k - i] = lb[p[k]];
                        ubs[k - i] = ub[p[k]];
                    }
                    ++nsubs;
                    ret = NelderMead.Minimize_(ns, subspaceFunc, lbs, ubs, xs, ref minf,
                                               xsstep, stop, psi, scratch, out fdiff);
                    if (fdiff > fdiffMax) fdiffMax = fdiff;
                    for (k = i; k < i + ns; ++k) x[p[k]] = xs[k - i];
                    if (ret == OptimisationResult.Failure) { ret = OptimisationResult.XtolReached; goto done; }
                    if (ret != OptimisationResult.XtolReached) goto done;
                }
                // nelder-mead on last subspace
                ns = n - i;
                sd.Is = i;
                for (; i < n; ++i)
                {
                    xs[i - sd.Is] = x[p[i]];
                    xsstep[i - sd.Is] = xstep[p[i]];
                    lbs[i - sd.Is] = lb[p[i]];
                    ubs[i - sd.Is] = ub[p[i]];
                }
                ++nsubs;
                ret = NelderMead.Minimize_(ns, subspaceFunc, lbs, ubs, xs, ref minf,
                                           xsstep, stop, psi, scratch, out fdiff);
                if (fdiff > fdiffMax) fdiffMax = fdiff;
                for (i = sd.Is; i < n; ++i) x[p[i]] = xs[i - sd.Is];
                if (ret == OptimisationResult.Failure) { ret = OptimisationResult.XtolReached; goto done; }
                if (ret != OptimisationResult.XtolReached) goto done;

                // termination tests:
                if (stop.StopFtol(minf, minf + fdiffMax))
                {
                    ret = OptimisationResult.FtolReached;
                    goto done;
                }
                if (stop.StopX(x, xprev))
                {
                    int j;
                    // as explained in Rowan's thesis, it is important to check |xstep| as well as
                    // |x-xprev|, since if the step size is too large (in early iterations), the
                    // inner Nelder-Mead may not make much progress
                    for (j = 0; j < n; ++j)
                        if (Math.Abs(xstep[j]) * psi > (stop.XtolAbs != null ? stop.XtolAbs[j] : 0)
                            && Math.Abs(xstep[j]) * psi > stop.XtolRel * Math.Abs(x[j]))
                            break;
                    if (j == n)
                    {
                        ret = OptimisationResult.XtolReached;
                        goto done;
                    }
                }

                // compute change in optimal point
                for (i = 0; i < n; ++i) dx[i] = x[i] - xprev[i];

                // setting stepsizes
                {
                    double scale;
                    if (nsubs == 1)
                        scale = psi;
                    else
                    {
                        double stepnorm = 0, dxnorm = 0;
                        for (i = 0; i < n; ++i)
                        {
                            stepnorm += Math.Abs(xstep[i]);
                            dxnorm += Math.Abs(dx[i]);
                        }
                        scale = dxnorm / stepnorm;
                        if (scale < omega) scale = omega;
                        if (scale > 1 / omega) scale = 1 / omega;
                    }
                    for (i = 0; i < n; ++i)
                        xstep[i] = (dx[i] == 0) ? -(xstep[i] * scale)
                            : CopySign(xstep[i] * scale, dx[i]);
                }
            }

        done:
            return ret;
        }
    }
}
