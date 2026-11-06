// Ported from NLopt, src/algs/cobyla/cobyla.c.
//
// cobyla : contrained optimization by linear approximation
//
// Copyright (c) 1992, Michael J. D. Powell (M.J.D.Powell@damtp.cam.ac.uk)
// Copyright (c) 2004, Jean-Sebastien Roy (js@jeannot.org)
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
// CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// This software is a C version of COBYLA2, a contrained optimization by linear
// approximation package developed by Michael J. D. Powell in Fortran.
//
// The original source code can be found at :
// http://plato.la.asu.edu/topics/problems/nlores.html
//
// Original RCS id
// static char const rcsid[] =
//  "	@(#) $Jeannot: cobyla.c,v 1.11 2004/04/18 09:51:36 js Exp $";
//
// From the COPYRIGHT file of src/algs/cobyla:
//
// Copyright (c) 1992, Michael J. D. Powell (M.J.D.Powell@damtp.cam.ac.uk)
// Copyright (c) 2004, Jean-Sebastien Roy (js@jeannot.org)
// Copyright (c) 2008, Steven G. Johnson (stevenj@alum.mit.edu)
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
// CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Globalization;

namespace Alpaca4d.Optimisation.NLopt
{
    /// <summary>
    /// NLopt's COBYLA (NLOPT_LN_COBYLA): Powell's constrained optimisation by linear
    /// approximations, as translated to C by Jean-Sébastien Roy and modified by Steven G. Johnson
    /// for NLopt (bounds, rescaling by the initial step, constraint tolerances, stopping criteria).
    ///
    /// The port keeps the C's memory layout: the working space is one array, and every "pointer"
    /// of the C is an index into it, offset the same way the C offsets its pointers so that the
    /// Fortran-style 1-based subscripts read the same. x, lb and ub get their own 1-based copies.
    /// </summary>
    internal static class Cobyla
    {
        // SGJ, 2008: modified COBYLA code to take explicit account of bound
        // constraints.  Since bound constraints are linear, these should
        // already be handled exactly when COBYLA optimizes it linear model.
        // However, they were not handled properly when COBYLA created its
        // initial simplex, or when COBYLA updated unacceptable simplices.
        // Since NLopt guarantees that the objective will not be evaluated
        // outside the bound constraints, this required us to handle such
        // points by putting a slope discontinuity into the objective &
        // constraints (below), which slows convergence considerably for
        // smooth functions.  Instead, handling them explicitly prevents this
        // problem.  (ENFORCE_BOUNDS is 1 in NLopt, so the bounds code is always in.)

        private static double MIN2(double a, double b) => a <= b ? a : b;
        private static double MAX2(double a, double b) => a >= b ? a : b;

        #region NLopt-style interface function

        // SGJ, 2008: NLopt-style interface function
        private sealed class FuncWrapState
        {
            public ObjectiveFunction F;
            public int MOrig;
            public Constraint[] Fc;
            public int P;
            public Constraint[] H;
            public double[] XTmp;
            public double[] Lb, Ub;
            public double[] ConTol, Scale;
            public Stopping Stop;
        }

        /// <summary>
        /// func_wrap: x is x[xo..xo + n), con is con[co..co + m). Returns 1 to end the
        /// minimisation (a forced stop), 0 otherwise.
        /// </summary>
        private static int FuncWrap(int ni, int mi, double[] x, int xo, ref double f, double[] con, int co, FuncWrapState s)
        {
            int n = ni;
            int i, j, k;
            double[] xtmp = s.XTmp;
            double[] lb = s.Lb, ub = s.Ub;

            // in nlopt, we guarante that the function is never evaluated outside
            // the lb and ub bounds, so we need force this with xtmp ... note
            // that this leads to discontinuity in the first derivative, which
            // slows convergence if we don't enable the ENFORCE_BOUNDS feature
            // above.
            for (j = 0; j < n; ++j)
            {
                if (x[xo + j] < lb[j]) xtmp[j] = lb[j];
                else if (x[xo + j] > ub[j]) xtmp[j] = ub[j];
                else xtmp[j] = x[xo + j];
            }
            Rescale.Unscale(n, s.Scale, xtmp, xtmp);

            f = s.F(xtmp);
            if (s.Stop.StopForced()) return 1;
            i = 0;
            for (j = 0; j < s.MOrig; ++j)
            {
                Constraint.Eval(con, co + i, s.Fc[j], n, xtmp);
                if (s.Stop.StopForced()) return 1;
                for (k = 0; k < s.Fc[j].M; ++k)
                    con[co + i + k] = -con[co + i + k];
                i += s.Fc[j].M;
            }
            for (j = 0; j < s.P; ++j)
            {
                Constraint.Eval(con, co + i, s.H[j], n, xtmp);
                if (s.Stop.StopForced()) return 1;
                for (k = 0; k < s.H[j].M; ++k)
                    con[co + (i + s.H[j].M) + k] = -con[co + i + k];
                i += 2 * s.H[j].M;
            }
            for (j = 0; j < n; ++j)
            {
                if (!Stopping.IsInf(lb[j]))
                    con[co + i++] = x[xo + j] - lb[j];
                if (!Stopping.IsInf(ub[j]))
                    con[co + i++] = ub[j] - x[xo + j];
            }
            return 0;
        }

        /// <summary>
        /// cobyla_minimize. m inequality constraints in fc and p equality ones in h, each of
        /// those giving M values. x is the initial guess on input and the minimizer on output;
        /// dx is the initial step.
        /// </summary>
        public static OptimisationResult Minimize(int n, ObjectiveFunction f, int m, Constraint[] fc, int p, Constraint[] h, double[] lb, double[] ub, double[] x, ref double minf, Stopping stop, double[] dx)
        {
            int i, j;
            var s = new FuncWrapState();
            OptimisationResult ret;
            double rhobeg, rhoend;

            s.F = f;
            s.MOrig = m;
            s.Fc = fc;
            s.P = p;
            s.H = h;
            s.Stop = stop;

            s.Scale = Rescale.ComputeRescaling(n, dx);
            for (j = 0; j < n; ++j)
                if (s.Scale[j] == 0 || !Stopping.IsFinite(s.Scale[j]))
                {
                    stop.StopMessage(string.Format(CultureInfo.InvariantCulture,
                        "invalid scaling {0} of dimension {1}: possible over/underflow?", s.Scale[j], j));
                    return OptimisationResult.InvalidArgs;
                }

            s.Lb = Rescale.NewRescaled(n, s.Scale, lb);
            s.Ub = Rescale.NewRescaled(n, s.Scale, ub);
            Rescale.ReorderBounds(n, s.Lb, s.Ub);

            s.XTmp = new double[n];

            // SGJ, 2008: compute rhoend from NLopt stop info
            // (with n == 0 the C reads dx[0] out of bounds; cobyla then returns at once anyway)
            rhobeg = n > 0 ? Math.Abs(dx[0] / s.Scale[0]) : 0.0;
            rhoend = stop.XtolRel * (rhobeg);
            if (stop.XtolAbs != null)
                for (j = 0; j < n; ++j)
                    if (rhoend < stop.XtolAbs[j] / Math.Abs(s.Scale[j]))
                        rhoend = stop.XtolAbs[j] / Math.Abs(s.Scale[j]);

            // each equality constraint gives two inequality constraints
            m = Constraint.Count(m, fc) + 2 * Constraint.Count(p, h);

            // add constraints for lower/upper bounds (if any)
            for (j = 0; j < n; ++j)
            {
                if (!Stopping.IsInf(lb[j]))
                    ++m;
                if (!Stopping.IsInf(ub[j]))
                    ++m;
            }

            s.ConTol = new double[m];

            for (j = 0; j < m; ++j) s.ConTol[j] = 0;
            for (j = i = 0; i < s.MOrig; ++i)
            {
                int ji = j, jnext = j + fc[i].M;
                for (; j < jnext; ++j) s.ConTol[j] = fc[i].Tol[j - ji];
            }
            for (i = 0; i < s.P; ++i)
            {
                int ji = j, jnext = j + h[i].M;
                for (; j < jnext; ++j) s.ConTol[j] = h[i].Tol[j - ji];
                ji = j; jnext = j + h[i].M;
                for (; j < jnext; ++j) s.ConTol[j] = h[i].Tol[j - ji];
            }

            Rescale.Scale(n, s.Scale, x, x);
            ret = CobylaDriver(n, m, x, ref minf, rhobeg, rhoend, stop, s.Lb, s.Ub, 0, s);
            Rescale.Unscale(n, s.Scale, x, x);

            // make sure e.g. rounding errors didn't push us slightly out of bounds
            for (j = 0; j < n; ++j)
            {
                if (x[j] < lb[j]) x[j] = lb[j];
                if (x[j] > ub[j]) x[j] = ub[j];
            }

            return ret;
        }

        #endregion

        #region Pseudorandom simplex steps

        // SGJ, 2010: I find that it seems to increase robustness of the algorithm
        // if, on "simplex" steps (which are intended to improve the linear
        // independence of the simplex, not improve the objective), we multiply
        // the steps by pseudorandom numbers in [0,1).  Intuitively, pseudorandom
        // steps are likely to avoid any accidental dependency in the simplex
        // vertices.  However, since I still want COBYLA to be a deterministic
        // algorithm, and I don't care all that much about the quality of the
        // randomness here, I implement a simple linear congruential generator
        // rather than use nlopt_urand, and set the initial seed deterministically.

        // a simple linear congruential generator
        private static uint LcgRand(ref uint seed)
        {
            return seed = unchecked(seed * 1103515245u + 12345u);
        }

        private static double LcgUrand(ref uint seed, double a, double b)
        {
            return a + (double)LcgRand(ref seed) * (b - a) / (double)uint.MaxValue;
        }

        #endregion

        #region cobyla

        /// <summary>
        /// cobyla: minimize a function subject to constraints.
        ///
        /// n: number of variables (&gt;=0); m: number of constraints (&gt;=0); x: on input, initial
        /// estimate, on output, the solution; minf: on output, minimum objective function;
        /// rhobeg: a reasonable initial change to the variables; stop: the NLopt stopping
        /// criteria; lb, ub: lower and upper bounds on x; iprint: the verbosity (the messages
        /// are not ported); state: used by the function (see FuncWrap).
        ///
        /// COBYLA will try to make all the values of the constraints positive.
        /// </summary>
        private static OptimisationResult CobylaDriver(int n, int m, double[] x0, ref double minf, double rhobeg, double rhoend, Stopping stop, double[] lb0, double[] ub0,
            int iprint, FuncWrapState state)
        {
            int icon, isim, isigb, idatm, iveta, isimi, ivsig, iwork, ia, idx, mpp;
            int[] iact;
            double[] w;
            OptimisationResult rc;

            // This subroutine minimizes an objective function F(X) subject to M
            // inequality constraints on X, where X is a vector of variables that has
            // N components. The algorithm employs linear approximations to the
            // objective and constraint functions, the approximations being formed by
            // linear interpolation at N+1 points in the space of the variables.
            // We regard these interpolation points as vertices of a simplex. The
            // parameter RHO controls the size of the simplex and it is reduced
            // automatically from RHOBEG to RHOEND. For each RHO the subroutine tries
            // to achieve a good vector of variables for the current size, and then
            // RHO is reduced until the value RHOEND is reached. Therefore RHOBEG and
            // RHOEND should be set to reasonable initial changes to and the required
            // accuracy in the variables respectively, but this accuracy should be
            // viewed as a subject for experimentation because it is not guaranteed.
            // The subroutine has an advantage over many of its competitors, however,
            // which is that it treats each constraint individually when calculating
            // a change to the variables, instead of lumping the constraints together
            // into a single penalty function. The name of the subroutine is derived
            // from the phrase Constrained Optimization BY Linear Approximations.
            //
            // The user must set the values of N, M, RHOBEG and RHOEND, and must
            // provide an initial vector of variables in X. Here SIGMA
            // is a penalty parameter, it being assumed that a change to X is an
            // improvement if it reduces the merit function
            //      F(X)+SIGMA*MAX(0.0,-C1(X),-C2(X),...,-CM(X)),
            // where C1,C2,...,CM denote the constraint functions that should become
            // nonnegative eventually, at least to the precision of RHOEND. The term
            // that is multiplied by SIGMA is called MAXCV, which stands for 'MAXimum
            // Constraint Violation'. The arguments W and IACT provide real and
            // int arrays that are used as working space. Their lengths must be at
            // least N*(3*N+2*M+11)+4*M+6 and M+1 respectively.
            //
            // Partition the working space array W to provide the storage that is needed
            // for the main calculation.

            stop.NEvals = 0;

            if (n == 0)
                return OptimisationResult.Success;

            if (n < 0 || m < 0)
                return OptimisationResult.InvalidArgs;

            // workspace allocation, with one more element in front so that, as after the C's
            // "--w" and "--iact", w[1] and iact[1] are the first elements
            w = new double[n * (3 * n + 2 * m + 11) + 4 * m + 6 + 1];
            iact = new int[m + 1 + 1];

            // x, lb and ub, 1-based ("--x; --lb; --ub;")
            var x = new double[n + 1];
            var lb = new double[n + 1];
            var ub = new double[n + 1];
            for (int i = 0; i < n; ++i)
            {
                x[i + 1] = x0[i];
                lb[i + 1] = lb0[i];
                ub[i + 1] = ub0[i];
            }

            // Function Body
            mpp = m + 2;
            icon = 1;
            isim = icon + mpp;
            isimi = isim + n * n + n;
            idatm = isimi + n * n;
            ia = idatm + n * mpp + mpp;
            ivsig = ia + m * n + n;
            iveta = ivsig + n;
            isigb = iveta + n;
            idx = isigb + n;
            iwork = idx + n;
            rc = Cobylb(n, m, mpp, x, ref minf, rhobeg, rhoend, stop, lb, ub, iprint,
                w, icon, isim, isimi, idatm, ia, ivsig, iveta,
                isigb, idx, iwork, iact, state);

            for (int i = 0; i < n; ++i)
                x0[i] = x[i + 1];

            return rc;
        }

        /// <summary>
        /// cobylb. x, lb and ub are 1-based arrays; con, sim, simi, datmat, a, vsig, veta, sigbar,
        /// dx and w are the indices in ws of the C's pointers as cobyla passes them; iact is
        /// 1-based.
        /// </summary>
        private static OptimisationResult Cobylb(int n, int m, int mpp,
            double[] x, ref double minf, double rhobeg, double rhoend,
            Stopping stop, double[] lb, double[] ub,
            int iprint, double[] ws, int con, int sim, int simi,
            int datmat, int a, int vsig, int veta,
            int sigbar, int dx, int w, int[] iact,
            FuncWrapState state)
        {
            // System generated locals
            int sim_dim1, sim_offset, simi_dim1, simi_offset, datmat_dim1,
                datmat_offset, a_dim1, a_offset, i__1, i__2, i__3;
            double d__1, d__2;

            // Local variables
            double alpha, delta, denom, tempa, barmu = 0.0;
            double beta, cmin = 0.0, cmax = 0.0;
            double cvmaxm, dxsign, prerem = 0.0;
            double edgmax, pareta = 0.0, prerec = 0.0, phimin, parsig = 0.0;
            double gamma_;
            double phi, rho, sum = 0.0;
            double ratio, vmold, parmu, error, vmnew;
            double resmax = 0.0, cvmaxp;
            double resnew, trured;
            double temp, wsig, f = 0.0;
            double weta;
            int i__, j, k, l;
            int idxnew;
            int iflag = 0;
            int isdirn, izdota;
            int ivmc;
            int ivmd;
            int mp, np, iz, ibrnch;
            int nbest, ifull = 0, jdrop;
            OptimisationResult rc = OptimisationResult.Success;
            uint seed = (uint)(n + m); // arbitrary deterministic LCG seed
            int feasible;

            // SGJ, 2008: added code to keep track of minimum feasible function val
            minf = double.PositiveInfinity;

            // Set the initial values of some parameters. The last column of SIM holds
            // the optimal vertex of the current simplex, and the preceding N columns
            // hold the displacements from the optimal vertex to the other vertices.
            // Further, SIMI holds the inverse of the matrix that is contained in the
            // first N columns of SIM.

            // Parameter adjustments
            a_dim1 = n;
            a_offset = 1 + a_dim1 * 1;
            a -= a_offset;
            simi_dim1 = n;
            simi_offset = 1 + simi_dim1 * 1;
            simi -= simi_offset;
            sim_dim1 = n;
            sim_offset = 1 + sim_dim1 * 1;
            sim -= sim_offset;
            datmat_dim1 = mpp;
            datmat_offset = 1 + datmat_dim1 * 1;
            datmat -= datmat_offset;
            // x, lb, ub and iact are 1-based already
            --con;
            --vsig;
            --veta;
            --sigbar;
            --dx;
            --w;

            // Function Body
            // (iptem and iptemp only served the printing, which is not ported)
            np = n + 1;
            mp = m + 1;
            alpha = .25;
            beta = 2.1;
            gamma_ = .5;
            delta = 1.1;
            rho = rhobeg;
            parmu = 0.0;
            temp = 1.0 / rho;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                double rhocur;
                ws[sim + i__ + np * sim_dim1] = x[i__];
                i__2 = n;
                for (j = 1; j <= i__2; ++j)
                {
                    ws[sim + i__ + j * sim_dim1] = 0.0;
                    ws[simi + i__ + j * simi_dim1] = 0.0;
                }
                rhocur = rho;
                // SGJ: make sure step rhocur stays inside [lb,ub]
                if (x[i__] + rhocur > ub[i__])
                {
                    if (x[i__] - rhocur >= lb[i__])
                        rhocur = -rhocur;
                    else if (ub[i__] - x[i__] > x[i__] - lb[i__])
                        rhocur = 0.5 * (ub[i__] - x[i__]);
                    else
                        rhocur = 0.5 * (x[i__] - lb[i__]);
                }
                ws[sim + i__ + i__ * sim_dim1] = rhocur;
                ws[simi + i__ + i__ * simi_dim1] = 1.0 / rhocur;
            }
            jdrop = np;
            ibrnch = 0;

        // Make the next call of the user-supplied subroutine CALCFC. These
        // instructions are also used for calling CALCFC during the iterations of
        // the algorithm.

        // SGJ comment: why the hell couldn't he just use a damn subroutine?
        // #*&!%*@ Fortran-66 spaghetti code

        L40:
            if (stop.StopForced()) rc = OptimisationResult.ForcedStop;
            else if (stop.NEvals > 0)
            {
                if (stop.StopEvals()) rc = OptimisationResult.MaxEvalReached;
                else if (stop.StopTime()) rc = OptimisationResult.MaxTimeReached;
            }
            if (rc != OptimisationResult.Success) goto L600;

            stop.NEvals++;
            if (FuncWrap(n, m, x, 1, ref f, ws, con + 1, state) != 0)
            {
                // user requested end of minimization
                rc = OptimisationResult.ForcedStop;
                goto L600;
            }

            resmax = 0.0;
            feasible = 1; // SGJ, 2010
            if (m > 0)
            {
                i__1 = m;
                for (k = 1; k <= i__1; ++k)
                {
                    d__1 = resmax; d__2 = -ws[con + k];
                    resmax = MAX2(d__1, d__2);
                    if (d__2 > state.ConTol[k - 1])
                        feasible = 0; // SGJ, 2010
                }
            }

            // SGJ, 2008: check for minf_max reached by feasible point
            if (f < stop.MinfMax && feasible != 0)
            {
                rc = OptimisationResult.StopValueReached;
                goto L620; // not L600 because we want to use current x, f, resmax
            }

            ws[con + mp] = f;
            ws[con + mpp] = resmax;
            if (ibrnch == 1)
            {
                goto L440;
            }

            // Set the recently calculated function values in a column of DATMAT. This
            // array has a column for each vertex of the current simplex, the entries of
            // each column being the values of the constraint functions (if any)
            // followed by the objective function and the greatest constraint violation
            // at the vertex.

            i__1 = mpp;
            for (k = 1; k <= i__1; ++k)
            {
                ws[datmat + k + jdrop * datmat_dim1] = ws[con + k];
            }
            if (stop.NEvals > np)
            {
                goto L130;
            }

            // Exchange the new vertex of the initial simplex with the optimal vertex if
            // necessary. Then, if the initial simplex is not complete, pick its next
            // vertex and calculate the function values there.

            if (jdrop <= n)
            {
                if (ws[datmat + mp + np * datmat_dim1] <= f)
                {
                    x[jdrop] = ws[sim + jdrop + np * sim_dim1];
                }
                else
                { // improvement in function val
                    double rhocur = x[jdrop] - ws[sim + jdrop + np * sim_dim1];
                    // SGJ: use rhocur instead of rho.  In original code, rhocur == rho
                    //      always, but I want to change this to ensure that simplex points
                    //      fall within [lb,ub].
                    ws[sim + jdrop + np * sim_dim1] = x[jdrop];
                    i__1 = mpp;
                    for (k = 1; k <= i__1; ++k)
                    {
                        ws[datmat + k + jdrop * datmat_dim1] = ws[datmat + k + np * datmat_dim1];
                        ws[datmat + k + np * datmat_dim1] = ws[con + k];
                    }
                    i__1 = jdrop;
                    for (k = 1; k <= i__1; ++k)
                    {
                        ws[sim + jdrop + k * sim_dim1] = -rhocur;
                        temp = 0.0f;
                        i__2 = jdrop;
                        for (i__ = k; i__ <= i__2; ++i__)
                        {
                            temp -= ws[simi + i__ + k * simi_dim1];
                        }
                        ws[simi + jdrop + k * simi_dim1] = temp;
                    }
                }
            }
            if (stop.NEvals <= n)
            { // evaluating initial simplex
                jdrop = stop.NEvals;
                // SGJ: was += rho, but using sim[jdrop,jdrop] enforces consistency
                //      if we change the stepsize above to stay in [lb,ub].
                x[jdrop] += ws[sim + jdrop + jdrop * sim_dim1];
                goto L40;
            }
        L130:
            ibrnch = 1;

        // Identify the optimal vertex of the current simplex.

        L140:
            phimin = ws[datmat + mp + np * datmat_dim1] + parmu * ws[datmat + mpp + np * datmat_dim1];
            nbest = np;
            i__1 = n;
            for (j = 1; j <= i__1; ++j)
            {
                temp = ws[datmat + mp + j * datmat_dim1] + parmu * ws[datmat + mpp + j * datmat_dim1];
                if (temp < phimin)
                {
                    nbest = j;
                    phimin = temp;
                }
                else if (temp == phimin && parmu == 0.0)
                {
                    if (ws[datmat + mpp + j * datmat_dim1] < ws[datmat + mpp + nbest * datmat_dim1])
                    {
                        nbest = j;
                    }
                }
            }

            // Switch the best vertex into pole position if it is not there already,
            // and also update SIM, SIMI and DATMAT.

            if (nbest <= n)
            {
                i__1 = mpp;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    temp = ws[datmat + i__ + np * datmat_dim1];
                    ws[datmat + i__ + np * datmat_dim1] = ws[datmat + i__ + nbest * datmat_dim1];
                    ws[datmat + i__ + nbest * datmat_dim1] = temp;
                }
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    temp = ws[sim + i__ + nbest * sim_dim1];
                    ws[sim + i__ + nbest * sim_dim1] = 0.0;
                    ws[sim + i__ + np * sim_dim1] += temp;
                    tempa = 0.0;
                    i__2 = n;
                    for (k = 1; k <= i__2; ++k)
                    {
                        ws[sim + i__ + k * sim_dim1] -= temp;
                        tempa -= ws[simi + k + i__ * simi_dim1];
                    }
                    ws[simi + nbest + i__ * simi_dim1] = tempa;
                }
            }

            // Make an error return if SIGI is a poor approximation to the inverse of
            // the leading N by N submatrix of SIG.

            error = 0.0;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                i__2 = n;
                for (j = 1; j <= i__2; ++j)
                {
                    temp = 0.0;
                    if (i__ == j)
                    {
                        temp += -1.0;
                    }
                    i__3 = n;
                    for (k = 1; k <= i__3; ++k)
                        if (ws[sim + k + j * sim_dim1] != 0)
                        {
                            temp += ws[simi + i__ + k * simi_dim1] * ws[sim + k + j * sim_dim1];
                        }
                    d__1 = error; d__2 = Math.Abs(temp);
                    error = MAX2(d__1, d__2);
                }
            }
            if (error > .1)
            {
                // rounding errors are becoming damaging
                rc = OptimisationResult.RoundoffLimited;
                goto L600;
            }

            // Calculate the coefficients of the linear approximations to the objective
            // and constraint functions, placing minus the objective function gradient
            // after the constraint gradients in the array A. The vector W is used for
            // working space.

            i__2 = mp;
            for (k = 1; k <= i__2; ++k)
            {
                ws[con + k] = -ws[datmat + k + np * datmat_dim1];
                i__1 = n;
                for (j = 1; j <= i__1; ++j)
                {
                    ws[w + j] = ws[datmat + k + j * datmat_dim1] + ws[con + k];
                }
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    temp = 0.0;
                    i__3 = n;
                    for (j = 1; j <= i__3; ++j)
                    {
                        temp += ws[w + j] * ws[simi + j + i__ * simi_dim1];
                    }
                    if (k == mp)
                    {
                        temp = -temp;
                    }
                    ws[a + i__ + k * a_dim1] = temp;
                }
            }

            // Calculate the values of sigma and eta, and set IFLAG=0 if the current
            // simplex is not acceptable.

            iflag = 1;
            parsig = alpha * rho;
            pareta = beta * rho;
            i__1 = n;
            for (j = 1; j <= i__1; ++j)
            {
                wsig = 0.0;
                weta = 0.0;
                i__2 = n;
                for (i__ = 1; i__ <= i__2; ++i__)
                {
                    d__1 = ws[simi + j + i__ * simi_dim1];
                    wsig += d__1 * d__1;
                    d__1 = ws[sim + i__ + j * sim_dim1];
                    weta += d__1 * d__1;
                }
                ws[vsig + j] = 1.0 / Math.Sqrt(wsig);
                ws[veta + j] = Math.Sqrt(weta);
                if (ws[vsig + j] < parsig || ws[veta + j] > pareta)
                {
                    iflag = 0;
                }
            }

            // If a new vertex is needed to improve acceptability, then decide which
            // vertex to drop from the simplex.

            if (ibrnch == 1 || iflag == 1)
            {
                goto L370;
            }
            jdrop = 0;
            temp = pareta;
            i__1 = n;
            for (j = 1; j <= i__1; ++j)
            {
                if (ws[veta + j] > temp)
                {
                    jdrop = j;
                    temp = ws[veta + j];
                }
            }
            if (jdrop == 0)
            {
                i__1 = n;
                for (j = 1; j <= i__1; ++j)
                {
                    if (ws[vsig + j] < temp)
                    {
                        jdrop = j;
                        temp = ws[vsig + j];
                    }
                }
            }

            // Calculate the step to the new vertex and its sign.

            temp = gamma_ * rho * ws[vsig + jdrop];
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[dx + i__] = temp * ws[simi + jdrop + i__ * simi_dim1];
            }
            cvmaxp = 0.0;
            cvmaxm = 0.0;
            i__1 = mp;
            for (k = 1; k <= i__1; ++k)
            {
                sum = 0.0;
                i__2 = n;
                for (i__ = 1; i__ <= i__2; ++i__)
                {
                    sum += ws[a + i__ + k * a_dim1] * ws[dx + i__];
                }
                if (k < mp)
                {
                    temp = ws[datmat + k + np * datmat_dim1];
                    d__1 = cvmaxp; d__2 = -sum - temp;
                    cvmaxp = MAX2(d__1, d__2);
                    d__1 = cvmaxm; d__2 = sum - temp;
                    cvmaxm = MAX2(d__1, d__2);
                }
            }
            dxsign = 1.0;
            if (parmu * (cvmaxp - cvmaxm) > sum + sum)
            {
                dxsign = -1.0;
            }

            // Update the elements of SIM and SIMI, and set the next X.

            temp = 0.0;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                // SGJ, 2010: pseudo-randomize simplex steps (see LCG comments above)
                ws[dx + i__] = dxsign * ws[dx + i__] * LcgUrand(ref seed, 0.01, 1);
                // SGJ: make sure dx step says in [lb,ub]
                {
                    double xi = ws[sim + i__ + np * sim_dim1];
                fixdx:
                    if (xi + ws[dx + i__] > ub[i__])
                        ws[dx + i__] = -ws[dx + i__];
                    if (xi + ws[dx + i__] < lb[i__])
                    {
                        if (xi - ws[dx + i__] <= ub[i__])
                            ws[dx + i__] = -ws[dx + i__];
                        else
                        { // try again with halved step
                            ws[dx + i__] *= 0.5;
                            goto fixdx;
                        }
                    }
                }
                ws[sim + i__ + jdrop * sim_dim1] = ws[dx + i__];
                temp += ws[simi + jdrop + i__ * simi_dim1] * ws[dx + i__];
            }
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[simi + jdrop + i__ * simi_dim1] /= temp;
            }
            i__1 = n;
            for (j = 1; j <= i__1; ++j)
            {
                if (j != jdrop)
                {
                    temp = 0.0;
                    i__2 = n;
                    for (i__ = 1; i__ <= i__2; ++i__)
                    {
                        temp += ws[simi + j + i__ * simi_dim1] * ws[dx + i__];
                    }
                    i__2 = n;
                    for (i__ = 1; i__ <= i__2; ++i__)
                    {
                        ws[simi + j + i__ * simi_dim1] -= temp * ws[simi + jdrop + i__ * simi_dim1];
                    }
                }
                x[j] = ws[sim + j + np * sim_dim1] + ws[dx + j];
            }
            goto L40;

        // Calculate DX=x(*)-x(0). Branch if the length of DX is less than 0.5*RHO.

        L370:
            iz = 1;
            izdota = iz + n * n;
            ivmc = izdota + n;
            isdirn = ivmc + mp;
            idxnew = isdirn + n;
            ivmd = idxnew + n;
            rc = Trstlp(n, m, ws, a + a_offset, con + 1, rho, dx + 1, ref ifull, iact, w + iz,
                w + izdota, w + ivmc, w + isdirn, w + idxnew, w + ivmd);
            if (rc != OptimisationResult.Success) goto L600;
            // SGJ: since the bound constraints are linear, we should never get
            // a dx that lies outside the [lb,ub] constraints here, but we'll
            // enforce this anyway just to be paranoid
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                double xi = ws[sim + i__ + np * sim_dim1];
                if (xi + ws[dx + i__] > ub[i__]) ws[dx + i__] = ub[i__] - xi;
                if (xi + ws[dx + i__] < lb[i__]) ws[dx + i__] = xi - lb[i__];
            }
            if (ifull == 0)
            {
                temp = 0.0;
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    d__1 = ws[dx + i__];
                    temp += d__1 * d__1;
                }
                if (temp < rho * .25 * rho)
                {
                    ibrnch = 1;
                    goto L550;
                }
            }

            // Predict the change to F and the new maximum constraint violation if the
            // variables are altered from x(0) to x(0)+DX.

            resnew = 0.0;
            ws[con + mp] = 0.0;
            i__1 = mp;
            for (k = 1; k <= i__1; ++k)
            {
                sum = ws[con + k];
                i__2 = n;
                for (i__ = 1; i__ <= i__2; ++i__)
                {
                    sum -= ws[a + i__ + k * a_dim1] * ws[dx + i__];
                }
                if (k < mp)
                {
                    resnew = MAX2(resnew, sum);
                }
            }

            // Increase PARMU if necessary and branch back if this change alters the
            // optimal vertex. Otherwise PREREM and PREREC will be set to the predicted
            // reductions in the merit function and the maximum constraint violation
            // respectively.

            barmu = 0.0;
            prerec = ws[datmat + mpp + np * datmat_dim1] - resnew;
            if (prerec > 0.0)
            {
                barmu = sum / prerec;
            }
            if (parmu < barmu * 1.5)
            {
                parmu = barmu * 2.0;
                phi = ws[datmat + mp + np * datmat_dim1] + parmu * ws[datmat + mpp + np * datmat_dim1];
                i__1 = n;
                for (j = 1; j <= i__1; ++j)
                {
                    temp = ws[datmat + mp + j * datmat_dim1] + parmu * ws[datmat + mpp + j * datmat_dim1];
                    if (temp < phi)
                    {
                        goto L140;
                    }
                    if (temp == phi && parmu == 0.0f)
                    {
                        if (ws[datmat + mpp + j * datmat_dim1] < ws[datmat + mpp + np * datmat_dim1])
                        {
                            goto L140;
                        }
                    }
                }
            }
            prerem = parmu * prerec - sum;

            // Calculate the constraint and objective functions at x(*). Then find the
            // actual reduction in the merit function.

            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                x[i__] = ws[sim + i__ + np * sim_dim1] + ws[dx + i__];
            }
            ibrnch = 1;
            goto L40;
        L440:
            vmold = ws[datmat + mp + np * datmat_dim1] + parmu * ws[datmat + mpp + np * datmat_dim1];
            vmnew = f + parmu * resmax;
            trured = vmold - vmnew;
            if (parmu == 0.0 && f == ws[datmat + mp + np * datmat_dim1])
            {
                prerem = prerec;
                trured = ws[datmat + mpp + np * datmat_dim1] - resmax;
            }

            // Begin the operations that decide whether x(*) should replace one of the
            // vertices of the current simplex, the change being mandatory if TRURED is
            // positive. Firstly, JDROP is set to the index of the vertex that is to be
            // replaced.

            ratio = 0.0;
            if (trured <= 0.0f)
            {
                ratio = 1.0f;
            }
            jdrop = 0;
            i__1 = n;
            for (j = 1; j <= i__1; ++j)
            {
                temp = 0.0;
                i__2 = n;
                for (i__ = 1; i__ <= i__2; ++i__)
                {
                    temp += ws[simi + j + i__ * simi_dim1] * ws[dx + i__];
                }
                temp = Math.Abs(temp);
                if (temp > ratio)
                {
                    jdrop = j;
                    ratio = temp;
                }
                ws[sigbar + j] = temp * ws[vsig + j];
            }

            // Calculate the value of ell.

            edgmax = delta * rho;
            l = 0;
            i__1 = n;
            for (j = 1; j <= i__1; ++j)
            {
                if (ws[sigbar + j] >= parsig || ws[sigbar + j] >= ws[vsig + j])
                {
                    temp = ws[veta + j];
                    if (trured > 0.0)
                    {
                        temp = 0.0;
                        i__2 = n;
                        for (i__ = 1; i__ <= i__2; ++i__)
                        {
                            d__1 = ws[dx + i__] - ws[sim + i__ + j * sim_dim1];
                            temp += d__1 * d__1;
                        }
                        temp = Math.Sqrt(temp);
                    }
                    if (temp > edgmax)
                    {
                        l = j;
                        edgmax = temp;
                    }
                }
            }
            if (l > 0)
            {
                jdrop = l;
            }
            if (jdrop == 0)
            {
                goto L550;
            }

            // Revise the simplex by updating the elements of SIM, SIMI and DATMAT.

            temp = 0.0;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[sim + i__ + jdrop * sim_dim1] = ws[dx + i__];
                temp += ws[simi + jdrop + i__ * simi_dim1] * ws[dx + i__];
            }
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[simi + jdrop + i__ * simi_dim1] /= temp;
            }
            i__1 = n;
            for (j = 1; j <= i__1; ++j)
            {
                if (j != jdrop)
                {
                    temp = 0.0;
                    i__2 = n;
                    for (i__ = 1; i__ <= i__2; ++i__)
                    {
                        temp += ws[simi + j + i__ * simi_dim1] * ws[dx + i__];
                    }
                    i__2 = n;
                    for (i__ = 1; i__ <= i__2; ++i__)
                    {
                        ws[simi + j + i__ * simi_dim1] -= temp * ws[simi + jdrop + i__ * simi_dim1];
                    }
                }
            }
            i__1 = mpp;
            for (k = 1; k <= i__1; ++k)
            {
                ws[datmat + k + jdrop * datmat_dim1] = ws[con + k];
            }

            // Branch back for further iterations with the current RHO.

            if (trured > 0.0 && trured >= prerem * .1)
            {
                // SGJ, 2010: following a suggestion in the SAS manual (which
                // mentions a similar modification to COBYLA, although they didn't
                // publish their source code), increase rho if predicted reduction
                // is sufficiently close to the actual reduction.  Otherwise,
                // COBLYA seems to easily get stuck making very small steps.
                // Also require iflag != 0 (i.e., acceptable simplex), again
                // following SAS suggestion (otherwise I get convergence failure
                // in some cases.)
                if (trured >= prerem * 0.9 && trured <= prerem * 1.1 && iflag != 0)
                {
                    rho *= 2.0;
                }
                goto L140;
            }
        L550:
            if (iflag == 0)
            {
                ibrnch = 0;
                goto L140;
            }

            // SGJ, 2008: convergence tests for function vals; note that current
            // best val is stored in datmat[mp + np * datmat_dim1], or in f if
            // ifull == 1, and previous best is in *minf.  This seems like a
            // sensible place to put the convergence test, as it is the same
            // place that Powell checks the x tolerance (rho > rhoend).
            {
                double fbest = ifull == 1 ? f : ws[datmat + mp + np * datmat_dim1];
                if (fbest < minf && stop.StopFtol(fbest, minf))
                {
                    rc = OptimisationResult.FtolReached;
                    goto L600;
                }
                minf = fbest;
            }

            // Otherwise reduce RHO if it is not at its least value and reset PARMU.

            if (rho > rhoend)
            {
                rho *= .5;
                if (rho <= rhoend * 1.5)
                {
                    rho = rhoend;
                }
                if (parmu > 0.0)
                {
                    denom = 0.0;
                    i__1 = mp;
                    for (k = 1; k <= i__1; ++k)
                    {
                        cmin = ws[datmat + k + np * datmat_dim1];
                        cmax = cmin;
                        i__2 = n;
                        for (i__ = 1; i__ <= i__2; ++i__)
                        {
                            d__1 = cmin; d__2 = ws[datmat + k + i__ * datmat_dim1];
                            cmin = MIN2(d__1, d__2);
                            d__1 = cmax; d__2 = ws[datmat + k + i__ * datmat_dim1];
                            cmax = MAX2(d__1, d__2);
                        }
                        if (k <= m && cmin < cmax * .5)
                        {
                            temp = MAX2(cmax, 0.0) - cmin;
                            if (denom <= 0.0)
                            {
                                denom = temp;
                            }
                            else
                            {
                                denom = MIN2(denom, temp);
                            }
                        }
                    }
                    if (denom == 0.0)
                    {
                        parmu = 0.0;
                    }
                    else if (cmax - cmin < parmu * denom)
                    {
                        parmu = (cmax - cmin) / denom;
                    }
                }
                goto L140;
            }
            else // rho <= rhoend
                rc = rhoend > 0 ? OptimisationResult.XtolReached : OptimisationResult.RoundoffLimited;

            // Return the best calculated values of the variables.

            if (ifull == 1)
            {
                goto L620;
            }
        L600:
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                x[i__] = ws[sim + i__ + np * sim_dim1];
            }
            f = ws[datmat + mp + np * datmat_dim1];
            resmax = ws[datmat + mpp + np * datmat_dim1];
        L620:
            minf = f;
            return rc;
        }

        /// <summary>
        /// trstlp. a, b, dx, z__, zdota, vmultc, sdirn, dxnew and vmultd are the indices in ws of
        /// the C's pointers as cobylb passes them; iact is 1-based.
        /// </summary>
        private static OptimisationResult Trstlp(int n, int m, double[] ws, int a,
            int b, double rho, int dx, ref int ifull,
            int[] iact, int z__, int zdota, int vmultc,
            int sdirn, int dxnew, int vmultd)
        {
            // System generated locals
            int a_dim1, a_offset, z_dim1, z_offset, i__1, i__2;
            double d__1, d__2;

            // Local variables
            double alpha, tempa;
            double beta;
            double optnew, stpful, sum, tot, acca, accb;
            double ratio, vsave, zdotv, zdotw, dd;
            double sd;
            double sp, ss, resold = 0.0, zdvabs, zdwabs, sumabs, resmax, optold = 0.0;
            double spabs;
            double temp, step;
            int icount = 0;
            int i__, j, k;
            int isave;
            int kk = 0;
            int kl, kp, kw;
            int nact, icon = 0, mcon;
            int nactx = 0;

            // This subroutine calculates an N-component vector DX by applying the
            // following two stages. In the first stage, DX is set to the shortest
            // vector that minimizes the greatest violation of the constraints
            //   A(1,K)*DX(1)+A(2,K)*DX(2)+...+A(N,K)*DX(N) .GE. B(K), K=2,3,...,M,
            // subject to the Euclidean length of DX being at most RHO. If its length is
            // strictly less than RHO, then we use the resultant freedom in DX to
            // minimize the objective function
            //      -A(1,M+1)*DX(1)-A(2,M+1)*DX(2)-...-A(N,M+1)*DX(N)
            // subject to no increase in any greatest constraint violation. This
            // notation allows the gradient of the objective function to be regarded as
            // the gradient of a constraint. Therefore the two stages are distinguished
            // by MCON .EQ. M and MCON .GT. M respectively. It is possible that a
            // degeneracy may prevent DX from attaining the target length RHO. Then the
            // value IFULL=0 would be set, but usually IFULL=1 on return.
            //
            // In general NACT is the number of constraints in the active set and
            // IACT(1),...,IACT(NACT) are their indices, while the remainder of IACT
            // contains a permutation of the remaining constraint indices. Further, Z is
            // an orthogonal matrix whose first NACT columns can be regarded as the
            // result of Gram-Schmidt applied to the active constraint gradients. For
            // J=1,2,...,NACT, the number ZDOTA(J) is the scalar product of the J-th
            // column of Z with the gradient of the J-th active constraint. DX is the
            // current vector of variables and here the residuals of the active
            // constraints should be zero. Further, the active constraints have
            // nonnegative Lagrange multipliers that are held at the beginning of
            // VMULTC. The remainder of this vector holds the residuals of the inactive
            // constraints at DX, the ordering of the components of VMULTC being in
            // agreement with the permutation of the indices of the constraints that is
            // in IACT. All these residuals are nonnegative, which is achieved by the
            // shift RESMAX that makes the least residual zero.
            //
            // Initialize Z and some other variables. The value of RESMAX will be
            // appropriate to DX=0, while ICON will be the index of a most violated
            // constraint if RESMAX is positive. Usually during the first stage the
            // vector SDIRN gives a search direction that reduces all the active
            // constraint violations by one simultaneously.

            // Parameter adjustments
            z_dim1 = n;
            z_offset = 1 + z_dim1 * 1;
            z__ -= z_offset;
            a_dim1 = n;
            a_offset = 1 + a_dim1 * 1;
            a -= a_offset;
            --b;
            --dx;
            // iact is 1-based already
            --zdota;
            --vmultc;
            --sdirn;
            --dxnew;
            --vmultd;

            // Function Body
            ifull = 1;
            mcon = m;
            nact = 0;
            resmax = 0.0;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                i__2 = n;
                for (j = 1; j <= i__2; ++j)
                {
                    ws[z__ + i__ + j * z_dim1] = 0.0;
                }
                ws[z__ + i__ + i__ * z_dim1] = 1.0;
                ws[dx + i__] = 0.0;
            }
            if (m >= 1)
            {
                i__1 = m;
                for (k = 1; k <= i__1; ++k)
                {
                    if (ws[b + k] > resmax)
                    {
                        resmax = ws[b + k];
                        icon = k;
                    }
                }
                i__1 = m;
                for (k = 1; k <= i__1; ++k)
                {
                    iact[k] = k;
                    ws[vmultc + k] = resmax - ws[b + k];
                }
            }
            if (resmax == 0.0)
            {
                goto L480;
            }
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[sdirn + i__] = 0.0;
            }

        // End the current stage of the calculation if 3 consecutive iterations
        // have either failed to reduce the best calculated value of the objective
        // function or to increase the number of active constraints since the best
        // value was calculated. This strategy prevents cycling, but there is a
        // remote possibility that it will cause premature termination.

        L60:
            optold = 0.0;
            icount = 0;
        L70:
            if (mcon == m)
            {
                optnew = resmax;
            }
            else
            {
                optnew = 0.0;
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    optnew -= ws[dx + i__] * ws[a + i__ + mcon * a_dim1];
                }
            }
            if (icount == 0 || optnew < optold)
            {
                optold = optnew;
                nactx = nact;
                icount = 3;
            }
            else if (nact > nactx)
            {
                nactx = nact;
                icount = 3;
            }
            else
            {
                --icount;
                if (icount == 0)
                {
                    goto L490;
                }
            }

            // If ICON exceeds NACT, then we add the constraint with index IACT(ICON) to
            // the active set. Apply Givens rotations so that the last N-NACT-1 columns
            // of Z are orthogonal to the gradient of the new constraint, a scalar
            // product being set to zero if its nonzero value could be due to computer
            // rounding errors. The array DXNEW is used for working space.

            if (icon <= nact)
            {
                goto L260;
            }
            kk = iact[icon];
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[dxnew + i__] = ws[a + i__ + kk * a_dim1];
            }
            tot = 0.0;
            k = n;
        L100:
            if (k > nact)
            {
                sp = 0.0;
                spabs = 0.0;
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    temp = ws[z__ + i__ + k * z_dim1] * ws[dxnew + i__];
                    sp += temp;
                    spabs += Math.Abs(temp);
                }
                acca = spabs + Math.Abs(sp) * .1;
                accb = spabs + Math.Abs(sp) * .2;
                if (spabs >= acca || acca >= accb)
                {
                    sp = 0.0;
                }
                if (tot == 0.0)
                {
                    tot = sp;
                }
                else
                {
                    kp = k + 1;
                    temp = Math.Sqrt(sp * sp + tot * tot);
                    alpha = sp / temp;
                    beta = tot / temp;
                    tot = temp;
                    i__1 = n;
                    for (i__ = 1; i__ <= i__1; ++i__)
                    {
                        temp = alpha * ws[z__ + i__ + k * z_dim1] + beta * ws[z__ + i__ + kp * z_dim1];
                        ws[z__ + i__ + kp * z_dim1] = alpha * ws[z__ + i__ + kp * z_dim1] -
                            beta * ws[z__ + i__ + k * z_dim1];
                        ws[z__ + i__ + k * z_dim1] = temp;
                    }
                }
                --k;
                goto L100;
            }

            // Add the new constraint if this can be done without a deletion from the
            // active set.

            if (tot != 0.0)
            {
                ++nact;
                ws[zdota + nact] = tot;
                ws[vmultc + icon] = ws[vmultc + nact];
                ws[vmultc + nact] = 0.0;
                goto L210;
            }

            // The next instruction is reached if a deletion has to be made from the
            // active set in order to make room for the new active constraint, because
            // the new constraint gradient is a linear combination of the gradients of
            // the old active constraints. Set the elements of VMULTD to the multipliers
            // of the linear combination. Further, set IOUT to the index of the
            // constraint to be deleted, but branch if no suitable index can be found.

            ratio = -1.0;
            k = nact;
        L130:
            zdotv = 0.0;
            zdvabs = 0.0;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                temp = ws[z__ + i__ + k * z_dim1] * ws[dxnew + i__];
                zdotv += temp;
                zdvabs += Math.Abs(temp);
            }
            acca = zdvabs + Math.Abs(zdotv) * .1;
            accb = zdvabs + Math.Abs(zdotv) * .2;
            if (zdvabs < acca && acca < accb)
            {
                temp = zdotv / ws[zdota + k];
                if (temp > 0.0 && iact[k] <= m)
                {
                    tempa = ws[vmultc + k] / temp;
                    if (ratio < 0.0 || tempa < ratio)
                        ratio = tempa;
                }
                if (k >= 2)
                {
                    kw = iact[k];
                    i__1 = n;
                    for (i__ = 1; i__ <= i__1; ++i__)
                    {
                        ws[dxnew + i__] -= temp * ws[a + i__ + kw * a_dim1];
                    }
                }
                ws[vmultd + k] = temp;
            }
            else
            {
                ws[vmultd + k] = 0.0;
            }
            --k;
            if (k > 0)
            {
                goto L130;
            }
            if (ratio < 0.0)
            {
                goto L490;
            }

            // Revise the Lagrange multipliers and reorder the active constraints so
            // that the one to be replaced is at the end of the list. Also calculate the
            // new value of ZDOTA(NACT) and branch if it is not acceptable.

            i__1 = nact;
            for (k = 1; k <= i__1; ++k)
            {
                d__1 = 0.0; d__2 = ws[vmultc + k] - ratio * ws[vmultd + k];
                ws[vmultc + k] = MAX2(d__1, d__2);
            }
            if (icon < nact)
            {
                isave = iact[icon];
                vsave = ws[vmultc + icon];
                k = icon;
            L170:
                kp = k + 1;
                kw = iact[kp];
                sp = 0.0;
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    sp += ws[z__ + i__ + k * z_dim1] * ws[a + i__ + kw * a_dim1];
                }
                d__1 = ws[zdota + kp];
                temp = Math.Sqrt(sp * sp + d__1 * d__1);
                alpha = ws[zdota + kp] / temp;
                beta = sp / temp;
                ws[zdota + kp] = alpha * ws[zdota + k];
                ws[zdota + k] = temp;
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    temp = alpha * ws[z__ + i__ + kp * z_dim1] + beta * ws[z__ + i__ + k * z_dim1];
                    ws[z__ + i__ + kp * z_dim1] = alpha * ws[z__ + i__ + k * z_dim1] - beta *
                        ws[z__ + i__ + kp * z_dim1];
                    ws[z__ + i__ + k * z_dim1] = temp;
                }
                iact[k] = kw;
                ws[vmultc + k] = ws[vmultc + kp];
                k = kp;
                if (k < nact)
                {
                    goto L170;
                }
                iact[k] = isave;
                ws[vmultc + k] = vsave;
            }
            temp = 0.0;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                temp += ws[z__ + i__ + nact * z_dim1] * ws[a + i__ + kk * a_dim1];
            }
            if (temp == 0.0)
            {
                goto L490;
            }
            ws[zdota + nact] = temp;
            ws[vmultc + icon] = 0.0;
            ws[vmultc + nact] = ratio;

        // Update IACT and ensure that the objective function continues to be
        // treated as the last active constraint when MCON>M.

        L210:
            iact[icon] = iact[nact];
            iact[nact] = kk;
            if (mcon > m && kk != mcon)
            {
                k = nact - 1;
                sp = 0.0;
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    sp += ws[z__ + i__ + k * z_dim1] * ws[a + i__ + kk * a_dim1];
                }
                d__1 = ws[zdota + nact];
                temp = Math.Sqrt(sp * sp + d__1 * d__1);
                alpha = ws[zdota + nact] / temp;
                beta = sp / temp;
                ws[zdota + nact] = alpha * ws[zdota + k];
                ws[zdota + k] = temp;
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    temp = alpha * ws[z__ + i__ + nact * z_dim1] + beta * ws[z__ + i__ + k * z_dim1];
                    ws[z__ + i__ + nact * z_dim1] = alpha * ws[z__ + i__ + k * z_dim1] - beta *
                        ws[z__ + i__ + nact * z_dim1];
                    ws[z__ + i__ + k * z_dim1] = temp;
                }
                iact[nact] = iact[k];
                iact[k] = kk;
                temp = ws[vmultc + k];
                ws[vmultc + k] = ws[vmultc + nact];
                ws[vmultc + nact] = temp;
            }

            // If stage one is in progress, then set SDIRN to the direction of the next
            // change to the current vector of variables.

            if (mcon > m)
            {
                goto L320;
            }
            kk = iact[nact];
            temp = 0.0;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                temp += ws[sdirn + i__] * ws[a + i__ + kk * a_dim1];
            }
            temp += -1.0;
            temp /= ws[zdota + nact];
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[sdirn + i__] -= temp * ws[z__ + i__ + nact * z_dim1];
            }
            goto L340;

        // Delete the constraint that has the index IACT(ICON) from the active set.

        L260:
            if (icon < nact)
            {
                isave = iact[icon];
                vsave = ws[vmultc + icon];
                k = icon;
            L270:
                kp = k + 1;
                kk = iact[kp];
                sp = 0.0;
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    sp += ws[z__ + i__ + k * z_dim1] * ws[a + i__ + kk * a_dim1];
                }
                d__1 = ws[zdota + kp];
                temp = Math.Sqrt(sp * sp + d__1 * d__1);
                alpha = ws[zdota + kp] / temp;
                beta = sp / temp;
                ws[zdota + kp] = alpha * ws[zdota + k];
                ws[zdota + k] = temp;
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    temp = alpha * ws[z__ + i__ + kp * z_dim1] + beta * ws[z__ + i__ + k * z_dim1];
                    ws[z__ + i__ + kp * z_dim1] = alpha * ws[z__ + i__ + k * z_dim1] - beta *
                        ws[z__ + i__ + kp * z_dim1];
                    ws[z__ + i__ + k * z_dim1] = temp;
                }
                iact[k] = kk;
                ws[vmultc + k] = ws[vmultc + kp];
                k = kp;
                if (k < nact)
                {
                    goto L270;
                }
                iact[k] = isave;
                ws[vmultc + k] = vsave;
            }
            --nact;

            // If stage one is in progress, then set SDIRN to the direction of the next
            // change to the current vector of variables.

            if (mcon > m)
            {
                goto L320;
            }
            temp = 0.0;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                temp += ws[sdirn + i__] * ws[z__ + i__ + (nact + 1) * z_dim1];
            }
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[sdirn + i__] -= temp * ws[z__ + i__ + (nact + 1) * z_dim1];
            }
            goto L340;

        // Pick the next search direction of stage two.

        L320:
            temp = 1.0 / ws[zdota + nact];
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[sdirn + i__] = temp * ws[z__ + i__ + nact * z_dim1];
            }

        // Calculate the step to the boundary of the trust region or take the step
        // that reduces RESMAX to zero. The two statements below that include the
        // factor 1.0E-6 prevent some harmless underflows that occurred in a test
        // calculation. Further, we skip the step if it could be zero within a
        // reasonable tolerance for computer rounding errors.

        L340:
            dd = rho * rho;
            sd = 0.0;
            ss = 0.0;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                // 1e-6f is a float in the C, so is 1e-6 rounded to single precision
                d__1 = ws[dx + i__];
                if (Math.Abs(d__1) >= rho * 1e-6f)
                {
                    d__2 = ws[dx + i__];
                    dd -= d__2 * d__2;
                }
                sd += ws[dx + i__] * ws[sdirn + i__];
                d__1 = ws[sdirn + i__];
                ss += d__1 * d__1;
            }
            if (dd <= 0.0)
            {
                goto L490;
            }
            temp = Math.Sqrt(ss * dd);
            if (Math.Abs(sd) >= temp * 1e-6f)
            {
                temp = Math.Sqrt(ss * dd + sd * sd);
            }
            stpful = dd / (temp + sd);
            step = stpful;
            if (mcon == m)
            {
                acca = step + resmax * .1;
                accb = step + resmax * .2;
                if (step >= acca || acca >= accb)
                {
                    goto L480;
                }
                step = MIN2(step, resmax);
            }

            // SGJ, 2010: check for error here
            if (Stopping.IsInf(step)) return OptimisationResult.RoundoffLimited;

            // Set DXNEW to the new variables if STEP is the steplength, and reduce
            // RESMAX to the corresponding maximum residual if stage one is being done.
            // Because DXNEW will be changed during the calculation of some Lagrange
            // multipliers, it will be restored to the following value later.

            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[dxnew + i__] = ws[dx + i__] + step * ws[sdirn + i__];
            }
            if (mcon == m)
            {
                resold = resmax;
                resmax = 0.0;
                i__1 = nact;
                for (k = 1; k <= i__1; ++k)
                {
                    kk = iact[k];
                    temp = ws[b + kk];
                    i__2 = n;
                    for (i__ = 1; i__ <= i__2; ++i__)
                    {
                        temp -= ws[a + i__ + kk * a_dim1] * ws[dxnew + i__];
                    }
                    resmax = MAX2(resmax, temp);
                }
            }

            // Set VMULTD to the VMULTC vector that would occur if DX became DXNEW. A
            // device is included to force VMULTD(K)=0.0 if deviations from this value
            // can be attributed to computer rounding errors. First calculate the new
            // Lagrange multipliers.

            k = nact;
        L390:
            zdotw = 0.0;
            zdwabs = 0.0;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                temp = ws[z__ + i__ + k * z_dim1] * ws[dxnew + i__];
                zdotw += temp;
                zdwabs += Math.Abs(temp);
            }
            acca = zdwabs + Math.Abs(zdotw) * .1;
            accb = zdwabs + Math.Abs(zdotw) * .2;
            if (zdwabs >= acca || acca >= accb)
            {
                zdotw = 0.0;
            }
            ws[vmultd + k] = zdotw / ws[zdota + k];
            if (k >= 2)
            {
                kk = iact[k];
                i__1 = n;
                for (i__ = 1; i__ <= i__1; ++i__)
                {
                    ws[dxnew + i__] -= ws[vmultd + k] * ws[a + i__ + kk * a_dim1];
                }
                --k;
                goto L390;
            }
            if (mcon > m)
            {
                d__1 = 0.0; d__2 = ws[vmultd + nact];
                ws[vmultd + nact] = MAX2(d__1, d__2);
            }

            // Complete VMULTC by finding the new constraint residuals.

            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[dxnew + i__] = ws[dx + i__] + step * ws[sdirn + i__];
            }
            if (mcon > nact)
            {
                kl = nact + 1;
                i__1 = mcon;
                for (k = kl; k <= i__1; ++k)
                {
                    kk = iact[k];
                    sum = resmax - ws[b + kk];
                    d__1 = ws[b + kk];
                    sumabs = resmax + Math.Abs(d__1);
                    i__2 = n;
                    for (i__ = 1; i__ <= i__2; ++i__)
                    {
                        temp = ws[a + i__ + kk * a_dim1] * ws[dxnew + i__];
                        sum += temp;
                        sumabs += Math.Abs(temp);
                    }
                    // .1f and .2f are floats in the C
                    acca = sumabs + Math.Abs(sum) * .1f;
                    accb = sumabs + Math.Abs(sum) * .2f;
                    if (sumabs >= acca || acca >= accb)
                    {
                        sum = 0.0f;
                    }
                    ws[vmultd + k] = sum;
                }
            }

            // Calculate the fraction of the step from DX to DXNEW that will be taken.

            ratio = 1.0;
            icon = 0;
            i__1 = mcon;
            for (k = 1; k <= i__1; ++k)
            {
                if (ws[vmultd + k] < 0.0)
                {
                    temp = ws[vmultc + k] / (ws[vmultc + k] - ws[vmultd + k]);
                    if (temp < ratio)
                    {
                        ratio = temp;
                        icon = k;
                    }
                }
            }

            // Update DX, VMULTC and RESMAX.

            temp = 1.0 - ratio;
            i__1 = n;
            for (i__ = 1; i__ <= i__1; ++i__)
            {
                ws[dx + i__] = temp * ws[dx + i__] + ratio * ws[dxnew + i__];
            }
            i__1 = mcon;
            for (k = 1; k <= i__1; ++k)
            {
                d__1 = 0.0; d__2 = temp * ws[vmultc + k] + ratio * ws[vmultd + k];
                ws[vmultc + k] = MAX2(d__1, d__2);
            }
            if (mcon == m)
            {
                resmax = resold + ratio * (resmax - resold);
            }

            // If the full step is not acceptable then begin another iteration.
            // Otherwise switch to stage two or end the calculation.

            if (icon > 0)
            {
                goto L70;
            }
            if (step == stpful)
            {
                goto L500;
            }
        L480:
            mcon = m + 1;
            icon = mcon;
            iact[mcon] = mcon;
            ws[vmultc + mcon] = 0.0;
            goto L60;

        // We employ any freedom that may be available to reduce the objective
        // function before returning a DX whose length is less than RHO.

        L490:
            if (mcon == m)
            {
                goto L480;
            }
            ifull = 0;
        L500:
            return OptimisationResult.Success;
        }

        #endregion
    }
}
