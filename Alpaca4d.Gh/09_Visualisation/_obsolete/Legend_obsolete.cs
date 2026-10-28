using Grasshopper.Kernel;
using System;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Legend as 0.12.0-beta.1 shipped it, with a sixth input, Range. Range went again once View
    /// Results' Values came to follow the Range given to it, and Legend took back its 0.11.0 GUID.
    /// Kept hidden so beta.1 definitions open: Grasshopper reads input chunks by index, only as
    /// many as the component has, so the stored Range is passed over with its wire, and the five
    /// inputs before it load as they were. "Upgrade Components" swaps it via
    /// <see cref="LegendUpgrader"/>.
    /// </summary>
    [Obsolete]
    public class Legend_obsolete : LegendBase
    {
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        public override Guid ComponentGuid => new Guid("{C942420B-9CE2-4898-BAFF-0A65A95DA4A7}");
    }

    /// <summary>
    /// Swaps <see cref="Legend_obsolete"/> for <see cref="Legend"/>, the same five inputs in order.
    /// </summary>
    public class LegendUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("{C942420B-9CE2-4898-BAFF-0A65A95DA4A7}");

        public Guid UpgradeTo => new Guid("{A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5}");

        /// <summary>
        /// Date the replacement component was introduced.
        /// </summary>
        public DateTime Version => new DateTime(2026, 10, 20);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is LegendBase old))
                return null;

            return UpgradeByName.Swap(old, UpgradeTo);
        }
    }
}
