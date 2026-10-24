using System;
using Rhino.Geometry;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d.Generic;

namespace Alpaca4d.Element
{
    public partial class Support : EntityBase, IStructure, ISerialize
    {
        public Point3d Pos { get; set; }
        public bool Tx { get; set; }
        public bool Ty { get; set; }
        public bool Tz { get; set; }
        public bool Rx { get; set; }
        public bool Ry { get; set; }
        public bool Rz { get; set; }
        public int? Id { get; set; }
        public int ndf { get; set; }

        private Plane plane = Plane.WorldXY;

        /// <summary>
        /// The frame Tx/Ty/Tz and Rx/Ry/Rz are read in. Its axes - not the world axes -
        /// are what each restraint refers to, so rotating the plane skews the support.
        /// The origin is the support location, mirrored by <see cref="Pos"/>; the reaction
        /// reader hands this back as the support's position and frame in one.
        /// </summary>
        public Plane Plane
        {
            get
            {
                // A support that was never given a plane still has to report where it is,
                // so fall back to the world axes at the support rather than at the origin.
                return this.plane.IsValid
                    ? this.plane
                    : new Plane(this.Pos, Vector3d.XAxis, Vector3d.YAxis);
            }
            set { this.plane = value; }
        }

        /// <summary>
        /// The coincident node that carries the <c>fix</c> of a skewed support, and with
        /// it the reaction. Null while the support restrains global axes and needs no
        /// spring. Assigned by <see cref="Model"/> during assembly.
        /// </summary>
        public int? AuxiliaryNodeId { get; set; }

        /// <summary>Tag of the zeroLength element joining the support node to <see cref="AuxiliaryNodeId"/>.</summary>
        public int? SpringElementId { get; set; }

        /// <summary>Penalty material holding the restrained translations, set during assembly.</summary>
        public Alpaca4d.Material.UniaxialMaterialElastic TranslationSpring { get; set; }

        /// <summary>Penalty material holding the restrained rotations, set during assembly.</summary>
        public Alpaca4d.Material.UniaxialMaterialElastic RotationSpring { get; set; }

        /// <summary>
        /// The springs of a spring support, one per direction of <see cref="Plane"/> it acts in; empty
        /// for an ordinary support. A spring support holds nothing rigidly - its Tx to Rz are all
        /// false - and a direction with no spring is free. It goes the way a skewed support goes:
        /// a fixed node of its own and a zeroLength to the support's node, which is where its
        /// reaction is read. It never fixes the support's node itself, so an ordinary support on the
        /// same node can hold the directions it leaves free.
        /// </summary>
        public List<SupportSpring> Springs { get; set; } = new List<SupportSpring>();

        /// <summary>The materials of <see cref="Springs"/>, in the order of <see cref="RestrainedDirections"/>; set during assembly.</summary>
        public List<IMaterial> SpringMaterials { get; set; }

        public bool IsSpring => this.Springs != null && this.Springs.Count > 0;

        /// <summary>A support of springs on <paramref name="frame"/>, one spring per direction at most.</summary>
        public static Support WithSprings(Plane frame, IEnumerable<SupportSpring> springs)
        {
            var list = springs.ToList();
            var repeated = list.GroupBy(spring => spring.Direction).FirstOrDefault(group => group.Count() > 1);
            if (repeated != null)
                throw new ArgumentException($"Direction {repeated.Key} has {repeated.Count()} springs; give it one.");

            return new Support(frame, false, false, false, false, false, false) { Springs = list };
        }

        /// <summary>
        /// Two frames count as the same one when their axes agree to this much. Well
        /// inside anything a user can dial in, and far outside the rounding a plane
        /// picks up being passed around Grasshopper.
        /// </summary>
        private const double AxisTolerance = 1e-9;

        /// <summary>
        /// True when the support frame is the world frame, so every restraint lands on a
        /// global degree of freedom and OpenSees' own <c>fix</c> can express it exactly.
        /// </summary>
        public bool IsAxisAligned
        {
            get
            {
                return this.Plane.XAxis.EpsilonEquals(Vector3d.XAxis, AxisTolerance)
                    && this.Plane.YAxis.EpsilonEquals(Vector3d.YAxis, AxisTolerance)
                    && this.Plane.ZAxis.EpsilonEquals(Vector3d.ZAxis, AxisTolerance);
            }
        }

        /// <summary>
        /// The restraints as zeroLength <c>-dir</c> indices: 1-3 translation along the
        /// plane's own axes, 4-6 rotation about them. A 3 ndf node - anything meshed out
        /// of bricks - carries no rotations, so Rx/Ry/Rz are dropped rather than written
        /// as directions the node does not have.
        /// </summary>
        public List<int> RestrainedDirections
        {
            get
            {
                var directions = new List<int>();

                if (this.IsSpring)
                {
                    // The springs' own directions, in order; a solid's node has no rotations to spring.
                    return this.Springs.Select(spring => spring.Direction)
                                       .Where(direction => this.ndf != 3 || direction <= 3)
                                       .OrderBy(direction => direction)
                                       .ToList();
                }

                if (this.Tx) directions.Add(1);
                if (this.Ty) directions.Add(2);
                if (this.Tz) directions.Add(3);

                if (this.ndf == 3)
                    return directions;

                if (this.Rx) directions.Add(4);
                if (this.Ry) directions.Add(5);
                if (this.Rz) directions.Add(6);

                return directions;
            }
        }

        /// <summary>True when this support is written as a zeroLength to a node of its own rather than a <c>fix</c>.</summary>
        public bool NeedsSpring
        {
            get { return (this.IsSpring || !this.IsAxisAligned) && this.RestrainedDirections.Count != 0; }
        }

        /// <summary>The spring acting in <paramref name="direction"/>, or null.</summary>
        public SupportSpring SpringAlong(int direction)
        {
            return this.Springs?.FirstOrDefault(spring => spring.Direction == direction);
        }

        /// <summary>True when a spring of this support pushes back one way only: it needs a non-linear analysis.</summary>
        public bool IsOneWay => this.IsSpring && this.Springs.Any(spring => spring.IsOneWay);

        public dynamic Geometry
        {
            get
            {
                var symbol = this.SymbolGeometry;

                if (symbol is Mesh mesh && !this.IsAxisAligned)
                {
                    var oriented = mesh.DuplicateMesh();
                    oriented.Transform(Transform.PlaneToPlane(
                        Plane.WorldXY,
                        new Plane(Point3d.Origin, this.Plane.XAxis, this.Plane.YAxis)));
                    return oriented;
                }

                return symbol;
            }
        }

        /// <summary>
        /// Which of the seven predefined supports this one is, or null when its six
        /// restraints are some other combination. Null is an ordinary answer: the booleans
        /// describe 64 supports and only seven of them are named.
        /// </summary>
        public SupportPreset Preset
        {
            // A spring support's six states are all false, which is the Free preset; it is not free.
            get { return this.IsSpring ? null : SupportPreset.Match(this); }
        }

        /// <summary>The preset name where there is one, and the six states either way.</summary>
        public string Description
        {
            get
            {
                if (this.IsSpring)
                    return "Spring support" + Environment.NewLine +
                           string.Join(Environment.NewLine, this.Springs.OrderBy(spring => spring.Direction).Select(spring => spring.Describe()));

                var preset = this.Preset;
                var dof = SupportPreset.DofSummary(this.Tx, this.Ty, this.Tz, this.Rx, this.Ry, this.Rz);

                return preset == null ? dof : preset.Label + Environment.NewLine + dof;
            }
        }

        /// <summary>
        /// What a panel or a tooltip shows: the name of the support and what it holds.
        /// EntityBase prints the Tcl instead, which answers a different question - the deck
        /// is still one <see cref="WriteTcl"/> away for anyone who wants it.
        /// </summary>
        public override string ToString()
        {
            return this.Description;
        }

        private dynamic SymbolGeometry
        {
            get
            {
                // A spring support is drawn as a tag, short enough for a dot: "k z" or "comp z".
                if (this.IsSpring)
                {
                    var names = new[] { "x", "y", "z", "rx", "ry", "rz" };
                    return string.Join(" ", this.Springs.OrderBy(spring => spring.Direction).Select(spring =>
                        (spring.IsOneWay ? "comp " : "k ") + names[spring.Direction - 1]));
                }

                // One of the seven gets its own symbol. Anything else falls through to the
                // short text tag, which is what a support outside the presets has always
                // been drawn as.
                var symbol = SupportSymbol.For(this.Preset);

                if (symbol != null)
                    return symbol;

                var tx = this.Tx == true ? "x" : "";
                var ty = this.Ty == true ? "y" : "";
                var tz = this.Tz == true ? "z" : "";
                var rx = this.Rx == true ? "xx" : "";
                var ry = this.Ry == true ? "yy" : "";
                var rz = this.Rz == true ? "zz" : "";
                return $"{tx}{ty}{tz}-{rx}{ry}{rz}";
            }
        }
        public Support(Point3d node, bool tx, bool ty, bool tz, bool rx, bool ry, bool rz)
            : this(new Plane(node, Vector3d.XAxis, Vector3d.YAxis), tx, ty, tz, rx, ry, rz)
        {
        }

        public Support(Plane frame, bool tx, bool ty, bool tz, bool rx, bool ry, bool rz)
        {
            this.Plane = frame;
            this.Pos = frame.Origin;
            this.Tx = tx;
            this.Ty = ty;
            this.Tz = tz;
            this.Rx = rx;
            this.Ry = ry;
            this.Rz = rz;
        }

        public Support(int index, bool tx, bool ty, bool tz, bool rx, bool ry, bool rz)
        {
            this.Id = index;
            this.Tx = tx;
            this.Ty = ty;
            this.Tz = tz;
            this.Rx = rx;
            this.Ry = ry;
            this.Rz = rz;
        }

        public override string WriteTcl()
        {
            // A support the model has not seen yet - the one a Grasshopper panel prints
            // straight off the component - has no node to name. Show it the way it always
            // has been shown rather than failing on a preview.
            if (this.ndf == 0 && this.Id == null)
                return this.WriteFixTcl(6);

            if (this.ndf != 3 && this.ndf != 6)
                throw new Exception($"The support at location {this.Pos.ToString()} is not part of the mdodel");

            return this.NeedsSpring ? this.WriteSpringTcl() : this.WriteFixTcl(this.ndf);
        }

        /// <summary>
        /// An axis-aligned support, written the way OpenSees means it: a single point
        /// constraint on global degrees of freedom, exact and free of any penalty.
        /// </summary>
        private string WriteFixTcl(int dofCount)
        {
            var translations = $"{Convert.ToInt16(this.Tx)} {Convert.ToInt16(this.Ty)} {Convert.ToInt16(this.Tz)}";

            if (dofCount == 3)
                return $"fix {this.Id} {translations}\n";

            return $"fix {this.Id} {translations} {Convert.ToInt16(this.Rx)} {Convert.ToInt16(this.Ry)} {Convert.ToInt16(this.Rz)}\n";
        }

        /// <summary>
        /// A skewed support. OpenSees has no notion of a nodal coordinate system - `fix`
        /// only ever speaks global degrees of freedom - so the restraint is carried by a
        /// zeroLength element between the support node and a coincident node that is
        /// fixed outright. `-orient` hands that element the support plane, and from then
        /// on its `-dir` indices count along the plane's own axes: 1-3 translation, 4-6
        /// rotation. Directions left out of `-dir` carry no stiffness at all, so a
        /// release along a local axis is exact; only the restrained ones are a penalty.
        ///
        /// The reaction ends up on the auxiliary node, in global components, because that
        /// is where the point constraint lives - see the reaction reader.
        /// </summary>
        private string WriteSpringTcl()
        {
            var directions = this.RestrainedDirections;
            var x = this.Plane.XAxis;
            var y = this.Plane.YAxis;

            var tcl = new StringBuilder();
            IEnumerable<int?> materials;

            if (this.IsSpring)
            {
                // The springs' own materials, one per direction; see SupportSpring.Material.
                foreach (var material in this.SpringMaterials)
                    tcl.Append(material.WriteTcl());
                materials = this.SpringMaterials.Select(material => material.Id);
            }
            else
            {
                materials = directions.Select(direction =>
                    direction <= 3 ? this.TranslationSpring.Id : this.RotationSpring.Id);

                if (directions.Any(direction => direction <= 3))
                    tcl.Append(this.TranslationSpring.WriteTcl());
                if (directions.Any(direction => direction > 3))
                    tcl.Append(this.RotationSpring.WriteTcl());
            }

            tcl.Append($"node {this.AuxiliaryNodeId} {this.Pos.X} {this.Pos.Y} {this.Pos.Z}\n");
            tcl.Append(this.ndf == 3
                ? $"fix {this.AuxiliaryNodeId} 1 1 1\n"
                : $"fix {this.AuxiliaryNodeId} 1 1 1 1 1 1\n");
            tcl.Append($"element zeroLength {this.SpringElementId} {this.AuxiliaryNodeId} {this.Id}" +
                       $" -mat {string.Join(" ", materials)}" +
                       $" -dir {string.Join(" ", directions)}" +
                       $" -orient {x.X} {x.Y} {x.Z} {y.X} {y.Y} {y.Z}\n");

            return tcl.ToString();
        }
        public void SetNodeTag(Model model)
        {
            if(this.ndf == 3)
                this.Id = model.NodeIndexThreeNDF.Nearest(this.Pos) + 1;
            else // ndf == 6
                this.Id = model.NodeIndexSixNDF.Nearest(this.Pos) + 1 + model.UniquePointsThreeNDF.Count;
        }
    }
}
