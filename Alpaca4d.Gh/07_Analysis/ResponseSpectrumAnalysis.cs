using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;

using Alpaca4d.Generic;
using Alpaca4d.Seismic;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Linear dynamic analysis by response spectrum (NTC 2018 7.3.3.1, EN 1998-1 4.3.3.3): the modes of
    /// the model, each pushed as far as the spectrum takes it, combined mode by mode (CQC or SRSS) and
    /// direction by direction (30% or SRSS).
    ///
    /// OpenSees solves the modes and records each one as a step, at a spectral acceleration of one; the
    /// model handed on carries the spectra and the rules, and the result components combine every
    /// quantity as they read it (<see cref="SpectrumCases"/>). So Nodal Displacements, Beam Forces,
    /// Reaction Force and View Results all read it, with Step picking the result case.
    /// </summary>
    public class ResponseSpectrumAnalysis : GH_Component
    {
        public ResponseSpectrumAnalysis()
          : base("Response Spectrum Analysis (Alpaca4d)", "Response Spectrum",
            "Linear dynamic analysis by response spectrum, as NTC 2018 7.3.3.1 and EN 1998-1 4.3.3.3 describe it: the " +
            "modes of the model, each pushed as far as the spectrum takes it at its period, combined mode by mode and " +
            "direction by direction.\n" +
            "Connect the assembled model and a spectrum, from Design Spectrum, for each direction the ground shakes in. " +
            "The loads are left out: this is the seismic action E alone. The mass is the model's own, its Mass Points " +
            "and its Mass from Loads.\n" +
            "Read the results with the result components. Step 0 is E, the directions combined; then one step per " +
            "direction, Ex, Ey, Ez. Every value is a peak, positive, without a sign, and each peaks on its own.",
            "Alpaca4d", "07_Analysis")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        private const int ModelInput = 0, FirstSpectrumInput = 1, ModesInput = 4, ModalInput = 5, DirectionalInput = 6, SolverInput = 7;
        private const int ModelOutput = 0, CasesOutput = 1, PeriodOutput = 2, MassRatioOutput = 3, BaseShearOutput = 4, LogOutput = 5;

        private static readonly string[] Axes = { "X", "Y", "Z" };

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel",
                "The assembled model, from Assemble Model. Not from Run Analysis - that clears the OpenSees domain when it finishes.",
                GH_ParamAccess.item);
            foreach (var axis in Axes)
            {
                pManager.AddGenericParameter($"Spectrum{axis}", $"Spectrum{axis}",
                    $"The spectrum the ground shakes with along {axis}, from Design Spectrum" +
                    (axis == "Z" ? " - its vertical component. " : ". ") +
                    "Left empty, the ground does not shake that way.",
                    GH_ParamAccess.item);
                pManager[pManager.ParamCount - 1].Optional = true;
            }
            pManager.AddIntegerParameter("Vibration Modes", "Vibration Modes",
                "How many modes to combine, from the lowest. Enough that together they move 85% of the mass in every " +
                "direction the ground shakes (NTC 2018), or 90% (EN 1998-1); MassRatio says how much they do.",
                GH_ParamAccess.item, 10);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Modal Combination", "Modal Combination",
                "How the modes are combined. CQC, the complete quadratic combination, which NTC 2018 asks for and which " +
                "holds for modes close together; or SRSS, the square root of the sum of the squares. Connect a Value List to pick.",
                GH_ParamAccess.item, "CQC");
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Directional Combination", "Directional Combination",
                "How the directions are combined into E. 30%: each direction in full with 30% of the others, the larger " +
                "taken - NTC 2018 7.3.5, EN 1998-1 4.3.3.5.2. SRSS: the square root of the sum of the squares. Connect a Value List to pick.",
                GH_ParamAccess.item, "30%");
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Solver", "Solver",
                "Connect a 'ValueList'\n-genBandArpack \n-fullGenLapack", GH_ParamAccess.item, "-genBandArpack");
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("AlpacaModel", "AlpacaModel",
                "The analysed model. Feed it to the result components and pick the case with their Step: see Cases.");
            pManager.Register_StringParam("Cases", "Cases", "What each step is: E, the directions combined, then Ex, Ey, Ez for each direction given.");
            pManager.Register_DoubleParam("Period", "Period", $"One period per mode, longest first [{Units.Time}]");
            pManager.Register_DoubleParam("MassRatio", "MassRatio", "How much of the mass the modes move together, along X, Y and Z [%].");
            pManager.Register_DoubleParam("BaseShear", "BaseShear",
                $"The total base shear along each direction the ground shakes, one per direction given, in order: the modes' Meff Sa combined [{Units.Force}]");
            pManager.Register_GenericParam("log", "log", "What OpenSees printed while solving. Read it when the analysis fails.");
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();

            ValueListUtils.UpdateValueLists(this, ModalInput, Enum.GetNames(typeof(ModalCombination)).ToList(), null);
            ValueListUtils.UpdateValueLists(this, DirectionalInput, new List<string> { "30%", "SRSS" }, null);

            // Not symmBandLapack: it cannot solve a vibration problem (Eigen.Unsuitable).
            var solvers = new List<Analysis.Solver> { Analysis.Solver.genBandArpack, Analysis.Solver.fullGenLapack };
            ValueListUtils.UpdateValueLists(this, SolverInput, solvers.Select(Alpaca4d.Helper.EnumHelper.SolverTypeConvert).ToList(), null);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var model = new Model();
            if (!DA.GetData(ModelInput, ref model)) return;

            var shaken = new List<(int Axis, ResponseSpectrum Spectrum)>();
            for (int axis = 0; axis < 3; axis++)
            {
                object value = null;
                if (!DA.GetData(FirstSpectrumInput + axis, ref value) || value == null)
                    continue;
                if (value is Grasshopper.Kernel.Types.GH_ObjectWrapper wrapper)
                    value = wrapper.Value;
                if (!(value is ResponseSpectrum spectrum))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Spectrum{Axes[axis]} is not a spectrum. Make one with Design Spectrum.");
                    return;
                }
                shaken.Add((axis, spectrum));
            }
            if (shaken.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Give a spectrum for at least one direction: SpectrumX, SpectrumY or SpectrumZ.");
                return;
            }
            WarnOfComponents(shaken);

            int modes = 10;
            string modal = "CQC", directional = "30%", solver = "-genBandArpack";
            DA.GetData(ModesInput, ref modes);
            DA.GetData(ModalInput, ref modal);
            DA.GetData(DirectionalInput, ref directional);
            DA.GetData(SolverInput, ref solver);

            if (modes < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Vibration Modes is at least 1.");
                return;
            }
            if (!Enum.TryParse((modal ?? "").Trim(), true, out ModalCombination modalRule))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Modal Combination is \"{modal}\". It is CQC or SRSS.");
                return;
            }
            DirectionalCombination directionalRule;
            switch ((directional ?? "").Trim().ToUpperInvariant())
            {
                case "30%": case "30": case "PERCENT30": directionalRule = DirectionalCombination.Percent30; break;
                case "SRSS": directionalRule = DirectionalCombination.SRSS; break;
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Directional Combination is \"{directional}\". It is 30% or SRSS.");
                    return;
            }
            string unsuitable = Alpaca4d.Eigen.Unsuitable(solver);
            if (unsuitable != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, unsuitable);
                return;
            }

            // Run Analysis ends its deck with "wipe", so an analysed model holds no model any more.
            if (model.IsAnalysed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "This AlpacaModel has already been analysed. Connect the Response Spectrum Analysis to the AlpacaModel output " +
                    "of Assemble Model, not to that of Run Analysis.");
                return;
            }

            if (!LicenseGate.AllowsModel(this, model))
                return;

            var files = AnalysisFiles.For(this, DA);
            var analysisModel = model.ShallowCopy();

            // The model without its load patterns: E is the ground shaking alone, and gravity is combined with it
            // afterwards, not run with it.
            analysisModel.Tcl = model.LoadPatternTclStart >= 0
                ? model.Tcl.Take(model.LoadPatternTclStart).ToList()
                : new List<string>(model.Tcl);

            // The results the MPCO Recorder on Assemble Model asks for, if there is one, but in a file of this
            // analysis's own: under the user's name it would be the file Run Analysis writes too.
            string recorderFile = files.OwnFile("recorder_spectrum", ".mpco");
            var chosen = model.Recorders?.OfType<Recorder>().FirstOrDefault();
            var recorder = chosen != null
                ? new Recorder(recorderFile, chosen.NodeResults, chosen.ElementResults)
                : Recorder.MpcoStatic(recorderFile);
            analysisModel.Recorders = new List<IRecorder> { recorder };
            analysisModel.Tcl.Add(recorder.WriteTcl());

            // As Natural Vibration: ARPACK does its shift-invert solves with the system set here, Mumps on every
            // core where the OpenSees has it, BandSPD where not; left unset, OpenSees falls back on ProfileSPD.
            // -fullGenLapack has a dense solver of its own.
            if (solver.IndexOf("fullGenLapack", StringComparison.OrdinalIgnoreCase) < 0)
                analysisModel.Tcl.Add(new Alpaca4d.SystemEquation("Auto").WriteTcl());

            analysisModel.ModalAnalysisReportFile = files.OwnFile("SpectrumReport", ".txt");
            analysisModel.Tcl.Add(SpectrumAnalysis.WriteTcl(solver, modes, shaken.Select(item => item.Axis), analysisModel.ModalAnalysisReportFile));
            analysisModel.Tcl.Add("wipe");
            analysisModel.FileName = files.OwnFile("AlpacaModel_spectrum", ".tcl");

            analysisModel.Serialise();
            string stderr;
            int exitCode;
            try
            {
                (_, stderr, exitCode) = analysisModel.RunOpenSees();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            DA.SetDataList(LogOutput, new List<string> { stderr });
            if (exitCode != 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    SpectrumAnalysis.ArpackShort(stderr, modes) ?? "Analysis Failed: OpenSees exited with an error. See log for details.");
                return;
            }

            // ARPACK may have been asked for a mode more than wanted (SpectrumAnalysis.Solved).
            var eigenvalues = Alpaca4d.Eigen.Read(stderr)?.Take(modes).ToList();
            if (eigenvalues == null || eigenvalues.Count == 0)
            {
                var said = Alpaca4d.Eigen.SolverMessages(stderr);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "OpenSees returned no eigenvalues." +
                    (said.Count > 0 ? " It said: " + string.Join(" / ", said) + "." : "") + " See log for details.");
                return;
            }
            if (eigenvalues.Any(lambda => !(lambda > 0.0) || double.IsInfinity(lambda)))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "A mode has no stiffness or no mass behind it (its eigenvalue is " +
                    $"{eigenvalues.First(lambda => !(lambda > 0.0) || double.IsInfinity(lambda))}). Check the supports, and that every free node carries mass.");
                return;
            }

            var periods = eigenvalues.Select(lambda => 2.0 * Math.PI / Math.Sqrt(lambda)).ToList();
            var directions = shaken.Select((item, k) => new SpectrumDirection(item.Axis, item.Spectrum, k * eigenvalues.Count, periods)).ToList();
            var cases = new SpectrumCases(periods, directions, modalRule, directionalRule);

            analysisModel.SpectrumCases = cases;
            analysisModel.IsStatic = true;
            analysisModel.IsTransient = false;
            analysisModel.IsModal = false;
            analysisModel.NumberOfModes = eigenvalues.Count;

            DA.SetData(ModelOutput, analysisModel);
            DA.SetDataList(CasesOutput, cases.Names);
            DA.SetDataList(PeriodOutput, periods);

            ModalMasses masses = null;
            try
            {
                masses = SpectrumAnalysis.ReadMasses(System.IO.File.ReadAllLines(analysisModel.ModalAnalysisReportFile), eigenvalues.Count);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The modal report could not be read: {ex.Message}");
            }

            if (masses != null)
            {
                DA.SetDataList(MassRatioOutput, Enumerable.Range(0, 3).Select(axis => 100.0 * masses.Ratio(axis)));
                DA.SetDataList(BaseShearOutput, directions.Select((direction, k) => cases.CombineScalar(k, masses.Along(direction.Axis))));
                WarnOfMissingMass(masses, directions);
            }
            else
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The modal report holds no masses, so MassRatio and BaseShear are empty.");

            if (modalRule == ModalCombination.SRSS)
                WarnOfCloseModes(periods);

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Step " + string.Join(", ", cases.Names.Select((name, i) => $"{i} is {name}")) + ". Every value is a peak without a sign. " +
                "What is worked out from several of them - a von Mises stress, a utilisation - is not a peak of its own: " +
                "the values it is made of need not happen together.");
        }

        /// <summary>Says so when a spectrum drawn for one component shakes the model along an axis of the other.</summary>
        private void WarnOfComponents(List<(int Axis, ResponseSpectrum Spectrum)> shaken)
        {
            foreach (var (axis, spectrum) in shaken)
            {
                SpectrumComponent? component =
                    spectrum is Ec8Spectrum ec8 ? ec8.Component :
                    spectrum is NtcSpectrum ntc ? ntc.Component : (SpectrumComponent?)null;
                if (component == null)
                    continue;

                bool vertical = axis == 2;
                if (vertical && component == SpectrumComponent.Horizontal)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "SpectrumZ is a horizontal spectrum. Set Component to Vertical on Design Spectrum for the vertical shaking.");
                else if (!vertical && component == SpectrumComponent.Vertical)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Spectrum{Axes[axis]} is a vertical spectrum, shaking the model sideways.");
            }
        }

        /// <summary>Says so when the modes move less of the mass than the code of a direction's spectrum asks.</summary>
        private void WarnOfMissingMass(ModalMasses masses, List<SpectrumDirection> directions)
        {
            foreach (var direction in directions)
            {
                double ratio = masses.Ratio(direction.Axis);
                double required = direction.Spectrum.MassRatioRequired;
                if (ratio < required)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"The modes move {100.0 * ratio:0.#}% of the mass along {direction.Name}; {direction.Spectrum.Code} asks for {100.0 * required:0}%. " +
                        "Ask for more Vibration Modes.");
            }
        }

        /// <summary>EN 1998-1 4.3.3.3.2: two periods within 10% of each other need CQC.</summary>
        private void WarnOfCloseModes(List<double> periods)
        {
            for (int i = 0; i + 1 < periods.Count; i++)
            {
                if (periods[i + 1] > 0.9 * periods[i])
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Modes {i + 1} and {i + 2} are within 10% of each other ({periods[i]:0.###} and {periods[i + 1]:0.###} {Units.Time}): " +
                        "SRSS treats them as independent, and they are not. Use CQC.");
                    return;
                }
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Response_Spectrum_Analysis__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{2F9C5B84-7D31-4A6E-B8E2-5C0D93A17F46}");
    }
}
