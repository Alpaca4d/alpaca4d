using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Alpaca4d.Design
{
    /// <summary>
    /// The EN 1993-1-1 check of one steel member: the cross-section at every integration section
    /// (6.2), and flexural buckling of the member as a whole (6.3.1).
    ///
    /// Not covered, and said so in every report: lateral-torsional buckling (6.3.2), bending with
    /// compression as a member (6.3.3), torsional and torsional-flexural buckling (6.3.1.4), warping
    /// torsion, shear buckling (EN 1993-1-5) and Class 4 sections. A member in compression and bending
    /// needs 6.3.3 on top of everything here before it can be called verified.
    /// </summary>
    public static class SteelCheck
    {
        private const string NotCovered =
            "Not checked: lateral-torsional buckling (6.3.2), bending with compression as a member (6.3.3), " +
            "torsional and torsional-flexural buckling (6.3.1.4), warping torsion, shear buckling (EN 1993-1-5).";

        /// <summary>
        /// Checks one member.
        /// </summary>
        /// <param name="forces">The section forces at each integration section, I end to J end.</param>
        /// <param name="bucklingLength">L_cr, the same about both axes.</param>
        /// <param name="stations">Where each integration section sits, 0 to 1 along the member, for the report. Optional.</param>
        /// <param name="report">
        /// Write the report. Off, nothing is formatted at all - on a model of thousands of members that is
        /// most of the time the check takes - and <see cref="MemberUtilisation.Report"/> stays empty.
        /// </param>
        public static MemberUtilisation Run(SteelSection section, IReadOnlyList<SectionForces> forces, double bucklingLength,
                                            double gammaM0 = 1.0, double gammaM1 = 1.0, IReadOnlyList<double> stations = null,
                                            bool report = true)
        {
            var result = new MemberUtilisation { Type = section.Grade?.Family ?? "Steel" };
            var text = new Report(report);

            text.Line(() => $"EN 1993-1-1, {section.Grade?.Name}: fy = {F(section.FyMPa)} N/mm² for the thickest plate, {F(section.ThicknessMm)} mm" +
                              (section.FyBeyondTable ? " (past the 80 mm of Table 3.1, so the 40-80 mm value)" : "") +
                              $"; ε = {F(section.Epsilon)}; γM0 = {F(gammaM0)}, γM1 = {F(gammaM1)}");
            text.Line(() => $"Section: {section.Description}");
            text.Line(() => $"  A = {G(section.A)}, Iy = {G(section.Iy)}, Iz = {G(section.Iz)}");
            text.Line(() => $"  Wel,y = {G(section.WelY)}, Wel,z = {G(section.WelZ)}, Wpl,y = {G(section.WplY)}, Wpl,z = {G(section.WplZ)}");
            text.Line(() => $"  Av,y = {G(section.AvY)}, Av,z = {G(section.AvZ)}");

            if (forces == null || forces.Count == 0)
            {
                text.Line(() => "No section forces: the recorder file holds none for this member.");
                result.Report = text.ToString();
                return result;
            }

            // Classification, section by section: the class depends on the forces, so it can change
            // along the member, and each section is checked in its own.
            var classes = new int[forces.Count];
            var setBy = new string[forces.Count];
            var lowStress = new bool[forces.Count];
            var bucklingClasses = new int[forces.Count];
            for (int k = 0; k < forces.Count; k++)
            {
                classes[k] = Classify(section, forces[k], gammaM0, true, out var part, out lowStress[k]);
                setBy[k] = part?.Name;
                bucklingClasses[k] = Classify(section, forces[k], gammaM0, false, out _, out _);
            }

            int worst = classes.Max();
            int worstAt = Array.IndexOf(classes, worst);
            result.Class = worst;
            result.LowStressClass3 = lowStress.Any(x => x);
            text.Line(() => $"Class {worst}" + (setBy[worstAt] != null ? $", set by the {setBy[worstAt]}" : "") +
                              $" {At(worstAt, forces.Count, stations)}" +
                              (lowStress[worstAt] ? " (Class 4 by Table 5.2; Class 3 by 5.5.2(9) at the stress it carries there)" : ""));

            if (worst == 4)
            {
                text.Line(() => "Class 4 needs effective section properties from EN 1993-1-5, which are not worked out here. " +
                                "Nothing is checked for this member.");
                AppendNotes(text, section);
                result.Report = text.ToString();
                return result;
            }

            int bucklingClass = bucklingClasses.Max();
            result.BucklingClass = bucklingClass;

            double fy = section.Fy;
            double nplRd = section.A * fy / gammaM0;
            double tauRd = fy / (Math.Sqrt(3.0) * gammaM0);

            var axial = new Worst();
            var shearY = new Worst();
            var shearZ = new Worst();
            var torsion = new Worst();
            var bendingY = new Worst();
            var bendingZ = new Worst();
            var combined = new Worst();

            for (int k = 0; k < forces.Count; k++)
            {
                var f = forces[k];
                var stress = section.Stress.At(f.N, f.Vy, f.Vz, f.T, f.My, f.Mz);
                bool plastic = classes[k] <= 2;

                axial.Offer(Math.Abs(f.N) / nplRd, k, () => $"N = {F(f.N)} against N_pl,Rd = {F(nplRd)}");

                // 6.2.7: elastic, on the St. Venant shear stress - the only torsion the analysis has.
                torsion.Offer(stress.TauT / tauRd, k, () => $"τt = {G(stress.TauT)} against fy/(√3 γM0) = {G(tauRd)}");

                // 6.2.6 with 6.2.7(9): the plastic shear resistance less what the torsion takes.
                double keep = Ec3.TorsionShearFactor(stress.TauT, fy, gammaM0, section.IOrH);
                double vplY = Ec3.PlasticShear(section.AvY, fy, gammaM0) * keep;
                double vplZ = Ec3.PlasticShear(section.AvZ, fy, gammaM0) * keep;
                shearY.Offer(Ratio(f.Vy, vplY), k, () => $"Vy = {F(f.Vy)} against V_pl,T,Rd = {F(vplY)}");
                shearZ.Offer(Ratio(f.Vz, vplZ), k, () => $"Vz = {F(f.Vz)} against V_pl,T,Rd = {F(vplZ)}");

                // 6.2.8: past half its resistance, shear takes ρ fy off its own shear area. Vy runs
                // along the web of an I and so weakens it for Mz; Vz the flanges, for My.
                double rhoY = Ec3.ShearReduction(f.Vy, vplY);
                double rhoZ = Ec3.ShearReduction(f.Vz, vplZ);

                double mzRd, myRd;
                if (plastic)
                {
                    mzRd = Math.Max(0.0, section.WplZ - rhoY * section.WvZ) * fy / gammaM0;
                    myRd = Math.Max(0.0, section.WplY - rhoZ * section.WvY) * fy / gammaM0;
                }
                else
                {
                    // Class 3 has no shear area to single out in an elastic distribution; the reduced
                    // fy is taken over the whole section, which is the safe side.
                    mzRd = section.WelZ * fy * (1.0 - rhoY) / gammaM0;
                    myRd = section.WelY * fy * (1.0 - rhoZ) / gammaM0;
                }

                string modulus = plastic ? "pl" : "el";
                bendingY.Offer(Ratio(f.My, myRd), k, () => $"My = {F(f.My)} against M_{modulus},y,Rd = {F(myRd)}" + (rhoZ > 0 ? $", reduced for Vz (ρ = {F(rhoZ)})" : ""));
                bendingZ.Offer(Ratio(f.Mz, mzRd), k, () => $"Mz = {F(f.Mz)} against M_{modulus},z,Rd = {F(mzRd)}" + (rhoY > 0 ? $", reduced for Vy (ρ = {F(rhoY)})" : ""));

                // 6.2.9 with 6.2.10.
                if (plastic)
                {
                    double nplV = Math.Max(0.0, nplRd - (rhoY * section.AvY + rhoZ * section.AvZ) * fy / gammaM0);
                    double n = nplV > 0.0 ? Math.Abs(f.N) / nplV : (f.N == 0.0 ? 0.0 : double.PositiveInfinity);
                    combined.Offer(Combined(section, n, f.My, myRd, f.Mz, mzRd, report, out string how), k, () => how);
                }
                else
                {
                    double sigma = Math.Max(Math.Abs(stress.SigmaMax), Math.Abs(stress.SigmaMin));
                    double limit = fy * (1.0 - Math.Max(rhoY, rhoZ)) / gammaM0;
                    combined.Offer(Ratio(sigma, limit), k, () => $"σx,Ed = {G(sigma)} against fy/γM0 = {G(limit)} (6.2.9.2)");
                }
            }

            result.Axial = axial.Value;
            result.ShearY = shearY.Value;
            result.ShearZ = shearZ.Value;
            result.Torsion = torsion.Value;
            result.BendingY = bendingY.Value;
            result.BendingZ = bendingZ.Value;
            result.Combined = combined.Value;

            // 6.3.1, for the largest compression anywhere along the member.
            double compression = forces.Max(f => Math.Max(0.0, -f.N));
            result.BucklingY = Buckling(section, compression, section.Iy, section.CurveY, bucklingLength, gammaM1, bucklingClass, report, out string aboutY);
            result.BucklingZ = Buckling(section, compression, section.Iz, section.CurveZ, bucklingLength, gammaM1, bucklingClass, report, out string aboutZ);
            if (!result.BucklingY.HasValue) result.Missing.Add("BucklingY");
            if (!result.BucklingZ.HasValue) result.Missing.Add("BucklingZ");
            result.Checked = true;

            if (report)
            {
                string force = Units.Force.ToString(), length = Units.Length.ToString();
                text.Line(() => $"Forces in {force} and {force}·{length}, stresses in {force}/{length}².");
                Line(text, "Axial", axial, forces.Count, stations);
                Line(text, "ShearY", shearY, forces.Count, stations);
                Line(text, "ShearZ", shearZ, forces.Count, stations);
                Line(text, "Torsion", torsion, forces.Count, stations);
                Line(text, "BendingY", bendingY, forces.Count, stations);
                Line(text, "BendingZ", bendingZ, forces.Count, stations);
                Line(text, "Combined", combined, forces.Count, stations);
                text.Line(() => $"BucklingY  {(result.BucklingY.HasValue ? F(result.BucklingY.Value) : "-")}  {aboutY}");
                text.Line(() => $"BucklingZ  {(result.BucklingZ.HasValue ? F(result.BucklingZ.Value) : "-")}  {aboutZ}");
                text.Line(() => result.Max.HasValue
                    ? $"Max {F(result.Max.Value)}, from {result.Governing}"
                    : $"No Max: {result.Governing}. The member is not verified until it is.");
                AppendNotes(text, section);
            }

            result.Report = text.ToString();
            return result;
        }

        /// <summary>
        /// The class of the section under one set of forces, and the part that sets it.
        ///
        /// Every part is judged on the stress it actually carries. An outstand anywhere in compression
        /// takes the compression limits, which are the lowest. An internal part takes α from the plastic
        /// distribution - the share of it on the compression side of the plastic neutral axis, moved by
        /// the axial force as 5.5 and 6.2.9 have it - and ψ from the elastic one. Where the plastic α
        /// would undersell the compression - a web under a little bending and a lot of axial force, whose
        /// plastic axis would sit outside it - the elastic share of the part in compression is used.
        ///
        /// With <paramref name="lowStress"/>, a part that fails Class 3 is given a second chance under
        /// 5.5.2(9): Class 3 after all if it meets the Class 3 limit with ε raised by √(fy/γM0 / σcom,Ed),
        /// σcom,Ed the largest compression it actually carries. That is what keeps a slender web at a
        /// section of next to no moment and a little axial force from reading as Class 4. 5.5.2(10)
        /// forbids it for member buckling, which classifies without it.
        /// </summary>
        public static int Classify(SteelSection section, SectionForces f, double gammaM0, bool lowStress,
                                   out SteelPart governing, out bool byLowStress)
        {
            governing = null;
            byLowStress = false;
            if (section.Parts.Count == 0)
                return 1;

            double fy = section.Fy;
            double eps = section.Epsilon;
            double tiny = 1e-6 * fy;

            var stress = section.Stress.At(f.N, f.Vy, f.Vz, f.T, f.My, f.Mz);
            double sectionCompression = Math.Max(0.0, -stress.SigmaMin);

            int worst = 0;
            bool worstByLowStress = false;
            foreach (var part in section.Parts)
            {
                int cls;
                double compression;
                switch (part.Kind)
                {
                    case PartKind.Tube:
                        compression = sectionCompression;
                        cls = compression > tiny ? Ec3.ClassTube(part.C / part.T, eps) : 1;
                        break;

                    case PartKind.Angle:
                        compression = sectionCompression;
                        cls = compression > tiny && !Ec3.AngleWithinClass3(part.C, part.B, part.T, eps) ? 4 : 1;
                        break;

                    case PartKind.Outstand:
                        compression = Math.Max(-section.Stress.NormalStressAt(f.N, f.My, f.Mz, part.Y1, part.Z1),
                                               -section.Stress.NormalStressAt(f.N, f.My, f.Mz, part.Y2, part.Z2));
                        cls = compression > tiny ? Ec3.ClassOutstand(part.C / part.T, eps) : 1;
                        break;

                    default:
                        cls = ClassifyInternal(section, part, f, tiny, eps, out compression);
                        break;
                }

                bool relaxed = false;
                if (cls == 4 && lowStress && compression > tiny)
                {
                    double raised = eps * Math.Sqrt(fy / gammaM0 / compression);
                    int again;
                    switch (part.Kind)
                    {
                        case PartKind.Tube: again = Ec3.ClassTube(part.C / part.T, raised); break;
                        case PartKind.Angle: again = Ec3.AngleWithinClass3(part.C, part.B, part.T, raised) ? 3 : 4; break;
                        case PartKind.Outstand: again = Ec3.ClassOutstand(part.C / part.T, raised); break;
                        default: again = ClassifyInternal(section, part, f, tiny, raised, out _); break;
                    }

                    if (again <= 3)
                    {
                        cls = 3;
                        relaxed = true;
                    }
                }

                if (cls > worst)
                {
                    worst = cls;
                    governing = part;
                    worstByLowStress = relaxed;
                }
            }

            byLowStress = worstByLowStress;
            return Math.Max(worst, 1);
        }

        private static int ClassifyInternal(SteelSection section, SteelPart part, SectionForces f, double tiny, double eps, out double compression)
        {
            // Compression positive, as Table 5.2 draws it.
            double c1 = -section.Stress.NormalStressAt(f.N, f.My, f.Mz, part.Y1, part.Z1);
            double c2 = -section.Stress.NormalStressAt(f.N, f.My, f.Mz, part.Y2, part.Z2);
            double cMax = Math.Max(c1, c2), cMin = Math.Min(c1, c2);
            compression = Math.Max(0.0, cMax);

            if (cMax <= tiny)
                return 1;

            double psi = cMin / cMax;
            double alphaElastic = cMin >= 0.0 ? 1.0 : cMax / (cMax - cMin);

            // How much the bending in the part's own plane varies the stress along it. With next to
            // none, the plastic distribution is the elastic one.
            double bending = part.AlongY
                ? Math.Abs(f.Mz) * Math.Abs(part.Y1 - part.Y2) / section.Iz
                : Math.Abs(f.My) * Math.Abs(part.Z1 - part.Z2) / section.Iy;

            double alpha = alphaElastic;
            if (bending > tiny && part.Length > 0.0 && part.ShiftThickness > 0.0)
            {
                // Positive Mz compresses +y; positive My compresses −z.
                bool positiveSide = part.AlongY ? f.Mz > 0.0 : f.My < 0.0;
                double share = positiveSide ? part.Alpha0 : 1.0 - part.Alpha0;
                double alphaPlastic = share + (-f.N) / (2.0 * section.Fy * part.ShiftThickness * part.Length);
                alpha = Math.Max(alphaPlastic, alphaElastic);
            }

            return Ec3.ClassInternal(part.C / part.T, Math.Max(0.0, Math.Min(1.0, alpha)), psi, eps);
        }

        /// <summary>
        /// 6.2.9.1 for Class 1 and 2. Reported as the largest of the two moments over their reduced
        /// resistances and the left-hand side of (6.41), which is ≤ 1 exactly when (6.41) holds - but,
        /// unlike the left-hand side alone, reads 0.9 for a member at 90% of its reduced moment, rather
        /// than the 0.81 an exponent of 2 would turn that into.
        /// </summary>
        private static double Combined(SteelSection section, double n, double my, double myRd, double mz, double mzRd,
                                       bool describe, out string how)
        {
            how = null;
            double mnY, mnZ, expY, expZ;
            switch (section.Interaction)
            {
                case PlasticInteraction.DoublySymmetricI:
                    mnZ = Ec3.ReducedMomentIMajor(mzRd, n, section.InteractionAZ);
                    mnY = Ec3.ReducedMomentIMinor(myRd, n, section.InteractionAY);
                    expZ = 2.0;
                    expY = Math.Max(5.0 * n, 1.0);
                    break;
                case PlasticInteraction.UniformBox:
                    mnZ = Ec3.ReducedMomentBox(mzRd, n, section.InteractionAZ);
                    mnY = Ec3.ReducedMomentBox(myRd, n, section.InteractionAY);
                    expZ = expY = Ec3.BoxExponent(n);
                    break;
                case PlasticInteraction.Round:
                    mnZ = Ec3.ReducedMomentTube(mzRd, n);
                    mnY = Ec3.ReducedMomentTube(myRd, n);
                    expZ = expY = 2.0;
                    break;
                case PlasticInteraction.SolidRectangle:
                    mnZ = Ec3.ReducedMomentRectangle(mzRd, n);
                    mnY = Ec3.ReducedMomentRectangle(myRd, n);
                    expZ = expY = 1.0;
                    break;
                default:
                {
                    double linear = n + Ratio(my, myRd) + Ratio(mz, mzRd);
                    if (describe)
                        how = $"n = {F(n)}, linear sum n + My/M_y,Rd + Mz/M_z,Rd (6.2.1(7))";
                    return linear;
                }
            }

            double lhs = Ec3.Biaxial(mz, mnZ, expZ, my, mnY, expY);
            double value = Math.Max(n, Math.Max(lhs, Math.Max(Ratio(my, mnY), Ratio(mz, mnZ))));
            if (describe)
                how = $"n = {F(n)}, M_N,y,Rd = {F(mnY)}, M_N,z,Rd = {F(mnZ)}, exponents {F(expZ)} (Mz) and {F(expY)} (My), (6.41) = {F(lhs)}";
            return value;
        }

        /// <summary>
        /// 6.3.1: N_Ed / N_b,Rd for buckling about one axis. 6.3.1.2(4) lets buckling be ignored for
        /// λ̄ ≤ 0.2 or N_Ed ≤ 0.04 N_cr, which here means χ = 1 - the cross-section check, at γM1.
        ///
        /// A section that is Class 4 by Table 5.2 - 5.5.2(10) allows no second chance here - needs
        /// A_eff, so it is not checked unless the compression is low enough to ignore buckling.
        /// </summary>
        private static double? Buckling(SteelSection section, double compression, double inertia, BucklingCurve curve,
                                        double length, double gammaM1, int bucklingClass, bool describe, out string how)
        {
            how = null;
            if (compression <= 0.0)
            {
                how = "no compression";
                return 0.0;
            }

            if (section.E <= 0.0 || length <= 0.0)
            {
                how = "no stiffness or no length to work out N_cr from - not checked";
                return null;
            }

            double ncr = Ec3.CriticalForce(section.E, inertia, length);
            double slenderness = Ec3.Slenderness(section.A, section.Fy, ncr);
            double imperfection = Ec3.Imperfection(curve);
            double chi = Ec3.Reduction(slenderness, imperfection);

            bool ignored = slenderness <= 0.2 || compression / ncr <= 0.04;
            if (ignored) chi = 1.0;

            if (bucklingClass == 4 && compression / ncr > 0.04)
            {
                if (describe)
                    how = $"N_Ed = {F(compression)}, N_cr = {F(ncr)}: Class 4 in compression by Table 5.2, so N_b,Rd needs A_eff from EN 1993-1-5 - not checked";
                return null;
            }

            double nbRd = chi * section.A * section.Fy / gammaM1;
            if (describe)
                how = $"N_Ed = {F(compression)}, L_cr = {F(length)}, N_cr = {F(ncr)}, λ̄ = {F(slenderness)}, curve {curve} (α = {F(imperfection)}), " +
                  $"χ = {F(chi)}{(ignored ? " (buckling may be ignored, 6.3.1.2(4))" : "")}, N_b,Rd = {F(nbRd)}";
            return compression / nbRd;
        }

        private static void AppendNotes(Report text, SteelSection section)
        {
            foreach (var note in section.Notes)
                text.Line(() => "Note: " + note);
            text.Line(() => NotCovered);
        }

        private static void Line(Report text, string name, Worst worst, int count, IReadOnlyList<double> stations)
        {
            text.Line(() => $"{name,-10} {F(worst.Value)}  {At(worst.At, count, stations)}: {worst.Detail?.Invoke()}");
        }

        /// <summary>
        /// The report, written only when one was asked for. Every line arrives as a function, so a
        /// report that is off formats nothing.
        /// </summary>
        private class Report
        {
            private readonly StringBuilder _text;

            public Report(bool on) => _text = on ? new StringBuilder() : null;

            public void Line(Func<string> line)
            {
                _text?.AppendLine(line());
            }

            public override string ToString() => _text?.ToString() ?? "";
        }

        private static string At(int index, int count, IReadOnlyList<double> stations)
        {
            string where = stations != null && index < stations.Count ? $", x = {F(stations[index])} L" : "";
            return $"at section {index + 1} of {count}{where}";
        }

        private static double Ratio(double action, double resistance)
        {
            action = Math.Abs(action);
            if (action == 0.0) return 0.0;
            return resistance > 0.0 ? action / resistance : double.PositiveInfinity;
        }

        private static string F(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
        private static string G(double x) => x.ToString("G4", CultureInfo.InvariantCulture);

        /// <summary>The largest of one utilisation along the member, where it is and what made it.</summary>
        private class Worst
        {
            public double Value = 0.0;
            public int At = 0;
            /// <summary>What made it, as a function: only the worst one is ever written out.</summary>
            public Func<string> Detail;
            private bool _any;

            public void Offer(double value, int at, Func<string> detail)
            {
                if (_any && !(value > this.Value)) return;
                _any = true;
                this.Value = value;
                this.At = at;
                this.Detail = detail;
            }
        }
    }
}
