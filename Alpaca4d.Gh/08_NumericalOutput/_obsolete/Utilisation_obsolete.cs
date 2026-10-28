using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Utilisation as it was released in 0.11.0: one buckling length for both axes, and no LTB or
    /// interaction outputs. Superseded by <see cref="Utilisation"/>; kept hidden so definitions saved
    /// with it open without Grasshopper reporting missing parameter chunks. It checks with the same
    /// code - lateral-torsional buckling and 6.3.3 included, with each beam's own length - so its Max
    /// is the new one. Grasshopper's "Upgrade Components" swaps it via <see cref="UtilisationUpgrader"/>.
    /// </summary>
    [Obsolete]
    public class Utilisation_obsolete : UtilisationBase
    {
        protected override bool MemberInputs => false;

        public override GH_Exposure Exposure => GH_Exposure.hidden;

        public override Guid ComponentGuid => new Guid("{6E2B9C41-8F0A-4B3D-A6C7-2D51E9F48B07}");
    }

    /// <summary>
    /// Swaps <see cref="Utilisation_obsolete"/> for <see cref="Utilisation"/>. The eight inputs keep
    /// their places, Length becoming LengthY, and Length's wires and values go to LengthZ too, so the
    /// one length is still about both axes. The outputs are rewired by name: LTB, InteractionY and
    /// InteractionZ come in before Max.
    /// </summary>
    public class UtilisationUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("{6E2B9C41-8F0A-4B3D-A6C7-2D51E9F48B07}");

        public Guid UpgradeTo => new Guid("{4F8A2D6B-91C3-4E57-B0A9-3C6E8D1F2A74}");

        /// <summary>
        /// Date the replacement component was introduced.
        /// </summary>
        public DateTime Version => new DateTime(2026, 10, 17);

        /// <summary>
        /// Not <see cref="UpgradeByName.Swap"/>: GH_UpgradeUtil.MigrateInputParameters wants as many
        /// inputs on both sides and throws otherwise, and its index-mapped form moves the old parameter
        /// objects themselves into the new component - which a switcher cannot take, its parameters
        /// belonging to its evaluation unit. So every wire and value is copied onto the new component's
        /// own parameters instead, and the old ones go with the old component.
        /// </summary>
        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is UtilisationBase old))
                return null;
            if (!(Grasshopper.Instances.ComponentServer.EmitObject(UpgradeTo) is IGH_Component upgraded))
                return null;

            // Wired before the swap: the swap takes the old component out of the document, and with
            // it whatever is still connected to it.
            for (int i = 0; i < old.Params.Input.Count && i < upgraded.Params.Input.Count; i++)
                CarryOver(old.Params.Input[i], upgraded.Params.Input[i]);

            var length = old.Params.Input.FirstOrDefault(p => p.Name == "Length");
            var lengthZ = upgraded.Params.Input.FirstOrDefault(p => p.Name == "LengthZ");
            if (length != null && lengthZ != null)
                CarryOver(length, lengthZ);

            foreach (var output in old.Params.Output)
            {
                var match = upgraded.Params.Output.FirstOrDefault(param => param.Name == output.Name);
                if (match != null)
                    GH_UpgradeUtil.MigrateRecipients(output, match);
            }

            return GH_UpgradeUtil.SwapComponents(old, upgraded, false) ? upgraded : null;
        }

        /// <summary>One input's wires, values and data settings, onto another of the same type.</summary>
        private static void CarryOver(IGH_Param from, IGH_Param to)
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
