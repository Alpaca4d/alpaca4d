using Grasshopper.Kernel;
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
    }
}
