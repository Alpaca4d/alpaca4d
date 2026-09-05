using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;
using Alpaca4d.Generic;

namespace Alpaca4d.Element
{
    /// <summary>
    /// A spring between two nodes that are not in the same place - OpenSees' <c>twoNodeLink</c>.
    ///
    /// One uniaxial material per direction, and only the directions listed carry anything: a link
    /// given direction 1 alone is a bar that pulls along its own axis and offers no resistance to
    /// anything else. That is what makes it the connector for a bearing, a bolt, a damper, or the
    /// interlayer of a laminate, where the whole point is that some directions are stiff and others
    /// are free.
    ///
    /// The directions are local. Local x runs from the first node to the second - so direction 1 is
    /// along the link and 2 and 3 across it - unless an <see cref="Orient"/> plane says otherwise.
    ///
    /// Both ends have to be nodes the model already has. A link joins parts of a model together; it
    /// does not bring nodes into being, and a point of its own that no element reaches is an error
    /// rather than a new node.
    ///
    /// It cannot ground a node, because both ends have to be apart: two coincident points are one
    /// node in Alpaca4d, every element arriving at a location sharing the node there. A support
    /// holds a node outright, and a support on a Plane holds it through a spring of its own.
    /// </summary>
    public partial class Link : IStructure, IElement, ILink, ISerialize
    {
        public Line Line { get; set; }
        public List<IUniaxialMaterial> Materials { get; set; }
        public List<int> Directions { get; set; }

        /// <summary>
        /// The local frame. Left invalid, it is built from the line: X along the link, Y across it.
        /// </summary>
        public Plane Orient { get; set; } = Plane.Unset;

        public ElementType Type => ElementType.Link;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public int? INode { get; set; }
        public int? JNode { get; set; }

        /// <summary>
        /// Degrees of freedom at the ends - 6 on a beam or shell node, 3 on a solid one. The
        /// smaller of the two, because a direction only exists at both ends or at neither.
        /// </summary>
        public int Ndf { get; set; } = 6;

        public Link(Line line, List<IUniaxialMaterial> materials, List<int> directions, Plane orient)
        {
            this.Line = line;
            this.Materials = materials;
            this.Directions = directions;
            this.Orient = orient;
        }

        public Link(Line line, List<IUniaxialMaterial> materials, List<int> directions)
            : this(line, materials, directions, Plane.Unset)
        {
        }

        /// <summary>The local x axis actually used, whether it was given or taken from the line.</summary>
        public Vector3d LocalX
        {
            get
            {
                Vector3d x, y;
                LinkFrame.Axes(this.Orient, this.Line.To - this.Line.From, out x, out y);
                return x;
            }
        }

        /// <summary>The local y axis actually used.</summary>
        public Vector3d LocalY
        {
            get
            {
                Vector3d x, y;
                LinkFrame.Axes(this.Orient, this.Line.To - this.Line.From, out x, out y);
                return y;
            }
        }

        public void SetTags()
        {
        }

        public void SetTopologyRTree(Model model)
        {
            int atStart, atEnd;
            this.INode = model.NodeTagAt(this.Line.From, out atStart, "A link");
            this.JNode = model.NodeTagAt(this.Line.To, out atEnd, "A link");
            this.Ndf = Math.Min(atStart, atEnd);
        }

        public string WriteTcl()
        {
            LinkFrame.Check(this.Materials, this.Directions, "A link");
            LinkFrame.CheckAgainstNdf(this.Directions, this.Ndf, "A link");

            if (this.Line.Length <= 0.0)
                throw new Exception("A link joins two different points; this one has both ends in the same " +
                                    "place. Alpaca4d puts one node at each point, so there is nothing " +
                                    "there to join - move one end onto the other node you meant.");

            var axis = this.Line.To - this.Line.From;
            Vector3d x, y;
            LinkFrame.Axes(this.Orient, axis, out x, out y);

            var materials = string.Join(" ", this.Materials.Select(material => material.Id));
            var directions = string.Join(" ", this.Directions);

            // No -mass. A spring is a stiffness between two nodes and nothing else, and a bearing
            // heavy enough to weigh is a Mass Point at the node - which is the one place mass is
            // written from, converted once, and counted by Model.TotalMass.
            return $"element twoNodeLink {this.Id} {this.INode} {this.JNode}" +
                   $" -mat {materials}" +
                   $" -dir {directions}" +
                   $" -orient {LinkFrame.OrientAlong(x, y, axis)}\n";
        }

        public override string ToString()
        {
            return "<Class Link>";
        }
    }
}
