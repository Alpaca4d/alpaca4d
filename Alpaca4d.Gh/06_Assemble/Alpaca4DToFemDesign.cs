using System;
using System.IO;

using Grasshopper.Kernel;

using Alpaca4d.FemDesign;
using Alpaca4d.Interop;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Writes an assembled Alpaca model to a .struxml file FEM-Design opens. The mapping - library
    /// materials and profiles where FEM-Design has them, trusses, plates and walls merged from shell
    /// elements, one load case per load pattern and a combination that adds them - lives in
    /// <see cref="StruxmlWriter"/>; this component reads the inputs, saves, and shows the report.
    /// </summary>
    public class Alpaca4DToFemDesign : GH_Component
    {
        public Alpaca4DToFemDesign()
          : base("Alpaca4DToFEM-Design", "A4D_FD",
            "Write an assembled Alpaca model to a .struxml file that FEM-Design opens. " +
            "Materials and profiles from Alpaca's libraries become FEM-Design's own, so FEM-Design can design with them; any other material becomes a custom one, " +
            "any other shape a section drawn from its outline, and a section known only by A, I and J a fictitious bar. " +
            "A Beam With Hinges pinned at both ends and loaded only there becomes a truss, and coplanar shell elements become one plate or wall. " +
            "Each load pattern becomes a load case, gravity a dead-load case, and the combination Alpaca adds them with the patterns' factors, which is what Alpaca analyses. " +
            "Whatever cannot be exported is reported: as an error when leaving it out changes the structure or its loads, " +
            "as a warning when it is approximated, and as a remark when it is worth knowing.",
            "Alpaca4d", "06_Assemble")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "An assembled model from Assemble Model, or an analysed one.", GH_ParamAccess.item);
            pManager.AddTextParameter("FilePath", "FilePath", "Where to write the .struxml file. Left empty it is written next to the Grasshopper definition.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Country", "Country",
                $"FEM-Design's national annex, whose material library Alpaca's materials are looked up in: {FemDesignExport.CountryCodes}. " +
                "Left empty it is common, the Eurocode's recommended values.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Save", "Save", "Set to true to write the file.", GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_StringParam("FilePath", "FilePath", "The file written, once Save is true.");
            pManager.Register_StringParam("LoadCases", "LoadCases",
                $"FEM-Design's load cases: one per load pattern with loads, in order, then the self-weight. The combination {StruxmlWriter.CombinationName} adds them.");
            pManager.Register_StringParam("Report", "Report", "Everything the export left out or changed, errors first.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Model model = null;
            if (!DA.GetData(0, ref model) || model == null)
                return;

            string filePath = null;
            DA.GetData(1, ref filePath);
            string countryText = null;
            DA.GetData(2, ref countryText);
            bool save = false;
            DA.GetData(3, ref save);

            if (!FemDesignExport.IsCountry(countryText))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Country takes {FemDesignExport.CountryCodes}, the national annexes whose material library FEM-Design's StruXml library carries; not {countryText}.");
                return;
            }

            if (model.Elements == null || model.Elements.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The model has no elements. Plug in a model from Assemble Model.");
                return;
            }

            FemDesignExportResult result;
            try
            {
                result = FemDesignExport.Export(model, countryText);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The export failed: {ex.Message}");
                return;
            }

            string written = null;
            if (save)
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    var document = OnPingDocument();
                    if (document != null && document.IsFilePathDefined)
                        filePath = Path.ChangeExtension(document.FilePath, "struxml");
                    else
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Save your .gh file or give a FilePath.");
                }

                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    try
                    {
                        written = result.Save(filePath);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The file could not be written: {ex.Message}");
                    }
                }
            }

            // After saving, which adds anything that fails FEM-Design's schema to the report.
            foreach (var entry in result.Report.Entries)
                AddRuntimeMessage(LevelOf(entry.Level), entry.Message);

            DA.SetData(0, written);
            DA.SetDataList(1, result.LoadCases);
            DA.SetDataList(2, result.Report.Lines());
        }

        private static GH_RuntimeMessageLevel LevelOf(ReportLevel level)
        {
            switch (level)
            {
                case ReportLevel.Error: return GH_RuntimeMessageLevel.Error;
                case ReportLevel.Warning: return GH_RuntimeMessageLevel.Warning;
                default: return GH_RuntimeMessageLevel.Remark;
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Alpaca_to_FEM_Design__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{75222D99-EE13-48A1-8C7F-848E32877C1E}");
    }
}
