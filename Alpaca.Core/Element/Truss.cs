using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Rhino.Geometry;
using Alpaca4d.Generic;
using Alpaca4d.BeamIntegration;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Element
{
    /// <summary>Which way a <see cref="Truss"/> bar carries its axial force.</summary>
    public enum AxialBehaviour
    {
        /// <summary>Tension and compression alike, the ordinary bar.</summary>
        Both,

        /// <summary>Tension only: a cable or a bracing rod, slack when pushed.</summary>
        Tension,

        /// <summary>Compression only: a strut that lets go when pulled.</summary>
        Compression,
    }

    /// <summary>
    /// A bar that carries axial force only - OpenSees' <c>corotTruss</c>.
    ///
    /// It is a beam to the rest of Alpaca4d, so self-weight, mass, Model View, loads by ElementId, Design
    /// Group and Section Optimiser treat it as one: it takes its area and its material from a beam
    /// section, any section, and its local axes only to stand that section up in Model View. What it
    /// writes is a bar, not a beam:
    ///
    /// <list type="bullet">
    /// <item>Always corotational. For small displacements the answer is a linear truss's; for large
    /// ones it is right, so there is no switch to get wrong.</item>
    /// <item><see cref="WorksIn"/> picks the material. Tension only is an Elastic material with no
    /// stiffness in compression - stiff at zero strain, which is where Newton starts. Compression
    /// only is ENT, not an Elastic with no stiffness in tension: OpenSees' Elastic takes its tension
    /// stiffness at zero strain, so that one would start with no stiffness at all and the first
    /// iteration would fail.</item>
    /// <item><see cref="Prestress"/> wraps the material in InitStressMaterial. A straight cable with
    /// no prestress has no stiffness across its length; with it, the geometric stiffness of the
    /// corotational bar holds the cable.</item>
    /// <item>A line load on a truss goes to its two end nodes, half each (<see cref="LumpedLoadTcl"/>):
    /// the bar has no bending to carry it with.</item>
    /// </list>
    ///
    /// A truss has no rotational stiffness, so a node that only trusses reach would leave its three
    /// rotations free and the stiffness matrix singular; Assemble restrains them there
    /// (<see cref="Model"/>).
    /// </summary>
    public partial class Truss : EntityBase, IStructure, IBeam, ISerialize
    {
        public Curve Curve { get; set; }
        public IUniaxialSection Section { get; set; }

        /// <summary>The local axes, for Model View and for loads resolved onto the bar. Never written: a truss has no transformation.</summary>
        public GeomTransf GeomTransf { get; set; }

        /// <summary>A station at each end, which is where the axial force is reported - it is the same all along.</summary>
        public IIntegration BeamIntegration { get; set; }

        public AxialBehaviour WorksIn { get; set; } = AxialBehaviour.Both;

        /// <summary>The axial force the bar starts with [kN], tension positive.</summary>
        public double Prestress { get; set; }

        public ElementType Type => ElementType.Beam;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public int? INode { get; set; }
        public int? JNode { get; set; }
        public int Ndf => 6;
        public System.Drawing.Color Color { get; set; } = Alpaca4d.Colors.DefaultBeam;

        /// <summary>Mass per unit length [kg/m], from the section's area and its material's density.</summary>
        public double? MassDens => this.Section.Area * this.Section.Material.Rho;

        // Tags of the bar's own materials. They are the bar's own because what it writes depends on
        // more than the section: on which way it works and on its prestress over its area.
        private readonly int materialTag = IdGenerator.GenerateId();
        private readonly int prestressTag = IdGenerator.GenerateId();

        public Truss(Curve curve, IUniaxialSection section, Vector3d localZ, AxialBehaviour worksIn = AxialBehaviour.Both, double prestress = 0.0)
        {
            this.Curve = curve;
            this.Section = section;
            this.GeomTransf = new GeomTransf(GeomTransfType.Linear, curve, localZ);
            this.BeamIntegration = new NewtonContes(section, 2);
            this.WorksIn = worksIn;
            this.Prestress = prestress;
        }

        public IBeam WithSection(IUniaxialSection section)
        {
            return new Truss(this.Curve, section, this.GeomTransf.LocalZ, this.WorksIn, this.Prestress)
            {
                ElementId = this.ElementId,
                Color = this.Color,
            };
        }

        public void SetTags()
        {
        }

        /// <summary>The nodes at the two ends, as a beam finds them.</summary>
        public void SetTopologyRTree(Model model)
        {
            var ends = new[] { this.Curve.PointAtStart, this.Curve.PointAtEnd };

            var nodes = Alpaca4d.Utils.NodeSearch(model.NodeIndexSixNDF, ends, $"Truss {this.Id}");

            this.INode = nodes[0] + 1 + model.UniquePointsThreeNDF.Count;
            this.JNode = nodes[1] + 1 + model.UniquePointsThreeNDF.Count;
        }

        /// <summary>
        /// Why this bar cannot be written, or null when it can: a prestress pulling a bar that only
        /// pushes, or pushing one that only pulls, has no strain that gives it, and InitStressMaterial
        /// would search for one without end.
        /// </summary>
        public string Problem()
        {
            if (!(this.Section.Area > 0.0))
                return "A truss needs a section with an area greater than zero.";
            if (this.WorksIn == AxialBehaviour.Tension && this.Prestress < 0.0)
                return $"A tension-only bar cannot start in compression: its prestress is {TclNumber.Write(this.Prestress)}. Give a prestress of zero or more, tension being positive.";
            if (this.WorksIn == AxialBehaviour.Compression && this.Prestress > 0.0)
                return $"A compression-only bar cannot start in tension: its prestress is {TclNumber.Write(this.Prestress)}. Give a prestress of zero or less, compression being negative.";
            return null;
        }

        /// <summary>The Tcl, as every element shows it - or what is wrong, rather than an exception in a Panel.</summary>
        public override string ToString()
        {
            var problem = this.Problem();
            return problem == null ? this.WriteTcl() : $"Truss: {problem}";
        }

        public override string WriteTcl()
        {
            var problem = this.Problem();
            if (problem != null)
                throw new Exception($"Truss {this.Id}: {problem}");

            var e = TclNumber.Write(this.Section.Material.E);
            var tcl = new StringBuilder();

            switch (this.WorksIn)
            {
                case AxialBehaviour.Tension:
                    tcl.Append($"uniaxialMaterial Elastic {this.materialTag} {e} 0.0 0.0\n");
                    break;
                case AxialBehaviour.Compression:
                    tcl.Append($"uniaxialMaterial ENT {this.materialTag} {e}\n");
                    break;
                default:
                    tcl.Append($"uniaxialMaterial Elastic {this.materialTag} {e} 0.0 {e}\n");
                    break;
            }

            int material = this.materialTag;
            if (this.Prestress != 0.0)
            {
                var stress = this.Prestress / this.Section.Area;
                tcl.Append($"uniaxialMaterial InitStressMaterial {this.prestressTag} {this.materialTag} {TclNumber.Write(stress)}\n");
                material = this.prestressTag;
            }

            tcl.Append($"element corotTruss {this.Id} {this.INode} {this.JNode} {TclNumber.Write(this.Section.Area)} {material}" +
                       $" -rho {TclNumber.Write(Alpaca4d.ModelMass.FromKg(this.MassDens))}\n");
            return tcl.ToString();
        }

        /// <summary>
        /// A line load on this bar, as two point loads at its ends, half the total each. The load per
        /// length is given the way <see cref="Loads.LineLoad"/> holds it - along the bar and along local y
        /// and z - and put back together on the same axes a beam in the same place would read it on,
        /// so a truss and a beam carry the same total.
        /// </summary>
        public string LumpedLoadTcl(Vector3d localForce)
        {
            var start = this.Curve.PointAtStart;
            var end = this.Curve.PointAtEnd;
            var x = end - start;
            double length = x.Length;
            x.Unitize();

            var perLength = localForce.X * x + localForce.Y * this.GeomTransf.LocalY + localForce.Z * this.GeomTransf.LocalZ;
            var half = perLength * (length / 2.0);

            string Load(int? node) =>
                $"\tload {node} {TclNumber.Write(half.X)} {TclNumber.Write(half.Y)} {TclNumber.Write(half.Z)} 0.0 0.0 0.0\n";

            return Load(this.INode) + Load(this.JNode);
        }
    }
}
