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
    public partial class CircleCS : ISerialize, IUniaxialSection
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public IUniaxialMaterial Material { get; set; }
        public string SectionName { get; set; }
        public double Diameter { get; set; }
        public double Thickness { get; set; }
        public double Area
        {
            get
            {
                double area;
                if (this.Thickness == 0 || this.Thickness == this.Diameter / 2)
                    area = Math.Pow(this.Diameter, 2) / 4 * Math.PI;
                else if (this.Thickness < this.Diameter / 2 && this.Thickness >= 0.00)
                    area = (Math.Pow(this.Diameter, 2) - Math.Pow(this.Diameter - 2 * this.Thickness, 2)) / 4 * Math.PI;
                else
                    throw new Exception("Thickness needs to be smaller than D/2");
                return area;
            }
        }
        public double AlphaY => 9.0 / 10;
        public double AlphaZ => 9.0 / 10;
        public double Iyy
        {
            get
            {
                double iYY;
                if (this.Thickness == 0 || this.Thickness == this.Diameter / 2)
                    iYY = Math.Pow(this.Diameter, 4) / 64 * Math.PI;
                else if (this.Thickness < this.Diameter / 2 && this.Thickness >= 0.00)
                    iYY = (Math.Pow(this.Diameter, 4) - Math.Pow(this.Diameter - 2 * this.Thickness, 4)) / 64 * Math.PI;
                else
                    throw new Exception("Thickness needs to be smaller than D/2");
                return iYY;
            }
        }

        public double Izz
        {
            get
            {
                return this.Iyy;
            }
        }

        public double J
        {
            get
            {
                double j;
                if (this.Thickness == 0 || this.Thickness == this.Diameter / 2)
                    j = Math.Pow(this.Diameter, 4) / 32 * Math.PI;
                else if (this.Thickness < this.Diameter / 2 && this.Thickness >= 0.00)
                    j = (Math.Pow(this.Diameter, 4) - Math.Pow(this.Diameter - 2 * this.Thickness, 4)) / 32 * Math.PI;
                else
                    throw new Exception("Thickness needs to be smaller than D/2");
                return j;
            }
        }

        public List<Curve> Curves
        {
            get
            {
                var curves = new List<Curve>();
                if (this.Thickness == 0 || this.Thickness == this.Diameter / 2)
                {
                    var circle = new Rhino.Geometry.Circle(Diameter / 2).ToNurbsCurve();
                    curves.Add(circle);
                }
                else if (this.Thickness < this.Diameter / 2 && this.Thickness >= 0.00)
                {
                    var outerCircle = new Rhino.Geometry.Circle(this.Diameter / 2).ToNurbsCurve();
                    var innerCircle = new Rhino.Geometry.Circle(this.Diameter / 2 - this.Thickness).ToNurbsCurve();
                    curves.Add(outerCircle);
                    curves.Add(innerCircle);
                }
                else
                    throw new Exception("Thickness needs to be smaller than D/2");
                return curves;
            }
        }

        public Brep Brep
        {
            get
            {
                return Rhino.Geometry.Brep.CreatePlanarBreps(this.Curves, 0.001)[0];
            }
        }


        public CircleCS(string secName, double diameter, double thickness, IUniaxialMaterial material)
        {
            this.SectionName = secName;
            this.Diameter = diameter;
            this.Thickness = thickness;
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
