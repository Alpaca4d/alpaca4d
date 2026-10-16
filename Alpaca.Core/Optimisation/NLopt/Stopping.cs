// Ported from NLopt, src/util/stop.c and src/util/nlopt-util.h.
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
using System.Diagnostics;
using System.Threading;

namespace Alpaca4d.Optimisation.NLopt
{
    /// <summary>
    /// Set from another thread to stop an optimisation: the Stop button, Esc, the document closing.
    /// NLopt's int *force_stop.
    /// </summary>
    internal sealed class StopFlag
    {
        private int _value;

        public int Value
        {
            get => Volatile.Read(ref _value);
            set => Volatile.Write(ref _value, value);
        }
    }

    /// <summary>
    /// NLopt's nlopt_stopping and the tests of stop.c, kept as they are so that the ported
    /// algorithms stop exactly where NLopt's do. Where the C passes a pointer into a larger array,
    /// the overloads with an offset stand in for the pointer arithmetic.
    /// </summary>
    internal sealed class Stopping
    {
        public int N;
        public double MinfMax;
        public double FtolRel;
        public double FtolAbs;
        public double XtolRel;
        public double[] XtolAbs;   // null when not set
        public double[] XWeights;  // null when not set
        public int NEvals;         // *nevals_p
        public int MaxEval;
        public double MaxTime;
        public double Start;
        public StopFlag ForceStop; // force_stop; may be null
        public string Message;     // *stop_msg

        private static double Sc(double x, double smin, double smax) => smin + x * (smax - smin);

        private static double VectorNorm(int n, double[] vec, int off, double[] w, double[] scaleMin, double[] scaleMax)
        {
            double ret = 0;
            if (scaleMin != null && scaleMax != null)
            {
                if (w != null)
                    for (int i = 0; i < n; i++) ret += w[i] * Math.Abs(Sc(vec[off + i], scaleMin[i], scaleMax[i]));
                else
                    for (int i = 0; i < n; i++) ret += Math.Abs(Sc(vec[off + i], scaleMin[i], scaleMax[i]));
            }
            else
            {
                if (w != null)
                    for (int i = 0; i < n; i++) ret += w[i] * Math.Abs(vec[off + i]);
                else
                    for (int i = 0; i < n; i++) ret += Math.Abs(vec[off + i]);
            }
            return ret;
        }

        private static double DiffNorm(int n, double[] x, int xOff, double[] oldx, int oldOff, double[] w, double[] scaleMin, double[] scaleMax)
        {
            double ret = 0;
            if (scaleMin != null && scaleMax != null)
            {
                if (w != null)
                    for (int i = 0; i < n; i++) ret += w[i] * Math.Abs(Sc(x[xOff + i], scaleMin[i], scaleMax[i]) - Sc(oldx[oldOff + i], scaleMin[i], scaleMax[i]));
                else
                    for (int i = 0; i < n; i++) ret += Math.Abs(Sc(x[xOff + i], scaleMin[i], scaleMax[i]) - Sc(oldx[oldOff + i], scaleMin[i], scaleMax[i]));
            }
            else
            {
                if (w != null)
                    for (int i = 0; i < n; i++) ret += w[i] * Math.Abs(x[xOff + i] - oldx[oldOff + i]);
                else
                    for (int i = 0; i < n; i++) ret += Math.Abs(x[xOff + i] - oldx[oldOff + i]);
            }
            return ret;
        }

        private static bool RelStop(double vold, double vnew, double reltol, double abstol)
        {
            if (IsInf(vold)) return false;
            return Math.Abs(vnew - vold) < abstol
                || Math.Abs(vnew - vold) < reltol * (Math.Abs(vnew) + Math.Abs(vold)) * 0.5
                || (reltol > 0 && vnew == vold); // catch vnew == vold == 0
        }

        public bool StopFtol(double f, double oldf) => RelStop(oldf, f, FtolRel, FtolAbs);

        public bool StopF(double f, double oldf) => f <= MinfMax || StopFtol(f, oldf);

        public bool StopX(double[] x, double[] oldx) => StopX(x, 0, oldx, 0);

        public bool StopX(double[] x, int xOff, double[] oldx, int oldOff)
        {
            if (DiffNorm(N, x, xOff, oldx, oldOff, XWeights, null, null) < XtolRel * VectorNorm(N, x, xOff, XWeights, null, null))
                return true;
            if (XtolAbs == null) return false;
            for (int i = 0; i < N; ++i)
                if (Math.Abs(x[xOff + i] - oldx[oldOff + i]) >= XtolAbs[i])
                    return false;
            return true;
        }

        public bool StopDx(double[] x, double[] dx) => StopDx(x, 0, dx, 0);

        public bool StopDx(double[] x, int xOff, double[] dx, int dxOff)
        {
            if (VectorNorm(N, dx, dxOff, XWeights, null, null) < XtolRel * VectorNorm(N, x, xOff, XWeights, null, null))
                return true;
            if (XtolAbs == null) return false;
            for (int i = 0; i < N; ++i)
                if (Math.Abs(dx[dxOff + i]) >= XtolAbs[i])
                    return false;
            return true;
        }

        /// <summary>For algorithms that work in a unit hypercube: scaled back before comparing.</summary>
        public bool StopXs(double[] xs, double[] oldxs, double[] scaleMin, double[] scaleMax) => StopXs(xs, 0, oldxs, 0, scaleMin, scaleMax);

        public bool StopXs(double[] xs, int xsOff, double[] oldxs, int oldOff, double[] scaleMin, double[] scaleMax)
        {
            if (DiffNorm(N, xs, xsOff, oldxs, oldOff, XWeights, scaleMin, scaleMax) < XtolRel * VectorNorm(N, xs, xsOff, XWeights, scaleMin, scaleMax))
                return true;
            if (XtolAbs == null) return false;
            for (int i = 0; i < N; ++i)
                if (Math.Abs(Sc(xs[xsOff + i], scaleMin[i], scaleMax[i]) - Sc(oldxs[oldOff + i], scaleMin[i], scaleMax[i])) >= XtolAbs[i])
                    return false;
            return true;
        }

        public bool StopEvals() => MaxEval > 0 && NEvals >= MaxEval;

        public static bool StopTime(double start, double maxtime) => maxtime > 0 && Seconds() - start >= maxtime;

        public bool StopTime() => StopTime(Start, MaxTime);

        public bool StopEvalsTime() => StopEvals() || StopTime();

        public bool StopForced() => ForceStop != null && ForceStop.Value != 0;

        /// <summary>nlopt_stop_msg, without the printf: the message is formatted by the caller.</summary>
        public void StopMessage(string message) => Message = message;

        #region nlopt-util.h

        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        /// <summary>nlopt_seconds: seconds since the first call, which is all the stopping tests need.</summary>
        public static double Seconds() => Clock.Elapsed.TotalSeconds;

        /// <summary>nlopt_time_seed.</summary>
        public static uint TimeSeed()
        {
            long ticks = DateTime.UtcNow.Ticks;
            return (uint)(ticks ^ (ticks >> 32));
        }

        /// <summary>nlopt_isinf: fabs(x) >= HUGE_VAL * 0.99, and HUGE_VAL is infinity.</summary>
        public static bool IsInf(double x) => double.IsInfinity(x);

        public static bool IsFinite(double x) => Math.Abs(x) <= double.MaxValue;

        public static bool IsTiny(double x) => x == 0.0 || Math.Abs(x) < 2.2250738585072014e-308;

        public static bool IsNaN(double x) => double.IsNaN(x);

        #endregion
    }
}
