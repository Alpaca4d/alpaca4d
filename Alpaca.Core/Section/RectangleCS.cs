using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Rhino.Geometry;
using Alpaca4d.Generic;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Section
{
    public partial class RectangleCS : ISerialize, IUniaxialSection
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public IUniaxialMaterial Material { get; set; }
        public string SectionName { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Area
        {
            get
            {
                double area = this.Width * this.Height;
                return area;
            }
        }
        public double AlphaY => 5.0 / 6.0;
        public double AlphaZ => 5.0 / 6.0;
        public double Izz
        {
            get
            {
                double iZZ = this.Width * Math.Pow(this.Height, 3) / 12.0;
                return iZZ;
            }
        }

        public double Iyy
        {
            get
            {
                double iYY = Math.Pow(this.Width, 3) * this.Height / 12.0;
                return iYY;
            }
        }

        /// <summary>
        /// Torsion constant of a solid rectangle, J = k a b^3 with a the long side and b the
        /// short one, and k = 1 / (3 + 4.1 (b/a)^1.5).
        ///
        /// The exponent is 1.5, not 3/2 written as C# integer division - which is 1, and was
        /// what this used to compute. The two agree on a square, where the ratio is one and
        /// the exponent cannot matter, so the error stayed hidden there; on a 1:4 rectangle
        /// it made J about 12% too low, and it is worst near 1:2.5.
        /// </summary>
        public double J
        {
            get
            {
                const double exponent = 1.5;

                double j;
                double k;
                if(this.Height < this.Width)
                {
                    k = 1 / (3 + 4.1 * Math.Pow((this.Height / this.Width), exponent));
                    j = k * this.Width * Math.Pow(this.Height, 3);
                }
                else
                {
                    k = 1 / (3 + 4.1 * Math.Pow((this.Width / this.Height), exponent));
                    j = k * this.Height * Math.Pow(this.Width, 3);
                }
                return j;
            }
        }

        public List<Curve> Curves
        {
            get
            {
                var curves = new List<Curve>();
                var width = new Interval(-Width/2, Width/2);
                var heigth = new Interval(-Height/2, Height/2);
                curves.Add(new Rhino.Geometry.Rectangle3d(Rhino.Geometry.Plane.WorldXY, width, heigth).ToNurbsCurve());
                return curves;
            }
        }

        public Brep Brep
        {
            get
            {
                var planarBreps = Rhino.Geometry.Brep.CreatePlanarBreps(this.Curves, 0.001);
                if (planarBreps != null && planarBreps.Length > 0)
                {
                    return planarBreps[0];
                }
                return null;
            }
        }

        public RectangleCS(string secName, double width, double height, IUniaxialMaterial material)
        {
            this.SectionName = secName;
            this.Width = width;
            this.Height = height;
            this.Material = material;
        }

        public double GetAreaY()
        {
            return this.Area * this.AlphaY;
        }

        public double GetAreaZ()
        {
            return this.Area * this.AlphaZ;
        }

        /// <summary>
        /// The section's name - what a Panel shows, so a list out of the Section Library reads as
        /// the profiles it holds, in order.
        /// </summary>
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(this.SectionName) ? base.ToString() : this.SectionName;
        }

        public string WriteTcl()
        {
            string tclText = $"section Elastic {Id} {Material.E} {Area} {Izz} {Iyy} {Material.G} {J} {AlphaY} {AlphaZ}\n";
            return tclText;
        }
    }
}
