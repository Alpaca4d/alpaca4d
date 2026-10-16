using Grasshopper.Kernel;
using System;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Legend as it was, without the Range input. Superseded by <see cref="Legend"/>; kept hidden
    /// so definitions saved before Range was added open without Grasshopper reporting a missing
    /// input chunk. It draws with the same code, so it has every other fix. Grasshopper's
    /// "Upgrade Components" swaps it via <see cref="LegendUpgrader"/>.
    /// </summary>
    [Obsolete]
    public class Legend_obsolete : LegendBase
    {
        protected override bool HasRange => false;

        public override GH_Exposure Exposure => GH_Exposure.hidden;

        public override Guid ComponentGuid => new Guid("{A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5}");
    }

    /// <summary>
    /// Swaps <see cref="Legend_obsolete"/> for <see cref="Legend"/>: the five inputs carried over
    /// in order, Range left empty.
    /// </summary>
    public class LegendUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("{A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5}");

        public Guid UpgradeTo => new Guid("{C942420B-9CE2-4898-BAFF-0A65A95DA4A7}");

        /// <summary>
        /// Date the replacement component was introduced.
        /// </summary>
        public DateTime Version => new DateTime(2026, 10, 16);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is LegendBase old))
                return null;

            return UpgradeByName.Swap(old, UpgradeTo);
        }
    }
}
