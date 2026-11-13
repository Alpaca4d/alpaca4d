using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Optimisation;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Topology Optimisation: finds where a solid needs its material. The solids of an assembled model -
    /// SSP Bricks, Ten Node or Four Node Tetrahedra - are the design domain; each gets a density, and the
    /// densities are found that make the model stiffest under its loads for the volume allowed
    /// (<see cref="TopologyOptimiser"/>, SIMP as in top3d). Void elements keep a billionth of their
    /// stiffness instead of being cut out, so the mesh never changes.
    ///
    /// The run goes on a thread of its own (<see cref="TopologyRun"/>), and the outputs follow it as it
    /// goes. Run starts it; switching Run off, or changing any input, stops it.
    /// </summary>
    public partial class TopologyOptimisation : TopologyComponent
    {
        private const string SimpHelp =
            "Finds where a solid needs its material. Every solid of the model - SSP Bricks, Ten Node or Four Node Tetrahedra - " +
            "gets a density between 0 and 1 and a stiffness of density cubed, and the densities are found that make the model " +
            "stiffest under its loads with VolumeFraction of the material: SIMP with the optimality criteria update and a density " +
            "filter, as top3d. Removed material is never cut out: a void element keeps a billionth of its stiffness, so the mesh " +
            "stays as it is.\n" +
            "Bricks are solved as OpenSees' eight point brick (stdBrick), on the same nodes, because an SSP Brick fixes part of " +
            "its stiffness when it is built. Every solid needs an Elastic Isotropic Material. Beams, shells and links in the " +
            "model take no part: they keep their full stiffness. One OpenSees stays open for the whole run.\n" +
            "Set Run to true to start. The outputs follow the run as it goes. Switching Run off pauses it; on again, it goes on " +
            "from where it stopped, and so does a run given more Iterations or a smaller Tolerance after it ended. Changing any " +
            "other input starts it again from the beginning.\n" +
            "Once it stops, AlpacaModel is the design as a model of its own, solid and void, to analyse.";

        public TopologyOptimisation()
          : base("Topology Optimisation (Alpaca4d)", "Topology", Help, "Alpaca4d", "12_Optimisation")
        {
            UpdateMessage();
        }

        private const string SimpUnit = "TopologyOptimisation", BesoUnit = "BesoTopology", MultiMaterialUnit = "MultiMaterialTopology";

        private const string Help =
            "Finds where a solid needs its material. Method picks how: SIMP (the default) gives every solid a density and finds " +
            "the stiffest design for VolumeFraction of the material, as top3d; BESO starts from every solid and takes a few percent " +
            "of the volume away each iteration, each element solid or void all along; Multi-Material finds where each of several " +
            "materials goes, and where none is needed. Right click the component, Method, to switch; the inputs and outputs change " +
            "with it, and a switch starts any run again.\n" +
            "The solids of the model - SSP Bricks, Ten Node or Four Node Tetrahedra - are the design domain. A removed element keeps " +
            "a billionth of its stiffness instead of being cut out, so the mesh never changes. Every solid needs an Elastic Isotropic " +
            "Material. Set Run to true to start; the outputs follow the run as it goes.\n" +
            "Once it stops, AlpacaModel is the design as a model of its own, to analyse.\n\n" +
            "SIMP: " + SimpHelp + "\n\nBESO: " + BesoHelp + "\n\nMulti-Material: " + MultiMaterialHelp;

        public override string UnitMenuName => "Method";
        public override string UnitMenuHeader => "Select the method";
        protected override string DefaultEvaluationUnit => SimpUnit;

        private void UpdateMessage()
        {
            string name = ActiveUnit?.DisplayName ?? this.NickName;
            this.Message = $"{name}\n{this.Category}";
        }

        protected override void SwitchUnit(EvaluationUnit unit, bool recompute = true, bool recordEvent = true)
        {
            // A run is of one method: the other's inputs are not its inputs.
            Run?.Stop();
            Run = null;
            _surfaces = new List<(Mesh mesh, Color colour)>();
            base.SwitchUnit(unit, recompute, recordEvent);
            UpdateMessage();
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // All inputs belong to the evaluation unit, see RegisterEvaluationUnits.
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // All outputs belong to the evaluation unit, see RegisterEvaluationUnits.
        }

        private const int ModelInput = 0, KeepInput = 1, VolumeFractionInput = 2, IterationsInput = 3, RunInput = 4;
        private const int PenaltyInput = 5, FilterRadiusInput = 6, ToleranceInput = 7, MinStiffnessInput = 8, MoveInput = 9;
        private const int ThresholdInput = 10, SystemInput = 11;
        // Step came after the Settings menu's inputs, and is registered last so that their places stay as they were.
        private const int StepInput = 12;

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            RegisterSimp(mngr);
            RegisterBeso(mngr);
            RegisterMultiMaterial(mngr);
        }

        private void RegisterSimp(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(SimpUnit, "SIMP", "Find where a solid needs its material.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Topology_Optimisation__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "The assembled model, from Assemble Model. Its solids are the design domain; supports, loads and any beams or shells stay as they are.",
                GH_ParamAccess.item);

            unit.RegisterInputParam(new Param_String(), "Keep", "Keep",
                "Solids to keep whatever the optimisation finds - around supports and loads, say. Left empty, every solid is designed.\n" +
                "Takes an ElementId (Support*), an element tag (47), or a regular expression (regex:^Load), mixed freely in one list.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "VolumeFraction", "VolumeFraction",
                "The share of the designed solids' volume to keep, more than 0 and at most 1. The solids in Keep come on top.",
                GH_ParamAccess.item, new GH_Number(0.3));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Integer(), "Iterations", "Iterations",
                "How many iterations at most, counted from the first. A run usually settles in 50 to 200. Raise it after a run " +
                "and the run goes on from where it stopped; lower it below what has run and it starts again from the beginning.",
                GH_ParamAccess.item, new GH_Integer(100));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Boolean(), "Run", "Run",
                "True runs the optimisation, false pauses it: on again, it goes on from where it stopped. A run whose inputs change, " +
                "Iterations and Tolerance apart, starts again from the beginning.",
                GH_ParamAccess.item, new GH_Boolean(false));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Penalty", "Penalty",
                "The SIMP penalty p: an element of density x has x^p of its stiffness. 3 pushes the result to solid or void.",
                GH_ParamAccess.item, new GH_Number(3.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "FilterRadius", "FilterRadius",
                $"How far an element's density is averaged over [{Units.Length}]: the thinnest member the result can have is about this " +
                "wide. Left empty, 1.5 times the typical element size, top3d's 1.5 elements.",
                GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Tolerance", "Tolerance",
                "The run stops when no density changes by more than this in an iteration. Make it smaller after a run and the run " +
                "goes on from where it stopped.",
                GH_ParamAccess.item, new GH_Number(0.01));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "MinStiffness", "MinStiffness",
                "The stiffness a void element keeps, as a fraction of the solid's. Small enough to be nothing, large enough to hold its nodes.",
                GH_ParamAccess.item, new GH_Number(1e-9));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Move", "Move",
                "The most a density can change in one iteration.",
                GH_ParamAccess.item, new GH_Number(0.2));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Threshold", "Threshold",
                "The density from which an element counts as solid, for the Surface, Solid and AlpacaModel outputs. It changes nothing in the run.",
                GH_ParamAccess.item, new GH_Number(0.5));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "System", "System",
                "How the system of equations is stored and solved. Connect a 'ValueList'.\n" +
                "Auto is the default: Mumps on every core when the OpenSees in use has it, BandSPD when it does not.\n" +
                "BandGen, BandSPD, ProfileSPD, SuperLU, UmfPack, SparseSYM, SparseSPD, SparseGeneral, FullGeneral, Mumps, Auto",
                GH_ParamAccess.item, new GH_String(DefaultSystem));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterOutputParam(new Param_Mesh(), "Surface", "Surface",
                "The outline of the material kept: where the density crosses Threshold, closed where it meets the edge of the domain. " +
                "It outlines what Solid holds: without the pieces left out, with the elements that join a member's steps, and " +
                "without the crumbs a thin member's surface breaks into where it fades out around Threshold.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Solid", "Solid",
                "The solids at or above Threshold that make the structure, as new copies of the model's: pieces no face joins to a part " +
                "the supports hold still are left out, and where a member's elements meet only along an edge or at a corner, the " +
                "element that joins them is in. AlpacaModel is these, assembled with the supports and loads.");
            unit.RegisterOutputParam(new Param_Number(), "Density", "Density",
                "Each solid's density, 0 to 1, in the order of the Element output.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Element", "Element", ElementIdentity.ElementOutput);
            unit.RegisterOutputParam(new Param_Number(), "Compliance", "Compliance",
                $"The compliance F.u of each iteration's design, the work of the loads [{Units.Force}·{Units.Length}]: the lower, the stiffer.");
            unit.RegisterOutputParam(new Param_Number(), "Volume", "Volume",
                "Each iteration's share of the designed solids' volume.");
            unit.RegisterOutputParam(new Param_Boolean(), "Converged", "Converged",
                "True when the run ended because no density moved by more than Tolerance.");
            unit.RegisterOutputParam(new Param_GenericObject(), "History", "History",
                "One item per iteration: compliance, volume, change, and how long the analysis and the update took. " +
                "A Panel shows each as a line of text; Deconstruct Anything splits it into its values.");
            unit.RegisterOutputParam(new Param_String(), "log", "log", "What OpenSees printed. Read it when the run fails.");

            unit.RegisterInputParam(new Param_Integer(), "Step", "Step",
                "Which iteration's design to show: 0 the one the run starts from, 1 the one after the first iteration, and so on. " +
                "Left empty, the latest. Connect a slider and drag it to watch the run again; every design is kept, small.",
                GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
            unit.RegisterOutputParam(new Param_Integer(), "Step", "Step",
                "The iteration whose design Surface, Solid, Density and AlpacaModel show.");
            // AlpacaModel came after Step, and is registered last for the same reason.
            unit.RegisterOutputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "The design as a model to analyse, once the run has stopped: the Solid output at full stiffness, the model's other " +
                "elements, and the supports, point loads and masses that stand on them. Feed it to an analysis as you would " +
                "Assemble Model's.");

            var settings = new GH_ExtendableMenu(0, "TopologyOptimisation_Settings")
            {
                Name = "Settings",
                Header = "Penalty, filter radius, tolerance, void stiffness, move limit, threshold and solver; history and log"
            };
            for (int i = PenaltyInput; i <= SystemInput; i++)
                settings.RegisterInputPlug(unit.Inputs[i]);
            foreach (var name in new[] { "History", "log" })
                settings.RegisterOutputPlug(unit.Outputs.First(plug => plug.Parameter.Name == name));
            unit.AddMenu(settings);
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            _surfaces = new List<(Mesh mesh, Color colour)>();
            int systemInput = ActiveUnit?.Name == BesoUnit ? BesoSystemInput : ActiveUnit?.Name == MultiMaterialUnit ? MmSystemInput : SystemInput;
            ValueListUtils.UpdateValueLists(this, systemInput, Enum.GetNames(typeof(Alpaca4d.SystemEquation.SystemType)).ToList(), null);
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            switch (unit?.Name)
            {
                case BesoUnit: SolveBeso(DA); return;
                case MultiMaterialUnit: SolveMultiMaterial(DA); return;
                default: SolveSimp(DA); return;
            }
        }

        private void SolveSimp(IGH_DataAccess DA)
        {
            if (DA.Iteration > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "One model at a time: give Topology Optimisation a single AlpacaModel.");
                return;
            }

            var model = new Alpaca4d.Model();
            double volumeFraction = 0.3, penalty = 3.0, filterRadius = 0.0, tolerance = 0.01, minStiffness = 1e-9, move = 0.2, threshold = 0.5;
            int iterations = 100;
            bool run = false;
            string system = DefaultSystem;

            if (!DA.GetData(ModelInput, ref model)) return;
            var keep = ElementFilterInput.Read(DA, KeepInput, this);
            DA.GetData(VolumeFractionInput, ref volumeFraction);
            DA.GetData(IterationsInput, ref iterations);
            DA.GetData(RunInput, ref run);
            DA.GetData(PenaltyInput, ref penalty);
            DA.GetData(FilterRadiusInput, ref filterRadius);
            DA.GetData(ToleranceInput, ref tolerance);
            DA.GetData(MinStiffnessInput, ref minStiffness);
            DA.GetData(MoveInput, ref move);
            DA.GetData(ThresholdInput, ref threshold);
            DA.GetData(SystemInput, ref system);
            int stepValue = -1;
            int? step = Params.Input.Count > StepInput && DA.GetData(StepInput, ref stepValue) && stepValue >= 0 ? stepValue : (int?)null;

            if (model == null || model.Bricks.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The model has no solids to optimise. Give it the AlpacaModel output of Assemble Model, " +
                                                                "with SSP Bricks or tetrahedra.");
                return;
            }

            var settings = new TopologySettings
            {
                VolumeFraction = volumeFraction,
                Penalty = penalty,
                FilterRadius = filterRadius,
                MaxIterations = iterations,
                Tolerance = tolerance,
                MinStiffness = minStiffness,
                Move = move,
            };
            if (!Valid(settings, threshold))
                return;

            var systemObj = SystemOf(system);
            if (systemObj == null)
                return;

            WarnAboutModel(model);
            var kept = Kept(keep, model);

            // What makes a run: everything but Iterations and Tolerance, which only say when it stops, so a
            // run can go on under new ones. The model by which object it is. The run asks for solutions of
            // this component alone, which hand it the same model; a model assembled again is a new one,
            // even when nothing changed, as every material gets a new tag when its component solves again.
            string key = string.Join("|", new object[]
            {
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(model), string.Join(",", kept.Select(b => b.Id)),
                volumeFraction, penalty, filterRadius, minStiffness, move, systemObj.Type,
            }.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));

            if (!run)
                Run?.Stop();
            else
            {
                var next = Next(Run, key, iterations, tolerance, (it, tol) => it.Change < tol);
                if (next != Going.On)
                {
                    var previous = next == Going.OnFromWhereItStopped ? Run.Result : null;
                    Run?.Stop();
                    Run = null;
                    Run = Start(DA, model, kept, settings, systemObj, key, previous);
                    if (Run == null)
                        return;
                }
            }

            if (Run == null || Run.Key != key)
            {
                if (!run)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Set Run to true to start the optimisation.");
                return;
            }

            ShowDensities(DA, Run, threshold, step,
                done => $"Densities were still moving after {done} iterations. The outputs are the last design; raise Iterations to go on from there.");
        }

        private bool Valid(TopologySettings settings, double threshold)
        {
            string problem =
                !(settings.VolumeFraction > 0.0 && settings.VolumeFraction <= 1.0) ? "VolumeFraction has to be more than 0 and at most 1." :
                settings.MaxIterations < 1 ? "Iterations has to be at least 1." :
                !(settings.Penalty >= 1.0) ? "Penalty has to be at least 1." :
                settings.FilterRadius < 0.0 ? "FilterRadius cannot be negative." :
                !(settings.Tolerance > 0.0) ? "Tolerance has to be positive." :
                !(settings.MinStiffness > 0.0 && settings.MinStiffness < 1.0) ? "MinStiffness has to be more than 0 and less than 1." :
                !(settings.Move > 0.0 && settings.Move <= 1.0) ? "Move has to be more than 0 and at most 1." :
                !(threshold > 0.0 && threshold < 1.0) ? "Threshold has to be between 0 and 1." :
                null;

            if (problem != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return problem == null;
        }

        /// <summary>
        /// Reads the model into cells and a deck and starts a run, from the beginning or from where
        /// <paramref name="previous"/> stopped; null, with an error, when it cannot.
        /// </summary>
        private TopologyRun Start(IGH_DataAccess DA, Alpaca4d.Model model, HashSet<IBrick> kept, TopologySettings settings, SystemEquation system,
                                  string key, TopologyResult previous)
        {
            if (!Prepare(DA, model, kept, "Topology Optimisation takes", "Topology", out var tcl, out var cells, out var openSees, out var folder, out var stem))
                return null;
            return TopologyRun.Start(this, key, model, tcl, cells, settings, system, openSees, folder, stem, previous);
        }

        private bool DrawsOwnSurfaces => ActiveUnit?.Name == MultiMaterialUnit;

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            // Multi-Material draws its surfaces in their materials' colours, rather than the outputs' preview.
            if (!DrawsOwnSurfaces)
            {
                base.DrawViewportMeshes(args);
                return;
            }
            if (this.Hidden || this.Locked) return;
            foreach (var (mesh, colour) in _surfaces)
                if (mesh != null && mesh.Faces.Count > 0)
                    args.Display.DrawMeshShaded(mesh, new DisplayMaterial(colour, this.Attributes.Selected ? 0.3 : 0.0));
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (!DrawsOwnSurfaces)
                base.DrawViewportWires(args);
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                if (!DrawsOwnSurfaces)
                    return base.ClippingBox;
                var box = BoundingBox.Empty;
                foreach (var (mesh, _) in _surfaces)
                    if (mesh != null)
                        box.Union(mesh.GetBoundingBox(false));
                return box.IsValid ? box : base.ClippingBox;
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Topology_Optimisation__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{8F3C2A61-5D4E-4B7A-9C1E-2B6D7A0F4E93}");
    }
}
