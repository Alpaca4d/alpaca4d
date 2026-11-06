// Ported from NLopt, src/util/qsort_r.c: the qsort routine from Bentley & McIlroy's
// "Engineering a Sort Function", as NLopt carries it.
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
    /// nlopt_qsort_r, on an array of indices, which is all NLopt sorts with it (DIRECT, ISRES and
    /// Sbplx). NLopt hands this to the C library's qsort_r on macOS and Linux and uses this copy
    /// elsewhere; the libraries order equal keys differently, so this copy is the one ported, and
    /// equal keys come out in the same order on every platform.
    /// </summary>
    internal static class QSort
    {
        /// <summary>Sorts a[0..n) with <paramref name="cmp"/> comparing two elements' values.</summary>
        public static void Sort(int[] a, int n, Func<int, int, int> cmp) => Sort(a, 0, n, cmp);

        private static void Swap(int[] arr, int i, int j)
        {
            int t = arr[i];
            arr[i] = arr[j];
            arr[j] = t;
        }

        private static void VecSwap(int[] arr, int i, int j, int n)
        {
            for (int k = 0; k < n; k++) Swap(arr, i + k, j + k);
        }

        private static int Med3(int[] arr, int a, int b, int c, Func<int, int, int> cmp)
        {
            return cmp(arr[a], arr[b]) < 0
                ? (cmp(arr[b], arr[c]) < 0 ? b : (cmp(arr[a], arr[c]) < 0 ? c : a))
                : (cmp(arr[b], arr[c]) > 0 ? b : (cmp(arr[a], arr[c]) < 0 ? a : c));
        }

        // Positions are element indices where the C has byte pointers, so "> es" is "> 1".
        private static void Sort(int[] arr, int a, int n, Func<int, int, int> cmp)
        {
            int pa, pb, pc, pd, pl, pm, pn;
            int d1, d2;
            int cmpResult;
            bool swapCnt;

        loop:
            swapCnt = false;
            if (n < 7)
            {
                for (pm = a + 1; pm < a + n; pm++)
                    for (pl = pm; pl > a && cmp(arr[pl - 1], arr[pl]) > 0; pl--)
                        Swap(arr, pl, pl - 1);
                return;
            }
            pm = a + n / 2;
            if (n > 7)
            {
                pl = a;
                pn = a + n - 1;
                if (n > 40)
                {
                    int d = n / 8;
                    pl = Med3(arr, pl, pl + d, pl + 2 * d, cmp);
                    pm = Med3(arr, pm - d, pm, pm + d, cmp);
                    pn = Med3(arr, pn - 2 * d, pn - d, pn, cmp);
                }
                pm = Med3(arr, pl, pm, pn, cmp);
            }
            Swap(arr, a, pm);
            pa = pb = a + 1;

            pc = pd = a + n - 1;
            for (;;)
            {
                while (pb <= pc && (cmpResult = cmp(arr[pb], arr[a])) <= 0)
                {
                    if (cmpResult == 0)
                    {
                        swapCnt = true;
                        Swap(arr, pa, pb);
                        pa++;
                    }
                    pb++;
                }
                while (pb <= pc && (cmpResult = cmp(arr[pc], arr[a])) >= 0)
                {
                    if (cmpResult == 0)
                    {
                        swapCnt = true;
                        Swap(arr, pc, pd);
                        pd--;
                    }
                    pc--;
                }
                if (pb > pc) break;
                Swap(arr, pb, pc);
                swapCnt = true;
                pb++;
                pc--;
            }
            if (!swapCnt)
            {
                // Switch to insertion sort
                for (pm = a + 1; pm < a + n; pm++)
                    for (pl = pm; pl > a && cmp(arr[pl - 1], arr[pl]) > 0; pl--)
                        Swap(arr, pl, pl - 1);
                return;
            }

            pn = a + n;
            d1 = Math.Min(pa - a, pb - pa);
            VecSwap(arr, a, pb - d1, d1);
            d1 = Math.Min(pd - pc, pn - pd - 1);
            VecSwap(arr, pb, pn - d1, d1);

            d1 = pb - pa;
            d2 = pd - pc;
            if (d1 <= d2)
            {
                // Recurse on left partition, then iterate on right partition
                if (d1 > 1) Sort(arr, a, d1, cmp);
                if (d2 > 1)
                {
                    a = pn - d2;
                    n = d2;
                    goto loop;
                }
            }
            else
            {
                // Recurse on right partition, then iterate on left partition
                if (d2 > 1) Sort(arr, pn - d2, d2, cmp);
                if (d1 > 1)
                {
                    n = d1;
                    goto loop;
                }
            }
        }
    }
}
