using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Alpaca4d.Element;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The GeomTransf input of the Beam and WithHinges types: a word - Linear, PDelta or
    /// Corotational, typed or from a Value List - or a transformation made in code. A word is built
    /// on the beam's own line and local z, so it needs nothing else; left empty the beam is Linear.
    /// </summary>
    internal static class GeomTransfInput
    {
        public const string Description =
            "Linear (default), PDelta or Corotational, typed or from a Value List. Corotational holds for large displacements.";

        /// <summary>What a Value List on the input is filled with.</summary>
        public static List<string> Names => Enum.GetNames(typeof(GeomTransfType)).ToList();

        /// <summary>
        /// The transformation the input asks for, or null with <paramref name="error"/> saying why.
        /// </summary>
        public static Alpaca4d.Element.GeomTransf Read(IGH_DataAccess DA, int index, Curve line, Vector3d zAxis, out string error)
        {
            error = null;
            IGH_Goo goo = null;
            if (!DA.GetData(index, ref goo) || goo == null)
                return new Alpaca4d.Element.GeomTransf(GeomTransfType.Linear, line, zAxis);

            if (goo.CastTo(out Alpaca4d.Element.GeomTransf made) && made != null)
                return made;

            string text = goo is GH_String s ? s.Value : goo.CastTo(out string cast) ? cast : null;
            if (Alpaca4d.Element.GeomTransf.TryParseType(text, out var type))
                return new Alpaca4d.Element.GeomTransf(type, line, zAxis);

            error = $"GeomTransf \"{goo}\" is not a transformation: give Linear, PDelta or Corotational.";
            return null;
        }
    }
}
