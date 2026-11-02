using Grasshopper.Kernel;
using System;
using System.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Swaps the old plain "Support (Alpaca4d)" for the <see cref="Support"/> switcher on its Rigid
    /// type, which has the same seven inputs - Position, Tx, Ty, Tz, Rx, Ry, Rz - in the same order
    /// and the same Support output. Each input's wires and set values are copied across, so a Tx
    /// turned off by hand stays off.
    /// </summary>
    public class SupportUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("08A676AD-7332-4D86-8206-DABD66A17357");

        public Guid UpgradeTo => new Guid("C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05");

        /// <summary>
        /// Date the replacement component was introduced.
        /// </summary>
        public DateTime Version => new DateTime(2026, 11, 1);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is IGH_Component old))
                return null;

            // Made here rather than emitted by GUID: it is in this assembly, and the component server
            // would hand back whichever Alpaca4d Grasshopper loaded first.
            var upgraded = new Support();
            upgraded.SwitchUnit(RigidSupport.Name, false, false);

            // Wired before the swap: the swap takes the old component out of the document, and with
            // it whatever is still connected to it.
            for (int i = 0; i < old.Params.Input.Count && i < upgraded.Params.Input.Count; i++)
                UpgradeByName.CarryOver(old.Params.Input[i], upgraded.Params.Input[i]);

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
