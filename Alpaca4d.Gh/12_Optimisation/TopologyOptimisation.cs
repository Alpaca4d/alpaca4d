using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
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
    public class TopologyOptimisation : GH_SwitcherComponent
    {
        private const string Help =
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

        private const int ModelInput = 0, KeepInput = 1, VolumeFractionInput = 2, IterationsInput = 3, RunInput = 4;
        private const int PenaltyInput = 5, FilterRadiusInput = 6, ToleranceInput = 7, MinStiffnessInput = 8, MoveInput = 9;
        private const int ThresholdInput = 10, SystemInput = 11;
        // Step came after the Settings menu's inputs, and is registered last so that their places stay as they were.
        private const int StepInput = 12;

        private const string DefaultSystem = "Auto";

        /// <summary>The run in progress, or the last one.</summary>
        internal TopologyRun Run { get; private set; }

        private int _runs;

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit("TopologyOptimisation", "Topology Optimisation", "Find where a solid needs its material.");
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
            ValueListUtils.UpdateValueLists(this, SystemInput, Enum.GetNames(typeof(Alpaca4d.SystemEquation.SystemType)).ToList(), null);
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
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

            SystemEquation systemObj;
            try { systemObj = new SystemEquation(string.IsNullOrWhiteSpace(system) ? DefaultSystem : system.Trim()); }
            catch (ArgumentException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"System \"{system}\" is not one of {string.Join(", ", Enum.GetNames(typeof(SystemEquation.SystemType)))}.");
                return;
            }

            string others = OtherElements(model);
            if (others != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The model also has {others}. Only solids are optimised: the rest keep their full stiffness and are analysed " +
                    "with the solids, but their strain energy is not in Compliance.");

            if (model.LoadPatterns.Any(pattern => pattern.Load != null && pattern.Load.Any(load => load is Alpaca4d.Loads.Gravity)))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "The model's self-weight is that of every solid, solid: it stays on the elements the optimisation empties. " +
                    "Leave Gravity out of the model to optimise for the other loads alone.");

            var bricks = model.Bricks;
            var kept = keep.MatchesEverything ? new HashSet<IBrick>() : new HashSet<IBrick>(keep.SelectIndices(bricks).Select(i => bricks[i]));
            if (!keep.MatchesEverything && kept.Count == 0)
                foreach (var problem in keep.Unmatched(model.Elements))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, problem);

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
                var next = Next(Run, key, iterations, tolerance);
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

            Show(DA, Run, threshold, step);
        }

        private enum Going { On, OnFromWhereItStopped, FromTheBeginning }

        /// <summary>
        /// What Run on does with the run there is, given the inputs: lets it be - running, or finished with
        /// nothing left to do; goes on from where it stopped - it stopped short of <paramref name="iterations"/>,
        /// or was stopped, with the densities still moving by more than <paramref name="tolerance"/>; or
        /// starts again - nothing has run, the run was of other inputs, or it has already made more
        /// iterations than asked for now, and the design after fewer is not kept.
        ///
        /// A run under way takes a new Iterations and Tolerance as it goes: it reads them every iteration.
        /// </summary>
        private static Going Next(TopologyRun run, string key, int iterations, double tolerance)
        {
            if (run == null || run.Key != key)
                return Going.FromTheBeginning;

            if (run.IsRunning)
            {
                if (run.Done >= iterations)
                    return Going.FromTheBeginning;
                run.Settings.MaxIterations = iterations;
                run.Settings.Tolerance = tolerance;
                return Going.On;
            }

            var result = run.Result;
            if (result == null)
                return run.StopRequested ? Going.FromTheBeginning : Going.On;   // failed: again once Run is switched off and on

            int done = result.History.Count;
            if (done > iterations)
                return Going.FromTheBeginning;
            bool moving = done == 0 || !(result.History[done - 1].Change < tolerance);
            if (done < iterations && moving)
                return Going.OnFromWhereItStopped;

            run.Settings.MaxIterations = iterations;
            run.Settings.Tolerance = tolerance;
            return Going.On;
        }

        /// <summary>The model's elements that are not solids, counted by kind - "2 beams and 1 shell" - or null when there are none.</summary>
        private static string OtherElements(Alpaca4d.Model model)
        {
            string Count(int n, string one, string many) => n == 0 ? null : n == 1 ? $"1 {one}" : $"{n} {many}";

            int trusses = model.Beams.OfType<Alpaca4d.Element.Truss>().Count();
            var kinds = new[]
            {
                Count(model.Beams.Count - trusses, "beam", "beams"),
                Count(trusses, "truss", "trusses"),
                Count(model.Shells.Count, "shell", "shells"),
                Count(model.Links.Count, "link", "links"),
            }.Where(kind => kind != null).ToList();

            if (kinds.Count == 0)
                return null;
            return kinds.Count == 1 ? kinds[0] : string.Join(", ", kinds.Take(kinds.Count - 1)) + " and " + kinds.Last();
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
            if (!LicenseGate.Allows(this, "Topology Optimisation"))
                return null;

            string openSees = Alpaca4d.Application.OpenSees;
            if (string.IsNullOrWhiteSpace(openSees) || !File.Exists(openSees))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "OpenSees is not where Alpaca4d looks for it. Set it in Alpaca4d → Settings in the Grasshopper menu.");
                return null;
            }

            List<string> tcl;
            List<SolidCell> cells;
            try
            {
                tcl = TopologyDeck.From(model, kept.Contains, out cells);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return null;
            }

            if (cells.All(cell => cell.Passive))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Keep holds every solid, so there is nothing left to optimise.");
                return null;
            }

            var files = AnalysisFiles.For(this, DA);
            string stem = "Topology_" + this.InstanceGuid.ToString("N").Substring(0, 8) + "_" + (++_runs);
            return TopologyRun.Start(this, key, model, tcl, cells, settings, system, openSees, files.Folder, stem, previous);
        }

        /// <summary>
        /// The outputs of the design after iteration <paramref name="step"/>, or of the latest when it is null or
        /// beyond what has run, and what the run is doing.
        /// </summary>
        private void Show(IGH_DataAccess DA, TopologyRun run, double threshold, int? step)
        {
            var (densities, history) = run.Snapshot();
            var model = run.Model;
            var cells = run.Cells;

            if (run.Failure != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, run.Failure);

            // The latest from the run itself, to the last digit; an earlier one from the frames kept.
            int shown = history.Count;
            if (step.HasValue && step.Value < history.Count && step.Value < run.Frames.Count)
            {
                shown = step.Value;
                densities = run.Frames[shown];
            }

            var design = DesignOf(run, densities, threshold, shown);
            var pieces = design.Pieces;
            if (pieces.LeftOut > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{pieces.LeftOut} solid element{(pieces.LeftOut == 1 ? "" : "s")} in {pieces.PiecesLeftOut} piece{(pieces.PiecesLeftOut == 1 ? " is" : "s are")} " +
                    "left out of Surface, Solid and AlpacaModel: no face joins them to a part the supports hold still.");
            if (pieces.Bridged > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{pieces.Bridged} element{(pieces.Bridged == 1 ? "" : "s")} under Threshold {(pieces.Bridged == 1 ? "is" : "are")} in Solid and AlpacaModel all the same: " +
                    "they join steps of a member that meet only along an edge or at a corner, where the member would hinge. Surface shows them solid.");

            // The surface outlines what Solid holds: the loose pieces void, their neighbours staying under the
            // threshold, and the elements joining steps solid.
            var outline = densities.ToArray();
            for (int i = 0; i < outline.Length; i++)
            {
                bool solid = cells[i].Passive || outline[i] >= threshold;
                if (solid && !pieces.Kept[i])
                    outline[i] = 0.0;
                else if (!solid && pieces.Kept[i])
                    outline[i] = 1.0;
            }
            var surface = new DensitySurface(cells, outline, tag => model.Nodes[tag - 1].Pos, threshold);
            var mesh = new Mesh();
            mesh.Vertices.AddVertices(surface.Vertices);
            foreach (var t in surface.Triangles)
                mesh.Faces.AddFace(t[0], t[1], t[2]);
            // Cuts that fell on a node at exactly the threshold stand on one point as vertices of their own.
            mesh.Vertices.CombineIdentical(true, true);
            mesh.Faces.CullDegenerateFaces();
            mesh.Normals.ComputeNormals();
            mesh.Compact();

            // Where a thin member fades out just around Threshold, the surface through the averaged
            // densities breaks into crumbs apart from the rest, though Solid joins their elements to it.
            var parts = mesh.SplitDisjointPieces();
            if (parts.Length > 1)
            {
                var volumes = parts.Select(part => Math.Abs(VolumeMassProperties.Compute(part, true, false, false, false)?.Volume ?? 0.0)).ToArray();
                double largest = volumes.Max();
                var whole = Enumerable.Range(0, parts.Length).Where(i => volumes[i] >= 0.01 * largest).ToList();
                int crumbs = parts.Length - whole.Count;
                if (crumbs > 0)
                {
                    mesh = new Mesh();
                    foreach (int i in whole)
                        mesh.Append(parts[i]);
                    mesh.Normals.ComputeNormals();
                    mesh.Compact();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Surface leaves out {crumbs} crumb{(crumbs == 1 ? "" : "s")}, under a hundredth of the largest piece each: where a thin " +
                        "member fades out around Threshold its surface breaks up, though Solid joins its elements to the rest.");
                }
            }

            var bricks = model.Bricks;
            Set(DA, "Surface", new[] { mesh });
            // Copies: assembled again, the model's own would be renumbered under the run.
            Set(DA, "Solid", Enumerable.Range(0, bricks.Count).Where(i => pieces.Kept[i]).Select(i => TopologyDesign.Copy(bricks[i])));
            Set(DA, "Density", densities);
            Set(DA, "Element", bricks);
            Set(DA, "Compliance", history.Select(it => it.Compliance));
            Set(DA, "Volume", history.Select(it => it.Volume));
            Set(DA, "Converged", new[] { run.Result?.Converged ?? false });
            Set(DA, "History", history);
            Set(DA, "log", new[] { run.Log });
            Set(DA, "Step", new[] { shown });

            // The design as a model once the run has stopped: assembling it every second would slow the run down.
            if (!run.IsRunning)
            {
                var analysed = design.Model(run.Model, cells);
                foreach (var (level, text) in design.Messages)
                    AddRuntimeMessage(level, text);
                if (analysed != null)
                    Set(DA, "AlpacaModel", new[] { analysed });
            }

            int done = history.Count;
            if (run.IsRunning)
                this.Message = $"Iteration {done + 1} of {run.Settings.MaxIterations}";
            else
            {
                this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
                if (run.Result != null && !run.Result.Converged)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, run.Result.Stopped
                        ? $"Paused after iteration {done}. The outputs are the design it reached; set Run to true to go on from there."
                        : $"Densities were still moving after {done} iterations. The outputs are the last design; raise Iterations to go on from there.");
            }
        }

        private void Set(IGH_DataAccess DA, string name, System.Collections.IEnumerable data)
        {
            DA.SetDataList(this.Params.IndexOfOutputParam(name), data);
        }

        /// <summary>
        /// A design shown: its pieces and, once asked for, its model with what assembling it had to say.
        /// Kept while the same design of the same run is shown, so that a solution that changes nothing
        /// neither finds the pieces again nor assembles them again.
        /// </summary>
        private sealed class ShownDesign
        {
            public TopologyRun Run;
            public int Step;
            public double Threshold;
            public SolidPieces Pieces;
            public readonly List<(GH_RuntimeMessageLevel level, string text)> Messages = new List<(GH_RuntimeMessageLevel, string)>();

            private bool _built;
            private Alpaca4d.Model _model;

            /// <summary>The design as a model, assembled, or null with a message saying why not.</summary>
            public Alpaca4d.Model Model(Alpaca4d.Model optimised, IReadOnlyList<SolidCell> cells)
            {
                if (_built)
                    return _model;
                _built = true;

                if (!Pieces.Kept.Any(kept => kept))
                {
                    Messages.Add((GH_RuntimeMessageLevel.Warning, "No part of the design is held still by the supports, so AlpacaModel is empty."));
                    return null;
                }
                try
                {
                    _model = TopologyDesign.Build(optimised, cells, Pieces.Kept, out int supports, out int loads);
                    if (supports > 0)
                        Messages.Add((GH_RuntimeMessageLevel.Remark,
                            $"{supports} support{(supports == 1 ? " stands" : "s stand")} where the design is void: AlpacaModel leaves {(supports == 1 ? "it" : "them")} out."));
                    if (loads > 0)
                        Messages.Add((GH_RuntimeMessageLevel.Warning,
                            $"{loads} point load{(loads == 1 ? " or mass stands" : "s or masses stand")} on nothing the design keeps, void or a piece left " +
                            $"out: AlpacaModel leaves {(loads == 1 ? "it" : "them")} out, so it does not carry every load the design was made for."));
                }
                catch (Exception ex)
                {
                    Messages.Add((GH_RuntimeMessageLevel.Warning, "The design could not be assembled as a model: " + ex.Message));
                }
                return _model;
            }
        }

        private ShownDesign _shown;

        private ShownDesign DesignOf(TopologyRun run, double[] densities, double threshold, int step)
        {
            if (_shown != null && _shown.Run == run && _shown.Step == step && _shown.Threshold == threshold)
                return _shown;
            _shown = new ShownDesign
            {
                Run = run,
                Step = step,
                Threshold = threshold,
                Pieces = TopologyDesign.Pieces(run.Model, run.Cells, densities, threshold),
            };
            return _shown;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            Run?.Stop();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                Run?.Stop();
            base.DocumentContextChanged(document, context);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Topology_Optimisation__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{8F3C2A61-5D4E-4B7A-9C1E-2B6D7A0F4E93}");
    }
}
