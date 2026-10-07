using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d.Core.Utils;
using Alpaca4d.Generic;

namespace Alpaca4d.Material
{
    public partial class Steel01 : EntityBase, IMaterial
    {
        public string MatName { get; set; }
        public int? Id { get; set; } = IdGenerator.GenerateId();
        /// <summary>Yield strength, in kN/m2.</summary>
        public double Fy { get; set; }
        /// <summary>Initial elastic modulus, in kN/m2.</summary>
        public double E0 { get; set; }
        /// <summary>Strain-hardening ratio, dimensionless: the post-yield tangent over E0.</summary>
        public double b { get; set; }
        public double? a1 { get; set; }
        public double? a2 { get; set; }
        public double? a3 { get; set; }
        public double? a4 { get; set; }
        public MinMax MinMax { get; set; }
        public string MaterialDimension => "UniaxialMaterial";
        public double? Rho { get; set; }

        /// <summary>
        /// S355, in Alpaca4d's units - kN and m, so strengths in kN/m2.
        /// </summary>
        public static Steel01 S355
        {
            get { return new Steel01("S355", 355000.0, 210000000.0, 0.01); }
        }

        public Steel01(string matName, double fy, double e0, double b, double? a1 = null, double? a2 = null, double? a3 = null, double? a4 = null)
        {
            this.MatName = matName;
            this.Fy = fy;
            this.E0 = e0;
            this.b = b;
            this.a1 = a1;
            this.a2 = a2;
            this.a3 = a3;
            this.a4 = a4;
        }

        /// <summary>
        /// The material's name - what a Panel shows, so a list out of the Material Library reads
        /// as the grades it holds, in order. A material with no name shows its Tcl, as before.
        /// </summary>
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(this.MatName) ? base.ToString() : this.MatName;
        }

        public override string WriteTcl()
        {
            string tcl = $"uniaxialMaterial Steel01 {this.Id} {TclNumber.Write(this.Fy)} {TclNumber.Write(this.E0)} {TclNumber.Write(this.b)}";

            // The isotropic hardening parameters are one optional group of four, so they
            // go in together or not at all. Interpolating them one by one wrote a1 as a2
            // and left a blank where a partly filled set should have been an error.
            if (this.a1.HasValue || this.a2.HasValue || this.a3.HasValue || this.a4.HasValue)
            {
                if (!(this.a1.HasValue && this.a2.HasValue && this.a3.HasValue && this.a4.HasValue))
                    throw new InvalidOperationException(
                        "Steel01 takes a1, a2, a3 and a4 together or not at all; " +
                        (this.MatName ?? "the material") + " has only some of them.");

                tcl += $" {TclNumber.Write(this.a1)} {TclNumber.Write(this.a2)} {TclNumber.Write(this.a3)} {TclNumber.Write(this.a4)}";
            }

            tcl += "\n";

            if (this.MinMax != null)
                tcl += this.MinMax.WriteTcl();

            return tcl;
        }
    }
}
