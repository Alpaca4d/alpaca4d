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
        Check(u.Governing == "BendingZ", $"bending governs: {u.Governing}");
        Check(u.Report.Contains("x = 0.5 L") && u.Report.Contains("Not checked: lateral-torsional buckling"),
              "the report says where, and what it does not cover");

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
    }

    static void Main(string[] args)
    {
        JrcColumn();
        JrcBeam();
        DesignersGuideBendingAndCompression();
        StructvilleBiaxial();
        DesignersGuideTube();
        IShape();
        UnequalI();
        BoxAndTube();
        Grades();
        Members();
        Chain(args.Length > 0 ? args[0] : ".");

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
