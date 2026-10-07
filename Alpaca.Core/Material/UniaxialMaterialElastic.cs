using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Alpaca4d.Generic;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Material
{
    public partial class UniaxialMaterialElastic : EntityBase, IUniaxialMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public double E { get; set; }

        public double? Eneg { get; set; }
        public double Eta { get; set; }
        public double G { get; set; }
        public double Nu { get; set; }
        public double? Rho { get; set; }

        /// <summary>See <see cref="IUniaxialMaterial.Grade"/>. Material Library Elastic sets it.</summary>
        public IDesignGrade Grade { get; set; }

        public static UniaxialMaterialElastic Steel
        {
            get
            {
                double e = 210000000;
                double eNeg = 210000000;
                double eta = 0.00;
                double g = 90760000;
                double v = 0.3;
                double rho = 7850;
                return new UniaxialMaterialElastic(null, e, eNeg, eta, g, v, rho);
            }
        }

        public string MaterialDimension => "UniaxialMaterial";
        public string MaterialType => "Elastic";

        public UniaxialMaterialElastic(string matName, double e, double eNeg, double eta, double g, double v, double? rho = null)
        {
            this.MatName = matName;
            this.E = e;
            this.Eneg = eNeg;
            this.Eta = eta;
            this.G = g;
            this.Nu = v;
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

        public override string WriteTcl()
        {
            string tclText = $"uniaxialMaterial Elastic {this.Id} {this.E} {this.Eta} {this.Eneg}\n";
            return tclText;
        }
	}
}