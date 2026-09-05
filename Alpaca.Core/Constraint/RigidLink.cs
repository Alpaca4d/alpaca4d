using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Rhino.Geometry;
using Alpaca4d.Element;
using Alpaca4d.Generic;

namespace Alpaca4d.Constraints
{
    public enum RigidLinkType
    {
        bar,
        beam
    }

    public partial class RigidLink : EntityBase, IConstraint, IStructure, ISerialize
    {
        public Rhino.Geometry.Point3d RetainedNode { get; set; }
        public Rhino.Geometry.Point3d ConstrainedNode { get; set; }
        public int RetainedNodeId { get; set; }
        public int ConstrainedNodeId { get; set; }
        public RigidLinkType Type { get; set; }
        public ConstraintType ConstraintType => ConstraintType.RigidLink;

        /// <summary>
        /// RTreeSearch hands back the index of the point in the cloud, counting from zero, while a
        /// node tag counts from one - and the six degree of freedom nodes are written after the
        /// three degree of freedom ones, so their tags start past that count. Both are needed:
        /// without the one the link lands on the node before the one it was drawn to.
        /// </summary>
        public void SetTopologyRTree(Model model)
        {
            this.RetainedNodeId = Alpaca4d.Utils.RTreeSearch(model.RTreeCloudPointSixNDF, new List<Point3d> { this.RetainedNode }, model.Tollerance)
                .Select(x => x + 1 + model.UniquePointsThreeNDF.Count)
                .First();
            this.ConstrainedNodeId = Alpaca4d.Utils.RTreeSearch(model.RTreeCloudPointSixNDF, new List<Point3d> { this.ConstrainedNode }, model.Tollerance)
                .Select(x => x + 1 + model.UniquePointsThreeNDF.Count)
                .First();

            // "rigidLink beam n n" is a FATAL out of the system of equations, and takes OpenSees
            // down with it. Culling makes it easy to reach: two points closer together than the
            // model tolerance are one node, and Alpaca4d never puts two nodes at one point.
            if (this.RetainedNodeId == this.ConstrainedNodeId)
                throw new Exception($"A Rigid Link has both ends on node {this.RetainedNodeId}, at " +
                                    $"{this.RetainedNode}. Alpaca4d puts one node at each point, so " +
                                    "two points closer together than the model tolerance are one " +
                                    "node and there is nothing to link. Move one end onto the other " +
                                    "node you meant.");
        }

        public RigidLink(Point3d retainedNode, Point3d constrainedNode, RigidLinkType type = RigidLinkType.beam)
        {
            this.RetainedNode = retainedNode;
            this.ConstrainedNode = constrainedNode;
            this.Type = type;
        }

        public override string WriteTcl()
        {
            return $"rigidLink {this.Type} {this.RetainedNodeId} {this.ConstrainedNodeId}\n";
        }
    }
}