using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Alpaca4d.Element;
using Rhino.Geometry;

namespace Alpaca4d.Generic
{
    public interface IBrick : IElement
    {
        public Mesh Mesh { get; set; }
        public ElementClass ElementClass { get; }
        public IMultiDimensionMaterial Material { get; set; }
        public List<int?> IndexNodes { get; set; }
        public Color Color { get; set; }

        /// <summary>
        /// The element's own axes, taken from the order its nodes are numbered in - see
        /// Utils.SolidAxes for the convention. A frame to report a stress in; it never reaches the
        /// solver, which runs every solid in the global axes.
        /// </summary>
        public Plane LocalPlane { get; }

    }
}