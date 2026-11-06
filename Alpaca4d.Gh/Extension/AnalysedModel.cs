using Grasshopper.Kernel;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Whether a model has results to read, for the components that read them. A model straight
    /// from Assemble Model or Deserialise has no recorder file, and reading one stopped the
    /// component with "Sequence contains no elements".
    /// </summary>
    internal static class AnalysedModel
    {
        public const string NotAnalysed =
            "The model is not analysed. Feed it from Run Analysis or another analysis, not from Assemble Model.";

        /// <summary>True when <paramref name="model"/> has results; otherwise says so on the component.</summary>
        public static bool Check(GH_Component component, Model model)
        {
            if (model != null && model.IsAnalysed && model.Recorders != null && model.Recorders.Count > 0)
                return true;

            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, NotAnalysed);
            return false;
        }
    }
}
