using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d
{
    /// <summary>
    /// A square sparse matrix, row by row (compressed sparse rows): how the stiffness matrices of a
    /// linear buckling analysis come out of OpenSees and go into <see cref="BucklingSolver"/>, which
    /// only ever multiplies by them and factors one of them.
    /// </summary>
    public sealed class SparseMatrix
    {
        public int Size { get; }
        internal readonly int[] RowStart;
        internal readonly int[] Columns;
        internal readonly double[] Values;

        private SparseMatrix(int size, int[] rowStart, int[] columns, double[] values)
        {
            Size = size;
            RowStart = rowStart;
            Columns = columns;
            Values = values;
        }

        public int NonZeros => Values.Length;

        /// <summary>
        /// The matrix of <paramref name="count"/> entries, summed where they repeat. Exact zeros are
        /// left out: a matrix OpenSees prints dense is mostly zeros, and the sparse one lists the
        /// zeros of its pattern as well.
        /// </summary>
        public static SparseMatrix FromEntries(int size, int[] rows, int[] columns, double[] values, int count)
        {
            var perRow = new int[size + 1];
            for (int k = 0; k < count; k++)
            {
                if (rows[k] < 0 || rows[k] >= size || columns[k] < 0 || columns[k] >= size)
                    throw new ArgumentOutOfRangeException(nameof(rows), $"Entry ({rows[k]}, {columns[k]}) is outside a {size} x {size} matrix.");
                if (values[k] != 0.0)
                    perRow[rows[k] + 1]++;
            }
            for (int i = 0; i < size; i++)
                perRow[i + 1] += perRow[i];

            var cols = new int[perRow[size]];
            var vals = new double[perRow[size]];
            var next = (int[])perRow.Clone();
            for (int k = 0; k < count; k++)
            {
                if (values[k] == 0.0) continue;
                int at = next[rows[k]]++;
                cols[at] = columns[k];
                vals[at] = values[k];
            }

            // Sort each row by column and add up repeats in place.
            var rowStart = new int[size + 1];
            int write = 0;
            for (int i = 0; i < size; i++)
            {
                int from = perRow[i], to = perRow[i + 1];
                Array.Sort(cols, vals, from, to - from);
                rowStart[i] = write;
                for (int k = from; k < to; k++)
                {
                    if (write > rowStart[i] && cols[write - 1] == cols[k])
                        vals[write - 1] += vals[k];
                    else
                    {
                        cols[write] = cols[k];
                        vals[write] = vals[k];
                        write++;
                    }
                }
            }
            rowStart[size] = write;
            Array.Resize(ref cols, write);
            Array.Resize(ref vals, write);
            return new SparseMatrix(size, rowStart, cols, vals);
        }

        public double this[int row, int column]
        {
            get
            {
                int at = Array.BinarySearch(Columns, RowStart[row], RowStart[row + 1] - RowStart[row], column);
                return at >= 0 ? Values[at] : 0.0;
            }
        }

        /// <summary>y = A x.</summary>
        public void Multiply(double[] x, double[] y)
        {
            for (int i = 0; i < Size; i++)
            {
                double sum = 0.0;
                for (int k = RowStart[i]; k < RowStart[i + 1]; k++)
                    sum += Values[k] * x[Columns[k]];
                y[i] = sum;
            }
        }

        public double MaxAbs()
        {
            double max = 0.0;
            foreach (double v in Values)
                max = Math.Max(max, Math.Abs(v));
            return max;
        }

        /// <summary>How far from symmetric: the largest |a_ij − a_ji| over the largest |a_ij|.</summary>
        public double Asymmetry()
        {
            double max = MaxAbs(), worst = 0.0;
            if (max == 0.0) return 0.0;
            for (int i = 0; i < Size; i++)
                for (int k = RowStart[i]; k < RowStart[i + 1]; k++)
                    worst = Math.Max(worst, Math.Abs(Values[k] - this[Columns[k], i]));
            return worst / max;
        }

        /// <summary>(A + Aᵀ) / 2.</summary>
        public SparseMatrix Symmetrised()
        {
            int n = NonZeros;
            var rows = new int[2 * n];
            var cols = new int[2 * n];
            var vals = new double[2 * n];
            int at = 0;
            for (int i = 0; i < Size; i++)
                for (int k = RowStart[i]; k < RowStart[i + 1]; k++)
                {
                    rows[at] = i; cols[at] = Columns[k]; vals[at++] = 0.5 * Values[k];
                    rows[at] = Columns[k]; cols[at] = i; vals[at++] = 0.5 * Values[k];
                }
            return FromEntries(Size, rows, cols, vals, at);
        }

        /// <summary>
        /// a − b, with entries that differ only by round-off dropped: an entry goes when
        /// |a_ij − b_ij| ≤ <paramref name="noise"/> · max(|a_ij|, |b_ij|). That is what K_geo is,
        /// the small difference of two large matrices printed to a finite number of digits, and
        /// without it every entry of K_mat would leave a trace of its last digit in K_geo.
        /// </summary>
        public static SparseMatrix Difference(SparseMatrix a, SparseMatrix b, double noise)
        {
            if (a.Size != b.Size)
                throw new ArgumentException($"A {a.Size} x {a.Size} matrix less a {b.Size} x {b.Size} one.");

            var rows = new List<int>();
            var cols = new List<int>();
            var vals = new List<double>();
            for (int i = 0; i < a.Size; i++)
            {
                int ka = a.RowStart[i], ea = a.RowStart[i + 1];
                int kb = b.RowStart[i], eb = b.RowStart[i + 1];
                while (ka < ea || kb < eb)
                {
                    int ca = ka < ea ? a.Columns[ka] : int.MaxValue;
                    int cb = kb < eb ? b.Columns[kb] : int.MaxValue;
                    double va = 0.0, vb = 0.0;
                    int c;
                    if (ca == cb) { c = ca; va = a.Values[ka++]; vb = b.Values[kb++]; }
                    else if (ca < cb) { c = ca; va = a.Values[ka++]; }
                    else { c = cb; vb = b.Values[kb++]; }

                    double d = va - vb;
                    if (Math.Abs(d) > noise * Math.Max(Math.Abs(va), Math.Abs(vb)))
                    {
                        rows.Add(i); cols.Add(c); vals.Add(d);
                    }
                }
            }
            return FromEntries(a.Size, rows.ToArray(), cols.ToArray(), vals.ToArray(), vals.Count);
        }

        /// <summary>The matrix with more entries added to it, summed where they meet its own.</summary>
        public SparseMatrix Plus(IList<int> rows, IList<int> columns, IList<double> values)
        {
            int own = NonZeros, count = own + values.Count;
            var r = new int[count];
            var c = new int[count];
            var v = new double[count];
            for (int i = 0; i < Size; i++)
                for (int k = RowStart[i]; k < RowStart[i + 1]; k++)
                {
                    r[k] = i;
                    c[k] = Columns[k];
                    v[k] = Values[k];
                }
            for (int k = 0; k < values.Count; k++)
            {
                r[own + k] = rows[k];
                c[own + k] = columns[k];
                v[own + k] = values[k];
            }
            return FromEntries(Size, r, c, v, count);
        }

        /// <summary>The matrix with its rows and columns renumbered: entry (i, j) moves to (map[i], map[j]).</summary>
        public SparseMatrix Renumbered(int[] map)
        {
            int n = NonZeros;
            var rows = new int[n];
            var cols = new int[n];
            var vals = new double[n];
            int at = 0;
            for (int i = 0; i < Size; i++)
                for (int k = RowStart[i]; k < RowStart[i + 1]; k++)
                {
                    rows[at] = map[i]; cols[at] = map[Columns[k]]; vals[at++] = Values[k];
                }
            return FromEntries(Size, rows, cols, vals, at);
        }
    }

    /// <summary>
    /// K_mat is not positive definite: the model is a mechanism, or a support is missing, at the
    /// equation named.
    /// </summary>
    public sealed class NotPositiveDefiniteException : Exception
    {
        public int Equation { get; }

        public NotPositiveDefiniteException(int equation)
            : base($"The stiffness matrix is not positive definite at equation {equation}.")
        {
            Equation = equation;
        }
    }

    /// <summary>
    /// K = L Lᵀ, for a symmetric positive definite sparse K, kept as the envelope of L: each row
    /// from its first nonzero to the diagonal. The rows are first put in reverse Cuthill-McKee
    /// order, which keeps that envelope narrow for the meshes and frames a structural model is
    /// made of - it is what OpenSees's own ProfileSPD solver does.
    /// </summary>
    internal sealed class EnvelopeCholesky
    {
        private readonly int n;
        private readonly int[] order;    // new index -> equation
        private readonly int[] first;    // first column of each row of L, in the new numbering
        private readonly long[] diagonal; // where L(i, i) is in values
        private readonly double[] values;

        public long EnvelopeSize => values.LongLength;

        private EnvelopeCholesky(int n, int[] order, int[] first, long[] diagonal, double[] values)
        {
            this.n = n;
            this.order = order;
            this.first = first;
            this.diagonal = diagonal;
            this.values = values;
        }

        /// <summary>L for <paramref name="a"/>, which only has its lower triangle read.</summary>
        public static EnvelopeCholesky Factor(SparseMatrix a)
        {
            int n = a.Size;
            int[] order = ReverseCuthillMcKee(a);
            var position = new int[n];
            for (int i = 0; i < n; i++)
                position[order[i]] = i;

            var first = new int[n];
            for (int i = 0; i < n; i++)
            {
                int row = order[i], f = i;
                for (int k = a.RowStart[row]; k < a.RowStart[row + 1]; k++)
                    f = Math.Min(f, position[a.Columns[k]]);
                first[i] = f;
            }

            var diagonal = new long[n];
            long size = 0;
            for (int i = 0; i < n; i++)
            {
                size += i - first[i] + 1;
                diagonal[i] = size - 1;
            }
            if (size > int.MaxValue)
                throw new InvalidOperationException(
                    $"The factor of the stiffness matrix would hold {size:N0} numbers, more than one array can.");
            var values = new double[size];

            // Scatter the lower triangle of the reordered matrix into the envelope.
            for (int i = 0; i < n; i++)
            {
                int row = order[i];
                long start = diagonal[i] - i;
                for (int k = a.RowStart[row]; k < a.RowStart[row + 1]; k++)
                {
                    int j = position[a.Columns[k]];
                    if (j <= i)
                        values[start + j] = a.Values[k];
                }
            }

            // Row by row: L(i, j) for j < i from the rows above, then the diagonal.
            for (int i = 0; i < n; i++)
            {
                int fi = first[i];
                long si = diagonal[i] - i;
                for (int j = fi; j < i; j++)
                {
                    int k0 = Math.Max(fi, first[j]);
                    long sj = diagonal[j] - j;
                    double sum = values[si + j];
                    for (int k = k0; k < j; k++)
                        sum -= values[si + k] * values[sj + k];
                    values[si + j] = sum / values[diagonal[j]];
                }

                double d = values[diagonal[i]], original = d;
                for (int k = fi; k < i; k++)
                    d -= values[si + k] * values[si + k];
                if (!(d > 1e-12 * Math.Abs(original)) || !(d > 0.0))
                    throw new NotPositiveDefiniteException(order[i]);
                values[diagonal[i]] = Math.Sqrt(d);
            }

            return new EnvelopeCholesky(n, order, first, diagonal, values);
        }

        /// <summary>x = K⁻¹ b, both in the original numbering. <paramref name="work"/> is scratch of length n.</summary>
        public void Solve(double[] b, double[] x, double[] work)
        {
            for (int i = 0; i < n; i++)
            {
                long si = diagonal[i] - i;
                double sum = b[order[i]];
                for (int k = first[i]; k < i; k++)
                    sum -= values[si + k] * work[k];
                work[i] = sum / values[diagonal[i]];
            }
            for (int i = n - 1; i >= 0; i--)
            {
                long si = diagonal[i] - i;
                double v = work[i] / values[diagonal[i]];
                work[i] = v;
                for (int k = first[i]; k < i; k++)
                    work[k] -= values[si + k] * v;
            }
            for (int i = 0; i < n; i++)
                x[order[i]] = work[i];
        }

        /// <summary>
        /// Reverse Cuthill-McKee order of the graph of <paramref name="a"/>, one connected part
        /// after another, each started from a pseudo-peripheral equation (George and Liu).
        /// </summary>
        internal static int[] ReverseCuthillMcKee(SparseMatrix a)
        {
            int n = a.Size;
            var degree = new int[n];
            for (int i = 0; i < n; i++)
                degree[i] = a.RowStart[i + 1] - a.RowStart[i];

            var placed = new bool[n];
            var order = new List<int>(n);
            var level = new int[n];
            var neighbours = new List<int>();

            // Breadth first from start over what is not yet placed; returns the equations reached,
            // level by level, and leaves the level of each in level[].
            List<int> Sweep(int start, bool place)
            {
                var reached = new List<int> { start };
                var seen = new HashSet<int> { start };
                level[start] = 0;
                if (place) placed[start] = true;
                for (int head = 0; head < reached.Count; head++)
                {
                    int v = reached[head];
                    neighbours.Clear();
                    for (int k = a.RowStart[v]; k < a.RowStart[v + 1]; k++)
                    {
                        int w = a.Columns[k];
                        if (w != v && !placed[w] && !seen.Contains(w))
                            neighbours.Add(w);
                    }
                    neighbours.Sort((p, q) => degree[p].CompareTo(degree[q]));
                    foreach (int w in neighbours)
                    {
                        seen.Add(w);
                        level[w] = level[v] + 1;
                        if (place) placed[w] = true;
                        reached.Add(w);
                    }
                }
                return reached;
            }

            for (int seed = 0; seed < n; seed++)
            {
                if (placed[seed]) continue;

                // Pseudo-peripheral start: move to a lowest-degree equation of the deepest level
                // while that makes the level structure deeper.
                int start = seed, depth = -1;
                for (int pass = 0; pass < 8; pass++)
                {
                    var reached = Sweep(start, false);
                    int deepest = level[reached[reached.Count - 1]];
                    if (deepest <= depth) break;
                    depth = deepest;
                    int best = start;
                    foreach (int v in reached)
                        if (level[v] == deepest && (best == start || degree[v] < degree[best]))
                            best = v;
                    if (best == start) break;
                    start = best;
                }

                order.AddRange(Sweep(start, true));
            }

            order.Reverse();
            return order.ToArray();
        }
    }

    /// <summary>
    /// The linear buckling problem K_mat φ = λ K_geo φ: the load factors λ at which the stiffness
    /// left under the reference load, K_mat − λ K_geo, stops being positive definite, lowest first,
    /// and the shapes φ the structure buckles into.
    ///
    /// Solved as K_geo φ = μ K_mat φ with μ = 1/λ, because K_mat is positive definite and K_geo is
    /// not - members in tension soften nothing - and the lowest λ are then the largest μ. Subspace
    /// iteration (Bathe) finds them: a block of vectors is pushed through K_mat⁻¹ K_geo, made
    /// K_mat-orthonormal and rotated onto the best approximation of the modes the block can hold.
    /// It is slower to converge than Lanczos but does not lose a mode that has a twin, as the two
    /// sway modes of a square tower are.
    /// </summary>
    public static class BucklingSolver
    {
        public sealed class Result
        {
            /// <summary>The load factors, lowest first. Fewer than asked for when the load buckles fewer modes.</summary>
            public double[] Factors = new double[0];

            /// <summary>One shape per factor, by equation, K_mat-normalised.</summary>
            public double[][] Modes = new double[0][];

            /// <summary>|K_geo φ − μ K_mat φ| / |K_geo φ| for each mode, once converged.</summary>
            public double[] Residuals = new double[0];

            public int Iterations;
            public bool Converged;
            public int SubspaceSize;
            public long FactorSize;
        }

        /// <param name="modes">How many of the lowest load factors to find.</param>
        /// <param name="tolerance">Relative change of each factor between iterations to stop at.</param>
        public static Result Solve(SparseMatrix kMat, SparseMatrix kGeo, int modes, double tolerance = 1e-10, int maxIterations = 1000)
        {
            int n = kMat.Size;
            if (kGeo.Size != n)
                throw new ArgumentException($"K_mat is {n} x {n} and K_geo {kGeo.Size} x {kGeo.Size}.");

            var result = new Result();
            if (n == 0 || modes < 1)
                return result;

            var factor = EnvelopeCholesky.Factor(kMat);
            result.FactorSize = factor.EnvelopeSize;

            // A load that compresses nothing has no geometric stiffness to buckle with.
            if (kGeo.NonZeros == 0 || kGeo.MaxAbs() == 0.0)
            {
                result.Converged = true;
                return result;
            }

            int k = Math.Min(modes, n);
            int p = Math.Min(n, Math.Max(2 * k, k + 8));
            var random = new Random(20261017);
            var work = new double[n];

            // Starting block: the equations whose geometric stiffness is largest next to their
            // material stiffness, then random vectors.
            var x = new List<double[]>();
            var start = new double[n];
            for (int i = 0; i < n; i++)
            {
                double km = kMat[i, i];
                start[i] = km > 0.0 ? Math.Abs(kGeo[i, i]) / km : 0.0;
            }
            x.Add(start);
            while (x.Count < p)
                x.Add(RandomVector(n, random));

            var y = x.Select(v => Times(kGeo, v)).ToList();   // K_geo x
            double[] previous = null;

            for (int iteration = 1; iteration <= maxIterations; iteration++)
            {
                result.Iterations = iteration;

                // x̄ = K_mat⁻¹ K_geo x, and K_mat x̄ = y exactly, which makes K_mat-orthonormalising
                // the block cost no product with K_mat.
                var xb = new List<double[]>(p);
                var kx = new List<double[]>(p);
                for (int j = 0; j < p; j++)
                {
                    var v = new double[n];
                    factor.Solve(y[j], v, work);
                    xb.Add(v);
                    kx.Add((double[])y[j].Clone());
                }
                Orthonormalise(xb, kx, factor, kGeo, random, work);

                var z = xb.Select(v => Times(kGeo, v)).ToList();   // K_geo x̄
                var a = new double[p, p];
                for (int i = 0; i < p; i++)
                    for (int j = 0; j <= i; j++)
                        a[i, j] = a[j, i] = 0.5 * (Dot(xb[i], z[j]) + Dot(xb[j], z[i]));

                Jacobi(a, p, out double[] mu, out double[,] q);
                var rank = Enumerable.Range(0, p).OrderByDescending(i => mu[i]).ToArray();

                // Rotate the block onto the Ritz vectors, largest μ first.
                var xn = new List<double[]>(p);
                var yn = new List<double[]>(p);
                var kn = new List<double[]>(p);
                foreach (int r in rank)
                {
                    xn.Add(Combine(xb, q, r, n));
                    yn.Add(Combine(z, q, r, n));
                    kn.Add(Combine(kx, q, r, n));
                }
                var sorted = rank.Select(r => mu[r]).ToArray();

                double scale = sorted.Max(Math.Abs);
                var wanted = sorted.Where(m => m > 1e-13 * scale).Take(k).ToArray();

                bool steady = previous != null && previous.Length == wanted.Length &&
                              wanted.Select((m, i) => Math.Abs(m - previous[i]) <= tolerance * Math.Abs(m)).All(ok => ok);
                previous = wanted;
                x = xn;
                y = yn;

                if (!steady && p < n)
                    continue;

                // The block holds fewer positive μ than asked for: members in tension are taking
                // its room, so it grows, unless it already spans everything.
                if (wanted.Length < k && p < n && p < 8 * k + 64)
                {
                    int grown = Math.Min(n, 2 * p);
                    while (x.Count < grown)
                    {
                        var v = RandomVector(n, random);
                        x.Add(v);
                        y.Add(Times(kGeo, v));
                    }
                    p = grown;
                    previous = null;
                    continue;
                }

                result.Converged = true;
                result.SubspaceSize = p;
                result.Factors = wanted.Select(m => 1.0 / m).ToArray();
                result.Modes = xn.Take(wanted.Length).ToArray();
                result.Residuals = Enumerable.Range(0, wanted.Length).Select(i =>
                {
                    double num = 0.0, den = 0.0;
                    for (int e = 0; e < n; e++)
                    {
                        double r = yn[i][e] - wanted[i] * kn[i][e];
                        num += r * r;
                        den += yn[i][e] * yn[i][e];
                    }
                    return den > 0.0 ? Math.Sqrt(num / den) : 0.0;
                }).ToArray();
                return result;
            }

            // Out of iterations: hand back the best estimate, flagged.
            result.SubspaceSize = p;
            result.Factors = (previous ?? new double[0]).Select(m => 1.0 / m).ToArray();
            result.Modes = x.Take(result.Factors.Length).ToArray();
            result.Residuals = result.Factors.Select(_ => double.NaN).ToArray();
            return result;
        }

        /// <summary>
        /// Modified Gram-Schmidt in the K_mat inner product, keeping kx = K_mat x̄ in step. A vector
        /// that has nothing left once the others are taken out of it is replaced with a fresh one.
        /// </summary>
        private static void Orthonormalise(List<double[]> xb, List<double[]> kx, EnvelopeCholesky factor,
                                           SparseMatrix kGeo, Random random, double[] work)
        {
            int n = work.Length;
            for (int i = 0; i < xb.Count; i++)
            {
                for (int attempt = 0; ; attempt++)
                {
                    double before = Math.Sqrt(Math.Max(Dot(xb[i], kx[i]), 0.0));
                    for (int j = 0; j < i; j++)
                    {
                        double r = Dot(xb[j], kx[i]);
                        Axpy(-r, xb[j], xb[i]);
                        Axpy(-r, kx[j], kx[i]);
                    }
                    double norm = Math.Sqrt(Math.Max(Dot(xb[i], kx[i]), 0.0));
                    if (norm > 1e-10 * before && norm > 0.0)
                    {
                        Scale(1.0 / norm, xb[i]);
                        Scale(1.0 / norm, kx[i]);
                        break;
                    }
                    if (attempt == 3)
                        throw new InvalidOperationException("Could not build an independent set of buckling vectors.");

                    // K_mat x̄ = r for a random r gives x̄ and K_mat x̄ together.
                    var r0 = RandomVector(n, random);
                    factor.Solve(r0, xb[i], work);
                    kx[i] = r0;
                }
            }
        }

        /// <summary>Cyclic Jacobi on the symmetric p x p matrix a: eigenvalues and eigenvectors (columns of v).</summary>
        internal static void Jacobi(double[,] a, int p, out double[] values, out double[,] vectors)
        {
            var m = (double[,])a.Clone();
            var v = new double[p, p];
            for (int i = 0; i < p; i++) v[i, i] = 1.0;

            for (int sweep = 0; sweep < 100; sweep++)
            {
                double off = 0.0, diag = 0.0;
                for (int i = 0; i < p; i++)
                {
                    diag += m[i, i] * m[i, i];
                    for (int j = i + 1; j < p; j++)
                        off += m[i, j] * m[i, j];
                }
                if (off <= 1e-30 * diag || off == 0.0)
                    break;

                for (int i = 0; i < p - 1; i++)
                    for (int j = i + 1; j < p; j++)
                    {
                        if (m[i, j] == 0.0) continue;
                        double theta = (m[j, j] - m[i, i]) / (2.0 * m[i, j]);
                        double t = Math.Sign(theta == 0.0 ? 1.0 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                        double c = 1.0 / Math.Sqrt(t * t + 1.0), s = t * c;
                        for (int r = 0; r < p; r++)
                        {
                            double mri = m[r, i], mrj = m[r, j];
                            m[r, i] = c * mri - s * mrj;
                            m[r, j] = s * mri + c * mrj;
                        }
                        for (int r = 0; r < p; r++)
                        {
                            double mir = m[i, r], mjr = m[j, r];
                            m[i, r] = c * mir - s * mjr;
                            m[j, r] = s * mir + c * mjr;
                        }
                        for (int r = 0; r < p; r++)
                        {
                            double vri = v[r, i], vrj = v[r, j];
                            v[r, i] = c * vri - s * vrj;
                            v[r, j] = s * vri + c * vrj;
                        }
                    }
            }

            values = new double[p];
            for (int i = 0; i < p; i++) values[i] = m[i, i];
            vectors = v;
        }

        private static double[] Combine(List<double[]> basis, double[,] q, int column, int n)
        {
            var result = new double[n];
            for (int a = 0; a < basis.Count; a++)
            {
                double c = q[a, column];
                if (c != 0.0)
                    Axpy(c, basis[a], result);
            }
            return result;
        }

        private static double[] Times(SparseMatrix m, double[] v)
        {
            var result = new double[v.Length];
            m.Multiply(v, result);
            return result;
        }

        private static double[] RandomVector(int n, Random random)
        {
            var v = new double[n];
            for (int i = 0; i < n; i++)
                v[i] = 2.0 * random.NextDouble() - 1.0;
            return v;
        }

        private static double Dot(double[] a, double[] b)
        {
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
                sum += a[i] * b[i];
            return sum;
        }

        private static void Axpy(double alpha, double[] x, double[] y)
        {
            for (int i = 0; i < x.Length; i++)
                y[i] += alpha * x[i];
        }

        private static void Scale(double alpha, double[] x)
        {
            for (int i = 0; i < x.Length; i++)
                x[i] *= alpha;
        }
    }
}
