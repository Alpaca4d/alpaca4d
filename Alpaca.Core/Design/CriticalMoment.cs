using System;

namespace Alpaca4d.Design
{
    /// <summary>
    /// The elastic critical moment of lateral-torsional buckling, worked out on the moment diagram
    /// the member actually carries rather than read from tables of C1, C2 and C3.
    ///
    /// EN 1993-1-1 gives no formula for M_cr: 6.3.2.2(2) asks only that it come from the gross
    /// section and allow for the loading, the moment distribution and the lateral restraints. This
    /// is the classical energy solution (Timoshenko and Gere; Trahair), the one LTBeam solves. A
    /// segment is held sideways and against twist at both ends and left free to rotate and warp
    /// there - fork supports, k = kw = 1. Its lateral deflection u and its twist φ are each a sine
    /// series, and the critical load is the smallest multiple λ of the moment diagram at which the
    /// strain energy
    ///
    ///   U = ½ ∫ (E I_lat u''² + E Iw φ''² + G It φ'²) ds
    ///
    /// no longer exceeds the work the loads do as the segment buckles,
    ///
    ///   W = ∫ M u'' φ ds + ½ ∫ M β φ'² ds + ½ ∫ q a φ² ds.
    ///
    /// The second term is Wagner's: the bending stresses on fibres that the twist tilts, zero for a
    /// doubly symmetric section and what moves M_cr for a monosymmetric one. The third is the height
    /// of the load: a is how far from the shear centre it acts, positive on the side it comes from -
    /// a load on the top flange, which the twist carries sideways and lets fall.
    ///
    /// With M quadratic along the segment, which is what a beam carries between its nodes under a
    /// uniform load, every integral has a closed form, and the only approximation is where the
    /// series stops.
    /// </summary>
    public static class CriticalMoment
    {
        /// <summary>
        /// Sine terms in each of u and φ. Eight hold the critical moment of every diagram tried in the
        /// tests - reversed end moments, a top-flange load between them - to 0.01% of what thirty would
        /// give; the matrix they make is 16 × 16, and a model of thousands of members solves thousands
        /// of them.
        /// </summary>
        public const int Terms = 8;

        /// <summary>
        /// The smallest λ for which λ times the given loading buckles the segment, or +∞ when no
        /// multiple of it does - a segment with no moment, or one held by the height of its load.
        /// </summary>
        /// <param name="length">The segment, between two fork supports.</param>
        /// <param name="c0">M(t) = c0 + c1 t + c2 t², t = s / length from 0 to 1.</param>
        /// <param name="transverseLoad">The load per unit length, its size: what bends the diagram.</param>
        /// <param name="loadHeight">Where the load acts, from the shear centre, positive on the side it comes from.</param>
        /// <param name="lateralInertia">The second moment for bending sideways, out of the plane of M.</param>
        /// <param name="beta">
        /// The monosymmetry constant, (1/I) ∫ y (y² + z²) dA − 2 y0 in the axes and sign of M: positive
        /// when a positive M puts the smaller flange in compression.
        /// </param>
        public static double Multiplier(double length, double c0, double c1, double c2,
                                        double transverseLoad, double loadHeight,
                                        double e, double lateralInertia, double warping, double g, double torsion,
                                        double beta = 0.0, int terms = Terms)
        {
            int n = terms;
            int size = 2 * n;
            double half = length / 2.0;

            // ∫₀¹ M(t) cos(jπt) dt. With sin·sin and cos·cos turned into cosines of the sum and the
            // difference, every integral below is one of these.
            double F(int j)
            {
                j = Math.Abs(j);
                if (j == 0) return c0 + c1 / 2.0 + c2 / 3.0;
                double jp = j * Math.PI;
                double sign = j % 2 == 0 ? 1.0 : -1.0;
                return (c1 * (sign - 1.0) + 2.0 * c2 * sign) / (jp * jp);
            }

            // K is diagonal: the sines are orthogonal, and the section is the same all along.
            var stiffness = new double[size];
            var wavenumber = new double[n];
            for (int i = 0; i < n; i++)
            {
                double k = (i + 1) * Math.PI / length;
                wavenumber[i] = k;
                stiffness[i] = e * lateralInertia * k * k * k * k * half;
                stiffness[n + i] = (e * warping * k * k * k * k + g * torsion * k * k) * half;
            }

            for (int i = 0; i < size; i++)
            {
                if (!(stiffness[i] > 0.0))
                    return double.NaN;
            }

            // G, the work, in the unknowns (u terms, then φ terms).
            var work = new double[size, size];
            for (int i = 0; i < n; i++)
            {
                for (int m = 0; m < n; m++)
                {
                    // Term numbers i + 1 and m + 1: their difference is i − m, their sum i + m + 2.
                    double sinSin = half * (F(i - m) - F(i + m + 2));
                    double cosCos = half * (F(i - m) + F(i + m + 2));

                    double coupling = wavenumber[i] * wavenumber[i] * sinSin;
                    work[i, n + m] = coupling;
                    work[n + m, i] = coupling;

                    work[n + i, n + m] += beta * wavenumber[i] * wavenumber[m] * cosCos;
                }

                work[n + i, n + i] += transverseLoad * loadHeight * half;
            }

            // K⁻½ G K⁻½ is symmetric: its largest eigenvalue μ is 1/λ for the smallest λ.
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                    work[i, j] /= Math.Sqrt(stiffness[i] * stiffness[j]);
            }

            double largest = LargestEigenvalue(work, size);
            double scale = 0.0;
            for (int i = 0; i < size; i++)
                scale = Math.Max(scale, Math.Abs(work[i, i]));

            if (!(largest > 1e-12 * Math.Max(scale, 1e-300)) || largest <= 0.0)
                return double.PositiveInfinity;

            return 1.0 / largest;
        }

        /// <summary>The largest eigenvalue of a symmetric matrix, by cyclic Jacobi rotations. The matrix is overwritten.</summary>
        private static double LargestEigenvalue(double[,] a, int n)
        {
            for (int sweep = 0; sweep < 100; sweep++)
            {
                double off = 0.0, all = 0.0;
                for (int p = 0; p < n; p++)
                {
                    for (int q = 0; q < n; q++)
                    {
                        double x = a[p, q] * a[p, q];
                        all += x;
                        if (p != q) off += x;
                    }
                }

                if (off <= 1e-28 * all || all == 0.0)
                    break;

                for (int p = 0; p < n - 1; p++)
                {
                    for (int q = p + 1; q < n; q++)
                    {
                        double apq = a[p, q];
                        if (apq == 0.0) continue;

                        double theta = (a[q, q] - a[p, p]) / (2.0 * apq);
                        double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                        if (theta == 0.0) t = 1.0;
                        double c = 1.0 / Math.Sqrt(t * t + 1.0);
                        double s = t * c;

                        for (int k = 0; k < n; k++)
                        {
                            double akp = a[k, p], akq = a[k, q];
                            a[k, p] = c * akp - s * akq;
                            a[k, q] = s * akp + c * akq;
                        }
                        for (int k = 0; k < n; k++)
                        {
                            double apk = a[p, k], aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk;
                            a[q, k] = s * apk + c * aqk;
                        }
                    }
                }
            }

            double largest = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
                largest = Math.Max(largest, a[i, i]);
            return largest;
        }
    }
}
