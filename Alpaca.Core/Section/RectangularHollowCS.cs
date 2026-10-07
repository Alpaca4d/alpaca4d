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
    public partial class RectangleHollowCS : ISerialize, IUniaxialSection
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public IUniaxialMaterial Material { get; set; }
        public string SectionName { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Web { get; set; }
        public double TopFlange { get; set; }
        public double BottomFlange { get; set; }
        public double Area
        {
            get
            {
                double area = Rhino.Geometry.AreaMassProperties.Compute(Brep).Area;
                return area;
            }
        }
        public double AlphaY => 9.0 / 10;
        public double AlphaZ => 9.0 / 10;
        public double Izz
        {
            get
            {
                double iZZ = Rhino.Geometry.AreaMassProperties.Compute(this.Brep).CentroidCoordinatesMomentsOfInertia.X;
                return iZZ;
            }
        }

        public double Iyy
        {
            get
            {
                double iYY = Rhino.Geometry.AreaMassProperties.Compute(this.Brep).CentroidCoordinatesMomentsOfInertia.Y;
                return iYY;
            }
        }

        public double J
        {
            get
            {
                if(this.Web <= this.Height / 2 && this.Web <= this.Width / 2)
                {
                    var h0 = 2 * ((this.Height - this.BottomFlange) + (this.Width - this.Web));
                    var Ah = (this.Height - this.BottomFlange) * (this.Width - this.Web);
                    var k = 2 * Ah * this.Web / h0;
                    var j = (Math.Pow(this.Web, 3) * h0 / 3) + 2 * k * Ah;
                    return j;
                }
                else
                    throw new Exception("Web must be less than Width/2 and Height/2 ");
            }
        }

        public List<Curve> Curves
        {
            get
            {
                var widthInterval = new Rhino.Geometry.Interval(-this.Width / 2, this.Width / 2);
                var heightInterval = new Rhino.Geometry.Interval(-this.Height / 2, this.Height / 2);
                var outerCurve = new Rhino.Geometry.Rectangle3d(Rhino.Geometry.Plane.WorldXY, widthInterval, heightInterval).ToNurbsCurve();


                widthInterval = new Rhino.Geometry.Interval(-(this.Width / 2 - this.Web), (this.Width / 2 - this.Web));
                heightInterval = new Rhino.Geometry.Interval(-(this.Height / 2 - this.BottomFlange), (this.Height / 2 - this.TopFlange));
                var innerCurve = new Rhino.Geometry.Rectangle3d(Rhino.Geometry.Plane.WorldXY, widthInterval, heightInterval).ToNurbsCurve();

                var boundaryCurves = new List<Curve>() { outerCurve, innerCurve };
                return boundaryCurves;
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


        public RectangleHollowCS(string secName, double width, double height, double web, double topFlange, double bottomFlange, IUniaxialMaterial material)
        {
            this.SectionName = secName;
            this.Width = width;
            this.Height = height;
            this.Web = web;
            this.BottomFlange = bottomFlange;
            this.TopFlange = topFlange;
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
