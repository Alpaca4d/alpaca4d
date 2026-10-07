using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d.Core.Utils;
using Alpaca4d.Generic;

namespace Alpaca4d.Material
{
    public partial class ElasticIsotropicMaterial : EntityBase, IMultiDimensionMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public double E { get; set; }
        public double G { get; set; }
        public double Nu { get; set; }
        public double? Rho { get; set; }

        public string MaterialDimension => "nDMaterial";
        public string MaterialType => "Elastic";

        public ElasticIsotropicMaterial(string matName, double e, double g, double v, double? rho = null)
        {
            this.MatName = matName;
            this.E = e;
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
            string tclText = $"nDMaterial ElasticIsotropic {this.Id} {this.E} {this.Nu} {Alpaca4d.ModelMass.FromKg(this.Rho)}\n";
            return tclText;
        }

    }
}
