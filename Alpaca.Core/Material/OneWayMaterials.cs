using System;
using Alpaca4d.Generic;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Material
{
    /// <summary>
    /// OpenSees' ENT, elastic with no tension: stiff in compression, nothing in tension, and stiff at
    /// zero strain - which is where Newton starts. Alpaca4d writes it for a compression-only spring
    /// support; it is not a material the user picks.
    /// </summary>
    public partial class UniaxialMaterialENT : EntityBase, IMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public System.Drawing.Color? Color { get; set; }
        public double? Rho { get; set; }
        public double E { get; set; }

        public UniaxialMaterialENT(double e)
        {
            this.E = e;
        }

        public override string WriteTcl()
        {
            return $"uniaxialMaterial ENT {this.Id} {TclNumber.Write(this.E)}\n";
        }
    }

    /// <summary>
    /// OpenSees' ElasticPPGap: nothing until the gap closes, then elastic up to a force. Alpaca4d writes
    /// it for a compression-only spring support with a gap, with the force as large as a double
    /// allows, so the spring never yields.
    ///
    /// Signed the way OpenSees reads it: a gap that closes in compression has a negative gap and a
    /// negative yield force.
    /// </summary>
    public partial class UniaxialMaterialElasticPPGap : EntityBase, IMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public System.Drawing.Color? Color { get; set; }
        public double? Rho { get; set; }
        public double E { get; set; }
        public double Fy { get; set; }
        public double Gap { get; set; }

        public UniaxialMaterialElasticPPGap(double e, double fy, double gap)
        {
            this.E = e;
            this.Fy = fy;
            this.Gap = gap;
        }

        public override string WriteTcl()
        {
            return $"uniaxialMaterial ElasticPPGap {this.Id} {TclNumber.Write(this.E)} {TclNumber.Write(this.Fy)} {TclNumber.Write(this.Gap)}\n";
        }
    }
}
