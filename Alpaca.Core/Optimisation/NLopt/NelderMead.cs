// Ported from NLopt, src/algs/neldermead/nldrmd.c.
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
    /// Nelder-Mead simplex algorithm, used as a subroutine for the Rowan's subplex algorithm.
    /// Modified to handle bound constraints ala Richardson and Kuester (1973), as mentioned below.
    ///
    /// The C keeps everything in one scratch array, the n+1 vertices as rows [f(x), x...] followed
    /// by the centroid c and the current point xcur; so does this port, with offsets for pointers.
    /// </summary>
    internal static class NelderMead
    {
        // heuristic "strategy" constants:
        private const double ALPHA = 1, BETA = 0.5, GAMMA = 2, DELTA = 0.5;

        /// <summary>
        /// A key of the red-black tree. In the C a key is a pointer to a vertex's row [f(x), x] in
        /// the scratch array; here it is the offset of that row, so that comparing offsets is
        /// comparing the pointers, as the tie-breaker does.
        /// </summary>
        private sealed class Row
        {
            public readonly int Off;

            public Row(int off)
            {
                Off = off;
            }
        }

        /// <summary>sort order in red-black tree: keys [f(x), x] are sorted by f(x)</summary>
        private static int SimplexCompare(double[] pts, Row k1, Row k2)
        {
            if (pts[k1.Off] < pts[k2.Off]) return -1;
            if (pts[k1.Off] > pts[k2.Off]) return +1;
            return k1.Off - k2.Off; // tie-breaker
        }

        /// <summary>
        /// return true if a and b are approximately equal relative to floating-point precision,
        /// false otherwise
        /// </summary>
        private static bool Close(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-13 * (Math.Abs(a) + Math.Abs(b));
        }

        /// <summary>
        /// Perform the reflection xnew = c + scale * (c - xold), returning false if xnew == c or
        /// xnew == xold (coincident points), true otherwise.
        ///
        /// The reflected point xnew is "pinned" to the lower and upper bounds (lb and ub), as
        /// suggested by J. A. Richardson and J. L. Kuester, "The complex method for constrained
        /// optimization," Commun. ACM 16(8), 487-489 (1973). This is probably a suboptimal way to
        /// handle bound constraints, but I don't know a better way. The main danger with this is
        /// that the simplex might collapse into a lower-dimensional hyperplane; this danger can be
        /// ameliorated by restarting (as in subplex), however.
        /// </summary>
        private static bool ReflectPt(int n, double[] xnew, int xnewOff,
                                      double[] c, int cOff, double scale, double[] xold, int xoldOff,
                                      double[] lb, double[] ub)
        {
            bool equalc = true, equalold = true;
            int i;
            for (i = 0; i < n; ++i)
            {
                double newx = c[cOff + i] + scale * (c[cOff + i] - xold[xoldOff + i]);
                if (newx < lb[i]) newx = lb[i];
                if (newx > ub[i]) newx = ub[i];
                equalc = equalc && Close(newx, c[cOff + i]);
                equalold = equalold && Close(newx, xold[xoldOff + i]);
                xnew[xnewOff + i] = newx;
            }
            return !(equalc || equalold);
        }

        /// <summary>f at the n values from a[off], copied out so that f gets an array of exactly n.</summary>
        private static double Eval(ObjectiveFunction f, int n, double[] a, int off)
        {
            var xv = new double[n];
            Array.Copy(a, off, xv, 0, n);
            return f(xv);
        }

        /// <summary>The C's CHECK_EVAL macro: true when the run must end, with ret set.</summary>
        private static bool CheckEval(int n, double[] xc, int xcOff, double fc, double[] x, ref double minf,
                                      Stopping stop, ref OptimisationResult ret)
        {
            stop.NEvals++;
            if (stop.StopForced()) { ret = OptimisationResult.ForcedStop; return true; }
            if (fc <= minf)
            {
                minf = fc; Array.Copy(xc, xcOff, x, 0, n);
                if (minf < stop.MinfMax) { ret = OptimisationResult.StopValueReached; return true; }
            }
            if (stop.StopEvals()) { ret = OptimisationResult.MaxEvalReached; return true; }
            if (stop.StopTime()) { ret = OptimisationResult.MaxTimeReached; return true; }
            return false;
        }

        /// <summary>
        /// nldrmd_minimize_: internal version of <see cref="Minimize"/>, intended to be used as a
        /// subroutine for the subplex method. Three differences compared to Minimize:
        ///
        /// minf should contain the value of f(x) (so that we don't have to re-evaluate f at the
        /// starting x).
        ///
        /// if psi > 0, then it *replaces* xtol and ftol in stop with the condition that the simplex
        /// diameter |xl - xh| must be reduced by a factor of psi ... this is for when nldrmd is used
        /// within the subplex method; for ordinary termination tests, set psi = 0.
        ///
        /// scratch should contain an array of length >= (n+1)*(n+1) + 2*n, used as scratch
        /// workspace.
        ///
        /// On output, fdiff will contain the difference between the high and low function values
        /// of the last simplex.
        /// </summary>
        internal static OptimisationResult Minimize_(int n, ObjectiveFunction f,
                                                     double[] lb, double[] ub, // bounds
                                                     double[] x, // in: initial guess, out: minimizer
                                                     ref double minf,
                                                     double[] xstep, // initial step sizes
                                                     Stopping stop,
                                                     double psi, double[] scratch,
                                                     out double fdiff)
        {
            double[] s = scratch;
            int pts; // (n+1) x (n+1) array of n+1 points plus function val [0]
            int c; // centroid * n
            int xcur; // current point
            RedBlackTree<Row> t; // red-black tree of simplex, sorted by f(x)
            int i, j;
            double ninv = 1.0 / n;
            OptimisationResult ret = OptimisationResult.Success;
            double initDiam = 0;

            pts = 0;
            c = (n + 1) * (n + 1);
            xcur = c + n;

            var rows = new Row[n + 1];
            for (i = 0; i < n + 1; ++i) rows[i] = new Row(pts + i * (n + 1));
            Comparison<Row> simplexCompare = (k1, k2) => SimplexCompare(s, k1, k2);

            t = new RedBlackTree<Row>(simplexCompare);

            fdiff = double.PositiveInfinity;

            // initialize the simplex based on the starting xstep
            Array.Copy(x, 0, s, pts + 1, n);
            s[pts + 0] = minf;
            if (minf < stop.MinfMax) { ret = OptimisationResult.StopValueReached; goto done; }
            for (i = 0; i < n; ++i)
            {
                int pt = pts + (i + 1) * (n + 1);
                Array.Copy(x, 0, s, pt + 1, n);
                s[pt + 1 + i] += xstep[i];
                if (s[pt + 1 + i] > ub[i])
                {
                    if (ub[i] - x[i] > Math.Abs(xstep[i]) * 0.1)
                        s[pt + 1 + i] = ub[i];
                    else // ub is too close to pt, go in other direction
                        s[pt + 1 + i] = x[i] - Math.Abs(xstep[i]);
                }
                if (s[pt + 1 + i] < lb[i])
                {
                    if (x[i] - lb[i] > Math.Abs(xstep[i]) * 0.1)
                        s[pt + 1 + i] = lb[i];
                    else
                    {
                        // lb is too close to pt, go in other direction
                        s[pt + 1 + i] = x[i] + Math.Abs(xstep[i]);
                        if (s[pt + 1 + i] > ub[i]) // go towards further of lb, ub
                            s[pt + 1 + i] = 0.5 * ((ub[i] - x[i] > x[i] - lb[i] ?
                                                    ub[i] : lb[i]) + x[i]);
                    }
                }
                if (Close(s[pt + 1 + i], x[i]))
                {
                    stop.StopMessage(string.Format(CultureInfo.InvariantCulture,
                        "starting step size led to simplex that was too small in dimension {0}: {1} is too close to x[{2}]={3}",
                        i, FormatG(s[pt + 1 + i]), i, FormatG(x[i])));
                    ret = OptimisationResult.Failure;
                    goto done;
                }
                s[pt] = Eval(f, n, s, pt + 1);
                if (CheckEval(n, s, pt + 1, s[pt], x, ref minf, stop, ref ret)) goto done;
            }

        restart:
            for (i = 0; i < n + 1; ++i)
                t.Insert(rows[i]);

            while (true)
            {
                var low = t.Min();
                var high = t.Max();
                double fl = s[low.K.Off]; int xl = low.K.Off + 1;
                double fh = s[high.K.Off]; int xh = high.K.Off + 1;
                double fr;

                fdiff = fh - fl;

                if (initDiam == 0) // initialize diam. for psi convergence test
                    for (i = 0; i < n; ++i) initDiam += Math.Abs(s[xl + i] - s[xh + i]);

                if (psi <= 0 && stop.StopFtol(fl, fh))
                {
                    ret = OptimisationResult.FtolReached;
                    goto done;
                }

                // compute centroid ... if we cared about the performance of this, we could do it
                // iteratively by updating the centroid on each step, but then we would have to be
                // more careful about accumulation of rounding errors... anyway n is unlikely to be
                // very large for Nelder-Mead in practical cases
                for (i = 0; i < n; ++i) s[c + i] = 0;
                for (i = 0; i < n + 1; ++i)
                {
                    int xi = pts + i * (n + 1) + 1;
                    if (xi != xh)
                        for (j = 0; j < n; ++j)
                            s[c + j] += s[xi + j];
                }
                for (i = 0; i < n; ++i) s[c + i] *= ninv;

                // x convergence check: find xcur = max radius from centroid
                if (n > 0)
                {
                    for (i = 0; i < n; ++i) s[xcur + i] = 0;
                }
                for (i = 0; i < n + 1; ++i)
                {
                    int xi = pts + i * (n + 1) + 1;
                    for (j = 0; j < n; ++j)
                    {
                        double dx = Math.Abs(s[xi + j] - s[c + j]);
                        if (dx > s[xcur + j]) s[xcur + j] = dx;
                    }
                }
                for (i = 0; i < n; ++i) s[xcur + i] += s[c + i];
                if (psi > 0)
                {
                    double diam = 0;
                    for (i = 0; i < n; ++i) diam += Math.Abs(s[xl + i] - s[xh + i]);
                    if (diam < psi * initDiam)
                    {
                        ret = OptimisationResult.XtolReached;
                        goto done;
                    }
                }
                else if (stop.StopX(s, c, s, xcur))
                {
                    ret = OptimisationResult.XtolReached;
                    goto done;
                }

                // reflection
                if (!ReflectPt(n, s, xcur, s, c, ALPHA, s, xh, lb, ub))
                {
                    ret = OptimisationResult.XtolReached; goto done;
                }
                fr = Eval(f, n, s, xcur);
                if (CheckEval(n, s, xcur, fr, x, ref minf, stop, ref ret)) goto done;

                if (fr < fl)
                {
                    // new best point, expand simplex
                    if (!ReflectPt(n, s, xh, s, c, GAMMA, s, xh, lb, ub))
                    {
                        ret = OptimisationResult.XtolReached; goto done;
                    }
                    fh = Eval(f, n, s, xh);
                    if (CheckEval(n, s, xh, fh, x, ref minf, stop, ref ret)) goto done;
                    if (fh >= fr)
                    {
                        // expanding didn't improve
                        fh = fr;
                        Array.Copy(s, xcur, s, xh, n);
                    }
                }
                else if (fr < s[t.Pred(high).K.Off])
                {
                    // accept new point
                    Array.Copy(s, xcur, s, xh, n);
                    fh = fr;
                }
                else
                {
                    // new worst point, contract
                    double fc;
                    if (!ReflectPt(n, s, xcur, s, c, fh <= fr ? -BETA : BETA, s, xh, lb, ub))
                    {
                        ret = OptimisationResult.XtolReached; goto done;
                    }
                    fc = Eval(f, n, s, xcur);
                    if (CheckEval(n, s, xcur, fc, x, ref minf, stop, ref ret)) goto done;
                    if (fc < fr && fc < fh)
                    {
                        // successful contraction
                        Array.Copy(s, xcur, s, xh, n);
                        fh = fc;
                    }
                    else
                    {
                        // failed contraction, shrink simplex
                        t = new RedBlackTree<Row>(simplexCompare);
                        for (i = 0; i < n + 1; ++i)
                        {
                            int pt = pts + i * (n + 1);
                            if (pt + 1 != xl)
                            {
                                if (!ReflectPt(n, s, pt + 1, s, xl, -DELTA, s, pt + 1, lb, ub))
                                {
                                    ret = OptimisationResult.XtolReached;
                                    goto done;
                                }
                                s[pt] = Eval(f, n, s, pt + 1);
                                if (CheckEval(n, s, pt + 1, s[pt], x, ref minf, stop, ref ret)) goto done;
                            }
                        }
                        goto restart;
                    }
                }

                s[high.K.Off] = fh;
                t.Resort(high);
            }

        done:
            return ret;
        }

        /// <summary>nldrmd_minimize.</summary>
        public static OptimisationResult Minimize(int n, ObjectiveFunction f, double[] lb, double[] ub, double[] x, ref double minf, double[] xstep, Stopping stop)
        {
            OptimisationResult ret;
            double[] scratch;
            double fdiff;

            minf = f(x);
            stop.NEvals++;
            if (stop.StopForced()) return OptimisationResult.ForcedStop;
            if (minf < stop.MinfMax) return OptimisationResult.StopValueReached;
            if (stop.StopEvals()) return OptimisationResult.MaxEvalReached;
            if (stop.StopTime()) return OptimisationResult.MaxTimeReached;

            scratch = new double[(n + 1) * (n + 1) + 2 * n];

            ret = Minimize_(n, f, lb, ub, x, ref minf, xstep, stop,
                            0.0, scratch, out fdiff);
            return ret;
        }

        /// <summary>printf's %g, for the message.</summary>
        private static string FormatG(double v)
        {
            if (double.IsNaN(v)) return "nan";
            if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
            if (v == 0) return 1 / v < 0 ? "-0" : "0";
            string e = v.ToString("E5", CultureInfo.InvariantCulture); // d.dddddE+ddd
            int at = e.IndexOf('E');
            int exp = int.Parse(e.Substring(at + 1), CultureInfo.InvariantCulture);
            if (exp < -4 || exp >= 6)
            {
                string mantissa = e.Substring(0, at).TrimEnd('0').TrimEnd('.');
                return mantissa + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture);
            }
            string fixedPoint = v.ToString("F" + (5 - exp).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return fixedPoint.IndexOf('.') >= 0 ? fixedPoint.TrimEnd('0').TrimEnd('.') : fixedPoint;
        }
    }
}
