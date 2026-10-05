using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Result;
using Alpaca4d.Section;

namespace Alpaca4d.Design
{
    /// <summary>
    /// How a section was made, which is all Table 6.2 needs to know about it and all Alpaca4d cannot
    /// tell from its dimensions.
    /// </summary>
    public enum Fabrication
    {
        /// <summary>Rolled I and H sections, hot-finished hollow sections.</summary>
        Rolled,
        /// <summary>Welded I sections, cold-formed hollow sections.</summary>
        Welded
    }

    public enum SteelShape
    {
        I,
        Box,
        Tube,
        SolidRound,
        SolidRectangle,
        DoubleAngle
    }

    public enum PartKind
    {
        /// <summary>Supported along both edges: a web, a wall of a box. Table 5.2 sheet 1.</summary>
        Internal,
        /// <summary>Free along one edge: a flange outstand, a leg of an angle. Table 5.2 sheet 2.</summary>
        Outstand,
        /// <summary>A circular hollow section as a whole. Table 5.2 sheet 3.</summary>
        Tube,
        /// <summary>An angle as a whole, for the extra limits of Table 5.2 sheet 3.</summary>
        Angle
    }

    /// <summary>Which reduced plastic moment of 6.2.9.1 the shape has.</summary>
    public enum PlasticInteraction
    {
        /// <summary>Equations (6.36) to (6.38), α = 2 and β = 5n.</summary>
        DoublySymmetricI,
        /// <summary>Equations (6.39) and (6.40), α = β = 1.66 / (1 − 1.13 n²).</summary>
        UniformBox,
        /// <summary>M_pl (1 − n^1.7), α = β = 2.</summary>
        Round,
        /// <summary>Equation (6.32) on each axis, α = β = 1.</summary>
        SolidRectangle,
        /// <summary>None given: the linear sum of 6.2.1(7), which the standard allows for any section.</summary>
        Linear
    }

    /// <summary>
    /// One compression part of a cross-section, as Table 5.2 sees it: a width c, a thickness t, and
    /// where its two ends are, so the stress along it can be worked out for the forces at hand.
    /// </summary>
    public class SteelPart
    {
        public string Name;
        public PartKind Kind;

        /// <summary>The width c of Table 5.2 - for a tube its diameter, for an angle its long leg.</summary>
        public double C;
        public double T;
        /// <summary>An angle's other leg.</summary>
        public double B;

        /// <summary>The part's two ends, in centroidal (y, z).</summary>
        public double Y1, Z1, Y2, Z2;

        /// <summary>
        /// Whether the part runs along local y, so that Mz varies the stress along it. Otherwise it
        /// runs along z and My does.
        /// </summary>
        public bool AlongY;

        /// <summary>
        /// The share of the part on the positive side of the plastic neutral axis, under bending alone:
        /// a half for a web on the axis of a symmetric section.
        /// </summary>
        public double Alpha0;

        /// <summary>
        /// The combined thickness of every wall the plastic neutral axis crosses alongside this one -
        /// tw for an I, both side walls for a box - which sets how far an axial force moves the axis.
        /// </summary>
        public double ShiftThickness;

        public double Length => Math.Sqrt((Y2 - Y1) * (Y2 - Y1) + (Z2 - Z1) * (Z2 - Z1));
    }

    /// <summary>
    /// A cross-section as EN 1993-1-1 sees it: its properties, its compression parts, its shear areas,
    /// its buckling curves and the reduced plastic moment it has. Worked out from the section's own
    /// dimensions; all values in the model's units.
    ///
    /// Axes are Alpaca4d's: local y along the depth as drawn, local z across it. For an I that makes
    /// bending about z the major axis - the standard's y-y - and Vy the shear along the web.
    ///
    /// Sharp corners throughout, because that is the shape the analysis used: an ISection has no root
    /// radius and a hollow section no corner radius. Against a rolled catalogue section that makes the
    /// area and plastic modulus a few percent low and a web's c a little long, all on the safe side.
    /// </summary>
    public class SteelSection
    {
        public SteelShape Shape;
        /// <summary>"I 300 x 150", for the report.</summary>
        public string Description;

        public BeamSectionStress Stress;
        public SteelGrade Grade;

        /// <summary>fy for the thickest plate, Table 3.1 [N/mm²].</summary>
        public double FyMPa;
        /// <summary>fy in the model's stress unit.</summary>
        public double Fy;
        public double Epsilon;
        /// <summary>The thickest plate is past the 80 mm Table 3.1 stops at.</summary>
        public bool FyBeyondTable;
        public double ThicknessMm;

        public double E;
        public double A, Iy, Iz;
        public double WelY, WelZ, WplY, WplZ;

        /// <summary>Shear area for Vy and for Vz. 6.2.6(3).</summary>
        public double AvY, AvZ;

        /// <summary>
        /// The share of WplZ that the shear area of Vy carries, which 6.2.8 takes away as Vy nears its
        /// resistance - the web of an I. WvY the same for Vz and WplY. Where the shear area cannot be
        /// told apart, the whole modulus, which is the safe side.
        /// </summary>
        public double WvY, WvZ;

        /// <summary>Buckling curve for buckling about local y, and about local z.</summary>
        public BucklingCurve CurveY, CurveZ;

        public List<SteelPart> Parts = new List<SteelPart>();

        public PlasticInteraction Interaction;
        /// <summary>The a of 6.2.9.1(5) for Mz and for My: (A − 2b tf)/A for an I, aw and af for a box.</summary>
        public double InteractionAZ, InteractionAY;

        /// <summary>An I or H, for the torsion reduction of 6.2.7(9).</summary>
        public bool IOrH;

        /// <summary>Things about the section that the check does not cover, for the report.</summary>
        public List<string> Notes = new List<string>();

        /// <summary>
        /// The section as EN 1993-1-1 sees it, or null when it has no shape to check - an Elastic Section,
        /// or one this does not know.
        /// </summary>
        public static SteelSection Of(IUniaxialSection section, SteelGrade grade, Fabrication fabrication)
        {
            if (section == null || grade == null)
                return null;

            var stress = BeamSectionStress.Of(section);
            if (!stress.HasShape)
                return null;

            var steel = new SteelSection
            {
                Stress = stress,
                Grade = grade,
                E = section.Material?.E ?? 0.0,
                A = stress.Area,
                Iy = stress.Iy,
                Iz = stress.Iz,
                WelY = stress.WelY,
                WelZ = stress.WelZ,
                WplY = stress.WplY,
                WplZ = stress.WplZ,
            };

            switch (section)
            {
                case Alpaca4d.Section.ISection i:
                    steel.I(i.Height, i.TopWidth, i.TopFlangeThickness, i.BottomWidth, i.BottomFlangeThickness, i.Web, fabrication);
                    break;
                case RectangleHollowCS box:
                    steel.Box(box.Width, box.Height, box.Web, box.TopFlange, box.BottomFlange, fabrication);
                    break;
                case CircleCS circle:
                    steel.Round(circle.Diameter, circle.Thickness, fabrication);
                    break;
                case RectangleCS rectangle:
                    steel.Rectangle(rectangle.Width, rectangle.Height);
                    break;
                case DoubleLAngleCS angles:
                    steel.DoubleAngle(angles.Height, angles.Width, angles.Thickness, angles.Gap);
                    break;
                default:
                    return null;
            }

            return steel;
        }

        /// <summary>
        /// fy from Table 3.1 for the thickest plate, and ε from it. Each shape calls this once it knows
        /// its thickness and before anything that depends on fy - a buckling curve, a slenderness limit.
        /// </summary>
        private void Strength(double thickness)
        {
            this.ThicknessMm = ModelLength.ToMillimetres(thickness);
            this.FyMPa = this.Grade.YieldStrengthMPa(this.ThicknessMm, out this.FyBeyondTable);
            this.Fy = ModelStress.FromMPa(this.FyMPa);
            this.Epsilon = Ec3.Epsilon(this.FyMPa);
        }

        #region Shapes

        private void I(double h, double bTop, double tfTop, double bBottom, double tfBottom, double tw, Fabrication fabrication)
        {
            var stress = this.Stress;
            double cy = stress.CentroidY;
            double hw = h - tfTop - tfBottom;
            double webTop = h / 2 - tfTop - cy;
            double webBottom = -h / 2 + tfBottom - cy;
            double pna = stress.PlasticNeutralY;

            bool symmetric = Math.Abs(bTop - bBottom) <= 1e-9 * h && Math.Abs(tfTop - tfBottom) <= 1e-9 * h;

            // An I with unequal flanges is welded whatever it is called; nobody rolls one.
            bool rolled = fabrication == Fabrication.Rolled && symmetric;

            this.Shape = SteelShape.I;
            this.Description = symmetric
                ? $"{(rolled ? "rolled" : "welded")} I {Number(h)} x {Number(bTop)}, flanges {Number(tfTop)}, web {Number(tw)}"
                : $"welded I {Number(h)} deep, flanges {Number(bTop)} x {Number(tfTop)} and {Number(bBottom)} x {Number(tfBottom)}, web {Number(tw)}";
            this.IOrH = true;
            this.Strength(Math.Max(tw, Math.Max(tfTop, tfBottom)));

            this.Parts.Add(new SteelPart
            {
                Name = "web",
                Kind = PartKind.Internal,
                C = hw,
                T = tw,
                Y1 = webTop, Z1 = 0.0, Y2 = webBottom, Z2 = 0.0,
                AlongY = true,
                Alpha0 = Share(webBottom, webTop, pna),
                ShiftThickness = tw,
            });

            foreach (double side in new[] { 1.0, -1.0 })
            {
                this.Parts.Add(Outstand("top flange", (bTop - tw) / 2, tfTop,
                                         h / 2 - tfTop / 2 - cy, side * tw / 2, h / 2 - tfTop / 2 - cy, side * bTop / 2));
                this.Parts.Add(Outstand("bottom flange", (bBottom - tw) / 2, tfBottom,
                                         -h / 2 + tfBottom / 2 - cy, side * tw / 2, -h / 2 + tfBottom / 2 - cy, side * bBottom / 2));
            }

            // 6.2.6(3): a) rolled, load parallel to the web, A − 2b tf + (tw + 2r) tf with no root radius,
            // but not less than η hw tw; e) load parallel to the flanges, A − hw tw.
            double rolledWeb = stress.Area - bTop * tfTop - bBottom * tfBottom + tw * (tfTop + tfBottom) / 2;
            this.AvY = rolled ? Math.Max(rolledWeb, hw * tw) : hw * tw;
            this.AvZ = stress.Area - hw * tw;

            this.WvZ = tw * ((webTop - pna) * (webTop - pna) + (pna - webBottom) * (pna - webBottom)) / 2;
            this.WvY = tfTop * bTop * bTop / 4 + tfBottom * bBottom * bBottom / 4;

            if (symmetric)
            {
                this.Interaction = PlasticInteraction.DoublySymmetricI;
                this.InteractionAZ = (stress.Area - 2 * bTop * tfTop) / stress.Area;
                this.InteractionAY = this.InteractionAZ;
            }
            else
            {
                this.Interaction = PlasticInteraction.Linear;
            }

            // Buckling about z is about the axis parallel to the flanges: the major axis, Table 6.2's y-y.
            double tfMm = ModelLength.ToMillimetres(Math.Max(tfTop, tfBottom));
            double hOverB = h / Math.Max(bTop, bBottom);
            this.CurveZ = rolled ? Ec3.CurveRolledI(hOverB, tfMm, true, this.FyMPa) : Ec3.CurveWeldedI(tfMm, true);
            this.CurveY = rolled ? Ec3.CurveRolledI(hOverB, tfMm, false, this.FyMPa) : Ec3.CurveWeldedI(tfMm, false);

            if (hw / tw > 72.0 * this.Epsilon)
                this.Notes.Add($"web hw/tw = {hw / tw:0.0} > 72ε = {72.0 * this.Epsilon:0.0}: shear buckling to EN 1993-1-5 is not checked");
            if (!symmetric)
                this.Notes.Add("unequal flanges: no reduced plastic moment in 6.2.9.1, so N + M is the linear sum of 6.2.1(7), and the buckling curves are those of a welded I");
        }

        private void Box(double w, double h, double tw, double tTop, double tBottom, Fabrication fabrication)
        {
            var stress = this.Stress;
            double cy = stress.CentroidY;
            double wallTop = h / 2 - tTop - cy;
            double wallBottom = -h / 2 + tBottom - cy;
            double pnaY = stress.PlasticNeutralY;
            double pnaZ = stress.PlasticNeutralZ;
            double inner = w / 2 - tw;

            bool uniform = Math.Abs(tw - tTop) <= 1e-9 * h && Math.Abs(tw - tBottom) <= 1e-9 * h;

            this.Shape = SteelShape.Box;
            this.Description = (fabrication == Fabrication.Rolled ? "hot-finished " : "cold-formed ") + (uniform
                ? $"RHS {Number(h)} x {Number(w)} x {Number(tw)}"
                : $"RHS {Number(h)} x {Number(w)}, walls {Number(tw)}, top {Number(tTop)}, bottom {Number(tBottom)}");
            this.Strength(Math.Max(tw, Math.Max(tTop, tBottom)));

            // The flat width of a hot-finished wall is about h − 3t once its corners are rounded. The
            // section here has sharp corners, but the tube it stands for does not, and this is how the
            // section tables classify them.
            foreach (double side in new[] { 1.0, -1.0 })
            {
                this.Parts.Add(new SteelPart
                {
                    Name = "side wall",
                    Kind = PartKind.Internal,
                    C = h - tTop - tBottom - tw,
                    T = tw,
                    Y1 = wallTop, Z1 = side * (w / 2 - tw / 2), Y2 = wallBottom, Z2 = side * (w / 2 - tw / 2),
                    AlongY = true,
                    Alpha0 = Share(wallBottom, wallTop, pnaY),
                    ShiftThickness = 2 * tw,
                });
            }

            this.Parts.Add(new SteelPart
            {
                Name = "top wall",
                Kind = PartKind.Internal,
                C = w - 2 * tw - tTop,
                T = tTop,
                Y1 = h / 2 - tTop / 2 - cy, Z1 = -inner, Y2 = h / 2 - tTop / 2 - cy, Z2 = inner,
                AlongY = false,
                Alpha0 = Share(-inner, inner, pnaZ),
                ShiftThickness = tTop + tBottom,
            });
            this.Parts.Add(new SteelPart
            {
                Name = "bottom wall",
                Kind = PartKind.Internal,
                C = w - 2 * tw - tBottom,
                T = tBottom,
                Y1 = -h / 2 + tBottom / 2 - cy, Z1 = -inner, Y2 = -h / 2 + tBottom / 2 - cy, Z2 = inner,
                AlongY = false,
                Alpha0 = Share(-inner, inner, pnaZ),
                ShiftThickness = tTop + tBottom,
            });

            // 6.2.6(3) f): A h / (b + h) parallel to the depth, A b / (b + h) parallel to the width.
            this.AvY = stress.Area * h / (w + h);
            this.AvZ = stress.Area * w / (w + h);

            this.WvZ = 2 * tw * ((wallTop - pnaY) * (wallTop - pnaY) + (pnaY - wallBottom) * (pnaY - wallBottom)) / 2;
            this.WvY = (tTop + tBottom) * (2 * inner) * (2 * inner) / 4;

            if (uniform)
            {
                this.Interaction = PlasticInteraction.UniformBox;
                this.InteractionAZ = (stress.Area - 2 * w * tw) / stress.Area;
                this.InteractionAY = (stress.Area - 2 * h * tw) / stress.Area;
            }
            else
            {
                this.Interaction = PlasticInteraction.Linear;
            }

            this.CurveY = Ec3.CurveHollow(fabrication == Fabrication.Rolled, this.FyMPa);
            this.CurveZ = this.CurveY;

            double sideDepth = h - tTop - tBottom;
            if (sideDepth / tw > 72.0 * this.Epsilon || 2 * inner / Math.Min(tTop, tBottom) > 72.0 * this.Epsilon)
                this.Notes.Add($"slender walls (hw/t > 72ε = {72.0 * this.Epsilon:0.0}): shear buckling to EN 1993-1-5 is not checked");
            if (!uniform)
                this.Notes.Add("walls of different thickness: N + M is the linear sum of 6.2.1(7)");
        }

        private void Round(double d, double t, Fabrication fabrication)
        {
            var stress = this.Stress;
            bool solid = t <= 0.0 || t >= d / 2;

            this.Shape = solid ? SteelShape.SolidRound : SteelShape.Tube;
            this.Description = solid
                ? $"round bar {Number(d)}"
                : $"{(fabrication == Fabrication.Rolled ? "hot-finished" : "cold-formed")} CHS {Number(d)} x {Number(t)}";
            this.Interaction = PlasticInteraction.Round;
            this.Strength(solid ? d : t);

            if (!solid)
                this.Parts.Add(new SteelPart { Name = "tube", Kind = PartKind.Tube, C = d, T = t });

            // 6.2.6(3) g): 2A/π for a tube. A solid bar has no wall to name; its whole area yields in
            // shear, which is the plastic resistance A fy/√3.
            this.AvY = solid ? stress.Area : 2 * stress.Area / Math.PI;
            this.AvZ = this.AvY;
            this.WvY = stress.WplY;
            this.WvZ = stress.WplZ;

            this.CurveY = solid ? BucklingCurve.c : Ec3.CurveHollow(fabrication == Fabrication.Rolled, this.FyMPa);
            this.CurveZ = this.CurveY;

            if (solid)
                this.Notes.Add("solid bar: M + N uses the tube's M_pl (1 − n^1.7), which is below the bar's own and so safe");
        }

        private void Rectangle(double w, double h)
        {
            var stress = this.Stress;

            this.Shape = SteelShape.SolidRectangle;
            this.Description = $"flat bar {Number(h)} x {Number(w)}";
            this.Interaction = PlasticInteraction.SolidRectangle;
            this.Strength(Math.Min(w, h));

            // A solid section has no wall to name; its whole area yields in shear.
            this.AvY = stress.Area;
            this.AvZ = stress.Area;
            this.WvY = stress.WplY;
            this.WvZ = stress.WplZ;
            this.CurveY = BucklingCurve.c;
            this.CurveZ = BucklingCurve.c;
        }

        private void DoubleAngle(double h, double w, double t, double gap)
        {
            var stress = this.Stress;
            double cy = stress.CentroidY;
            double inner = gap / 2;

            this.Shape = SteelShape.DoubleAngle;
            this.Description = $"2L {Number(h)} x {Number(w)} x {Number(t)}, gap {Number(gap)}";
            this.Interaction = PlasticInteraction.Linear;
            this.Strength(t);

            // The legs parallel to the force carry it.
            this.AvY = 2 * h * t;
            this.AvZ = 2 * w * t;
            this.WvY = stress.WplY;
            this.WvZ = stress.WplZ;

            foreach (double side in new[] { 1.0, -1.0 })
            {
                double legZ = side * (inner + t / 2);
                this.Parts.Add(Outstand("long leg", h - t, t, -h / 2 + t - cy, legZ, h / 2 - cy, legZ));

                double shortY = -h / 2 + t / 2 - cy;
                this.Parts.Add(Outstand("short leg", w - t, t, shortY, side * (inner + t), shortY, side * (inner + w)));
            }

            this.Parts.Add(new SteelPart { Name = "angle", Kind = PartKind.Angle, C = h, B = w, T = t });

            this.CurveY = BucklingCurve.b;
            this.CurveZ = BucklingCurve.b;
            this.Notes.Add("pair of angles: torsional-flexural buckling (6.3.1.4), which can govern in compression, is not checked");
        }

        private static SteelPart Outstand(string name, double c, double t, double rootY, double rootZ, double tipY, double tipZ)
        {
            return new SteelPart
            {
                Name = name,
                Kind = PartKind.Outstand,
                C = c,
                T = t,
                Y1 = rootY, Z1 = rootZ, Y2 = tipY, Z2 = tipZ,
            };
        }

        /// <summary>The share of [from, to] that lies above <paramref name="cut"/>.</summary>
        private static double Share(double from, double to, double cut)
        {
            double low = Math.Min(from, to), high = Math.Max(from, to);
            if (high <= low) return 0.5;
            return Math.Max(0.0, Math.Min(1.0, (high - cut) / (high - low)));
        }

        /// <summary>A dimension in millimetres for the report, whatever the model's length unit.</summary>
        private static string Number(double length) => ModelLength.ToMillimetres(length).ToString("0.#");

        #endregion
    }
}
