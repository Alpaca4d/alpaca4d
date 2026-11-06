using System;
using Rhino.Geometry;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d.Generic;

namespace Alpaca4d.Element
{
    public partial class Node : IStructure
    {
        public Point3d Pos { get; set; }
        public int? Id { get; set; }
        public int Ndf { get; set; }

        public Node()
        {

        }

        public Node(Point3d pos)
        {
            this.Pos = pos;
        }

        public Node(int id, double x, double y, double z)
        {
            this.Id = id;
            this.Pos = new Rhino.Geometry.Point3d(x, y, z);
        }

        public void SetNodeTag(Model model)
        {
            if (this.Ndf == 3)
                this.Id = Alpaca4d.Utils.NodeSearch(model.NodeIndexThreeNDF, new[] { this.Pos })[0] + 1;
            else // ndf == 6
                this.Id = Alpaca4d.Utils.NodeSearch(model.NodeIndexSixNDF, new[] { this.Pos })[0] + 1 + model.UniquePointsThreeNDF.Count;
        }

        /// <summary>
        /// A node carries no mass of its own.
        ///
        /// Concentrated mass belongs to <see cref="Alpaca4d.Loads.MassLoad"/>, which writes a
        /// "mass" command, converts kg to the solver's mass unit, keeps the translational and
        /// rotational terms apart, and is counted by <see cref="Model.TotalMass"/>. This used
        /// to write "-mass" here as well, from a single double applied to every degree of
        /// freedom - one number standing in for both a mass and a mass moment of inertia, with
        /// no unit conversion. It was never assigned, and could not be: the nodes that reach a
        /// deck are built inside CreateNodes from the model's own points.
        ///
        /// It could not have worked even if it had been. Both "node -mass" and "mass" land on
        /// Node::setMass, which assigns rather than accumulates, and Alpaca4d writes every node
        /// before any mass - so a MassLoad at the same node overwrote it silently.
        ///
        /// Omitting "-mass" is equivalent to writing zeros: Node::getMass returns a zeroed
        /// matrix when none was set.
        /// </summary>
        public string WriteTcl()
        {
            if (this.Ndf != 3 && this.Ndf != 6)
                throw new Exception($"No ndf has been assigned");

            return $"node {this.Id} {this.Pos.X} {this.Pos.Y} {this.Pos.Z}\n";
        }
    }
}