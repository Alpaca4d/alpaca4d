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
    public partial class ISection : ISerialize, IUniaxialSection
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public IUniaxialMaterial Material { get; set; }
        public string SectionName { get; set; }
        public double TopWidth { get; set; }
        public double BottomWidth { get; set; }
        public double TopFlangeThickness { get; set; }
        public double BottomFlangeThickness { get; set; }
        public double Height { get; set; }
        public double Web { get; set; }
        public double Area
        {
            get
            {
                double area = Rhino.Geometry.AreaMassProperties.Compute(Brep).Area;
                return area;
            }
        }


        // Shear area factors, as OpenSees reads them: alphaY scales the shear stiffness for Vy and
        // alphaZ for Vz. The depth runs along local y, so Vy is carried by the web and Vz by the
        // flanges. These were the other way round, which left an I about four times too stiff in
        // shear about its strong axis - a 2 m HEA300 cantilever deflected 7.6 mm against 8.5 mm.
        public double AlphaY => ((this.Height - this.TopFlangeThickness - this.BottomFlangeThickness)*this.Web)/this.Area;
        public double AlphaZ => (this.TopWidth * this.TopFlangeThickness + this.BottomWidth * this.BottomFlangeThickness)/this.Area;
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
                return this.Iyy + this.Izz;
            }
        }

        public List<Curve> Curves
        {
            get
            {

                var plane = Rhino.Geometry.Plane.WorldXY;
                var p1 = plane.PointAt(this.Web/2,-this.Height/2+this.BottomFlangeThickness);
                var p2 = plane.PointAt(this.Web/2, this.Height / 2 - this.TopFlangeThickness);
                var p3 = plane.PointAt(this.TopWidth/2, this.Height / 2 - this.TopFlangeThickness);
                var p4 = plane.PointAt(this.TopWidth / 2, this.Height / 2);
                var p5 = plane.PointAt(-this.TopWidth / 2, this.Height / 2);
                var p6 = plane.PointAt(-this.TopWidth / 2, this.Height / 2 - this.TopFlangeThickness);
                var p7 = plane.PointAt(-this.Web / 2, this.Height / 2 - this.TopFlangeThickness);
                var p8 = plane.PointAt(-this.Web / 2, -this.Height / 2 + this.BottomFlangeThickness);
                var p9 = plane.PointAt(-this.BottomWidth/2, -this.Height / 2 + this.BottomFlangeThickness);
                var p10 = plane.PointAt(-this.BottomWidth/2, -this.Height / 2);
                var p11 = plane.PointAt(this.BottomWidth / 2, -this.Height / 2);
                var p12 = plane.PointAt(this.BottomWidth / 2, -this.Height / 2 + this.BottomFlangeThickness);

                var wireframe = new List<Point3d>() { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p1};
                var boundaryCurves = new List<Curve>() { new Rhino.Geometry.Polyline(wireframe).ToNurbsCurve() };

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


        public ISection(string secName, double height, double topWidth, double topFlangeThickness, double bottomWidth, double bottomFlangeThickness, double web, IUniaxialMaterial material)
        {
            this.SectionName = secName;
            this.Height = height;
            this.TopWidth = topWidth;
            this.TopFlangeThickness = topFlangeThickness;
            this.BottomWidth = bottomWidth;
            this.BottomFlangeThickness = bottomFlangeThickness;
            this.Web = web;
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

        public string WriteTcl()
        {
            string tclText = $"section Elastic {Id} {Material.E} {Area} {Izz} {Iyy} {Material.G} {J} {AlphaY} {AlphaZ}\n";
            return tclText;
        }
    }
}
