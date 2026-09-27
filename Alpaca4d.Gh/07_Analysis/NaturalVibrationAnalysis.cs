using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

using Microsoft.CSharp.RuntimeBinder;
using Alpaca4d;
using Alpaca4d.Generic;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;
using System.Reflection;

namespace Alpaca4d.Gh
{
    public class NaturalVibrationAnalysis : GH_Component
    {
        public NaturalVibrationAnalysis()
          : base("Natural Vibration Analysis (Alpaca4d)", "Natural Vibration",
            "Solves the eigenvalue problem of the model and returns its modes, eigenvalues, periods " +
            "and frequencies.\n" +
            "It needs no Analysis Settings - an eigenvalue problem has nothing to converge. Connect " +
            "the assembled model rather than an analysed one, then feed the solved model to Nodal " +
            "Displacements to read a mode shape, or to Modal Analysis Report for the participating " +
            "masses.",
            "Alpaca4d", "07_Analysis")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The assembled model, from Assemble Model. Not from Run Analysis - that clears the OpenSees domain when it finishes, leaving nothing to solve.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Vibration Modes", "Vibration Modes", "How many modes to solve for, starting at the lowest. Ask for enough that the cumulative participating mass reaches whatever your code requires.", GH_ParamAccess.item, 1);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Solver", "Solver", "Connect a 'ValueList'\n-genBandArpack \n-symmBandLapack \n-fullGenLapack", GH_ParamAccess.item, "-genBandArpack");
            pManager[pManager.ParamCount-1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("log", "log", "What OpenSees printed while solving. Read it when the analysis fails.");
            pManager.Register_GenericParam("AlpacaModel", "AlpacaModel", "The solved model. Feed it to Nodal Displacements to read a mode shape, or to Modal Analysis Report.");
            pManager.Register_DoubleParam("Eigenvalues", "Eigenvalues", "One eigenvalue per mode, lowest first. Each is omega squared, in rad²/s².");
            pManager.Register_DoubleParam("Period", "Period", $"One period per mode, longest first [{Units.Time}]");
            pManager.Register_DoubleParam("Frequencies", "Frequencies", "One frequency per mode, lowest first, in Hz.");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var model = new Model();
            if (!DA.GetData(0, ref model)) return;

            var vibrationNumber = 1;
            DA.GetData(1, ref vibrationNumber);

            string solver = "";
            DA.GetData(2, ref solver);

            // An analysed model is not a model any more: Run Analysis ends the script it
            // hands on with "wipe", which clears the OpenSees domain. Appending the eigen
            // block to that would call eigen on an empty domain - ArpackSolver fails with
            // "N must be positive" and no eigenvalue is ever set. Modal analysis belongs
            // on its own branch, straight off the assembled model.
            if (model.IsAnalysed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "This AlpacaModel has already been analysed. Connect the Natural Vibration Analysis " +
                    "to the AlpacaModel output of the Assemble component, not to the output of Run Analysis: " +
                    "Run Analysis clears the OpenSees domain when it finishes, so the eigen analysis would " +
                    "find nothing to solve.");
                return;
            }

            // Validate license
            if (!Alpaca4d.License.License.ValidateLicense(model, false, () => Alpaca4d.UI.LicenseManagementForm.ShowForm(), Alpaca4d.Gh.Forms.Advertise.NumberOfElements))
            {
                // add a warning on component saying that the message will be shown every 5 minutes
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "License validation failed. The license management form will be shown every 5 minutes.");
            }


            // create a shallow copy
            var analysisModel = model.ShallowCopy();
            analysisModel.Tcl = new List<string>(analysisModel.Tcl);

            bool fileExist = OnPingDocument().IsFilePathDefined;
            if (!fileExist)
            {
                throw new Exception("Have you saved the Grasshopper script?");
            }
            var filePath = OnPingDocument().FilePath;
            var currentDir = System.IO.Path.GetDirectoryName(filePath);
            System.IO.Directory.SetCurrentDirectory(currentDir);

            string recorderName = "recorder_eigen.mpco";
            analysisModel.ModalAnalysisReportFile = "ModalReport.txt";

            analysisModel.FileName = System.IO.Path.GetFullPath("AlpacaModel");


            // Recorder. Mode shapes only, whatever Assemble Model was given: its recorders are
            // chosen for Run Analysis, and an eigen analysis has no steps for them to record.
            if (model.Recorders != null && model.Recorders.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"The recorders from Assemble Model are for Run Analysis. The mode shapes are recorded to {recorderName} as always.");
            analysisModel.Recorders = new List<IRecorder>();
            var recorder = new Alpaca4d.Recorder();
            recorder = Alpaca4d.Recorder.MpcoEigen(recorderName);
            analysisModel.IsModal = true;
            analysisModel.NumberOfModes = vibrationNumber;

            analysisModel.Recorders.Add(recorder);
            analysisModel.Tcl.Add(recorder.WriteTcl());

            // Settings
            string eigenTcl = $"set lambdaN [eigen {solver} {vibrationNumber}]\n";
            analysisModel.Tcl.Add(eigenTcl);
            analysisModel.Tcl.Add("puts \"$lambdaN\"\n");
            analysisModel.Tcl.Add($"modalProperties -file \"{analysisModel.ModalAnalysisReportFile}\" -unorm\n");
            analysisModel.Tcl.Add("record\nwipe");

            analysisModel.Serialise();
            string stdout, stderr;
            int exitCode;
            try
            {
                (stdout, stderr, exitCode) = analysisModel.RunOpenSees();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            if (exitCode != 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Analysis Failed: OpenSees exited with an error. See log for details.");
                return;
            }

            // clean string

            String[] separator = { "Italy.", "Using DomainModalProperties" };
            String[] lineSeparator = { "\r\n", " " };

            var eigenValue = stderr.Split(separator,
                   StringSplitOptions.RemoveEmptyEntries)[1].Trim().Split(lineSeparator, StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x.Trim())).ToList();


            var frequencies = eigenValue.Select(eigen => Math.Sqrt(eigen)/(2 * Math.PI)).ToList();
            var period = frequencies.Select(x => 1/x).ToList();

            // add error handling
            var log = new List<string>() {stderr};

            // Finally assign the spiral to the output parameter.
            DA.SetDataList(0, log);
            DA.SetData(1, analysisModel);
            DA.SetDataList(2, eigenValue);
            DA.SetDataList(3, period);
            DA.SetDataList(4, frequencies);
        }

        protected override void BeforeSolveInstance()
        {
            var resultTypes = new List<string>();

            var _resultTypes = new List<Analysis.Solver> { Analysis.Solver.genBandArpack, Analysis.Solver.symmBandLapack, Analysis.Solver.fullGenLapack };

            foreach(Analysis.Solver solver in _resultTypes)
			{
                var result = Alpaca4d.Helper.EnumHelper.SolverTypeConvert(solver);
                resultTypes.Add(result);
            }

            ValueListUtils.UpdateValueLists(this, 2, resultTypes, null);

        }


        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Run_Natural_Vibration_Analysis__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{F16B4D71-DBE1-4CDA-BAEE-A8CB82EE23C2}");
    }
}