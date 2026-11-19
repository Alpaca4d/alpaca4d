using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Alpaca4d.Generic;
using Alpaca4d.Loads;
using Alpaca4d.Material;
using Alpaca4d.Section;

namespace Alpaca4d.Design
{
    /// <summary>
    /// A timber cross-section as the EN 1995-1-1 check sees it: a solid rectangle or a solid circle,
    /// in the beam's local axes. Mz bends across <see cref="DepthZ"/> - a rectangle's Height - and My
    /// across <see cref="DepthY"/>, its Width, as Rectangle Section gives OpenSees Iz and Iy.
    /// </summary>
    public sealed class TimberSection
    {
        public bool Round { get; private set; }
        public string Description { get; private set; }

        public double A { get; private set; }
        public double Iy { get; private set; }
        public double Iz { get; private set; }
        /// <summary>St. Venant torsion constant.</summary>
        public double It { get; private set; }
        /// <summary>Elastic section modulus for My, and for Mz.</summary>
        public double Wy { get; private set; }
        public double Wz { get; private set; }
        /// <summary>The depth My bends across, and Mz: k_h's depth in bending, and the load height of lateral-torsional buckling.</summary>
        public double DepthY { get; private set; }
        public double DepthZ { get; private set; }
        /// <summary>The largest dimension, k_h's size in tension.</summary>
        public double Largest => Math.Max(DepthY, DepthZ);

        /// <summary>Largest shear stress of a shear force V over V / A: 3/2 for a rectangle, 4/3 for a circle.</summary>
        public double ShearShape => Round ? 4.0 / 3.0 : 1.5;

        /// <summary>The axis of the larger second moment, which lateral-torsional buckling bends about.</summary>
        public bool MajorIsZ => Iz >= Iy;

        public double RadiusY => Math.Sqrt(Iy / A);
        public double RadiusZ => Math.Sqrt(Iz / A);

        /// <summary>k_shape of (6.15).</summary>
        public double Kshape => Round ? Ec5.KshapeCircle : Ec5.KshapeRectangle(DepthY, DepthZ);

        /// <summary>k_m of 6.1.6(2).</summary>
        public double Km => Ec5.Km(!Round);

        /// <summary>The largest St. Venant shear stress under a torque.</summary>
        public double TorsionStress(double torque) =>
            Round ? Ec5.TorsionStressCircle(torque, DepthY) : Ec5.TorsionStressRectangle(torque, DepthY, DepthZ);

        /// <summary>
        /// The section as the check sees it, or null and why: a section the check does not cover - a
        /// hollow circle, an I, an Elastic Section with no shape behind it.
        /// </summary>
        public static TimberSection Of(IUniaxialSection section, out string problem)
        {
            problem = null;
            switch (section)
            {
                case RectangleCS rectangle when rectangle.Width > 0.0 && rectangle.Height > 0.0:
                {
                    double w = rectangle.Width, h = rectangle.Height;
                    return new TimberSection
                    {
                        Description = $"rectangle {Number(h)} x {Number(w)} (Height x Width)",
                        A = w * h,
                        Iz = w * h * h * h / 12.0,
                        Iy = h * w * w * w / 12.0,
                        It = rectangle.J,
                        Wz = w * h * h / 6.0,
                        Wy = h * w * w / 6.0,
                        DepthZ = h,
                        DepthY = w,
                    };
                }
                case CircleCS circle when circle.Diameter > 0.0 && (circle.Thickness == 0.0 || circle.Thickness >= circle.Diameter / 2.0):
                {
                    double d = circle.Diameter;
                    double i = Math.PI * Math.Pow(d, 4) / 64.0;
                    return new TimberSection
                    {
                        Round = true,
                        Description = $"round {Number(d)}",
                        A = Math.PI * d * d / 4.0,
                        Iy = i,
                        Iz = i,
                        It = 2.0 * i,
                        Wy = Math.PI * Math.Pow(d, 3) / 32.0,
                        Wz = Math.PI * Math.Pow(d, 3) / 32.0,
                        DepthY = d,
                        DepthZ = d,
                    };
                }
                case CircleCS _:
                    problem = "a hollow circle, while timber is checked as a solid rectangle or a solid circle";
                    return null;
                case null:
                    problem = "no section";
                    return null;
                default:
                    problem = $"of type {section.GetType().Name}, while timber is checked as a solid rectangle (Rectangle Section) or a solid circle (Circle Section)";
                    return null;
            }
        }

        private static string Number(double x) => x.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The EN 1995-1-1 check of one timber member: the cross-section at every integration section
    /// (6.1, 6.2) and the member against buckling (6.3), with k_mod for its service class and the
    /// duration of its loads, and k_h for its size.
    ///
    /// Not covered, and said so in every report: compression and tension across the grain (6.1.5,
    /// bearing at supports and under point loads), shear and torsion together, notched, curved and
    /// tapered members (6.4, 6.5), system strength k_sys (6.6), connections and fire.
    /// </summary>
    public static class TimberCheck
    {
        private const string NotCovered =
            "Not checked: compression and tension across the grain (6.1.5, bearing), shear with torsion, notched, curved and " +
            "tapered members (6.4, 6.5), system strength k_sys (6.6, taken as 1), connections, fire.";

        /// <summary>Checks one member.</summary>
        /// <param name="forces">The section forces at each integration section, I end to J end.</param>
        /// <param name="member">Its length, buckling lengths, distance between lateral restraints, load position and service class.</param>
        /// <param name="settings">The duration of the loads, whether the combination is accidental, γM if given, and whether to report.</param>
        /// <param name="stations">Where each integration section sits, 0 to 1; null for evenly from end to end.</param>
        public static MemberUtilisation Run(TimberSection section, TimberGrade grade, IReadOnlyList<SectionForces> forces,
                                            SteelMember member, DesignSettings settings, IReadOnlyList<double> stations = null)
        {
            settings = settings ?? new DesignSettings();
            member = member ?? new SteelMember();
            bool describe = settings.Report;
            var result = new MemberUtilisation { Type = grade.Family };
            var text = new SteelCheck.Report(describe);

            var lacking = grade.Missing();
            if (lacking.Length > 0)
            {
                result.NotCheckedWhy = $"its grade {grade.Name} has no {string.Join(", ", lacking)}";
                result.Report = describe ? $"EN 1995-1-1, {grade.Name}: not checked, {result.NotCheckedWhy}." : "";
                return result;
            }

            int serviceClass = member.ServiceClass;
            if (serviceClass < 1 || serviceClass > 3)
            {
                result.NotCheckedWhy = $"service class {serviceClass} is not 1, 2 or 3";
                result.Report = describe ? $"EN 1995-1-1, {grade.Name}: not checked, {result.NotCheckedWhy}." : "";
                return result;
            }

            var duration = settings.Duration ?? LoadDuration.Permanent;
            double kmod = Ec5.Kmod(serviceClass, duration);
            result.Kmod = kmod;
            bool gammaGiven = settings.GammaM.HasValue;
            double gammaM = settings.GammaM ?? Ec5.GammaM(grade.Kind, settings.Accidental);
            double factor = kmod / gammaM;

            double MPa(double x) => ModelStress.FromMPa(x);
            double Mm(double x) => ModelLength.ToMillimetres(x);

            double khY = Ec5.Kh(grade.Kind, grade.RhoK, Mm(section.DepthY));
            double khZ = Ec5.Kh(grade.Kind, grade.RhoK, Mm(section.DepthZ));
            double khT = Ec5.Kh(grade.Kind, grade.RhoK, Mm(section.Largest));

            double ft0d = factor * khT * MPa(grade.Ft0K);
            double fc0d = factor * MPa(grade.Fc0K);
            double fmyd = factor * khY * MPa(grade.FmK);
            double fmzd = factor * khZ * MPa(grade.FmK);
            double fvd = factor * MPa(grade.FvK);
            double km = section.Km;
            double kshape = section.Kshape;
            double kcr = Ec5.Kcr;

            string product = grade.Kind == TimberKind.Glulam ? "glulam" : "solid timber";
            text.Line(() => $"EN 1995-1-1, {grade.Name} ({product}): f_m,k = {F(grade.FmK)}, f_t,0,k = {F(grade.Ft0K)}, f_c,0,k = {F(grade.Fc0K)}, " +
                            $"f_v,k = {F(grade.FvK)}, E_0,05 = {F(grade.E005)}, G_0,05 = {F(grade.G005)}" +
                            (grade.G005Tabulated ? "" : " (G_mean E_0,05 / E_0,mean)") + $" N/mm², ρ_k = {F(grade.RhoK)} kg/m³");
            text.Line(() => $"k_mod = {F(kmod)}: service class {serviceClass}, {LoadDurations.NameOf(duration).ToLowerInvariant()} loads" +
                            (string.IsNullOrEmpty(settings.DurationNote) ? "" : $" ({settings.DurationNote})") + " (Table 3.1)");
            text.Line(() => $"γM = {F(gammaM)}" + (gammaGiven ? " (given)" : settings.Accidental ? " (Table 2.3, accidental combination)" : $" (Table 2.3, {product})") +
                            $"; k_h = {F(khY)} for My, {F(khZ)} for Mz, {F(khT)} in tension; k_cr = {F(kcr)}; k_m = {F(km)}; k_shape = {F(kshape)}");
            text.Line(() => $"Section: {section.Description}");
            text.Line(() => $"  A = {G(section.A)}, Iy = {G(section.Iy)}, Iz = {G(section.Iz)}, It = {G(section.It)}, Wy = {G(section.Wy)}, Wz = {G(section.Wz)}");
            text.Line(() => $"  f_t,0,d = {G(ft0d)}, f_c,0,d = {G(fc0d)}, f_m,y,d = {G(fmyd)}, f_m,z,d = {G(fmzd)}, f_v,d = {G(fvd)}");

            if (forces == null || forces.Count == 0)
            {
                text.Line(() => "No section forces: the recorder file holds none for this member.");
                result.NotCheckedWhy = "the recorder file holds no section forces for it";
                result.Report = text.ToString();
                return result;
            }

            var axial = new SteelCheck.Worst();
            var shearY = new SteelCheck.Worst();
            var shearZ = new SteelCheck.Worst();
            var torsion = new SteelCheck.Worst();
            var bendingY = new SteelCheck.Worst();
            var bendingZ = new SteelCheck.Worst();
            var combined = new SteelCheck.Worst();

            for (int k = 0; k < forces.Count; k++)
            {
                var f = forces[k];
                double sigmaN = f.N / section.A;
                double sigmaMy = Math.Abs(f.My) / section.Wy;
                double sigmaMz = Math.Abs(f.Mz) / section.Wz;

                if (f.N >= 0.0)
                    axial.Offer(sigmaN / ft0d, k, () => $"σ_t,0,d = {G(sigmaN)} against f_t,0,d = {G(ft0d)} (6.1.2)");
                else
                    axial.Offer(-sigmaN / fc0d, k, () => $"σ_c,0,d = {G(-sigmaN)} against f_c,0,d = {G(fc0d)} (6.1.4)");

                double tauY = section.ShearShape * Math.Abs(f.Vy) / (kcr * section.A);
                double tauZ = section.ShearShape * Math.Abs(f.Vz) / (kcr * section.A);
                shearY.Offer(tauY / fvd, k, () => $"τ_y,d = {F(section.ShearShape)} Vy / (k_cr A) = {G(tauY)} against f_v,d = {G(fvd)} (6.1.7)");
                shearZ.Offer(tauZ / fvd, k, () => $"τ_z,d = {F(section.ShearShape)} Vz / (k_cr A) = {G(tauZ)} against f_v,d = {G(fvd)} (6.1.7)");

                double tauT = section.TorsionStress(f.T);
                torsion.Offer(tauT / (kshape * fvd), k, () => $"τ_tor,d = {G(tauT)} against k_shape f_v,d = {G(kshape * fvd)} (6.1.8)");

                double ry = sigmaMy / fmyd, rz = sigmaMz / fmzd;
                bendingY.Offer(ry, k, () => $"σ_m,y,d = {G(sigmaMy)} against f_m,y,d = {G(fmyd)} (6.1.6)");
                bendingZ.Offer(rz, k, () => $"σ_m,z,d = {G(sigmaMz)} against f_m,z,d = {G(fmzd)} (6.1.6)");

                // 6.2.3 in tension, 6.2.4 in compression, 6.1.6 with neither: the axial term, then
                // each axis in full with k_m on the other.
                double n = f.N >= 0.0 ? sigmaN / ft0d : Math.Pow(-sigmaN / fc0d, 2);
                double first = n + ry + km * rz, second = n + km * ry + rz;
                string clause = f.N > 0.0 ? "(6.17), (6.18)" : f.N < 0.0 ? "(6.19), (6.20)" : "(6.11), (6.12)";
                string axialTerm = f.N > 0.0 ? "σ_t,0,d/f_t,0,d" : f.N < 0.0 ? "(σ_c,0,d/f_c,0,d)²" : "";
                combined.Offer(Math.Max(first, second), k, () =>
                    $"{(axialTerm.Length > 0 ? axialTerm + $" = {F(n)}, " : "")}σ_m,y,d/f_m,y,d = {F(ry)}, σ_m,z,d/f_m,z,d = {F(rz)}: " +
                    $"{F(first)} and {F(second)} by {clause}");
            }

            result.Axial = axial.Value;
            result.ShearY = shearY.Value;
            result.ShearZ = shearZ.Value;
            result.Torsion = torsion.Value;
            result.BendingY = bendingY.Value;
            result.BendingZ = bendingZ.Value;
            result.Combined = combined.Value;

            // 6.3.2, for the largest compression anywhere along the member.
            double compression = forces.Max(f => Math.Max(0.0, -f.N));
            double sigmaC = compression / section.A;
            double e005 = MPa(grade.E005), fc0k = MPa(grade.Fc0K);
            double betaC = Ec5.BetaC(grade.Kind);

            double lambdaY = member.LengthY / section.RadiusY, lambdaZ = member.LengthZ / section.RadiusZ;
            double relY = Ec5.RelativeSlenderness(lambdaY, fc0k, e005), relZ = Ec5.RelativeSlenderness(lambdaZ, fc0k, e005);
            double kcY = Ec5.Kc(relY, betaC), kcZ = Ec5.Kc(relZ, betaC);

            string Buckling(double length, double lambda, double rel, double kc) =>
                $"L_cr = {F(length)}, λ = {F(lambda)}, λ_rel = {F(rel)}, β_c = {F(betaC)}, k_c = {F(kc)}";

            result.BucklingY = sigmaC / (kcY * fc0d);
            result.BucklingZ = sigmaC / (kcZ * fc0d);
            string bucklingYDetail = compression > 0.0
                ? $"σ_c,0,d = {G(sigmaC)} against k_c,y f_c,0,d = {G(kcY * fc0d)}; {Buckling(member.LengthY, lambdaY, relY, kcY)} (6.3.2)"
                : "not in compression";
            string bucklingZDetail = compression > 0.0
                ? $"σ_c,0,d = {G(sigmaC)} against k_c,z f_c,0,d = {G(kcZ * fc0d)}; {Buckling(member.LengthZ, lambdaZ, relZ, kcZ)} (6.3.2)"
                : "not in compression";

            // (6.23) and (6.24), at every section with the member's largest compression. Up to λ_rel
            // 0.3 about both axes, 6.3.2(2) leaves it to (6.19) and (6.20), in Combined.
            string interactionYDetail, interactionZDetail;
            SteelCheck.Worst interactionY = null, interactionZ = null;
            if (compression <= 0.0)
            {
                result.InteractionY = 0.0;
                result.InteractionZ = 0.0;
                interactionYDetail = interactionZDetail = "not in compression: bending alone is in Combined";
            }
            else if (relY <= 0.3 && relZ <= 0.3)
            {
                result.InteractionY = 0.0;
                result.InteractionZ = 0.0;
                interactionYDetail = interactionZDetail = $"λ_rel,y = {F(relY)} and λ_rel,z = {F(relZ)}, both up to 0.3: (6.19) and (6.20) apply, in Combined (6.3.2(2))";
            }
            else
            {
                var y = new SteelCheck.Worst();
                var z = new SteelCheck.Worst();
                double cy = sigmaC / (kcY * fc0d), cz = sigmaC / (kcZ * fc0d);
                for (int k = 0; k < forces.Count; k++)
                {
                    double ry = Math.Abs(forces[k].My) / section.Wy / fmyd, rz = Math.Abs(forces[k].Mz) / section.Wz / fmzd;
                    y.Offer(cy + ry + km * rz, k, () => $"σ_c,0,d/(k_c,y f_c,0,d) = {F(cy)} + σ_m,y,d/f_m,y,d = {F(ry)} + k_m σ_m,z,d/f_m,z,d = {F(km * rz)} (6.23)");
                    z.Offer(cz + km * ry + rz, k, () => $"σ_c,0,d/(k_c,z f_c,0,d) = {F(cz)} + k_m σ_m,y,d/f_m,y,d = {F(km * ry)} + σ_m,z,d/f_m,z,d = {F(rz)} (6.24)");
                }
                result.InteractionY = y.Value;
                result.InteractionZ = z.Value;
                interactionY = y;
                interactionZ = z;
                interactionYDetail = describe ? $"{y.Detail?.Invoke()}, {At(y.At, forces.Count, stations)}" : null;
                interactionZDetail = describe ? $"{z.Detail?.Invoke()}, {At(z.At, forces.Count, stations)}" : null;
            }

            // 6.3.3.
            var ltb = LateralTorsional(section, grade, member, forces, stations, compression, sigmaC,
                                       section.MajorIsZ ? kcY : kcZ, fc0d, section.MajorIsZ ? fmzd : fmyd, describe);
            result.LateralTorsional = ltb.Value;
            if (!ltb.Value.HasValue) result.Missing.Add("LTB");

            result.Checked = true;

            if (describe)
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
                text.Line(() => $"BucklingY  {Value(result.BucklingY)}  {bucklingYDetail}");
                text.Line(() => $"BucklingZ  {Value(result.BucklingZ)}  {bucklingZDetail}");
                text.Line(() => $"LTB        {Value(result.LateralTorsional)}  {ltb.Detail}");
                text.Line(() => $"InteractionY {Value(result.InteractionY)}  {interactionYDetail}");
                text.Line(() => $"InteractionZ {Value(result.InteractionZ)}  {interactionZDetail}");
                text.Line(() => result.Max.HasValue
                    ? $"Max {F(result.Max.Value)}, from {result.Governing}"
                    : $"No Max: {result.Governing}. The member is not verified until it is.");
                text.Line(() => NotCovered);

                var positions = SteelCheck.Positions(forces.Count, stations);
                TimberReportCheck Entry(string name, double? value, string clause, SteelCheck.Worst at, string detail = null) => new TimberReportCheck
                {
                    Name = name, Value = value, Clause = clause,
                    Section = at != null ? at.At + 1 : 0,
                    Position = at != null ? positions[at.At] : (double?)null,
                    Detail = detail ?? at?.Detail?.Invoke(),
                };
                double n = forces[combined.At].N;
                result.Details = new TimberReport(text.ToString())
                {
                    Grade = grade.Name, Product = product,
                    FmK = grade.FmK, Ft0K = grade.Ft0K, Fc0K = grade.Fc0K, FvK = grade.FvK, E005 = grade.E005, G005 = grade.G005, RhoK = grade.RhoK,
                    ServiceClass = serviceClass, Duration = LoadDurations.NameOf(duration), DurationSource = settings.DurationNote ?? "",
                    Kmod = kmod, GammaM = gammaM,
                    GammaMSource = gammaGiven ? "given" : settings.Accidental ? "Table 2.3, accidental combination" : $"Table 2.3, {product}",
                    KhY = khY, KhZ = khZ, KhTension = khT, Kcr = kcr, Km = km, Kshape = kshape,
                    Section = section.Description, A = section.A, Iy = section.Iy, Iz = section.Iz, It = section.It, Wy = section.Wy, Wz = section.Wz,
                    Ft0d = ft0d, Fc0d = fc0d, FmYd = fmyd, FmZd = fmzd, Fvd = fvd,
                    Compression = compression, LambdaRelY = relY, LambdaRelZ = relZ, KcY = kcY, KcZ = kcZ,
                    Mcr = ltb.Mcr, SigmaMCrit = ltb.SigmaCrit, LambdaRelM = ltb.Rel, Kcrit = ltb.Kcrit,
                    Checks = new List<TimberReportCheck>
                    {
                        Entry("Axial", result.Axial, forces[axial.At].N >= 0.0 ? "6.1.2" : "6.1.4", axial),
                        Entry("ShearY", result.ShearY, "6.1.7", shearY),
                        Entry("ShearZ", result.ShearZ, "6.1.7", shearZ),
                        Entry("Torsion", result.Torsion, "6.1.8", torsion),
                        Entry("BendingY", result.BendingY, "6.1.6", bendingY),
                        Entry("BendingZ", result.BendingZ, "6.1.6", bendingZ),
                        Entry("Combined", result.Combined, n > 0.0 ? "6.2.3, (6.17), (6.18)" : n < 0.0 ? "6.2.4, (6.19), (6.20)" : "6.1.6, (6.11), (6.12)", combined),
                        Entry("BucklingY", result.BucklingY, "6.3.2", null, bucklingYDetail),
                        Entry("BucklingZ", result.BucklingZ, "6.3.2", null, bucklingZDetail),
                        Entry("LTB", result.LateralTorsional, ltb.WithCompression ? "6.3.3, (6.35)" : "6.3.3, (6.33)", null, ltb.Detail),
                        Entry("InteractionY", result.InteractionY, "6.3.2, (6.23)", interactionY, interactionYDetail),
                        Entry("InteractionZ", result.InteractionZ, "6.3.2, (6.24)", interactionZ, interactionZDetail),
                    },
                    Max = result.Max, Governing = result.Governing, NotCovered = NotCovered,
                };
            }

            result.Report = text.ToString();
            return result;
        }

        /// <summary>
        /// A lower bound on the Max of <see cref="Run"/> for the same section and forces, cheap enough to
        /// rule a candidate out before its whole check: the largest of tension or compression, shear and
        /// bending about each axis alone, at k_mod for service class 1 - the largest for any duration -
        /// so it holds whatever service class the member is in. Zero for a grade the check cannot use.
        /// </summary>
        public static double LowerBound(TimberSection section, TimberGrade grade, IReadOnlyList<SectionForces> forces, DesignSettings settings)
        {
            if (section == null || grade == null || forces == null || grade.Missing().Length > 0)
                return 0.0;
            settings = settings ?? new DesignSettings();

            double kmod = Ec5.Kmod(1, settings.Duration ?? LoadDuration.Permanent);
            double gammaM = settings.GammaM ?? Ec5.GammaM(grade.Kind, settings.Accidental);
            double factor = kmod / gammaM;
            double Mm(double x) => ModelLength.ToMillimetres(x);
            double ft0d = factor * Ec5.Kh(grade.Kind, grade.RhoK, Mm(section.Largest)) * ModelStress.FromMPa(grade.Ft0K);
            double fc0d = factor * ModelStress.FromMPa(grade.Fc0K);
            double fmyd = factor * Ec5.Kh(grade.Kind, grade.RhoK, Mm(section.DepthY)) * ModelStress.FromMPa(grade.FmK);
            double fmzd = factor * Ec5.Kh(grade.Kind, grade.RhoK, Mm(section.DepthZ)) * ModelStress.FromMPa(grade.FmK);
            double fvd = factor * ModelStress.FromMPa(grade.FvK);

            double bound = 0.0;
            foreach (var f in forces)
            {
                double axial = f.N >= 0.0 ? f.N / section.A / ft0d : -f.N / section.A / fc0d;
                double shear = section.ShearShape * Math.Max(Math.Abs(f.Vy), Math.Abs(f.Vz)) / (Ec5.Kcr * section.A) / fvd;
                double bending = Math.Max(Math.Abs(f.My) / section.Wy / fmyd, Math.Abs(f.Mz) / section.Wz / fmzd);
                bound = Math.Max(bound, Math.Max(axial, Math.Max(shear, bending)));
            }
            return bound;
        }

        private class Lateral
        {
            public double? Value;
            public string Detail;
            /// <summary>The worst piece's M_cr, σ_m,crit, λ_rel,m and k_crit, and whether (6.35) gave its value.</summary>
            public double? Mcr, SigmaCrit, Rel, Kcrit;
            public bool WithCompression;
        }

        /// <summary>
        /// 6.3.3: (6.33), σ_m,d against k_crit f_m,d, and with compression (6.35), its square plus
        /// σ_c,0,d / (k_c,z f_c,0,d) with k_c about the minor axis - for every piece of the member
        /// between lateral restraints, and the worst of them.
        ///
        /// σ_m,crit is M_cr / W "according to the classical theory of stability, with 5-percentile
        /// stiffness values" (6.3.3(2)): M_cr from <see cref="CriticalMoment"/> on the piece's own part
        /// of the moment diagram, with E_0,05 and G_0,05, fork supports and the load at the height
        /// <see cref="SteelMember.Load"/> puts it - what Table 6.1's effective lengths approximate. An
        /// L_LT longer than the member takes its peak moment as uniform over all of it, as the steel
        /// check does.
        /// </summary>
        private static Lateral LateralTorsional(TimberSection section, TimberGrade grade, SteelMember member,
                                                IReadOnlyList<SectionForces> forces, IReadOnlyList<double> stations,
                                                double compression, double sigmaC, double kcMinor, double fc0d, double fmd, bool describe)
        {
            var result = new Lateral();
            if (section.Round)
            {
                result.Value = 0.0;
                result.Detail = "round: the same about every axis, so nothing to buckle sideways";
                return result;
            }

            bool majorIsZ = section.MajorIsZ;
            double w = majorIsZ ? section.Wz : section.Wy;
            double depth = majorIsZ ? section.DepthZ : section.DepthY;
            double lateralInertia = majorIsZ ? section.Iy : section.Iz;
            var positions = SteelCheck.Positions(forces.Count, stations);
            var values = forces.Select(f => majorIsZ ? f.Mz : f.My).ToList();
            var major = SteelCheck.Diagram.Fit(positions, values);
            double peak = Math.Max(major.Peak, values.Max(m => Math.Abs(m)));
            double fmk = ModelStress.FromMPa(grade.FmK);

            if (peak <= 1e-9 * w * fmd)
            {
                result.Value = 0.0;
                result.Detail = "no bending about the major axis";
                return result;
            }

            double length = member.Length;
            double llt = member.LengthLT;
            double e = ModelStress.FromMPa(grade.E005), g = ModelStress.FromMPa(grade.G005);
            if (length <= 0.0 || llt <= 0.0)
            {
                result.Detail = "no length to work out M_cr from - not checked";
                return result;
            }

            // A load on the top face, the side it comes from, is a + depth/2 from the centroid; hung
            // from the bottom, − depth/2. The section is doubly symmetric, so which way it acts does
            // not matter.
            double q = 2.0 * Math.Abs(major.C2) / (length * length);
            double a = member.Load == LoadPosition.Destabilising ? depth / 2.0 : member.Load == LoadPosition.Stabilising ? -depth / 2.0 : 0.0;

            bool longer = llt > length * (1.0 + 1e-6);
            var pieces = new List<(double From, double To, double Length, SteelCheck.Diagram Moment, bool Uniform)>();
            if (longer || !major.Parabola)
            {
                pieces.Add((0.0, 1.0, llt, new SteelCheck.Diagram { C0 = peak, Parabola = true }, true));
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
            bool unknown = false;
            Func<string> worstDetail = null;
            // Whether a piece was weighed yet: the detail is only written with the report on, so it cannot tell.
            bool weighed = false;
            double compressionTerm = compression > 0.0 ? sigmaC / (kcMinor * fc0d) : 0.0;

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

                if (mEd <= 1e-9 * w * fmd)
                    continue;

                var m = piece.Moment;
                double multiplier = piece.Uniform
                    ? CriticalMoment.Multiplier(piece.Length, mEd, 0.0, 0.0, q, a, e, lateralInertia, 0.0, g, section.It)
                    : CriticalMoment.Multiplier(piece.Length, m.C0, m.C1, m.C2, q, a, e, lateralInertia, 0.0, g, section.It);
                double mcr = multiplier * (piece.Uniform ? mEd : m.Peak);
                if (double.IsNaN(mcr))
                {
                    unknown = true;
                    continue;
                }

                double sigmaCrit = mcr / w;
                double rel = Ec5.RelativeSlendernessM(fmk, sigmaCrit);
                double kcrit = Ec5.Kcrit(rel);
                double sigmaM = mEd / w;
                double bending = sigmaM / (kcrit * fmd);
                double withCompression = compression > 0.0 ? bending * bending + compressionTerm : 0.0;
                double value = Math.Max(bending, withCompression);

                if (value > worst || !weighed)
                {
                    weighed = true;
                    worst = value;
                    result.Mcr = mcr;
                    result.SigmaCrit = sigmaCrit;
                    result.Rel = rel;
                    result.Kcrit = kcrit;
                    result.WithCompression = withCompression > bending;
                    if (describe)
                    {
                        var p = piece;
                        double pieceMcr = mcr, pieceCrit = sigmaCrit, pieceRel = rel, pieceKcrit = kcrit, pieceSigma = sigmaM,
                               pieceBending = bending, pieceWith = withCompression;
                        worstDetail = () =>
                        {
                            string where = p.Uniform
                                ? longer
                                    ? $"L_LT = {F(p.Length)}, longer than the member ({F(length)}): its peak moment taken as uniform over all of it"
                                    : $"L_LT = {F(p.Length)}: the moments along the member do not lie on a parabola, so its peak is taken as uniform"
                                : pieces.Count == 1
                                    ? $"L_LT = {F(p.Length)}"
                                    : $"L_LT = {F(p.Length)}, restrained at {pieces.Count - 1} point(s) between the ends, worst between x = {F(p.From)} L and {F(p.To)} L";
                            string load = member.Load == LoadPosition.ShearCentre || q == 0.0 ? "" : $", load {member.Load.ToString().ToLowerInvariant()} (a = {G(a)} from the centroid)";
                            string both = compression > 0.0
                                ? $"; with compression (6.35): {F(pieceBending)}² + σ_c,0,d/(k_c f_c,0,d) = {F(compressionTerm)} gives {F(pieceWith)}"
                                : "";
                            return $"{where}, fork supports{load}: M_cr = {G(pieceMcr)} with E_0,05 and G_0,05, σ_m,crit = {G(pieceCrit)}, " +
                                   $"λ_rel,m = {F(pieceRel)}, k_crit = {F(pieceKcrit)}: σ_m,d = {G(pieceSigma)} against k_crit f_m,d = {G(pieceKcrit * fmd)}, " +
                                   $"{F(pieceBending)} (6.33){both}";
                        };
                    }
                }
            }

            if (unknown)
            {
                result.Detail = "the section has no stiffness to buckle sideways against - not checked";
                return result;
            }

            result.Value = worst;
            result.Detail = worstDetail?.Invoke() ?? "no bending about the major axis";
            return result;
        }

        private static string Value(double? x) => x.HasValue ? F(x.Value) : "-";

        private static void Line(SteelCheck.Report text, string name, SteelCheck.Worst worst, int count, IReadOnlyList<double> stations)
        {
            text.Line(() => $"{name,-10} {F(worst.Value)}  {At(worst.At, count, stations)}: {worst.Detail?.Invoke()}");
        }

        private static string At(int index, int count, IReadOnlyList<double> stations)
        {
            string where = stations != null && index < stations.Count ? $", x = {F(stations[index])} L" : "";
            return $"at section {index + 1} of {count}{where}";
        }

        private static string F(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
        private static string G(double x) => x.ToString("G4", CultureInfo.InvariantCulture);
    }
}
