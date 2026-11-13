using System;
using System.Linq;

using Grasshopper.Kernel;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Superseded by <see cref="SteelLaws"/>, whose Law menu has ReinforcingSteel. It is that component with the
    /// ReinforcingSteel law fixed, so definitions saved before that change still load and solve; "Upgrade
    /// Components" swaps it via <see cref="ReinforcingSteelUpgrader"/>.
    /// </summary>
    [Obsolete]
    public class ReinforcingSteel_obsolete : SteelLaws
    {
        public ReinforcingSteel_obsolete()
        {
            Name = "ReinforcingSteel (Alpaca4d)";
            NickName = "ReinforcingSteel";
        }

        protected override string DefaultEvaluationUnit => ReinforcingSteel;
        public override GH_Exposure Exposure => GH_Exposure.hidden;
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.reinforcing_steel_material;
        public override Guid ComponentGuid => new Guid("{47B04CA7-0F7E-4585-8F89-D31592E61394}");
    }

    /// <summary>Swaps a ReinforcingSteel component for <see cref="SteelLaws"/> on its ReinforcingSteel law, the inputs carried across by name.</summary>
    public class ReinforcingSteelUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("{47B04CA7-0F7E-4585-8F89-D31592E61394}");

        public Guid UpgradeTo => new Guid("{DEB86811-4429-426F-8A7B-02DF89C5A074}");

        public DateTime Version => new DateTime(2026, 11, 13);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is IGH_Component old))
                return null;

            var upgraded = new SteelLaws();
            upgraded.SwitchUnit(SteelLaws.ReinforcingSteel, false, false);

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
}
