using System;

namespace Alpaca4d.Design
{
    /// <summary>The buckling curves of EN 1993-1-1 Table 6.1.</summary>
    public enum BucklingCurve
    {
        a0,
        a,
        b,
        c,
        d
    }

    /// <summary>
    /// The rules of EN 1993-1-1 that the steel check is built from, one function per clause or table,
    /// each taking plain numbers. Kept apart from the sections and the forces so that every one can be
    /// set against a published worked example on its own: a classification against the c/t a textbook
    /// printed, a reduction factor against the χ it printed.
    ///
    /// Recommended values throughout, which are the ones the standard gives and a National Annex may
    /// change: γM0 = γM1 = 1.0, η = 1.0, the α of Table 6.1.
    /// </summary>
    public static class Ec3
    {
        /// <summary>ε = √(235 / fy), fy in N/mm². Table 5.2.</summary>
        public static double Epsilon(double fyMPa) => Math.Sqrt(235.0 / fyMPa);

        #region Classification, Table 5.2

        /// <summary>
        /// An internal compression part - a web, a wall of a box. Table 5.2 sheet 1.
        ///
        /// Class 1 and 2 are judged on the plastic distribution: <paramref name="alpha"/> is the share of
        /// the part in compression, 1 when all of it is, 0.5 under pure bending. Class 3 is judged on the
        /// elastic one: <paramref name="psi"/> is the stress at the less compressed end over the stress at
        /// the more compressed one, 1 for uniform compression and −1 for pure bending.
        /// </summary>
        public static int ClassInternal(double cOverT, double alpha, double psi, double epsilon)
        {
            if (alpha <= 0.0)
                return 1;

            alpha = Math.Min(alpha, 1.0);

            double class1 = alpha > 0.5 ? 396.0 * epsilon / (13.0 * alpha - 1.0) : 36.0 * epsilon / alpha;
            double class2 = alpha > 0.5 ? 456.0 * epsilon / (13.0 * alpha - 1.0) : 41.5 * epsilon / alpha;
            double class3 = psi > -1.0
                ? 42.0 * epsilon / (0.67 + 0.33 * Math.Min(psi, 1.0))
                : 62.0 * epsilon * (1.0 - psi) * Math.Sqrt(-psi);

            return Class(cOverT, class1, class2, class3);
        }

        /// <summary>
        /// An outstand flange in compression. Table 5.2 sheet 2: 9ε, 10ε, 14ε.
        ///
        /// Used whatever the stress across the outstand. Bending that puts its tip in tension, or only
        /// part of it in compression, earns higher limits, so taking the compression ones is the safe side.
        /// </summary>
        public static int ClassOutstand(double cOverT, double epsilon)
        {
            return Class(cOverT, 9.0 * epsilon, 10.0 * epsilon, 14.0 * epsilon);
        }

        /// <summary>A circular hollow section in bending and/or compression. Table 5.2 sheet 3: 50ε², 70ε², 90ε².</summary>
        public static int ClassTube(double dOverT, double epsilon)
        {
            double e2 = epsilon * epsilon;
            return Class(dOverT, 50.0 * e2, 70.0 * e2, 90.0 * e2);
        }

        /// <summary>
        /// An angle in compression, Table 5.2 sheet 3: no worse than Class 3 if h/t ≤ 15ε and
        /// (b + h)/2t ≤ 11.5ε. The sheet gives no Class 1 or 2 for angles; those come from treating each
        /// leg as an outstand.
        /// </summary>
        public static bool AngleWithinClass3(double h, double b, double t, double epsilon)
        {
            return h / t <= 15.0 * epsilon && (b + h) / (2.0 * t) <= 11.5 * epsilon;
        }

        private static int Class(double ratio, double class1, double class2, double class3)
        {
            if (ratio <= class1) return 1;
            if (ratio <= class2) return 2;
            if (ratio <= class3) return 3;
            return 4;
        }

        #endregion

        #region Cross-section resistance, 6.2

        /// <summary>V_pl,Rd = Av (fy / √3) / γM0. 6.2.6(2).</summary>
        public static double PlasticShear(double shearArea, double fy, double gammaM0)
        {
            return shearArea * fy / (Math.Sqrt(3.0) * gammaM0);
        }

        /// <summary>
        /// How much of V_pl,Rd is left with a St. Venant shear stress τt,Ed in the section. 6.2.7(9):
        /// √(1 − τ / (1.25 fy/√3/γM0)) for an I or H, (1 − τ / (fy/√3/γM0)) for a hollow section. The
        /// hollow form is the lower of the two and is used for any other shape. Zero once the torsion
        /// alone has used the section up.
        /// </summary>
        public static double TorsionShearFactor(double tauTorsion, double fy, double gammaM0, bool iOrH)
        {
            double tauRd = fy / (Math.Sqrt(3.0) * gammaM0);

            double factor = iOrH
                ? 1.0 - tauTorsion / (1.25 * tauRd)
                : 1.0 - tauTorsion / tauRd;

            if (factor <= 0.0)
                return 0.0;

            return iOrH ? Math.Sqrt(factor) : factor;
        }

        /// <summary>
        /// ρ = (2 V_Ed / V_Rd − 1)², or 0 while V_Ed is no more than half V_Rd. 6.2.8(3), 6.2.10(3): the
        /// share by which fy is reduced over the shear area.
        /// </summary>
        public static double ShearReduction(double vEd, double vRd)
        {
            vEd = Math.Abs(vEd);
            if (vRd <= 0.0)
                return vEd > 0.0 ? 1.0 : 0.0;
            if (vEd <= 0.5 * vRd)
                return 0.0;

            double r = 2.0 * vEd / vRd - 1.0;
            return Math.Min(r * r, 1.0);
        }

        /// <summary>
        /// M_N,y,Rd of a doubly symmetric I or H about its major axis, 6.2.9.1(5) equation (6.36):
        /// M_pl (1 − n) / (1 − 0.5a), no more than M_pl. a = (A − 2b tf)/A, capped at 0.5.
        /// </summary>
        public static double ReducedMomentIMajor(double mpl, double n, double a)
        {
            a = Math.Min(a, 0.5);
            return Math.Max(0.0, Math.Min(mpl, mpl * (1.0 - n) / (1.0 - 0.5 * a)));
        }

        /// <summary>
        /// M_N,z,Rd of a doubly symmetric I or H about its minor axis, equations (6.37) and (6.38): M_pl
        /// while n ≤ a, then M_pl [1 − ((n − a)/(1 − a))²].
        /// </summary>
        public static double ReducedMomentIMinor(double mpl, double n, double a)
        {
            a = Math.Min(a, 0.5);
            if (n <= a)
                return mpl;

            double r = (n - a) / (1.0 - a);
            return Math.Max(0.0, mpl * (1.0 - r * r));
        }

        /// <summary>
        /// M_N,Rd of a rectangular hollow section of uniform thickness, equations (6.39) and (6.40):
        /// M_pl (1 − n) / (1 − 0.5 a), no more than M_pl. <paramref name="a"/> is aw for the major axis and
        /// af for the minor, each capped at 0.5.
        /// </summary>
        public static double ReducedMomentBox(double mpl, double n, double a)
        {
            a = Math.Min(a, 0.5);
            return Math.Max(0.0, Math.Min(mpl, mpl * (1.0 - n) / (1.0 - 0.5 * a)));
        }

        /// <summary>M_N,Rd = M_pl (1 − n^1.7) of a circular hollow section, 6.2.9.1(6).</summary>
        public static double ReducedMomentTube(double mpl, double n)
        {
            return Math.Max(0.0, mpl * (1.0 - Math.Pow(Math.Max(n, 0.0), 1.7)));
        }

        /// <summary>M_N,Rd = M_pl [1 − n²] of a solid rectangle, 6.2.9.1(3) equation (6.32).</summary>
        public static double ReducedMomentRectangle(double mpl, double n)
        {
            return Math.Max(0.0, mpl * (1.0 - n * n));
        }

        /// <summary>The exponents α = β of 6.2.9.1(6) for a rectangular hollow section: 1.66 / (1 − 1.13 n²), at most 6.</summary>
        public static double BoxExponent(double n)
        {
            double denominator = 1.0 - 1.13 * n * n;
            return denominator <= 0.0 ? 6.0 : Math.Min(1.66 / denominator, 6.0);
        }

        /// <summary>The left-hand side of 6.2.9.1(6) equation (6.41): (M1/MN1)^α + (M2/MN2)^β.</summary>
        public static double Biaxial(double m1, double mn1, double alpha, double m2, double mn2, double beta)
        {
            return Term(m1, mn1, alpha) + Term(m2, mn2, beta);
        }

        private static double Term(double m, double mn, double exponent)
        {
            m = Math.Abs(m);
            if (m == 0.0) return 0.0;
            if (mn <= 0.0) return double.PositiveInfinity;
            return Math.Pow(m / mn, exponent);
        }

        #endregion

        #region Flexural buckling, 6.3.1

        /// <summary>The imperfection factor α of a buckling curve. Table 6.1.</summary>
        public static double Imperfection(BucklingCurve curve)
        {
            switch (curve)
            {
                case BucklingCurve.a0: return 0.13;
                case BucklingCurve.a: return 0.21;
                case BucklingCurve.b: return 0.34;
                case BucklingCurve.c: return 0.49;
                default: return 0.76;
            }
        }

        /// <summary>N_cr = π² E I / L_cr², the Euler load.</summary>
        public static double CriticalForce(double e, double inertia, double length)
        {
            return Math.PI * Math.PI * e * inertia / (length * length);
        }

        /// <summary>λ̄ = √(A fy / N_cr), Class 1 to 3. 6.3.1.2(1).</summary>
        public static double Slenderness(double area, double fy, double criticalForce)
        {
            return Math.Sqrt(area * fy / criticalForce);
        }

        /// <summary>
        /// χ = 1 / (Φ + √(Φ² − λ̄²)), Φ = 0.5 [1 + α (λ̄ − 0.2) + λ̄²], at most 1. 6.3.1.2(1) equation (6.49).
        /// </summary>
        public static double Reduction(double slenderness, double imperfection)
        {
            double phi = 0.5 * (1.0 + imperfection * (slenderness - 0.2) + slenderness * slenderness);
            double chi = 1.0 / (phi + Math.Sqrt(phi * phi - slenderness * slenderness));
            return Math.Min(chi, 1.0);
        }

        /// <summary>
        /// The buckling curve of a rolled I or H, Table 6.2. <paramref name="majorAxis"/> for buckling
        /// about y-y, the axis parallel to the flanges. The S460 column applies from fy = 460 N/mm².
        /// </summary>
        public static BucklingCurve CurveRolledI(double hOverB, double tfMm, bool majorAxis, double fyMPa)
        {
            bool s460 = fyMPa >= 460.0;

            if (hOverB > 1.2)
            {
                if (tfMm <= 40.0)
                    return s460 ? BucklingCurve.a0 : (majorAxis ? BucklingCurve.a : BucklingCurve.b);
                return s460 ? BucklingCurve.a : (majorAxis ? BucklingCurve.b : BucklingCurve.c);
            }

            if (tfMm <= 100.0)
                return s460 ? BucklingCurve.a : (majorAxis ? BucklingCurve.b : BucklingCurve.c);
            return s460 ? BucklingCurve.c : BucklingCurve.d;
        }

        /// <summary>The buckling curve of a welded I, Table 6.2: b / c up to tf = 40 mm, c / d beyond.</summary>
        public static BucklingCurve CurveWeldedI(double tfMm, bool majorAxis)
        {
            if (tfMm <= 40.0)
                return majorAxis ? BucklingCurve.b : BucklingCurve.c;
            return majorAxis ? BucklingCurve.c : BucklingCurve.d;
        }

        /// <summary>The buckling curve of a hollow section, Table 6.2: a (a0 for S460) hot finished, c cold formed.</summary>
        public static BucklingCurve CurveHollow(bool hotFinished, double fyMPa)
        {
            if (!hotFinished)
                return BucklingCurve.c;
            return fyMPa >= 460.0 ? BucklingCurve.a0 : BucklingCurve.a;
        }

        #endregion

        #region Lateral-torsional buckling, 6.3.2

        /// <summary>
        /// The lateral-torsional buckling curve of the general case, Table 6.4: a rolled I a up to
        /// h/b = 2 and b beyond, a welded I c and d, any other section d.
        /// </summary>
        public static BucklingCurve CurveLtGeneral(bool iSection, bool rolled, double hOverB)
        {
            if (!iSection)
                return BucklingCurve.d;
            if (rolled)
                return hOverB <= 2.0 ? BucklingCurve.a : BucklingCurve.b;
            return hOverB <= 2.0 ? BucklingCurve.c : BucklingCurve.d;
        }

        /// <summary>
        /// The lateral-torsional buckling curve of 6.3.2.3, rolled or equivalent welded I sections,
        /// Table 6.5: rolled b up to h/b = 2 and c beyond, welded c and d.
        /// </summary>
        public static BucklingCurve CurveLtRolled(bool rolled, double hOverB)
        {
            if (rolled)
                return hOverB <= 2.0 ? BucklingCurve.b : BucklingCurve.c;
            return hOverB <= 2.0 ? BucklingCurve.c : BucklingCurve.d;
        }

        /// <summary>λ̄LT = √(Wy fy / M_cr). 6.3.2.2(1).</summary>
        public static double SlendernessLt(double modulus, double fy, double criticalMoment)
        {
            return Math.Sqrt(modulus * fy / criticalMoment);
        }

        /// <summary>
        /// χLT = 1 / (Φ + √(Φ² − β λ̄²)), Φ = 0.5 [1 + αLT (λ̄ − λ̄LT,0) + β λ̄²], at most 1 and at most
        /// 1/λ̄². 6.3.2.2(1) equation (6.56) with λ̄LT,0 = 0.2 and β = 1; 6.3.2.3(1) equation (6.57)
        /// with the recommended λ̄LT,0 = 0.4 and β = 0.75.
        /// </summary>
        public static double ReductionLt(double slenderness, double imperfection, double plateau, double beta)
        {
            double phi = 0.5 * (1.0 + imperfection * (slenderness - plateau) + beta * slenderness * slenderness);
            double chi = 1.0 / (phi + Math.Sqrt(phi * phi - beta * slenderness * slenderness));
            chi = Math.Min(chi, 1.0);
            if (slenderness > 0.0)
                chi = Math.Min(chi, 1.0 / (slenderness * slenderness));
            return chi;
        }

        /// <summary>
        /// 6.3.2.2(4): lateral-torsional buckling may be ignored for λ̄LT ≤ λ̄LT,0, or for
        /// M_Ed / M_cr ≤ λ̄LT,0².
        /// </summary>
        public static bool LtIgnored(double slenderness, double plateau, double mEd, double criticalMoment)
        {
            return slenderness <= plateau || Math.Abs(mEd) / criticalMoment <= plateau * plateau;
        }

        #endregion

        #region Bending and axial compression, 6.3.3 and Annex B

        /// <summary>
        /// The equivalent uniform moment factor of Table B.3, for a diagram with <paramref name="mh"/>
        /// at one end, ψ mh at the other and <paramref name="ms"/> in the span, under a uniform load -
        /// the only span load a member carries between its nodes. With no span load the diagram is
        /// linear and its mid-span value makes the same factor, 0.6 + 0.4ψ, so one form serves both.
        ///
        /// <paramref name="mh"/> is the end moment of the larger size; <paramref name="ms"/>, the moment
        /// in the span the table measures against it.
        /// </summary>
        public static double EquivalentMoment(double mh, double psi, double ms)
        {
            psi = Math.Max(-1.0, Math.Min(1.0, psi));

            if (Math.Abs(ms) <= Math.Abs(mh))
            {
                if (mh == 0.0)
                    return 0.95;

                double alphaS = ms / mh;
                double cm;
                if (alphaS >= 0.0)
                    cm = 0.2 + 0.8 * alphaS;
                else if (psi >= 0.0)
                    cm = 0.1 - 0.8 * alphaS;
                else
                    cm = 0.1 * (1.0 - psi) - 0.8 * alphaS;
                return Math.Max(cm, 0.4);
            }

            double alphaH = mh / ms;
            if (alphaH >= 0.0 || psi >= 0.0)
                return 0.95 + 0.05 * alphaH;
            return 0.95 + 0.05 * alphaH * (1.0 + 2.0 * psi);
        }

        /// <summary>The interaction factors of Annex B (method 2).</summary>
        public struct InteractionFactors
        {
            public double Kyy, Kyz, Kzy, Kzz;
        }

        /// <summary>
        /// kyy, kyz, kzy and kzz of Annex B, Table B.1 - and Table B.2's kzy for a member susceptible
        /// to torsional deformations, an open section free to twist. y is the major axis, as the
        /// standard has it.
        ///
        /// <paramref name="ny"/> and <paramref name="nz"/> are N_Ed / (χ N_Rk / γM1) about each axis,
        /// <paramref name="plastic"/> Class 1 or 2. For kzz of a Class 1 or 2 member Table B.1 gives
        /// one formula for an I and another for a rectangular hollow section, and is silent on the
        /// rest: those take the larger of the two.
        /// </summary>
        public static InteractionFactors AnnexB(bool plastic, double lambdaY, double lambdaZ, double ny, double nz,
                                                double cmy, double cmz, double cmLT, bool torsionallyDeformable,
                                                SteelShape shape)
        {
            double kyy, kzz;
            if (plastic)
            {
                kyy = cmy * Math.Min(1.0 + (lambdaY - 0.2) * ny, 1.0 + 0.8 * ny);

                double kzzI = cmz * Math.Min(1.0 + (2.0 * lambdaZ - 0.6) * nz, 1.0 + 1.4 * nz);
                double kzzBox = cmz * Math.Min(1.0 + (lambdaZ - 0.2) * nz, 1.0 + 0.8 * nz);
                kzz = shape == SteelShape.I ? kzzI : shape == SteelShape.Box ? kzzBox : Math.Max(kzzI, kzzBox);
            }
            else
            {
                kyy = cmy * Math.Min(1.0 + 0.6 * lambdaY * ny, 1.0 + 0.6 * ny);
                kzz = cmz * Math.Min(1.0 + 0.6 * lambdaZ * nz, 1.0 + 0.6 * nz);
            }

            double kyz = plastic ? 0.6 * kzz : kzz;

            double kzy;
            if (!torsionallyDeformable)
            {
                kzy = plastic ? 0.6 * kyy : 0.8 * kyy;
            }
            else
            {
                double factor = plastic ? 0.1 : 0.05;
                double d = cmLT - 0.25;
                kzy = Math.Max(1.0 - factor * lambdaZ / d * nz, 1.0 - factor / d * nz);
                if (plastic && lambdaZ < 0.4)
                    kzy = Math.Min(0.6 + lambdaZ, 1.0 - 0.1 * lambdaZ / d * nz);
            }

            return new InteractionFactors { Kyy = kyy, Kyz = kyz, Kzy = kzy, Kzz = kzz };
        }

        #endregion
    }
}
