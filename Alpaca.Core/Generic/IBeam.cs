using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Alpaca4d.Element;

using Rhino.Geometry;

namespace Alpaca4d.Generic
{
    public interface IBeam : IElement
    {
        public Curve Curve { get; set; }
        public GeomTransf GeomTransf { get; set; }
        public IUniaxialSection Section { get; set; }
        public IIntegration BeamIntegration { get; set; }
        public int? INode { get; set; }
        public int? JNode { get; set; }
        public System.Drawing.Color Color { get; set; }

        /// <summary>
        /// The same beam - curve, axes, integration, releases, ElementId - with another section. A new
        /// beam, this one left as it is: Grasshopper hands the same object to every component downstream.
        /// </summary>
        public IBeam WithSection(IUniaxialSection section);
    }
}