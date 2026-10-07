using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d.Core.Utils;
using Alpaca4d.Generic;

namespace Alpaca4d.Material
{
    public partial class Concrete01 : EntityBase, IMaterial
    {
        /// <summary>
        /// Integer tag identifying material 
        /// </summary>
        public int? Id { get; set; } = IdGenerator.GenerateId();
        /// <summary>
        /// Concrete compressive strength at 28 days, in kN/m2.
        ///
        /// Compression is negative, as everywhere in Concrete01 - a positive strength
        /// describes a material that carries nothing in compression, and every fibre
        /// fails on the first step.
        /// </summary>
        public double FpCo { get; set; }
        /// <summary>
        /// Concrete crushing strength, in kN/m2. Negative, and no larger in magnitude
        /// than <see cref="FpCo"/>: this is what is left at <see cref="EpsilonCu"/>,
        /// past the peak.
        /// </summary>
        public double FpCu { get; set; }
        /// <summary>
        /// Concrete strain at maximum strength. Negative, dimensionless.
        /// </summary>
        public double EpsilonCo { get; set; }
        /// <summary>
        /// Concrete strain at crushing strength. Negative, dimensionless.
        /// </summary>
        public double EpsilonCu { get; set; }
        public MinMax MinMax { get; set; }
        public string MatName { get; set; }
        public string MaterialDimension => "UniaxialMaterial";
        public double? Rho { get; set; }


        /// <summary>
        /// C25/30, in Alpaca4d's units - kN and m, so strengths in kN/m2 - with the
        /// strain limits Eurocode 2 gives for a parabola-rectangle diagram. What the
        /// fibre components fall back to when no concrete is wired to them.
        /// </summary>
        public static Concrete01 C2530
        {
            get { return new Concrete01("C25/30", -25000.0, -20000.0, -0.002, -0.0035, false); }
        }

        public Concrete01(string matName, double fpco, double fpcu, double epsilonCo, double epsilonCu, bool isMinMax)
        {
            this.MatName = matName;
            this.FpCo = fpco;
            this.FpCu = fpcu;
            this.EpsilonCo = epsilonCo;
            this.EpsilonCu = epsilonCu;
            if (isMinMax == true)
                this.MinMax = new Alpaca4d.Material.MinMax(matName, this, this.EpsilonCu, 0.0);
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
            string tcl = $"uniaxialMaterial Concrete01 {this.Id} {TclNumber.Write(this.FpCo)} {TclNumber.Write(this.EpsilonCo)} {TclNumber.Write(this.FpCu)} {TclNumber.Write(this.EpsilonCu)}\n";
            if (this.MinMax != null)
                tcl += this.MinMax.WriteTcl();
            return tcl;
        }
    }
}
