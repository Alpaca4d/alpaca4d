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
    /// <summary>
    /// Double L-section (double angle) with specified gap between the two angles.
    /// Can handle both equal and unequal angles.
    /// </summary>
    public partial class DoubleLAngleCS : ISerialize, IUniaxialSection
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public IUniaxialMaterial Material { get; set; }
        public string SectionName { get; set; }
        
        /// <summary>First leg dimension (height)</summary>
        public double Height { get; set; }
        
        /// <summary>Second leg dimension (width)</summary>
        public double Width { get; set; }
        
        /// <summary>Thickness of the angle</summary>
        public double Thickness { get; set; }
        
        /// <summary>Gap/padding between the two L-sections</summary>
        public double Gap { get; set; }
        
        public double Area
        {
            get
            {
                // Area of single L-section: (height * thickness) + (width * thickness) - (thickness^2 for corner overlap)
                double singleLArea = (this.Height * this.Thickness) + (this.Width * this.Thickness) - (this.Thickness * this.Thickness);
                // Two L-sections
                return 2 * singleLArea;
            }
        }
        
        public double AlphaY => 0.7; // Conservative shear area factor for angle sections
        public double AlphaZ => 0.7;
        
        public double Izz
        {
            get
            {
                // Moment of inertia about z-z axis (horizontal bending)
                // For double L-section with gap, considering parallel axis theorem
                double area = Rhino.Geometry.AreaMassProperties.Compute(this.Brep).Area;
                double izz = Rhino.Geometry.AreaMassProperties.Compute(this.Brep).CentroidCoordinatesMomentsOfInertia.X;
                return izz;
            }
        }

        public double Iyy
        {
            get
            {
                // Moment of inertia about y-y axis (vertical bending)
                double iyy = Rhino.Geometry.AreaMassProperties.Compute(this.Brep).CentroidCoordinatesMomentsOfInertia.Y;
                return iyy;
            }
        }

        public double J
        {
            get
            {
                // Torsional constant - approximate for angle sections
                // For thin-walled open sections: J ≈ (1/3) * Σ(b*t³)
                double j = (1.0 / 3.0) * ((this.Height * Math.Pow(this.Thickness, 3)) + 
                                          (this.Width * Math.Pow(this.Thickness, 3))) * 2; // times 2 for double angle
                return j;
            }
        }

        public List<Curve> Curves
        {
            get
            {
                var curves = new List<Curve>();
                var plane = Rhino.Geometry.Plane.WorldXY;
                
                // Create two L-sections symmetrically positioned with the gap
                double halfGap = this.Gap / 2.0;
                
                // First L-section (on the positive side)
                var l1Points = new List<Point3d>
                {
                    plane.PointAt(halfGap, -this.Height / 2),
                    plane.PointAt(halfGap, this.Height / 2),
                    plane.PointAt(halfGap + this.Thickness, this.Height / 2),
                    plane.PointAt(halfGap + this.Thickness, -this.Height / 2 + this.Thickness),
                    plane.PointAt(halfGap + this.Width, -this.Height / 2 + this.Thickness),
                    plane.PointAt(halfGap + this.Width, -this.Height / 2),
                    plane.PointAt(halfGap, -this.Height / 2)
                };
                curves.Add(new Rhino.Geometry.Polyline(l1Points).ToNurbsCurve());
                
                // Second L-section (on the negative side, mirrored)
                var l2Points = new List<Point3d>
                {
                    plane.PointAt(-halfGap, -this.Height / 2),
                    plane.PointAt(-halfGap, this.Height / 2),
                    plane.PointAt(-halfGap - this.Thickness, this.Height / 2),
                    plane.PointAt(-halfGap - this.Thickness, -this.Height / 2 + this.Thickness),
                    plane.PointAt(-halfGap - this.Width, -this.Height / 2 + this.Thickness),
                    plane.PointAt(-halfGap - this.Width, -this.Height / 2),
                    plane.PointAt(-halfGap, -this.Height / 2)
                };
                curves.Add(new Rhino.Geometry.Polyline(l2Points).ToNurbsCurve());
                
                return curves;
            }
        }

        public Brep Brep
        {
            get
            {
                double tolerance = 0.0001;
                
                // Get the two L-section curves
                var curves = this.Curves.ToList();
                
                // Create connecting rectangle between the two L-sections
                // Dimensions: (Gap + 0.1 * Thickness) width × Thickness * 0.1 (height)
                // Position: at the middle (y = 0)
                var plane = Rhino.Geometry.Plane.WorldXY;

                var rectPoints = new List<Point3d>
                {
                    plane.PointAt(-this.Gap / 2 - this.Thickness * 0.5, this.Thickness * 0.1),
                    plane.PointAt(-this.Gap / 2 - this.Thickness * 0.5, -this.Thickness * 0.1),
                    plane.PointAt(this.Gap / 2 + this.Thickness * 0.5, -this.Thickness * 0.1),
                    plane.PointAt(this.Gap / 2 + this.Thickness * 0.5, this.Thickness * 0.1),
                    plane.PointAt(-this.Gap / 2 - this.Thickness * 0.5, this.Thickness * 0.1) // Close the polyline
                };
                var connectingRectangle = new Rhino.Geometry.Polyline(rectPoints).ToNurbsCurve();
                curves.Add(connectingRectangle);
                
                // Use RegionUnion to combine all 3 curves into one unified curve
                var unionCurves = Curve.CreateBooleanUnion(curves, tolerance);
                
                if (unionCurves != null && unionCurves.Length > 0)
                {
                    // Create planar brep from the unified curve
                    var planarBreps = Brep.CreatePlanarBreps(unionCurves[0], tolerance);
                    if (planarBreps != null && planarBreps.Length > 0)
                    {
                        return planarBreps[0];
                    }
                }
                
                // Fallback: if union fails, try creating breps from individual curves and joining
                var breps = new List<Brep>();
                foreach (var curve in curves)
                {
                    if (curve != null && curve.IsClosed)
                    {
                        var planarBreps = Brep.CreatePlanarBreps(curve, tolerance);
                        if (planarBreps != null && planarBreps.Length > 0)
                        {
                            breps.Add(planarBreps[0]);
                        }
                    }
                }
                
                if (breps.Count > 0)
                {
                    var joinedBreps = Brep.JoinBreps(breps, tolerance);
                    if (joinedBreps != null && joinedBreps.Length > 0)
                    {
                        return joinedBreps[0];
                    }
                    return breps[0];
                }
                
                return null;
            }
        }

        public DoubleLAngleCS(string secName, double height, double width, double thickness, double gap, IUniaxialMaterial material)
        {
            this.SectionName = secName;
            this.Height = height;
            this.Width = width;
            this.Thickness = thickness;
            this.Gap = gap;
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
