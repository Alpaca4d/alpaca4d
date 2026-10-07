using System;
using Alpaca4d.Generic;
using Alpaca4d.Core.Utils;

namespace Alpaca4d.Material
{
    /// <summary>
    /// uniaxialMaterial MinMax $matTag $otherTag &lt;-min $minStrain&gt; &lt;-max $maxStrain&gt;
    ///
    /// A wrapper that fails the material it wraps once the strain leaves a range. It is a
    /// material in its own right, with its own tag, and it is the wrapper's tag - not the
    /// wrapped material's - that a fibre has to name for the limits to apply; see
    /// <see cref="FiberTag"/>.
    /// </summary>
    public partial class MinMax : EntityBase
    {
        public string MatName { get; set; }
        public int? Id { get; set; } = IdGenerator.GenerateId();

        /// <summary>
        /// The material this wraps - <c>$otherTag</c> in the Tcl. Without it the wrapper
        /// used to write its own tag in both places, and OpenSees rejected the line:
        /// "WARNING invalid otherTag uniaxialMaterial MinMax".
        /// </summary>
        public IMaterial Wrapped { get; set; }

        public double MinStrain { get; set; }
        public double MaxStrain { get; set; }

        public MinMax(string matName, IMaterial wrapped, double minStrain, double maxStrain)
        {
            this.MatName = matName;
            this.Wrapped = wrapped;

            // Ordered, not taken as given: the two bounds are read off a material's own
            // properties by the callers, and which of those is the lower one depends on
            // the sign convention the properties were entered with. OpenSees accepts an
            // inverted range and then fails every fibre on the first step.
            this.MinStrain = Math.Min(minStrain, maxStrain);
            this.MaxStrain = Math.Max(minStrain, maxStrain);
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
            if (this.Wrapped == null)
                throw new InvalidOperationException(
                    "A MinMax material has no material to wrap; there is no tag to write as its otherTag.");

            return $"uniaxialMaterial MinMax {this.Id} {this.Wrapped.Id} " +
                   $"-min {TclNumber.Write(this.MinStrain)} -max {TclNumber.Write(this.MaxStrain)}\n";
        }

        /// <summary>
        /// The tag a fibre should be given for <paramref name="material"/>: the MinMax
        /// wrapper's, when the material declares one, and the material's own otherwise.
        /// </summary>
        public static int? FiberTag(IMaterial material)
        {
            if (material == null)
                return null;

            var concrete = material as Concrete01;
            if (concrete != null && concrete.MinMax != null)
                return concrete.MinMax.Id;

            var reinforcement = material as ReinforcingSteel;
            if (reinforcement != null && reinforcement.MinMax != null)
                return reinforcement.MinMax.Id;

            var steel = material as Steel01;
            if (steel != null && steel.MinMax != null)
                return steel.MinMax.Id;

            return material.Id;
        }
    }
}
