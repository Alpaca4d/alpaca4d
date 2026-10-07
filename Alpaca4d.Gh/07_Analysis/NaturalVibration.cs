using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Natural Vibration Analysis, with what Modal Analysis Report used to give folded into it.
    ///
    /// The report is written by this analysis and read by nothing else, so a second component to
    /// split it was only ever wired straight to this one. Its nine sections are outputs of a Modal
    /// Report menu that stays folded until it is opened: the body shows the modes, the menu the
    /// masses behind them.
    ///
    /// A switcher with a single unit for the sake of that menu, as Utilisation is - only a unit's
    /// parameters can be plugs of a menu. It replaces the plain component of the same name, which
    /// is kept hidden so older definitions open, and is swapped for this by
    /// <see cref="NaturalVibrationUpgrader"/>. The inputs are the same, in the same order; the
    /// log has moved from first output to after the frequencies, so the results come first and
    /// the upgrader rewires the outputs by name rather than by position.
    /// </summary>
    public class NaturalVibration : GH_SwitcherComponent
    {
        public NaturalVibration()
          : base("Natural Vibration Analysis (Alpaca4d)", "Natural Vibration",
            "Solves the eigenvalue problem of the model and returns its modes, eigenvalues, periods " +
            "and frequencies.\n" +
            "It needs no Analysis Settings - an eigenvalue problem has nothing to converge. Connect " +
            "the assembled model rather than an analysed one, then feed the solved model to Nodal " +
            "Displacements to read a mode shape.\n" +
            "The modal report - masses, centre of mass, participation factors and mass ratios - is in " +
            "the Modal Report menu below the component.",
            "Alpaca4d", "07_Analysis")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // All inputs belong to the evaluation unit, see RegisterEvaluationUnits.
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // All outputs belong to the evaluation unit, see RegisterEvaluationUnits.
        }

        private const int ModelInput = 0, ModesInput = 1, SolverInput = 2;
        internal const int ModelOutput = 0, EigenvaluesOutput = 1, PeriodOutput = 2, FrequenciesOutput = 3, LogOutput = 4;

        /// <summary>The first of the report's outputs; the rest follow in the order of <see cref="ReportOutputs"/>.</summary>
        private const int FirstReportOutput = 5;

        /// <summary>
        /// The report's outputs, named and described as Modal Analysis Report named them, one per
        /// entry of <see cref="Alpaca4d.Eigen.ReportSectionNumbers"/>.
        /// </summary>
        private static readonly (string Name, string Description)[] ReportOutputs =
        {
            ("EigenValueAnalysis", "Section 2 of the report, as text: eigenvalue, frequency and period per mode."),
            ("TotalMassOfStructure", "Section 3 of the report, as text: the mass of the whole model, per direction."),
            ("TotalFreeMass", "Section 4 of the report, as text: the mass on unrestrained degrees of freedom, which is the mass the modes can actually move."),
            ("CenterOfMass", "Section 5 of the report, as text: where the mass of the model sits."),
            ("ModalParticipationFactors", "Section 6 of the report, as text: the participation factor of each mode, per direction."),
            ("ModalParticipationMasses", "Section 7 of the report, as text: how much mass each mode moves, per direction."),
            ("ModalParticipationMasses_Cumulative", "Section 8 of the report, as text: participating mass summed over the modes up to each one."),
            ("ModalParticipationMassesRatio(%)", "Section 9 of the report, as text: participating mass as a percentage of the total, per mode and direction."),
            ("ModalParticipationMassesRatio(%)_Cumulative", "Section 10 of the report, as text: the percentages of section 9 summed over the modes up to each one. This is the column a code check against a 90% threshold reads."),
        };

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit("NaturalVibration", "Natural Vibration", "Solve the eigenvalue problem of the model.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Run_Natural_Vibration_Analysis__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "The assembled model, from Assemble Model. Not from Run Analysis - that clears the OpenSees domain when it finishes, leaving nothing to solve.",
                GH_ParamAccess.item);

            unit.RegisterInputParam(new Param_Integer(), "Vibration Modes", "Vibration Modes",
                "How many modes to solve for, starting at the lowest. Ask for enough that the cumulative participating mass reaches whatever your code requires.",
                GH_ParamAccess.item, new GH_Integer(1));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "Solver", "Solver",
                "Connect a 'ValueList'\n-genBandArpack \n-fullGenLapack",
                GH_ParamAccess.item, new GH_String("-genBandArpack"));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterOutputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel", "The solved model. Feed it to Nodal Displacements to read a mode shape.");
            unit.RegisterOutputParam(new Param_Number(), "Eigenvalues", "Eigenvalues", "One eigenvalue per mode, lowest first. Each is omega squared, in rad²/s².");
            unit.RegisterOutputParam(new Param_Number(), "Period", "Period", $"One period per mode, longest first [{Units.Time}]");
            unit.RegisterOutputParam(new Param_Number(), "Frequencies", "Frequencies", "One frequency per mode, lowest first, in Hz.");
            unit.RegisterOutputParam(new Param_GenericObject(), "log", "log", "What OpenSees printed while solving. Read it when the analysis fails.");

            foreach (var output in ReportOutputs)
                unit.RegisterOutputParam(new Param_String(), output.Name, output.Name, output.Description);

            // Folded away until it is opened.
            var report = new GH_ExtendableMenu(0, "NaturalVibration_ModalReport")
            {
                Name = "Modal Report",
                Header = "The modal report, a section per output: masses, centre of mass, participation factors and mass ratios"
            };
            for (int i = 0; i < ReportOutputs.Length; i++)
                report.RegisterOutputPlug(unit.Outputs[FirstReportOutput + i]);
            unit.AddMenu(report);
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            var model = new Model();
            if (!DA.GetData(ModelInput, ref model)) return;

            var vibrationNumber = 1;
            DA.GetData(ModesInput, ref vibrationNumber);

            string solver = "";
            DA.GetData(SolverInput, ref solver);

            string unsuitable = Alpaca4d.Eigen.Unsuitable(solver);
            if (unsuitable != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, unsuitable);
                return;
            }

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

            // Free up to License.FreeElementLimit elements. Past it, no license means no analysis.
            if (!LicenseGate.AllowsModel(this, model))
                return;

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
            var recorder = Alpaca4d.Recorder.MpcoEigen(recorderName);
            analysisModel.IsModal = true;
            analysisModel.NumberOfModes = vibrationNumber;

            analysisModel.Recorders.Add(recorder);
            analysisModel.Tcl.Add(recorder.WriteTcl());

            // Settings
            analysisModel.Tcl.Add(Alpaca4d.Eigen.WriteTcl(solver, vibrationNumber));
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

            // OpenSees prints to its error stream, so that is the log, failed or not.
            DA.SetDataList(LogOutput, new List<string>() { stderr });

            if (exitCode != 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Analysis Failed: OpenSees exited with an error. See log for details.");
                return;
            }

            var eigenValue = Alpaca4d.Eigen.Read(stderr);
            if (eigenValue == null || eigenValue.Count == 0)
            {
                var said = Alpaca4d.Eigen.SolverMessages(stderr);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "OpenSees returned no eigenvalues." +
                    (said.Count > 0 ? " It said: " + string.Join(" / ", said) + "." : "") + " See log for details.");
                return;
            }

            var frequencies = eigenValue.Select(eigen => Math.Sqrt(eigen) / (2 * Math.PI)).ToList();
            var period = frequencies.Select(x => 1 / x).ToList();

            DA.SetData(ModelOutput, analysisModel);
            DA.SetDataList(EigenvaluesOutput, eigenValue);
            DA.SetDataList(PeriodOutput, period);
            DA.SetDataList(FrequenciesOutput, frequencies);

            SetReport(DA, analysisModel.ModalAnalysisReportFile);
        }

        /// <summary>
        /// The report, a section per output. Read whether the menu is open or not, so a wire from a
        /// folded output still carries it. A report that cannot be read leaves them empty and says
        /// why, without taking the modes with it.
        /// </summary>
        private void SetReport(IGH_DataAccess DA, string reportFile)
        {
            string[] lines;
            try
            {
                lines = System.IO.File.ReadAllLines(reportFile);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The modal report could not be read: {ex.Message}");
                return;
            }

            var sections = Alpaca4d.Eigen.ReportSections(lines);
            for (int i = 0; i < ReportOutputs.Length && i < sections.Count; i++)
                DA.SetDataList(FirstReportOutput + i, sections[i]);
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();

            // Not symmBandLapack: it cannot solve a vibration problem (Eigen.Unsuitable).
            var solvers = new List<Analysis.Solver> { Analysis.Solver.genBandArpack, Analysis.Solver.fullGenLapack };
            var names = solvers.Select(Alpaca4d.Helper.EnumHelper.SolverTypeConvert).ToList();

            ValueListUtils.UpdateValueLists(this, SolverInput, names, null);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Run_Natural_Vibration_Analysis__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{3B7D2E95-6A41-4C8F-9E1D-5F0A8C27B6D3}");
    }
}
