using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;
using Alpaca4d.Section;

namespace Alpaca4d.Result
{
    /// <summary>
    /// The stresses at one integration section of one beam, recovered from the six section forces
    /// by elastic beam theory. Nothing here depends on the material: it is what a linear elastic
    /// section of that shape carries under those forces, and a material check is a separate step.
    ///
    /// Every value is the worst the section sees, not the value at one point. The direct stresses
    /// are checked at every corner of the shape, which is where a linear field over a polygon has
    /// its extremes; the shears are the peaks of their own distributions, which sit elsewhere.
    /// </summary>
    public struct BeamStress
    {
        /// <summary>N / A, positive in tension.</summary>
        public double SigmaN;
        /// <summary>The largest bending stress My alone causes, at the fibre furthest along local z.</summary>
        public double SigmaMy;
        /// <summary>The largest bending stress Mz alone causes, at the fibre furthest along local y.</summary>
        public double SigmaMz;
        /// <summary>The largest direct stress anywhere on the section, N, My and Mz together.</summary>
        public double SigmaMax;
        /// <summary>The smallest - most compressive - direct stress anywhere on the section.</summary>
        public double SigmaMin;
        /// <summary>Peak shear stress from Vy and Vz.</summary>
        public double TauV;
        /// <summary>Peak shear stress from torsion.</summary>
        public double TauT;
        /// <summary>
        /// Whether the section had a shape to recover stresses on. An Elastic Section is a list of
        /// properties with no geometry behind them, so it can give N / A and nothing else.
        /// </summary>
        public bool HasShape;

        /// <summary>
        /// The two shears added. They peak at different points for most shapes, so this is an upper
        /// bound - the exact sum where they coincide, as on a rectangle at the middle of its long side.
        /// </summary>
        public double Tau => TauV + TauT;

        /// <summary>
        /// Von Mises equivalent stress, the worst direct stress with the worst shear: sqrt(σ² + 3τ²).
        ///
        /// Those two rarely meet at the same fibre - bending peaks at the corners, where a free
        /// surface carries no shear, and shear peaks on the neutral axis, where bending is zero - so
        /// this is an upper bound rather than the stress at a point. Conservative on short deep
        /// members, and close to exact on slender ones, where the shear is small anyway.
        /// </summary>
        public double VonMises
        {
            get
            {
                double sigma = Math.Max(Math.Abs(SigmaMax), Math.Abs(SigmaMin));
                double tau = Tau;
                return Math.Sqrt(sigma * sigma + 3.0 * tau * tau);
            }
        }
    }

    /// <summary>
    /// What a cross-section needs to turn section forces into stresses: its area and second moments,
    /// where its corners are, and how much peak shear a unit shear force or torque puts into it.
    ///
    /// Worked out from the section's own dimensions rather than from its Brep, for two reasons. The
    /// stresses want the shape the dimensions describe, and the Brep of a double angle carries a
    /// sliver joining the two angles that is there for drawing. And dimensions are plain numbers,
    /// so this can be checked outside Rhino, where a Brep cannot be built.
    ///
    /// Axes follow the drawing: Model View lays a section's curves on a plane whose X is the beam's
    /// local z and whose Y is its local y, so a section drawn tall is tall in local y - which is
    /// also how Izz = b·h³/12 reads. Coordinates here are measured from the centroid, which for an
    /// I with unequal flanges or a pair of angles is not where the curves are centred.
    /// </summary>
    public class BeamSectionStress
    {
        /// <summary>A rectangle of material, local y from Y1 to Y2 and local z from Z1 to Z2.</summary>
        private struct Box
        {
            public double Y1, Y2, Z1, Z2;

            /// <summary>
            /// Not material, only a tie: it joins the rectangles either side of it into one body and
            /// adds nothing to area, stiffness or the width of a cut. See <see cref="DoubleAngle"/>.
            /// </summary>
            public bool Link;

            public Box(double y1, double y2, double z1, double z2, bool link = false)
            {
                Y1 = Math.Min(y1, y2); Y2 = Math.Max(y1, y2);
                Z1 = Math.Min(z1, z2); Z2 = Math.Max(z1, z2);
                Link = link;
            }

            public double Area => (Y2 - Y1) * (Z2 - Z1);
        }

        public double Area { get; private set; }
        /// <summary>Second moment about local y, through the centroid - the one My bends about.</summary>
        public double Iy { get; private set; }
        /// <summary>Second moment about local z, through the centroid - the one Mz bends about.</summary>
        public double Iz { get; private set; }
        /// <summary>See <see cref="BeamStress.HasShape"/>.</summary>
        public bool HasShape { get; private set; }

        /// <summary>
        /// Where the centroid is, in local y, measured in the coordinates the section's curves are
        /// drawn in. Zero for a doubly symmetric shape; not for a pair of angles.
        /// </summary>
        public double CentroidY { get; private set; }
        /// <summary>As <see cref="CentroidY"/>, in local z.</summary>
        public double CentroidZ { get; private set; }

        /// <summary>Peak shear stress per unit Vy.</summary>
        public double ShearY { get; private set; }
        /// <summary>Peak shear stress per unit Vz.</summary>
        public double ShearZ { get; private set; }
        /// <summary>Peak shear stress per unit torque.</summary>
        public double Torsion { get; private set; }

        /// <summary>Elastic section modulus for My: Iy over the distance to the furthest fibre in z.</summary>
        public double WelY { get; private set; }
        /// <summary>Elastic section modulus for Mz: Iz over the distance to the furthest fibre in y.</summary>
        public double WelZ { get; private set; }
        /// <summary>Plastic section modulus for My, about the axis that halves the area.</summary>
        public double WplY { get; private set; }
        /// <summary>Plastic section modulus for Mz, about the axis that halves the area.</summary>
        public double WplZ { get; private set; }

        /// <summary>
        /// Where the plastic neutral axis of Mz sits, as a centroidal y: the line with half the area
        /// either side. On the centroid for a doubly symmetric shape, off it for an I with unequal
        /// flanges or a pair of angles.
        /// </summary>
        public double PlasticNeutralY { get; private set; }
        /// <summary>As <see cref="PlasticNeutralY"/>, for My, as a centroidal z.</summary>
        public double PlasticNeutralZ { get; private set; }

        /// <summary>The corners of the shape, centroidal (y, z). Empty for a round section.</summary>
        private readonly List<(double Y, double Z)> _corners = new List<(double Y, double Z)>();

        /// <summary>Outer radius of a round section, zero for every other shape.</summary>
        private double _radius;

        private BeamSectionStress() { }

        /// <summary>
        /// The stress properties of a section. Never null: a section with no shape - an Elastic
        /// Section, or one this does not know - comes back with <see cref="HasShape"/> false and only
        /// its area set.
        /// </summary>
        public static BeamSectionStress Of(IUniaxialSection section)
        {
            switch (section)
            {
                case RectangleCS rectangle:
                    return Rectangle(rectangle.Width, rectangle.Height);
                case CircleCS circle:
                    return Circle(circle.Diameter, circle.Thickness);
                case RectangleHollowCS hollow:
                    return Hollow(hollow.Width, hollow.Height, hollow.Web, hollow.TopFlange, hollow.BottomFlange);
                case Alpaca4d.Section.ISection i:
                    return I(i.Height, i.TopWidth, i.TopFlangeThickness, i.BottomWidth, i.BottomFlangeThickness, i.Web);
                case DoubleLAngleCS angles:
                    return DoubleAngle(angles.Height, angles.Width, angles.Thickness, angles.Gap);
                default:
                    return new BeamSectionStress { Area = section?.Area ?? 0.0, HasShape = false };
            }
        }

        /// <summary>
        /// The stresses under one set of section forces, given in the order Read.ForceBeamColumn
        /// names them: N, Vy, Vz, T, My, Mz.
        /// </summary>
        public BeamStress At(double n, double vy, double vz, double t, double my, double mz)
        {
            var stress = new BeamStress
            {
                SigmaN = this.Area > 0.0 ? n / this.Area : 0.0,
                HasShape = this.HasShape
            };

            if (!this.HasShape)
            {
                stress.SigmaMax = stress.SigmaN;
                stress.SigmaMin = stress.SigmaN;
                return stress;
            }

            if (_radius > 0.0)
            {
                // A circle has no corners, and the largest of a linear field over it is the radius
                // times the field's gradient - exactly, with no points to sample.
                double gy = my / this.Iy;
                double gz = mz / this.Iz;
                double bending = _radius * Math.Sqrt(gy * gy + gz * gz);

                stress.SigmaMy = Math.Abs(my) * _radius / this.Iy;
                stress.SigmaMz = Math.Abs(mz) * _radius / this.Iz;
                stress.SigmaMax = stress.SigmaN + bending;
                stress.SigmaMin = stress.SigmaN - bending;
            }
            else
            {
                double maxY = _corners.Max(c => Math.Abs(c.Y));
                double maxZ = _corners.Max(c => Math.Abs(c.Z));

                stress.SigmaMy = Math.Abs(my) * maxZ / this.Iy;
                stress.SigmaMz = Math.Abs(mz) * maxY / this.Iz;

                // A linear field over a polygon peaks at a corner of its outline, and those are
                // among the corners of the rectangles, so checking them all is exact.
                stress.SigmaMax = double.MinValue;
                stress.SigmaMin = double.MaxValue;
                foreach (var corner in _corners)
                {
                    double sigma = NormalStressAt(n, my, mz, corner.Y, corner.Z);
                    stress.SigmaMax = Math.Max(stress.SigmaMax, sigma);
                    stress.SigmaMin = Math.Min(stress.SigmaMin, sigma);
                }
            }

            // Vy and Vz each peak where their own neutral axis cuts the thinnest wall. Where both
            // peak at one point - the middle of a rectangle, the side of a tube - they are at right
            // angles to each other there, so they add as a vector.
            double tauY = this.ShearY * Math.Abs(vy);
            double tauZ = this.ShearZ * Math.Abs(vz);
            stress.TauV = Math.Sqrt(tauY * tauY + tauZ * tauZ);
            stress.TauT = this.Torsion * Math.Abs(t);

            return stress;
        }

        /// <summary>
        /// The direct stress at one point of the section, (y, z) measured from the centroid in the
        /// beam's local axes. OpenSees' sign convention, the one FiberSection3d strains its fibres
        /// by: σ = N/A − Mz·y/Iz + My·z/Iy, so a positive Mz compresses the +y side.
        /// </summary>
        public double NormalStressAt(double n, double my, double mz, double y, double z)
        {
            double sigma = this.Area > 0.0 ? n / this.Area : 0.0;
            if (this.Iz > 0.0) sigma -= mz * y / this.Iz;
            if (this.Iy > 0.0) sigma += my * z / this.Iy;
            return sigma;
        }

        #region Shapes

        private static BeamSectionStress Rectangle(double width, double height)
        {
            var section = FromBoxes(new List<Box> { new Box(-height / 2, height / 2, -width / 2, width / 2) });

            // Saint-Venant, from Roark (Table 10.7, case 4) written for full sides: the peak is at the
            // middle of the long side, 3T/(a·b²) times a correction that is 1 for a thin strip and
            // 1.604 for a square - which is T/(0.208·a³), the textbook value.
            double a = Math.Max(width, height);
            double b = Math.Min(width, height);
            double r = a > 0.0 ? b / a : 0.0;
            double correction = 1.0 + 0.6095 * r + 0.8865 * r * r - 1.8023 * r * r * r + 0.9100 * r * r * r * r;
            section.Torsion = a > 0.0 && b > 0.0 ? 3.0 / (a * b * b) * correction : 0.0;

            return section;
        }

        /// <summary>
        /// A solid round bar or a tube. CircleCS reads a thickness of zero, or of half the diameter,
        /// as solid, and so does this.
        /// </summary>
        private static BeamSectionStress Circle(double diameter, double thickness)
        {
            double outer = diameter / 2.0;
            double inner = thickness > 0.0 && thickness < outer ? outer - thickness : 0.0;

            if (outer <= 0.0)
                return new BeamSectionStress { HasShape = false };

            double r4 = Math.Pow(outer, 4) - Math.Pow(inner, 4);
            double inertia = Math.PI * r4 / 4.0;

            var section = new BeamSectionStress
            {
                HasShape = true,
                Area = Math.PI * (outer * outer - inner * inner),
                Iy = inertia,
                Iz = inertia,
                _radius = outer
            };

            section.WelY = inertia / outer;
            section.WelZ = inertia / outer;
            section.WplY = 4.0 / 3.0 * (Math.Pow(outer, 3) - Math.Pow(inner, 3));
            section.WplZ = section.WplY;

            // Jourawski across a diameter: S = (2/3)(R³ − r³), cut width 2(R − r). That is 4V/3A for a
            // solid bar and tends to 2V/A for a thin tube, the two textbook values, from one formula.
            double s = 2.0 / 3.0 * (Math.Pow(outer, 3) - Math.Pow(inner, 3));
            double cut = 2.0 * (outer - inner);
            double shear = inertia > 0.0 && cut > 0.0 ? s / (inertia * cut) : 0.0;
            section.ShearY = shear;
            section.ShearZ = shear;

            // The polar moment is the exact torsion constant of a circle, solid or hollow, and the
            // peak is on the outside.
            double polar = Math.PI * r4 / 2.0;
            section.Torsion = polar > 0.0 ? outer / polar : 0.0;

            return section;
        }

        /// <summary>
        /// A rectangular hollow section. Width runs along local z and Height along y, as drawn; the
        /// two side walls are Web thick and the top and bottom ones TopFlange and BottomFlange.
        /// </summary>
        private static BeamSectionStress Hollow(double width, double height, double web, double topFlange, double bottomFlange)
        {
            double top = height / 2;
            double bottom = -height / 2;
            double side = width / 2;

            var section = FromBoxes(new List<Box>
            {
                new Box(top - topFlange, top, -side, side),
                new Box(bottom, bottom + bottomFlange, -side, side),
                new Box(bottom + bottomFlange, top - topFlange, -side, -side + web),
                new Box(bottom + bottomFlange, top - topFlange, side - web, side),
            });

            // Bredt: a closed cell carries torque as a constant shear flow T / 2Am round its wall,
            // Am the area inside the wall's centre line, and the stress peaks where the wall is thinnest.
            double enclosed = (width - web) * (height - (topFlange + bottomFlange) / 2.0);
            double thinnest = Math.Min(web, Math.Min(topFlange, bottomFlange));
            section.Torsion = enclosed > 0.0 && thinnest > 0.0 ? 1.0 / (2.0 * enclosed * thinnest) : 0.0;

            return section;
        }

        /// <summary>An I, flanges along local z and the web along y; the flanges may differ.</summary>
        private static BeamSectionStress I(double height, double topWidth, double topFlange,
                                           double bottomWidth, double bottomFlange, double web)
        {
            double top = height / 2;
            double bottom = -height / 2;

            var section = FromBoxes(new List<Box>
            {
                new Box(top - topFlange, top, -topWidth / 2, topWidth / 2),
                new Box(bottom + bottomFlange, top - topFlange, -web / 2, web / 2),
                new Box(bottom, bottom + bottomFlange, -bottomWidth / 2, bottomWidth / 2),
            });

            section.Torsion = OpenTorsion(section);
            return section;
        }

        /// <summary>
        /// Two angles back to back, as DoubleLAngleCS draws them: the long legs upright either side of
        /// the gap, the short legs along the bottom pointing outwards. The sliver the Brep adds to join
        /// them is left out - it is not material.
        ///
        /// What it stands for is kept, though. The pair is analysed as one member, which only works
        /// because battens or packing plates in the gap make the two angles bend together, and those
        /// carry shear from one angle to the other. Without them each angle would be a body of its
        /// own whose shear could only leave through its own legs, and a cut near the tip of a short
        /// leg would read the whole angle's shear going through it. So the gap is bridged by a link
        /// with no area: it ties the two angles into one body and takes no stress of its own - what a
        /// batten carries depends on how far apart the battens are, which the section does not know.
        /// </summary>
        private static BeamSectionStress DoubleAngle(double height, double width, double thickness, double gap)
        {
            double top = height / 2;
            double bottom = -height / 2;
            double inner = gap / 2;

            var boxes = new List<Box>();
            foreach (double side in new[] { 1.0, -1.0 })
            {
                boxes.Add(new Box(bottom, top, side * inner, side * (inner + thickness)));
                boxes.Add(new Box(bottom, bottom + thickness, side * (inner + thickness), side * (inner + width)));
            }

            // With no gap the long legs touch and are one body already.
            if (gap > 0.0)
                boxes.Add(new Box(bottom, top, -inner, inner, link: true));

            var section = FromBoxes(boxes);
            section.Torsion = OpenTorsion(section);
            return section;
        }

        #endregion

        #region Working it out from rectangles

        /// <summary>The rectangles of material the shape was built from, in centroidal coordinates.</summary>
        private List<Box> _boxes;

        /// <summary>
        /// Area, centroid, second moments, corners and peak shears of a shape made of rectangles,
        /// which every polygonal section Alpaca4d has is. Torsion is left to the caller, because it
        /// depends on whether the shape is open or closed and the rectangles cannot say which.
        /// </summary>
        private static BeamSectionStress FromBoxes(List<Box> boxes)
        {
            var material = boxes.Where(b => !b.Link && b.Area > 0.0).ToList();

            double area = material.Sum(b => b.Area);
            if (area <= 0.0)
                return new BeamSectionStress { HasShape = false };

            double yc = material.Sum(b => b.Area * (b.Y1 + b.Y2) / 2) / area;
            double zc = material.Sum(b => b.Area * (b.Z1 + b.Z2) / 2) / area;

            Box Centre(Box b) => new Box(b.Y1 - yc, b.Y2 - yc, b.Z1 - zc, b.Z2 - zc, b.Link);
            var centred = material.Select(Centre).ToList();

            double iz = 0.0, iy = 0.0;
            foreach (var b in centred)
            {
                double h = b.Y2 - b.Y1;
                double w = b.Z2 - b.Z1;
                double y = (b.Y1 + b.Y2) / 2;
                double z = (b.Z1 + b.Z2) / 2;
                iz += w * h * h * h / 12.0 + b.Area * y * y;
                iy += h * w * w * w / 12.0 + b.Area * z * z;
            }

            var section = new BeamSectionStress
            {
                HasShape = true,
                Area = area,
                Iy = iy,
                Iz = iz,
                CentroidY = yc,
                CentroidZ = zc,
                _boxes = centred
            };

            foreach (var b in centred)
            {
                section._corners.Add((b.Y1, b.Z1));
                section._corners.Add((b.Y1, b.Z2));
                section._corners.Add((b.Y2, b.Z1));
                section._corners.Add((b.Y2, b.Z2));
            }

            // The links go to the shear search too: they decide which rectangles are one body.
            var joined = centred.Concat(boxes.Where(b => b.Link).Select(Centre)).ToList();

            // Vy is resisted across cuts at constant y, Vz across cuts at constant z. The same search
            // does both with the axes swapped.
            section.ShearY = PeakShear(joined, iz, b => new Strip(b.Y1, b.Y2, b.Z1, b.Z2, b.Link));
            section.ShearZ = PeakShear(joined, iy, b => new Strip(b.Z1, b.Z2, b.Y1, b.Y2, b.Link));

            section.WelZ = iz / section._corners.Max(c => Math.Abs(c.Y));
            section.WelY = iy / section._corners.Max(c => Math.Abs(c.Z));

            section.PlasticNeutralY = HalfArea(centred, b => new Strip(b.Y1, b.Y2, b.Z1, b.Z2, false));
            section.PlasticNeutralZ = HalfArea(centred, b => new Strip(b.Z1, b.Z2, b.Y1, b.Y2, false));
            section.WplZ = FirstMoment(centred, section.PlasticNeutralY, b => new Strip(b.Y1, b.Y2, b.Z1, b.Z2, false));
            section.WplY = FirstMoment(centred, section.PlasticNeutralZ, b => new Strip(b.Z1, b.Z2, b.Y1, b.Y2, false));

            return section;
        }

        /// <summary>
        /// A rectangle seen from a cut: U across the cut, V along it. Whether the cut goes through it
        /// is only filled in once the rectangle has been clipped to one side of a cut.
        /// </summary>
        private struct Strip
        {
            public double U1, U2, V1, V2;
            public bool Link, Crosses;

            public Strip(double u1, double u2, double v1, double v2, bool link, bool crosses = false)
            {
                U1 = u1; U2 = u2; V1 = v1; V2 = v2;
                Link = link; Crosses = crosses;
            }

            /// <summary>Area, which a link has none of.</summary>
            public double Area => this.Link ? 0.0 : (U2 - U1) * (V2 - V1);
        }

        /// <summary>
        /// Peak shear per unit shear force, by Jourawski: across a straight cut, τ = V·S / (I·b), S the
        /// first moment about the neutral axis of what lies beyond the cut and b the width of material
        /// the cut goes through.
        ///
        /// Done piece by piece rather than once across the whole cut, and that is what makes it right
        /// for thin walls. A cut just outside the web of an I goes through both flanges, and beyond it
        /// lie two separate outstands; each one's shear has nowhere to go but back across its own
        /// flange, so each is worked out with its own S and its own thickness. Lumping them would
        /// average a deep flange with a shallow one. Where what lies beyond is one connected piece
        /// crossing several walls - half a box section - the stress is shared, and the average across
        /// those walls is what is left. Both sides of every cut are tried, since either can be the side
        /// that sees a wall alone.
        ///
        /// The peak sits on the neutral axis, or at a step in width, so those are the only cuts tried:
        /// within a run of constant width, S/b is largest where S is, which is at the neutral axis or
        /// at whichever end of the run is nearest it.
        /// </summary>
        /// <param name="axes">How a rectangle reads from the cut: (y, z) for Vy, (z, y) for Vz.</param>
        private static double PeakShear(List<Box> boxes, double inertia, Func<Box, Strip> axes)
        {
            if (inertia <= 0.0) return 0.0;

            var strips = boxes.Select(axes).ToList();

            double size = strips.Max(r => Math.Max(Math.Abs(r.U2), Math.Abs(r.U1)));
            double nudge = 1e-9 * size;

            var cuts = new List<double> { 0.0 };
            foreach (var r in strips.Where(r => !r.Link))
            {
                cuts.Add(r.U1 - nudge); cuts.Add(r.U1 + nudge);
                cuts.Add(r.U2 - nudge); cuts.Add(r.U2 + nudge);
            }

            double peak = 0.0;

            foreach (double cut in cuts)
            {
                foreach (bool above in new[] { true, false })
                {
                    // What lies beyond the cut on this side, rectangle by rectangle.
                    var beyond = new List<Strip>();
                    foreach (var r in strips)
                    {
                        double u1 = above ? Math.Max(r.U1, cut) : r.U1;
                        double u2 = above ? r.U2 : Math.Min(r.U2, cut);
                        if (u2 - u1 <= 0.0) continue;

                        beyond.Add(new Strip(u1, u2, r.V1, r.V2, r.Link, r.U1 < cut && cut < r.U2));
                    }

                    foreach (var piece in Pieces(beyond, nudge))
                    {
                        // Only material the cut actually goes through gives it width. A piece that
                        // does not reach the cut is held on by something else, and a cut through a
                        // link alone is a batten's business, not the section's.
                        double width = piece.Where(r => r.Crosses && !r.Link).Sum(r => r.V2 - r.V1);
                        if (width <= 0.0) continue;

                        double s = piece.Sum(r => r.Area * (r.U1 + r.U2) / 2);
                        peak = Math.Max(peak, Math.Abs(s) / (inertia * width));
                    }
                }
            }

            return peak;
        }

        /// <summary>
        /// The rectangles grouped into connected pieces: two belong together when they share an edge
        /// of some length, and a corner touching a corner does not count.
        /// </summary>
        private static List<List<Strip>> Pieces(List<Strip> strips, double touch)
        {
            int count = strips.Count;
            var parent = Enumerable.Range(0, count).ToArray();

            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);

            bool Overlap(double a1, double a2, double b1, double b2) => Math.Min(a2, b2) - Math.Max(a1, b1) > touch;
            bool Meet(double a1, double a2, double b1, double b2) => Math.Abs(a2 - b1) <= touch || Math.Abs(b2 - a1) <= touch;

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    var a = strips[i];
                    var b = strips[j];
                    bool joined = (Overlap(a.U1, a.U2, b.U1, b.U2) && (Meet(a.V1, a.V2, b.V1, b.V2) || Overlap(a.V1, a.V2, b.V1, b.V2)))
                               || (Overlap(a.V1, a.V2, b.V1, b.V2) && Meet(a.U1, a.U2, b.U1, b.U2));
                    if (joined)
                        parent[Find(i)] = Find(j);
                }
            }

            return Enumerable.Range(0, count)
                             .GroupBy(Find)
                             .Select(group => group.Select(i => strips[i]).ToList())
                             .ToList();
        }

        /// <summary>
        /// Peak torsional shear per unit torque of an open thin-walled shape: T·t / J, with t the
        /// thickest wall and J = Σ a·b³/3 over its rectangles, a the long side and b the short.
        ///
        /// J is worked out here rather than taken from the section, because ISection hands the solver
        /// Iy + Iz - the polar moment, which is the torsion constant of a circle and hundreds of times
        /// too stiff for an I. This is the stress under the torque the analysis reports; that the torque
        /// itself came out of an over-stiff member is a matter for the section.
        /// </summary>
        private static double HalfArea(List<Box> boxes, Func<Box, Strip> axes)
        {
            var strips = boxes.Select(axes).ToList();
            double half = strips.Sum(r => r.Area) / 2.0;

            // The area beyond a line only grows as the line moves the other way, so halving the
            // interval homes in on it; a hundred halvings is past double precision.
            double low = strips.Min(r => r.U1), high = strips.Max(r => r.U2);
            for (int i = 0; i < 100; i++)
            {
                double mid = (low + high) / 2.0;
                double beyond = strips.Sum(r => (r.V2 - r.V1) * Math.Max(0.0, r.U2 - Math.Max(r.U1, mid)));
                if (beyond > half) low = mid; else high = mid;
            }

            return (low + high) / 2.0;
        }

        /// <summary>
        /// The plastic modulus about a line: Σ |first moment| of the area either side of it, every
        /// fibre at fy pulling or pushing with its own lever arm.
        /// </summary>
        private static double FirstMoment(List<Box> boxes, double cut, Func<Box, Strip> axes)
        {
            double total = 0.0;
            foreach (var r in boxes.Select(axes))
            {
                double above = Math.Max(0.0, r.U2 - Math.Max(r.U1, cut));
                double below = Math.Max(0.0, Math.Min(r.U2, cut) - r.U1);
                double fromCut = Math.Max(r.U1, cut) - cut;
                double toCut = cut - Math.Min(r.U2, cut);
                total += (r.V2 - r.V1) * (above * (fromCut + above / 2.0) + below * (toCut + below / 2.0));
            }

            return total;
        }

        private static double OpenTorsion(BeamSectionStress section)
        {
            if (section._boxes == null || section._boxes.Count == 0) return 0.0;

            double j = 0.0;
            double thickest = 0.0;
            foreach (var b in section._boxes)
            {
                double h = b.Y2 - b.Y1;
                double w = b.Z2 - b.Z1;
                double longSide = Math.Max(h, w);
                double shortSide = Math.Min(h, w);
                j += longSide * shortSide * shortSide * shortSide / 3.0;
                thickest = Math.Max(thickest, shortSide);
            }

            return j > 0.0 ? thickest / j : 0.0;
        }

        #endregion
    }

    public partial class Read
    {
        /// <summary>
        /// The stresses at every integration section of every beam, one list per beam in the order of
        /// Model.Beams and one entry per section from the I end to the J end - the same layout as
        /// <see cref="ForceBeamColumn"/>, whose forces they are recovered from.
        ///
        /// Every section along a beam is recovered with the beam's own cross-section, including the
        /// end sections of a Beam With Hinges. Those carry a softened copy of it to the solver so that
        /// the released forces come out near zero; the material at the end of the beam is the same.
        /// </summary>
        public static List<List<BeamStress>> BeamStresses(Model alpacaModel, int step)
        {
            var output = new List<List<BeamStress>>();

            if (alpacaModel.Beams.Count == 0)
                return output;

            var (n, mz, vy, my, vz, t) = ForceBeamColumn(alpacaModel, step);

            // Most models share a handful of sections between many beams.
            var properties = new Dictionary<IUniaxialSection, BeamSectionStress>();

            for (int i = 0; i < alpacaModel.Beams.Count; i++)
            {
                var section = alpacaModel.Beams[i].Section;
                if (section == null || !properties.TryGetValue(section, out var stressOf))
                {
                    stressOf = BeamSectionStress.Of(section);
                    if (section != null)
                        properties[section] = stressOf;
                }

                var stresses = new List<BeamStress>();
                for (int k = 0; k < n[i].Count; k++)
                    stresses.Add(stressOf.At(n[i][k], vy[i][k], vz[i][k], t[i][k], my[i][k], mz[i][k]));

                output.Add(stresses);
            }

            return output;
        }
    }
}
