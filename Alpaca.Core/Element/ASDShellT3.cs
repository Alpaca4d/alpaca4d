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
    public partial class ASDShellT3 : ISerialize, IShell, IElement, IStructure
    {
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public Mesh Mesh { get; set; }
        public IMultiDimensionSection Section { get; set; }
        public ElementType Type => ElementType.Shell;
        public ElementClass ElementClass => ElementClass.ASDShellT3;
        public List<int?> IndexNodes { get; set; }
        public int Ndf => 6;
        public Color Color { get; set; } = Alpaca4d.Colors.DefaultShell;
        public bool IsCorotational { get; set; } = false;
        public Vector3d LocalX { get; set; }

        public ASDShellT3(Mesh mesh, IMultiDimensionSection section,  Vector3d localX = default, bool isCorotational = false)
        {
            this.Mesh = mesh;
            this.Section = section;
            this.LocalX = localX;
            this.IsCorotational = isCorotational;
        }

        public void SetTags()
        {

        }
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
            string corotationalFlag = this.IsCorotational ? "-corotational" : string.Empty;
            string localXString = this.LocalX == default ? string.Empty : $"-local {this.LocalX.X} {this.LocalX.Y} {this.LocalX.Z}";
            string tcl = $"element ASDShellT3 {this.Id} {this.IndexNodes[0]} {this.IndexNodes[1]} {this.IndexNodes[2]} {this.Section.Id} {corotationalFlag} {localXString}\n";

            return tcl;
        }
    }
}
