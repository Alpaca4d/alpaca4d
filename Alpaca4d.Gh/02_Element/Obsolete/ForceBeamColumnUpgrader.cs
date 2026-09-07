using Grasshopper.Kernel;
using System;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Swaps the old standalone "Force Beam Column (Alpaca4d)" component for the
    /// <see cref="BeamBase"/> switcher, whose default ForceBeamColumn unit registers
    /// the same five inputs (Line, Section, GeomTransf, ZAxis, Colour) in the same
    /// order and one generic output, so every wire migrates by index.
    /// </summary>
    public class ForceBeamColumnUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("BA137E72-9B35-44FD-9601-5859C0E97AD4");

        public Guid UpgradeTo => new Guid("A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6");

        /// <summary>
        /// Date the replacement component was introduced.
        /// </summary>
        public DateTime Version => new DateTime(2026, 6, 9);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is IGH_Component component))
                return null;

            return GH_UpgradeUtil.SwapComponents(component, UpgradeTo);
        }
    }
}
