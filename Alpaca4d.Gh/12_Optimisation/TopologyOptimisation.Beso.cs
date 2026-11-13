using System;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

using Alpaca4d.Optimisation;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The BESO method of Topology Optimisation: finds where a solid needs its material by bi-directional evolutionary structural
    /// optimisation (<see cref="BesoOptimiser"/>, esoL of Xia et al. 2018 in soft-kill form). It starts from
    /// every solid, takes a few percent of the volume away each iteration and lets elements come back, each
    /// element solid or void all along. The run and its outputs work as Topology Optimisation's.
    /// </summary>
    public partial class TopologyOptimisation
    {
        private const string BesoHelp =
            "Finds where a solid needs its material by BESO, bi-directional evolutionary structural optimisation. It starts from " +
            "every solid of the model and takes EvolutionRate of the volume away each iteration, until VolumeFraction is left: each " +
            "iteration keeps the elements that work hardest - their strain energy per volume, smoothed over FilterRadius and averaged " +
            "with the iteration before - and lets the rest go, or brings void ones back. Every element is solid or void all along, so " +
            "every iteration is a design of its own, of the volume reached: drag Step to look through them.\n" +
            "A void element keeps a billionth of its stiffness instead of being cut out, so the mesh stays as it is. Bricks are " +
            "solved as OpenSees' eight point brick (stdBrick), and every solid needs an Elastic Isotropic Material, as in Topology " +
            "Optimisation. The run stops once the volume is reached and the compliance has settled: the last five iterations' within " +
            "Tolerance of the five before.\n" +
            "Set Run to true to start. Switching Run off pauses it; on again, it goes on from where it stopped. Changing any input " +
            "but Iterations and Tolerance starts it again from the beginning.";

        private const int BesoModelInput = 0, BesoKeepInput = 1, BesoVolumeFractionInput = 2, BesoIterationsInput = 3, BesoRunInput = 4;
        private const int BesoEvolutionRateInput = 5, BesoPenaltyInput = 6, BesoFilterRadiusInput = 7, BesoToleranceInput = 8, BesoMinStiffnessInput = 9, BesoSystemInput = 10;
        private const int BesoStepInput = 11;

        /// <summary>Every element is 1 or x_min, a thousandth by default, so any threshold between finds the same solids.</summary>
        private const double BesoThreshold = 0.5;

        private void RegisterBeso(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(BesoUnit, "BESO", "Find where a solid needs its material, by BESO.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.BESO_Topology__Alpaca4d_;
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
                "The share of the designed solids' volume to keep at the end, more than 0 and at most 1. The solids in Keep come on top.",
                GH_ParamAccess.item, new GH_Number(0.3));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Integer(), "Iterations", "Iterations",
                "How many iterations at most, counted from the first. Coming down to VolumeFraction takes about log(VolumeFraction) / " +
                "log(1 - EvolutionRate) iterations - 60 for 0.3 at 2% - and settling ten to thirty more.",
                GH_ParamAccess.item, new GH_Integer(200));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Boolean(), "Run", "Run",
                "True runs the optimisation, false pauses it: on again, it goes on from where it stopped. A run whose inputs change, " +
                "Iterations and Tolerance apart, starts again from the beginning.",
                GH_ParamAccess.item, new GH_Boolean(false));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "EvolutionRate", "EvolutionRate",
                "How much of the volume each iteration takes away, as a share of the volume before it. Smaller is slower and steadier.",
                GH_ParamAccess.item, new GH_Number(0.02));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Penalty", "Penalty",
                "The penalty p of soft-kill BESO: a void element has MinStiffness of its stiffness, and its strain energy counts " +
                "MinStiffness^((p-1)/p) as much, next to nothing.",
                GH_ParamAccess.item, new GH_Number(3.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "FilterRadius", "FilterRadius",
                $"How far the elements' strain energies are smoothed over [{Units.Length}]: the thinnest member the result can have is " +
                "about this wide. Left empty, 2 times the typical element size; much wider, a thin member's energy is averaged away " +
                "with the void around it, and the member can be cut through.",
                GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Tolerance", "Tolerance",
                "Once VolumeFraction is reached, the run stops when the compliances of the last five iterations add up to within this " +
                "share of the five before. Make it smaller after a run and the run goes on from where it stopped.",
                GH_ParamAccess.item, new GH_Number(0.001));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "MinStiffness", "MinStiffness",
                "The stiffness a void element keeps, as a fraction of the solid's. Small enough to be nothing, large enough to hold its nodes.",
                GH_ParamAccess.item, new GH_Number(1e-9));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "System", "System",
                "How the system of equations is stored and solved. Connect a 'ValueList'.\n" +
                "Auto is the default: Mumps on every core when the OpenSees in use has it, BandSPD when it does not.\n" +
                "BandGen, BandSPD, ProfileSPD, SuperLU, UmfPack, SparseSYM, SparseSPD, SparseGeneral, FullGeneral, Mumps, Auto",
                GH_ParamAccess.item, new GH_String(DefaultSystem));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Integer(), "Step", "Step",
                "Which iteration's design to show: 0 the whole block the run starts from, 1 the one after the first iteration, and so on. " +
                "Left empty, the latest. Every design is kept, so a slider dragged back shows the design of each volume on the way down.",
                GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterOutputParam(new Param_Mesh(), "Surface", "Surface",
                "The outline of the material kept, closed where it meets the edge of the domain. It outlines what Solid holds: without " +
                "the pieces left out, and with the elements that join a member's steps.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Solid", "Solid",
                "The solids kept that make the structure, as new copies of the model's: pieces no face joins to a part the supports " +
                "hold still are left out, and where a member's elements meet only along an edge or at a corner, the element that joins " +
                "them is in. AlpacaModel is these, assembled with the supports and loads.");
            unit.RegisterOutputParam(new Param_Number(), "Density", "Density",
                "Each solid's density in the order of the Element output: 1 solid, MinStiffness^(1/Penalty) void.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Element", "Element", ElementIdentity.ElementOutput);
            unit.RegisterOutputParam(new Param_Number(), "Compliance", "Compliance",
                $"The compliance F.u of each iteration's design, the work of the loads [{Units.Force}·{Units.Length}]: the lower, the stiffer. " +
                "It rises as the volume comes down, then settles.");
            unit.RegisterOutputParam(new Param_Number(), "Volume", "Volume",
                "Each iteration's share of the designed solids' volume: from 1 down to VolumeFraction.");
            unit.RegisterOutputParam(new Param_Boolean(), "Converged", "Converged",
                "True when the run ended at VolumeFraction with the compliance settled within Tolerance.");
            unit.RegisterOutputParam(new Param_GenericObject(), "History", "History",
                "One item per iteration: compliance, volume, the change the test of convergence measures (NaN before VolumeFraction " +
                "is reached), and how long the analysis and the update took. A Panel shows each as a line of text; Deconstruct " +
                "Anything splits it into its values.");
            unit.RegisterOutputParam(new Param_String(), "log", "log", "What OpenSees printed. Read it when the run fails.");
            unit.RegisterOutputParam(new Param_Integer(), "Step", "Step",
                "The iteration whose design Surface, Solid, Density and AlpacaModel show.");
            unit.RegisterOutputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "The design as a model to analyse, once the run has stopped: the Solid output at full stiffness, the model's other " +
                "elements, and the supports, point loads and masses that stand on them. Feed it to an analysis as you would " +
                "Assemble Model's.");

            var settings = new GH_ExtendableMenu(0, "BesoTopology_Settings")
            {
                Name = "Settings",
                Header = "Evolution rate, penalty, filter radius, tolerance, void stiffness and solver; history and log"
            };
            for (int i = BesoEvolutionRateInput; i <= BesoSystemInput; i++)
                settings.RegisterInputPlug(unit.Inputs[i]);
            foreach (var name in new[] { "History", "log" })
                settings.RegisterOutputPlug(unit.Outputs.First(plug => plug.Parameter.Name == name));
            unit.AddMenu(settings);
        }

        private void SolveBeso(IGH_DataAccess DA)
        {
            if (DA.Iteration > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "One model at a time: give Topology Optimisation a single AlpacaModel.");
                return;
            }

            var model = new Alpaca4d.Model();
            double volumeFraction = 0.3, rate = 0.02, penalty = 3.0, filterRadius = 0.0, tolerance = 0.001, minStiffness = 1e-9;
            int iterations = 200;
            bool run = false;
            string system = DefaultSystem;

            if (!DA.GetData(BesoModelInput, ref model)) return;
            var keep = ElementFilterInput.Read(DA, BesoKeepInput, this);
            DA.GetData(BesoVolumeFractionInput, ref volumeFraction);
            DA.GetData(BesoIterationsInput, ref iterations);
            DA.GetData(BesoRunInput, ref run);
            DA.GetData(BesoEvolutionRateInput, ref rate);
            DA.GetData(BesoPenaltyInput, ref penalty);
            DA.GetData(BesoFilterRadiusInput, ref filterRadius);
            DA.GetData(BesoToleranceInput, ref tolerance);
            DA.GetData(BesoMinStiffnessInput, ref minStiffness);
            DA.GetData(BesoSystemInput, ref system);
            int stepValue = -1;
            int? step = DA.GetData(BesoStepInput, ref stepValue) && stepValue >= 0 ? stepValue : (int?)null;

            if (model == null || model.Bricks.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The model has no solids to optimise. Give it the AlpacaModel output of Assemble Model, " +
                                                                "with SSP Bricks or tetrahedra.");
                return;
            }

            var settings = new BesoSettings
            {
                VolumeFraction = volumeFraction,
                EvolutionRate = rate,
                Penalty = penalty,
                FilterRadius = filterRadius,
                MaxIterations = iterations,
                Tolerance = tolerance,
                MinStiffness = minStiffness,
            };
            string problem =
                !(volumeFraction > 0.0 && volumeFraction <= 1.0) ? "VolumeFraction has to be more than 0 and at most 1." :
                iterations < 1 ? "Iterations has to be at least 1." :
                !(rate > 0.0 && rate < 1.0) ? "EvolutionRate has to be more than 0 and less than 1." :
                !(penalty >= 1.0) ? "Penalty has to be at least 1." :
                filterRadius < 0.0 ? "FilterRadius cannot be negative." :
                !(tolerance > 0.0) ? "Tolerance has to be positive." :
                !(minStiffness > 0.0 && minStiffness < 1.0) ? "MinStiffness has to be more than 0 and less than 1." :
                null;
            if (problem != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
                return;
            }

            var systemObj = SystemOf(system);
            if (systemObj == null)
                return;

            WarnAboutModel(model);
            var kept = Kept(keep, model);

            // What makes a run: everything but Iterations and Tolerance, as in Topology Optimisation.
            string key = string.Join("|", new object[]
            {
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(model), string.Join(",", kept.Select(b => b.Id)),
                volumeFraction, rate, penalty, filterRadius, minStiffness, systemObj.Type,
            }.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));

            if (!run)
                Run?.Stop();
            else
            {
                var next = Next(Run, key, iterations, tolerance, (it, tol) => it.Change <= tol);
                if (next != Going.On)
                {
                    var previous = next == Going.OnFromWhereItStopped ? Run.Result : null;
                    Run?.Stop();
                    Run = null;
                    if (Prepare(DA, model, kept, "Topology Optimisation (BESO) takes", "Beso", out var tcl, out var cells, out var openSees, out var folder, out var stem))
                        Run = TopologyRun.Start(this, key, model, tcl, cells, settings, cells.Select(_ => 1.0).ToArray(),
                            (analyse, stop, progress, before, frames) => BesoOptimiser.Run(cells, settings, analyse, stop, progress, before, frames),
                            systemObj, openSees, folder, stem, previous);
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

            var result = Run.Result;
            ShowDensities(DA, Run, BesoThreshold, step,
                done => result?.BrokeAfter is int broke
                    ? $"The design broke apart after iteration {broke}: a member was cut through, the compliance came out more than " +
                      $"{BesoOptimiser.Breakdown:0} times what it was and never came back, and the run settled on what was left. Drag Step to " +
                      $"{broke - 1} for the last whole design, or start again with a smaller FilterRadius or EvolutionRate, or Keep more."
                    : $"The volume or the compliance was still changing after {done} iterations. The outputs are the last design; " +
                      "raise Iterations to go on from there.");
        }

    }
}
