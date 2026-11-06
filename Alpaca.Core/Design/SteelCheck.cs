using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Alpaca4d.Design
{
    /// <summary>Where a transverse load acts on the section, for the critical moment of lateral-torsional buckling.</summary>
    public enum LoadPosition
    {
        /// <summary>At the shear centre: neither helps nor hinders.</summary>
        ShearCentre,
        /// <summary>
        /// On the face it comes from - gravity on the top flange. As the beam twists the load moves
        /// sideways with that face and pulls the twist further.
        /// </summary>
        Destabilising,
        /// <summary>On the face it goes towards - a load hung from the bottom flange, which pulls the twist back.</summary>
        Stabilising
    }

    /// <summary>
    /// What the check needs to know about a member that neither its section nor its forces can say:
    /// how long it is between the points that hold it, and where its load acts. A length of zero or
    /// less is the member's own.
    /// </summary>
    public class SteelMember
    {
        /// <summary>Node to node, the length the forces were worked out over.</summary>
        public double Length;

        /// <summary>L_cr for flexural buckling about local y, and about local z.</summary>
        public double BucklingLengthY, BucklingLengthZ;

        /// <summary>
        /// The distance between lateral restraints. The member is taken as held sideways and against
        /// twist at both ends and at equal steps in between no longer than this, and free to rotate
        /// and warp at each of them - fork supports. Longer than the member, it stands for a longer
        /// unrestrained length the member is only part of.
        /// </summary>
        public double LateralTorsionalLength;

        public LoadPosition Load = LoadPosition.ShearCentre;

        /// <summary>The member belongs to a frame that buckles by swaying: Cm = 0.9 about both axes, Table B.3.</summary>
        public bool Sway;

        public SteelMember() { }

        /// <summary>A member of the given length, held at its ends against every kind of buckling.</summary>
        public SteelMember(double length)
        {
            this.Length = length;
            this.BucklingLengthY = length;
            this.BucklingLengthZ = length;
            this.LateralTorsionalLength = length;
        }

        internal double LengthY => this.BucklingLengthY > 0.0 ? this.BucklingLengthY : this.Length;
        internal double LengthZ => this.BucklingLengthZ > 0.0 ? this.BucklingLengthZ : this.Length;
        internal double LengthLT => this.LateralTorsionalLength > 0.0 ? this.LateralTorsionalLength : this.Length;
    }

    /// <summary>
    /// The EN 1993-1-1 check of one steel member: the cross-section at every integration section
    /// (6.2), flexural buckling (6.3.1), lateral-torsional buckling (6.3.2), and bending with
    /// compression as a member (6.3.3, by Annex B).
    ///
    /// Not covered, and said so in every report: torsional and torsional-flexural buckling
    /// (6.3.1.4), warping torsion, shear buckling (EN 1993-1-5) and Class 4 sections.
    /// </summary>
    public static class SteelCheck
    {
        private const string NotCovered =
            "Not checked: torsional and torsional-flexural buckling (6.3.1.4), warping torsion, shear buckling (EN 1993-1-5).";

        /// <summary>
        /// Checks one member of length <paramref name="bucklingLength"/>, held at both ends against
        /// every kind of buckling: L_cr about both axes and L_LT are all that length.
        /// </summary>
        public static MemberUtilisation Run(SteelSection section, IReadOnlyList<SectionForces> forces, double bucklingLength,
                                            double gammaM0 = 1.0, double gammaM1 = 1.0, IReadOnlyList<double> stations = null,
                                            bool report = true)
        {
            return Run(section, forces, new SteelMember(bucklingLength), gammaM0, gammaM1, stations, report);
        }

        /// <summary>
        /// A lower bound on the Max of <see cref="Run"/>, for ruling a section out without the whole check:
        /// axial force, shear and bending each against the section's full plastic resistance, before
        /// anything reduces it - torsion, shear, an elastic modulus for Class 3 - and before buckling, which
        /// only adds checks. Run makes each of these against a resistance no larger, so whenever it has a
        /// Max, the Max is at least this. A section whose bound is above the target fails without Run.
        /// </summary>
        public static double LowerBound(SteelSection section, IReadOnlyList<SectionForces> forces, double gammaM0 = 1.0)
        {
            if (section == null || forces == null)
                return 0.0;

            double fy = section.Fy;
            double nplRd = section.A * fy / gammaM0;
            double vplY = Ec3.PlasticShear(section.AvY, fy, gammaM0);
            double vplZ = Ec3.PlasticShear(section.AvZ, fy, gammaM0);
            double mplY = section.WplY * fy / gammaM0;
            double mplZ = section.WplZ * fy / gammaM0;

            double bound = 0.0;
            foreach (var f in forces)
            {
                bound = Math.Max(bound, Ratio(f.N, nplRd));
                bound = Math.Max(bound, Ratio(f.Vy, vplY));
                bound = Math.Max(bound, Ratio(f.Vz, vplZ));
                bound = Math.Max(bound, Ratio(f.My, mplY));
                bound = Math.Max(bound, Ratio(f.Mz, mplZ));
            }
            return bound;
        }

        /// <summary>
        /// Checks one member.
        /// </summary>
        /// <param name="forces">The section forces at each integration section, I end to J end.</param>
        /// <param name="stations">
        /// Where each integration section sits, 0 to 1 along the member. Without them the sections are
        /// taken as evenly spaced from end to end, which is where Newton-Cotes puts them.
        /// </param>
        /// <param name="report">
        /// Write the report. Off, nothing is formatted at all - on a model of thousands of members that is
        /// most of the time the check takes - and <see cref="MemberUtilisation.Report"/> stays empty.
        /// </param>
        public static MemberUtilisation Run(SteelSection section, IReadOnlyList<SectionForces> forces, SteelMember member,
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
            var aboutY = Buckling(section, compression, section.Iy, section.CurveY, member.LengthY, gammaM1, bucklingClass, report);
            var aboutZ = Buckling(section, compression, section.Iz, section.CurveZ, member.LengthZ, gammaM1, bucklingClass, report);
            result.BucklingY = aboutY.Value;
            result.BucklingZ = aboutZ.Value;
            if (!result.BucklingY.HasValue) result.Missing.Add("BucklingY");
            if (!result.BucklingZ.HasValue) result.Missing.Add("BucklingZ");

            // The moments along the member, as parabolas: the major axis one for 6.3.2, both for 6.3.3.
            var positions = Positions(forces.Count, stations);
            var majorValues = forces.Select(f => section.MajorIsZ ? f.Mz : f.My).ToList();
            var minorValues = forces.Select(f => section.MajorIsZ ? f.My : f.Mz).ToList();
            var major = Diagram.Fit(positions, majorValues);
            var minor = Diagram.Fit(positions, minorValues);

            // 6.3.2.
            var ltb = LateralTorsional(section, member, major, positions, majorValues, bucklingClass, gammaM1, report);
            result.LateralTorsional = ltb.Value;
            if (!ltb.Value.HasValue) result.Missing.Add("LTB");

            // 6.3.3.
            double majorPeak = Math.Max(major.Peak, majorValues.Max(m => Math.Abs(m)));
            double minorPeak = Math.Max(minor.Peak, minorValues.Max(m => Math.Abs(m)));
            var interaction = Interaction(section, member, compression, aboutY, aboutZ, ltb, major, minor, majorPeak, minorPeak,
                                          bucklingClass, gammaM1, report);
            result.InteractionY = interaction.Y;
            result.InteractionZ = interaction.Z;
            if (interaction.Missing)
            {
                result.Missing.Add("InteractionY");
                result.Missing.Add("InteractionZ");
            }

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
                text.Line(() => $"BucklingY  {Value(result.BucklingY)}  {aboutY.Detail}");
                text.Line(() => $"BucklingZ  {Value(result.BucklingZ)}  {aboutZ.Detail}");
                text.Line(() => $"LTB        {Value(result.LateralTorsional)}  {ltb.Detail}");
                text.Line(() => $"InteractionY {Value(result.InteractionY)}  {interaction.DetailY}");
                text.Line(() => $"InteractionZ {Value(result.InteractionZ)}  {interaction.DetailZ}");
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

        /// <summary>Flexural buckling about one axis: the utilisation, and the χ and λ̄ Annex B goes on to use.</summary>
        private class Flexural
        {
            public double? Value;
            /// <summary>χ and λ̄ were worked out: there was a stiffness and a length to work them from.</summary>
            public bool Known;
            public double Chi = 1.0;
            public double Slenderness;
            public double Length;
            public string Detail;
        }

        /// <summary>
        /// 6.3.1: N_Ed / N_b,Rd for buckling about one axis. 6.3.1.2(4) lets buckling be ignored for
        /// λ̄ ≤ 0.2 or N_Ed ≤ 0.04 N_cr, which here means χ = 1 - the cross-section check, at γM1.
        ///
        /// A section that is Class 4 by Table 5.2 - 5.5.2(10) allows no second chance here - needs
        /// A_eff, so it is not checked unless the compression is low enough to ignore buckling.
        ///
        /// χ and λ̄ are worked out with no compression too: Annex B's kzy depends on λ̄z alone when
        /// a member bends about both axes.
        /// </summary>
        private static Flexural Buckling(SteelSection section, double compression, double inertia, BucklingCurve curve,
                                         double length, double gammaM1, int bucklingClass, bool describe)
        {
            var result = new Flexural { Length = length };

            if (section.E <= 0.0 || length <= 0.0)
            {
                if (compression <= 0.0)
                {
                    result.Value = 0.0;
                    result.Detail = "no compression";
                }
                else
                {
                    result.Detail = "no stiffness or no length to work out N_cr from - not checked";
                }
                return result;
            }

            double ncr = Ec3.CriticalForce(section.E, inertia, length);
            double slenderness = Ec3.Slenderness(section.A, section.Fy, ncr);
            double imperfection = Ec3.Imperfection(curve);
            double chi = Ec3.Reduction(slenderness, imperfection);

            bool ignored = slenderness <= 0.2 || compression / ncr <= 0.04;
            if (ignored) chi = 1.0;

            result.Known = true;
            result.Chi = chi;
            result.Slenderness = slenderness;

            if (compression <= 0.0)
            {
                result.Value = 0.0;
                result.Detail = describe ? $"no compression (L_cr = {F(length)}, λ̄ = {F(slenderness)})" : null;
                return result;
            }

            if (bucklingClass == 4 && compression / ncr > 0.04)
            {
                if (describe)
                    result.Detail = $"N_Ed = {F(compression)}, N_cr = {F(ncr)}: Class 4 in compression by Table 5.2, so N_b,Rd needs A_eff from EN 1993-1-5 - not checked";
                result.Known = false;
                return result;
            }

            double nbRd = chi * section.A * section.Fy / gammaM1;
            if (describe)
                result.Detail = $"N_Ed = {F(compression)}, L_cr = {F(length)}, N_cr = {F(ncr)}, λ̄ = {F(slenderness)}, curve {curve} (α = {F(imperfection)}), " +
                                $"χ = {F(chi)}{(ignored ? " (buckling may be ignored, 6.3.1.2(4))" : "")}, N_b,Rd = {F(nbRd)}";
            result.Value = compression / nbRd;
            return result;
        }

        /// <summary>Lateral-torsional buckling: the utilisation, and the χLT and CmLT Annex B goes on to use.</summary>
        private class Lateral
        {
            public double? Value;
            /// <summary>
            /// The lowest χLT along the member, 1 where it cannot buckle sideways - from the curve alone.
            /// 6.3.2.2(4) lets a beam whose M_Ed is below λ̄LT,0² M_cr ignore lateral-torsional buckling,
            /// but that is a beam in bending: compression lowers the moment it buckles at, so 6.3.3 is
            /// not given the same leave.
            /// </summary>
            public double Chi = 1.0;
            /// <summary>CmLT of the piece that has that χLT.</summary>
            public double CmLT = 1.0;
            public string Detail;
        }

        /// <summary>
        /// 6.3.2: M_Ed / M_b,Rd, M_b,Rd = χLT Wy fy / γM1, for every piece of the member between lateral
        /// restraints, and the worst of them.
        ///
        /// M_cr comes from <see cref="CriticalMoment"/> on the piece's own part of the moment diagram,
        /// with the load at the height <see cref="SteelMember.Load"/> puts it. An L_LT longer than the
        /// member stands for an unrestrained length the member is only part of, whose diagram this
        /// member cannot tell: the moment is then taken as uniform at its peak over all of L_LT, the
        /// most severe distribution there is, and both ways round for a monosymmetric section.
        ///
        /// χLT by 6.3.2.3 for a rolled or equivalent welded I, without the modification f of
        /// 6.3.2.3(2), which a National Annex may allow and leaving out is the safe side; by 6.3.2.2
        /// for everything else. Like 6.3.1, judged by the class of Table 5.2 alone (5.5.2(10)): Class 4
        /// needs W_eff, and is not checked unless lateral-torsional buckling may be ignored.
        /// </summary>
        private static Lateral LateralTorsional(SteelSection section, SteelMember member, Diagram major,
                                                IReadOnlyList<double> positions, IReadOnlyList<double> values,
                                                int bucklingClass, double gammaM1, bool describe)
        {
            var result = new Lateral();
            double fy = section.Fy;
            double peak = Math.Max(major.Peak, values.Max(m => Math.Abs(m)));

            if (peak <= 1e-9 * section.WelMajor * fy)
            {
                result.Value = 0.0;
                result.Detail = "no bending about the major axis";
                return result;
            }

            if (!section.LateralTorsional)
            {
                result.Value = 0.0;
                result.Detail = "the same about every axis, so nothing to buckle sideways";
                return result;
            }

            double length = member.Length;
            double llt = member.LengthLT;
            if (section.E <= 0.0 || section.G <= 0.0 || length <= 0.0 || llt <= 0.0)
            {
                result.Detail = "no stiffness, no shear modulus or no length to work out M_cr from - not checked";
                return result;
            }

            bool plastic = bucklingClass <= 2;
            double w = plastic ? section.WplMajor : section.WelMajor;
            double alpha = Ec3.Imperfection(section.CurveLT);

            // The load that bends the diagram, and where it acts. Positive Mz compresses +y, so a load
            // towards −y makes Mz'' negative; positive My compresses −z, so a load towards −z makes My''
            // positive.
            double q = 2.0 * Math.Abs(major.C2) / (length * length);
            bool towardsNegative = section.MajorIsZ ? major.C2 < 0.0 : major.C2 > 0.0;
            double a = LoadHeight(section, member.Load, towardsNegative);

            // The pieces between restraints, each with its part of the diagram - or, where the diagram
            // along L_LT is not this member's to tell, one piece L_LT long under the peak moment.
            bool longer = llt > length * (1.0 + 1e-6);
            var pieces = new List<(double From, double To, double Length, Diagram Moment, bool Uniform)>();
            if (longer || !major.Parabola)
            {
                pieces.Add((0.0, 1.0, llt, new Diagram { C0 = peak, Parabola = true }, true));
            }
            else
            {
                int count = Math.Max(1, (int)Math.Ceiling(length / llt - 1e-6));
                for (int i = 0; i < count; i++)
                {
                    double from = (double)i / count, to = (double)(i + 1) / count;
                    pieces.Add((from, to, length / count, major.Slice(from, to), false));
                }
            }

            double worst = 0.0;
            bool classFour = false, unknown = false;
            Func<string> worstDetail = null;

            foreach (var piece in pieces)
            {
                double mEd = piece.Moment.Peak;
                if (!piece.Uniform)
                {
                    for (int k = 0; k < positions.Count; k++)
                    {
                        if (positions[k] >= piece.From - 1e-9 && positions[k] <= piece.To + 1e-9)
                            mEd = Math.Max(mEd, Math.Abs(values[k]));
                    }
                }

                if (mEd <= 1e-9 * section.WelMajor * fy)
                    continue;

                double mcr;
                if (piece.Uniform)
                {
                    double up = Multiplier(section, piece.Length, mEd, 0.0, 0.0, q, a);
                    double down = section.Monosymmetry == 0.0 ? up : Multiplier(section, piece.Length, -mEd, 0.0, 0.0, q, a);
                    mcr = Math.Min(up, down) * mEd;
                }
                else
                {
                    var m = piece.Moment;
                    mcr = Multiplier(section, piece.Length, m.C0, m.C1, m.C2, q, a) * m.Peak;
                }

                if (double.IsNaN(mcr))
                {
                    unknown = true;
                    continue;
                }

                double slenderness = double.IsPositiveInfinity(mcr) ? 0.0 : Ec3.SlendernessLt(w, fy, mcr);
                bool ignored = Ec3.LtIgnored(slenderness, section.LtPlateau, mEd, mcr);
                double curve = Ec3.ReductionLt(slenderness, alpha, section.LtPlateau, section.LtBeta);
                double chi = ignored ? 1.0 : curve;
                if (bucklingClass == 4 && !ignored)
                    classFour = true;

                double mbRd = chi * w * fy / gammaM1;
                double value = mEd / mbRd;

                if (curve < result.Chi)
                {
                    result.Chi = curve;
                    result.CmLT = piece.Uniform ? 1.0 : Cm(piece.Moment);
                }

                if (value > worst || worstDetail == null)
                {
                    worst = value;
                    if (describe)
                    {
                        var p = piece;
                        double pieceMcr = mcr, pieceSlenderness = slenderness, pieceChi = chi, pieceCurve = curve, pieceMbRd = mbRd, pieceMEd = mEd;
                        bool pieceIgnored = ignored;
                        worstDetail = () =>
                        {
                            string where = p.Uniform
                                ? longer
                                    ? $"L_LT = {F(p.Length)}, longer than the member ({F(length)}): its peak moment taken as uniform over all of it"
                                    : $"L_LT = {F(p.Length)}: the moments along the member do not lie on a parabola, so its peak is taken as uniform"
                                : pieces.Count == 1
                                    ? $"L_LT = {F(p.Length)}"
                                    : $"L_LT = {F(p.Length)}, restrained at {pieces.Count - 1} point(s) between the ends, worst between x = {F(p.From)} L and {F(p.To)} L";
                            string c1 = p.Uniform ? "" : $" (C1 = {F(EquivalentC1(section, p.Length, p.Moment))})";
                            string load = member.Load == LoadPosition.ShearCentre || q == 0.0 ? "" : $", load {member.Load.ToString().ToLowerInvariant()} (a = {G(a)} from the shear centre)";
                            string method = section.LtRolledMethod ? "6.3.2.3 (λ̄LT,0 = 0.4, β = 0.75, f = 1)" : "6.3.2.2";
                            return $"{where}, fork supports{load}: M_Ed = {F(pieceMEd)}, M_cr = {G(pieceMcr)}{c1}, " +
                                   $"λ̄LT = {F(pieceSlenderness)}, curve {section.CurveLT} (α = {F(alpha)}), {method}, χLT = {F(pieceChi)}" +
                                   $"{(pieceIgnored ? $" (may be ignored, 6.3.2.2(4); {F(pieceCurve)} by the curve, which 6.3.3 uses)" : "")}, M_b,Rd = {F(pieceMbRd)} with W_{(plastic ? "pl" : "el")}";
                        };
                    }
                }
            }

            if (unknown)
            {
                result.Detail = "the section has no stiffness to buckle sideways against - not checked";
                return result;
            }

            if (classFour)
            {
                result.Detail = "Class 4 by Table 5.2 along the member: M_b,Rd needs W_eff from EN 1993-1-5 - not checked";
                return result;
            }

            result.Value = worst;
            result.Detail = worstDetail?.Invoke();
            return result;
        }

        private static double Multiplier(SteelSection section, double length, double c0, double c1, double c2, double q, double a)
        {
            return CriticalMoment.Multiplier(length, c0, c1, c2, q, a, section.E, section.LateralInertia, section.Iw,
                                             section.G, section.It, section.Monosymmetry);
        }

        /// <summary>
        /// M_cr of the diagram over M_cr of a uniform moment at its peak, both with the load at the
        /// shear centre: the C1 a table would have given, for the report.
        /// </summary>
        private static double EquivalentC1(SteelSection section, double length, Diagram moment)
        {
            double peak = moment.Peak;
            double sign = Math.Abs(moment.C0) >= Math.Abs(moment.At(1.0)) ? Math.Sign(moment.C0) : Math.Sign(moment.At(1.0));
            double actual = Multiplier(section, length, moment.C0, moment.C1, moment.C2, 0.0, 0.0) * peak;
            double uniform = Multiplier(section, length, (sign == 0 ? 1 : sign) * peak, 0.0, 0.0, 0.0, 0.0) * peak;
            return actual / uniform;
        }

        /// <summary>
        /// How far from the shear centre the load acts, positive on the side it comes from: to the
        /// extreme fibre it comes from when destabilising, to the one it goes towards when stabilising.
        /// </summary>
        private static double LoadHeight(SteelSection section, LoadPosition load, bool towardsNegative)
        {
            double from = towardsNegative ? section.FibreTop - section.ShearCentre : section.ShearCentre - section.FibreBottom;
            double to = towardsNegative ? section.ShearCentre - section.FibreBottom : section.FibreTop - section.ShearCentre;
            switch (load)
            {
                case LoadPosition.Destabilising: return from;
                case LoadPosition.Stabilising: return -to;
                default: return 0.0;
            }
        }

        /// <summary>The two utilisations of 6.3.3, by the axis their χ is about.</summary>
        private class Interactions
        {
            public double? Y, Z;
            public bool Missing;
            public string DetailY, DetailZ;
        }

        /// <summary>
        /// 6.3.3, equations (6.61) and (6.62), with the interaction factors of Annex B (method 2):
        ///
        ///   N_Ed / (χy N_Rk/γM1) + kyy My,Ed / (χLT My,Rk/γM1) + kyz Mz,Ed / (Mz,Rk/γM1) ≤ 1
        ///   N_Ed / (χz N_Rk/γM1) + kzy My,Ed / (χLT My,Rk/γM1) + kzz Mz,Ed / (Mz,Rk/γM1) ≤ 1
        ///
        /// y the major axis, as the standard has it, and each moment the largest along the member: Cm
        /// accounts for how it is distributed. Applied to a member with compression and bending, and to
        /// one bent about both axes without compression - the only rule EN 1993-1-1 has that puts
        /// lateral-torsional buckling together with minor axis bending. With no bending it is 6.3.1.
        ///
        /// Cm from the member's own diagram, Table B.3 - unless its buckling length is longer than the
        /// member, when the diagram that matters is not this member's to tell and Cm = 1, the most it
        /// can be; or unless the frame sways, when it is 0.9.
        /// </summary>
        private static Interactions Interaction(SteelSection section, SteelMember member, double compression,
                                                Flexural aboutY, Flexural aboutZ, Lateral ltb, Diagram major, Diagram minor,
                                                double majorPeak, double minorPeak, int bucklingClass, double gammaM1, bool describe)
        {
            var result = new Interactions();
            double fy = section.Fy;

            bool compressed = compression > 1e-9 * section.A * fy;
            bool bentMajor = majorPeak > 1e-9 * section.WelMajor * fy;
            bool bentMinor = minorPeak > 1e-9 * section.WelMinor * fy;

            void Both(double? value, string why)
            {
                result.Y = value;
                result.Z = value;
                result.DetailY = why;
                result.DetailZ = why;
            }

            if (!bentMajor && !bentMinor)
            {
                Both(0.0, "no bending: 6.3.1 is the whole of it");
                return result;
            }

            if (!compressed && !(bentMajor && bentMinor))
            {
                Both(0.0, "no compression, and bending about one axis only: 6.3.3 adds nothing to 6.2 and 6.3.2");
                return result;
            }

            result.Missing = true;
            if (!section.DoublySymmetric)
            {
                Both(null, "6.3.3 is for doubly symmetric sections (6.3.3(1)), which this is not - not checked");
                return result;
            }
            if (bucklingClass == 4)
            {
                Both(null, "Class 4 by Table 5.2: needs A_eff and W_eff from EN 1993-1-5 - not checked");
                return result;
            }
            if (!aboutY.Known || !aboutZ.Known || !ltb.Value.HasValue)
            {
                Both(null, "needs flexural and lateral-torsional buckling, which could not be worked out - not checked");
                return result;
            }
            result.Missing = false;

            // The standard's y is the major axis.
            var flexY = section.MajorIsZ ? aboutZ : aboutY;
            var flexZ = section.MajorIsZ ? aboutY : aboutZ;

            bool plastic = bucklingClass <= 2;
            double myRk = (plastic ? section.WplMajor : section.WelMajor) * fy;
            double mzRk = (plastic ? section.WplMinor : section.WelMinor) * fy;

            // The same expression as N_Ed / N_b,Rd in 6.3.1, so a member with no moment reads the same here.
            double ny = compression / (flexY.Chi * section.A * section.Fy / gammaM1);
            double nz = compression / (flexZ.Chi * section.A * section.Fy / gammaM1);

            double cmy = member.Sway ? 0.9 : flexY.Length > member.Length * (1.0 + 1e-6) || !major.Parabola ? 1.0 : Cm(major);
            double cmz = member.Sway ? 0.9 : flexZ.Length > member.Length * (1.0 + 1e-6) || !minor.Parabola ? 1.0 : Cm(minor);
            double cmLT = ltb.CmLT;
            double chiLT = ltb.Chi;

            var k = Ec3.AnnexB(plastic, flexY.Slenderness, flexZ.Slenderness, ny, nz, cmy, cmz, cmLT,
                               section.TorsionallyDeformable, section.Shape);

            double my = majorPeak / (chiLT * myRk / gammaM1);
            double mz = minorPeak / (mzRk / gammaM1);
            double eq61 = ny + k.Kyy * my + k.Kyz * mz;
            double eq62 = nz + k.Kzy * my + k.Kzz * mz;

            string common = null;
            if (describe)
            {
                string table = section.TorsionallyDeformable ? "B.2" : "B.1";
                common = $"N_Ed = {F(compression)}, My,Ed = {F(majorPeak)}, Mz,Ed = {F(minorPeak)} (y the major axis), " +
                         $"χy = {F(flexY.Chi)}, χz = {F(flexZ.Chi)}, χLT = {F(chiLT)}, λ̄y = {F(flexY.Slenderness)}, λ̄z = {F(flexZ.Slenderness)}, " +
                         $"Cmy = {F(cmy)}, Cmz = {F(cmz)}, CmLT = {F(cmLT)}{(member.Sway ? " (sway: 0.9)" : "")}, " +
                         $"M_Rk with W_{(plastic ? "pl" : "el")}, Table {table}";
            }

            string d61 = describe ? $"(6.61) = {F(ny)} + {F(k.Kyy)} × {F(my)} + {F(k.Kyz)} × {F(mz)}, kyy = {F(k.Kyy)}, kyz = {F(k.Kyz)}; {common}" : null;
            string d62 = describe ? $"(6.62) = {F(nz)} + {F(k.Kzy)} × {F(my)} + {F(k.Kzz)} × {F(mz)}, kzy = {F(k.Kzy)}, kzz = {F(k.Kzz)}; {common}" : null;

            // Named by the axis their χ is about, as BucklingY and BucklingZ are.
            if (section.MajorIsZ)
            {
                result.Z = eq61; result.DetailZ = d61;
                result.Y = eq62; result.DetailY = d62;
            }
            else
            {
                result.Y = eq61; result.DetailY = d61;
                result.Z = eq62; result.DetailZ = d62;
            }

            return result;
        }

        /// <summary>Table B.3 for one diagram: the larger end moment, the other as ψ of it, and the span moment.</summary>
        private static double Cm(Diagram moment)
        {
            double start = moment.At(0.0), end = moment.At(1.0);
            double mh = Math.Abs(start) >= Math.Abs(end) ? start : end;
            double other = Math.Abs(start) >= Math.Abs(end) ? end : start;
            double psi = mh != 0.0 ? other / mh : 1.0;
            return Ec3.EquivalentMoment(mh, psi, moment.Span);
        }

        /// <summary>Where the sections sit, 0 to 1: as given, or evenly from end to end.</summary>
        private static IReadOnlyList<double> Positions(int count, IReadOnlyList<double> stations)
        {
            if (stations != null && stations.Count == count)
                return stations;
            if (count == 1)
                return new[] { 0.5 };
            return Enumerable.Range(0, count).Select(i => (double)i / (count - 1)).ToList();
        }

        /// <summary>
        /// A moment along the member as a parabola in t = x / L, from 0 to 1. Between its nodes a beam
        /// under a uniform load carries exactly that, whatever its material - a forceBeamColumn's
        /// moments come from equilibrium - so a least squares fit through the integration sections
        /// lands on them. Where it does not, the sections do not lie on one, and the check says so and
        /// takes the safe side.
        /// </summary>
        private struct Diagram
        {
            public double C0, C1, C2;
            public bool Parabola;

            public double At(double t) => C0 + t * (C1 + t * C2);

            /// <summary>The same parabola over [from, to], in that piece's own t.</summary>
            public Diagram Slice(double from, double to)
            {
                double d = to - from;
                return new Diagram { C0 = At(from), C1 = (C1 + 2.0 * C2 * from) * d, C2 = C2 * d * d, Parabola = Parabola };
            }

            private double Turn => C2 != 0.0 ? -C1 / (2.0 * C2) : double.NaN;
            private bool TurnsInside => C2 != 0.0 && Turn > 0.0 && Turn < 1.0;

            /// <summary>The largest size, at an end or where it turns.</summary>
            public double Peak
            {
                get
                {
                    double peak = Math.Max(Math.Abs(At(0.0)), Math.Abs(At(1.0)));
                    return TurnsInside ? Math.Max(peak, Math.Abs(At(Turn))) : peak;
                }
            }

            /// <summary>What Table B.3 sets against the end moments: where the parabola turns, if it does in the span, or else mid-span.</summary>
            public double Span => TurnsInside ? At(Turn) : At(0.5);

            public static Diagram Fit(IReadOnlyList<double> t, IReadOnlyList<double> m)
            {
                int n = m.Count;
                if (n == 0)
                    return new Diagram { Parabola = true };
                if (n == 1)
                    return new Diagram { C0 = m[0], Parabola = true };

                // Normal equations of the least squares parabola - or straight line, through two.
                int order = n >= 3 ? 3 : 2;
                var s = new double[5];
                var r = new double[3];
                for (int i = 0; i < n; i++)
                {
                    double p = 1.0;
                    for (int j = 0; j < 5; j++) { s[j] += p; p *= t[i]; }
                    p = 1.0;
                    for (int j = 0; j < 3; j++) { r[j] += p * m[i]; p *= t[i]; }
                }

                var a = new double[order, order + 1];
                for (int i = 0; i < order; i++)
                {
                    for (int j = 0; j < order; j++) a[i, j] = s[i + j];
                    a[i, order] = r[i];
                }

                var c = Solve(a, order);
                var fit = new Diagram { C0 = c[0], C1 = c[1], C2 = order == 3 ? c[2] : 0.0 };

                double size = m.Max(x => Math.Abs(x));
                double off = 0.0;
                for (int i = 0; i < n; i++)
                    off = Math.Max(off, Math.Abs(fit.At(t[i]) - m[i]));
                fit.Parabola = off <= 1e-4 * size + 1e-12;
                return fit;
            }

            /// <summary>Gaussian elimination with partial pivoting on an augmented matrix.</summary>
            private static double[] Solve(double[,] a, int n)
            {
                for (int col = 0; col < n; col++)
                {
                    int pivot = col;
                    for (int row = col + 1; row < n; row++)
                        if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col])) pivot = row;
                    for (int j = 0; j <= n; j++)
                    {
                        double swap = a[col, j]; a[col, j] = a[pivot, j]; a[pivot, j] = swap;
                    }

                    if (a[col, col] == 0.0)
                        continue;
                    for (int row = col + 1; row < n; row++)
                    {
                        double factor = a[row, col] / a[col, col];
                        for (int j = col; j <= n; j++) a[row, j] -= factor * a[col, j];
                    }
                }

                var x = new double[n];
                for (int row = n - 1; row >= 0; row--)
                {
                    double sum = a[row, n];
                    for (int j = row + 1; j < n; j++) sum -= a[row, j] * x[j];
                    x[row] = a[row, row] != 0.0 ? sum / a[row, row] : 0.0;
                }
                return x;
            }
        }

        private static string Value(double? x) => x.HasValue ? F(x.Value) : "-";

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
