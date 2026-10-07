using Grasshopper.Kernel;
using System;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Run Analysis as it was, with the log as the first output and the analysed model second.
    /// Superseded by <see cref="RunAnalysis"/>, which puts the model first. Kept hidden so
    /// definitions saved before that change still open and run; Grasshopper's "Upgrade
    /// Components" swaps it via <see cref="RunAnalysisUpgrader"/>.
    /// </summary>
    [Obsolete]
    public class RunAnalysis_obsolete : RunAnalysisBase
    {
        protected override bool LogFirst => true;

        public override GH_Exposure Exposure => GH_Exposure.hidden;

        public override Guid ComponentGuid => new Guid("46711AB3-D3BC-437B-A2DD-91BED579346D");
    }

    /// <summary>
    /// Swaps <see cref="RunAnalysis_obsolete"/> for <see cref="RunAnalysis"/>: the inputs in
    /// order, the two outputs by name - they have changed places - and the "Do not use settings"
    /// choice carried over.
    /// </summary>
    public class RunAnalysisUpgrader : IGH_UpgradeObject
    {
        public Guid UpgradeFrom => new Guid("46711AB3-D3BC-437B-A2DD-91BED579346D");

        public Guid UpgradeTo => new Guid("{8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452}");

        /// <summary>
        /// Date the replacement component was introduced.
        /// </summary>
        public DateTime Version => new DateTime(2026, 10, 7);

        public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)
        {
            if (!(target is RunAnalysisBase old))
                return null;

            return UpgradeByName.Swap(old, UpgradeTo,
                upgraded => ((RunAnalysisBase)upgraded)._settings = old._settings);
        }
    }
}
