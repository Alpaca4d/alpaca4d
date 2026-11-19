using System;

using Alpaca4d.Loads;
using Alpaca4d.Material;

namespace Alpaca4d.Design
{
    /// <summary>
    /// The rules of EN 1995-1-1 (2004, with A1 2008 and A2 2014) the timber check is built from, each
    /// a plain function of numbers, with its clause. Where the code leaves a value to the National
    /// Annex, the recommended one.
    /// </summary>
    public static class Ec5
    {
        /// <summary>
        /// Table 3.1 for solid timber, glulam and LVL, which share it: a row per service class, a
        /// column per load-duration class, permanent to instantaneous.
        /// </summary>
        private static readonly double[,] KmodTable =
        {
            { 0.60, 0.70, 0.80, 0.90, 1.10 },
            { 0.60, 0.70, 0.80, 0.90, 1.10 },
            { 0.50, 0.55, 0.65, 0.70, 0.90 },
        };

        /// <summary>k_mod (Table 3.1) for a service class, 1 to 3, and the duration of the loads.</summary>
        public static double Kmod(int serviceClass, LoadDuration duration)
        {
            if (serviceClass < 1 || serviceClass > 3)
                throw new ArgumentOutOfRangeException(nameof(serviceClass), "A service class is 1, 2 or 3.");
            return KmodTable[serviceClass - 1, (int)duration];
        }

        /// <summary>γM, Table 2.3 as recommended: 1.3 for solid timber, 1.25 for glulam, 1.0 for an accidental combination.</summary>
        public static double GammaM(TimberKind kind, bool accidental)
        {
            if (accidental) return 1.0;
            return kind == TimberKind.Glulam ? 1.25 : 1.3;
        }

        /// <summary>
        /// k_h, the size factor on f_m,k and f_t,0,k for a member less deep in bending, or less wide in
        /// tension, than the reference: (3.1) for solid timber, 150 mm, and only up to ρ_k 700 kg/m³;
        /// (3.2) for glulam, 600 mm.
        /// </summary>
        /// <param name="sizeMm">The depth in bending, or the largest dimension in tension [mm].</param>
        public static double Kh(TimberKind kind, double rhoK, double sizeMm)
        {
            if (!(sizeMm > 0.0)) return 1.0;
            if (kind == TimberKind.Glulam)
                return sizeMm < 600.0 ? Math.Min(Math.Pow(600.0 / sizeMm, 0.1), 1.1) : 1.0;
            if (rhoK > 700.0)
                return 1.0;
            return sizeMm < 150.0 ? Math.Min(Math.Pow(150.0 / sizeMm, 0.2), 1.3) : 1.0;
        }

        /// <summary>k_cr, the share of the width that carries shear past the cracks (6.1.7(2)): 0.67 for solid timber and glulam.</summary>
        public const double Kcr = 0.67;

        /// <summary>k_m (6.1.6(2)): 0.7 for a rectangle of solid timber, glulam or LVL, which yields at a corner; 1.0 for any other section.</summary>
        public static double Km(bool rectangular) => rectangular ? 0.7 : 1.0;

        /// <summary>β_c, the straightness factor of (6.29): 0.2 for solid timber, 0.1 for glulam and LVL.</summary>
        public static double BetaC(TimberKind kind) => kind == TimberKind.Glulam ? 0.1 : 0.2;

        /// <summary>λ_rel of (6.21) and (6.22): the slenderness L_cr / i over π, times √(f_c,0,k / E_0,05).</summary>
        public static double RelativeSlenderness(double slenderness, double fc0K, double e005)
        {
            return slenderness / Math.PI * Math.Sqrt(fc0K / e005);
        }

        /// <summary>k_c of (6.25) to (6.28); 1 up to λ_rel 0.3, which (6.27) reaches there too.</summary>
        public static double Kc(double relativeSlenderness, double betaC)
        {
            double l = relativeSlenderness;
            if (l <= 0.3) return 1.0;
            double k = 0.5 * (1.0 + betaC * (l - 0.3) + l * l);
            return 1.0 / (k + Math.Sqrt(k * k - l * l));
        }

        /// <summary>λ_rel,m of (6.30): √(f_m,k / σ_m,crit).</summary>
        public static double RelativeSlendernessM(double fmK, double sigmaCrit)
        {
            if (double.IsPositiveInfinity(sigmaCrit)) return 0.0;
            return Math.Sqrt(fmK / sigmaCrit);
        }

        /// <summary>k_crit of (6.34), the reduction for lateral-torsional buckling.</summary>
        public static double Kcrit(double relativeSlendernessM)
        {
            double l = relativeSlendernessM;
            if (l <= 0.75) return 1.0;
            if (l <= 1.4) return 1.56 - 0.75 * l;
            return 1.0 / (l * l);
        }

        /// <summary>k_shape of (6.15) for a circle: 1.2.</summary>
        public const double KshapeCircle = 1.2;

        /// <summary>k_shape of (6.15) for a rectangle: 1 + 0.15 h/b, at most 2, with h the larger side and b the smaller.</summary>
        public static double KshapeRectangle(double side1, double side2)
        {
            double h = Math.Max(side1, side2), b = Math.Min(side1, side2);
            return Math.Min(1.0 + 0.15 * h / b, 2.0);
        }

        /// <summary>
        /// The largest St. Venant shear stress of a torque on a solid rectangle, at the middle of its
        /// long side. EN 1995-1-1 gives none; this is Roark's (7th edition, Table 10.7, case 4),
        /// 3T / (h b²) (1 + 0.6095 r + 0.8865 r² − 1.8023 r³ + 0.9100 r⁴), r = b/h, within 0.1% of the
        /// exact series, and T / (0.208 b³) for a square.
        /// </summary>
        public static double TorsionStressRectangle(double torque, double side1, double side2)
        {
            double h = Math.Max(side1, side2), b = Math.Min(side1, side2);
            double r = b / h;
            double factor = 1.0 + r * (0.6095 + r * (0.8865 + r * (-1.8023 + r * 0.9100)));
            return 3.0 * Math.Abs(torque) / (h * b * b) * factor;
        }

        /// <summary>The largest St. Venant shear stress of a torque on a solid circle: 16 T / (π d³).</summary>
        public static double TorsionStressCircle(double torque, double diameter)
        {
            return 16.0 * Math.Abs(torque) / (Math.PI * diameter * diameter * diameter);
        }
    }
}
