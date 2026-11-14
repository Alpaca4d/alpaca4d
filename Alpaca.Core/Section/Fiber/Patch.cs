using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Alpaca4d.Generic;
using Rhino.Geometry;

namespace Alpaca4d.Section
{
    public partial class Patch : ISerialize
    {
        public Mesh PatchGeometry { get; set; }
        public IMaterial Material { get; set; }
        public List<PointFiber> Fibers
        {
            get
            {
                var fibers = new List<PointFiber>();
                var meshes = Alpaca4d.Utils.ExplodeMesh(this.PatchGeometry);

                foreach (var meshFace in meshes)
                {
                    var areaProperty = Rhino.Geometry.AreaMassProperties.Compute(meshFace);

                    // A degenerate face - two coincident vertices, say - has no area
                    // properties at all, and Compute hands back null rather than throwing.
                    // This used to dereference it regardless, from inside the display
                    // pipeline, on every redraw. A fibre of no area contributes nothing.
                    if (areaProperty == null || areaProperty.Area <= 0.0)
                        continue;

                    var fiber = new PointFiber(areaProperty.Centroid, areaProperty.Area, this.Material);
                    fibers.Add(fiber);
                }
                return fibers;
            }
        }
        public Patch(Mesh geometry, IMaterial material)
        {
            this.PatchGeometry = geometry;
            this.Material = material;
        }

        public string WriteTcl()
        {
            return WriteTcl(0.0, 0.0);
        }

        /// <summary>The fibres, with <paramref name="dy"/> and <paramref name="dz"/> taken off their y and z.</summary>
        public string WriteTcl(double dy, double dz)
        {
            var sb = new StringBuilder();
            foreach (var fiber in this.Fibers)
                sb.Append(fiber.WriteTcl(dy, dz));
            return sb.ToString();
        }
    }
}
