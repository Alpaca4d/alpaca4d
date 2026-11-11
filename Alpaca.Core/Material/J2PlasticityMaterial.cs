using System;
using System.Linq;
using System.Text;
using Alpaca4d.Generic;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Material
{
    /// <summary>
    /// A metal that yields: OpenSees' J2Plasticity, von Mises plasticity with linear isotropic
    /// hardening. Elastic, with E and nu, until the von Mises stress reaches Fy; past it the stress
    /// stays on the yield surface, rising by H per unit of equivalent plastic strain. H = 0 is
    /// perfectly plastic.
    ///
    /// The same stress in tension and in compression, so steel and aluminium, not concrete,
    /// masonry or timber.
    ///
    /// It goes on solids and on shells. A ten node tetrahedron carries it best: on a clamped beam
    /// it matches beam theory before yield and levels off after it, even with two elements through
    /// the depth. An SSP brick is right up to first yield but too strong after it, because its
    /// hourglass stiffness is set once from the elastic tangent (SSPbrick::GetStab) and never
    /// softens: a third too strong on a coarse mesh. Assemble says so.
    ///
    /// On a shell it goes through a Layered Shell or a Plate Fiber section, whose layers carry each
    /// their own yield state. OpenSees' own plate form of J2Plasticity, J2PlateFiber, does not
    /// converge on a triangular ASD shell, and holds sigma_zz = 0 to an absolute 1e-8: so this
    /// material is also written as a PlateFromPlaneStress around J2Plasticity's plane stress form,
    /// under <see cref="PlateId"/>, and shell sections name that one (<see cref="ShellTag"/>). Its
    /// transverse shear stays elastic, with G. And ASD shells on it write -noeas -drillingNL
    /// (<see cref="IsIn"/>): with the enhanced assumed strains on, a plate yielding in its own plane
    /// stops converging just past first yield.
    ///
    /// Two things this material does not carry to OpenSees:
    ///   - Density. J2Plasticity takes none, so the elements made of it have no mass in OpenSees.
    ///     Gravity Load reads <see cref="Rho"/> itself, so self-weight is right; a natural vibration
    ///     or a time history is not, and Natural Vibration and Run Analysis say so.
    ///   - Units other than kN and m. J2Plasticity in N and m does not yield at all - a brick
    ///     pulled to three times Fy stays elastic - and Alpaca4d always writes kN and m.
    /// </summary>
    public partial class J2PlasticityMaterial : EntityBase, IMultiDimensionMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();

        /// <summary>The tag of the plate form a shell section names, written next to the material.</summary>
        public int? PlateId { get; set; } = IdGenerator.GenerateId();

        public string MatName { get; set; }
        public System.Drawing.Color? Color { get; set; }

        /// <summary>Young's modulus.</summary>
        public double E { get; set; }

        /// <summary>Poisson's ratio.</summary>
        public double Nu { get; set; }

        /// <summary>The von Mises stress the material yields at.</summary>
        public double Fy { get; set; }

        /// <summary>Linear isotropic hardening: how much the yield stress rises per unit of equivalent plastic strain.</summary>
        public double H { get; set; }

        /// <summary>Density in kg/m³. Read by Gravity Load; not written to OpenSees, which takes none for this material.</summary>
        public double? Rho { get; set; }

        /// <summary>Shear modulus, from E and nu.</summary>
        public double G => this.E / (2.0 * (1.0 + this.Nu));

        /// <summary>Bulk modulus, from E and nu. J2Plasticity takes K and G, not E and nu.</summary>
        public double K => this.E / (3.0 * (1.0 - 2.0 * this.Nu));

        public string MaterialDimension => "nDMaterial";
        public string MaterialType => "J2Plasticity";

        public J2PlasticityMaterial(string matName, double e, double nu, double fy, double h = 0.0, double? rho = null)
        {
            if (!(e > 0.0))
                throw new ArgumentException($"E must be greater than zero; it is {e}.");
            if (!(nu > -1.0 && nu < 0.5))
                throw new ArgumentException($"ν must be between -1 and 0.5; it is {nu}. At 0.5 the material would not change volume and has no bulk modulus.");
            if (!(fy > 0.0))
                throw new ArgumentException($"Fy must be greater than zero; it is {fy}.");
            if (!(h >= 0.0))
                throw new ArgumentException($"H cannot be negative; it is {h}. A material that softens as it yields has no unique answer on a mesh.");

            this.MatName = matName;
            this.E = e;
            this.Nu = nu;
            this.Fy = fy;
            this.H = h;
            this.Rho = rho;
        }

        /// <summary>
        /// Whether a shell section has a J2Plasticity material in any layer: ASD shells on it then
        /// write -noeas and -drillingNL, without which in-plane yielding stops converging.
        /// </summary>
        public static bool IsIn(IMultiDimensionSection section)
        {
            return section?.Materials?.Any(material => material is J2PlasticityMaterial) == true;
        }

        /// <summary>
        /// The tag a shell section names for <paramref name="material"/>: the plate form of a
        /// J2Plasticity material, the material itself for any other.
        /// </summary>
        public static int? ShellTag(IMultiDimensionMaterial material)
        {
            return material is J2PlasticityMaterial plastic ? plastic.PlateId : material?.Id;
        }

        /// <summary>
        /// The material's name - what a Panel shows. A material with no name shows its Tcl.
        /// </summary>
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(this.MatName) ? base.ToString() : this.MatName;
        }

        /// <summary>
        /// nDMaterial J2Plasticity $tag $K $G $sig0 $sigInf $delta $H, with sigInf = sig0 and
        /// delta = 0, so no saturation term: the hardening is H alone. Then the plate form for
        /// shells, and the density as a comment, for Deserialise to read back.
        /// </summary>
        public override string WriteTcl()
        {
            var tcl = new StringBuilder();
            tcl.Append($"nDMaterial J2Plasticity {this.Id} {TclNumber.Write(this.K)} {TclNumber.Write(this.G)} " +
                       $"{TclNumber.Write(this.Fy)} {TclNumber.Write(this.Fy)} 0.0 {TclNumber.Write(this.H)}\n");
            tcl.Append($"nDMaterial PlateFromPlaneStress {this.PlateId} {this.Id} {TclNumber.Write(this.G)}\n");
            if (this.Rho.HasValue)
                tcl.Append($"# alpaca:density {this.Id} {TclNumber.Write(this.Rho.Value)}\n");
            return tcl.ToString();
        }
    }
}
