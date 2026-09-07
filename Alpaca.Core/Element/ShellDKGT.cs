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
    public partial class ShellDKGT : ISerialize, IShell, IElement, IStructure
    {
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public Mesh Mesh { get; set; }
        public IMultiDimensionSection Section { get; set; }
        public ElementType Type => ElementType.Shell;
        public ElementClass ElementClass => ElementClass.ShellDKGT;
        public List<int?> IndexNodes { get; set; }
        public int Ndf => 6;
        public Color Color { get; set; } = Alpaca4d.Colors.DefaultShell;
        public bool IsNonLinear { get; set; }

        public ShellDKGT(Mesh mesh, IMultiDimensionSection section, bool isNonLinear = false)
        {
            this.Mesh = mesh;
            this.Section = section;
            
            this.IsNonLinear = isNonLinear;
        }

        public void SetTags()
        {

        }
        /// <summary>
        /// The axes this shell's forces and stresses are reported in. A ShellDKGT takes no local
        /// axis of its own, so this is always the frame its node order gives.
        /// </summary>
        public Plane LocalPlane => Alpaca4d.Utils.ShellAxes(this.Mesh.Vertices.ToPoint3dArray(), Vector3d.Zero);

        public void SetTopologyRTree(Alpaca4d.Model model)
        {
            var tol = model.Tollerance;
            var meshPoints = this.Mesh.Vertices.ToList();

            var closestIndexes = new List<int?>();

            void SearchCallback(object sender, RTreeEventArgs e)
            {
                closestIndexes.Add(e.Id + 1);
            }

            foreach (var pt in meshPoints)
            {
                model.RTreeCloudPointSixNDF.Search(new Rhino.Geometry.Sphere(pt, tol), SearchCallback);
            }

            this.IndexNodes = closestIndexes.Select(x => x + model.UniquePointsThreeNDF.Count).ToList();
        }
        public string WriteTcl()
        {
            var elementType = IsNonLinear ? "ShellNLDKGT" : "ShellDKGT";
            string tcl = $"element {elementType} {this.Id} {this.IndexNodes[0]} {this.IndexNodes[1]} {this.IndexNodes[2]} {this.Section.Id}\n";

            return tcl;
        }
    }
}
