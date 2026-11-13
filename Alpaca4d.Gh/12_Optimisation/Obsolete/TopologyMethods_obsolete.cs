using System;
using System.Linq;

using Grasshopper.Kernel;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Superseded by <see cref="TopologyOptimisation"/>, whose Method switch has BESO. It is that component
    /// with the BESO method fixed, so definitions saved before that change still load and solve; "Upgrade
    /// Components" swaps it via <see cref="BesoTopologyUpgrader"/>.
    /// </summary>
    [Obsolete]
    public class BesoTopology_obsolete : TopologyOptimisation
    {
        public BesoTopology_obsolete()
        {
            Name = "BESO Topology (Alpaca4d)";
            NickName = "BESO";
        }

        protected override string DefaultEvaluationUnit => "BesoTopology";
        public override GH_Exposure Exposure => GH_Exposure.hidden;
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.BESO_Topology__Alpaca4d_;
        public override Guid ComponentGuid => new Guid("{3B7E5C21-9A4D-4F68-B2E1-7C5D0A8F6E14}");
    }

    /// <summary>
    /// Superseded by <see cref="TopologyOptimisation"/>, whose Method switch has Multi-Material. See
    /// <see cref="BesoTopology_obsolete"/>.
    /// </summary>
    [Obsolete]
    public class MultiMaterialTopology_obsolete : TopologyOptimisation
    {
        public MultiMaterialTopology_obsolete()
        {
            Name = "Multi-Material Topology (Alpaca4d)";
            NickName = "MultiTopology";
        }

        protected override string DefaultEvaluationUnit => "MultiMaterialTopology";
        public override GH_Exposure Exposure => GH_Exposure.hidden;
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Multi_Material_Topology__Alpaca4d_;
        public override Guid ComponentGuid => new Guid("{6D2A9E47-1C8B-4E35-A9F0-58B3C7D16A22}");
    }

    /// <summary>
    /// Swaps a BESO or Multi-Material Topology component for <see cref="TopologyOptimisation"/> on the same
    /// method. The unit has the inputs and outputs of the old component, by name; each input's wires and set
    /// values are copied across.
    /// </summary>
    public abstract class TopologyMethodUpgrader : IGH_UpgradeObject
    {
        protected abstract string Unit { get; }

        public abstract Guid UpgradeFrom { get; }

        public Guid UpgradeTo => new Guid("{8F3C2A61-5D4E-4B7A-9C1E-2B6D7A0F4E93}");

        public DateTime Version => new DateTime(2026, 11, 13);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is IGH_Component old))
                return null;

            // Made here rather than emitted by GUID, as the Support upgrader does.
            var upgraded = new TopologyOptimisation();
            upgraded.SwitchUnit(Unit, false, false);

            foreach (var input in old.Params.Input)
            {
                var match = upgraded.Params.Input.FirstOrDefault(param => param.Name == input.Name);
                if (match != null)
                    UpgradeByName.CarryOver(input, match);
            }

            foreach (var output in old.Params.Output)
            {
                var match = upgraded.Params.Output.FirstOrDefault(param => param.Name == output.Name);
                if (match != null)
                    GH_UpgradeUtil.MigrateRecipients(output, match);
            }

            return GH_UpgradeUtil.SwapComponents(old, upgraded, false) ? upgraded : null;
        }
    }

    public class BesoTopologyUpgrader : TopologyMethodUpgrader
    {
        protected override string Unit => "BesoTopology";
        public override Guid UpgradeFrom => new Guid("{3B7E5C21-9A4D-4F68-B2E1-7C5D0A8F6E14}");
    }

    public class MultiMaterialTopologyUpgrader : TopologyMethodUpgrader
    {
        protected override string Unit => "MultiMaterialTopology";
        public override Guid UpgradeFrom => new Guid("{6D2A9E47-1C8B-4E35-A9F0-58B3C7D16A22}");
    }
}
