using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Rhino.Geometry;
using Alpaca4d.Generic;
using Alpaca4d.Section;
using Alpaca4d.BeamIntegration;


namespace Alpaca4d.Element
{
    public partial class ZeroLengthSection : IStructure, IElement, ISerialize
    {
        public Point3d Pos { get; set; }
        public FiberSection Section { get; set; }
        public Plane Orient { get; set; }
        public Vector3d LocalXAxis { get { return this.Orient.XAxis; } }
        public Vector3d LocalYAxis {get {return this.Orient.YAxis;}}
        public Vector3d LocalZAxis { get { return this.Orient.ZAxis; } }
        public struct Colour { };
        public ElementType Type => ElementType.Beam;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public int? INode { get; set; }
        public int? JNode { get; set; }
        public int Ndf { get; set; }

        public ZeroLengthSection(Point3d pos, FiberSection crossSection, Plane orient)
        {
            this.Pos = pos;
            this.Section = crossSection;
            this.Orient = orient;
        }

        // each element will have a unique tag for TransfTag, IntegrationTag
        public void SetTags()
        {
        }

        public void SetTopologyRTree(Model model)
        {
        }

        public string WriteTcl()
        {
            string tcl = $"element zeroLengthSection {this.Id} {this.INode} {this.JNode} {this.Section.Id} -orient {this.LocalXAxis.X} {this.LocalXAxis.Y} {this.LocalXAxis.Z} {this.LocalYAxis.X} {this.LocalYAxis.Y} {this.LocalYAxis.Z}";
            return tcl;
        }

        public override string ToString()
        {
            return "Class zeroLengthSection";
        }
    }
}
