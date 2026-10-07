using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d.Core.Utils;
using Alpaca4d.Generic;

namespace Alpaca4d.Material
{
    public partial class ReinforcingSteel : EntityBase, IMaterial
    {
        public string MatName { get; set; }
        public int? Id { get; set; } = IdGenerator.GenerateId();
        /// <summary>Yield strength, in kN/m2.</summary>
        public double Fy { get; set; }
        /// <summary>Ultimate strength, in kN/m2.</summary>
        public double Fu { get; set; }
        /// <summary>Initial elastic modulus, in kN/m2.</summary>
        public double Es { get; set; }
        /// <summary>
        /// Tangent modulus at the onset of strain hardening, in kN/m2. Greater than
        /// zero: OpenSees divides by it while fitting the hardening curve.
        /// </summary>
        public double Esh { get; set; }
        /// <summary>
        /// Strain at the onset of strain hardening, dimensionless. Past yield -
        /// hardening cannot begin before the bar has yielded.
        /// </summary>
        public double EpsilonSh { get; set; }
        /// <summary>Strain at peak stress, dimensionless.</summary>
        public double EpsilonUlt { get; set; }
        public MinMax MinMax { get; set; }
        public string MaterialDimension => "UniaxialMaterial";
        public double? Rho { get; set; }


        /// <summary>
        /// B450C, in Alpaca4d's units - kN and m, so strengths in kN/m2. What the fibre
        /// components fall back to when no reinforcement is wired to them.
        /// </summary>
        public static ReinforcingSteel B450C
        {
            get
            {
                return new ReinforcingSteel(
                    "B450C",
                    fy: 450000.0,
                    fu: 540000.0,
                    es: 200000000.0,
                    esh: 2000000.0,
                    epislonSh: 0.008,
                    epsilponUlt: 0.075,
                    isMinMax: false);
            }
        }

        public ReinforcingSteel(string matName, double fy, double fu, double es, double esh, double epislonSh, double epsilponUlt, bool isMinMax)
        {
            this.MatName = matName;
            this.Fy = fy;
            this.Fu = fu;
            this.Es = es;
            this.Esh = esh;
            this.EpsilonSh = epislonSh;
            this.EpsilonUlt = epsilponUlt;
            if (isMinMax == true)
                this.MinMax = new Alpaca4d.Material.MinMax(matName, this, -this.EpsilonUlt, this.EpsilonUlt);
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
            string tcl = $"uniaxialMaterial ReinforcingSteel {this.Id} {TclNumber.Write(this.Fy)} {TclNumber.Write(this.Fu)} {TclNumber.Write(this.Es)} {TclNumber.Write(this.Esh)} {TclNumber.Write(this.EpsilonSh)} {TclNumber.Write(this.EpsilonUlt)}\n";
            if (this.MinMax != null)
                tcl += this.MinMax.WriteTcl();
            return tcl;
        }
    }
}
