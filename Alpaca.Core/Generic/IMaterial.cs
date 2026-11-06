using System;

namespace Alpaca4d.Generic
{
    public interface IMaterial : ISerialize
    {
        public int? Id { get; set; }
        public double? Rho { get; set; }

        /// <summary>What the material is called - S355 out of the Material Library - or null when it was given no name.</summary>
        public string MatName { get; set; }

        /// <summary>
        /// The colour the user gave the material, or null when they gave none: Model View then picks
        /// one of its own for each material when it colours the elements by material. Display only -
        /// the solver never reads it.
        /// </summary>
        public System.Drawing.Color? Color { get; set; }
    }
}
