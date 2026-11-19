// EN 1995-1-1 utilisation of timber members.
//
// Published worked examples first: Swedish Wood, Design of timber structures, Volume 3: Examples,
// edition 3:2022, which follows EN 1995-1-1 without a National Annex - each to the digits printed.
// The rules of the standard - k_mod, γM, k_h, k_c, k_crit, k_shape - are checked value by value
// against the tables and expressions they come from. Members are checked against the same expressions
// written out by hand here, independently of Ec5.cs, for every output: tension, compression,
// bending, shear, torsion, (6.11) to (6.20), (6.23), (6.24), (6.33) and (6.35). M_cr is checked
// against EN 1995-1-1's own closed form (6.32) and its Table 6.1. Load durations are checked from the
// patterns to k_mod. And the whole chain - recorder file, forces, check - is run on a deck OpenSees
// solves here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Alpaca4d;
using Alpaca4d.Design;
using Alpaca4d.Generic;
using Alpaca4d.Loads;
using Alpaca4d.Material;
using Alpaca4d.Result;
using Alpaca4d.Section;

class TimberCheckTest
{
    static int fails = 0;
    static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Close(double? got, double want, double tol, string what)
    {
        bool ok = got.HasValue && Math.Abs(got.Value - want) <= tol * Math.Max(1e-12, Math.Abs(want));
        if (!ok) fails++;
        string shown = got.HasValue ? got.Value.ToString("G6", Invariant) : "null";
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {shown}, want {want.ToString("G6", Invariant)}");
    }

    /// <summary>Agrees with a printed value to every digit printed.</summary>
    static void Printed(double? got, string printed, string what)
    {
        double want = double.Parse(printed, Invariant);
        int decimals = printed.Contains(".") ? printed.Length - printed.IndexOf('.') - 1 : 0;
        bool ok = got.HasValue && Math.Abs(Math.Round(got.Value, decimals, MidpointRounding.AwayFromZero) - want) <= 1e-9 * Math.Max(1.0, Math.Abs(want));
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {(got.HasValue ? got.Value.ToString("G6", Invariant) : "null")}, printed {printed}");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    // Model units are kN and m, so a stress of 1 N/mm² is 1000 kN/m².
    const double MPa = 1000.0;

    static UniaxialMaterialElastic Timber(string grade)
    {
        var g = TimberGrade.Find(grade);
        return new UniaxialMaterialElastic(grade, g.E0Mean * MPa, g.E0Mean * MPa, 0.0, g.GMean * MPa, 0.3, g.RhoMean) { Grade = g };
    }

    /// <summary>Five integration sections, all with the same forces.</summary>
    static List<SectionForces> Uniform(double n = 0, double vy = 0, double vz = 0, double t = 0, double my = 0, double mz = 0)
    {
        return Enumerable.Range(0, 5).Select(_ => new SectionForces(n, vy, vz, t, my, mz)).ToList();
    }

    static MemberUtilisation Run(IUniaxialSection section, List<SectionForces> forces, SteelMember member,
                                 LoadDuration duration = LoadDuration.MediumTerm, bool accidental = false, double? gammaM = null)
    {
        var grade = (TimberGrade)DesignGrades.Of(section.Material);
        var timber = TimberSection.Of(section, out string why);
        if (timber == null) throw new Exception(why);
        var settings = new DesignSettings { Duration = duration, Accidental = accidental, GammaM = gammaM, Report = true };
        return TimberCheck.Run(timber, grade, forces, member, settings);
    }

    // Hand formulas, written out again rather than taken from Ec5.cs.
    static double KcHand(double lambda, double fc0k, double e005, double betaC)
    {
        double rel = lambda / Math.PI * Math.Sqrt(fc0k / e005);
        if (rel <= 0.3) return 1.0;
        double k = 0.5 * (1 + betaC * (rel - 0.3) + rel * rel);
        return 1 / (k + Math.Sqrt(k * k - rel * rel));
    }

    static double KcritHand(double rel) => rel <= 0.75 ? 1.0 : rel <= 1.4 ? 1.56 - 0.75 * rel : 1 / (rel * rel);

    // ---------------------------------------------------------------- published: Swedish Wood, Volume 3

    static void SwedishWoodJoist()
    {
        // Example 3.1: a simply supported C24 joist, 45 x 220, span 4.5 m, q_d = 0.6 x 3.3 = 2.0 kN/m,
        // permanent and medium-term loads, service class 1. M_Ed = 5.1 kNm, V_Ed = 4.5 kN,
        // f_m,d = 14.8 MPa, f_v,d = 2.46 MPa, V_Rd = 2/3 k_cr A f_v,d = 10.9 kN. Braced by the floor.
        Section("Published: Swedish Wood, Volume 3 (2022), Example 3.1, C24 joist 45 x 220");
        var joist = new RectangleCS("C24 45x220", 0.045, 0.22, Timber("C24"));
        int n = 9;
        var forces = Enumerable.Range(0, n).Select(k => { double x = k / 8.0; return new SectionForces(0, 0, 2.0 * 4.5 * (0.5 - x), 0, 0, 2.0 * 4.5 * 4.5 * x * (1 - x) / 2); }).ToList();
        var u = Run(joist, forces, new SteelMember(4.5) { LateralTorsionalLength = 0.6 });
        var d = u.Details;
        Printed(d.Kmod, "0.8", "k_mod, medium-term, service class 1");
        Printed(d.KhZ, "1.0", "k_h for the 220 mm depth");
        Printed(d.FmZd / MPa, "14.8", "f_m,d [MPa]");
        Printed(d.Fvd / MPa, "2.46", "f_v,d [MPa]");
        Printed(2.0 / 3.0 * 0.67 * 0.045 * 0.22 * d.Fvd, "10.9", "V_Rd = 2/3 k_cr A f_v,d [kN]");
        Printed(forces.Max(f => f.Mz), "5.1", "M_Ed [kNm]");
        Close(u.ShearZ, 4.5 / 10.9, 0.005, "ShearZ against V_Ed / V_Rd = 4.5 / 10.9");
        Close(u.BendingZ, 5.0625 / (0.045 * 0.22 * 0.22 / 6) / d.FmZd, 1e-9, "BendingZ, M_Ed / (W f_m,d), the book's 5.1 / (363 cm³ × 14.8) = 0.95 with M rounded");
        Close(u.LateralTorsional, u.BendingZ.Value, 1e-9, "held by the floor every 0.6 m: no lateral-torsional buckling");
    }

    static void SwedishWoodGlulamBeam()
    {
        // Example 3.4: a simply supported GL30c beam, 115 x 630, 10 m, point loads on its top, service
        // class 1, medium-term: M_Ed = 74.6 kNm at mid-span, V_Ed = 15.8 kN at the ends. W_y = 7.61e6
        // mm³, f_m,y,d = 19.20 MPa, b_ef = 77 mm, τ_d = 0.48 MPa, f_v,d = 2.24 MPa. For lateral-torsional
        // buckling the book takes l_ef = 0.8 l + 2h and (6.33): σ_m,crit = 19.1 MPa, λ_rel,m = 1.25,
        // k_crit = 0.62, M_Rd = 90.6 kNm.
        Section("Published: Swedish Wood, Volume 3 (2022), Example 3.4, GL30c beam 115 x 630, 10 m");
        var beam = new RectangleCS("GL30c 115x630", 0.115, 0.63, Timber("GL30c"));
        int n = 9;
        var forces = Enumerable.Range(0, n).Select(k => { double x = k / 8.0; return new SectionForces(0, 0, x < 0.5 ? 15.8 : -15.8, 0, 0, 74.6 * (1 - Math.Abs(2 * x - 1))); }).ToList();
        var u = Run(beam, forces, new SteelMember(10.0) { Load = LoadPosition.Destabilising });
        var d = u.Details;
        Printed(d.GammaM, "1.25", "γM, glulam");
        Printed(d.KhZ, "1.0", "k_h, deeper than 600 mm");
        Printed(d.Wz * 1e3, "7.61", "W_y [10⁶ mm³]");
        Printed(d.FmZd / MPa, "19.20", "f_m,y,d [MPa]");
        Printed(d.Fvd / MPa, "2.24", "f_v,d [MPa]");
        Printed(0.67 * 115, "77", "b_ef = k_cr b [mm]");
        // 1.5 × 15.8e3 / (77 × 630) = 0.4886, which the book prints as 0.48: cut, not rounded.
        Close(1.5 * 15.8 / (0.67 * 0.115 * 0.63) / MPa, 0.48, 0.02, "τ_d [MPa], the book's 0.4886 cut to 0.48");
        Close(u.BendingZ, 74.6 / (19.2 * 7.6144), 0.001, "BendingZ, 74.6 / (19.2 MPa × 7.61e6 mm³)");
        Close(u.ShearZ, 0.48 / 2.24, 0.02, "ShearZ, τ_d / f_v,d = 0.48 / 2.24, the book's b_ef and τ_d rounded");

        // The book's σ_m,crit through our k_crit: the same λ_rel,m and k_crit, M_Rd and utilisation.
        double crit = 0.78 * 0.115 * 0.115 / (0.63 * (0.8 * 10 + 2 * 0.63)) * 10800;
        Printed(crit, "19.1", "σ_m,crit by (6.33), l_ef = 0.8 l + 2h [MPa]");
        double rel = Ec5.RelativeSlendernessM(30, crit);
        Printed(rel, "1.25", "λ_rel,m (6.30)");
        Printed(Ec5.Kcrit(rel), "0.62", "k_crit (6.34)");
        Printed(19.2 * 7.6144 * Ec5.Kcrit(rel), "90.6", "M_Rd = f_m,y,d W_y k_crit [kNm]");

        // Ours is on the safe side of it: the triangle of a point load is not a parabola, so its peak
        // is taken as uniform over the length (C1 = 1, where Table 6.1 credits 0.8 l), and σ_m,crit is
        // (6.32) with G_0,05 = 540 and the section's own torsion constant, where (6.33) - for solid
        // softwood - implies G/E = 1/16 for glulam's 1/20.
        Check(u.LateralTorsional > 74.6 / 90.6, $"LTB {u.LateralTorsional:0.000}, on the safe side of the book's {74.6 / 90.6:0.000}");
        Check(u.Report.Contains("do not lie on a parabola"), "and the report says why: the moments do not lie on a parabola");
    }

    // ---------------------------------------------------------------- the rules on their own

    static void Rules()
    {
        Section("Rules: Tables 2.3 and 3.1, k_h, k_c, k_crit, k_shape, torsion");

        Close(Ec5.Kmod(1, LoadDuration.Permanent), 0.60, 1e-12, "k_mod, service class 1, permanent");
        Close(Ec5.Kmod(2, LoadDuration.MediumTerm), 0.80, 1e-12, "k_mod, service class 2, medium-term");
        Close(Ec5.Kmod(1, LoadDuration.Instantaneous), 1.10, 1e-12, "k_mod, service class 1, instantaneous");
        Close(Ec5.Kmod(3, LoadDuration.LongTerm), 0.55, 1e-12, "k_mod, service class 3, long-term");
        Close(Ec5.Kmod(3, LoadDuration.ShortTerm), 0.70, 1e-12, "k_mod, service class 3, short-term");
        bool threw = false;
        try { Ec5.Kmod(4, LoadDuration.Permanent); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "service class 4 is refused");

        Close(Ec5.GammaM(TimberKind.Solid, false), 1.3, 1e-12, "γM solid timber");
        Close(Ec5.GammaM(TimberKind.Glulam, false), 1.25, 1e-12, "γM glulam");
        Close(Ec5.GammaM(TimberKind.Glulam, true), 1.0, 1e-12, "γM accidental");

        Close(Ec5.Kh(TimberKind.Solid, 350, 100), Math.Pow(1.5, 0.2), 1e-12, "k_h solid, 100 mm: (150/h)^0.2 (3.1)");
        Close(Ec5.Kh(TimberKind.Solid, 350, 50), Math.Pow(3.0, 0.2), 1e-12, "k_h solid, 50 mm");
        Close(Ec5.Kh(TimberKind.Solid, 350, 30), 1.3, 1e-12, "k_h solid, 30 mm: capped at 1.3");
        Close(Ec5.Kh(TimberKind.Solid, 350, 200), 1.0, 1e-12, "k_h solid, 200 mm: 1");
        Close(Ec5.Kh(TimberKind.Solid, 800, 100), 1.0, 1e-12, "k_h solid past ρ_k 700: 1");
        Close(Ec5.Kh(TimberKind.Glulam, 385, 300), Math.Pow(2.0, 0.1), 1e-12, "k_h glulam, 300 mm: (600/h)^0.1 (3.2)");
        Close(Ec5.Kh(TimberKind.Glulam, 385, 100), 1.1, 1e-12, "k_h glulam, 100 mm: capped at 1.1");
        Close(Ec5.Kh(TimberKind.Glulam, 385, 800), 1.0, 1e-12, "k_h glulam, 800 mm: 1");

        // λ_rel = 1, β_c = 0.2: k = 0.5 (1 + 0.2 × 0.7 + 1) = 1.07, k_c = 1 / (1.07 + √(1.07² − 1)).
        Close(Ec5.Kc(1.0, 0.2), 1 / (1.07 + Math.Sqrt(1.07 * 1.07 - 1)), 1e-12, "k_c at λ_rel 1, β_c 0.2 (6.25, 6.27)");
        Close(Ec5.Kc(0.3, 0.2), 1.0, 1e-12, "k_c at λ_rel 0.3: 1");
        Close(Ec5.Kc(0.30001, 0.1), 1.0, 1e-4, "k_c just past 0.3: continuous");

        Close(Ec5.Kcrit(0.75), 1.0, 1e-12, "k_crit at λ_rel,m 0.75 (6.34)");
        Close(Ec5.Kcrit(1.0), 0.81, 1e-12, "k_crit at 1.0: 1.56 − 0.75");
        Close(Ec5.Kcrit(1.4), 0.51, 1e-12, "k_crit at 1.4");
        Close(Ec5.Kcrit(2.0), 0.25, 1e-12, "k_crit at 2.0: 1/λ²");

        Close(Ec5.KshapeRectangle(0.1, 0.2), 1.3, 1e-12, "k_shape 100 x 200: 1 + 0.15 × 2 (6.15)");
        Close(Ec5.KshapeRectangle(1.0, 0.1), 2.0, 1e-12, "k_shape 100 x 1000: capped at 2");

        // A square: T / (0.208 b³) (Timoshenko); a thin strip: 3T / (h b²).
        Close(Ec5.TorsionStressRectangle(1.0, 0.1, 0.1), 1.0 / (0.208 * 1e-3), 2e-3, "torsion of a square, T / (0.208 b³)");
        Close(Ec5.TorsionStressRectangle(1.0, 1.0, 0.001), 3.0 / (1.0 * 1e-6), 1e-3, "torsion of a thin strip, 3T / (h b²)");
        // Roark's α for h/b = 2 is 0.246: τ = T / (α h b²).
        Close(Ec5.TorsionStressRectangle(1.0, 0.2, 0.1), 1.0 / (0.246 * 0.2 * 0.01), 3e-3, "torsion of 2:1, T / (0.246 h b²)");
        Close(Ec5.TorsionStressCircle(1.0, 0.2), 16 / (Math.PI * 0.008), 1e-12, "torsion of a circle, 16T / (π d³)");
    }

    static void Grades()
    {
        Section("Grades: EN 338:2016 and EN 14080 from the timber table");

        var c24 = TimberGrade.Find("C24");
        Check(c24 != null && c24.Kind == TimberKind.Solid, "C24 found, solid timber");
        Close(c24.FmK, 24, 1e-12, "C24 f_m,k");
        Close(c24.E0Mean, 11000, 1e-12, "C24 E_0,mean, EN 338:2016");
        Close(c24.RhoMean, 420, 1e-12, "C24 ρ_mean, EN 338:2016");
        Close(c24.G005, 690.0 * 7400 / 11000, 1e-12, "C24 G_0,05 = G_mean E_0,05 / E_0,mean");
        Check(!c24.G005Tabulated, "and said to be worked out, not tabulated");

        var gl = TimberGrade.Find("gl 24 h");
        Check(gl != null && gl.Name == "GL24h" && gl.Kind == TimberKind.Glulam, "\"gl 24 h\" finds GL24h, glulam");
        Close(gl.Ft0K, 19.2, 1e-12, "GL24h f_t,0,k");
        Close(gl.G005, 540, 1e-12, "GL24h G_g,05 as tabulated");
        Check(TimberGrade.Find("GL28c")?.Fc0K == 24.0, "GL28c f_c,0,k 24");
        Check(TimberGrade.Find("D40")?.FmK == 40.0, "D40 f_m,k 40");
        Check(TimberGrade.Find("C50")?.E005 == 10700, "C50 E_0,05 10 700");
        Check(TimberGrade.Find("S355") == null, "S355 is no timber class");

        var named = new UniaxialMaterialElastic("GL28c", 12.5e6, 12.5e6, 0, 0.65e6, 0.3, 420);
        Check(DesignGrades.Of(named) is TimberGrade, "a material named GL28c is checked as timber");
        var steel = new UniaxialMaterialElastic("S355", 210e6, 210e6, 0, 80.77e6, 0.3, 7850);
        Check(DesignGrades.Of(steel) is SteelGrade, "and one named S355 as steel still");

        var oak = TimberGrade.Find("GOB_Green_Oak");
        Check(oak != null && oak.Missing().Contains("f_t,0,k"), "green oak lacks f_t,0,k");
    }

    // ---------------------------------------------------------------- sections, by hand

    static void Sections()
    {
        Section("Sections: C24 200 x 100, service class 1, medium-term (k_mod 0.8, γM 1.3)");

        // Width 0.1 (across which My bends), Height 0.2 (across which Mz bends).
        var rect = new RectangleCS("C24 100x200", 0.1, 0.2, Timber("C24"));
        var member = new SteelMember(3.0);
        double f = 0.8 / 1.3;
        double a = 0.02, wz = 0.1 * 0.04 / 6, wy = 0.2 * 0.01 / 6;
        double khY = Math.Pow(1.5, 0.2);                    // My bends across 100 mm
        double fmzd = f * 24 * MPa, fmyd = f * khY * 24 * MPa;
        double ft0d = f * 14.5 * MPa, fc0d = f * 21 * MPa, fvd = f * 4.0 * MPa; // largest dimension 200: k_h 1

        var u = Run(rect, Uniform(mz: 10), member);
        Check(u.Type == "Timber" && u.Checked, $"checked as timber: {u.Type}, {u.Checked}");
        Check(u.Class == null, "no cross-section class");
        Close(u.BendingZ, 10 / wz / fmzd, 1e-9, "Mz 10 kNm: σ_m,z,d / f_m,z,d (6.11)");
        Close(u.Combined, 10 / wz / fmzd, 1e-9, "Combined: the same, k_m on nothing");
        Close(u.Axial, 0.0, 1e-12, "no axial force");
        // LTB over 10 m between forks, M_cr by (6.32) with G_0,05 = 690 × 7400 / 11000.
        double mcr = Math.PI / 10.0 * Math.Sqrt(7400 * MPa * (0.2 * 0.001 / 12) * (690.0 * 7400 / 11000 * MPa) * rect.J);
        double kcrit = KcritHand(Math.Sqrt(24 * MPa / (mcr / wz)));
        var slender = Run(rect, Uniform(mz: 5), new SteelMember(10.0));
        Close(slender.LateralTorsional, 5 / wz / (kcrit * fmzd), 1e-4, $"LTB of solid timber over 10 m, k_crit = {kcrit:0.0000}");
        Check(kcrit < 0.9, "and k_crit well below 1, so G_0,05 counts");

        // Tension with bending about both axes, (6.17) and (6.18).
        double n = 50, my = 2, mz = 6;
        u = Run(rect, Uniform(n: n, my: my, mz: mz), member);
        double t = n / a / ft0d, ry = my / wy / fmyd, rz = mz / wz / fmzd;
        Close(u.Axial, t, 1e-9, "N +50 kN: σ_t,0,d / f_t,0,d (6.1.2)");
        Close(u.BendingY, ry, 1e-9, "My 2 kNm, with k_h of the 100 mm side");
        Close(u.Combined, Math.Max(t + ry + 0.7 * rz, t + 0.7 * ry + rz), 1e-9, "Combined: (6.17), (6.18)");

        // Compression with bending, (6.19) and (6.20).
        u = Run(rect, Uniform(n: -60, my: my, mz: mz), member);
        double c = 60 / a / fc0d;
        Close(u.Axial, c, 1e-9, "N −60 kN: σ_c,0,d / f_c,0,d (6.1.4)");
        Close(u.Combined, Math.Max(c * c + ry + 0.7 * rz, c * c + 0.7 * ry + rz), 1e-9, "Combined: (6.19), (6.20)");

        // Shear, with k_cr, and torsion with k_shape.
        u = Run(rect, Uniform(vy: 12, vz: 20, t: 1.0), member);
        Close(u.ShearY, 1.5 * 12 / (0.67 * a) / fvd, 1e-9, "Vy 12 kN: 1.5 V / (k_cr A) against f_v,d (6.1.7)");
        Close(u.ShearZ, 1.5 * 20 / (0.67 * a) / fvd, 1e-9, "Vz 20 kN");
        double tau = 3 * 1.0 / (0.2 * 0.01) * (1 + 0.5 * (0.6095 + 0.5 * (0.8865 + 0.5 * (-1.8023 + 0.5 * 0.91))));
        Close(u.Torsion, tau / (1.3 * fvd), 1e-9, "T 1 kNm: τ_tor,d against k_shape f_v,d, k_shape 1.3 (6.1.8)");

        // Service class, duration, accidental and a γM given.
        u = Run(rect, Uniform(mz: 10), member, LoadDuration.Permanent);
        Close(u.BendingZ, 10 / wz / (0.6 / 1.3 * 24 * MPa), 1e-9, "permanent: k_mod 0.6");
        u = Run(rect, Uniform(mz: 10), new SteelMember(3.0) { ServiceClass = 3 }, LoadDuration.ShortTerm);
        Close(u.BendingZ, 10 / wz / (0.7 / 1.3 * 24 * MPa), 1e-9, "service class 3, short-term: k_mod 0.7");
        u = Run(rect, Uniform(mz: 10), member, LoadDuration.Instantaneous, accidental: true);
        Close(u.BendingZ, 10 / wz / (1.1 / 1.0 * 24 * MPa), 1e-9, "accidental, instantaneous: k_mod 1.1, γM 1.0");
        u = Run(rect, Uniform(mz: 10), member, gammaM: 1.5);
        Close(u.BendingZ, 10 / wz / (0.8 / 1.5 * 24 * MPa), 1e-9, "γM 1.5 given");
        Check(u.Report.Contains("γM = 1.5 (given)"), "and the report says it was given");

        Section("Sections: GL24h 120 high x 80 wide, k_h of glulam");
        var small = new RectangleCS("GL24h 80x120", 0.08, 0.12, Timber("GL24h"));
        u = Run(small, Uniform(n: 20, mz: 3), new SteelMember(2.0));
        double g = 0.8 / 1.25;
        double khBend = Math.Min(Math.Pow(600.0 / 120, 0.1), 1.1), khTension = Math.Min(Math.Pow(600.0 / 120, 0.1), 1.1);
        Close(u.BendingZ, 3 / (0.08 * 0.0144 / 6) / (g * khBend * 24 * MPa), 1e-9, "Mz: f_m,d with k_h (600/120)^0.1");
        Close(u.Axial, 20 / 0.0096 / (g * khTension * 19.2 * MPa), 1e-9, "N: f_t,0,d with k_h of the largest side");

        Section("Sections: C24 round 200");
        var round = new CircleCS("C24 d200", 0.2, 0.0, Timber("C24"));
        u = Run(round, Uniform(vy: 10, t: 2, my: 3, mz: 4), new SteelMember(3.0));
        double ar = Math.PI * 0.01, wr = Math.PI * 0.008 / 32;
        Close(u.ShearY, 4.0 / 3.0 * 10 / (0.67 * ar) / fvd, 1e-9, "Vy: 4V / (3 k_cr A)");
        Close(u.Torsion, 16 * 2 / (Math.PI * 0.008) / (1.2 * fvd), 1e-9, "T: 16T / (π d³) against 1.2 f_v,d");
        Close(u.Combined, 3 / wr / fmzd + 4 / wr / fmzd, 1e-9, "Combined: k_m 1.0 for a circle, both moments in full");
        Close(u.LateralTorsional, 0.0, 1e-12, "LTB: none for a circle");

        Section("Sections the check does not cover");
        var tube = new CircleCS("tube", 0.2, 0.02, Timber("C24"));
        var notTube = Utilisations.Check(null, tube, Uniform(mz: 1), new SteelMember(3.0), new DesignSettings(), null);
        Check(notTube.Type == "Timber" && !notTube.Checked && notTube.NotCheckedWhy.Contains("hollow"), $"a hollow circle: {notTube.NotCheckedWhy}");
        var i = new Alpaca4d.Section.ISection("I", 0.3, 0.15, 0.02, 0.15, 0.02, 0.02, Timber("GL24h"));
        var notI = Utilisations.Check(null, i, Uniform(mz: 1), new SteelMember(3.0), new DesignSettings(), null);
        Check(!notI.Checked && notI.Max == null && notI.NotCheckedWhy != null, $"an I: {notI.NotCheckedWhy}");
        var oak = new RectangleCS("oak", 0.1, 0.2, new UniaxialMaterialElastic("GOB_Green_Oak", 9.26e6, 9.26e6, 0, 0.59e6, 0.3, 1000));
        var notOak = Utilisations.Check(null, oak, Uniform(mz: 1), new SteelMember(3.0), new DesignSettings(), null);
        Check(!notOak.Checked && notOak.NotCheckedWhy.Contains("f_t,0,k"), $"green oak: {notOak.NotCheckedWhy}");

        Section("Each type its own material: DesignSettings.Family");
        var bySteel = Utilisations.Check(null, rect, Uniform(mz: 10), member, new DesignSettings { Family = "Steel" }, null);
        Check(bySteel.Type == "Timber" && !bySteel.Checked && bySteel.Max == null && bySteel.NotCheckedWhy.Contains("timber"),
              $"timber under the steel check: not checked, {bySteel.NotCheckedWhy}");
        var byTimber = Utilisations.Check(null, rect, Uniform(mz: 10), member, new DesignSettings { Family = "Timber", Duration = LoadDuration.MediumTerm }, null);
        Close(byTimber.BendingZ, 10 / wz / fmzd, 1e-9, "under the timber check: checked");
        Close(byTimber.Kmod, 0.8, 1e-12, "with its k_mod given back");
        var s355 = new RectangleCS("S355 bar", 0.1, 0.2, new UniaxialMaterialElastic("S355", 210e6, 210e6, 0, 80.77e6, 0.3, 7850));
        var steelByTimber = Utilisations.Check(null, s355, Uniform(mz: 10), member, new DesignSettings { Family = "Timber" }, null);
        Check(steelByTimber.Type == "Steel" && !steelByTimber.Checked && steelByTimber.Kmod == null, "steel under the timber check: not checked");
        Check(Utilisations.Check(null, s355, Uniform(mz: 10), member, new DesignSettings { Family = "Steel" }, null).Checked, "and under the steel check it is");
    }

    // ---------------------------------------------------------------- members, by hand

    static void Columns()
    {
        Section("Members: C24 200 x 100 column, 3 m, (6.23) and (6.24)");

        var rect = new RectangleCS("C24 100x200", 0.1, 0.2, Timber("C24"));
        double f = 0.8 / 1.3, a = 0.02, wz = 0.1 * 0.04 / 6, wy = 0.2 * 0.01 / 6;
        double fc0d = f * 21 * MPa, fmzd = f * 24 * MPa, fmyd = f * Math.Pow(1.5, 0.2) * 24 * MPa;
        double iy = 0.1 / Math.Sqrt(12), iz = 0.2 / Math.Sqrt(12);
        double kcY = KcHand(3.0 / iy, 21, 7400, 0.2), kcZ = KcHand(3.0 / iz, 21, 7400, 0.2);

        var u = Run(rect, Uniform(n: -50), new SteelMember(3.0));
        double c = 50 / a;
        Close(u.BucklingY, c / (kcY * fc0d), 1e-9, $"BucklingY: σ_c,0,d / (k_c,y f_c,0,d), k_c,y = {kcY:0.0000}");
        Close(u.BucklingZ, c / (kcZ * fc0d), 1e-9, $"BucklingZ: k_c,z = {kcZ:0.0000}");

        var forces = Enumerable.Range(0, 5).Select(k => new SectionForces(-50, 0, 0, 0, 0.5, 4.0 * k / 4.0)).ToList();
        u = Run(rect, forces, new SteelMember(3.0));
        double ry = 0.5 / wy / fmyd, rz = 4.0 / wz / fmzd;
        Close(u.InteractionY, c / (kcY * fc0d) + ry + 0.7 * rz, 1e-9, "InteractionY (6.23), at the section of the largest Mz");
        Close(u.InteractionZ, c / (kcZ * fc0d) + 0.7 * ry + rz, 1e-9, "InteractionZ (6.24)");

        // Short and stocky: λ_rel ≤ 0.3 about both axes, so (6.19) and (6.20) are all 6.3.2(2) asks.
        u = Run(rect, Uniform(n: -50, mz: 1), new SteelMember(0.4));
        Check(u.InteractionY == 0.0 && u.InteractionZ == 0.0, "λ_rel ≤ 0.3 both ways: no (6.23), (6.24)");
        Close(u.BucklingY, c / fc0d, 1e-9, "and k_c = 1");

        // Buckling lengths given per axis.
        u = Run(rect, Uniform(n: -50), new SteelMember(3.0) { BucklingLengthY = 1.5 });
        Close(u.BucklingY, c / (KcHand(1.5 / iy, 21, 7400, 0.2) * fc0d), 1e-9, "L_cr,y 1.5 m given");
        Close(u.BucklingZ, c / (kcZ * fc0d), 1e-9, "and L_cr,z still the length");

        // Glulam: β_c = 0.1.
        var gl = new RectangleCS("GL24h", 0.1, 0.2, Timber("GL24h"));
        u = Run(gl, Uniform(n: -50), new SteelMember(3.0));
        Close(u.BucklingY, c / (KcHand(3.0 / iy, 24, 9600, 0.1) * (0.8 / 1.25 * 24 * MPa)), 1e-9, "glulam: β_c 0.1");
    }

    static void LateralTorsional()
    {
        Section("Members: GL24h 600 x 140 beam, 8 m, lateral-torsional buckling (6.3.3)");

        var section = new RectangleCS("GL24h 140x600", 0.14, 0.6, Timber("GL24h"));
        double e = 9600 * MPa, g = 540 * MPa, ilat = 0.6 * Math.Pow(0.14, 3) / 12, it = section.J;
        double w = 0.14 * 0.36 / 6, l = 8.0;
        double fmd = 0.8 / 1.25 * 24 * MPa;  // k_h 1 at 600 mm

        // (6.32) for a uniform moment between fork supports: σ_m,crit = π √(E_0,05 Iz G_0,05 Itor) / (l_ef W).
        double mcr = Math.PI / l * Math.Sqrt(e * ilat * g * it);
        double rel = Math.Sqrt(24 * MPa / (mcr / w));
        double kcrit = KcritHand(rel);
        var u = Run(section, Uniform(mz: 60), new SteelMember(l));
        Close(u.LateralTorsional, 60 / w / (kcrit * fmd), 1e-4, $"uniform 60 kNm: σ_m,d / (k_crit f_m,d), M_cr by (6.32), k_crit = {kcrit:0.0000}");
        Check(u.Report.Contains("(6.33)"), "the report names (6.33)");

        // With compression, (6.35): k_c about the minor axis, local y here.
        u = Run(section, Uniform(n: -100, mz: 60), new SteelMember(l));
        double kcMinor = KcHand(l / (0.14 / Math.Sqrt(12)), 24, 9600, 0.1);
        double b = 60 / w / (kcrit * fmd), cterm = 100 / (0.14 * 0.6) / (kcMinor * 0.8 / 1.25 * 24 * MPa);
        Close(u.LateralTorsional, Math.Max(b, b * b + cterm), 1e-4, "N −100 kN with it: (6.35)");

        // Restrained at mid-span: two pieces of 4 m, each under a uniform moment.
        u = Run(section, Uniform(mz: 60), new SteelMember(l) { LateralTorsionalLength = 4.0 });
        double rel4 = Math.Sqrt(24 * MPa / (2 * mcr / w));
        Close(u.LateralTorsional, 60 / w / (KcritHand(rel4) * fmd), 1e-4, "restrained at mid-span: L_LT 4 m");

        // Table 6.1's effective lengths against the energy solution. A uniform load between forks:
        // l_ef = 0.9 l; C1 = 1.13 by the energy solution, an l_ef of 0.885 l.
        int count = 9;
        var positions = Enumerable.Range(0, count).Select(k => k / (double)(count - 1)).ToList();
        var parabola = positions.Select(x => new SectionForces(0, 0, 0, 0, 0, 4 * 60 * x * (1 - x))).ToList();
        double mcrParabola = CriticalMoment.Multiplier(l, 0, 240, -240, 2 * 240 / (l * l), 0, e, ilat, 0, g, it) * 60;
        Close(mcrParabola / mcr, 1 / 0.9, 0.03, "uniform load at the centroid: M_cr within 3% of Table 6.1's l_ef = 0.9 l");
        double h = 0.6;
        double mcrTop = CriticalMoment.Multiplier(l, 0, 240, -240, 2 * 240 / (l * l), h / 2, e, ilat, 0, g, it) * 60;
        double tableTop = Math.PI / (0.9 * l + 2 * h) * Math.Sqrt(e * ilat * g * it);
        Close(mcrTop / tableTop, 1.0, 0.10, "on the top face: within 10% of l_ef = 0.9 l + 2h");
        var top = Run(section, parabola, new SteelMember(l) { Load = LoadPosition.Destabilising });
        var centre = Run(section, parabola, new SteelMember(l));
        Check(top.LateralTorsional > centre.LateralTorsional, $"a load on the top face is worse: {top.LateralTorsional:0.000} > {centre.LateralTorsional:0.000}");

        // A beam bent about its minor axis does not buckle sideways.
        u = Run(section, Uniform(my: 10), new SteelMember(l));
        Close(u.LateralTorsional, 0.0, 1e-12, "bending about the minor axis only: LTB 0");
    }

    static void ReportObject()
    {
        Section("The report: the same with it off, and as an object for Deconstruct Anything");

        // Restrained at thirds of a uniformly loaded 9 m span: the middle third is the worst.
        var gl = new RectangleCS("GL24h 140x600", 0.14, 0.6, Timber("GL24h"));
        int n = 9;
        var forces = Enumerable.Range(0, n).Select(k => { double x = k / (double)(n - 1); return new SectionForces(-20, 0, 30 * (1 - 2 * x), 0, 0, 400 * x * (1 - x)); }).ToList();
        var member = new SteelMember(9.0) { LateralTorsionalLength = 3.0 };
        var on = Run(gl, forces, member);
        var grade = (TimberGrade)DesignGrades.Of(gl.Material);
        var off = TimberCheck.Run(TimberSection.Of(gl, out _), grade, forces, member, new DesignSettings { Duration = LoadDuration.MediumTerm, Report = false });
        foreach (var check in on.Checks)
            Check(Nullable.Equals(check.Value, off.Checks.First(o => o.Key == check.Key).Value), $"{check.Key}: {Value(check.Value)} either way");
        Check(off.Report == "" && off.Details == null, "off: no text and no object");

        var d = on.Details;
        Check(d != null && d.ToString() == on.Report, "on: an object whose text is the report");
        Check(d.Grade == "GL24h" && d.Product == "glulam" && d.Kmod == 0.8 && d.GammaM == 1.25 && d.ServiceClass == 1 && d.Duration == "Medium-term",
              $"its values: {d.Grade}, {d.Product}, k_mod {d.Kmod}, γM {d.GammaM}, service class {d.ServiceClass}, {d.Duration}");
        Check(d.GammaMSource == "Table 2.3, glulam", $"γM from {d.GammaMSource}");
        Close(d.FmZd, 0.8 / 1.25 * 24 * MPa, 1e-12, "f_m,z,d");
        Close(d.Compression, 20, 1e-12, "the largest compression");
        Check(d.Kcrit.HasValue && d.Mcr.HasValue && d.SigmaMCrit.Value == d.Mcr.Value / d.Wz, $"M_cr {d.Mcr:0.##} and k_crit {d.Kcrit:0.###} of the worst piece");
        Check(string.Join(", ", d.Checks.Select(c => c.Name)) == "Axial, ShearY, ShearZ, Torsion, BendingY, BendingZ, Combined, BucklingY, BucklingZ, LTB, InteractionY, InteractionZ",
              "Checks, one per output, in their order");
        foreach (var check in d.Checks)
            Check(Nullable.Equals(check.Value, on.Checks.First(o => o.Key == check.Name).Value), $"{check.Name} {Value(check.Value)}, {check.Clause}, section {check.Section}");
        var bending = d.Checks.First(c => c.Name == "BendingZ");
        Check(bending.Section == 5 && bending.Position == 0.5, $"BendingZ governs mid-span: section {bending.Section}, x = {bending.Position} L");
        Check(d.Checks.First(c => c.Name == "ShearZ").Section == 1, "ShearZ at an end");
        Check(d.Checks.First(c => c.Name == "LTB").Clause == "6.3.3, (6.35)", "LTB with compression: (6.35)");
        Check(d.Checks.First(c => c.Name == "Combined").Clause.Contains("(6.19)"), "Combined in compression: (6.19), (6.20)");
        Check(d.Max == on.Max && d.Governing == on.Governing, $"Max {Value(d.Max)}, from {d.Governing}");
        Check(d.Checks.All(c => !string.IsNullOrEmpty(c.Detail)), "every check says how it was worked out");
    }

    static string Value(double? x) => x.HasValue ? x.Value.ToString("0.####", Invariant) : "-";

    // ---------------------------------------------------------------- durations, from the patterns

    static LoadPattern Pattern(string name, LoadDuration? duration, double factor = 1.0)
    {
        return new LoadPattern(PatternType.Plain, Alpaca4d.TimeSeries.Constant.Default(), new List<ILoad>(), factor) { Name = name, Duration = duration };
    }

    static void Durations()
    {
        Section("Durations: the shortest of the patterns that act (3.1.3(2))");

        var model = new Model { LoadPatterns = new List<LoadPattern> { Pattern("G", LoadDuration.Permanent), Pattern("Q", LoadDuration.MediumTerm), Pattern("W", LoadDuration.Instantaneous) } };
        Check(LoadDurations.Governing(model, out var unset) == LoadDuration.Instantaneous && unset.Count == 0, "G + Q + W: instantaneous");

        model.LoadPatterns[2].Factor = 0.0;
        Check(LoadDurations.Governing(model, out _) == LoadDuration.MediumTerm, "W scaled to nothing: medium-term");

        model.LoadPatterns.Add(Pattern("S", null));
        var d = LoadDurations.Governing(model, out unset);
        Check(d == LoadDuration.MediumTerm && unset.SequenceEqual(new[] { "S" }), "S with no duration is named, and counts as permanent");

        var settings = new DesignSettings().For(model);
        Check(settings.Duration == LoadDuration.MediumTerm && settings.DurationNote.Contains("set by Q") && settings.DurationNote.Contains("S"),
              $"settings for the model: {settings.DurationNote}");
        Check(new DesignSettings { Duration = LoadDuration.Permanent }.For(model).Duration == LoadDuration.Permanent, "a duration given is kept");

        Check(LoadDurations.TryParse("medium term", out var parsed) && parsed == LoadDuration.MediumTerm, "\"medium term\" reads");
        Check(LoadDurations.TryParse("Short-term", out parsed) && parsed == LoadDuration.ShortTerm, "\"Short-term\" reads");
        Check(!LoadDurations.TryParse("weekly", out _), "\"weekly\" does not");

        // A combination carries its patterns' durations, and an accidental one sets γM 1.0.
        var assembled = new Model
        {
            LoadPatterns = new List<LoadPattern> { Pattern("G", LoadDuration.Permanent), Pattern("Q", LoadDuration.MediumTerm), Pattern("A", LoadDuration.Instantaneous) },
            Tcl = new List<string>(),
            LoadPatternTclStart = 0,
        };
        var uls = LoadCombination.Parse("ULS 1", "1.35 G + 1.5 Q", CombinationKind.ULS, out _).Apply(assembled, out string problem);
        Check(uls != null, $"ULS 1 applied {problem}");
        Check(LoadDurations.Governing(uls, out _) == LoadDuration.MediumTerm, "1.35 G + 1.5 Q: medium-term, as Q");
        var dead = LoadCombination.Parse("ULS 0", "1.35 G", CombinationKind.ULS, out _).Apply(assembled, out _);
        Check(LoadDurations.Governing(dead, out _) == LoadDuration.Permanent, "1.35 G alone: permanent");
        var accident = LoadCombination.Parse("ACC", "G + 0.3 Q + A", CombinationKind.Accidental, out _).Apply(assembled, out _);
        var accidental = new DesignSettings().For(accident);
        Check(accidental.Accidental && accidental.Duration == LoadDuration.Instantaneous, "accidental G + 0.3 Q + A: γM 1.0, instantaneous");
        Check(!new DesignSettings().For(uls).Accidental, "ULS: not accidental");
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

        string file = Path.Combine(folder, "timber.mpco");
        if (!File.Exists(file))
        {
            Console.WriteLine("  (skipped: timber.tcl was not solved - no OpenSees)");
            return;
        }

        var c24 = new RectangleCS("C24 100x200", 0.1, 0.2, Timber("C24"));
        var gl = new RectangleCS("GL24h 140x400", 0.14, 0.4, Timber("GL24h"));
        var round = new CircleCS("C24 d200", 0.2, 0.0, Timber("C24"));
        var model = new Model
        {
            Recorders = new List<IRecorder> { new Recorder { FileName = file } },
            Beams = new List<IBeam>
            {
                new StubBeam { Id = 1, Section = c24, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(c24, 5) },
                new StubBeam { Id = 2, Section = gl, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(gl, 5) },
                new StubBeam { Id = 3, Section = round, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(round, 5) },
            }
        };

        // No patterns in the model: permanent, unless given.
        var members = new[] { new SteelMember(3.0), new SteelMember(3.0), new SteelMember(3.0) };
        var permanent = Utilisations.Of(model, 0, members, new DesignSettings());
        Check(permanent.All(u => u.Type == "Timber" && u.Checked), "three timber members, checked");
        Check(permanent[0].Report.Contains("permanent"), "with no patterns, permanent");

        var all = Utilisations.Of(model, 0, members, new DesignSettings { Duration = LoadDuration.MediumTerm });
        double f = 0.8 / 1.3;

        // Beam 1: C24, N = −20 kN, Vy = 3 kN, Mz = 9 kNm at the fixed end.
        double c = 20 / 0.02 / (f * 21 * MPa), rz = 9 / (0.1 * 0.04 / 6) / (f * 24 * MPa);
        Close(all[0].Axial, c, 1e-6, "beam 1 Axial");
        Close(all[0].BendingZ, rz, 1e-6, "beam 1 BendingZ, at the fixed end");
        Close(all[0].ShearY, 1.5 * 3 / (0.67 * 0.02) / (f * 4 * MPa), 1e-6, "beam 1 ShearY");
        Close(all[0].Combined, c * c + rz, 1e-6, "beam 1 Combined, (6.19)");
        Check(all[0].Report.Contains("x = 0 L"), "and it says the fixed end governs");
        Close(permanent[0].BendingZ, rz * 0.8 / 0.6, 1e-6, "permanent: k_mod 0.6 for 0.8");

        // Beam 2: GL24h, N = +30 kN, Vz = 4 kN, My = 12 kNm across the 140 mm side, T = 0.5 kNm.
        double g = 0.8 / 1.25;
        double khY = Math.Min(Math.Pow(600.0 / 140, 0.1), 1.1), khT = Math.Min(Math.Pow(600.0 / 400, 0.1), 1.1);
        double t = 30 / 0.056 / (g * khT * 19.2 * MPa), ry = 12 / (0.4 * 0.0196 / 6) / (g * khY * 24 * MPa);
        Close(all[1].Axial, t, 1e-6, "beam 2 Axial, tension with k_h of the 400 mm side");
        Close(all[1].BendingY, ry, 1e-6, "beam 2 BendingY, k_h of the 140 mm side");
        Close(all[1].Combined, Math.Max(t + ry, t + 0.7 * ry), 1e-6, "beam 2 Combined, (6.17)");
        Close(all[1].Torsion, Ec5.TorsionStressRectangle(0.5, 0.14, 0.4) / (Math.Min(1 + 0.15 * 0.4 / 0.14, 2) * g * 3.5 * MPa), 1e-6, "beam 2 Torsion");
        Check(all[1].BucklingY == 0.0 && all[1].LateralTorsional == 0.0, "beam 2 in tension, bent about its minor axis: no buckling, no LTB");

        // Beam 3: round, Mz = 6 kNm.
        Close(all[2].BendingZ, 6 / (Math.PI * 0.008 / 32) / (f * 24 * MPa), 1e-6, "beam 3 BendingZ, round");
    }

    static void Main(string[] args)
    {
        SwedishWoodJoist();
        SwedishWoodGlulamBeam();
        Rules();
        Grades();
        Sections();
        Columns();
        LateralTorsional();
        ReportObject();
        Durations();
        Chain(args.Length > 0 ? args[0] : ".");

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
