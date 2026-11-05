using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System;
using System.Linq;

using Alpaca4d.UIWidgets;

// The upgraders read the obsolete components they replace.
#pragma warning disable CS0612, CS0618

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Swaps the old Model View for the new one, which draws the model the same way with no result
    /// ticked. Every setting is carried over - Extruded, the ids, section names, axes, releases,
    /// loads, supports, their sizes, Colour by - and so are the wires: AlpacaModel to AlpacaModel,
    /// LP to LoadPattern, ElemIds to ElementId, which takes an element tag as the old one did.
    ///
    /// One difference shows: the elements ElemIds left out are greyed now rather than hidden, so
    /// that the ones shown stay in the model around them, as View Results did.
    /// </summary>
    public class ModelViewUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("5EDB6364-D458-43D6-81E9-324DECA1FEA6");

        public Guid UpgradeTo => new Guid("F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE");

        /// <summary>
        /// Date the replacement component was introduced.
        /// </summary>
        public DateTime Version => new DateTime(2026, 11, 3);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is ModelView_obsolete old))
                return null;

            // Made here rather than emitted by GUID: it is in this assembly, and the component server
            // would hand back whichever Alpaca4d Grasshopper loaded first.
            var upgraded = new ModelView();
            upgraded.CarryOver = view =>
            {
                view._ddColourBy.Value = old._ddColourBy.Value;
                view._ckExtruded.Active = old._ckExtruded.Active;
                view._ckNodeIds.Active = old._ckNodeIds.Active;
                view._ckElementIds.Active = old._ckElementIds.Active;
                view._ckSectionNames.Active = old._ckSectionNames.Active;
                view._ckLocalAxes.Active = old._ckLocalAxes.Active;
                view._ckReleases.Active = old._ckReleases.Active;
                view._ckShowLoads.Active = old._ckShowLoads.Active;
                view._ckShowLoadValues.Active = old._ckShowLoadValues.Active;
                view._ckShowSupports.Active = old._ckShowSupports.Active;
                Copy(old._slTextSize, view._slTextSize);
                Copy(old._slAxesSize, view._slAxesSize);
                Copy(old._slReleaseSize, view._slReleaseSize);
                Copy(old._slLoadScale, view._slLoadScale);
                Copy(old._slSupportScale, view._slSupportScale);
            };

            // Wired before the swap: the swap takes the old component out of the document, and with
            // it whatever is still connected to it.
            UpgradeByName.CarryOver(old.Params.Input[0], upgraded.Params.Input[0]);
            UpgradeByName.CarryOver(old.Params.Input[1], upgraded.Params.Input[5]);
            UpgradeByName.CarryOver(old.Params.Input[2], upgraded.Params.Input[2]);
            TagsAsText(old.Params.Input[2], upgraded.Params.Input[2]);

            MigrateOutputs(old, upgraded);

            return GH_UpgradeUtil.SwapComponents(old, upgraded, false) ? upgraded : null;
        }

        /// <summary>
        /// The element tags typed into the old integer ElemIds, as text for the new ElementId, which
        /// reads a whole number as a tag. A wire needs nothing: Grasshopper turns the numbers into
        /// text on the way in.
        /// </summary>
        private static void TagsAsText(IGH_Param from, IGH_Param to)
        {
            if (!(from is GH_PersistentParam<GH_Integer> tags) || !(to is GH_PersistentParam<GH_String> text))
                return;

            text.PersistentData.Clear();
            foreach (var path in tags.PersistentData.Paths)
                text.PersistentData.AppendRange(tags.PersistentData[path].Where(x => x != null).Select(x => new GH_String(x.Value.ToString())), path);
        }

        /// <summary>Each output's wires moved to the output of the same name.</summary>
        internal static void MigrateOutputs(IGH_Component old, IGH_Component upgraded)
        {
            foreach (var output in old.Params.Output)
            {
                var match = upgraded.Params.Output.FirstOrDefault(param => param.Name == output.Name);
                if (match != null)
                    GH_UpgradeUtil.MigrateRecipients(output, match);
            }
        }

        /// <summary>A slider's range, decimals and value, in that order so the value is not clipped to the old range.</summary>
        internal static void Copy(MenuSlider from, MenuSlider to)
        {
            to.MinValue = from.MinValue;
            to.MaxValue = from.MaxValue;
            to.NumDecimals = from.NumDecimals;
            to.Value = from.Value;
        }
    }
}
