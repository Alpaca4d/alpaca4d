// SCI P354 by hand, written out again rather than calling the code under test. FootfallCheck.cs (the
// formulas, the worked examples, a beam in OpenSees) and PlateCheck.cs (the beam made again of shells,
// through the Grasshopper components) both check against these.
using System;

static class ByHand
{
    internal static double Alpha(int h, double fp)
    {
        double f = h * fp;
        return h == 1 ? 0.436 * (f - 0.95) : h == 2 ? 0.006 * (f + 12.3) : h == 3 ? 0.007 * (f + 5.2) : 0.007 * (f + 2.0);
    }

    internal static double Wb(double f) => f < 2 ? 0.4 : f < 5 ? f / 5 : f <= 16 ? 1.0 : 16 / f;

    /// <summary>Equation 30: h²β² / √((1 - h²β²)² + (2hζβ)²), β = fp / fn.</summary>
    internal static double D(int h, double fp, double fn, double zeta)
    {
        double hb = h * fp / fn;
        return hb * hb / Math.Sqrt(Math.Pow(1 - hb * hb, 2) + Math.Pow(2 * zeta * hb, 2));
    }

    /// <summary>
    /// Equation 31, the full time history, sampled over one pace for its rms, times ρ (Equation 37): each
    /// harmonic drives each mode at D (Equation 30) and its phase φ (Equation 32), tan φ = -2hβζ / (1 - h²β²),
    /// -π ≤ φ ≤ 0. The share of each mode, φₑ φᵣ, is given; Q in kN. The harmonics' own phases φₕ are left
    /// at 0: at their different frequencies they do not change the rms over a pace.
    /// </summary>
    internal static double SteadyByHand(double fp, double[] fn, double[] share, double zeta, int? steps, double q, int harmonics = 4,
                               Func<int, double, double> alpha = null)
    {
        alpha = alpha ?? Alpha;
        double rho = steps.HasValue ? 1 - Math.Exp(-2 * Math.PI * zeta * steps.Value) : 1;
        int samples = 720;
        double period = 1 / fp, sum = 0;
        for (int s = 0; s < samples; s++) // a periodic signal: the rectangle rule over a period is exact
        {
            double t = s * period / samples, a = 0;
            for (int h = 1; h <= harmonics; h++)
                for (int n = 0; n < fn.Length; n++)
                {
                    double beta = fp / fn[n];
                    double phase = Math.Atan2(-2 * h * beta * zeta, 1 - h * h * beta * beta);
                    a += share[n] * alpha(h, fp) * q * D(h, fp, fn[n], zeta) * Math.Sin(2 * Math.PI * h * fp * t + phase) * Wb(h * fp);
                }
            sum += a * a;
        }
        return rho * Math.Sqrt(sum / samples);
    }

    /// <summary>Equation 36 as P354 prints it: the modes added within a harmonic without their phases.</summary>
    internal static double Equation36(double fp, double[] fn, double[] share, double zeta, double q)
    {
        double sum = 0;
        for (int h = 1; h <= 4; h++)
        {
            double modes = 0;
            for (int n = 0; n < fn.Length; n++)
                modes += share[n] * Alpha(h, fp) * q * D(h, fp, fn[n], zeta) * Wb(h * fp);
            sum += modes * modes;
        }
        return Math.Sqrt(sum / 2);
    }

    /// <summary>Equations 18, 34 and 12 sampled: the modes' decaying sines added, and their rms over one pace.</summary>
    internal static double TransientByHand(double fp, double[] fn, double[] phi2, double zeta, double qNewtons, Func<double, double> w = null)
    {
        w = w ?? Wb;
        int samples = 200000;
        double period = 1 / fp, dt = period / samples, sum = 0;
        for (int s = 0; s <= samples; s++)
        {
            double t = s * dt, a = 0;
            for (int n = 0; n < fn.Length; n++)
            {
                double fi = 60 * Math.Pow(fp, 1.43) / Math.Pow(fn[n], 1.3) * qNewtons / 700 / 1000; // kN s
                double wd = 2 * Math.PI * fn[n] * Math.Sqrt(1 - zeta * zeta);
                a += wd * phi2[n] * fi * Math.Sin(wd * t) * Math.Exp(-zeta * 2 * Math.PI * fn[n] * t) * w(fn[n]);
            }
            double weight = s == 0 || s == samples ? 1 : s % 2 == 1 ? 4 : 2; // Simpson
            sum += weight * a * a;
        }
        return Math.Sqrt(sum * dt / 3 / period);
    }
}
