// Ported from NLopt, src/util/nlopt-util.h and src/util/stop.c.
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
    /// nlopt_constraint: M values of x that must each be &lt;= 0 (or = 0, for an equality), each
    /// counted as met when it is out by no more than its tolerance. Only the vector form is kept,
    /// as everything Alpaca4d hands NLopt comes as a vector.
    /// </summary>
    internal sealed class Constraint
    {
        public int M;
        public ConstraintFunction Mf;
        public double[] Tol;

        /// <summary>nlopt_count_constraints: how many values p constraints give in all.</summary>
        public static int Count(int p, Constraint[] c)
        {
            int count = 0;
            for (int i = 0; i < p; ++i) count += c[i].M;
            return count;
        }

        /// <summary>nlopt_max_constraint_dim: the most values any one of p constraints gives.</summary>
        public static int MaxDim(int p, Constraint[] c)
        {
            int maxDim = 0;
            for (int i = 0; i < p; ++i)
                if (c[i].M > maxDim) maxDim = c[i].M;
            return maxDim;
        }

        /// <summary>
        /// nlopt_eval_constraint, into result[offset..offset + M). x has exactly n values: copy
        /// out of a larger array before calling.
        /// </summary>
        public static void Eval(double[] result, int offset, Constraint c, int n, double[] x)
        {
            if (offset == 0 && result.Length == c.M)
            {
                c.Mf(x, result);
                return;
            }
            var values = new double[c.M];
            c.Mf(x, values);
            Array.Copy(values, 0, result, offset, c.M);
        }
    }
}
