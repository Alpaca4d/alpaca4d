using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d.Generic;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Material
{
    public partial class ElasticOrthotropicMaterial : EntityBase, IMultiDimensionMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public System.Drawing.Color? Color { get; set; }
        public double E { get; set; } // it will be null
        public double Ex { get; set; }
        public double Ey { get; set; }
        public double Ez { get; set; }
        public double Gxy { get; set; }
        public double Gyz { get; set; }
        public double Gzx { get; set; }
        public double NuXy { get; set; }
        public double NuYz { get; set; }
        public double NuZx { get; set; }
        public double? Rho { get; set; }

        public string MaterialDimension => "nDMaterial";
        public string MaterialType => "Elastic";

        public ElasticOrthotropicMaterial(string matName, double ex, double ey, double ez, double gxy, double gyz, double gzx, double vxy, double vyz, double vzx, double? rho = null)
        {
            this.MatName = matName;
            this.Ex = ex;
            this.Ey = ey;
            this.Ez = ez;
            this.Gxy = gxy;
            this.Gyz = gyz;
            this.Gzx = gzx;
            this.NuXy = vxy;
            this.NuYz = vyz;
            this.NuZx = vzx;
            this.Rho = rho;
        }

        /// <summary>
        /// The material's name - what a Panel shows, so a list out of the Material Library reads
        /// as the grades it holds, in order. A material with no name shows its Tcl, as before.
        /// </summary>
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(this.MatName) ? base.ToString() : this.MatName;
        }

        /// <summary>
        /// The material of a joint band: a thin band of solids standing in for springs spread over a
        /// horizontal joint, its Ez the normal stiffness per area times its thickness and its Gxz, Gyz
        /// the shear stiffness per area times it - such as the printed cylinder example's Surface
        /// Connection makes between its layers. A comment before its line says so, for a buckling
        /// analysis to give the band the stress stiffness of a joint (see Buckling.JointMarker).
        /// </summary>
        public bool IsSurfaceConnection { get; set; }

        public override string WriteTcl()
        {
            string marker = this.IsSurfaceConnection ? Alpaca4d.Buckling.JointMarker + "\n" : "";
            string tclText = marker + $"nDMaterial ElasticOrthotropic {this.Id} {this.Ex} {this.Ey} {this.Ez} {this.NuXy} {this.NuYz} {this.NuZx} {this.Gxy} {this.Gyz} {this.Gzx} {Alpaca4d.ModelMass.FromKg(this.Rho)}\n";
            return tclText;
        }
    }
}
