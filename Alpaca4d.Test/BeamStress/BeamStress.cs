// Beam stresses recovered from section forces.
//
// Two kinds of check. The first is arithmetic: every shape Alpaca4d has, set against the textbook
// formula for it, worked out here independently of the rectangles BeamSectionStress builds its
// shapes from. The second runs OpenSees: two lopsided elastic fibre sections, loaded every way at
// once, and the stress the solver integrates in a corner fibre set against the stress recovered
// from the section forces it reported. That one is what pins the sign convention and the centroid,
// because a recovery that bent the wrong way, or measured from the middle of the drawing rather
// than from the centroid, would still pass on anything symmetric.
//
// Shear and torsion are checked against formulas only. OpenSees does not resolve a shear stress
// distribution over a beam section, so there is nothing of the solver's to compare them with.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Alpaca4d.Result;
using Alpaca4d.Section;

class BeamStressTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Close(double got, double want, double tol, string what)
    {
        bool ok = Math.Abs(got - want) <= tol * Math.Max(1e-12, Math.Abs(want));
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {got:G6}, want {want:G6}");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    // ---------------------------------------------------------------- rectangle

    static void RectangleTests()
    {
        Section("Rectangle 0.2 wide (z) by 0.4 tall (y)");

        double w = 0.2, h = 0.4;
        var s = BeamSectionStress.Of(new RectangleCS("r", w, h, null));

        Close(s.Area, w * h, 1e-12, "area");
        Close(s.Iz, w * h * h * h / 12, 1e-12, "Iz = b·h³/12, tall in y");
        Close(s.Iy, h * w * w * w / 12, 1e-12, "Iy = h·b³/12");

        // N/A = 1250, My·(w/2)/Iy = 3750, Mz·(h/2)/Iz = 3750.
        var at = s.At(100.0, 0.0, 0.0, 0.0, 10.0, 20.0);
        Close(at.SigmaN, 1250.0, 1e-12, "σN = N/A");
        Close(at.SigmaMy, 3750.0, 1e-12, "σMy = My/Wy");
        Close(at.SigmaMz, 3750.0, 1e-12, "σMz = Mz/Wz");
        Close(at.SigmaMax, 8750.0, 1e-12, "σmax = N/A + My/Wy + Mz/Wz, at a corner");
        Close(at.SigmaMin, -6250.0, 1e-12, "σmin = N/A − My/Wy − Mz/Wz, at the opposite corner");

        Close(s.ShearY, 1.5 / (w * h), 1e-9, "τ from Vy peaks at 1.5·V/A");
        Close(s.ShearZ, 1.5 / (w * h), 1e-9, "τ from Vz peaks at 1.5·V/A");

        // Saint-Venant, against the tabulated τmax = T / (k·a·b²): k = 0.208 for a square,
        // 0.246 at 2:1 and 0.312 at 10:1 (Timoshenko & Goodier).
        Close(BeamSectionStress.Of(new RectangleCS("sq", 0.1, 0.1, null)).Torsion,
              1.0 / (0.208 * 0.1 * 0.01), 5e-3, "torsion, square");
        Close(s.Torsion, 1.0 / (0.246 * h * w * w), 5e-3, "torsion, 2:1");
        Close(BeamSectionStress.Of(new RectangleCS("strip", 0.01, 0.1, null)).Torsion,
              1.0 / (0.312 * 0.1 * 0.01 * 0.01), 1e-2, "torsion, 10:1");
    }

    // ---------------------------------------------------------------- round

    static void CircleTests()
    {
        Section("Round bar and tube");

        double d = 0.1;
        var bar = BeamSectionStress.Of(new CircleCS("bar", d, 0.0, null));
        double a = Math.PI * d * d / 4;
        double i = Math.PI * Math.Pow(d, 4) / 64;

        Close(bar.Area, a, 1e-12, "bar area");
        Close(bar.Iy, i, 1e-12, "bar I");
        Close(bar.ShearY, 4.0 / (3.0 * a), 1e-9, "bar: τ from V peaks at 4V/3A");
        Close(bar.Torsion, 16.0 / (Math.PI * d * d * d), 1e-9, "bar: τ = 16T/πd³");

        // A circle has no corners: the bending peak is the moment resultant over W, whichever
        // way it points.
        var at = bar.At(1000.0, 0.0, 0.0, 0.0, 30.0, 40.0);
        Close(at.SigmaMax, 1000.0 / a + 50.0 * (d / 2) / i, 1e-12, "bar: σmax = N/A + √(My² + Mz²)/W");
        Close(at.SigmaMin, 1000.0 / a - 50.0 * (d / 2) / i, 1e-12, "bar: σmin = N/A − √(My² + Mz²)/W");

        var solidToo = BeamSectionStress.Of(new CircleCS("half", d, d / 2, null));
        Close(solidToo.Area, a, 1e-12, "a thickness of half the diameter is solid, as CircleCS reads it");

        double D = 0.2, t = 0.002;
        var tube = BeamSectionStress.Of(new CircleCS("tube", D, t, null));
        double R = D / 2, r = R - t;
        double at4 = Math.Pow(R, 4) - Math.Pow(r, 4);
        Close(tube.ShearY, (2.0 / 3.0) * (Math.Pow(R, 3) - Math.Pow(r, 3)) / (Math.PI * at4 / 4 * 2 * t), 1e-9,
              "tube: Jourawski across a diameter");
        Close(tube.ShearY, 2.0 / tube.Area, 2e-2, "tube: and a thin one is close to 2V/A");
        Close(tube.Torsion, R / (Math.PI * at4 / 2), 1e-9, "tube: τ = T·R / Ip");

        var both = tube.At(0.0, 300.0, 400.0, 0.0, 0.0, 0.0);
        Close(both.TauV, tube.ShearY * 500.0, 1e-9, "tube: Vy and Vz together act as their resultant");
    }

    // ---------------------------------------------------------------- box

    static void HollowTests()
    {
        Section("Rectangular hollow 0.2 wide by 0.3 tall, walls 0.01");

        double w = 0.2, h = 0.3, t = 0.01;
        var s = BeamSectionStress.Of(new RectangleHollowCS("rhs", w, h, t, t, t, null));

        double iz = (w * h * h * h - (w - 2 * t) * Math.Pow(h - 2 * t, 3)) / 12;
        double iy = (h * w * w * w - (h - 2 * t) * Math.Pow(w - 2 * t, 3)) / 12;
        Close(s.Area, w * h - (w - 2 * t) * (h - 2 * t), 1e-12, "area");
        Close(s.Iz, iz, 1e-12, "Iz");
        Close(s.Iy, iy, 1e-12, "Iy");

        // Vy crosses the neutral axis through the two side walls.
        double sz = w * t * (h / 2 - t / 2) + 2 * t * Math.Pow(h / 2 - t, 2) / 2;
        Close(s.ShearY, sz / (iz * 2 * t), 1e-9, "τ from Vy = V·S / (Iz·2t), through both side walls");

        double sy = h * t * (w / 2 - t / 2) + 2 * t * Math.Pow(w / 2 - t, 2) / 2;
        Close(s.ShearZ, sy / (iy * 2 * t), 1e-9, "τ from Vz = V·S / (Iy·2t), through top and bottom");

        Close(s.Torsion, 1.0 / (2 * (w - t) * (h - t) * t), 1e-12, "Bredt: τ = T / (2·Am·t)");
    }

    // ---------------------------------------------------------------- I

    static void ITests()
    {
        Section("I, IPE 300 without its root radii");

        double h = 0.300, b = 0.150, tw = 0.0071, tf = 0.0107;
        var s = BeamSectionStress.Of(new ISection("ipe", h, b, tf, b, tf, tw, null));

        double iz = (b * h * h * h - (b - tw) * Math.Pow(h - 2 * tf, 3)) / 12;
        double iy = 2 * tf * b * b * b / 12 + (h - 2 * tf) * tw * tw * tw / 12;
        Close(s.Iz, iz, 1e-12, "Iz, strong axis");
        Close(s.Iy, iy, 1e-12, "Iy, weak axis");

        double sz = b * tf * (h / 2 - tf / 2) + tw * Math.Pow(h / 2 - tf, 2) / 2;
        Close(s.ShearY, sz / (iz * tw), 1e-9, "τ from Vy = V·S / (Iz·tw), mid-web");

        // Thin-walled: each flange outstand carries its own shear back to the web.
        Close(s.ShearZ, (b * b - tw * tw) / (8 * iy), 1e-9, "τ from Vz = V·(b² − tw²) / 8Iy, flange at the web");

        double j = (2 * b * tf * tf * tf + (h - 2 * tf) * tw * tw * tw) / 3;
        Close(s.Torsion, tf / j, 1e-9, "torsion: T·tmax / J, J = Σ b·t³/3 - not the polar moment ISection hands the solver");

        Section("I with unequal flanges");

        var lop = BeamSectionStress.Of(new ISection("lop", 0.30, 0.20, 0.015, 0.12, 0.010, 0.008, null));
        double topFlange = (0.20 * 0.20 - 0.008 * 0.008) / (8 * lop.Iy);
        double bottomFlange = (0.12 * 0.12 - 0.008 * 0.008) / (8 * lop.Iy);
        Close(lop.ShearZ, Math.Max(topFlange, bottomFlange), 1e-9,
              "τ from Vz is the wider flange's own, not an average over both");

        // Centroid of three rectangles, measured from mid-height.
        double aTop = 0.20 * 0.015, aWeb = 0.008 * 0.275, aBot = 0.12 * 0.010;
        double yc = (aTop * 0.1425 + aWeb * (-0.0025) + aBot * (-0.145)) / (aTop + aWeb + aBot);
        Close(lop.CentroidY, yc, 1e-12, "centroid sits towards the heavier flange");
        Close(lop.CentroidZ, 0.0, 1e-12, "and on the axis of symmetry");
    }

    // ---------------------------------------------------------------- angles

    static void DoubleAngleTests()
    {
        Section("Two angles 100 x 75 x 8, gap 10");

        double h = 0.100, w = 0.075, t = 0.008, g = 0.010;
        var s = BeamSectionStress.Of(new DoubleLAngleCS("2L", h, w, t, g, null));

        double aLeg = h * t, aOut = (w - t) * t;
        Close(s.Area, 2 * (aLeg + aOut), 1e-12, "area, with no sliver between the angles");

        double yc = (aLeg * 0.0 + aOut * (-h / 2 + t / 2)) / (aLeg + aOut);
        Close(s.CentroidY, yc, 1e-12, "centroid pulled down towards the short legs");

        double zLeg = g / 2 + t / 2, zOut = g / 2 + t + (w - t) / 2;
        double iy = 2 * (h * t * t * t / 12 + aLeg * zLeg * zLeg
                         + t * Math.Pow(w - t, 3) / 12 + aOut * zOut * zOut);
        Close(s.Iy, iy, 1e-12, "Iy, about the axis between the angles");

        double yOut = -h / 2 + t / 2;
        double iz = 2 * (t * h * h * h / 12 + aLeg * yc * yc
                         + (w - t) * t * t * t / 12 + aOut * (yOut - yc) * (yOut - yc));
        Close(s.Iz, iz, 1e-12, "Iz, about the centroid rather than mid-height");

        // Vy crosses the neutral axis through both long legs, above the short ones.
        double sz = 2 * t * Math.Pow(h / 2 - yc, 2) / 2;
        Close(s.ShearY, sz / (iz * 2 * t), 1e-9, "τ from Vy = V·S / (Iz·2t), through both long legs");

        // Vz: the short legs take it like the flanges of an I, from their tips to the corner. The
        // angles are tied together across the gap, so a cut near a tip carries next to nothing -
        // without the tie it would read the whole angle's shear going through the tip.
        double sOut = aOut * zOut;
        double sLeg = aLeg * zLeg;
        Check(sOut > sLeg, "here the short leg is the one that governs Vz");
        Close(s.ShearZ, sOut / (iy * t), 1e-9, "τ from Vz = V·S / (Iy·t), at the root of the short leg");

        var touching = BeamSectionStress.Of(new DoubleLAngleCS("2L0", h, w, t, 0.0, null));
        double zOut0 = t + (w - t) / 2;
        double iy0 = 2 * (h * t * t * t / 12 + aLeg * (t / 2) * (t / 2) + t * Math.Pow(w - t, 3) / 12 + aOut * zOut0 * zOut0);
        Close(touching.ShearZ, aOut * zOut0 / (iy0 * t), 1e-9, "with no gap the long legs touch, and the same holds");

        double j = 2 * (h * t * t * t + (w - t) * t * t * t) / 3;
        Close(s.Torsion, t / j, 1e-9, "torsion: T·t / J, open section");
    }

    // ---------------------------------------------------------------- published worked examples
    //
    // R.C. Hibbeler, Mechanics of Materials, 8th ed. (Pearson, 2010), from the instructor's
    // solutions manual: chapter 6 (bending), 7 (transverse shear) and 8 (combined loadings). Each
    // problem is set up in its own units with its own numbers, and has to agree with the answer to
    // every digit printed - no more, since the manual rounds, and no less.
    //
    // Hibbeler writes the flexure formula as σ = −Mz·y/Iz + My·z/Iy with y up the depth, which is
    // OpenSees' convention and so this one: his moments go in as they are.

    /// <summary>
    /// <paramref name="got"/>, rounded to as many decimals as <paramref name="printed"/> shows, is
    /// <paramref name="printed"/>. Given in the unit the answer is printed in.
    /// </summary>
    static void Printed(double got, string printed, string what)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        double want = double.Parse(printed, culture);
        int decimals = printed.Contains(".") ? printed.Length - printed.IndexOf('.') - 1 : 0;
        double rounded = Math.Round(got, decimals, MidpointRounding.AwayFromZero);

        bool ok = Math.Abs(rounded - want) <= 1e-9 * Math.Max(1.0, Math.Abs(want));
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {got.ToString("G6", culture)}, printed {printed}");
    }

    static void PublishedISectionTests()
    {
        Section("Published: I-sections (Hibbeler, Mechanics of Materials 8th ed.)");

        // 7-2: flanges 200 x 20, web 20, depth 340 mm, V = 20 kN.
        // I = 0.2501(10^-3) m^4, Qmax = 0.865(10^-3) m^3, τmax = 3.459(10^6) Pa.
        var p72 = BeamSectionStress.Of(new ISection("7-2", 0.340, 0.200, 0.020, 0.200, 0.020, 0.020, null));
        Printed(p72.Iz * 1e3, "0.2501", "7-2 I [10^-3 m^4]");
        Printed(p72.At(0, 20e3, 0, 0, 0, 0).TauV / 1e6, "3.459", "7-2 τmax [MPa], mid-web");

        // 7-7: flanges 200 x 30, web 25, web 250 deep (310 overall), V = 30 kN.
        // I = 268.652(10^-6) m^4, Qmax = 1.0353(10^-3) m^3, τmax = 4.62 MPa.
        var p77 = BeamSectionStress.Of(new ISection("7-7", 0.310, 0.200, 0.030, 0.200, 0.030, 0.025, null));
        Printed(p77.Iz * 1e6, "268.652", "7-7 I [10^-6 m^4]");
        Printed(p77.At(0, 30e3, 0, 0, 0, 0).TauV / 1e6, "4.62", "7-7 τmax [MPa]");

        // 7-4: a T - flange 12 x 3 in. on a web 4 in. thick and 6 in. deep - V = 12 kip. Centroid
        // 3.30 in. below the top, I = 390.60 in^4, Qmax = 64.98 in^3, τmax = 0.499 ksi. Drawn as an
        // ISection whose bottom flange is as wide as the web, which is a T.
        var p74 = BeamSectionStress.Of(new ISection("7-4", 9.0, 12.0, 3.0, 4.0, 1.0, 4.0, null));
        Printed(4.5 - p74.CentroidY, "3.30", "7-4 centroid below the top of the T [in.]");
        Printed(p74.Iz, "390.60", "7-4 I [in^4]");
        Printed(p74.At(0, 12.0, 0, 0, 0, 0).TauV, "0.499", "7-4 τmax [ksi], on the neutral axis in the web");

        // 6-117: flanges 200 x 10, web 10, depth 170 mm, cantilever 2 m, P = 600 N at 30°.
        // Mz = −1039.23 N·m, My = −600 N·m, Iz = 28.44583(10^-6), Iy = 13.34583(10^-6) m^4,
        // σmax = 7.60 MPa (T) at a flange tip.
        var p6117 = BeamSectionStress.Of(new ISection("6-117", 0.170, 0.200, 0.010, 0.200, 0.010, 0.010, null));
        Printed(p6117.Iz * 1e6, "28.44583", "6-117 Iz [10^-6 m^4]");
        Printed(p6117.Iy * 1e6, "13.34583", "6-117 Iy [10^-6 m^4]");
        var s6117 = p6117.At(0, 0, 0, 0, -600.0, -1039.23);
        Printed(s6117.SigmaMax / 1e6, "7.60", "6-117 σmax [MPa] (T), biaxial bending");
        Printed(s6117.SigmaMin / 1e6, "-7.60", "6-117 and as much compression at the opposite tip");

        // 8-35: flanges 4 x 0.5 in., web 0.5 in., depth 7 in., M = 11 500 lb·ft.
        // I = 51.33 in^4, σA = −Mc/I = −9.41 ksi at the top.
        var p835 = BeamSectionStress.Of(new ISection("8-35", 7.0, 4.0, 0.5, 4.0, 0.5, 0.5, null));
        Printed(p835.Iz, "51.33", "8-35 I [in^4]");
        Printed(p835.NormalStressAt(0, 0, 11500.0 * 12, 3.5, 0) / 1e3, "-9.41", "8-35 σA at the top fibre [ksi]");
        Printed(p835.At(0, 0, 0, 0, 0, 11500.0 * 12).SigmaMin / 1e3, "-9.41", "8-35 which is σmin");

        // 6-118: a 300 x 600 mm block with a 150 x 150 hole whose centre is 375 mm from one face,
        // M = 1200 kN·m at 30°. That is a box section with walls 75, 75, 300 and 150 thick, the
        // thick one on Hibbeler's +y side. Centroid 0.2893 m from that face, Iz = 5.2132(10^-3),
        // Iy = 1.3078(10^-3) m^4, σA = 126 MPa (T), σB = −131 MPa (C). Not an I, but lopsided in a
        // way no I is, so the centroid shift is tested against a published number too.
        var p6118 = BeamSectionStress.Of(new RectangleHollowCS("6-118", 0.300, 0.600, 0.075, 0.300, 0.150, null));
        Printed(0.300 - p6118.CentroidY, "0.2893", "6-118 centroid from the thick face [m]");
        Printed(p6118.Iz * 1e3, "5.2132", "6-118 Iz [10^-3 m^4]");
        Printed(p6118.Iy * 1e3, "1.3078", "6-118 Iy [10^-3 m^4]");
        var s6118 = p6118.At(0, 0, 0, 0, 600e3, -1039.23e3);
        Printed(s6118.SigmaMax / 1e6, "126", "6-118 σmax [MPa] (T), corner A");
        Printed(s6118.SigmaMin / 1e6, "-131", "6-118 σmin [MPa] (C), corner B");
    }

    static void PublishedCircleTests()
    {
        Section("Published: round bars and pipes (Hibbeler, Mechanics of Materials 8th ed.)");

        // 7-20: solid rod, radius 2 in., V = 30 kip. τmax = 10/π = 3.18 ksi.
        var p720 = BeamSectionStress.Of(new CircleCS("7-20", 4.0, 0.0, null));
        Printed(p720.At(0, 30.0, 0, 0, 0, 0).TauV, "3.18", "7-20 τmax [ksi]");

        // 6-121: 30 mm shaft bent both ways, the worst section carrying 400 and 150 N·m.
        // M = √(400² + 150²) = 427.2 N·m, σmax = Mc/I = 161 MPa.
        var p6121 = BeamSectionStress.Of(new CircleCS("6-121", 0.030, 0.0, null));
        var s6121 = p6121.At(0, 0, 0, 0, 400.0, 150.0);
        Printed(s6121.SigmaMax / 1e6, "161", "6-121 σmax [MPa], from the resultant moment");
        Close(p6121.At(0, 0, 0, 0, 150.0, 400.0).SigmaMax, s6121.SigmaMax, 1e-12,
              "6-121 and the same whichever axis carries which moment");

        // 8-36: drill bit, 10 mm diameter. N = 120 N in compression, Vy = 90 N, T = 20 N·m,
        // Mz = 21 N·m. σA = −215.43 MPa at y = 5 mm, τ from torsion = 101.86 MPa.
        var p836 = BeamSectionStress.Of(new CircleCS("8-36", 0.010, 0.0, null));
        var s836 = p836.At(-120.0, 90.0, 0, 20.0, 0, 21.0);
        Printed(p836.NormalStressAt(-120.0, 0, 21.0, 0.005, 0) / 1e6, "-215.43", "8-36 σA [MPa]");
        Printed(s836.SigmaMin / 1e6, "-215.43", "8-36 which is σmin");
        Printed(s836.TauT / 1e6, "101.86", "8-36 τ from torsion [MPa]");

        // 8-63 / 8-64: sign on a pipe, outer radius 3.00 in., inner 2.75 in. At the section:
        // N = −1.50 kip, Vy = 10.8 kip, Vz = 0, T = 64.8 kip·ft, My = 9.00 kip·ft, Mz = −64.8 kip·ft.
        // A = 1.4375π in^2, I = 18.6992, J = 37.3984 in^4, Q = 4.13542 in^3.
        // σE = −125 ksi at (y −3, z 0), σF = −17.7 ksi at (y 0, z −3), τF = 67.2 ksi - where the
        // peak shear from Vy and the torsional shear meet, on the same wall in the same direction.
        var p863 = BeamSectionStress.Of(new CircleCS("8-63", 6.0, 0.25, null));
        Printed(p863.Area / Math.PI, "1.4375", "8-63 A [π in^2]");
        Printed(p863.Iy, "18.6992", "8-63 I [in^4]");
        double n863 = -1.50, vy863 = 10.8, t863 = 64.8 * 12, my863 = 9.00 * 12, mz863 = -64.8 * 12;
        Printed(p863.NormalStressAt(n863, my863, mz863, -3.0, 0.0), "-125", "8-64 σE [ksi]");
        Printed(p863.NormalStressAt(n863, my863, mz863, 0.0, -3.0), "-17.7", "8-64 σF [ksi]");
        var s863 = p863.At(n863, vy863, 0, t863, my863, mz863);
        Printed(s863.Tau, "67.2", "8-64 τF [ksi] = τV + τT, since both peak at F");
        Check(s863.SigmaMin <= p863.NormalStressAt(n863, my863, mz863, -3.0, 0.0),
              $"8-64 σmin {s863.SigmaMin:G4} ksi is at least as compressive as σE - with My as well as Mz, " +
              "the worst point is a little off E");

        // 8-65 / 8-66: pipe, outer radius 1 in., inner 0.75 in. Vy = 43.30 lb, Vz = 25 lb,
        // T = −519.62 lb·in, My = 250 lb·in, Mz = −433.01 lb·in. I = 0.53689, J = 1.07379 in^4.
        // σA = 605 psi at (y 0.75, z 0) on the bore, σB = −466 psi at (y 0, z −1).
        // The components: from Vz 35.89 psi, from Vy 62.17 psi, from T at the outside 483.91 psi.
        var p865 = BeamSectionStress.Of(new CircleCS("8-65", 2.0, 0.25, null));
        Printed(p865.Iy, "0.53689", "8-65 I [in^4]");
        Printed(p865.NormalStressAt(0, 250.0, -433.01, 0.75, 0.0), "605", "8-65 σA [psi]");
        Printed(p865.NormalStressAt(0, 250.0, -433.01, 0.0, -1.0), "-466", "8-66 σB [psi]");
        Printed(p865.ShearY * 43.30, "62.17", "8-66 τ from Vy [psi]");
        Printed(p865.ShearZ * 25.0, "35.89", "8-65 τ from Vz [psi]");
        Printed(p865.At(0, 0, 0, -519.62, 0, 0).TauT, "483.91", "8-66 τ from T [psi]");

        // 8-57 / 8-58: 2 in. rod, T = 7200 lb·in, shears of 600 and 500 lb at right angles.
        // τ from T = 4.584 ksi; from 600 lb 0.2546 ksi, from 500 lb 0.2122 ksi; at A and B, where
        // each shear meets the torsion, 4.838 and 4.796 ksi.
        var p857 = BeamSectionStress.Of(new CircleCS("8-57", 2.0, 0.0, null));
        var s857 = p857.At(0, 500.0, 600.0, 7200.0, 0, 0);
        Printed(s857.TauT / 1e3, "4.584", "8-57 τ from T [ksi]");
        Printed(p857.ShearZ * 600.0 / 1e3, "0.2546", "8-57 τ from 600 lb [ksi]");
        Printed(p857.ShearY * 500.0 / 1e3, "0.2122", "8-58 τ from 500 lb [ksi]");
        Check(s857.Tau >= 4838.0 && s857.Tau >= 4796.0,
              $"8-57/58 τ = {s857.Tau:G4} psi bounds both published points, 4838 and 4796 psi");
    }

    // ---------------------------------------------------------------- exact elasticity
    //
    // sectionproperties 3.10.2 (Robbie van Leeuwen), which solves the warping function over a
    // finite element mesh: the exact Saint-Venant stresses rather than beam theory. Steel, ν = 0.3,
    // units N and mm. The numbers are from fe_check.py, next to this file. Its circles are
    // polygons - 128 and 160 sides - which is worth a few hundredths of a percent.
    //
    // Direct stress has to agree - both are linear over the section. Shear does not, and the point
    // of these checks is to pin by how much: beam theory (Jourawski, what Hibbeler, Gere and
    // EN 1993-1-1 6.2.6(4) use) takes the shear as uniform across a cut, and the exact solution
    // is not. Hoogenboom (TU Delft, "Shear stiffness and maximum shear stress of tubular members")
    // finds a 200 x 20 tube 10% above the beam-theory value; these find 9.9%.

    static void FiniteElementTests()
    {
        Section("Exact elasticity: sectionproperties, steel, N and mm");

        // The Hibbeler 7-2 I, sharp corners.
        var i = BeamSectionStress.Of(new ISection("7-2", 340, 200, 20, 200, 20, 20, null));
        Close(i.At(0, 0, 0, 0, 0, -10e6).SigmaMax, 6.798187, 1e-6, "I: σ from 10 kN·m about the strong axis, FE 6.798187");
        Close(i.At(0, 0, 0, 0, 10e6, 0).SigmaMax, 37.220844, 1e-6, "I: σ from 10 kN·m about the weak axis, FE 37.220844");
        var both = i.At(100e3, 20e3, 5e3, 0.2e6, 2e6, -10e6);
        Close(both.SigmaMax, 21.385213, 1e-6, "I: σmax under N, both moments, both shears and torsion, FE 21.385213");

        // FE's own peak shear sits in the sharp corners between web and flange, where the exact
        // solution is singular and grows with the mesh. Away from them it is beam theory's.
        Close(i.ShearY * 20e3, 3.4588, 1e-3, "I: τ from Vy on the neutral axis, FE 3.4588 at that point");
        Check(i.Torsion * 1e6 >= 10.5593 && i.Torsion * 1e6 <= 10.5593 * 1.02,
              $"I: τ from T {i.Torsion * 1e6:G5} against FE 10.5593 on the faces - 1.5% safe, J = Σbt³/3 " +
              "leaving out the web-flange junction");
        Check(both.VonMises >= 21.959160 && both.VonMises <= 21.959160 * 1.10,
              $"I: Von Mises {both.VonMises:G5} bounds FE's worst point 21.959 by under 10%");

        // Solid bar, 100 diameter.
        var bar = BeamSectionStress.Of(new CircleCS("bar", 100, 0, null));
        Close(bar.At(0, 0, 0, 0, 4e6, 3e6).SigmaMax, 50.939075, 5e-4, "bar: σ under 3 and 4 kN·m together, FE 50.939");
        Close(bar.Torsion * 1e6, 5.093981, 1e-3, "bar: τ from T, FE 5.093981");
        Gap(bar.ShearY * 10e3, 1.764473, 0.03, 0.05, "bar: τ from V, FE 1.764473 at the centre");

        // Tubes: Hoogenboom's thick one, and a common thin CHS.
        var thick = BeamSectionStress.Of(new CircleCS("200x20", 200, 20, null));
        Close(thick.Torsion * 1e6, 1.090985, 0.015, "tube 200 x 20: τ from T, FE 1.090985");
        Gap(thick.ShearY * 10e3, 1.927491, 0.08, 0.12, "tube 200 x 20: τ from V, FE 1.927491");

        var thin = BeamSectionStress.Of(new CircleCS("168x8", 168.3, 8, null));
        Close(thin.Torsion * 1e6, 3.273290, 0.015, "CHS 168.3 x 8: τ from T, FE 3.273290");
        Gap(thin.ShearY * 10e3, 5.155985, 0.02, 0.06, "CHS 168.3 x 8: τ from V, FE 5.155985");
    }

    /// <summary>
    /// A known shortfall, pinned: the exact value is above beam theory's <paramref name="got"/> by
    /// between <paramref name="low"/> and <paramref name="high"/> of it. A change of formula shows here.
    /// </summary>
    static void Gap(double got, double exact, double low, double high, string what)
    {
        double gap = (exact - got) / got;
        bool ok = gap >= low && gap <= high;
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: beam theory {got:G6}, exact {gap:P1} above it - " +
                          $"expected {low:P0} to {high:P0}");
    }

    // ---------------------------------------------------------------- the rest

    static void OtherTests()
    {
        Section("A section with no shape");

        var elastic = BeamSectionStress.Of(new ElasticSection("e", 0.01, 1e-4, 1e-5, 1e-6, 0.8, 0.8, null));
        Check(!elastic.HasShape, "an Elastic Section has no shape to recover bending on");
        var at = elastic.At(500.0, 10.0, 10.0, 10.0, 10.0, 10.0);
        Close(at.SigmaN, 50000.0, 1e-12, "but N/A still reads");
        Check(at.SigmaMy == 0.0 && at.TauV == 0.0 && at.TauT == 0.0 && !at.HasShape,
              "and nothing else pretends to");

        Section("Combining");

        var rect = BeamSectionStress.Of(new RectangleCS("r", 0.1, 0.2, null));
        var all = rect.At(1000.0, 300.0, 400.0, 50.0, 20.0, 30.0);
        double tauY = rect.ShearY * 300.0, tauZ = rect.ShearZ * 400.0;
        Close(all.TauV, Math.Sqrt(tauY * tauY + tauZ * tauZ), 1e-12,
              "Vy and Vz peak together at the middle of a rectangle, at right angles, so add as a vector");
        Close(all.TauT, rect.Torsion * 50.0, 1e-12, "torsion is its own peak");
        Close(all.Tau, all.TauV + all.TauT, 1e-12, "τ = τV + τT, an upper bound");

        double sigma = Math.Max(Math.Abs(all.SigmaMax), Math.Abs(all.SigmaMin));
        Close(all.VonMises, Math.Sqrt(sigma * sigma + 3 * all.Tau * all.Tau), 1e-12,
              "Von Mises = √(σ² + 3τ²), worst σ with worst τ");

        var signs = rect.At(-1000.0, -300.0, -400.0, -50.0, -20.0, -30.0);
        Close(signs.TauV, all.TauV, 1e-12, "shear reads the same whichever way the force points");
        Close(signs.SigmaMax, -all.SigmaMin, 1e-12, "and reversing every force swaps σmax and σmin");
    }

    // ---------------------------------------------------------------- point by point

    // What View Results paints a solid beam with: the stress at one point of the surface, the shear
    // the one running along it. Forces in OpenSees' convention, whose Vy acts down -y - so a load
    // pushing up along +y reads as a negative Vy - and whose Vz acts along +z.

    /// <summary>The stresses at (y, z) on the surface, running along (ty, tz), under N, Vy, Vz, T, My, Mz.</summary>
    static PointStress At(BeamSectionStress s, double y, double z, double ty, double tz,
                          double n = 0, double vy = 0, double vz = 0, double t = 0, double my = 0, double mz = 0) =>
        s.SurfaceAt(y, z, ty, tz).Under(n, vy, vz, t, my, mz);

    static void SurfaceTests()
    {
        Section("Point by point: a rectangle 0.2 wide (z) by 0.4 tall (y)");
        {
            var s = BeamSectionStress.Of(new RectangleCS("r", 0.2, 0.4, null));
            var whole = s.At(100.0, 0.0, 0.0, 0.0, 10.0, 20.0);

            // A linear field over a rectangle is at its worst in two opposite corners.
            var corners = new[] { (0.2, 0.1), (0.2, -0.1), (-0.2, 0.1), (-0.2, -0.1) }
                .Select(c => At(s, c.Item1, c.Item2, 0, 1, n: 100.0, my: 10.0, mz: 20.0).Sigma).ToList();
            Close(corners.Max(), whole.SigmaMax, 1e-12, "the worst corner is the section's σmax");
            Close(corners.Min(), whole.SigmaMin, 1e-12, "and the opposite one its σmin");

            Close(At(s, 0.2, 0.0, 0, 1, mz: 20.0).Sigma, -3750.0, 1e-12, "a positive Mz compresses the +y face");
            Close(At(s, 0.0, 0.1, 1, 0, my: 10.0).Sigma, 3750.0, 1e-12, "a positive My stretches the +z face");
            var parts = At(s, 0.2, 0.1, 0, 1, n: 100.0, my: 10.0, mz: 20.0);
            Close(parts.SigmaMy + parts.SigmaMz + 100.0 / s.Area, parts.Sigma, 1e-12, "σ is N/A and its two bending parts");

            // Jourawski's parabola, along the side face: 1.5·V/A at the middle, nothing at the corners.
            double peak = 1.5 * 10.0 / s.Area;
            Close(At(s, 0.0, 0.1, 1, 0, vy: -10.0).TauV, peak, 1e-12, "+10 along y, side face, middle: +1.5·V/A along +y");
            Close(At(s, 0.1, 0.1, 1, 0, vy: -10.0).TauV, 0.75 * peak, 1e-12, "a quarter of the depth from the top: three-quarters of it");
            Check(Math.Abs(At(s, 0.2, 0.1, 1, 0, vy: -10.0).TauV) < 1e-9, "nothing at the corner");
            Check(Math.Abs(At(s, 0.2, 0.0, 0, 1, vy: -10.0).TauV) < 1e-9,
                  "nor anywhere on the top face, which Vy's shear does not run along");
            Close(At(s, 0.2, 0.0, 0, 1, vz: 10.0).TauV, peak, 1e-12, "Vz runs along the top face instead, peaking at its middle");
            Close(At(s, 0.0, 0.1, -1, 0, vy: -10.0).TauV, -peak, 1e-12, "read the other way along the surface, the sign turns");

            // Saint-Venant: the long side's middle is the section's τT, the corners nothing, and a
            // positive torque turns from y towards z - down -y on the +z face.
            Close(At(s, 0.0, 0.1, 1, 0, t: 1.0).TauT, -s.Torsion, 1e-9, "torque, +z face, middle: the section's τT, along −y");
            Close(At(s, 0.0, -0.1, 1, 0, t: 1.0).TauT, s.Torsion, 1e-9, "and along +y on the −z face");
            Check(Math.Abs(At(s, 0.2, 0.1, 1, 0, t: 1.0).TauT) < 2e-3 * s.Torsion, "nothing at the corner, to the series' last term");

            var square = BeamSectionStress.Of(new RectangleCS("sq", 0.1, 0.1, null));
            Close(At(square, 0.05, 0.0, 0, 1, t: 1.0).TauT, At(square, 0.0, -0.05, 1, 0, t: 1.0).TauT, 1e-9,
                  "a square: the middle of each side the same, worked out from two different series");
            var strip = BeamSectionStress.Of(new RectangleCS("strip", 0.002, 0.2, null));
            Close(Math.Abs(At(strip, 0.1, 0.0, 0, 1, t: 1.0).TauT) / strip.Torsion, 0.9159656 / (Math.PI * Math.PI / 8), 1e-3,
                  "a thin strip's short side: Catalan's G over π²/8, 0.742 of the long side's");

            var both = At(s, 0.0, 0.1, 1, 0, vy: -10.0, t: 1.0);
            Close(both.Tau, peak - s.Torsion, 1e-9, "shear and torque against each other on the +z face");
            Close(At(s, 0.0, -0.1, 1, 0, vy: -10.0, t: 1.0).Tau, peak + s.Torsion, 1e-9, "and with each other on the −z face");
            Close(both.VonMises, Math.Sqrt(3.0) * Math.Abs(peak - s.Torsion), 1e-9, "Von Mises from what meets at the point");
        }

        Section("Point by point: round bar and tube");
        {
            double d = 0.1;
            var bar = BeamSectionStress.Of(new CircleCS("bar", d, 0.0, null));
            Close(At(bar, 0.0, 0.05, 1, 0, vy: -10.0).TauV, bar.ShearY * 10.0, 1e-12,
                  "+10 along y: the side, where the surface runs along the force, takes Jourawski's peak");
            Check(Math.Abs(At(bar, 0.05, 0.0, 0, 1, vy: -10.0).TauV) < 1e-12, "the top, which faces it, nothing");
            double c = Math.Cos(Math.PI / 6), sn = Math.Sin(Math.PI / 6);
            Close(At(bar, 0.05 * sn, 0.05 * c, c, -sn, vy: -10.0).TauV, bar.ShearY * 10.0 * c, 1e-12,
                  "in between, the force's share along the surface");
            double polar = Math.PI * Math.Pow(d, 4) / 32;
            Close(At(bar, 0.0, 0.05, 1, 0, t: 1.0).TauT, -0.05 / polar, 1e-12, "torque: T·R/Ip, turning from y towards z");

            var tube = BeamSectionStress.Of(new CircleCS("tube", 0.2, 0.01, null));
            double tubePolar = Math.PI * (Math.Pow(0.1, 4) - Math.Pow(0.09, 4)) / 2;
            Close(Math.Abs(At(tube, 0.0, 0.09, 1, 0, t: 1.0).TauT), 0.09 / tubePolar, 1e-12, "a tube's inner face: T·r/Ip, r the inner radius");
            Close(At(tube, 0.0, 0.09, 1, 0, vy: -10.0).TauV, At(tube, 0.0, 0.1, 1, 0, vy: -10.0).TauV, 1e-12,
                  "and the same shear as the outer face, Jourawski's being the same through the wall");
        }

        Section("Point by point: the Hibbeler 7-2 I, N and mm");
        {
            var s = BeamSectionStress.Of(new ISection("7-2", 340, 200, 20, 200, 20, 20, null));
            double v = 20e3;

            Close(At(s, 0.0, 10.0, 1, 0, vy: -v).TauV, s.ShearY * v, 1e-12, "web face at the centroid: the section's τ from Vy, 3.459");
            Close(At(s, 0.0, -10.0, 1, 0, vy: -v).TauV, s.ShearY * v, 1e-12, "the same on the other face, upwards");

            // Thin-walled flow in the flange: V·S/(I·tf), S the outstand beyond the cut about the centroid.
            double flow = v * (20 * (100 - 55)) * 160 / (s.Iz * 20);
            Close(At(s, 150.0, 55.0, 0, 1, vy: -v).TauV, flow, 1e-12, "top flange's underside, 55 out: V·S/(I·t), outwards");
            Close(At(s, 150.0, -55.0, 0, 1, vy: -v).TauV, -flow, 1e-12, "and outwards on the other side, along −z");
            Close(At(s, -150.0, 55.0, 0, 1, vy: -v).TauV, -flow, 1e-12, "the bottom flange's runs in, towards the web");
            Check(Math.Abs(At(s, 160.0, 100.0, 1, 0, vy: -v).TauV) < 1e-12, "nothing on a flange's tip, which the flow does not run along");
            Check(Math.Abs(At(s, 170.0, 0.0, 0, 1, vy: -v).TauV) < 1e-9,
                  "and nothing over the web's centre line, where the two halves' flows meet and turn down it");
            Close(At(s, 170.0, 5.0, 0, 1, vy: -v).TauV, 0.5 * At(s, 170.0, 10.0, 0, 1, vy: -v).TauV, 1e-9,
                  "turning across the web's thickness rather than all at once");

            Close(At(s, 0.0, 10.0, 1, 0, t: 1e6).TauT, -1e6 * 20 / s.TorsionConstant, 1e-9, "torque on the web's face: T·t/J");
            Close(At(s, 170.0, 50.0, 0, 1, t: 1e6).TauT, 1e6 * 20 / s.TorsionConstant, 1e-3, "and on the flange's top, along +z");
            Close(At(s, 149.9, 10.0, 1, 0, t: 1e6).TauT, -1e6 * 20 / s.TorsionConstant, 1e-9,
                  "all the way up the web to the flange it runs into, which is no free end");
            Check(Math.Abs(At(s, 170.0, 99.9, 0, 1, t: 1e6).TauT) < 0.1 * 1e6 * 20 / s.TorsionConstant,
                  "but dying away into the corner at a flange's tip, which is: under a tenth 0.1 mm from it");
        }

        Section("Point by point: a box with unequal flanges, N and mm");
        {
            // 200 wide, 300 tall, sides 10, top 16, bottom 10.
            var s = BeamSectionStress.Of(new RectangleHollowCS("box", 200, 300, 10, 16, 10, null));
            double v = 20e3, top = 150 - s.CentroidY;

            Check(Math.Abs(At(s, top, 0.0, 0, 1, vy: -v).TauV) < 1e-9, "Vy: nothing at the middle of the top, the cell's line of symmetry");
            Close(At(s, 0.0, 100.0, 1, 0, vy: -v).TauV, s.ShearY * v, 5e-3,
                  "Vy: the side face at the centroid, the section's τ through both walls");
            Close(At(s, 0.0, 100.0, 1, 0, t: 1e6).TauT, -At(s, 0.0, -100.0, 1, 0, t: 1e6).TauT, 1e-12,
                  "torque runs one way round the cell");

            // Bredt: T over twice the area inside the walls' centre lines, over the wall.
            double enclosed = 190.0 * (300.0 - 13.0);
            Close(Math.Abs(At(s, 0.0, 100.0, 1, 0, t: 1e6).TauT), 1e6 / (2 * enclosed * 10), 1e-9, "Bredt in a side wall");
            Close(Math.Abs(At(s, top, 0.0, 0, 1, t: 1e6).TauT), 1e6 / (2 * enclosed * 16), 1e-9, "and in the thicker top");
        }

        Section("Point by point: a pair of angles");
        {
            // 100 tall, 75 wide, 8 thick, 10 apart: the long legs either side of the gap, upright.
            var s = BeamSectionStress.Of(new DoubleLAngleCS("2L", 100, 75, 8, 10, null));
            double v = 20e3;
            Close(At(s, 0.0, 13.0, 1, 0, vy: -v).TauV, At(s, 0.0, -13.0, 1, 0, vy: -v).TauV, 1e-9,
                  "Vy: the two long legs alike");
            Check(At(s, 0.0, 13.0, 1, 0, vy: -v).TauV > 0.0, "and upwards, with the force");
            Check(Math.Abs(At(s, -s.CentroidY - 46.0, 80.0, 1, 0, vy: -v).TauV) < 1e-12, "nothing on a short leg's tip");
        }

        Section("Point by point: nothing beyond the section's own worst direct stress");
        {
            // Every corner of every outline, under everything at once: their worst is the section's.
            // Corners as drawn, (y, z).
            var sections = new Alpaca4d.Generic.IUniaxialSection[]
            {
                new ISection("I", 400, 250, 25, 150, 15, 12, null),
                new RectangleHollowCS("box", 200, 300, 10, 16, 10, null),
                new DoubleLAngleCS("2L", 100, 75, 8, 10, null)
            };
            var corners = new[]
            {
                new[] { (200.0, 125.0), (200.0, -125.0), (-200.0, 75.0), (-200.0, -75.0), (175.0, 6.0), (-185.0, -6.0) },
                new[] { (150.0, 100.0), (150.0, -100.0), (-150.0, 100.0), (-150.0, -100.0) },
                new[] { (50.0, 5.0), (50.0, 13.0), (-50.0, 80.0), (-42.0, 80.0), (50.0, -13.0), (-50.0, -80.0), (-42.0, -80.0), (50.0, -5.0) }
            };
            for (int k = 0; k < sections.Length; k++)
            {
                var section = sections[k];
                var s = BeamSectionStress.Of(section);
                var worst = s.At(80e3, 15e3, -6e3, 0.3e6, -7e6, 12e6);
                var at = corners[k].Select(c => At(s, c.Item1 - s.CentroidY, c.Item2 - s.CentroidZ, 1, 0, 80e3, 15e3, -6e3, 0.3e6, -7e6, 12e6).Sigma).ToList();
                Close(at.Max(), worst.SigmaMax, 1e-9, $"{section.GetType().Name}: σmax at a corner of the outline");
                Close(at.Min(), worst.SigmaMin, 1e-9, $"{section.GetType().Name}: σmin at another");
            }
        }

        Section("Point by point: a section with no shape");
        {
            var elastic = BeamSectionStress.Of(new ElasticSection("e", 0.01, 1e-4, 1e-5, 1e-6, 0.8, 0.8, null));
            var at = At(elastic, 0.1, 0.1, 1, 0, 500.0, 10.0, 10.0, 10.0, 10.0, 10.0);
            Close(at.Sigma, 50000.0, 1e-12, "N/A");
            Check(at.SigmaMy == 0.0 && at.SigmaMz == 0.0 && at.TauV == 0.0 && at.TauT == 0.0, "and nothing else");
        }
    }

    // Exact elasticity at points of the surface: fe_check.py, "On the surface". A hair inside, where
    // the shear has nowhere to run but along the surface.
    static void SurfaceFiniteElementTests()
    {
        Section("Point by point against exact elasticity: sectionproperties, steel, N and mm");

        // The loads are fe_check.py's turned into OpenSees' forces: its vy is Vy, its vx -Vz, its
        // mzz T, its myy -My and its mxx -Mz.
        var i = BeamSectionStress.Of(new ISection("7-2", 340, 200, 20, 200, 20, 20, null));
        Close(Math.Abs(At(i, 0, 10, 1, 0, vy: 20e3).TauV), 3.459698, 1e-3, "I: Vy, web face at the centroid, FE 3.459698");
        Close(Math.Abs(At(i, 150, 55, 0, 1, vy: 20e3).TauV), 0.586701, 0.03, "I: Vy, flange underside 55 out, FE 0.586701 - thin walls within 3%");
        Close(Math.Abs(At(i, 170, 0, 0, 1, vz: 20e3).TauV), 3.656910, 0.03, "I: Vz, flange top over the web, FE 3.656910");
        Gap(Math.Abs(At(i, 0, 10, 1, 0, t: 1e6).TauT), 10.537329, -0.02, -0.01, "I: T, web face, FE 10.537329 - J = Σbt³/3 leaves out the junction");
        Close(At(i, 0, 10, 1, 0, 100e3, 20e3, 5e3, 0.2e6, 2e6, -10e6).VonMises, 12.456596, 0.02,
              "I: Von Mises under everything, web face at the centroid, FE 12.456596");
        Close(At(i, 170, 99.9, 0, 1, 100e3, 20e3, 5e3, 0.2e6, 2e6, -10e6).VonMises, 21.380273, 2e-3,
              "I: and at the top of a flange tip, FE 21.380273 - against the bound's 23.57");

        var box = BeamSectionStress.Of(new RectangleHollowCS("box", 200, 300, 10, 16, 10, null));
        Close(box.CentroidY, 13.853933, 1e-6, "box: centroid 13.853933 above the middle, as FE");
        Close(Math.Abs(At(box, 0, 100, 1, 0, vy: 20e3).TauV), 4.006214, 0.01, "box: Vy, side face at the centroid, FE 4.006214");
        Close(Math.Abs(At(box, -150 - box.CentroidY, 0, 0, 1, vz: 20e3).TauV), 5.212344, 0.03,
              "box: Vz, the bottom's middle, FE 5.212344 - the cell kept from twisting");
        Gap(Math.Abs(At(box, 0, 100, 1, 0, t: 1e6).TauT), 0.986774, 0.06, 0.09, "box: T, side face (10 thick), FE 0.986774");
        Gap(Math.Abs(At(box, 150 - box.CentroidY, 0, 0, 1, t: 1e6).TauT), 0.687306, 0.17, 0.23, "box: T, top face (16 thick), FE 0.687306");

        var chs = BeamSectionStress.Of(new CircleCS("chs", 168.3, 8, null));
        Gap(At(chs, 0, 84.15, 1, 0, vy: -10e3).TauV, 4.775870, -0.05, -0.02, "CHS 168.3 x 8: V at the side, FE 4.775870");
        Check(Math.Abs(At(chs, 84.15, 0, 0, 1, vy: -10e3).TauV) < 1e-9, "CHS 168.3 x 8: and nothing at the top, FE 0.0005");

        var bar = BeamSectionStress.Of(new CircleCS("bar", 100, 0, null));
        Gap(At(bar, 0, 50, 1, 0, vy: -10e3).TauV, 1.568422, -0.09, -0.07, "bar 100: V at the side, FE 1.568422");

        var r = BeamSectionStress.Of(new RectangleCS("r", 200, 400, null));
        Gap(At(r, 0, 100, 1, 0, vy: -10e3).TauV, 0.194683, 0.03, 0.05, "rectangle 200 x 400: Vy, side face's middle, FE 0.194683");
        Gap(At(r, 200, 0, 0, 1, vz: 10e3).TauV, 0.273325, 0.4, 0.5,
            "rectangle 200 x 400: Vz across the depth, top face's middle, FE 0.273325 - a wide beam, beyond Jourawski");
        Close(Math.Abs(At(r, 0, 100, 1, 0, t: 1e6).TauT), 0.254059, 5e-3, "rectangle: T, long side's middle, FE 0.254059");
        Close(Math.Abs(At(r, 200, 0, 0, 1, t: 1e6).TauT), 0.202125, 5e-3, "rectangle: T, short side's middle, FE 0.202125");
        Close(Math.Abs(At(r, 150, 100, 1, 0, t: 1e6).TauT), 0.168876, 5e-3, "rectangle: T, long side 150 up, FE 0.168876");
    }

    // ---------------------------------------------------------------- against OpenSees

    static double Last(string file)
    {
        var line = File.ReadAllLines(file).Last(x => !string.IsNullOrWhiteSpace(x));
        return double.Parse(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Last(),
                            System.Globalization.CultureInfo.InvariantCulture);
    }

    static double[] Forces(string file)
    {
        var line = File.ReadAllLines(file).Last(x => !string.IsNullOrWhiteSpace(x));
        return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture))
                   .ToArray();
    }

    /// <summary>
    /// The stress in the fibre at (y, z), as the solver integrated it, against the stress recovered
    /// at the same point from the section forces the solver reported.
    ///
    /// Not exact, by about the fibre discretisation: the solver bends a section whose second moment
    /// is a sum over fibres, and the recovery uses the exact one. A few hundredths of a percent with
    /// the subdivisions in fibre.tcl - a wrong sign or a wrong centroid is tens of percent.
    /// </summary>
    static void Fibre(BeamSectionStress section, double[] forces, double y, double z, string file, string what)
    {
        // A fibre section reports P, Mz, My, T.
        double n = forces[0], mz = forces[1], my = forces[2];
        double recovered = section.NormalStressAt(n, my, mz, y - section.CentroidY, z - section.CentroidZ);
        Close(recovered, Last(file), 2e-3, what);
    }

    /// <summary>
    /// Every fibre of a section, as "fiberData" writes them: y, z, area, stress, strain, fibre after
    /// fibre, in the coordinates the patches were drawn in.
    /// </summary>
    static List<(double Y, double Z, double Area, double Stress)> AllFibres(string file)
    {
        var line = File.ReadAllLines(file).Last(x => !string.IsNullOrWhiteSpace(x));
        var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture))
                         .ToArray();

        var fibres = new List<(double, double, double, double)>();
        for (int k = 0; k + 4 < values.Length; k += 5)
            fibres.Add((values[k], values[k + 1], values[k + 2], values[k + 3]));
        return fibres;
    }

    /// <summary>
    /// The stress recovered at every fibre's centre against the stress the solver integrated there,
    /// over the whole section rather than at a few corners.
    ///
    /// Not exact, by the fibre discretisation: OpenSees bends a section whose second moment is a sum
    /// over fibres, and the recovery uses the exact shape. How far apart those two are is measured
    /// from the fibres themselves and printed alongside, so a mismatch can be told from a coarse mesh.
    /// </summary>
    static void EveryFibre(BeamSectionStress section, string forcesFile, string fibresFile, double tolerance, string what)
    {
        var forces = Forces(forcesFile);
        double n = forces[0], mz = forces[1], my = forces[2];
        var fibres = AllFibres(fibresFile);

        double area = fibres.Sum(f => f.Area);
        double yc = fibres.Sum(f => f.Area * f.Y) / area;
        double zc = fibres.Sum(f => f.Area * f.Z) / area;
        double izFibres = fibres.Sum(f => f.Area * (f.Y - yc) * (f.Y - yc));
        double iyFibres = fibres.Sum(f => f.Area * (f.Z - zc) * (f.Z - zc));

        double peak = fibres.Max(f => Math.Abs(f.Stress));
        double worst = fibres.Max(f => Math.Abs(section.NormalStressAt(n, my, mz, f.Y - section.CentroidY, f.Z - section.CentroidZ) - f.Stress));

        bool ok = worst <= tolerance * peak;
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: {fibres.Count} fibres, worst difference {worst / peak:P3} of the peak " +
                          $"{peak:G5} (the fibres' own Iz and Iy are {izFibres / section.Iz - 1:P3} and {iyFibres / section.Iy - 1:P3} off the exact shape's)");

        // The recovered extremes sit on the outline; the outermost fibres sit just inside it.
        var extremes = section.At(n, 0, 0, forces[3], my, mz);
        double highest = fibres.Max(f => f.Stress), lowest = fibres.Min(f => f.Stress);
        Check(extremes.SigmaMax >= highest - tolerance * peak && extremes.SigmaMax <= highest + 0.05 * peak
              && extremes.SigmaMin <= lowest + tolerance * peak && extremes.SigmaMin >= lowest - 0.05 * peak,
              $"{what}: σmax {extremes.SigmaMax:G5} and σmin {extremes.SigmaMin:G5} bound the fibres' " +
              $"{highest:G5} and {lowest:G5}, and come within a fibre's depth of them");
    }

    static void SolverTests(string folder)
    {
        Section("Against OpenSees fibre stresses, I with unequal flanges");

        if (!File.Exists(Path.Combine(folder, "force1.txt")))
        {
            Console.WriteLine("  (skipped: fibre.tcl was not solved - no OpenSees)");
            return;
        }

        var i = BeamSectionStress.Of(new ISection("lop", 0.30, 0.20, 0.015, 0.12, 0.010, 0.008, null));
        var f1 = Forces(Path.Combine(folder, "force1.txt"));
        Check(f1[1] != 0.0 && f1[2] != 0.0 && f1[0] != 0.0, "the section carries N, Mz and My at once");

        Fibre(i, f1,  0.1490625,  0.09875, Path.Combine(folder, "i_top_pos.txt"), "top flange, +z tip");
        Fibre(i, f1,  0.1490625, -0.09875, Path.Combine(folder, "i_top_neg.txt"), "top flange, −z tip");
        Fibre(i, f1, -0.149375,   0.05875, Path.Combine(folder, "i_bot_pos.txt"), "bottom flange, +z tip");
        Fibre(i, f1, -0.149375,  -0.05875, Path.Combine(folder, "i_bot_neg.txt"), "bottom flange, −z tip");

        // The extremes are at the outline corners; the fibres sit half a fibre inside them, so
        // the recovered extremes bound every fibre and come within a fibre's worth of the worst.
        var stress = i.At(f1[0], 0.0, 0.0, f1[3], f1[2], f1[1]);
        var fibres = new[] { "i_top_pos.txt", "i_top_neg.txt", "i_bot_pos.txt", "i_bot_neg.txt" }
            .Select(x => Last(Path.Combine(folder, x))).ToList();
        Check(stress.SigmaMax >= fibres.Max() && stress.SigmaMax <= fibres.Max() * 1.03,
              $"σmax {stress.SigmaMax:G6} bounds the worst fibre {fibres.Max():G6}, just");
        Check(stress.SigmaMin <= fibres.Min() && stress.SigmaMin >= fibres.Min() * 1.03,
              $"σmin {stress.SigmaMin:G6} bounds the worst fibre {fibres.Min():G6}, just");

        Section("Against OpenSees fibre stresses, two angles");

        var l = BeamSectionStress.Of(new DoubleLAngleCS("2L", 0.100, 0.075, 0.008, 0.010, null));
        var f2 = Forces(Path.Combine(folder, "force2.txt"));
        Check(f2[0] < 0.0, "this one in compression");

        Fibre(l, f2,  0.0495,  0.0125, Path.Combine(folder, "l_top_pos.txt"), "top of the +z long leg");
        Fibre(l, f2, -0.0495,  0.0795, Path.Combine(folder, "l_tip_pos.txt"), "tip of the +z short leg");
        Fibre(l, f2,  0.0495, -0.0125, Path.Combine(folder, "l_top_neg.txt"), "top of the −z long leg");
        Fibre(l, f2, -0.0495, -0.0795, Path.Combine(folder, "l_tip_neg.txt"), "tip of the −z short leg");

        Section("Against OpenSees fibre stresses, every fibre: round bar, tube, IPE 300");

        EveryFibre(BeamSectionStress.Of(new CircleCS("bar", 0.100, 0.0, null)),
                   Path.Combine(folder, "force3.txt"), Path.Combine(folder, "bar_fibres.txt"), 1e-2,
                   "round bar 100, N + My + Mz");
        EveryFibre(BeamSectionStress.Of(new CircleCS("tube", 0.200, 0.010, null)),
                   Path.Combine(folder, "force4.txt"), Path.Combine(folder, "tube_fibres.txt"), 1e-2,
                   "tube 200 x 10, N + My + Mz");
        EveryFibre(BeamSectionStress.Of(new ISection("IPE300", 0.300, 0.150, 0.0107, 0.150, 0.0107, 0.0071, null)),
                   Path.Combine(folder, "force5.txt"), Path.Combine(folder, "ipe_fibres.txt"), 1e-2,
                   "IPE 300, N + My + Mz");
    }

    // ---------------------------------------------------------------- the whole path

    /// <summary>
    /// A beam with nothing but a tag and a section, which is all Read.BeamStresses asks of one. A
    /// real ForceBeamColumn needs a Rhino curve, and a curve needs the native library that only
    /// loads inside Rhino.
    /// </summary>
    class StubBeam : Alpaca4d.Generic.IBeam
    {
        public Rhino.Geometry.Curve Curve { get; set; }
        public Alpaca4d.Element.GeomTransf GeomTransf { get; set; }
        public Alpaca4d.Generic.IUniaxialSection Section { get; set; }
        public Alpaca4d.Generic.IIntegration BeamIntegration { get; set; }
        public int? INode { get; set; }
        public int? JNode { get; set; }
        public System.Drawing.Color Color { get; set; }
        public Alpaca4d.Element.ElementType Type => Alpaca4d.Element.ElementType.Beam;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public void SetTags() { }
        public void SetTopologyRTree(Alpaca4d.Model model) { }
        public string WriteTcl() => "";
        public Alpaca4d.Generic.IBeam WithSection(Alpaca4d.Generic.IUniaxialSection section) =>
            new StubBeam { Id = Id, ElementId = ElementId, Section = section, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(section, BeamIntegration?.IntegrationPoint ?? 5) };
    }

    static void ReaderTests(string folder)
    {
        Section("From the recorder file, the way Alpaca4d writes a beam");

        string file = Path.Combine(folder, "beam.mpco");
        if (!File.Exists(file))
        {
            Console.WriteLine("  (skipped: mpco.tcl was not solved - no OpenSees)");
            return;
        }

        var rectangle = new RectangleCS("r", 0.2, 0.4, null);
        var elastic = new ElasticSection("e", 0.01, 1e-4, 2e-5, 1e-5, 0.8, 0.8, null);

        var model = new Alpaca4d.Model
        {
            Recorders = new List<Alpaca4d.Generic.IRecorder> { new Alpaca4d.Recorder { FileName = file } },
            Beams = new List<Alpaca4d.Generic.IBeam>
            {
                new StubBeam { Id = 1, Section = rectangle },
                new StubBeam { Id = 2, Section = elastic },
            }
        };

        var all = Read.BeamStresses(model, 0);
        Check(all.Count == 2 && all[0].Count == 5 && all[1].Count == 5,
              "one list per beam, one entry per Newton-Cotes section");

        // At the fixed end of beam 1: N 12000, Vy 3000, Vz 2000, T 700, Mz 3000·3, My 2000·3, every
        // one of them distinct so a force read into the wrong slot reads as the wrong stress.
        double a = 0.08, iz = 0.2 * 0.4 * 0.4 * 0.4 / 12, iy = 0.4 * 0.2 * 0.2 * 0.2 / 12;
        var root = all[0][0];
        Close(root.SigmaN, 12000.0 / a, 1e-6, "σN = N/A");
        Close(root.SigmaMz, 9000.0 * 0.2 / iz, 1e-6, "σMz from the Mz column, over the depth");
        Close(root.SigmaMy, 6000.0 * 0.1 / iy, 1e-6, "σMy from the My column, over the width");
        Close(root.SigmaMax, 12000.0 / a + 9000.0 * 0.2 / iz + 6000.0 * 0.1 / iy, 1e-6, "σmax");
        Close(root.SigmaMin, 12000.0 / a - 9000.0 * 0.2 / iz - 6000.0 * 0.1 / iy, 1e-6, "σmin");
        Close(root.TauV, Math.Sqrt(Math.Pow(1.5 * 3000.0 / a, 2) + Math.Pow(1.5 * 2000.0 / a, 2)), 1e-6,
              "τV from the Vy and Vz columns");
        Close(root.TauT, BeamSectionStress.Of(rectangle).Torsion * 700.0, 1e-6, "τT from the T column");

        var tip = all[0][4];
        Check(Math.Abs(tip.SigmaMy) < 1e-6 * root.SigmaMy && Math.Abs(tip.SigmaMz) < 1e-6 * root.SigmaMz,
              "no bending at the free end");
        Close(tip.TauV, root.TauV, 1e-6, "and the same shear all along");

        var shapeless = all[1][0];
        Check(!shapeless.HasShape, "the Elastic Section beam is marked as having no shape");
        Close(shapeless.SigmaN, -4000.0 / 0.01, 1e-6, "and still reads σN, in compression");
    }

    static void Main(string[] args)
    {
        RectangleTests();
        CircleTests();
        HollowTests();
        ITests();
        DoubleAngleTests();
        OtherTests();
        PublishedISectionTests();
        PublishedCircleTests();
        FiniteElementTests();
        SurfaceTests();
        SurfaceFiniteElementTests();
        SolverTests(args.Length > 0 ? args[0] : ".");
        ReaderTests(args.Length > 0 ? args[0] : ".");

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
