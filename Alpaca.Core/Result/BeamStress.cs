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
    /// The stresses at one point on the surface of a beam, where <see cref="BeamStress"/> holds the
    /// worst anywhere on the section: what View Results paints a beam with, point by point round
    /// its section.
    ///
    /// The shear is the one running along the surface - a free surface carries no other - signed
    /// along the way the point was given in <see cref="BeamSectionStress.SurfaceAt"/>.
    /// </summary>
    public struct PointStress
    {
        /// <summary>Direct stress along the beam, N, My and Mz together, positive in tension.</summary>
        public double Sigma;
        /// <summary>The part of <see cref="Sigma"/> My causes, My·z/Iy.</summary>
        public double SigmaMy;
        /// <summary>The part of <see cref="Sigma"/> Mz causes, −Mz·y/Iz.</summary>
        public double SigmaMz;
        /// <summary>Shear from Vy and Vz, along the surface.</summary>
        public double TauV;
        /// <summary>Shear from torsion, along the surface.</summary>
        public double TauT;

        /// <summary>
        /// The two shears together. Signed, so where they run the same way they add and where they
        /// run against each other they take away - which side of a wall is the worse one is much of
        /// what reading a stress point by point is for.
        /// </summary>
        public double Tau => TauV + TauT;

        /// <summary>Von Mises at the point, √(σ² + 3τ²), with the σ and τ that actually meet there.</summary>
        public double VonMises => Math.Sqrt(Sigma * Sigma + 3.0 * Tau * Tau);
    }

    /// <summary>
    /// A point on the surface of a section and what one unit of each section force does there.
    /// Stress is linear in the forces, so the shape is worked out once per point, and every station
    /// of every beam with that section is then a handful of multiplications.
    /// </summary>
    public struct SurfacePoint
    {
        /// <summary>Where it is, from the centroid, in the beam's local axes.</summary>
        public double Y, Z;
        /// <summary>Direct stress per unit N, My and Mz.</summary>
        public double PerN, PerMy, PerMz;
        /// <summary>Shear along the surface per unit Vy, Vz and T, as OpenSees reports them.</summary>
        public double PerVy, PerVz, PerT;

        /// <summary>The stresses under one set of section forces, in the order Read.ForceBeamColumn names them.</summary>
        public PointStress Under(double n, double vy, double vz, double t, double my, double mz)
        {
            double sigmaMy = this.PerMy * my;
            double sigmaMz = this.PerMz * mz;
            return new PointStress
            {
                Sigma = this.PerN * n + sigmaMy + sigmaMz,
                SigmaMy = sigmaMy,
                SigmaMz = sigmaMz,
                TauV = this.PerVy * vy + this.PerVz * vz,
                TauT = this.PerT * t
            };
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

        /// <summary>
        /// The St. Venant torsion constant It, worked out from the shape: Σ a·b³/3 for an open one,
        /// Bredt's 4Am² / ∮ds/t for a box, the polar moment for a circle. Not the section's own J,
        /// which for an ISection is Iy + Iz - see <see cref="OpenTorsion"/>.
        /// </summary>
        public double TorsionConstant { get; private set; }

        /// <summary>
        /// ∫ y (y² + z²) dA, centroidal: what the monosymmetry constant of lateral-torsional buckling
        /// under Mz is made from. Zero for a shape symmetric about z, and for a circle.
        /// </summary>
        public double WagnerY { get; private set; }

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

            // The k of RectangleCS.J, so the check and the solver agree on the bar.
            section.TorsionConstant = a > 0.0 ? a * b * b * b / (3.0 + 4.1 * Math.Pow(r, 1.5)) : 0.0;

            if (section.HasShape) section._walls = Walls.Solid;
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
                _radius = outer,
                _walls = Walls.Round
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
            section.TorsionConstant = polar;
            section._polar = polar;

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

            // ∮ds/t round the same centre line: two side walls and the top and bottom ones.
            double wallHeight = height - (topFlange + bottomFlange) / 2.0;
            double round = web > 0.0 && topFlange > 0.0 && bottomFlange > 0.0
                ? 2.0 * wallHeight / web + (width - web) / topFlange + (width - web) / bottomFlange
                : 0.0;
            section.TorsionConstant = round > 0.0 ? 4.0 * enclosed * enclosed / round : 0.0;

            if (section.HasShape)
            {
                // The centre lines of the walls, round the cell the way a positive torque turns it -
                // from local y towards local z - and measured from the centroid.
                double wall = side - web / 2.0;
                double upper = top - topFlange / 2.0 - section.CentroidY;
                double lower = bottom + bottomFlange / 2.0 - section.CentroidY;
                double z = -section.CentroidZ;
                section._cell = Cell.Round(
                    new[] { (upper, z - wall), (upper, z + wall), (lower, z + wall), (lower, z - wall) },
                    new[] { topFlange, web, bottomFlange, web },
                    section.Iy, section.Iz);
                section._walls = Walls.Closed;
            }

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
            section.WagnerY = Wagner(section);
            if (section.HasShape) section._walls = Walls.Open;
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
            section.WagnerY = Wagner(section);
            if (section.HasShape) section._walls = Walls.Open;
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
                _boxes = centred,
                _links = boxes.Where(b => b.Link).Select(Centre).ToList()
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

            section.TorsionConstant = j;
            return j > 0.0 ? thickest / j : 0.0;
        }

        /// <summary>∫ y (y² + z²) dA over the rectangles, each integrated exactly.</summary>
        private static double Wagner(BeamSectionStress section)
        {
            if (section._boxes == null) return 0.0;

            double total = 0.0;
            foreach (var b in section._boxes)
            {
                double w = b.Z2 - b.Z1;
                double y4 = (Math.Pow(b.Y2, 4) - Math.Pow(b.Y1, 4)) / 4.0;
                double y2 = (b.Y2 * b.Y2 - b.Y1 * b.Y1) / 2.0;
                double z3 = (Math.Pow(b.Z2, 3) - Math.Pow(b.Z1, 3)) / 3.0;
                total += w * y4 + y2 * z3;
            }

            return total;
        }

        #endregion

        #region At a point of the surface

        /// <summary>How the shear at a point of the surface is worked out; see <see cref="SurfaceAt"/>.</summary>
        private enum Walls { None, Round, Solid, Open, Closed }

        private Walls _walls;

        /// <summary>Polar moment of a round section, which its torsion is spread by.</summary>
        private double _polar;

        /// <summary>The links of a pair of angles, centred: they tie the two together for shear.</summary>
        private List<Box> _links = new List<Box>();

        /// <summary>A box section's walls, for its shear flow. Null for every other shape.</summary>
        private Cell _cell;

        /// <summary>
        /// What one unit of each section force does at a point on the section's surface: (y, z)
        /// measured from the centroid, (ty, tz) the way the surface runs there, either way along it.
        ///
        /// Direct stress is exact, the same σ = N/A − Mz·y/Iz + My·z/Iy as everywhere else. Shear
        /// needs the shape:
        /// - round bar or tube: Jourawski's peak turned to run round the surface - full where the
        ///   surface runs along the shear force, nothing where it faces it - and T·r/Ip.
        /// - rectangle: Jourawski's parabola each way, and Saint-Venant's torsion, which peaks at the
        ///   middle of the long sides and is nothing in the corners.
        /// - I and pair of angles, open thin walls: thin-walled theory's shear flow, cut through the
        ///   wall the point is on (see <see cref="OpenFlow"/>), and each wall's own Saint-Venant
        ///   torsion, T·t/J at its middle.
        /// - box, a closed cell: the shear flow that goes round the cell without twisting it, and
        ///   Bredt's T/2Am.
        ///
        /// Both shears are signed along (ty, tz), so on one face of a wall they add and on the other
        /// they take away. Thin-walled flow is the textbook's, and away from the corners between
        /// walls it is what exact elasticity gives; at those corners real sections have root radii.
        /// </summary>
        public SurfacePoint SurfaceAt(double y, double z, double ty, double tz)
        {
            var point = new SurfacePoint
            {
                Y = y,
                Z = z,
                PerN = this.Area > 0.0 ? 1.0 / this.Area : 0.0,
                PerMy = this.Iy > 0.0 ? z / this.Iy : 0.0,
                PerMz = this.Iz > 0.0 ? -y / this.Iz : 0.0
            };

            double length = Math.Sqrt(ty * ty + tz * tz);
            if (!this.HasShape || length <= 0.0) return point;
            ty /= length;
            tz /= length;

            // Per unit of the shear forces as they act - their resultants along +y and +z - and per
            // unit torque, along (ty, tz).
            double perY = 0.0, perZ = 0.0, perT = 0.0;

            switch (_walls)
            {
                case Walls.Round:
                {
                    double r = Math.Sqrt(y * y + z * z);
                    if (r <= 0.0) break;

                    // Round the section the way a positive torque turns it, from y towards z.
                    double cy = -z / r, cz = y / r;
                    double along = cy * ty + cz * tz;

                    perY = this.ShearY * cy * along;
                    perZ = this.ShearZ * cz * along;
                    perT = _polar > 0.0 ? r / _polar * along : 0.0;
                    break;
                }

                case Walls.Solid:
                {
                    // Jourawski: V times the first moment beyond a cut through the point, over I and
                    // the width cut, which on a rectangle cancels to V·(h²/4 − y²)/2I.
                    var box = _boxes[0];
                    double hy = (box.Y2 - box.Y1) / 2.0, hz = (box.Z2 - box.Z1) / 2.0;
                    perY = this.Iz > 0.0 ? Math.Max(0.0, hy * hy - y * y) / (2.0 * this.Iz) * ty : 0.0;
                    perZ = this.Iy > 0.0 ? Math.Max(0.0, hz * hz - z * z) / (2.0 * this.Iy) * tz : 0.0;
                    perT = this.Torsion * Twist(box, y, z, ty, tz);
                    break;
                }

                case Walls.Open:
                {
                    int wall = WallAt(y, z, ty, tz);
                    if (wall < 0) break;

                    OpenFlow(wall, y, z, ty, tz, out perY, out perZ);

                    var box = _boxes[wall];
                    double thickness = Math.Min(box.Y2 - box.Y1, box.Z2 - box.Z1);
                    perT = this.TorsionConstant > 0.0 ? thickness / this.TorsionConstant * Twist(box, y, z, ty, tz, Joined(wall)) : 0.0;
                    break;
                }

                case Walls.Closed:
                    _cell?.At(y, z, ty, tz, out perY, out perZ, out perT);
                    break;
            }

            // OpenSees' Vy is dMz/dx, and with a positive Mz compressing +y that makes it the shear
            // acting down -y; its Vz, dMy/dx, acts along +z. Checked on a cantilever: a tip load of
            // +10 along y reads Vy = -10, one of +20 along z reads Vz = +20, a torque of +7 reads T = +7.
            point.PerVy = -perY;
            point.PerVz = perZ;
            point.PerT = perT;
            return point;
        }

        /// <summary>
        /// The Saint-Venant torsion stress at a point on the edge of one rectangle, as a share of the
        /// stress at the middle of its long sides, signed along (ty, tz): positive where it runs the
        /// way a positive torque turns, from y towards z.
        ///
        /// Which edge the point is on is read from the way the surface runs there, and which of the
        /// two parallel edges from which side of the middle it is.
        ///
        /// The stress dies away into the corners of a free end - a flange's tip - but not where the
        /// rectangle's end is joined to another wall, as a web's are to its flanges: there the wall
        /// carries on round the corner and, if anything, the stress gathers. <paramref name="joined"/>
        /// says which ends along the rectangle's length are joined, the low end and the high.
        /// </summary>
        private static double Twist(Box box, double y, double z, double ty, double tz, (bool Low, bool High) joined = default)
        {
            double my = (box.Y1 + box.Y2) / 2.0, mz = (box.Z1 + box.Z2) / 2.0;
            double hy = (box.Y2 - box.Y1) / 2.0, hz = (box.Z2 - box.Z1) / 2.0;

            // The outward normal, and where along the edge the point is, how long the edge is and how
            // far it is to the edge opposite - all as halves.
            double ny, nz, along, half, across;
            if (Math.Abs(tz) >= Math.Abs(ty))
            {
                ny = y >= my ? 1.0 : -1.0; nz = 0.0;
                along = z - mz; half = hz; across = hy;
            }
            else
            {
                ny = 0.0; nz = z >= mz ? 1.0 : -1.0;
                along = y - my; half = hy; across = hz;
            }

            if (half <= 0.0 || across <= 0.0) return 0.0;

            double peak = LongSide(Math.Min(half, across), Math.Max(half, across), 0.0);
            double share = half >= across ? LongSide(across, half, along) : ShortSide(half, across, along);
            if (half >= across && (along < 0.0 ? joined.Low : joined.High))
                share = peak;

            // The normal turned a quarter from y towards z.
            return share / peak * (-nz * ty + ny * tz);
        }

        /// <summary>Odd terms summed in the series below: enough for 0.2% at the very corner, far better away from it.</summary>
        private const int TwistTerms = 250;

        /// <summary>
        /// Saint-Venant's torsion stress along a long side of a rectangle, half sides a ≤ b, at s from
        /// the middle of that side, over 16·Gθ·a/π² (Timoshenko and Goodier, §109):
        /// Σ (1 − cosh(nπs/2a) / cosh(nπb/2a)) / n² over odd n, written as π²/8 less the part that
        /// dies away, which is the part that converges.
        /// </summary>
        private static double LongSide(double a, double b, double s)
        {
            s = Math.Min(Math.Abs(s), b);
            double sum = 0.0;
            for (int k = 0; k < TwistTerms; k++)
            {
                double n = 2 * k + 1;
                double c = n * Math.PI / (2.0 * a);

                // cosh(c·s) / cosh(c·b), put so that neither overflows.
                double ratio = Math.Exp(-c * (b - s)) * (1.0 + Math.Exp(-2.0 * c * s)) / (1.0 + Math.Exp(-2.0 * c * b));
                sum += ratio / (n * n);
                if (ratio < 1e-15) break;
            }

            return Math.Max(0.0, Math.PI * Math.PI / 8.0 - sum);
        }

        /// <summary>
        /// The same along a short side, at x from its middle, a now that side's half length:
        /// Σ (−1)^((n−1)/2) tanh(nπb/2a) cos(nπx/2a) / n² over odd n. Equal to the long side's at the
        /// middle of a square, and three-quarters of it on a thin strip.
        /// </summary>
        private static double ShortSide(double a, double b, double x)
        {
            x = Math.Min(Math.Abs(x), a);
            double sum = 0.0;
            for (int k = 0; k < TwistTerms; k++)
            {
                double n = 2 * k + 1;
                double c = n * Math.PI / (2.0 * a);
                double sign = k % 2 == 0 ? 1.0 : -1.0;
                sum += sign * Math.Tanh(c * b) * Math.Cos(c * x) / (n * n);
            }

            return Math.Max(0.0, sum);
        }

        #region Open thin walls

        /// <summary>
        /// Something attached to a wall: the stretch of the wall it is attached along, from Low to
        /// High, and the first moments of everything hanging off it there - what a cut through the
        /// wall on the near side would set free. A cut within the stretch frees the share beyond it.
        /// </summary>
        private struct Hanging
        {
            public double Low, High, Sy, Sz;

            public double Beyond(double cut) =>
                cut >= this.High ? 0.0 : cut <= this.Low ? 1.0 : (this.High - cut) / (this.High - this.Low);
        }

        /// <summary>What hangs off each of <see cref="_boxes"/>. Built the first time it is needed.</summary>
        private List<Hanging>[] _hanging;

        /// <summary>Whether a rectangle runs along local y - a web - rather than along z, a flange.</summary>
        private static bool AlongY(Box box) => box.Y2 - box.Y1 >= box.Z2 - box.Z1;

        /// <summary>Which ends of a wall, along its length, another wall is joined to: a web's both, a flange's neither.</summary>
        private (bool Low, bool High) Joined(int wall)
        {
            if (_hanging == null) Hang();

            var box = _boxes[wall];
            bool alongY = AlongY(box);
            double start = alongY ? box.Y1 : box.Z1, end = alongY ? box.Y2 : box.Z2;
            double touch = 1e-6 * Size();
            return (_hanging[wall].Any(h => h.Low <= start + touch), _hanging[wall].Any(h => h.High >= end - touch));
        }

        /// <summary>
        /// The shear flow of thin-walled theory, per unit of each shear force, at a point on one wall,
        /// along (ty, tz).
        ///
        /// The wall is cut across its thickness at the point. In an open section that frees what lies
        /// beyond: the rest of the wall, and whatever hangs off it further along. That part's first
        /// moment S over I and the wall's thickness is the stress, running along the wall - so it is
        /// full on the faces of a wall and nothing on its ends. Which way along the wall is taken does
        /// not matter: the part on the other side has the opposite S, the section's own first moment
        /// about its centroid being nothing.
        ///
        /// Where a wall ends on the face of another - a web on a flange, a short leg on a long one -
        /// it hangs off the whole edge they share, so the flow in the flange turns into the web
        /// across the web's thickness rather than all at once on its centre line. Two walls lying
        /// alongside each other, as a pair of angles do back to back or across their links, are
        /// tied where each meets its other walls, at the heel: the angles' shear crosses over by
        /// the shortest way, which is what exact elasticity shows two angles touching do. The
        /// links carry the tie and no area.
        /// </summary>
        private void OpenFlow(int wall, double y, double z, double ty, double tz, out double perY, out double perZ)
        {
            if (_hanging == null) Hang();

            var box = _boxes[wall];
            bool alongY = AlongY(box);
            double thickness = alongY ? box.Z2 - box.Z1 : box.Y2 - box.Y1;
            double start = alongY ? box.Y1 : box.Z1;
            double end = alongY ? box.Y2 : box.Z2;
            double cut = Math.Min(Math.Max(alongY ? y : z, start), end);

            // The wall from the cut to its far end, and what hangs off that stretch.
            double area = thickness * (end - cut);
            double middle = (cut + end) / 2.0;
            double sy = area * (alongY ? middle : (box.Y1 + box.Y2) / 2.0);
            double sz = area * (alongY ? (box.Z1 + box.Z2) / 2.0 : middle);

            foreach (var hanging in _hanging[wall])
            {
                double share = hanging.Beyond(cut);
                sy += share * hanging.Sy;
                sz += share * hanging.Sz;
            }

            double way = alongY ? ty : tz;
            perY = this.Iz > 0.0 && thickness > 0.0 ? sy / (this.Iz * thickness) * way : 0.0;
            perZ = this.Iy > 0.0 && thickness > 0.0 ? sz / (this.Iy * thickness) * way : 0.0;
        }

        /// <summary>
        /// Works out what hangs off every wall: for each edge one wall shares with another, the first
        /// moments of everything reached through that edge without coming back through the wall.
        /// </summary>
        private void Hang()
        {
            var nodes = _boxes.Concat(_links).ToList();
            double touch = 1e-9 * Size();

            // Each shared edge, from (Y1, Z1) to (Y2, Z2): along y when the two sit side by side.
            var edges = new List<(int A, int B, double Y1, double Z1, double Y2, double Z2)>();
            for (int i = 0; i < nodes.Count; i++)
                for (int j = i + 1; j < nodes.Count; j++)
                    if (Shared(nodes[i], nodes[j], touch, out var edge))
                        edges.Add((i, j, edge.Y1, edge.Z1, edge.Y2, edge.Z2));

            _hanging = new List<Hanging>[_boxes.Count];
            for (int i = 0; i < _boxes.Count; i++)
            {
                _hanging[i] = new List<Hanging>();
                bool alongY = AlongY(_boxes[i]);
                var mine = edges.Where(e => e.A == i || e.B == i).ToList();

                // Along this wall, the stretch an edge covers - a point where the edge runs across it.
                (double Low, double High) Stretch((int A, int B, double Y1, double Z1, double Y2, double Z2) e) =>
                    alongY ? (Math.Min(e.Y1, e.Y2), Math.Max(e.Y1, e.Y2)) : (Math.Min(e.Z1, e.Z2), Math.Max(e.Z1, e.Z2));

                // Side by side: the other runs the same way as the edge, and so as this wall.
                bool Lapped((int A, int B, double Y1, double Z1, double Y2, double Z2) e)
                {
                    var other = nodes[e.A == i ? e.B : e.A];
                    bool edgeAlongY = Math.Abs(e.Y2 - e.Y1) > Math.Abs(e.Z2 - e.Z1);
                    return AlongY(other) == edgeAlongY && alongY == edgeAlongY;
                }

                // Where the walls ending on this one meet it, which a wall alongside is tied at.
                var junctions = mine.Where(e => !Lapped(e)).Select(e => Stretch(e)).Select(s => (s.Low + s.High) / 2.0).ToList();

                foreach (var edge in mine)
                {
                    int other = edge.A == i ? edge.B : edge.A;

                    // Everything reached from the other side without passing back through this wall.
                    var reached = new HashSet<int> { other };
                    var queue = new Queue<int>(reached);
                    while (queue.Count > 0)
                    {
                        int at = queue.Dequeue();
                        foreach (var next in edges.Where(e => e.A == at || e.B == at).Select(e => e.A == at ? e.B : e.A))
                        {
                            if (next != i && reached.Add(next))
                                queue.Enqueue(next);
                        }
                    }

                    var material = reached.Select(k => nodes[k]).Where(b => !b.Link).ToList();
                    var stretch = Stretch(edge);
                    if (Lapped(edge))
                    {
                        // Tied at the point of the edge nearest a junction, or at its middle with none.
                        double tie = (stretch.Low + stretch.High) / 2.0;
                        double nearest = double.MaxValue;
                        foreach (double junction in junctions)
                        {
                            double on = Math.Min(Math.Max(junction, stretch.Low), stretch.High);
                            if (Math.Abs(on - junction) < nearest)
                            {
                                nearest = Math.Abs(on - junction);
                                tie = on;
                            }
                        }
                        stretch = (tie, tie);
                    }

                    _hanging[i].Add(new Hanging
                    {
                        Low = stretch.Low,
                        High = stretch.High,
                        Sy = material.Sum(b => b.Area * (b.Y1 + b.Y2) / 2.0),
                        Sz = material.Sum(b => b.Area * (b.Z1 + b.Z2) / 2.0)
                    });
                }
            }
        }

        /// <summary>Whether two rectangles share an edge of some length, and where it runs if so.</summary>
        private static bool Shared(Box a, Box b, double touch, out (double Y1, double Z1, double Y2, double Z2) edge)
        {
            edge = default;

            // One on top of the other, across y.
            bool above = Math.Abs(a.Y2 - b.Y1) <= touch, below = Math.Abs(a.Y1 - b.Y2) <= touch;
            if (above || below)
            {
                double low = Math.Max(a.Z1, b.Z1), high = Math.Min(a.Z2, b.Z2);
                if (high - low > touch)
                {
                    double y = above ? a.Y2 : a.Y1;
                    edge = (y, low, y, high);
                    return true;
                }
            }

            // Side by side, across z.
            bool right = Math.Abs(a.Z2 - b.Z1) <= touch, left = Math.Abs(a.Z1 - b.Z2) <= touch;
            if (right || left)
            {
                double low = Math.Max(a.Y1, b.Y1), high = Math.Min(a.Y2, b.Y2);
                if (high - low > touch)
                {
                    double z = right ? a.Z2 : a.Z1;
                    edge = (low, z, high, z);
                    return true;
                }
            }

            return false;
        }

        /// <summary>How far the section reaches from its centroid, which tolerances are measured against.</summary>
        private double Size() => _corners.Count > 0 ? _corners.Max(c => Math.Max(Math.Abs(c.Y), Math.Abs(c.Z))) : 1.0;

        /// <summary>
        /// Which of <see cref="_boxes"/> a point of the surface is on: the one a short step off the
        /// surface lands in, on the side where a step the other way lands in no material. At a corner
        /// between two walls, the one running the way the surface does there. -1 when there is none.
        /// </summary>
        private int WallAt(double y, double z, double ty, double tz)
        {
            double step = 1e-6 * Size();

            // Generous going in and strict looking out: a point at the corner between a web and a
            // flange steps out onto the edge of the flange, which is not material on that side.
            bool Inside(Box b, double py, double pz, double margin) =>
                py >= b.Y1 + margin && py <= b.Y2 - margin && pz >= b.Z1 + margin && pz <= b.Z2 - margin;

            foreach (double side in new[] { 1.0, -1.0 })
            {
                double iy = y + side * step * tz, iz = z - side * step * ty;
                double oy = y - side * step * tz, oz = z + side * step * ty;
                if (_boxes.Any(b => Inside(b, oy, oz, step / 2.0))) continue;

                int found = -1;
                for (int k = 0; k < _boxes.Count; k++)
                {
                    if (!Inside(_boxes[k], iy, iz, -step / 2.0)) continue;
                    if (found < 0 || AlongY(_boxes[k]) == (Math.Abs(ty) >= Math.Abs(tz))) found = k;
                }

                if (found >= 0) return found;
            }

            return -1;
        }

        #endregion

        #region A closed cell

        /// <summary>
        /// A box section as thin-walled theory sees it: the centre lines of its walls round one closed
        /// cell, each with its thickness, and the shear flow per unit shear force all the way round.
        ///
        /// Cut open anywhere, the flow is what an open section's would be from that cut; closed, a
        /// constant flow is added so that the cell comes back to where it started without twisting,
        /// ∮ q/t ds = 0. Torsion is Bredt's constant flow T/2Am on top.
        /// </summary>
        private class Cell
        {
            /// <summary>Points the flow is held at along each wall.</summary>
            private const int Steps = 64;

            private (double Y1, double Z1, double Y2, double Z2, double T)[] _walls;
            private double[][] _flowY, _flowZ;
            private double _enclosed;

            /// <summary>
            /// The cell through <paramref name="corners"/> (y, z), each wall from one corner to the
            /// next and <paramref name="thickness"/> thick, the way a positive torque turns.
            /// </summary>
            public static Cell Round((double Y, double Z)[] corners, double[] thickness, double iy, double iz)
            {
                int count = corners.Length;
                var cell = new Cell
                {
                    _walls = new (double, double, double, double, double)[count],
                    _flowY = new double[count][],
                    _flowZ = new double[count][]
                };

                double area = 0.0, firstY = 0.0, firstZ = 0.0;
                for (int k = 0; k < count; k++)
                {
                    var a = corners[k];
                    var b = corners[(k + 1) % count];
                    cell._walls[k] = (a.Y, a.Z, b.Y, b.Z, thickness[k]);

                    double length = Math.Sqrt((b.Y - a.Y) * (b.Y - a.Y) + (b.Z - a.Z) * (b.Z - a.Z));
                    area += length * thickness[k];
                    firstY += length * thickness[k] * (a.Y + b.Y) / 2.0;
                    firstZ += length * thickness[k] * (a.Z + b.Z) / 2.0;

                    // Shoelace, y across and z up: positive going from y towards z.
                    cell._enclosed += (a.Y * b.Z - b.Y * a.Z) / 2.0;
                }

                // Measured from the centre lines' own centroid, so the first moment comes back to
                // nothing all the way round and the flow meets itself where the cell was cut.
                double centreY = area > 0.0 ? firstY / area : 0.0;
                double centreZ = area > 0.0 ? firstZ / area : 0.0;

                double sy = 0.0, sz = 0.0;
                double loopY = 0.0, loopZ = 0.0, perThickness = 0.0;
                for (int k = 0; k < count; k++)
                {
                    var w = cell._walls[k];
                    double length = Math.Sqrt((w.Y2 - w.Y1) * (w.Y2 - w.Y1) + (w.Z2 - w.Z1) * (w.Z2 - w.Z1));
                    double ds = length / Steps;
                    cell._flowY[k] = new double[Steps + 1];
                    cell._flowZ[k] = new double[Steps + 1];

                    for (int j = 0; j <= Steps; j++)
                    {
                        if (j > 0)
                        {
                            double f = (j - 0.5) / Steps;
                            sy += w.T * ds * (w.Y1 + (w.Y2 - w.Y1) * f - centreY);
                            sz += w.T * ds * (w.Z1 + (w.Z2 - w.Z1) * f - centreZ);
                        }

                        // Along the wall, with what lies behind the point as the part beyond the cut.
                        cell._flowY[k][j] = iz > 0.0 ? -sy / iz : 0.0;
                        cell._flowZ[k][j] = iy > 0.0 ? -sz / iy : 0.0;

                        if (j > 0)
                        {
                            loopY += (cell._flowY[k][j] + cell._flowY[k][j - 1]) / 2.0 * ds / w.T;
                            loopZ += (cell._flowZ[k][j] + cell._flowZ[k][j - 1]) / 2.0 * ds / w.T;
                        }
                    }

                    perThickness += length / w.T;
                }

                double closeY = perThickness > 0.0 ? -loopY / perThickness : 0.0;
                double closeZ = perThickness > 0.0 ? -loopZ / perThickness : 0.0;
                for (int k = 0; k < count; k++)
                {
                    for (int j = 0; j <= Steps; j++)
                    {
                        cell._flowY[k][j] += closeY;
                        cell._flowZ[k][j] += closeZ;
                    }
                }

                return cell;
            }

            /// <summary>
            /// The stress at a point of the surface per unit shear force and torque, along (ty, tz):
            /// that of the nearest wall running the same way, at the point's place along it.
            /// </summary>
            public void At(double y, double z, double ty, double tz, out double perY, out double perZ, out double perT)
            {
                perY = perZ = perT = 0.0;

                int best = -1;
                double nearest = double.MaxValue, place = 0.0, way = 0.0;
                for (int k = 0; k < _walls.Length; k++)
                {
                    var w = _walls[k];
                    double dy = w.Y2 - w.Y1, dz = w.Z2 - w.Z1;
                    double length = Math.Sqrt(dy * dy + dz * dz);
                    if (length <= 0.0) continue;
                    dy /= length; dz /= length;

                    double along = dy * ty + dz * tz;
                    if (Math.Abs(along) < 0.5) continue;

                    double off = Math.Abs((y - w.Y1) * dz - (z - w.Z1) * dy);
                    if (off < nearest)
                    {
                        nearest = off;
                        best = k;
                        place = Math.Min(Math.Max(((y - w.Y1) * dy + (z - w.Z1) * dz) / length, 0.0), 1.0);
                        way = along;
                    }
                }

                if (best < 0) return;

                double at = place * Steps;
                int i = Math.Min((int)at, Steps - 1);
                double f = at - i;
                double flowY = _flowY[best][i] + (_flowY[best][i + 1] - _flowY[best][i]) * f;
                double flowZ = _flowZ[best][i] + (_flowZ[best][i + 1] - _flowZ[best][i]) * f;
                double thickness = _walls[best].T;

                perY = flowY / thickness * way;
                perZ = flowZ / thickness * way;
                // Signed, so a cell given the other way round comes out the same.
                perT = _enclosed != 0.0 ? 1.0 / (2.0 * _enclosed) / thickness * way : 0.0;
            }
        }

        #endregion

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
            return BeamSectionForces(alpacaModel, step)
                .Select(beam => beam.Forces.Select(f => beam.Shape.At(f[0], f[1], f[2], f[3], f[4], f[5])).ToList())
                .ToList();
        }

        /// <summary>
        /// What <see cref="BeamStresses"/> recovers its stresses from, for when they are wanted at a
        /// point of the section rather than at its worst: every beam's section, and its section
        /// forces at every integration section as N, Vy, Vz, T, My, Mz. One entry per beam in the
        /// order of Model.Beams, sections from the I end to the J end.
        /// </summary>
        public static List<(BeamSectionStress Shape, List<double[]> Forces)> BeamSectionForces(Model alpacaModel, int step)
        {
            var output = new List<(BeamSectionStress, List<double[]>)>();

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

                var forces = new List<double[]>();
                for (int k = 0; k < n[i].Count; k++)
                    forces.Add(new[] { n[i][k], vy[i][k], vz[i][k], t[i][k], my[i][k], mz[i][k] });

                output.Add((stressOf, forces));
            }

            return output;
        }
    }
}
