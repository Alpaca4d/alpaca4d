// Ported from NLopt, src/algs/cdirect/cdirect.c.
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
    /// Steven G. Johnson's re-implementation of DIRECT and DIRECT-L, NLopt's cdirect.c:
    ///
    ///   D. R. Jones, C. D. Perttunen, and B. E. Stuckmann, "Lipschitzian optimization without the
    ///   lipschitz constant," J. Optimization Theory and Applications, vol. 79, p. 157 (1993).
    ///
    ///   J. M. Gablonsky and C. T. Kelley, "A locally-biased form of the DIRECT algorithm,"
    ///   J. Global Optimization 21 (1), p. 27-37 (2001).
    ///
    /// <see cref="Minimize"/> is cdirect, which works in the unit hypercube, and
    /// <see cref="MinimizeUnscaled"/> is cdirect_unscaled. which_alg picks the variant as in the C:
    /// which_alg % 3 the diameter measure, (which_alg / 3) % 3 the division, (which_alg / 9) % 3
    /// which rectangles are potentially optimal. GN_DIRECT is 0, GN_DIRECT_L 13, GN_DIRECT_L_RAND 16.
    /// </summary>
    internal static class Cdirect
    {
        /***************************************************************************/
        /* basic data structure:
         *
         * a hyper-rectangle is stored as an array of length L = 2n+3, where [1]
         * is the value (f) of the function at the center, [0] is the "size"
         * measure (d) of the rectangle, [3..n+2] are the coordinates of the
         * center (c), [n+3..2n+2] are the widths of the sides (w), and [2]
         * is an "age" measure for tie-breaking purposes.
         *
         * we store the hyper-rectangles in a red-black tree, sorted by (d,f)
         * in lexographic order, to allow us to perform quick convex-hull
         * calculations (in the future, we might make this data structure
         * more sophisticated based on the dynamic convex-hull literature).
         *
         * n > 0 always, of course.
         */

        /// <summary>
        /// A hyper-rectangle: the C's double[L], plus a number standing in for its address, which
        /// the C's comparison falls back on. It never gets that far: every rectangle has an age of
        /// its own, so two rectangles only compare equal when they are the same one.
        /// </summary>
        private sealed class Rect
        {
            public readonly double[] V;
            public readonly long Address;

            public Rect(double[] v, long address)
            {
                V = v;
                Address = address;
            }
        }

        /// <summary>The objective as cdirect_unscaled sees it: at the n values of x from offset on.</summary>
        private delegate double RectFunction(double[] x, int offset);

        /* parameters of the search algorithm and various information that
           needs to be passed around */
        private sealed class Params
        {
            public int N; /* dimension */
            public int L; /* size of each rectangle (2n+3) */
            public double MagicEps; /* Jones' epsilon parameter (1e-4 is recommended) */
            public int WhichDiam; /* which measure of hyper-rectangle diam to use:
                                     0 = Jones, 1 = Gablonsky */
            public int WhichDiv; /* which way to divide rects:
                                    0: orig. Jones (divide all longest sides)
                                    1: Gablonsky (cubes divide all, rects longest)
                                    2: Jones Encyc. Opt.: pick random longest side */
            public int WhichOpt; /* which rects are considered "potentially optimal"
                                    0: Jones (all pts on cvx hull, even equal pts)
                                    1: Gablonsky DIRECT-L (pick one pt, if equal pts)
                                    2: ~ 1, but pick points randomly if equal pts
                                    ... 2 seems to suck compared to just picking oldest pt */

            public double[] Lb, Ub;
            public Stopping Stop; /* stopping criteria */
            public RectFunction F;
            public double[] Work; /* workspace, of length >= 2*n */
            public int[] IWork; /* workspace, length >= n */
            public double Minf; /* minimum so far */
            public double[] XMin;

            /* red-black tree of hyperrects, sorted by (d,f,age) in
               lexographical order */
            public RedBlackTree<Rect> RTree;
            public int Age; /* age for next new rect */
            public Rect[] Hull; /* array to store convex hull */
            public int HullLen; /* allocated length of hull array */

            public MersenneTwister Rng; /* nlopt_iurand */
            public long NextAddress;
        }

        private static Rect AllocRect(Params p) => new Rect(new double[p.L], p.NextAddress++);

        /***************************************************************************/

        /* Evaluate the "diameter" (d) of a rectangle of widths w[n]

           We round the result to single precision, which should be plenty for
           the use we put the diameter to (rect sorting), to allow our
           performance hack in convex_hull to work (in the Jones and Gablonsky
           DIRECT algorithms, all of the rects fall into a few diameter
           values, and we don't want rounding error to spoil this) */
        private static double RectDiameter(int n, double[] w, int wOff, Params p)
        {
            int i;
            if (p.WhichDiam == 0)
            {
                /* Jones measure */
                double sum = 0;
                for (i = 0; i < n; ++i)
                    sum += w[wOff + i] * w[wOff + i];
                /* distance from center to a vertex */
                return (float)(Math.Sqrt(sum) * 0.5);
            }
            else
            {
                /* Gablonsky measure */
                double maxw = 0;
                for (i = 0; i < n; ++i)
                    if (w[wOff + i] > maxw)
                        maxw = w[wOff + i];
                /* half-width of longest side */
                return (float)(maxw * 0.5);
            }
        }

        private static void SortFv(int n, double[] fv, int[] isort)
        {
            int i;
            for (i = 0; i < n; ++i) isort[i] = i;
            QSort.Sort(isort, n, (a, b) =>
            {
                double fa = fv[2 * a] < fv[2 * a + 1] ? fv[2 * a] : fv[2 * a + 1];
                double fb = fv[2 * b] < fv[2 * b + 1] ? fv[2 * b] : fv[2 * b + 1];
                if (fa < fb)
                    return -1;
                else if (fa > fb)
                    return +1;
                else
                    return 0;
            });
        }

        private static double FunctionEval(double[] x, int xOff, Params p)
        {
            double f = p.F(x, xOff);
            if (f < p.Minf)
            {
                p.Minf = f;
                Array.Copy(x, xOff, p.XMin, 0, p.N);
            }
            p.Stop.NEvals++;
            return f;
        }

        /// <summary>
        /// The tests of the C's FUNCTION_EVAL macro after an evaluation, in its order; Success to
        /// carry on.
        /// </summary>
        private static OptimisationResult StopAfterEval(Params p)
        {
            if (p.Stop.StopForced()) return OptimisationResult.ForcedStop;
            else if (p.Minf < p.Stop.MinfMax) return OptimisationResult.StopValueReached;
            else if (p.Stop.StopEvals()) return OptimisationResult.MaxEvalReached;
            else if (p.Stop.StopTime()) return OptimisationResult.MaxTimeReached;
            return OptimisationResult.Success;
        }

        private const double THIRD = 0.3333333333333333333333;

        private const double EQUAL_SIDE_TOL = 5e-2; /* tolerance to equate side sizes */

        /* divide rectangle idiv in the list p->rects */
        private static OptimisationResult DivideRect(Rect rdivRect, Params p)
        {
            int i;
            int n = p.N;
            int L = p.L;
            double[] rdiv = rdivRect.V;
            const int c = 3; /* center of rect to divide: rdiv[c..c+n) */
            int w = c + n; /* widths of rect to divide: rdiv[w..w+n) */
            double wmax = rdiv[w + 0];
            int imax = 0, nlongest = 0;
            RedBlackTree<Rect>.Node node;
            OptimisationResult ret;

            for (i = 1; i < n; ++i)
                if (rdiv[w + i] > wmax)
                    wmax = rdiv[w + (imax = i)];
            for (i = 0; i < n; ++i)
                if (wmax - rdiv[w + i] <= wmax * EQUAL_SIDE_TOL)
                    ++nlongest;
            if (p.WhichDiv == 1 || (p.WhichDiv == 0 && nlongest == n))
            {
                /* trisect all longest sides, in increasing order of the average
                   function value along that direction */
                double[] fv = p.Work;
                int[] isort = p.IWork;
                for (i = 0; i < n; ++i)
                {
                    if (wmax - rdiv[w + i] <= wmax * EQUAL_SIDE_TOL)
                    {
                        double csave = rdiv[c + i];
                        rdiv[c + i] = csave - rdiv[w + i] * THIRD;
                        fv[2 * i] = FunctionEval(rdiv, c, p);
                        if ((ret = StopAfterEval(p)) != OptimisationResult.Success) return ret;
                        rdiv[c + i] = csave + rdiv[w + i] * THIRD;
                        fv[2 * i + 1] = FunctionEval(rdiv, c, p);
                        if ((ret = StopAfterEval(p)) != OptimisationResult.Success) return ret;
                        rdiv[c + i] = csave;
                    }
                    else
                    {
                        fv[2 * i] = fv[2 * i + 1] = double.PositiveInfinity;
                    }
                }
                SortFv(n, fv, isort);
                if ((node = p.RTree.Find(rdivRect)) == null)
                    return OptimisationResult.Failure;
                for (i = 0; i < nlongest; ++i)
                {
                    int k;
                    rdiv[w + isort[i]] *= THIRD;
                    rdiv[0] = RectDiameter(n, rdiv, w, p);
                    rdiv[2] = p.Age++;
                    node = p.RTree.Resort(node);
                    for (k = 0; k <= 1; ++k)
                    {
                        if (!Stopping.IsFinite(fv[2 * isort[i] + k]))
                            return OptimisationResult.Failure;
                        Rect rnew = AllocRect(p);
                        Array.Copy(rdiv, rnew.V, L);
                        rnew.V[3 + isort[i]] += rdiv[w + isort[i]] * (2 * k - 1);
                        rnew.V[1] = fv[2 * isort[i] + k];
                        rnew.V[2] = p.Age++;
                        p.RTree.Insert(rnew);
                    }
                }
            }
            else
            {
                int k;
                if (nlongest > 1 && p.WhichDiv == 2)
                {
                    /* randomly choose longest side */
                    i = p.Rng.Iurand(nlongest);
                    for (k = 0; k < n; ++k)
                        if (wmax - rdiv[w + k] <= wmax * EQUAL_SIDE_TOL)
                        {
                            if (i == 0) { i = k; break; }
                            --i;
                        }
                }
                else
                    i = imax; /* trisect longest side */
                if ((node = p.RTree.Find(rdivRect)) == null)
                    return OptimisationResult.Failure;
                rdiv[w + i] *= THIRD;
                rdiv[0] = RectDiameter(n, rdiv, w, p);
                rdiv[2] = p.Age++;
                node = p.RTree.Resort(node);
                for (k = 0; k <= 1; ++k)
                {
                    Rect rnew = AllocRect(p);
                    Array.Copy(rdiv, rnew.V, L);
                    rnew.V[3 + i] += rdiv[w + i] * (2 * k - 1);
                    rnew.V[1] = FunctionEval(rnew.V, 3, p);
                    if ((ret = StopAfterEval(p)) != OptimisationResult.Success) return ret;
                    rnew.V[2] = p.Age++;
                    if (!Stopping.IsFinite(rnew.V[1]))
                        return OptimisationResult.Failure;
                    p.RTree.Insert(rnew);
                }
            }
            return OptimisationResult.Success;
        }

        /***************************************************************************/
        /* Convex hull algorithm, used later to find the potentially optimal
           points.  What we really have in DIRECT is a "dynamic convex hull"
           problem, since we are dynamically adding/removing points and
           updating the hull, but I haven't implemented any of the fancy
           algorithms for this problem yet. */

        /// <summary>
        /// The key {x, -HUGE_VAL} the C searches the tree with. The C's has no age: it is never
        /// read, since no diameter is within a factor 1e-13 of another.
        /// </summary>
        private static Rect Shift(double x) => new Rect(new[] { x, double.NegativeInfinity, 0.0 }, -1);

        /* Find the lower convex hull of a set of points (x,y) stored in a rb-tree
           of pointers to {x,y} arrays sorted in lexographic order by (x,y).

           Unlike standard convex hulls, we allow redundant points on the hull,
           and even allow duplicate points if allow_dups is nonzero.

           The return value is the number of points in the hull, with pointers
           stored in hull[i] (should be an array of length >= t->N).
        */
        private static int ConvexHull(RedBlackTree<Rect> t, Rect[] hull, bool allowDups)
        {
            int nhull = 0;
            double minslope;
            double xmin, xmax, yminmin, ymaxmin;
            RedBlackTree<Rect>.Node n, nmax;

            /* Monotone chain algorithm [Andrew, 1979]. */

            n = t.Min();
            if (n == null) return 0;
            nmax = t.Max();

            xmin = n.K.V[0];
            yminmin = n.K.V[1];
            xmax = nmax.K.V[0];

            if (allowDups)
                do
                {
                    /* include any duplicate points at (xmin,yminmin) */
                    hull[nhull++] = n.K;
                    n = t.Succ(n);
                } while (n != null && n.K.V[0] == xmin && n.K.V[1] == yminmin);
            else
                hull[nhull++] = n.K;

            if (xmin == xmax) return nhull;

            /* set nmax = min mode with x == xmax */
            /* performance hack (see also below) */
            nmax = t.FindGt(Shift(xmax * (1 - 1e-13))); /* non-NULL since xmin != xmax */

            ymaxmin = nmax.K.V[1];
            minslope = (ymaxmin - yminmin) / (xmax - xmin);

            /* set n = first node with x != xmin */
            /* performance hack (see also below) */
            n = t.FindGt(Shift(xmin * (1 + 1e-13))); /* non-NULL since xmin != xmax */

            for (; n != nmax; n = t.Succ(n))
            {
                double[] k = n.K.V;
                if (k[1] > yminmin + (k[0] - xmin) * minslope)
                    continue;

                /* performance hack: most of the points in DIRECT lie along
                   vertical lines at a few x values, and we can exploit this */
                if (nhull != 0 && k[0] == hull[nhull - 1].V[0])
                {
                    /* x == previous x */
                    if (k[1] > hull[nhull - 1].V[1])
                    {
                        /* because of the round to float in rect_diameter, above,
                           it shouldn't be possible for two diameters (x values)
                           to have a fractional difference < 1e-13.  Note
                           that k[0] > 0 always in DIRECT */
                        n = t.Pred(t.FindGt(Shift(k[0] * (1 + 1e-13))));
                        continue;
                    }
                    else
                    {
                        /* equal y values, add to hull */
                        if (allowDups)
                            hull[nhull++] = n.K;
                        continue;
                    }
                }

                /* remove points until we are making a "left turn" to k */
                while (nhull > 1)
                {
                    double[] t1 = hull[nhull - 1].V, t2;

                    /* because we allow equal points in our hull, we have
                       to modify the standard convex-hull algorithm slightly:
                       we need to look backwards in the hull list until we
                       find a point t2 != t1 */
                    int it2 = nhull - 2;
                    do
                    {
                        t2 = hull[it2--].V;
                    } while (it2 >= 0 && t2[0] == t1[0] && t2[1] == t1[1]);
                    if (it2 < 0) break;

                    /* cross product (t1-t2) x (k-t2) > 0 for a left turn: */
                    if ((t1[0] - t2[0]) * (k[1] - t2[1])
                        - (t1[1] - t2[1]) * (k[0] - t2[0]) >= 0)
                        break;
                    --nhull;
                }
                hull[nhull++] = n.K;
            }

            if (allowDups)
                do
                {
                    /* include any duplicate points at (xmax,ymaxmin) */
                    hull[nhull++] = nmax.K;
                    nmax = t.Succ(nmax);
                } while (nmax != null && nmax.K.V[0] == xmax && nmax.K.V[1] == ymaxmin);
            else
                hull[nhull++] = nmax.K;

            return nhull;
        }

        /***************************************************************************/

        private static bool Small(double[] w, int wOff, Params p)
        {
            int i;
            for (i = 0; i < p.N; ++i)
                if (w[wOff + i] > (p.Stop.XtolAbs != null ? p.Stop.XtolAbs[i] : 0) &&
                    w[wOff + i] > (p.Ub[i] - p.Lb[i]) * p.Stop.XtolRel)
                    return false;
            return true;
        }

        private static OptimisationResult DivideGoodRects(Params p)
        {
            int n = p.N;
            Rect[] hull;
            int nhull, i;
            bool xtolReached = true, dividedSome = false;
            double magicEps = p.MagicEps;

            if (p.HullLen < p.RTree.N)
            {
                p.HullLen += p.RTree.N;
                Array.Resize(ref p.Hull, p.HullLen);
            }
            nhull = ConvexHull(p.RTree, hull = p.Hull, p.WhichOpt != 1);
        divisions:
            for (i = 0; i < nhull; ++i)
            {
                double K1 = double.NegativeInfinity, K2 = double.NegativeInfinity, K;
                int im, ip;

                /* find unequal points before (im) and after (ip) to get slope */
                for (im = i - 1; im >= 0 && hull[im].V[0] == hull[i].V[0]; --im) { }
                for (ip = i + 1; ip < nhull && hull[ip].V[0] == hull[i].V[0]; ++ip) { }

                if (im >= 0)
                    K1 = (hull[i].V[1] - hull[im].V[1]) / (hull[i].V[0] - hull[im].V[0]);
                if (ip < nhull)
                    K2 = (hull[i].V[1] - hull[ip].V[1]) / (hull[i].V[0] - hull[ip].V[0]);
                K = K1 > K2 ? K1 : K2;
                if (hull[i].V[1] - K * hull[i].V[0]
                    <= p.Minf - magicEps * Math.Abs(p.Minf) || ip == nhull)
                {
                    /* "potentially optimal" rectangle, so subdivide */
                    OptimisationResult ret = DivideRect(hull[i], p);
                    dividedSome = true;
                    if (ret != OptimisationResult.Success) return ret;
                    xtolReached = xtolReached && Small(hull[i].V, 3 + n, p);
                }

                /* for the DIRECT-L variant, we only divide one rectangle out
                   of all points with equal diameter and function values
                   ... note that for p->which_opt == 1, i == ip-1 should be a no-op
                       anyway, since we set allow_dups=0 in convex_hull above */
                if (p.WhichOpt == 1)
                    i = ip - 1; /* skip to next unequal point for next iteration */
                else if (p.WhichOpt == 2) /* like DIRECT-L but randomized */
                    i += p.Rng.Iurand(ip - i); /* possibly do another equal pt */
            }
            if (!dividedSome)
            {
                if (magicEps != 0)
                {
                    magicEps = 0;
                    goto divisions; /* try again */
                }
                else
                {
                    /* WTF? divide largest rectangle with smallest f */
                    /* (note that this code actually gets called from time
                       to time, and the heuristic here seems to work well,
                       but I don't recall this situation being discussed in
                       the references?) */
                    RedBlackTree<Rect>.Node max = p.RTree.Max();
                    RedBlackTree<Rect>.Node pred = max;
                    double wmax = max.K.V[0];
                    do
                    {
                        /* note: this loop is O(N) worst-case time */
                        max = pred;
                        pred = p.RTree.Pred(max);
                    } while (pred != null && pred.K.V[0] == wmax);
                    return DivideRect(max.K, p);
                }
            }
            return xtolReached ? OptimisationResult.XtolReached : OptimisationResult.Success;
        }

        /***************************************************************************/

        /* lexographic sort order (d,f,age) of hyper-rects, for red-black tree */
        private static int HyperrectCompare(Rect ra, Rect rb)
        {
            double[] a = ra.V, b = rb.V;
            if (a[0] < b[0]) return -1;
            if (a[0] > b[0]) return +1;
            if (a[1] < b[1]) return -1;
            if (a[1] > b[1]) return +1;
            if (a[2] < b[2]) return -1;
            if (a[2] > b[2]) return +1;
            return ra.Address.CompareTo(rb.Address); /* tie-breaker, shouldn't be needed */
        }

        /***************************************************************************/

        /// <summary>cdirect_unscaled: DIRECT in the bounds as they are.</summary>
        public static OptimisationResult MinimizeUnscaled(int n, ObjectiveFunction f, double[] lb, double[] ub, double[] x, ref double minf,
            Stopping stop, double magicEps, int whichAlg, MersenneTwister rng)
        {
            // f is handed a pointer into a rectangle in the C; here it gets the n values on their own
            var xf = new double[n];
            RectFunction rf = (xr, off) =>
            {
                Array.Copy(xr, off, xf, 0, n);
                return f(xf);
            };
            return Unscaled(n, rf, lb, ub, x, ref minf, stop, magicEps, whichAlg, rng);
        }

        private static OptimisationResult Unscaled(int n, RectFunction f, double[] lb, double[] ub, double[] x, ref double minf,
            Stopping stop, double magicEps, int whichAlg, MersenneTwister rng)
        {
            var p = new Params();
            int i;
            Rect rnew;
            OptimisationResult ret;

            p.MagicEps = magicEps;
            p.WhichDiam = whichAlg % 3;
            p.WhichDiv = (whichAlg / 3) % 3;
            p.WhichOpt = (whichAlg / (3 * 3)) % 3;
            p.Lb = lb; p.Ub = ub;
            p.Stop = stop;
            p.N = n;
            p.L = 2 * n + 3;
            p.F = f;
            p.XMin = x;
            p.Minf = double.PositiveInfinity;
            p.Age = 0;
            p.Rng = rng;

            p.RTree = new RedBlackTree<Rect>(HyperrectCompare);

            p.Work = new double[2 * n];
            p.IWork = new int[n];
            p.HullLen = 128; /* start with a reasonable number */
            p.Hull = new Rect[p.HullLen];

            rnew = AllocRect(p);
            for (i = 0; i < n; ++i)
            {
                rnew.V[3 + i] = 0.5 * (lb[i] + ub[i]);
                rnew.V[3 + n + i] = ub[i] - lb[i];
            }
            rnew.V[0] = RectDiameter(n, rnew.V, 3 + n, p);
            rnew.V[1] = FunctionEval(rnew.V, 3, p);
            rnew.V[2] = p.Age++;
            p.RTree.Insert(rnew);

            ret = DivideRect(rnew, p);
            if (ret != OptimisationResult.Success) goto done;

            while (true)
            {
                double minf0 = p.Minf;
                ret = DivideGoodRects(p);
                if (ret != OptimisationResult.Success) goto done;
                if (p.Minf < minf0 && p.Stop.StopF(p.Minf, minf0))
                {
                    ret = OptimisationResult.FtolReached;
                    goto done;
                }
            }

        done:
            minf = p.Minf;
            return ret;
        }

        /* in the conventional DIRECT-type algorithm, we first rescale our
           coordinates to a unit hypercube ... we do this simply by
           wrapping cdirect() around cdirect_unscaled(). */

        /// <summary>cdirect: DIRECT in the unit hypercube the bounds are scaled to.</summary>
        public static OptimisationResult Minimize(int n, ObjectiveFunction f, double[] lb, double[] ub, double[] x, ref double minf,
            Stopping stop, double magicEps, int whichAlg, MersenneTwister rng)
        {
            // cdirect_uf_data: the C keeps these four in one buffer d.x of 3n or 4n
            var dx = new double[n];   // d.x[0..n): the point handed to f
            var ulb = new double[n];  // d.x[n..2n): the unit cube's lower bounds
            var uub = new double[n];  // d.x[2n..3n): and its upper ones
            double[] xtolAbsSave = null;
            OptimisationResult ret;
            int i;

            // cdirect_uf
            RectFunction uf = (xu, off) =>
            {
                int j;
                for (j = 0; j < n; ++j)
                    dx[j] = lb[j] + xu[off + j] * (ub[j] - lb[j]);
                return f(dx);
            };

            for (i = 0; i < n; ++i)
            {
                x[i] = (x[i] - lb[i]) / (ub[i] - lb[i]);
                ulb[i] = 0;
                uub[i] = 1;
            }
            if (stop.XtolAbs != null)
            {
                var xtolAbs = new double[n]; // d.x[3n..4n)
                for (i = 0; i < n; ++i)
                    xtolAbs[i] = stop.XtolAbs[i] / (ub[i] - lb[i]);
                xtolAbsSave = stop.XtolAbs;
                stop.XtolAbs = xtolAbs;
            }
            try
            {
                ret = Unscaled(n, uf, ulb, uub, x, ref minf, stop, magicEps, whichAlg, rng);
            }
            finally
            {
                // in a finally only so that an objective that throws leaves things as they were
                stop.XtolAbs = xtolAbsSave;
                for (i = 0; i < n; ++i)
                    x[i] = lb[i] + x[i] * (ub[i] - lb[i]);
            }
            return ret;
        }
    }
}
