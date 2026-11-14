using Grasshopper.Kernel;
using System;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Swaps <see cref="FourNodeTetrahedron"/> for <see cref="TenNodeTetrahedron"/>, which takes the
    /// same four inputs - Mesh, Material, Colour, ElementId - in the same order. Element keeps its
    /// wires; Points is the ten node one's own. The swapped model needs an OpenSees that knows the
    /// ten node element: a master build since 16 October 2026.
    /// </summary>
    public class FourNodeTetrahedronUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("{4e0b2529-f2fb-4d7d-9be3-cdf53ff2c22f}");

        public Guid UpgradeTo => new Guid("{1f38cdfc-f909-4f3f-969e-9e2c63f9a321}");

        /// <summary>
        /// Date the replacement component was introduced.
        /// </summary>
        public DateTime Version => new DateTime(2026, 10, 26);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is IGH_Component component))
                return null;

            return UpgradeByName.Swap(component, UpgradeTo);
        }
    }
}
