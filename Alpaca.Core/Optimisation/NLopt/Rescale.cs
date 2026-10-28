// Ported from NLopt, src/util/rescale.c.
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

namespace Alpaca4d.Optimisation.NLopt
{
    /// <summary>NLopt's rescaling of variables to equal initial steps, for COBYLA and BOBYQA.</summary>
    internal static class Rescale
    {
        /// <summary>
        /// nlopt_compute_rescaling: a factor per dimension, from the nonzero initial steps dx. No
        /// rescaling when every step is the same; otherwise each is made equal to dx[0].
        /// </summary>
        public static double[] ComputeRescaling(int n, double[] dx)
        {
            var s = new double[n];
            int i;
            for (i = 0; i < n; ++i) s[i] = 1.0; // default: no rescaling
            if (n == 1) return s;

            for (i = 1; i < n && dx[i] == dx[i - 1]; ++i) { }
            if (i < n)
            {
                // unequal initial steps, rescale to make equal to dx[0]
                for (i = 1; i < n; ++i)
                    s[i] = dx[i] / dx[0];
            }
            return s;
        }

        /// <summary>nlopt_rescale: xs = x / s.</summary>
        public static void Scale(int n, double[] s, double[] x, double[] xs)
        {
            if (s == null)
                for (int i = 0; i < n; ++i) xs[i] = x[i];
            else
                for (int i = 0; i < n; ++i) xs[i] = x[i] / s[i];
        }

        /// <summary>nlopt_unscale: xs = x * s.</summary>
        public static void Unscale(int n, double[] s, double[] x, double[] xs)
        {
            if (s == null)
                for (int i = 0; i < n; ++i) xs[i] = x[i];
            else
                for (int i = 0; i < n; ++i) xs[i] = x[i] * s[i];
        }

        /// <summary>nlopt_new_rescaled: a new array of x / s.</summary>
        public static double[] NewRescaled(int n, double[] s, double[] x)
        {
            var xs = new double[n];
            Scale(n, s, x, xs);
            return xs;
        }

        /// <summary>
        /// nlopt_reorder_bounds: rescaling can flip the signs of x and of the bounds, so the bounds
        /// may have to be swapped back into order.
        /// </summary>
        public static void ReorderBounds(int n, double[] lb, double[] ub)
        {
            for (int i = 0; i < n; ++i)
                if (lb[i] > ub[i])
                {
                    double t = lb[i];
                    lb[i] = ub[i];
                    ub[i] = t;
                }
        }
    }
}
