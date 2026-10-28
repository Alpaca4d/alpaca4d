// EN 1993-1-1 utilisation of steel members.
//
// Three kinds of check. The rules of the standard - classification limits, χ, V_pl,Rd, M_N,Rd - are
// set against published worked examples one by one, each in its own units and with its own numbers,
// to the digits printed. Alpaca4d's shapes are turned into EC3 properties and checked against hand
// formulas, since Alpaca4d's I-sections have no root radius and no catalogue matches them. And the
// whole chain - recorder file, forces, check - is run on a deck OpenSees solves here, with the
// expected utilisations worked out by hand.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Alpaca4d;
using Alpaca4d.Design;
using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Result;
using Alpaca4d.Section;
using ISection = Alpaca4d.Section.ISection;

class SteelCheckTest
{
    static int fails = 0;
    static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Close(double got, double want, double tol, string what)
    {
        bool ok = Math.Abs(got - want) <= tol * Math.Max(1e-12, Math.Abs(want));
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {got.ToString("G6", Invariant)}, want {want.ToString("G6", Invariant)}");
    }

    /// <summary>Agrees with a printed value to every digit printed.</summary>
    static void Printed(double got, string printed, string what)
    {
        double want = double.Parse(printed, Invariant);
        int decimals = printed.Contains(".") ? printed.Length - printed.IndexOf('.') - 1 : 0;
        double rounded = Math.Round(got, decimals, MidpointRounding.AwayFromZero);
        bool ok = Math.Abs(rounded - want) <= 1e-9 * Math.Max(1.0, Math.Abs(want));
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {got.ToString("G6", Invariant)}, printed {printed}");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    // Model units are kN and m, so a stress of 1 N/mm² is 1000 kN/m².
    const double MPa = 1000.0;

    static UniaxialMaterialElastic Steel(string grade)
    {
        return new UniaxialMaterialElastic(grade, 210e6, 210e6, 0.0, 80.77e6, 0.3, 7850) { Grade = SteelGrade.Find(grade) };
    }

    // ---------------------------------------------------------------- published: the rules on their own

    static void JrcColumn()
    {
        // JRC / European Commission, "Eurocodes - Design of steel buildings with worked examples",
        // Brussels 2014, R. Simões, Design of Members, Example 1: inner column E-3, HEB 340 in S355,
        // L = 4.335 m about both axes, N_Ed = 3326.0 kN.
        // A = 170.9 cm², b = 300, h = 340, tf = 21.5, tw = 12, r = 27 mm, Iy = 36660 cm⁴, iy = 14.65 cm,
        // Iz = 9690 cm⁴, iz = 7.53 cm.
        Section("Published: JRC 2014, Example 1, HEB 340 column in S355");

        double eps = Ec3.Epsilon(355);
        Printed(eps, "0.81", "ε");

        double cWeb = (340 - 2 * 21.5 - 2 * 27) / 12.0;
        double cFlange = ((300 - 12 - 2 * 27) / 2.0) / 21.5;
        Printed(cWeb, "20.25", "web c/t, (h − 2tf − 2r)/tw");
        Printed(cFlange, "5.44", "flange c/t");
        Check(Ec3.ClassInternal(cWeb, 1.0, 1.0, eps) == 1, "web in compression: class 1");
        Check(Ec3.ClassOutstand(cFlange, eps) == 1, "flange in compression: class 1");

        double a = 170.9e-4, fy = 355 * MPa, e = 210e6, l = 4.335;
        Printed(a * fy, "6067", "N_c,Rd [kN]");

        double ncrY = Ec3.CriticalForce(e, 36660e-8, l), ncrZ = Ec3.CriticalForce(e, 9690e-8, l);
        double lamY = Ec3.Slenderness(a, fy, ncrY), lamZ = Ec3.Slenderness(a, fy, ncrZ);
        Printed(lamY, "0.39", "λ̄y");
        Printed(lamZ, "0.75", "λ̄z");

        // h/b = 1.13 ≤ 1.2, tf ≤ 100 mm: curve b about y-y, c about z-z.
        Check(Ec3.CurveRolledI(340.0 / 300.0, 21.5, true, 355) == BucklingCurve.b, "curve b about the major axis");
        Check(Ec3.CurveRolledI(340.0 / 300.0, 21.5, false, 355) == BucklingCurve.c, "curve c about the minor axis");

        double chiZ = Ec3.Reduction(lamZ, Ec3.Imperfection(BucklingCurve.c));
        Printed(chiZ, "0.69", "χz");
        // The example rounds χ to 0.69 before multiplying.
        Printed(Math.Round(chiZ, 2) * a * fy, "4186.2", "N_b,z,Rd = χz A fy [kN], with χ as printed");
    }

    static void JrcBeam()
    {
        // JRC 2014, Example 2: IPE 400 in S355, laterally braced, M_Ed = 114.3 kNm, V_Ed = 75.9 kN.
        // A = 84.46 cm², b = 180, h = 400, tf = 13.5, tw = 8.6, r = 21 mm, Wpl,y = 1307 cm³, Av = 42.69 cm².
        Section("Published: JRC 2014, Example 2, IPE 400 beam in S355");

        double eps = Ec3.Epsilon(355);
        double cWeb = 331.0 / 8.6, cFlange = ((180 - 8.6 - 2 * 21) / 2.0) / 13.5;
        Printed(cWeb, "38.49", "web c/t");
        Printed(cFlange, "4.79", "flange c/t");
        Check(Ec3.ClassInternal(cWeb, 0.5, -1.0, eps) == 1, "web in bending: class 1 (72ε = 58.3)");
        Check(Ec3.ClassOutstand(cFlange, eps) == 1, "flange: class 1");

        double fy = 355 * MPa;
        Printed(1307e-6 * fy, "464.0", "M_pl,Rd [kNm]");

        double vpl = Ec3.PlasticShear(42.69e-4, fy, 1.0);
        Printed(vpl, "875.0", "V_pl,Rd [kN]");
        Check(Ec3.ShearReduction(75.9, vpl) == 0.0, "V_Ed = 75.9 kN < 0.5 V_pl,Rd: no reduction of the moment resistance");
        Printed(373.0 / 8.6, "43.4", "hw/tw, below 72ε, so no shear buckling check");

        // A rolled I's shear area with its root radius, 6.2.6(3) a): A − 2b tf + (tw + 2r) tf.
        Printed((8446 - 2 * 180 * 13.5 + (8.6 + 2 * 21) * 13.5) / 100.0, "42.69", "Av [cm²] from 6.2.6(3) a)");
    }

    static void DesignersGuideBendingAndCompression()
    {
        // Gardner & Nethercot, Designers' Guide to EN 1993-1-1, Example 6.6 (as reproduced in the SDC
        // Verifier benchmarks): UKB 457x191x98 in S275 with fy = 265 N/mm² (tf = 19.6 mm > 16 mm),
        // N_Ed = 1400 kN. A = 12500 mm², b = 192.8, tf = 19.6, tw = 11.4, r = 10.2, h = 467.2,
        // Wpl,y = 2230 cm³.
        Section("Published: Designers' Guide Example 6.6, UKB 457x191x98, N + My");

        double eps = Ec3.Epsilon(265);
        Printed(eps, "0.94", "ε for fy = 265");
        double cFlange = ((192.8 - 11.4 - 2 * 10.2) / 2.0) / 19.6;
        double cWeb = (467.2 - 2 * 19.6 - 2 * 10.2) / 11.4;
        Printed(cFlange, "4.11", "flange c/t");
        Printed(cWeb, "35.75", "web c/t");
        Check(Ec3.ClassOutstand(cFlange, eps) == 1, "flange class 1 (9ε = 8.48)");
        Check(Ec3.ClassInternal(cWeb, 1.0, 1.0, eps) == 2, "web in compression class 2 (38ε = 35.78)");

        double fy = 265 * MPa, a = 12500e-6;
        double npl = a * fy, mpl = 2230e-6 * fy;
        Printed(npl, "3312.5", "N_pl,Rd [kN]");
        Printed(mpl, "590.95", "M_pl,y,Rd [kNm]");

        double n = 1400.0 / npl;
        Printed(n, "0.42", "n");
        double aw = (12500 - 2 * 192.8 * 19.6) / 12500.0;
        Printed(Ec3.ReducedMomentIMajor(mpl, n, aw), "425.3", "M_N,y,Rd = M_pl (1 − n)/(1 − 0.5a) [kNm]");
    }

    static void StructvilleBiaxial()
    {
        // Structville, "Design of steel columns for biaxial bending" (2022): UKC 254x254x89 in S275,
        // fy = 265, N_Ed = 1500 kN, My,Ed = 89.0 kNm, Mz,Ed = 7.9 kNm. Npl,Rd = 3003 kN,
        // Mpl,y,Rd = 324.3 kNm, Mpl,z,Rd = 152.5 kNm, n = 0.500, a = 0.217, MN,y,Rd = 182.1 kNm,
        // MN,z,Rd = 132.6 kNm, α = 2, β = 2.5, (6.41) = 0.240; λ̄z = 0.604, χz = 0.783, Nb,z,Rd = 2350.4 kN.
        Section("Published: Structville 2022, UKC 254x254x89, N + biaxial bending");

        double eps = Ec3.Epsilon(265);
        Check(Ec3.ClassInternal(19.45, 1.0, 1.0, eps) == 1, "web c/t 19.45 in compression: class 1 (33ε = 31.08)");
        Check(Ec3.ClassOutstand(6.38, eps) == 1, "flange c/t 6.38: class 1 (9ε = 8.48)");

        double n = 1500.0 / 3003.0;
        Printed(n, "0.500", "n");
        // The page prints a rounded to 0.217 but carries more; the major axis value is only reproduced
        // with n unrounded, and the minor with both as printed - so each is held to 0.1%.
        Close(Ec3.ReducedMomentIMajor(324.3, n, 0.2173), 182.1, 1e-3, "M_N,y,Rd [kNm]");
        Close(Ec3.ReducedMomentIMinor(152.5, 0.500, 0.217), 132.6, 1e-3, "M_N,z,Rd [kNm]");
        Printed(Math.Max(5 * n, 1.0), "2.50", "β = 5n");
        Printed(Ec3.Biaxial(89.0, 182.1, 2.0, 7.9, 132.6, 2.5), "0.240", "(6.41) left-hand side");

        double chi = Ec3.Reduction(0.604, Ec3.Imperfection(BucklingCurve.c));
        Printed(chi, "0.783", "χz for λ̄z = 0.604, curve c");
        Printed(chi * 3003.0, "2351", "N_b,z,Rd [kN] (printed 2350.4, from χ to more places)");
    }

    // ---------------------------------------------------------------- published: a whole member

    static void DesignersGuideTube()
    {
        // Designers' Guide Example 6.7: hot-finished CHS 244.5 x 10 in S355, pinned, L = 4 m,
        // N_Ed = 1630 kN. A = 7370 mm², I = 5073 cm⁴. d/t = 24.45, class 1; N_c,Rd = 2616 kN;
        // N_cr = 6571.7 kN, λ̄ = 0.63, curve a, Φ = 0.74, χ = 0.88, N_b,Rd = 2297 kN; 0.71.
        //
        // A tube's area and inertia follow from its diameter and wall exactly, so this one runs through
        // Alpaca4d's own section rather than the published properties.
        Section("Published: Designers' Guide Example 6.7, CHS 244.5 x 10 column, end to end");

        var chs = new CircleCS("CHS", 0.2445, 0.010, Steel("S355"));
        var steel = SteelSection.Of(chs, (SteelGrade)chs.Material.Grade, Fabrication.Rolled);

        Printed(steel.A * 1e6, "7367", "A [mm²] (the book's 7370 is rounded)");
        Printed(steel.Iz * 1e12 / 1e4, "5073", "I [cm⁴]");
        Check(steel.CurveY == BucklingCurve.a && steel.CurveZ == BucklingCurve.a, "hot finished: curve a");

        var forces = Enumerable.Range(0, 5).Select(_ => new SectionForces(-1630, 0, 0, 0, 0, 0)).ToList();
        var u = SteelCheck.Run(steel, forces, 4.0);

        Check(u.Class == 1, $"class 1 (d/t = 24.45 ≤ 50ε² = 33.1): got {u.Class}");
        Printed(steel.A * steel.Fy, "2615", "N_c,Rd [kN]");
        Printed(Ec3.CriticalForce(210e6, steel.Iz, 4.0), "6572", "N_cr [kN]");
        double lam = Ec3.Slenderness(steel.A, steel.Fy, Ec3.CriticalForce(210e6, steel.Iz, 4.0));
        Printed(lam, "0.63", "λ̄");
        Printed(Ec3.Reduction(lam, 0.21), "0.88", "χ");
        Close(1630 / u.BucklingY.Value, 2297, 2e-3, "N_b,Rd [kN], within the rounding of the book's area");
        Printed(u.BucklingY.Value, "0.71", "utilisation N_Ed / N_b,Rd");
        Close(u.Axial.Value, 1630 / (steel.A * steel.Fy), 1e-12, "and the cross-section, N_Ed / N_c,Rd");
        Check(u.Governing == "BucklingY" || u.Governing == "BucklingZ", $"buckling governs: {u.Governing}");
    }

    // ---------------------------------------------------------------- published: lateral-torsional buckling and 6.3.3

    static void EccsCriticalMoment()
    {
        // ECCS No. 119, Worked Example 5, as SOFiSTiK verification DCE-EN24 (2024) reproduces it: an
        // IPE 500 in S235, L = 3.75 m between fork supports, My,Ed = −100 kNm at both ends and 199 kNm
        // at mid-span (qz = 170 kN/m) at the shear centre. A = 115.5 cm², Iz = 2142 cm⁴, It = 88.57 cm⁴,
        // Iw = 1236e3 cm⁶, Wpl,y = 2194 cm³, E = 210000 and G = 81000 N/mm². Printed: N_cr,z = 3157 kN,
        // λ̄z = 0.927, χz = 0.644 (curve b); M_cr,0 = 895 kNm under uniform moment; C1 = 1.194
        // "determined by eigenvalue analysis" (1.200 in ECCS 119); λ̄LT = 0.695; χLT = 0.787 by the
        // general case, curve b. The example goes on with Annex A, which is not what this does.
        Section("Published: ECCS 119 Example 5 (SOFiSTiK DCE-EN24), IPE 500, critical moment");

        double e = 210e6, g = 81e6, iz = 2142e-8, it = 88.57e-8, iw = 1236e3 * 1e-12, l = 3.75;
        double fy = 235 * MPa, a = 115.5e-4, wpl = 2194e-6;

        double ncrZ = Ec3.CriticalForce(e, iz, l);
        Printed(ncrZ, "3157", "N_cr,z [kN]");
        double lamZ = Ec3.Slenderness(a, fy, ncrZ);
        Printed(lamZ, "0.927", "λ̄z");
        Printed(Ec3.Reduction(lamZ, Ec3.Imperfection(BucklingCurve.b)), "0.644", "χz, curve b");

        double mcr0 = CriticalMoment.Multiplier(l, 1.0, 0.0, 0.0, 0.0, 0.0, e, iz, iw, g, it);
        Printed(mcr0, "895", "M_cr,0 under a uniform moment [kNm]");

        // M(t) = −100 + 4 × 299 t (1 − t): the parabola through −100, 199 and −100 kNm.
        double mcr = CriticalMoment.Multiplier(l, -100.0, 1196.0, -1196.0, 170.0, 0.0, e, iz, iw, g, it) * 199.0;
        Close(mcr / mcr0, 1.194, 1e-3, "C1 of that diagram, against SOFiSTiK's eigenvalue analysis");

        double lamLT = Ec3.SlendernessLt(wpl, fy, mcr);
        Printed(lamLT, "0.695", "λ̄LT");
        Check(Ec3.CurveLtGeneral(true, true, 500.0 / 200.0) == BucklingCurve.b, "Table 6.4: rolled I with h/b = 2.5 > 2, curve b");
        Printed(Ec3.ReductionLt(lamLT, Ec3.Imperfection(BucklingCurve.b), 0.2, 1.0), "0.787", "χLT by the general case 6.3.2.2");
    }

    static void SchneiderBeamColumn()
    {
        // Schneider, Bautabellen für Ingenieure (21st edition, 2014), Example 8-41, as SOFiSTiK
        // verification DCE-EN29 (2024) reproduces it: an HEA 200 column in S235, 8 m, pinned at both
        // ends and held sideways at mid-length, N_Ed = 300 kN and q = 4 kN/m on the top flange
        // (zp = −9.5 cm), γM1 = 1.1. A fy = 1264.3 kN, Wpl,y fy = 100.9 kNm, iy = 8.28 cm, iz = 4.98 cm,
        // Iz = 1340 cm⁴. Printed: λ̄y = 1.029, χy = 0.58 (curve b); λ̄z = 0.855, χz = 0.63 (curve c);
        // N_cr,z = 1736 kN; M_cr = 220.9 kNm, λ̄LT = 0.676, curve b, χLT = 0.88 by 6.3.2.3; Cmy = 0.95,
        // CmLT = 0.80; kyy = 1.292, kzy = 0.936; (6.61) 0.96 and (6.62) 0.79.
        Section("Published: Schneider 8-41 (SOFiSTiK DCE-EN29), HEA 200 beam-column, Annex B");

        double lamY = 800 / (8.28 * 93.9), lamZ = 400 / (4.98 * 93.9);
        Printed(lamY, "1.029", "λ̄y = L_cr / (iy λ1)");
        Printed(lamZ, "0.855", "λ̄z, held at mid-length");
        Printed(Ec3.Reduction(lamY, Ec3.Imperfection(BucklingCurve.b)), "0.58", "χy, curve b");
        Printed(Ec3.Reduction(lamZ, Ec3.Imperfection(BucklingCurve.c)), "0.63", "χz, curve c");
        Printed(Ec3.CriticalForce(21000, 1340, 400), "1736", "N_cr,z [kN], in kN and cm");

        double lamLT = Math.Sqrt(100.9 / 220.9);
        Printed(lamLT, "0.676", "λ̄LT = √(Wpl fy / M_cr)");
        Check(Ec3.CurveLtRolled(true, 190.0 / 200.0) == BucklingCurve.b, "Table 6.5: rolled, h/b = 0.95 ≤ 2, curve b");
        Printed(Ec3.ReductionLt(lamLT, Ec3.Imperfection(BucklingCurve.b), 0.4, 0.75), "0.88", "χLT by 6.3.2.3, λ̄LT,0 = 0.4, β = 0.75");

        // Cmy over the whole span: nothing at the ends, 32 kNm at mid-span. CmLT over each half: nothing
        // at the support, 32 kNm at the restraint and 24 kNm half way between.
        double cmy = Ec3.EquivalentMoment(0.0, 1.0, 32.0), cmLT = Ec3.EquivalentMoment(32.0, 0.0, 24.0);
        Printed(cmy, "0.95", "Cmy, Table B.3 with αh = 0");
        Printed(cmLT, "0.80", "CmLT, Table B.3 with αs = 0.75");

        // The example carries on with χ as printed.
        double ny = 300 * 1.1 / (0.58 * 1264.3), nz = 300 * 1.1 / (0.63 * 1264.3);
        var k = Ec3.AnnexB(true, 1.029, 0.855, ny, nz, cmy, 1.0, cmLT, true, SteelShape.I);
        Printed(k.Kyy, "1.292", "kyy, capped at Cmy (1 + 0.8 ny)");
        Printed(k.Kzy, "0.936", "kzy, Table B.2");
        double my = 32.0 / (0.88 * 100.9 / 1.1);
        Printed(ny + k.Kyy * my, "0.96", "(6.61)");
        Printed(nz + k.Kzy * my, "0.79", "(6.62)");

        // End to end, through Alpaca4d's own HEA 200 - drawn without its root radii, so A is 5105 mm²
        // against 5380 and Wpl 5% low, and the member reads a few percent worse than the book's, on the
        // safe side. M_cr is the energy solution for each 4 m half between fork supports: 256 kNm, above
        // the book's 220.9, whose load height term is DIN 18800's fixed 0.5 zp, the same for every
        // diagram - where in each half most of the moment comes from the reaction, not from the load
        // the top flange carries. At the shear centre the two agree: C1 1.336 here, ζ 1.35 there.
        var hea = new ISection("HEA200", 0.190, 0.200, 0.010, 0.200, 0.010, 0.0065, Steel("S235"));
        var s = SteelSection.Of(hea, (SteelGrade)hea.Material.Grade, Fabrication.Rolled);
        var forces = new List<SectionForces>
        {
            new SectionForces(-300, 16, 0, 0, 0, 0),
            new SectionForces(-300, 8, 0, 0, 0, 24),
            new SectionForces(-300, 0, 0, 0, 0, 32),
            new SectionForces(-300, -8, 0, 0, 0, 24),
            new SectionForces(-300, -16, 0, 0, 0, 0),
        };
        var member = new SteelMember(8.0) { BucklingLengthY = 4.0, LateralTorsionalLength = 4.0, Load = LoadPosition.Destabilising };
        var u = SteelCheck.Run(s, forces, member, 1.0, 1.1);

        Check(u.Report.Contains("Cmy = 0.95,") && u.Report.Contains("CmLT = 0.8,"), "Cmy and CmLT from the member's own diagram, as the book has them");
        Check(u.Report.Contains("C1 = 1.336"), "C1 of each half, at the shear centre, 1.336 against the book's ζ = 1.35");
        Close(u.InteractionZ.Value, 0.96, 0.06, "(6.61), InteractionZ for an I, against the book's 0.96");
        Check(u.InteractionZ.Value >= 0.96, "and above it, as the smaller section has it");
        Close(u.InteractionY.Value, 0.79, 0.06, "(6.62), InteractionY, against the book's 0.79");
        Check(u.Governing == "InteractionZ", $"(6.61) governs: {u.Governing}");
    }

    static void CriticalMoments()
    {
        // The energy solution against what it can be held to exactly, and against the coefficients
        // everyone uses. An IPE 300 as the catalogue has it, kN and m.
        Section("Critical moment: closed forms and published coefficients");

        double e = 210e6, g = 80.77e6, iz = 603.8e-8, it = 20.12e-8, iw = 125.9e-9;
        foreach (double l in new[] { 3.0, 6.0, 10.0 })
        {
            double exact = Math.PI / l * Math.Sqrt(e * iz * g * it * (1 + Math.PI * Math.PI * e * iw / (g * it * l * l)));
            double mcr = CriticalMoment.Multiplier(l, 1.0, 0.0, 0.0, 0.0, 0.0, e, iz, iw, g, it);
            Close(mcr, exact, 1e-9, $"L = {l} m, uniform moment: (π/L) √(E Iz G It (1 + π² E Iw / G It L²))");
        }

        double l6 = 6.0, uniform = CriticalMoment.Multiplier(l6, 1.0, 0.0, 0.0, 0.0, 0.0, e, iz, iw, g, it);

        // A uniform load, M(t) = 4 t (1 − t) at its peak of 1: C1 = 1.127 (ENV 1993-1-1 Table F.1.2),
        // 1.13 (NCCI SN003).
        double q = 8.0 / (l6 * l6);
        double udl = CriticalMoment.Multiplier(l6, 0.0, 4.0, -4.0, q, 0.0, e, iz, iw, g, it);
        Close(udl / uniform, 1.127, 5e-3, "uniform load at the shear centre: C1");

        // End moments M and M/2: C1 = 1.323 (ENV Table F.1.1), 1.31 (SN003).
        Close(CriticalMoment.Multiplier(l6, 1.0, -0.5, 0.0, 0.0, 0.0, e, iz, iw, g, it) / uniform, 1.323, 1e-2, "ψ = 0.5: C1");

        // On the top flange, against the three-factor formula with C1 = 1.127, C2 = 0.454.
        double zg = 0.15, pz = Math.PI * Math.PI * e * iz / (l6 * l6);
        double three = 1.127 * pz * (Math.Sqrt(iw / iz + l6 * l6 * g * it / (Math.PI * Math.PI * e * iz) + Math.Pow(0.454 * zg, 2)) - 0.454 * zg);
        double top = CriticalMoment.Multiplier(l6, 0.0, 4.0, -4.0, q, zg, e, iz, iw, g, it);
        Close(top, three, 1e-2, "uniform load on the top flange, against C1 = 1.127, C2 = 0.454");
        Check(CriticalMoment.Multiplier(l6, 0.0, 4.0, -4.0, q, -zg, e, iz, iw, g, it) > udl, "and hung from the bottom flange, higher");

        // Reversed end moments with a top-flange load between them, the hardest case tried: the terms
        // the check uses against thirty.
        double terms = CriticalMoment.Multiplier(l6, -1.0, 6.0, -6.0, 12.0 / (l6 * l6), zg, e, iz, iw, g, it);
        double thirty = CriticalMoment.Multiplier(l6, -1.0, 6.0, -6.0, 12.0 / (l6 * l6), zg, e, iz, iw, g, it, 0.0, 30);
        Close(terms, thirty, 1e-4, $"{CriticalMoment.Terms} sine terms hold M_cr to 0.01%");
        Close(CriticalMoment.Multiplier(l6, 1.0, -2.0, 0.0, 0.0, 0.0, e, iz, iw, g, it),
              CriticalMoment.Multiplier(l6, 1.0, -2.0, 0.0, 0.0, 0.0, e, iz, iw, g, it, 0.0, 30), 1e-4, "and for ψ = −1");

        Check(double.IsPositiveInfinity(CriticalMoment.Multiplier(l6, 0.0, 0.0, 0.0, 0.0, 0.0, e, iz, iw, g, it)), "no moment, no buckling");
    }

    // ---------------------------------------------------------------- Alpaca4d's shapes

    static void IShape()
    {
        Section("Shapes: an IPE 300 drawn without root radii, S355");

        double h = 0.300, b = 0.150, tf = 0.0107, tw = 0.0071, hw = h - 2 * tf;
        var ipe = new ISection("IPE300", h, b, tf, b, tf, tw, Steel("S355"));
        var s = SteelSection.Of(ipe, (SteelGrade)ipe.Material.Grade, Fabrication.Rolled);

        double a = 2 * b * tf + hw * tw;
        Close(s.A, a, 1e-12, "A");
        Close(s.WplZ, b * tf * (h - tf) + tw * hw * hw / 4, 1e-9, "Wpl about the major axis, b tf (h − tf) + tw hw²/4");
        Close(s.WplY, 2 * tf * b * b / 4 + hw * tw * tw / 4, 1e-9, "Wpl about the minor axis");
        Close(s.WelZ, (b * h * h * h - (b - tw) * hw * hw * hw) / 12 / (h / 2), 1e-9, "Wel about the major axis");
        Close(s.AvY, a - 2 * b * tf + tw * tf, 1e-12, "Av for Vy: rolled, A − 2b tf + tw tf (no root radius)");
        Close(s.AvZ, a - hw * tw, 1e-12, "Av for Vz: A − hw tw");
        Close(s.WvZ, tw * hw * hw / 4, 1e-9, "the web's share of Wpl, which 6.2.8 takes away: Aw²/4tw");
        Check(s.Interaction == PlasticInteraction.DoublySymmetricI, "doubly symmetric: (6.36) to (6.38)");
        Close(s.InteractionAZ, (a - 2 * b * tf) / a, 1e-12, "a = (A − 2b tf)/A");
        Check(s.CurveZ == BucklingCurve.a && s.CurveY == BucklingCurve.b, "h/b = 2 > 1.2, tf ≤ 40 mm: curve a major, b minor");

        var welded = SteelSection.Of(ipe, (SteelGrade)ipe.Material.Grade, Fabrication.Welded);
        Check(welded.CurveZ == BucklingCurve.b && welded.CurveY == BucklingCurve.c, "welded: b major, c minor");
        Close(welded.AvY, hw * tw, 1e-12, "welded Av for Vy: η hw tw");

        Section("Classification of the IPE 300, S355 (ε = 0.814)");
        double eps = s.Epsilon;
        Check(SteelCheck.Classify(s, new SectionForces(0, 0, 0, 0, 0, 100), 1.0, true, out _, out _) == 1,
              $"pure major axis bending: class 1 (web c/t {hw / tw:0.0} ≤ 72ε, flange {(b - tw) / 2 / tf:0.00} ≤ 9ε)");

        // Pure compression: web c/t 39.2 > 42ε = 34.2. A rolled IPE 300 in S355 is Class 4 in
        // compression too (its c/t is 35.0 with the root radius), so this is not an artefact.
        Check(SteelCheck.Classify(s, new SectionForces(-1000, 0, 0, 0, 0, 0), 1.0, false, out var part, out _) == 4 && part.Name == "web",
              "pure compression at full strength: class 4, the web, by Table 5.2 alone");

        // ...unless the compression is low: 5.5.2(9) raises ε by √(fy/σ). At 50 kN the web carries
        // 50/A = 9.6 N/mm², ε' = ε √(355/9.6) = 4.95, and 42ε' is far beyond 39.2.
        Check(SteelCheck.Classify(s, new SectionForces(-50, 0, 0, 0, 0, 0), 1.0, true, out _, out bool relaxed) == 3 && relaxed,
              "low compression: class 3 by 5.5.2(9)");

        // N + M: α = 0.5 + N/(2 fy tw hw). At N = 0.2 Npl, α = 0.762: class 1 needs c/t ≤ 396ε/(13α − 1)
        // = 36.2, class 2 ≤ 456ε/(13α − 1) = 41.7. The web is 39.2, so class 2.
        double npl = a * s.Fy;
        double alpha = 0.5 + 0.2 * npl / (2 * s.Fy * tw * hw);
        Close(alpha, 0.762, 2e-3, "α under 0.2 N_pl and bending");
        Check(SteelCheck.Classify(s, new SectionForces(-0.2 * npl, 0, 0, 0, 0, 0.5 * s.WplZ * s.Fy), 1.0, true, out _, out _) == 2,
              "0.2 N_pl with half the plastic moment: class 2, set by α");
        Check(Ec3.ClassInternal(hw / tw, alpha, -0.5, eps) == 2, "and Table 5.2 itself says 2 for that α");
    }

    static void UnequalI()
    {
        Section("Shapes: an I with unequal flanges");

        var lop = new ISection("lop", 0.30, 0.20, 0.015, 0.12, 0.010, 0.008, Steel("S355"));
        var s = SteelSection.Of(lop, (SteelGrade)lop.Material.Grade, Fabrication.Rolled);

        // Plastic neutral axis: half the area, 4600 mm², above it. Top flange 3000, so it sits in the
        // web 1600/8 = 200 mm below the top flange's underside: 0.15 − 0.015 − 0.2 = −0.065 from mid-depth.
        double aTop = 0.20 * 0.015, aWeb = 0.008 * 0.275, aBot = 0.12 * 0.010, a = aTop + aWeb + aBot;
        double ypna = 0.135 - (a / 2 - aTop) / 0.008;
        Close(s.Stress.PlasticNeutralY + s.Stress.CentroidY, ypna, 1e-9, "plastic neutral axis, from mid-depth");

        double wpl = aTop * (0.1425 - ypna) + 0.008 * (0.135 - ypna) * (0.135 - ypna) / 2
                   + 0.008 * (ypna + 0.140) * (ypna + 0.140) / 2 + aBot * (ypna + 0.145);
        Close(s.WplZ, wpl, 1e-9, "Wpl about the major axis, about that axis");
        Check(s.Interaction == PlasticInteraction.Linear, "no reduced plastic moment in 6.2.9.1: linear");
        Check(s.CurveZ == BucklingCurve.b && s.CurveY == BucklingCurve.c, "welded curves whatever the input says - nobody rolls one");
    }

    static void BoxAndTube()
    {
        Section("Shapes: RHS 200 x 100 x 10 and CHS 168.3 x 5, S355");

        double h = 0.200, w = 0.100, t = 0.010;
        var rhs = new RectangleHollowCS("RHS", w, h, t, t, t, Steel("S355"));
        var s = SteelSection.Of(rhs, (SteelGrade)rhs.Material.Grade, Fabrication.Rolled);

        double a = w * h - (w - 2 * t) * (h - 2 * t);
        Close(s.AvY, a * h / (w + h), 1e-12, "Av parallel to the depth, A h/(b + h)");
        Close(s.AvZ, a * w / (w + h), 1e-12, "Av parallel to the width, A b/(b + h)");
        Check(s.Interaction == PlasticInteraction.UniformBox, "uniform walls: (6.39) and (6.40)");
        Close(s.InteractionAZ, (a - 2 * w * t) / a, 1e-12, "aw = (A − 2bt)/A for the major axis");
        Close(s.InteractionAY, (a - 2 * h * t) / a, 1e-12, "af = (A − 2ht)/A for the minor axis");
        Check(s.CurveY == BucklingCurve.a, "hot finished: curve a");
        var cold = SteelSection.Of(rhs, (SteelGrade)rhs.Material.Grade, Fabrication.Welded);
        Check(cold.CurveY == BucklingCurve.c, "cold formed: curve c");

        // Major axis bending puts the top wall in compression: c/t = (b − 3t)/t = 7 ≤ 33ε. The side walls
        // in bending, (h − 3t)/t = 17 ≤ 72ε. Class 1.
        Check(SteelCheck.Classify(s, new SectionForces(0, 0, 0, 0, 0, 50), 1.0, true, out _, out _) == 1, "major axis bending: class 1");

        var thin = new RectangleHollowCS("thin", 0.400, 0.400, 0.004, 0.004, 0.004, Steel("S355"));
        var st = SteelSection.Of(thin, (SteelGrade)thin.Material.Grade, Fabrication.Rolled);
        // (400 − 12)/4 = 97 > 42ε = 34.2 in compression...
        Check(SteelCheck.Classify(st, new SectionForces(-5000, 0, 0, 0, 0, 0), 1.0, false, out _, out _) == 4, "400 x 400 x 4 in compression: class 4");
        // ...and in bending the compressed flange too: the same 97.
        Check(SteelCheck.Classify(st, new SectionForces(0, 0, 0, 0, 0, 200), 1.0, false, out var flange, out _) == 4 && flange.Name == "top wall",
              "and in bending, set by the compressed top wall");

        var chs = new CircleCS("CHS", 0.1683, 0.005, Steel("S355"));
        var sc = SteelSection.Of(chs, (SteelGrade)chs.Material.Grade, Fabrication.Rolled);
        // d/t = 33.7: above 50ε² = 33.1, within 70ε² = 46.3. Class 2.
        Check(SteelCheck.Classify(sc, new SectionForces(-100, 0, 0, 0, 0, 0), 1.0, true, out _, out _) == 2, "CHS d/t 33.7 in compression: class 2");
        Check(SteelCheck.Classify(sc, new SectionForces(100, 0, 0, 0, 0, 0), 1.0, true, out _, out _) == 1, "and in pure tension, nothing to buckle: class 1");
        Close(sc.AvY, 2 * sc.A / Math.PI, 1e-12, "Av = 2A/π");
    }

    static void Grades()
    {
        Section("Grades: names and Table 3.1");

        Check(SteelGrade.Find("S355")?.FyMPa == 355, "S355 is 355 N/mm²");
        Check(SteelGrade.Find("s355 j2")?.Name == "S355" && SteelGrade.Find("S355JR")?.Name == "S355" && SteelGrade.Find("S 275")?.Name == "S275",
              "EN 10025 spellings find the grade");
        Check(SteelGrade.Find("Steel") == null && SteelGrade.Find("C24") == null && SteelGrade.Find("") == null, "anything else finds nothing");

        var s355 = SteelGrade.Find("S355");
        Check(s355.YieldStrengthMPa(40, out _) == 355 && s355.YieldStrengthMPa(40.1, out _) == 335, "Table 3.1: 355 up to 40 mm, 335 above");
        s355.YieldStrengthMPa(90, out bool beyond);
        Check(beyond, "past 80 mm, Table 3.1 runs out and says so");

        var thick = new ISection("HL", 1.0, 0.4, 0.045, 0.4, 0.045, 0.025, Steel("S355"));
        Check(SteelSection.Of(thick, s355, Fabrication.Rolled).FyMPa == 335, "an I with 45 mm flanges gets fy = 335");

        var named = new UniaxialMaterialElastic("S275", 210e6, 210e6, 0, 80e6, 0.3, 7850);
        Check(DesignGrades.Of(named)?.Name == "S275", "a hand-made material named S275 is S275");
        var unnamed = new UniaxialMaterialElastic("my steel", 210e6, 210e6, 0, 80e6, 0.3, 7850);
        Check(DesignGrades.Of(unnamed) == null, "one named anything else has no grade");
        unnamed.Grade = s355;
        Check(DesignGrades.Of(unnamed)?.Name == "S355", "unless it carries one");
    }

    // ---------------------------------------------------------------- whole members, by hand

    static void Members()
    {
        Section("Members: an IPE 300 beam in S355, by hand");

        double h = 0.300, b = 0.150, tf = 0.0107, tw = 0.0071, hw = h - 2 * tf;
        var ipe = new ISection("IPE300", h, b, tf, b, tf, tw, Steel("S355"));
        var s = SteelSection.Of(ipe, (SteelGrade)ipe.Material.Grade, Fabrication.Rolled);
        double fy = s.Fy, a = s.A;
        double mpl = s.WplZ * fy, vpl = s.AvY * fy / Math.Sqrt(3);

        // A simply supported beam under a uniform load: shear at the ends, moment at mid-span.
        double mMax = 120.0, vMax = 80.0;
        var udl = new List<SectionForces>
        {
            new SectionForces(0, vMax, 0, 0, 0, 0),
            new SectionForces(0, vMax / 2, 0, 0, 0, 0.75 * mMax),
            new SectionForces(0, 0, 0, 0, 0, mMax),
            new SectionForces(0, -vMax / 2, 0, 0, 0, 0.75 * mMax),
            new SectionForces(0, -vMax, 0, 0, 0, 0),
        };
        var u = SteelCheck.Run(s, udl, 6.0, 1.0, 1.0, new[] { 0, 0.25, 0.5, 0.75, 1.0 });
        Check(u.Checked && u.Class == 1, $"class 1, checked: {u.Class}");
        Close(u.BendingZ.Value, mMax / mpl, 1e-12, "BendingZ = M / (Wpl fy) at mid-span");
        Close(u.ShearY.Value, vMax / vpl, 1e-12, "ShearY = V / (Av fy/√3) at the support");
        Close(u.Combined.Value, mMax / mpl, 1e-12, "Combined, with no axial force, is the bending");
        Check(u.BucklingY == 0.0 && u.BucklingZ == 0.0, "no compression, no buckling");
        Check(u.Governing == "LTB", $"unrestrained over 6 m, lateral-torsional buckling governs: {u.Governing}");
        Check(u.Report.Contains("x = 0.5 L") && u.Report.Contains("Not checked: torsional and torsional-flexural buckling"),
              "the report says where, and what it does not cover");

        // Held sideways every 0.5 m, M_Ed / M_cr ≤ λ̄LT,0² and 6.3.2.2(4) lets buckling be ignored: what
        // is left is the cross-section, at γM1.
        var held = SteelCheck.Run(s, udl, new SteelMember(6.0) { LateralTorsionalLength = 0.5 }, 1.0, 1.0, new[] { 0, 0.25, 0.5, 0.75, 1.0 });
        Close(held.LateralTorsional.Value, held.BendingZ.Value, 1e-9, "held sideways every 0.5 m: LTB is the bending check");

        // With the report off nothing is written, and nothing else changes.
        var quiet = SteelCheck.Run(s, udl, 6.0, 1.0, 1.0, new[] { 0, 0.25, 0.5, 0.75, 1.0 }, report: false);
        Check(quiet.Report == "" && quiet.Checks.Zip(u.Checks, (off, on) => off.Value == on.Value).All(x => x)
              && quiet.Class == u.Class && quiet.Max == u.Max,
              "with the report off: no text, and every utilisation the same");

        // High shear with moment at the same section: 6.2.8 (6.30), My,V,Rd = (Wpl − ρ Aw²/4tw) fy.
        double v = 0.8 * vpl, m = 100.0;
        double rho = Math.Pow(2 * 0.8 - 1, 2);
        var shear = SteelCheck.Run(s, new List<SectionForces> { new SectionForces(0, v, 0, 0, 0, m) }, 6.0);
        Close(shear.BendingZ.Value, m / ((s.WplZ - rho * hw * tw * hw * tw / (4 * tw)) * fy), 1e-12,
              "V = 0.8 V_pl: M_V,Rd = (Wpl − ρ Aw²/4tw) fy, ρ = 0.36 (6.30)");

        // Axial force and major axis moment: (6.36), n = N/Npl, a = (A − 2b tf)/A.
        double n = 0.3, aw = (a - 2 * b * tf) / a;
        double mn = mpl * (1 - n) / (1 - 0.5 * aw);
        var nm = SteelCheck.Run(s, new List<SectionForces> { new SectionForces(n * a * fy, 0, 0, 0, 0, 0.6 * mn) }, 6.0);
        Close(nm.Combined.Value, 0.6, 1e-9, "tension 0.3 Npl with 0.6 M_N,y,Rd: Combined = 0.6");

        // Torsion: τt from Beam Stresses against fy/√3; V_pl,T,Rd = V_pl √(1 − τ/(1.25 fy/√3)).
        double torque = 0.5;
        var tor = SteelCheck.Run(s, new List<SectionForces> { new SectionForces(0, 50, 0, torque, 0, 0) }, 6.0);
        double tau = s.Stress.Torsion * torque, tauRd = fy / Math.Sqrt(3);
        Close(tor.Torsion.Value, tau / tauRd, 1e-12, "Torsion = τt / (fy/√3)");
        Close(tor.ShearY.Value, 50 / (vpl * Math.Sqrt(1 - tau / (1.25 * tauRd))), 1e-12, "ShearY with V_pl,T,Rd from 6.2.7(9)");

        Section("Members: an HE 300 B-like column, S355, L = 5 m");

        // h = b = 300, tf = 19, tw = 11: web (262/11 = 23.8 ≤ 33ε) class 1, flange (144.5/19 = 7.6) class 2.
        var hea = new ISection("HE300B", 0.300, 0.300, 0.019, 0.300, 0.019, 0.011, Steel("S355"));
        var c = SteelSection.Of(hea, (SteelGrade)hea.Material.Grade, Fabrication.Rolled);
        double nEd = 1500;
        var col = SteelCheck.Run(c, Enumerable.Range(0, 5).Select(_ => new SectionForces(-nEd, 0, 0, 0, 0, 0)).ToList(), 5.0);
        Check(col.Class == 2, $"class 2, set by the flange outstand: {col.Class}");

        double ncrY = Math.PI * Math.PI * 210e6 * c.Iy / 25.0;
        double lam = Math.Sqrt(c.A * c.Fy / ncrY);
        double phi = 0.5 * (1 + 0.49 * (lam - 0.2) + lam * lam);
        double chi = 1 / (phi + Math.Sqrt(phi * phi - lam * lam));
        Check(c.CurveY == BucklingCurve.c, "h/b = 1 ≤ 1.2: curve c about the minor axis");
        Close(col.BucklingY.Value, nEd / (chi * c.A * c.Fy), 1e-12, "BucklingY = N / (χ A fy), χ from curve c worked by hand");
        Check(col.BucklingY > col.BucklingZ, "the minor axis governs");

        // The same column with next to no load: N/Ncr ≤ 0.04, buckling may be ignored.
        var light = SteelCheck.Run(c, new List<SectionForces> { new SectionForces(-10, 0, 0, 0, 0, 0) }, 5.0);
        Close(light.BucklingY.Value, 10 / (c.A * c.Fy), 1e-12, "N ≤ 0.04 Ncr: χ = 1 (6.3.1.2(4))");

        Section("Members: an IPE 300 column, S355 - Class 4 in compression");

        // 1200 kN puts 231 N/mm² in the web. 5.5.2(9) raises ε to 0.814 √(355/231) = 1.01, and 42ε' = 42.3
        // clears the web's 39.2: the cross-section is Class 3 at that stress and is checked. Buckling is
        // not - 5.5.2(10) classifies it by Table 5.2 alone, Class 4, which needs A_eff.
        var slender = SteelCheck.Run(s, Enumerable.Range(0, 5).Select(_ => new SectionForces(-1200, 0, 0, 0, 0, 0)).ToList(), 4.0);
        Check(slender.Checked && slender.Class == 3, $"heavily loaded: the cross-section is Class 3 by 5.5.2(9): {slender.Class}");
        Close(slender.Axial.Value, 1200 / (s.A * s.Fy), 1e-12, "and its axial check is made");
        Check(!slender.BucklingY.HasValue && !slender.BucklingZ.HasValue, "buckling is not: Class 4 by Table 5.2");
        Check(slender.Max == null && slender.Governing.StartsWith("Not checked: BucklingY"),
              $"so there is no Max to read as a pass, and Governing says why: {slender.Governing}");
        Check(slender.BucklingClass == 4 && slender.LowStressClass3,
              "and the result says why: Class 4 for buckling, Class 3 by 5.5.2(9) for the cross-section");

        var crushed = SteelCheck.Run(s, Enumerable.Range(0, 5).Select(_ => new SectionForces(-1800, 0, 0, 0, 0, 0)).ToList(), 4.0);
        Check(!crushed.Checked && crushed.Class == 4 && crushed.Max == null,
              "at 1800 kN, 347 N/mm², even 5.5.2(9) leaves the web Class 4: nothing checked");

        var plain = SteelCheck.Run(s, new List<SectionForces> { new SectionForces(0, 0, 0, 0, 0, 100) }, 4.0);
        Check(plain.BucklingClass == 1 && !plain.LowStressClass3, "a beam in plain bending: nothing to flag");

        var lightBeam = SteelCheck.Run(s, new List<SectionForces> { new SectionForces(-20, 0, 0, 0, 0, 100) }, 4.0);
        Check(lightBeam.Checked && lightBeam.BucklingY.HasValue,
              $"a beam with 20 kN of compression is checked, and with N ≤ 0.04 Ncr its buckling too: class {lightBeam.Class}");
    }

    /// <summary>χLT of 6.3.2.3 by hand: λ̄LT,0 = 0.4, β = 0.75, at most 1 and 1/λ̄².</summary>
    static double ChiLtRolled(double lam, double alpha)
    {
        double phi = 0.5 * (1 + alpha * (lam - 0.4) + 0.75 * lam * lam);
        double chi = 1 / (phi + Math.Sqrt(phi * phi - 0.75 * lam * lam));
        return Math.Min(1.0, Math.Min(chi, 1 / (lam * lam)));
    }

    /// <summary>M_cr under a uniform moment between fork supports, doubly symmetric, in closed form.</summary>
    static double McrUniform(double e, double g, double iLat, double it, double iw, double l)
    {
        return Math.PI / l * Math.Sqrt(e * iLat * g * it * (1 + Math.PI * Math.PI * e * iw / (g * it * l * l)));
    }

    static void LateralTorsionalMembers()
    {
        Section("Members: lateral-torsional buckling of an IPE 300 in S355, by hand");

        double h = 0.300, b = 0.150, tf = 0.0107, tw = 0.0071, hw = h - 2 * tf;
        var ipe = new ISection("IPE300", h, b, tf, b, tf, tw, Steel("S355"));
        var s = SteelSection.Of(ipe, (SteelGrade)ipe.Material.Grade, Fabrication.Rolled);
        double e = 210e6, g = 80.77e6, fy = s.Fy, w = s.WplZ;

        double it = (2 * b * tf * tf * tf + hw * tw * tw * tw) / 3;
        double iw = tf * b * b * b / 12 * (h - tf) * (h - tf) / 2;
        Close(s.It, it, 1e-12, "It = (2 b tf³ + hw tw³)/3 with sharp corners - not ISection.J, which is Iy + Iz");
        Close(s.Iw, iw, 1e-12, "Iw = If (h − tf)²/2, If = tf b³/12");
        Check(s.LtRolledMethod && s.CurveLT == BucklingCurve.b, "rolled, h/b = 2: 6.3.2.3, curve b (Table 6.5)");
        Check(s.MajorIsZ && s.LateralInertia == s.Iy, "bending about z buckles sideways against Iy");

        // A uniform moment over 6 m between fork supports: M_cr in closed form, then 6.3.2.3 by hand.
        double m = 80.0;
        var uniform = Enumerable.Range(0, 5).Select(_ => new SectionForces(0, 0, 0, 0, 0, m)).ToList();
        double mcr = McrUniform(e, g, s.Iy, it, iw, 6.0);
        double chi = ChiLtRolled(Math.Sqrt(w * fy / mcr), 0.34);
        var u = SteelCheck.Run(s, uniform, 6.0);
        Close(u.LateralTorsional.Value, m / (chi * w * fy), 1e-9, $"uniform moment: M / (χLT Wpl fy), M_cr = {mcr:0.0} kNm in closed form, χLT = {chi:0.000}");
        Check(u.Governing == "LTB", $"and it governs: {u.Governing}");

        // Longer than the member - a beam that is only part of an unrestrained length - its peak is
        // taken as uniform over all of L_LT.
        var longer = SteelCheck.Run(s, uniform, new SteelMember(6.0) { LateralTorsionalLength = 9.0 });
        double chi9 = ChiLtRolled(Math.Sqrt(w * fy / McrUniform(e, g, s.Iy, it, iw, 9.0)), 0.34);
        Close(longer.LateralTorsional.Value, m / (chi9 * w * fy), 1e-9, "L_LT = 9 m on a 6 m member: its peak, uniform over 9 m");

        // The uniform load of the Members section: where it acts, and how often the beam is held.
        double mMax = 120.0, vMax = 80.0;
        var udl = new List<SectionForces>
        {
            new SectionForces(0, vMax, 0, 0, 0, 0),
            new SectionForces(0, vMax / 2, 0, 0, 0, 0.75 * mMax),
            new SectionForces(0, 0, 0, 0, 0, mMax),
            new SectionForces(0, -vMax / 2, 0, 0, 0, 0.75 * mMax),
            new SectionForces(0, -vMax, 0, 0, 0, 0),
        };
        Func<SteelMember, double> At = member => SteelCheck.Run(s, udl, member).LateralTorsional.Value;
        double centre = At(new SteelMember(6.0));
        double onTop = At(new SteelMember(6.0) { Load = LoadPosition.Destabilising });
        double hung = At(new SteelMember(6.0) { Load = LoadPosition.Stabilising });
        Check(onTop > centre && centre > hung, $"on the top flange {onTop:0.000}, at the shear centre {centre:0.000}, hung below {hung:0.000}");

        // The load goes towards −y here (Mz'' < 0), so on the top flange is on +y: flipping the load
        // and the diagram together must change nothing, for a doubly symmetric section.
        var flipped = udl.Select(f => new SectionForces(f.N, -f.Vy, f.Vz, f.T, f.My, -f.Mz)).ToList();
        Close(SteelCheck.Run(s, flipped, new SteelMember(6.0) { Load = LoadPosition.Destabilising }).LateralTorsional.Value, onTop, 1e-9,
              "the same load the other way, on the other flange: the same");

        double third = At(new SteelMember(6.0) { LateralTorsionalLength = 2.0 });
        double half = At(new SteelMember(6.0) { LateralTorsionalLength = 3.0 });
        Check(centre > half && half > third, $"held at mid-span {half:0.000}, at the third points {third:0.000}");

        Section("Members: what cannot buckle sideways");

        var chs = new CircleCS("CHS", 0.1683, 0.005, Steel("S355"));
        var tube = SteelCheck.Run(SteelSection.Of(chs, (SteelGrade)chs.Material.Grade, Fabrication.Rolled),
                                  Enumerable.Range(0, 5).Select(_ => new SectionForces(0, 0, 0, 0, 0, 20)).ToList(), 6.0);
        Check(tube.LateralTorsional == 0.0 && tube.Max.HasValue, "a tube: LTB is zero, and the member is checked");

        // An RHS bent about its strong axis can still buckle sideways, if it is long and narrow enough:
        // Bredt's It, no warping, and the general case 6.3.2.2 with curve d, by hand.
        var rhs = new RectangleHollowCS("RHS", 0.100, 0.200, 0.010, 0.010, 0.010, Steel("S355"));
        var r = SteelSection.Of(rhs, (SteelGrade)rhs.Material.Grade, Fabrication.Rolled);
        double am = (0.100 - 0.010) * (0.200 - 0.010);
        Close(r.It, 4 * am * am / (2 * 0.190 / 0.010 + 2 * 0.090 / 0.010), 1e-12, "It = 4 Am² / ∮ds/t");
        Check(r.MajorIsZ && !r.TorsionallyDeformable && r.Iw == 0.0 && r.CurveLT == BucklingCurve.d && !r.LtRolledMethod,
              "stiffer about z, closed (Table B.1), no warping, the general case with curve d");
        double rMcr = Math.PI / 6.0 * Math.Sqrt(e * r.Iy * g * r.It);
        double rLam = Math.Sqrt(r.WplZ * r.Fy / rMcr);
        double rPhi = 0.5 * (1 + 0.76 * (rLam - 0.2) + rLam * rLam);
        double rChi = Math.Min(1.0, 1 / (rPhi + Math.Sqrt(rPhi * rPhi - rLam * rLam)));
        var box = SteelCheck.Run(r, Enumerable.Range(0, 5).Select(_ => new SectionForces(0, 0, 0, 0, 0, 50)).ToList(), 6.0);
        Close(box.LateralTorsional.Value, 50 / (rChi * r.WplZ * r.Fy), 1e-9,
              $"an RHS 200 x 100 x 10 over 6 m: M_cr = {rMcr:0} kNm, λ̄LT = {rLam:0.00}, χLT = {rChi:0.000}");
        var shortBox = SteelCheck.Run(r, Enumerable.Range(0, 5).Select(_ => new SectionForces(0, 0, 0, 0, 0, 50)).ToList(), 1.0);
        Close(shortBox.LateralTorsional.Value, 50 / (r.WplZ * r.Fy), 1e-9, "over 1 m, λ̄LT ≤ 0.2: the bending check, at γM1");

        var wide = new RectangleHollowCS("wide", 0.300, 0.100, 0.008, 0.008, 0.008, Steel("S355"));
        Check(!SteelSection.Of(wide, (SteelGrade)wide.Material.Grade, Fabrication.Rolled).MajorIsZ, "a box wider than it is deep is stiffer about y");

        Section("Members: an I with unequal flanges in S235, monosymmetric");

        // The larger flange on top, at +y: a positive Mz compresses it.
        double hl = 0.30, bt = 0.20, tt = 0.015, bb = 0.12, tb = 0.010, twl = 0.008;
        var lop = new ISection("lop", hl, bt, tt, bb, tb, twl, Steel("S235"));
        var ls = SteelSection.Of(lop, (SteelGrade)lop.Material.Grade, Fabrication.Rolled);
        double cy = ls.Stress.CentroidY;
        double yt = hl / 2 - tt / 2 - cy, yb = -hl / 2 + tb / 2 - cy;
        double ift = tt * bt * bt * bt / 12, ifb = tb * bb * bb * bb / 12;
        Close(ls.ShearCentre, (ift * yt + ifb * yb) / (ift + ifb), 1e-12, "shear centre between the flanges, nearer the stiffer one");
        Close(ls.Iw, ift * ifb / (ift + ifb) * (yt - yb) * (yt - yb), 1e-12, "Iw = If1 If2 / (If1 + If2) hs²");

        // β by hand from the three plates: ∫ y (y² + z²) dA of a rectangle is w (y2⁴ − y1⁴)/4 + (y2² − y1²)/2 · (z2³ − z1³)/3.
        Func<double, double, double, double> Plate = (y1, y2, width) =>
        {
            double z1 = -width / 2, z2 = width / 2;
            return width * (Math.Pow(y2, 4) - Math.Pow(y1, 4)) / 4 + (y2 * y2 - y1 * y1) / 2 * (Math.Pow(z2, 3) - Math.Pow(z1, 3)) / 3;
        };
        double wagner = Plate(hl / 2 - tt - cy, hl / 2 - cy, bt) + Plate(-hl / 2 + tb - cy, hl / 2 - tt - cy, twl) + Plate(-hl / 2 - cy, -hl / 2 + tb - cy, bb);
        double beta = wagner / ls.Iz - 2 * ls.ShearCentre;
        Close(ls.Monosymmetry, beta, 1e-9, "β = (1/Iz) ∫ y (y² + z²) dA − 2 y0");
        Check(ls.Monosymmetry < 0, "negative: a positive Mz compresses the larger flange, which holds it up");

        // A uniform moment, in closed form: M_cr = Pz [−β/2 + √((β/2)² + Iw/Iz + G It L² / π² E Iz)].
        double ll = 5.0, pz = Math.PI * Math.PI * e * ls.Iy / (ll * ll);
        double mono = pz * (-beta / 2 + Math.Sqrt(beta * beta / 4 + ls.Iw / ls.Iy + g * ls.It * ll * ll / (Math.PI * Math.PI * e * ls.Iy)));
        double up = CriticalMoment.Multiplier(ll, 1.0, 0.0, 0.0, 0.0, 0.0, e, ls.LateralInertia, ls.Iw, g, ls.It, ls.Monosymmetry);
        double down = CriticalMoment.Multiplier(ll, -1.0, 0.0, 0.0, 0.0, 0.0, e, ls.LateralInertia, ls.Iw, g, ls.It, ls.Monosymmetry);
        Close(up, mono, 1e-9, "uniform moment, monosymmetric, against the closed form");
        Check(up > down, $"the larger flange compressed {up:0.0} kNm, the smaller {down:0.0} kNm");
        Check(ls.CurveLT == BucklingCurve.d && !ls.LtRolledMethod, "the general case: welded, h/b by the narrower flange = 2.5, curve d");

        var bent = SteelCheck.Run(ls, Enumerable.Range(0, 5).Select(_ => new SectionForces(-100, 0, 0, 0, 0, 50)).ToList(), 5.0);
        Check(bent.LateralTorsional.HasValue && !bent.InteractionY.HasValue && bent.Missing.Contains("InteractionY") && bent.Max == null,
              "in compression and bending: LTB is checked, 6.3.3 - for doubly symmetric sections only - is not, and there is no Max");
        var pushed = SteelCheck.Run(ls, Enumerable.Range(0, 5).Select(_ => new SectionForces(-100, 0, 0, 0, 0, 0)).ToList(), 5.0);
        Check(pushed.InteractionY == 0.0 && pushed.Max.HasValue, "in compression alone there is nothing for 6.3.3 to add, and it is checked");
    }

    static void BendingAndCompression()
    {
        Section("Members: an HE 300 B-like column in S355 under N and Mz, 6.3.3 by hand");

        // Class 2 by its flanges. N = 1000 kN, Mz = 100 kNm uniform, every length 5 m.
        var hea = new ISection("HE300B", 0.300, 0.300, 0.019, 0.300, 0.019, 0.011, Steel("S355"));
        var c = SteelSection.Of(hea, (SteelGrade)hea.Material.Grade, Fabrication.Rolled);
        double e = 210e6, g = 80.77e6, l = 5.0, n = 1000, m = 100, fy = c.Fy, a = c.A;
        var forces = Enumerable.Range(0, 5).Select(_ => new SectionForces(-n, 0, 0, 0, 0, m)).ToList();
        var u = SteelCheck.Run(c, forces, l);
        Check(u.Checked && u.BucklingClass == 2, $"class 2 for buckling: {u.BucklingClass}");

        // The standard's y is the major axis, Alpaca4d's z.
        double lamY = Math.Sqrt(a * fy / (Math.PI * Math.PI * e * c.Iz / (l * l)));
        double lamZ = Math.Sqrt(a * fy / (Math.PI * Math.PI * e * c.Iy / (l * l)));
        double chiY = Ec3.Reduction(lamY, Ec3.Imperfection(c.CurveZ)), chiZ = Ec3.Reduction(lamZ, Ec3.Imperfection(c.CurveY));
        double ny = n / (chiY * a * fy), nz = n / (chiZ * a * fy);
        Close(u.BucklingZ.Value, ny, 1e-12, "N / (χy A fy), about the major axis: BucklingZ");

        // A uniform moment: Cmy = CmLT = 0.6 + 0.4 ψ = 1. h/b = 1: curve b for LTB.
        double chiLT = ChiLtRolled(Math.Sqrt(c.WplZ * fy / McrUniform(e, g, c.Iy, c.It, c.Iw, l)), 0.34);
        double my = m / (chiLT * c.WplZ * fy);
        double kyy = 1.0 * Math.Min(1 + (lamY - 0.2) * ny, 1 + 0.8 * ny);
        double kzy = Math.Max(1 - 0.1 * lamZ / (1.0 - 0.25) * nz, 1 - 0.1 / (1.0 - 0.25) * nz);
        if (lamZ < 0.4) kzy = Math.Min(0.6 + lamZ, 1 - 0.1 * lamZ / (1.0 - 0.25) * nz);

        Close(u.InteractionZ.Value, ny + kyy * my, 1e-9, $"(6.61) = ny + kyy My/(χLT Wpl fy), kyy = {kyy:0.000}: InteractionZ, χ about local z");
        Close(u.InteractionY.Value, nz + kzy * my, 1e-9, $"(6.62) = nz + kzy My/(χLT Wpl fy), kzy = {kzy:0.000} (Table B.2): InteractionY");
        Check(u.Governing.StartsWith("Interaction"), $"and an interaction governs: {u.Governing}");

        // Sway: Cm = 0.9 in place of 1.
        var sway = SteelCheck.Run(c, forces, new SteelMember(l) { Sway = true });
        Check(sway.Report.Contains("Cmy = 0.9,") && sway.InteractionZ < u.InteractionZ, "a sway frame: Cmy = 0.9 (Table B.3)");

        // A buckling length longer than the member: its diagram is not the one that matters, so Cm = 1.
        var bent = new List<SectionForces>
        {
            new SectionForces(-n, 0, 0, 0, 0, m), new SectionForces(-n, 0, 0, 0, 0, m / 2), new SectionForces(-n, 0, 0, 0, 0, 0),
            new SectionForces(-n, 0, 0, 0, 0, -m / 2), new SectionForces(-n, 0, 0, 0, 0, -m),
        };
        var own = SteelCheck.Run(c, bent, new SteelMember(l));
        var part = SteelCheck.Run(c, bent, new SteelMember(l) { BucklingLengthZ = 2 * l });
        Check(own.Report.Contains("Cmy = 0.4,") && part.Report.Contains("Cmy = 1,"),
              "end moments M and −M: Cmy = 0.6 − 0.4 = 0.2, at least 0.4; with L_cr twice the member, 1");

        // No compression, bending about one axis only: 6.3.2 is the whole of it.
        var beam = SteelCheck.Run(c, Enumerable.Range(0, 5).Select(_ => new SectionForces(0, 0, 0, 0, 0, m)).ToList(), l);
        Check(beam.InteractionY == 0.0 && beam.InteractionZ == 0.0, "a beam with no axial force: interactions zero");

        // No compression but bending about both axes: (6.62) puts LTB and minor axis bending together.
        var biaxial = SteelCheck.Run(c, Enumerable.Range(0, 5).Select(_ => new SectionForces(0, 0, 0, 0, 30, m)).ToList(), l);
        Check(biaxial.InteractionY > beam.LateralTorsional && biaxial.InteractionY > 0.0, "bending about both axes with no compression: 6.3.3 still applies");
    }

    // ---------------------------------------------------------------- the whole chain

    class StubBeam : IBeam
    {
        public Rhino.Geometry.Curve Curve { get; set; }
        public Alpaca4d.Element.GeomTransf GeomTransf { get; set; }
        public IUniaxialSection Section { get; set; }
        public IIntegration BeamIntegration { get; set; }
        public int? INode { get; set; }
        public int? JNode { get; set; }
        public System.Drawing.Color Color { get; set; }
        public Alpaca4d.Element.ElementType Type => Alpaca4d.Element.ElementType.Beam;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public void SetTags() { }
        public void SetTopologyRTree(Model model) { }
        public string WriteTcl() => "";
        public IBeam WithSection(IUniaxialSection section) =>
            new StubBeam { Id = Id, ElementId = ElementId, Section = section, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(section, BeamIntegration?.IntegrationPoint ?? 5) };
    }

    static void Chain(string folder)
    {
        Section("Whole chain: recorder file to utilisation");

        string file = Path.Combine(folder, "steel.mpco");
        if (!File.Exists(file))
        {
            Console.WriteLine("  (skipped: steel.tcl was not solved - no OpenSees)");
            return;
        }

        // steel.tcl: three 3 m cantilevers, loaded at the tip. Statically determinate, so the forces at
        // the fixed end are the loads times the lever arm whatever the section stiffness.
        var hea = new ISection("HE300B", 0.300, 0.300, 0.019, 0.300, 0.019, 0.011, Steel("S355"));
        var rhs = new RectangleHollowCS("RHS", 0.100, 0.200, 0.010, 0.010, 0.010, Steel("S275"));
        var timber = new RectangleCS("glulam", 0.2, 0.4, new UniaxialMaterialElastic("GL24h", 11e6, 11e6, 0, 0.65e6, 0.3, 420));

        var model = new Model
        {
            Recorders = new List<IRecorder> { new Recorder { FileName = file } },
            Beams = new List<IBeam>
            {
                new StubBeam { Id = 1, Section = hea, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(hea, 5) },
                new StubBeam { Id = 2, Section = rhs, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(rhs, 5) },
                new StubBeam { Id = 3, Section = timber, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(timber, 5) },
            }
        };

        var all = Utilisations.Of(model, 0, new[] { 3.0, 3.0, 3.0 }, null, new DesignSettings());
        Check(all.Count == 3, "one result per beam");

        // Beam 1: N = −400 kN, Vy = 30 kN, Mz = 90 kNm at the fixed end.
        var c = SteelSection.Of(hea, (SteelGrade)hea.Material.Grade, Fabrication.Rolled);
        var u = all[0];
        Check(u.Type == "Steel" && u.Checked, $"beam 1 is steel and checked: {u.Type}, {u.Checked}");
        Close(u.Axial.Value, 400 / (c.A * c.Fy), 1e-6, "beam 1 Axial");
        Close(u.BendingZ.Value, 90 / (c.WplZ * c.Fy), 1e-6, "beam 1 BendingZ, at the fixed end");
        Close(u.ShearY.Value, 30 / (c.AvY * c.Fy / Math.Sqrt(3)), 1e-6, "beam 1 ShearY");
        double n = 400 / (c.A * c.Fy);
        double mn = Ec3.ReducedMomentIMajor(c.WplZ * c.Fy, n, c.InteractionAZ);
        Close(u.Combined.Value, Math.Max(n, 90 / mn), 1e-6, "beam 1 Combined, (6.36)");
        Check(u.Report.Contains("x = 0 L"), "and it says the fixed end governs");

        // Beam 2: RHS in S275, tension 100 kN, Vz = 5 kN, My = 15 kNm.
        var r = SteelSection.Of(rhs, (SteelGrade)rhs.Material.Grade, Fabrication.Rolled);
        Close(all[1].BendingY.Value, 15 / (r.WplY * r.Fy), 1e-6, "beam 2 BendingY, S275");
        Check(all[1].BucklingY == 0.0, "beam 2 in tension: no buckling");

        Check(all[2].Type == "Unknown" && !all[2].Checked && all[2].Max == null,
              "beam 3, whose material has no steel grade, is reported and left unchecked");

        // The same through the overload that takes each member's lengths: beam 1's minor axis buckling
        // over 6 m rather than 3.
        var members = new[] { new SteelMember(3.0) { BucklingLengthY = 6.0 }, new SteelMember(3.0), new SteelMember(3.0) };
        var held = Utilisations.Of(model, 0, members, new DesignSettings());
        double lam = Math.Sqrt(c.A * c.Fy / (Math.PI * Math.PI * 210e6 * c.Iy / 36.0));
        Close(held[0].BucklingY.Value, 400 / (Ec3.Reduction(lam, Ec3.Imperfection(c.CurveY)) * c.A * c.Fy), 1e-6, "beam 1 BucklingY with L_cr,y = 6 m");
        Close(held[0].BucklingZ.Value, u.BucklingZ.Value, 1e-12, "and BucklingZ as before");
    }

    static void Main(string[] args)
    {
        JrcColumn();
        JrcBeam();
        DesignersGuideBendingAndCompression();
        StructvilleBiaxial();
        DesignersGuideTube();
        EccsCriticalMoment();
        SchneiderBeamColumn();
        CriticalMoments();
        IShape();
        UnequalI();
        BoxAndTube();
        Grades();
        Members();
        LateralTorsionalMembers();
        BendingAndCompression();
        Chain(args.Length > 0 ? args[0] : ".");

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
