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
    public partial class Tetrahedron : ISerialize, IBrick, IElement, IStructure
    {
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public Mesh Mesh { get; set; }
        public IMultiDimensionMaterial Material { get; set; }
        public ElementType Type => ElementType.Brick;
        public ElementClass ElementClass => ElementClass.FourNodeTetrahedron;
        public List<int?> IndexNodes { get; set; }
        public double? BodyForce { get; set; }
        public Color Color { get; set; } = Alpaca4d.Colors.DefaultBrick;
        public int Ndf => 3;


        public Tetrahedron(Mesh mesh, IMultiDimensionMaterial material)
        {
            if (mesh.Vertices.Count != 4)
                throw new Exception("Mesh vertices must be 4!");
            this.Mesh = mesh;
            this.Material = material;
        }

        /// <summary>
        /// The element's own axes. The mesh carries its vertices in OpenSees node order - that is
        /// what Utils.CleanTetrahedron leaves behind - so the numbering the frame is read off is
        /// the numbering the solver was given.
        /// </summary>
        public Plane LocalPlane => Alpaca4d.Utils.SolidAxes(this.Mesh.Vertices.ToPoint3dArray());

        public void SetTags()
        {

        }

        /// <summary>
        /// The four nodes this tetrahedron sits on, in the order the mesh carries its vertices -
        /// which is the order OpenSees wants, because Utils.CleanTetrahedron put them in it.
        /// Exactly one node per vertex; see <see cref="SSPbrick.SetTopologyRTree"/> for why that
        /// is worth insisting on.
        /// </summary>
        public void SetTopologyRTree(Alpaca4d.Model model)
        {
            var meshPoints = this.Mesh.Vertices.ToPoint3dArray();

            this.IndexNodes = Alpaca4d.Utils
                .RTreeSearch(model.RTreeCloudPointThreeNDF, meshPoints, model.Tollerance,
                             $"Four Node Tetrahedron {this.Id}", model.UniquePointsThreeNDF)
                .Select(x => (int?)(x + 1))
                .ToList();
        }

        public string WriteTcl()
        {
            string tcl = $"element FourNodeTetrahedron {this.Id} {this.IndexNodes[0]} {this.IndexNodes[1]} {this.IndexNodes[2]} {this.IndexNodes[3]} {this.Material.Id} {this.BodyForce} {this.BodyForce} {this.BodyForce}\n";
            return tcl;
        }
    }
}
