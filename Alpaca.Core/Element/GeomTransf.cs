using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

using Alpaca4d.Generic;
using Rhino.Geometry;

namespace Alpaca4d.Element
{
    public partial class GeomTransf : EntityBase, ISerialize
    {
        public GeomTransfType Type { get; set; }
        public Curve Line { get; set; }
        public int? Id { get; set; }
        public Vector3d LocalZ { get; set; }
        public Vector3d LocalY
        {
            get
            {
                Vector3d lineVector = new Rhino.Geometry.Vector3d(Line.PointAtEnd - Line.PointAtStart);
                lineVector.Unitize();
                return Vector3d.CrossProduct(LocalZ, lineVector);
            }
        }

        // Constructor

        public GeomTransf()
        {
        }


        /// <summary>
        /// <paramref name="refVector"/> is OpenSees's vecxz: any vector in the beam's local x-z
        /// plane. It is kept squared off the beam and of unit length, which is the local z axis
        /// itself, so everything that reads LocalZ - the axes Model View draws, a section stood up
        /// on the beam, a load resolved onto it - uses the axes the solver does. A file read from
        /// Tcl commonly gives vecxz = (0, 0, 1) for a sloping member, and taken as it was, the
        /// local z drawn leaned along the beam and local y came out short. Squaring it off does
        /// not change the plane, so what OpenSees builds from it is the same.
        /// </summary>
        public GeomTransf(GeomTransfType type, Curve line, Vector3d refVector)
        {
            this.Type = type;
            this.Line = line;
            this.LocalZ = refVector;

            if (line != null)
            {
                var x = line.PointAtEnd - line.PointAtStart;
                var z = refVector;
                if (x.Unitize())
                {
                    z -= x * (z * x);
                    if (z.Unitize())
                        this.LocalZ = z;
                }
            }
        }

        public override string WriteTcl()
        {
            string tclText = $"geomTransf {Type} {Id} {TclNumber.Write(LocalZ.X)} {TclNumber.Write(LocalZ.Y)} {TclNumber.Write(LocalZ.Z)}\n";
            return tclText;
        }
    }

    public enum GeomTransfType
    {
        Linear,
        PDelta,
        Corotational
    }
}
