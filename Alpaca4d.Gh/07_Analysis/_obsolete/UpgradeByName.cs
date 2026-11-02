using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System;
using System.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Swapping a component for its replacement when the outputs have moved. GH_UpgradeUtil's
    /// plain swap carries wires across by position, which would hand every wire from an output
    /// that moved to whichever output now sits in its old place.
    /// </summary>
    internal static class UpgradeByName
    {
        /// <summary>
        /// A new <paramref name="newGuid"/> component in place of <paramref name="old"/>: the
        /// inputs carried over in order, with their wires and values, and each output's wires
        /// moved to the output of the same name. Null when the replacement cannot be made.
        /// </summary>
        internal static IGH_Component Swap(IGH_Component old, Guid newGuid, Action<IGH_Component> carryOver = null)
        {
            if (!(Grasshopper.Instances.ComponentServer.EmitObject(newGuid) is IGH_Component upgraded))
                return null;

            carryOver?.Invoke(upgraded);

            // Wired before the swap: the swap takes the old component out of the document, and
            // with it whatever is still connected to it.
            GH_UpgradeUtil.MigrateInputParameters(old, upgraded);
            foreach (var output in old.Params.Output)
            {
                var match = upgraded.Params.Output.FirstOrDefault(param => param.Name == output.Name);
                if (match != null)
                    GH_UpgradeUtil.MigrateRecipients(output, match);
            }

            return GH_UpgradeUtil.SwapComponents(old, upgraded, false) ? upgraded : null;
        }

        /// <summary>
        /// One input's wires, values and data settings, copied onto another of the same type. For an
        /// upgrade to a switcher: GH_UpgradeUtil's index-mapped migration moves the old parameter
        /// objects themselves into the new component, which a switcher cannot take, its parameters
        /// belonging to its evaluation unit.
        /// </summary>
        internal static void CarryOver(IGH_Param from, IGH_Param to)
        {
            foreach (var source in from.Sources)
                to.AddSource(source);

            to.DataMapping = from.DataMapping;
            to.Reverse = from.Reverse;
            to.Simplify = from.Simplify;

            Values<GH_Number>(from, to);
            Values<GH_Integer>(from, to);
            Values<GH_String>(from, to);
            Values<GH_Boolean>(from, to);
            Values<IGH_Goo>(from, to);
        }

        private static void Values<T>(IGH_Param from, IGH_Param to) where T : class, IGH_Goo
        {
            if (!(from is GH_PersistentParam<T> source) || !(to is GH_PersistentParam<T> target))
                return;

            target.PersistentData.Clear();
            foreach (var path in source.PersistentData.Paths)
                target.PersistentData.AppendRange(source.PersistentData[path].Select(x => x?.Duplicate() as T), path);
        }
    }
}
