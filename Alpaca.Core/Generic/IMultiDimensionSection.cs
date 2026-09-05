using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alpaca4d.Generic
{
    public interface IMultiDimensionSection : ISection
    {
        /// <summary>
        /// The section's material. A layered section has one per layer, and answers here with the
        /// first of them - use <see cref="Materials"/> to reach them all.
        /// </summary>
        public IMultiDimensionMaterial Material { get; set; }

        /// <summary>Total thickness, summed over the layers where there is more than one.</summary>
        public double Thickness { get; set; }

        /// <summary>
        /// Every material the section is built from, each one once.
        ///
        /// Assemble writes a material declaration per entry, so a layered section whose layers are
        /// of different materials would otherwise reference tags OpenSees was never given: only
        /// Material was ever asked for, and the rest of the stack went undeclared.
        /// </summary>
        public IEnumerable<IMaterial> Materials { get; }
    }
}
