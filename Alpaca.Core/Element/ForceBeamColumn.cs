using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Rhino.Geometry;
using Alpaca4d.Generic;
using Alpaca4d.BeamIntegration;


namespace Alpaca4d.Element
{
    public partial class ForceBeamColumn : EntityBase, IStructure, IBeam, ISerialize
    {
        public Curve Curve { get; set; }
        public IUniaxialSection Section { get; set; }
        public GeomTransf GeomTransf { get; set; } = new GeomTransf();
        public IIntegration BeamIntegration { get; set; }
        public Vector3d LocalZAxis
        {
            get
            {
                return this.GeomTransf.LocalZ;
            }
        }
        public ElementType Type => ElementType.Beam;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public int? INode { get; set; }
        public int? JNode { get; set; }
        public int Ndf => 6;
        public double? MassDens => this.Section.Area * this.Section.Material.Rho;
        public System.Drawing.Color Color { get; set; } = Alpaca4d.Colors.DefaultBeam;
        public ForceBeamColumn(Curve curve, IUniaxialSection crossSection, GeomTransf geomTransf)
        {
            this.Curve = curve;
            this.Section = crossSection;
            this.GeomTransf = geomTransf;

            this.BeamIntegration = new NewtonContes(crossSection, 5);
        }

        // each element will have a unique tag for TransfTag, IntegrationTag
        public void SetTags()
        {
            this.GeomTransf.Id = this.Id;
            this.BeamIntegration.Id = this.Id;
        }

        /// <summary>
        /// The nodes at the two ends of this beam. Exactly one node per end; see
        /// <see cref="SSPbrick.SetTopologyRTree"/> for why that is worth insisting on.
        /// </summary>
        public void SetTopologyRTree(Model model)
        {
            var ends = new[] { this.Curve.PointAtStart, this.Curve.PointAtEnd };

            var nodes = Alpaca4d.Utils.RTreeSearch(model.RTreeCloudPointSixNDF, ends, model.Tollerance,
                                                   $"Force Beam Column {this.Id}", model.UniquePointsSixNDF);

            this.INode = nodes[0] + 1 + model.UniquePointsThreeNDF.Count;
            this.JNode = nodes[1] + 1 + model.UniquePointsThreeNDF.Count;
        }

        public override string WriteTcl()
        {
            string geomTransf = this.GeomTransf.WriteTcl();
            string integration = this.BeamIntegration.WriteTcl();
            string beam = $"element forceBeamColumn {Id} {INode} {JNode} {GeomTransf.Id} {integration} -mass {Alpaca4d.ModelMass.FromKg(MassDens)}\n";
            return geomTransf + beam;
        }
    }
}
