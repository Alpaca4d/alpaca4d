using System;
using System.Linq;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// The method of moving asymptotes (Svanberg 1987, "The method of moving asymptotes - a new method
    /// for structural optimization", IJNME 24:359), for problems of the form
    ///
    ///     minimise   f0(x) + a0 z + sum_i (c_i y_i + d_i y_i^2 / 2)
    ///     subject to f_i(x) - a_i z - y_i &lt;= 0,  i = 1..m
    ///                xmin_j &lt;= x_j &lt;= xmax_j,  y &gt;= 0,  z &gt;= 0.
    ///
    /// Each iteration replaces every f_i by a convex approximation through the gradients at the current
    /// point, with poles at the moving asymptotes l_j and u_j, and the solution of that subproblem is the
    /// next point. The subproblem is solved by a primal-dual interior point method.
    ///
    /// Written from Svanberg's note "MMA and GCMMA - two methods for nonlinear optimization" (2007),
    /// equation by equation: the approximations (3.2)-(3.5), the move limits (3.6)-(3.7), the asymptotes
    /// (3.11)-(3.14), and the primal-dual method of its section 5, with the (m+1) x (m+1) system (5.20),
    /// as m is small here and n large. The parameters are the note's.
    ///
    /// One object holds one problem's history - the two points before and the asymptotes - so that each
    /// <see cref="Update"/> is one iteration. <see cref="Copy"/> keeps it, to go on from it.
    /// </summary>
    public sealed class Mma
    {
        private const double AsymptoteInit = 0.5, AsymptoteIncrease = 1.2, AsymptoteDecrease = 0.7;
        private const double AlphaBeta = 0.1, MoveLimit = 0.5, Raa0 = 1e-5, EpsilonMin = 1e-7;

        public int N { get; }
        public int M { get; }
        public double A0 { get; }
        private readonly double[] _a, _c, _d;

        /// <summary>How many updates have been made: the k of the note, less one.</summary>
        public int Iterations { get; private set; }

        private double[] _low, _upp, _xold1, _xold2;

        /// <param name="n">The number of variables x.</param>
        /// <param name="m">The number of constraints.</param>
        /// <param name="a0">The note's a0, positive.</param>
        /// <param name="a">a_i, one per constraint, at least 0.</param>
        /// <param name="c">c_i, one per constraint: large makes y_i expensive, so the constraint holds.</param>
        /// <param name="d">d_i, one per constraint.</param>
        public Mma(int n, int m, double a0, double[] a, double[] c, double[] d)
        {
            if (n < 1 || m < 1)
                throw new ArgumentException("MMA needs at least one variable and one constraint.");
            if (a.Length != m || c.Length != m || d.Length != m)
                throw new ArgumentException($"a, c and d need one value per constraint, {m}.");
            N = n;
            M = m;
            A0 = a0;
            _a = (double[])a.Clone();
            _c = (double[])c.Clone();
            _d = (double[])d.Clone();
        }

        /// <summary>
        /// The usual case: minimise f0 subject to f_i &lt;= 0, with the artificial variables all but out of
        /// it - a0 = 1, a = 0, d = 0 and c = <paramref name="c"/>, large, for every constraint.
        /// </summary>
        public static Mma Standard(int n, int m, double c = 1e6) =>
            new Mma(n, m, 1.0, new double[m], Enumerable.Repeat(c, m).ToArray(), new double[m]);

        public Mma Copy()
        {
            var copy = new Mma(N, M, A0, _a, _c, _d) { Iterations = Iterations };
            copy._low = (double[])_low?.Clone();
            copy._upp = (double[])_upp?.Clone();
            copy._xold1 = (double[])_xold1?.Clone();
            copy._xold2 = (double[])_xold2?.Clone();
            return copy;
        }

        /// <summary>
        /// One iteration from <paramref name="x"/>: the next point, from the functions' values and gradients
        /// there, within <paramref name="xmin"/> and <paramref name="xmax"/>.
        /// </summary>
        /// <param name="f0">The objective at x.</param>
        /// <param name="df0dx">Its gradient, n values.</param>
        /// <param name="f">The constraint functions at x, m values: a constraint holds when its value is at most 0.</param>
        /// <param name="dfdx">Their gradients, m rows of n.</param>
        public double[] Update(double[] x, double[] xmin, double[] xmax, double f0, double[] df0dx, double[] f, double[][] dfdx)
        {
            int n = N, m = M;
            if (x.Length != n || xmin.Length != n || xmax.Length != n || df0dx.Length != n || f.Length != m || dfdx.Length != m)
                throw new ArgumentException("The point, its bounds and the gradients do not fit the problem's size.");
            Iterations++;
            int k = Iterations;

            // The asymptotes, (3.11)-(3.14).
            var low = new double[n];
            var upp = new double[n];
            for (int j = 0; j < n; j++)
            {
                double range = xmax[j] - xmin[j];
                if (k <= 2)
                {
                    low[j] = x[j] - AsymptoteInit * range;
                    upp[j] = x[j] + AsymptoteInit * range;
                    continue;
                }
                double trend = (x[j] - _xold1[j]) * (_xold1[j] - _xold2[j]);
                double gamma = trend > 0.0 ? AsymptoteIncrease : trend < 0.0 ? AsymptoteDecrease : 1.0;
                low[j] = x[j] - gamma * (_xold1[j] - _low[j]);
                upp[j] = x[j] + gamma * (_upp[j] - _xold1[j]);
                low[j] = Math.Min(Math.Max(low[j], x[j] - 10.0 * range), x[j] - 0.01 * range);
                upp[j] = Math.Max(Math.Min(upp[j], x[j] + 10.0 * range), x[j] + 0.01 * range);
            }

            // The bounds of the subproblem, (3.6)-(3.7).
            var alpha = new double[n];
            var beta = new double[n];
            for (int j = 0; j < n; j++)
            {
                double range = xmax[j] - xmin[j];
                alpha[j] = Math.Max(xmin[j], Math.Max(low[j] + AlphaBeta * (x[j] - low[j]), x[j] - MoveLimit * range));
                beta[j] = Math.Min(xmax[j], Math.Min(upp[j] - AlphaBeta * (upp[j] - x[j]), x[j] + MoveLimit * range));
            }

            // The approximations, (3.2)-(3.5): p and q of the objective and of each constraint, and b = -r.
            var p0 = new double[n];
            var q0 = new double[n];
            var p = new double[m][];
            var q = new double[m][];
            var b = new double[m];
            for (int i = 0; i < m; i++)
            {
                p[i] = new double[n];
                q[i] = new double[n];
            }
            for (int j = 0; j < n; j++)
            {
                double ux = upp[j] - x[j], xl = x[j] - low[j];
                double raa = Raa0 / Math.Max(xmax[j] - xmin[j], 1e-5);
                (p0[j], q0[j]) = PQ(df0dx[j], ux, xl, raa);
                for (int i = 0; i < m; i++)
                {
                    (p[i][j], q[i][j]) = PQ(dfdx[i][j], ux, xl, raa);
                    b[i] += p[i][j] / ux + q[i][j] / xl;
                }
            }
            for (int i = 0; i < m; i++)
                b[i] -= f[i];

            var next = Subproblem(low, upp, alpha, beta, p0, q0, p, q, b);

            _xold2 = _xold1 ?? (double[])x.Clone();
            _xold1 = (double[])x.Clone();
            _low = low;
            _upp = upp;
            return next;
        }

        /// <summary>(3.3) and (3.4) for one variable and one function.</summary>
        private static (double p, double q) PQ(double derivative, double ux, double xl, double raa)
        {
            double plus = Math.Max(derivative, 0.0), minus = Math.Max(-derivative, 0.0);
            return (ux * ux * (1.001 * plus + 0.001 * minus + raa), xl * xl * (0.001 * plus + 1.001 * minus + raa));
        }

        /// <summary>
        /// The primal-dual interior point method of the note's section 5 for the subproblem (5.1): the
        /// relaxed optimality conditions (5.9) solved by Newton's method, with epsilon made ten times
        /// smaller each time they hold to 0.9 epsilon, until epsilon is 1e-7.
        /// </summary>
        private double[] Subproblem(double[] low, double[] upp, double[] alpha, double[] beta,
                                    double[] p0, double[] q0, double[][] p, double[][] q, double[] b)
        {
            int n = N, m = M;
            var a = _a; var c = _c; var d = _d;

            // The starting point of section 5.5.
            var w = new State(n, m);
            for (int j = 0; j < n; j++)
            {
                w.X[j] = 0.5 * (alpha[j] + beta[j]);
                w.Xi[j] = Math.Max(1.0, 1.0 / (w.X[j] - alpha[j]));
                w.Eta[j] = Math.Max(1.0, 1.0 / (beta[j] - w.X[j]));
            }
            for (int i = 0; i < m; i++)
            {
                w.Y[i] = 1.0;
                w.Lambda[i] = 1.0;
                w.S[i] = 1.0;
                w.Mu[i] = Math.Max(1.0, 0.5 * c[i]);
            }
            w.Z = 1.0;
            w.Zeta = 1.0;

            double epsilon = 1.0;
            var g = new double[m][];
            for (int i = 0; i < m; i++)
                g[i] = new double[n];
            var dx = new double[n];
            var diagX = new double[n];
            var deltaX = new double[n];

            while (epsilon > EpsilonMin)
            {
                var (norm, largest) = Residual(w, epsilon, low, upp, alpha, beta, p0, q0, p, q, b);
                for (int newton = 0; newton < 200 && largest > 0.9 * epsilon; newton++)
                {
                    // The Newton direction, (5.13)-(5.20).
                    var lambda = w.Lambda;
                    var deltaLambdaY = new double[m];
                    var diagLambdaY = new double[m];
                    var diagY = new double[m];
                    var deltaY = new double[m];
                    for (int i = 0; i < m; i++)
                    {
                        diagY[i] = d[i] + w.Mu[i] / w.Y[i];
                        deltaY[i] = c[i] + d[i] * w.Y[i] - lambda[i] - epsilon / w.Y[i];
                        diagLambdaY[i] = w.S[i] / lambda[i] + 1.0 / diagY[i];
                    }

                    var gx = new double[m];
                    for (int j = 0; j < n; j++)
                    {
                        double ux = upp[j] - w.X[j], xl = w.X[j] - low[j];
                        double ux2 = ux * ux, xl2 = xl * xl;
                        double pl = p0[j], ql = q0[j];
                        for (int i = 0; i < m; i++)
                        {
                            pl += lambda[i] * p[i][j];
                            ql += lambda[i] * q[i][j];
                            gx[i] += p[i][j] / ux + q[i][j] / xl;
                            g[i][j] = p[i][j] / ux2 - q[i][j] / xl2;
                        }
                        double xa = w.X[j] - alpha[j], bx = beta[j] - w.X[j];
                        deltaX[j] = pl / ux2 - ql / xl2 - epsilon / xa + epsilon / bx;
                        diagX[j] = 2.0 * (pl / (ux2 * ux) + ql / (xl2 * xl)) + w.Xi[j] / xa + w.Eta[j] / bx;
                    }
                    double deltaZ = A0 - Dot(a, lambda) - epsilon / w.Z;
                    for (int i = 0; i < m; i++)
                    {
                        double deltaLambda = gx[i] - a[i] * w.Z - w.Y[i] - b[i] + epsilon / lambda[i];
                        deltaLambdaY[i] = deltaLambda + deltaY[i] / diagY[i];
                    }

                    // (5.20): [D_ly + G D_x^-1 G^T, a; a^T, -zeta/z] [dlambda; dz] = [delta_ly - G D_x^-1 delta_x; delta_z].
                    var system = new double[m + 1, m + 1];
                    var rhs = new double[m + 1];
                    for (int i = 0; i < m; i++)
                    {
                        double sum = 0.0;
                        for (int j = 0; j < n; j++)
                            sum += g[i][j] * deltaX[j] / diagX[j];
                        rhs[i] = deltaLambdaY[i] - sum;
                        for (int l = 0; l <= i; l++)
                        {
                            double s = 0.0;
                            for (int j = 0; j < n; j++)
                                s += g[i][j] * g[l][j] / diagX[j];
                            system[i, l] = s;
                            system[l, i] = s;
                        }
                        system[i, i] += diagLambdaY[i];
                        system[i, m] = a[i];
                        system[m, i] = a[i];
                    }
                    system[m, m] = -w.Zeta / w.Z;
                    rhs[m] = deltaZ;
                    var solution = Solve(system, rhs);

                    var step = new State(n, m);
                    for (int i = 0; i < m; i++)
                        step.Lambda[i] = solution[i];
                    step.Z = solution[m];
                    for (int j = 0; j < n; j++)
                    {
                        double gl = 0.0;
                        for (int i = 0; i < m; i++)
                            gl += g[i][j] * step.Lambda[i];
                        dx[j] = -deltaX[j] / diagX[j] - gl / diagX[j];
                        step.X[j] = dx[j];
                        double xa = w.X[j] - alpha[j], bx = beta[j] - w.X[j];
                        step.Xi[j] = -w.Xi[j] + epsilon / xa - w.Xi[j] * dx[j] / xa;
                        step.Eta[j] = -w.Eta[j] + epsilon / bx + w.Eta[j] * dx[j] / bx;
                    }
                    for (int i = 0; i < m; i++)
                    {
                        step.Y[i] = -deltaY[i] / diagY[i] + step.Lambda[i] / diagY[i];
                        step.Mu[i] = -w.Mu[i] + epsilon / w.Y[i] - w.Mu[i] * step.Y[i] / w.Y[i];
                        step.S[i] = -w.S[i] + epsilon / lambda[i] - w.S[i] * step.Lambda[i] / lambda[i];
                    }
                    step.Zeta = -w.Zeta + epsilon / w.Z - w.Zeta * step.Z / w.Z;

                    // The step length of section 5.4: every variable that has to stay positive keeps at least a
                    // hundredth of what it was, and so does each distance to alpha and beta.
                    double t = 1.0;
                    void Limit(double value, double change)
                    {
                        if (change < 0.0)
                            t = Math.Min(t, 0.99 * value / -change);
                    }
                    for (int j = 0; j < n; j++)
                    {
                        Limit(w.X[j] - alpha[j], step.X[j]);
                        Limit(beta[j] - w.X[j], -step.X[j]);
                        Limit(w.Xi[j], step.Xi[j]);
                        Limit(w.Eta[j], step.Eta[j]);
                    }
                    for (int i = 0; i < m; i++)
                    {
                        Limit(w.Y[i], step.Y[i]);
                        Limit(w.Lambda[i], step.Lambda[i]);
                        Limit(w.Mu[i], step.Mu[i]);
                        Limit(w.S[i], step.S[i]);
                    }
                    Limit(w.Z, step.Z);
                    Limit(w.Zeta, step.Zeta);

                    // Then halved until the residual is smaller than it was.
                    State tried = null;
                    double triedNorm = double.PositiveInfinity, triedLargest = double.PositiveInfinity;
                    for (int halving = 0; halving < 50; halving++)
                    {
                        tried = w.Plus(step, t);
                        (triedNorm, triedLargest) = Residual(tried, epsilon, low, upp, alpha, beta, p0, q0, p, q, b);
                        if (triedNorm < norm)
                            break;
                        t *= 0.5;
                    }
                    w = tried;
                    norm = triedNorm;
                    largest = triedLargest;
                }
                epsilon *= 0.1;
            }
            return w.X;
        }

        /// <summary>The Euclidean and largest norms of the left hand sides of (5.9a)-(5.9i).</summary>
        private (double norm, double largest) Residual(State w, double epsilon, double[] low, double[] upp, double[] alpha, double[] beta,
                                                       double[] p0, double[] q0, double[][] p, double[][] q, double[] b)
        {
            int n = N, m = M;
            double sum = 0.0, largest = 0.0;
            void Add(double r)
            {
                sum += r * r;
                largest = Math.Max(largest, Math.Abs(r));
            }

            var gx = new double[m];
            for (int j = 0; j < n; j++)
            {
                double ux = upp[j] - w.X[j], xl = w.X[j] - low[j];
                double pl = p0[j], ql = q0[j];
                for (int i = 0; i < m; i++)
                {
                    pl += w.Lambda[i] * p[i][j];
                    ql += w.Lambda[i] * q[i][j];
                    gx[i] += p[i][j] / ux + q[i][j] / xl;
                }
                Add(pl / (ux * ux) - ql / (xl * xl) - w.Xi[j] + w.Eta[j]);   // (5.9a)
                Add(w.Xi[j] * (w.X[j] - alpha[j]) - epsilon);                  // (5.9e)
                Add(w.Eta[j] * (beta[j] - w.X[j]) - epsilon);                  // (5.9f)
            }
            for (int i = 0; i < m; i++)
            {
                Add(_c[i] + _d[i] * w.Y[i] - w.Lambda[i] - w.Mu[i]);          // (5.9b)
                Add(gx[i] - _a[i] * w.Z - w.Y[i] + w.S[i] - b[i]);            // (5.9d)
                Add(w.Mu[i] * w.Y[i] - epsilon);                              // (5.9g)
                Add(w.Lambda[i] * w.S[i] - epsilon);                          // (5.9i)
            }
            Add(A0 - w.Zeta - Dot(_a, w.Lambda));                             // (5.9c)
            Add(w.Zeta * w.Z - epsilon);                                      // (5.9h)
            return (Math.Sqrt(sum), largest);
        }

        private static double Dot(double[] u, double[] v)
        {
            double sum = 0.0;
            for (int i = 0; i < u.Length; i++)
                sum += u[i] * v[i];
            return sum;
        }

        /// <summary>Gaussian elimination with partial pivoting, for the small system (5.20).</summary>
        private static double[] Solve(double[,] matrix, double[] rhs)
        {
            int size = rhs.Length;
            var A = (double[,])matrix.Clone();
            var x = (double[])rhs.Clone();
            for (int col = 0; col < size; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < size; row++)
                    if (Math.Abs(A[row, col]) > Math.Abs(A[pivot, col]))
                        pivot = row;
                if (pivot != col)
                {
                    for (int k = 0; k < size; k++)
                        (A[col, k], A[pivot, k]) = (A[pivot, k], A[col, k]);
                    (x[col], x[pivot]) = (x[pivot], x[col]);
                }
                for (int row = col + 1; row < size; row++)
                {
                    double factor = A[row, col] / A[col, col];
                    if (factor == 0.0)
                        continue;
                    for (int k = col; k < size; k++)
                        A[row, k] -= factor * A[col, k];
                    x[row] -= factor * x[col];
                }
            }
            for (int row = size - 1; row >= 0; row--)
            {
                double sum = x[row];
                for (int k = row + 1; k < size; k++)
                    sum -= A[row, k] * x[k];
                x[row] = sum / A[row, row];
            }
            return x;
        }

        /// <summary>A point of the subproblem's primal-dual method, or a step from one.</summary>
        private sealed class State
        {
            public readonly double[] X, Xi, Eta, Y, Lambda, Mu, S;
            public double Z, Zeta;

            public State(int n, int m)
            {
                X = new double[n]; Xi = new double[n]; Eta = new double[n];
                Y = new double[m]; Lambda = new double[m]; Mu = new double[m]; S = new double[m];
            }

            public State Plus(State step, double t)
            {
                var result = new State(X.Length, Y.Length);
                for (int j = 0; j < X.Length; j++)
                {
                    result.X[j] = X[j] + t * step.X[j];
                    result.Xi[j] = Xi[j] + t * step.Xi[j];
                    result.Eta[j] = Eta[j] + t * step.Eta[j];
                }
                for (int i = 0; i < Y.Length; i++)
                {
                    result.Y[i] = Y[i] + t * step.Y[i];
                    result.Lambda[i] = Lambda[i] + t * step.Lambda[i];
                    result.Mu[i] = Mu[i] + t * step.Mu[i];
                    result.S[i] = S[i] + t * step.S[i];
                }
                result.Z = Z + t * step.Z;
                result.Zeta = Zeta + t * step.Zeta;
                return result;
            }
        }
    }
}
