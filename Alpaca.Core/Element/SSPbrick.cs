using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d.Generic;
using Rhino.Geometry;

namespace Alpaca4d.Element
{
    public partial class SSPbrick : ISerialize, IBrick, IStructure
    {
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public Mesh Mesh { get; set; }
        public IMultiDimensionMaterial Material { get; set; }
        public ElementType Type => ElementType.Brick;
        public ElementClass ElementClass => ElementClass.SSPBrick;
        public List<int?> IndexNodes { get; set; }
        public double? BodyForce { get; set; }
        public int Ndf => 3;
        public Color Color { get; set; } = Alpaca4d.Colors.DefaultBrick;

        public SSPbrick(Mesh mesh, IMultiDimensionMaterial material)
        {
            if (mesh.Vertices.Count != 8)
                throw new Exception("Mesh vertices must be 8!");
            this.Mesh = mesh;
            this.Material = material;
        }

        /// <summary>
        /// The element's own axes. The mesh carries its vertices in OpenSees node order - that is
        /// what Utils.CleanHexahedron leaves behind - so the numbering the frame is read off is
        /// the numbering the solver was given.
        /// </summary>
        public Plane LocalPlane => Alpaca4d.Utils.SolidAxes(this.Mesh.Vertices.ToPoint3dArray());

        public void SetTags()
        {

        }

        /// <summary>
        /// The eight nodes this brick sits on, in the order the mesh carries its vertices - which
        /// is the order OpenSees wants, because Utils.CleanHexahedron put them in it.
        ///
        /// This used to append every hit of every search to one flat list, so a tolerance wide
        /// enough to catch two nodes at one vertex produced nine indexes for eight vertices and
        /// WriteTcl read the first eight of them: a brick quietly attached to the wrong nodes.
        /// RTreeSearch returns exactly one index per vertex and says so when a vertex finds none.
        /// </summary>
        public void SetTopologyRTree(Alpaca4d.Model model)
        {
            var meshPoints = this.Mesh.Vertices.ToPoint3dArray();

            this.IndexNodes = Alpaca4d.Utils
                .RTreeSearch(model.RTreeCloudPointThreeNDF, meshPoints, model.Tollerance,
                             $"SSP Brick {this.Id}", model.UniquePointsThreeNDF)
                .Select(x => (int?)(x + 1))
                .ToList();
        }

        public string WriteTcl()
        {
            string tcl = $"element SSPbrick {this.Id} {this.IndexNodes[0]} {this.IndexNodes[1]} {this.IndexNodes[2]} {this.IndexNodes[3]} {this.IndexNodes[4]} {this.IndexNodes[5]} {this.IndexNodes[6]} {this.IndexNodes[7]} {this.Material.Id} {this.BodyForce} {this.BodyForce} {this.BodyForce}\n";
            return tcl;
        }

    }
}
