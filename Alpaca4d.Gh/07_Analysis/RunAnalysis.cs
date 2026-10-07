using Alpaca4d;
using Alpaca4d.Generic;
using Alpaca4d.License;
using Eto.Forms;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Microsoft.CSharp.RuntimeBinder;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Run Analysis, as two components that differ only in where the log output sits:
    /// <see cref="RunAnalysis"/>, with the model first, and the one it replaced, with the log
    /// first, kept hidden so older definitions open (see <see cref="RunAnalysis_obsolete"/>).
    /// </summary>
    public abstract class RunAnalysisBase : GH_Component
    {
        // The leading space is on purpose: the ribbon sorts a panel by name, and it puts this
        // ahead of Natural Vibration Analysis, as the first component of 07_Analysis.
        protected RunAnalysisBase()
          : base(" Run Analysis (Alpaca4d)", "Run Analysis",
            "Writes the assembled model out as an OpenSees script, solves it, and returns the model " +
            "with its results attached.\n" +
            "Results are recorded to a recorder.mpco file beside the Grasshopper document and read " +
            "back by the 08_NumericalOutput components; an MPCO Recorder connected to Assemble Model " +
            "chooses what the file holds, and what it is called. When a run fails the AlpacaModel output comes " +
            "out empty - read the log output to find out why.",
            "Alpaca4d", "07_Analysis")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
            this._settings = true;
        }

        public bool _settings { get; set; }

        public override bool Write(GH_IWriter writer)
        {
            // Save the version when this component was created
            writer.SetBoolean("settings", _settings);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            // Read the version when this component was created
            try
            {
                _settings = reader.GetBoolean("settings");
            }
            catch (NullReferenceException) { } // In case the info component was created before the VersionWhenFirstCreated was implemented.
            return base.Read(reader);
        }

        protected override void AppendAdditionalComponentMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            // Append the item to the menu, making sure it's always enabled and checked if Absolute is True.
            ToolStripMenuItem item = Menu_AppendItem(menu, "Do not use settings", Menu_AbsoluteClicked, null, true, !_settings);
        }

        private void Menu_AbsoluteClicked(object sender, EventArgs e)
        {
            _settings = !_settings;
            ExpireSolution(true);
        }




        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The assembled model, from Assemble Model.", GH_ParamAccess.item);
            pManager.AddGenericParameter("Settings", "Settings", "How to solve: solver, algorithm, integrator and steps. From the Analysis Settings component. It can be left empty only with \"Do not use settings\" ticked in the right-click menu.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            if (LogFirst)
                pManager.Register_GenericParam("log", "log", LogDescription);
            pManager.Register_GenericParam("AlpacaModel", "AlpacaModel", "The analysed model. Feed it to any of the 08_NumericalOutput components to read results. It comes out empty if the analysis failed.");
            if (!LogFirst)
                pManager.Register_GenericParam("log", "log", LogDescription);
        }

        private const string LogDescription = "What OpenSees printed while solving. Read it when the analysis fails or warns.";

        /// <summary>Whether the log is the first output, as it was before the model was put first.</summary>
        protected abstract bool LogFirst { get; }

        private int LogOutput => LogFirst ? 0 : 1;
        private int ModelOutput => LogFirst ? 1 : 0;

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var model = new Model();
            Settings settings = null;


            if (!DA.GetData(0, ref model)) return;
            
            DA.GetData(1, ref settings);
            if (_settings == true && settings == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter Settings failed to collect data");
                return;
            }


            // Free up to License.FreeElementLimit elements. Past it, no license means no analysis.
            if (!LicenseGate.AllowsModel(this, model))
                return;



            // create a shallow copy

            var analysisModel = model.ShallowCopy();
            analysisModel.Tcl = new List<string>(analysisModel.Tcl);


            bool fileExist = OnPingDocument().IsFilePathDefined;
            if (!fileExist)
            {
                // hops issue
                var folderPath = System.IO.Directory.GetCurrentDirectory();
                System.IO.Directory.SetCurrentDirectory(folderPath);
            }
            else
            {
                var filePath = OnPingDocument().FilePath;
                var currentDir = System.IO.Path.GetDirectoryName(filePath);
                System.IO.Directory.SetCurrentDirectory(currentDir);
            }


            analysisModel.FileName = System.IO.Path.GetFullPath("AlpacaModel");



            if(settings != null)
            {
                string recorderName = "recorder.mpco";
                analysisModel.Settings = settings;
                // Recorder
                var recorder = new Alpaca4d.Recorder();
                if (settings.Analysis.Type == Analysis.AnalysisType.Static)
                {
                    recorder = Alpaca4d.Recorder.MpcoStatic(recorderName);
                    analysisModel.IsStatic = true;
                }

                if (settings.Analysis.Type == Analysis.AnalysisType.Transient)
                {
                    recorder = Alpaca4d.Recorder.MpcoTransient(recorderName);
                    analysisModel.IsTransient = true;
                }

                // Recorders from Assemble Model are the user's choice and go in as they are, in
                // place of the one picked above. The result components read the first of them.
                analysisModel.Recorders = model.Recorders != null && model.Recorders.Count > 0
                    ? new List<IRecorder>(model.Recorders)
                    : new List<IRecorder> { recorder };

                // Two recorders on one file would have the second overwrite the first.
                var shared = analysisModel.Recorders
                    .Where(item => !string.IsNullOrEmpty(item.FileName))
                    .GroupBy(item => System.IO.Path.GetFullPath(item.FileName), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(group => group.Count() > 1);
                if (shared != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"{shared.Count()} recorders write to {shared.First().FileName}. Give each one a FileName of its own.");
                    return;
                }

                foreach (var item in analysisModel.Recorders)
                    analysisModel.Tcl.Add(item.WriteTcl());
                // Settings
                analysisModel.Tcl.Add(settings.WriteTcl());
                analysisModel.Tcl.Add("wipe");
            }
            else if (model.Recorders != null && model.Recorders.Count > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "With \"Do not use settings\" the script runs as it stands, and the recorders from " +
                    "Assemble Model are not written into it.");
            }


            analysisModel.Serialise();
            string output, error;
            int exitCode;
            try
            {
                (output, error, exitCode) = analysisModel.RunOpenSees();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            const string AnalyzeResultTag = "ALPACA_ANALYZE_RESULT";
            string log = string.IsNullOrEmpty(error) ? output : error + "\n" + output;

            if (exitCode != 0)
            {
                // A non-zero exit code means the Tcl script itself errored out
                // (e.g. bad command, undefined material) - see TclInterpreter::run.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Analysis Failed: OpenSees exited with an error. See log for details.");
                analysisModel = null;
            }
            else
            {
                // analyze() always returns TCL_OK to Tcl even on non-convergence; the
                // actual result is a negative integer captured via the sentinel below.
                int? analyzeResult = null;
                var resultLine = output
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .LastOrDefault(l => l.Contains(AnalyzeResultTag));

                if (resultLine != null)
                {
                    var token = resultLine.Substring(resultLine.IndexOf(AnalyzeResultTag) + AnalyzeResultTag.Length).Trim();
                    if (int.TryParse(token, out int parsed))
                        analyzeResult = parsed;
                }

                if (analyzeResult.HasValue && analyzeResult.Value < 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Analysis Failed: OpenSees 'analyze' did not converge.");
                    analysisModel = null;
                }
                else if (error.Contains("WARNING"))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Warning! Double check the log!");
                }
            }

            DA.SetData(ModelOutput, analysisModel);
            DA.SetData(LogOutput, log);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Run_Analysis__Alpaca4d_;
    }

    /// <summary>
    /// Run Analysis with the analysed model as the first output and the log after it, so the
    /// result comes first. Replaces <see cref="RunAnalysis_obsolete"/>, which had them the other
    /// way round; <see cref="RunAnalysisUpgrader"/> swaps one for the other.
    /// </summary>
    public class RunAnalysis : RunAnalysisBase
    {
        protected override bool LogFirst => false;

        public override Guid ComponentGuid => new Guid("{8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452}");
    }
}