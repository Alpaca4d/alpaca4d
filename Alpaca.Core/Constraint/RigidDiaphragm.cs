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
    public partial class RigidDiaphragm : EntityBase, IConstraint, IStructure, ISerialize
    {
        public int MasterNodeId { get; set; }
        public Rhino.Geometry.Point3d? MasterNode { get; set; } = new Point3d();
        public List<int> SlaveNodeId { get; set; }
        public List<Rhino.Geometry.Point3d> SlaveNodes { get; set; }
        public int Dir { get; set; }
        public ConstraintType ConstraintType => ConstraintType.RigidDiaphgram;


        public void SetTopologyRTree(Model model)
        {
            // to add the threends start counting
            // Counting from zero for the cloud index, from one for the node tag, and past every
            // three degree of freedom node because those are written first.
            this.SlaveNodeId = Alpaca4d.Utils.RTreeSearch(model.RTreeCloudPointSixNDF, SlaveNodes, model.Tollerance).Select(x => x + 1 + model.UniquePointsThreeNDF.Count).ToList();
            this.MasterNodeId = Alpaca4d.Utils.RTreeSearch(model.RTreeCloudPointSixNDF, new List<Point3d> { (Point3d)MasterNode }, model.Tollerance).Select(x => x + 1 + model.UniquePointsThreeNDF.Count).First();
        }


        public RigidDiaphragm(List<Point3d> slaveNodes, Point3d? masterNode = null, int dir = 3)
        {
            this.SlaveNodes = slaveNodes;

            if(masterNode == null)
            {
                // Set MasterNode as Average of points
                this.MasterNode = new Point3d(0, 0, 0);
                foreach(var node in slaveNodes)
                {
                    this.MasterNode += node;
                }
                this.MasterNode /= slaveNodes.Count;
            }
            else
            {
                this.MasterNode = masterNode;
            }

            this.Dir = dir;
        }

        public override string WriteTcl()
        {
            var slaveIds = string.Join(" ", this.SlaveNodeId);
            return $"rigidDiaphragm {this.Dir} {this.MasterNodeId} {slaveIds}\n";
        }
    }
}
