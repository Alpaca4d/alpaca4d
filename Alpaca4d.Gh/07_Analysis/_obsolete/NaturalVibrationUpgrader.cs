using Grasshopper.Kernel;
using System;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Swaps the old plain "Natural Vibration Analysis (Alpaca4d)" component for the
    /// <see cref="NaturalVibration"/> switcher. The three inputs are the same, in the same order.
    /// The outputs are the same five under the same names, but log has moved from first to after
    /// Frequencies, so they are rewired by name. The report outputs come after them, in the
    /// Modal Report menu.
    /// </summary>
    public class NaturalVibrationUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("{F16B4D71-DBE1-4CDA-BAEE-A8CB82EE23C2}");

        public Guid UpgradeTo => new Guid("{3B7D2E95-6A41-4C8F-9E1D-5F0A8C27B6D3}");

        /// <summary>
        /// Date the replacement component was introduced.
        /// </summary>
        public DateTime Version => new DateTime(2026, 10, 7);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is IGH_Component component))
                return null;

            return UpgradeByName.Swap(component, UpgradeTo);
        }
    }
}
