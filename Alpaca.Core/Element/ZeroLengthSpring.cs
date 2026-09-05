using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Rhino.Geometry;
using Alpaca4d.Generic;

namespace Alpaca4d.Element
{
    /// <summary>
    /// A spring between a node of the model and the ground - OpenSees' <c>zeroLength</c>.
    ///
    /// An elastic support, in other words: a bearing pad, a soil spring, a foundation that gives.
    /// One uniaxial material per direction, and only the directions listed carry anything, so a
    /// spring with directions 1 and 2 alone leaves the node free in every other sense.
    ///
    /// The other end is a node of its own, in the same place, restrained outright. Alpaca4d never
    /// puts two nodes at one point - every element that reaches a location shares the node there -
    /// so the spring makes the second node itself, and nothing else can attach to it. That is why
    /// this element grounds a node rather than joining two: to join two, use <see cref="Link"/>.
    ///
    /// A support at the same point would fight the spring, and win: <c>fix</c> is a boundary
    /// condition, not a stiffness. Ground a node with one or the other, not both.
    /// </summary>
    public partial class ZeroLengthSpring : IStructure, IElement, ILink, ISerialize
    {
        public Point3d Pos { get; set; }
        public List<IUniaxialMaterial> Materials { get; set; }
        public List<int> Directions { get; set; }

        /// <summary>
        /// The local frame the directions count along. Left invalid it is the world frame, so 1, 2
        /// and 3 are the global X, Y and Z. A rotated plane is how an inclined bearing is written.
        /// </summary>
        public Plane Orient { get; set; } = Plane.Unset;

        public ElementType Type => ElementType.ZeroLength;
        public int? Id { get; set; }
        public string ElementId { get; set; }

        /// <summary>The node of the model the spring holds.</summary>
        public int? NodeId { get; set; }

        /// <summary>The restrained node the spring reacts against, made by this element.</summary>
        public int? GroundNodeId { get; set; }

        /// <summary>Degrees of freedom at the node - 6 on a beam or shell, 3 on a solid.</summary>
        public int Ndf { get; set; } = 6;

        /// <summary>
        /// The degrees of freedom the rest of the deck is being written with, so the builder can be
        /// put back after the ground node has been added under its own.
        /// </summary>
        private int _deckNdf = 6;

        public ZeroLengthSpring(Point3d pos, List<IUniaxialMaterial> materials, List<int> directions, Plane orient)
        {
            this.Pos = pos;
            this.Materials = materials;
            this.Directions = directions;
            this.Orient = orient;
        }

        public ZeroLengthSpring(Point3d pos, List<IUniaxialMaterial> materials, List<int> directions)
            : this(pos, materials, directions, Plane.Unset)
        {
        }

        /// <summary>The local x axis actually used, whether it was given or left to the world axes.</summary>
        public Vector3d LocalX
        {
            get { return LinkFrame.HasFrame(this.Orient) ? this.Orient.XAxis : Vector3d.XAxis; }
        }

        /// <summary>The local y axis actually used.</summary>
        public Vector3d LocalY
        {
            get { return LinkFrame.HasFrame(this.Orient) ? this.Orient.YAxis : Vector3d.YAxis; }
        }

        public void SetTags()
        {
        }

        public void SetTopologyRTree(Model model)
        {
            int ndf;
            this.NodeId = model.NodeTagAt(this.Pos, out ndf, "A zero length spring");
            this.Ndf = ndf;
            this._deckNdf = model.UniquePointsSixNDF.Count != 0 ? 6 : 3;
            this.GroundNodeId = model.NextNodeTag();
        }

        public string WriteTcl()
        {
            LinkFrame.Check(this.Materials, this.Directions, "A zero length spring");
            LinkFrame.CheckAgainstNdf(this.Directions, this.Ndf, "A zero length spring");

            var materials = string.Join(" ", this.Materials.Select(material => material.Id));
            var directions = string.Join(" ", this.Directions);

            var tcl = new StringBuilder();

            // The ground node is written here rather than with the rest of the nodes because it
            // only exists to give this element something to react against, and the builder has to
            // agree with it: the node block leaves the builder at 6 whenever the model has any
            // beams or shells, so a spring on a solid node needs it moved back and moved again.
            if (this.Ndf != this._deckNdf)
                tcl.Append(this.Ndf == 3 ? Model.Initialise3ndf() : Model.Initialise6ndf());

            tcl.Append($"node {this.GroundNodeId} {this.Pos.X} {this.Pos.Y} {this.Pos.Z}\n");
            tcl.Append(this.Ndf == 3
                ? $"fix {this.GroundNodeId} 1 1 1\n"
                : $"fix {this.GroundNodeId} 1 1 1 1 1 1\n");

            // Ground first, model node second, the same way a skewed support is written. The
            // element's force is then reported as the force the ground puts into the model.
            tcl.Append($"element zeroLength {this.Id} {this.GroundNodeId} {this.NodeId}" +
                       $" -mat {materials}" +
                       $" -dir {directions}" +
                       $" -orient {LinkFrame.Orient(this.LocalX, this.LocalY)}\n");

            if (this.Ndf != this._deckNdf)
                tcl.Append(this._deckNdf == 3 ? Model.Initialise3ndf() : Model.Initialise6ndf());

            return tcl.ToString();
        }

        public override string ToString()
        {
            return "<Class ZeroLengthSpring>";
        }
    }
}
