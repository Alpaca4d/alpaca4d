using Grasshopper.Kernel;
using System;
using System.Linq;

// The upgraders read the obsolete components they replace.
#pragma warning disable CS0612, CS0618

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Swaps View Results_WIP for Model View, with the result it was drawing ticked alone. Its five
    /// inputs - AlpacaModel, Step, ElementId, Colors, Range - are the first five of Model View, in
    /// the same order, and Info, Values and Colors keep their wires by name. Every setting is
    /// carried over: the component, unit, layer, reaction style and axes, the deformed shape and
    /// its scale, Extruded, the diagram scale, iso-surfaces, edges, values and their size. End
    /// releases, which View Results never drew, are left off.
    /// </summary>
    public class ViewResultsUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("4D8A0F26-3B71-4E59-9C84-A2F70D51B6E3");

        public Guid UpgradeTo => new Guid("F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE");

        /// <summary>
        /// Date the replacement component was introduced.
        /// </summary>
        public DateTime Version => new DateTime(2026, 11, 3);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is ViewResults_obsolete old))
                return null;

            var upgraded = new ModelView();
            upgraded.CarryOver = view =>
            {
                var result = view._results.First(x => x.Family == old.Family);
                result.Tick.Active = true;
                result.Pick(old._ddComponent.Value);
                for (int i = 0; i < result.Units.Length && i < old._ddUnits.Length; i++)
                    if (result.Units[i] != null)
                        result.Units[i].Value = old._ddUnits[i].Value;

                view._ddLayer.Value = old._ddLayer.Value;
                view._ddReaction.Value = old._ddReaction.Value;
                view._ddAxes.Value = old._ddAxes.Value;

                view._ckDeformed.Active = old._ckDeformed.Active;
                ModelViewUpgrader.Copy(old._slDeformScale, view._slDeformScale);
                view._ckExtruded.Active = old._ckExtruded.Active;

                // One scale sized both the diagrams and the arrows; each has its own now.
                ModelViewUpgrader.Copy(old._slDiagramScale, view._slDiagramScale);
                ModelViewUpgrader.Copy(old._slDiagramScale, view._slArrowScale);
                view._ckBands.Active = old._ckBands.Active;
                view._ckWires.Active = old._ckWires.Active;
                view._ckValues.Active = old._ckValues.Active;
                ModelViewUpgrader.Copy(old._slTextSize, view._slTextSize);
                view._ckReleases.Active = false;
            };

            // Wired before the swap: the swap takes the old component out of the document, and with
            // it whatever is still connected to it.
            for (int i = 0; i < old.Params.Input.Count && i < 5; i++)
                UpgradeByName.CarryOver(old.Params.Input[i], upgraded.Params.Input[i]);

            ModelViewUpgrader.MigrateOutputs(old, upgraded);

            return GH_UpgradeUtil.SwapComponents(old, upgraded, false) ? upgraded : null;
        }
    }
}
