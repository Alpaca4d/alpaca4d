using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Optimisation;
using Alpaca4d.UIWidgets;

using ElasticMaterial = Alpaca4d.Material.ElasticIsotropicMaterial;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Multi-Material Topology: finds where each of several materials goes in a solid, and where none is
    /// needed (<see cref="MultiMaterialOptimiser"/>, after Zheng et al. 2024). The run and its outputs work
    /// as Topology Optimisation's, with a surface, a volume and densities per material.
    /// </summary>
    public class MultiMaterialTopology : TopologyComponent
    {
        private const string Help =
            "Finds where each of several materials goes in a solid, and where none is needed: the design stiffest under the model's " +
            "loads with each material filling at most its share of the volume. Every solid of the model - SSP Bricks, Ten Node or " +
            "Four Node Tetrahedra - gets a density of every material, and its stiffness mixes the materials' Young's moduli so that " +
            "an element of one material alone pays least: the mapping-based interpolation of Zheng, Yi, Peng and Yoon (2024), with " +
            "a density filter, a projection that grows sharper every 50 iterations, and MMA, as they do.\n" +
            "Only the materials' Young's moduli count: every solid keeps the Poisson's ratio of its own material, and only stiffness " +
            "is optimised, so a material strong in tension goes where its stiffness pays, not where the tension is.\n" +
            "Bricks are solved as OpenSees' eight point brick (stdBrick), as in Topology Optimisation, and every solid needs an " +
            "Elastic Isotropic Material. The solids in Keep stay as modelled.\n" +
            "Set Run to true to start. Switching Run off pauses it; on again, it goes on from where it stopped. Changing any input " +
            "but Iterations and Tolerance starts it again from the beginning.";

        public MultiMaterialTopology()
          : base("Multi-Material Topology (Alpaca4d)", "MultiTopology", Help, "Alpaca4d", "12_Optimisation")
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

        private const int ModelInput = 0, KeepInput = 1, MaterialsInput = 2, VolumeFractionsInput = 3, IterationsInput = 4, RunInput = 5;
        private const int PenaltyInput = 6, PNormInput = 7, FilterRadiusInput = 8, ToleranceInput = 9, MinStiffnessInput = 10, MoveInput = 11;
        private const int ThresholdInput = 12, SystemInput = 13, StepInput = 14;

        /// <summary>The colours of materials that have none of their own, in order: the brand palette's.</summary>
        private static readonly Color[] Palette =
        {
            Alpaca4d.Colors.AlpacaLightBlue, Alpaca4d.Colors.AlpacaOrange, Alpaca4d.Colors.AlpacaPurple, Alpaca4d.Colors.AlpacaLightGreen,
            Alpaca4d.Colors.DarkTech, Alpaca4d.Colors.AlpacaDarkBlue,
        };

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit("MultiMaterialTopology", "Multi-Material Topology", "Find where each of several materials goes in a solid.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Multi_Material_Topology__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "The assembled model, from Assemble Model. Its solids are the design domain; supports, loads and any beams or shells stay as they are.",
                GH_ParamAccess.item);

            unit.RegisterInputParam(new Param_String(), "Keep", "Keep",
                "Solids to keep as modelled, of their own material, whatever the optimisation finds - around supports and loads, say. " +
                "Left empty, every solid is designed.\n" +
                "Takes an ElementId (Support*), an element tag (47), or a regular expression (regex:^Load), mixed freely in one list.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_GenericObject(), "Materials", "Materials",
                "The Elastic Isotropic Materials to choose from, two or more. Only their Young's moduli count. Each is drawn in its " +
                "Colour, or in a colour of the brand palette when it has none.",
                GH_ParamAccess.list);

            unit.RegisterInputParam(new Param_Number(), "VolumeFractions", "VolumeFractions",
                "The share of the designed solids' volume each material may fill, one per material in the order of Materials; a " +
                "shorter list repeats its last value. Together at most 1. Left empty, 0.3 shared equally.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Integer(), "Iterations", "Iterations",
                "How many iterations at most, counted from the first. The projection is at its sharpest from iteration 251, so a run " +
                "needs about 300, as the paper's. Raise it after a run and the run goes on from where it stopped.",
                GH_ParamAccess.item, new GH_Integer(300));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Boolean(), "Run", "Run",
                "True runs the optimisation, false pauses it: on again, it goes on from where it stopped. A run whose inputs change, " +
                "Iterations and Tolerance apart, starts again from the beginning.",
                GH_ParamAccess.item, new GH_Boolean(false));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Penalty", "Penalty",
                "The penalty n: an element's stiffness takes each material's share to the power n, which pushes it to all or nothing.",
                GH_ParamAccess.item, new GH_Number(3.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "PNorm", "PNorm",
                "The p of the interpolation's p-norm: the larger, the more an element mixing materials is penalised. The paper's 6 " +
                "balances a clear result against a steady run; much larger makes the run unsteady.",
                GH_ParamAccess.item, new GH_Number(6.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "FilterRadius", "FilterRadius",
                $"How far the densities are averaged over [{Units.Length}]: the thinnest member, and the thinnest layer of one material, " +
                "is about this wide. Left empty, 2 times the typical element size.",
                GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Tolerance", "Tolerance",
                "Once the projection is at its sharpest, the run stops when the compliances of the last five iterations add up to " +
                "within this share of the five before. Make it smaller after a run and the run goes on from where it stopped.",
                GH_ParamAccess.item, new GH_Number(0.001));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "MinStiffness", "MinStiffness",
                "The stiffness a void element keeps, as a fraction of the stiffest material's. Small enough to be nothing, large enough to hold its nodes.",
                GH_ParamAccess.item, new GH_Number(1e-9));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Move", "Move",
                "The most a density can change in one iteration.",
                GH_ParamAccess.item, new GH_Number(0.1));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "Threshold", "Threshold",
                "The density of its material from which an element counts as that material, for the Surface, Solid, Material and " +
                "AlpacaModel outputs. It changes nothing in the run.",
                GH_ParamAccess.item, new GH_Number(0.5));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "System", "System",
                "How the system of equations is stored and solved. Connect a 'ValueList'.\n" +
                "Auto is the default: Mumps on every core when the OpenSees in use has it, BandSPD when it does not.\n" +
                "BandGen, BandSPD, ProfileSPD, SuperLU, UmfPack, SparseSYM, SparseSPD, SparseGeneral, FullGeneral, Mumps, Auto",
                GH_ParamAccess.item, new GH_String(DefaultSystem));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Integer(), "Step", "Step",
                "Which iteration's design to show: 0 the one the run starts from, 1 the one after the first iteration, and so on. " +
                "Left empty, the latest. Connect a slider and drag it to watch the run again; every design is kept, small.",
                GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterOutputParam(new Param_Mesh(), "Surface", "Surface",
                "The outline of each material, one mesh per material in the order of Materials, closed where it meets the edge of the " +
                "domain. The component draws each in its colour. The solids in Keep show as the material nearest them in stiffness.");
            unit.RegisterOutputParam(new Param_Colour(), "Colour", "Colour",
                "The colour each material's Surface is drawn in: its own, or the brand palette's.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Solid", "Solid",
                "The solids that make the structure, as new copies of the model's, each of the material it ended as - the solids in " +
                "Keep of their own. Pieces no face joins to a part the supports hold still are left out. AlpacaModel is these, " +
                "assembled with the supports and loads.");
            unit.RegisterOutputParam(new Param_Integer(), "Material", "Material",
                "Each solid's material as its index in Materials, in the order of the Element output; -1 where it is void or left out.");
            unit.RegisterOutputParam(new Param_Number(), "Density", "Density",
                "Each solid's density of each material, 0 to 1: a branch per material, in the order of the Element output.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Element", "Element", ElementIdentity.ElementOutput);
            unit.RegisterOutputParam(new Param_Number(), "Compliance", "Compliance",
                $"The compliance F.u of each iteration's design, the work of the loads [{Units.Force}·{Units.Length}]: the lower, the stiffer.");
            unit.RegisterOutputParam(new Param_Number(), "Volume", "Volume",
                "Each iteration's share of the designed solids' volume filled by each material: a branch per material.");
            unit.RegisterOutputParam(new Param_Boolean(), "Converged", "Converged",
                "True when the run ended at the sharpest projection with the compliance settled within Tolerance.");
            unit.RegisterOutputParam(new Param_GenericObject(), "History", "History",
                "One item per iteration: compliance, volume, the change the test of convergence measures (NaN before the projection is " +
                "at its sharpest), how long the analysis and the update took, each material's volume and the projection's sharpness. A Panel shows each as a line of text; Deconstruct Anything splits it into its values.");
            unit.RegisterOutputParam(new Param_String(), "log", "log", "What OpenSees printed. Read it when the run fails.");
            unit.RegisterOutputParam(new Param_Integer(), "Step", "Step",
                "The iteration whose design Surface, Solid, Material, Density and AlpacaModel show.");
            unit.RegisterOutputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "The design as a model to analyse, once the run has stopped: the Solid output, each solid of its material at full " +
                "stiffness, the model's other elements, and the supports, point loads and masses that stand on them.");

            var settings = new GH_ExtendableMenu(0, "MultiMaterialTopology_Settings")
            {
                Name = "Settings",
                Header = "Penalty, p-norm, filter radius, tolerance, void stiffness, move limit, threshold and solver; history and log"
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
            _surfaces = new List<(Mesh mesh, Color colour)>();
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            if (DA.Iteration > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "One model at a time: give Multi-Material Topology a single AlpacaModel.");
                return;
            }

            var model = new Alpaca4d.Model();
            var items = new List<IMultiDimensionMaterial>();
            var fractions = new List<double>();
            double penalty = 3.0, pNorm = 6.0, filterRadius = 0.0, tolerance = 0.001, minStiffness = 1e-9, move = 0.1, threshold = 0.5;
            int iterations = 300;
            bool run = false;
            string system = DefaultSystem;

            if (!DA.GetData(ModelInput, ref model)) return;
            var keep = ElementFilterInput.Read(DA, KeepInput, this);
            if (!DA.GetDataList(MaterialsInput, items)) return;
            DA.GetDataList(VolumeFractionsInput, fractions);
            DA.GetData(IterationsInput, ref iterations);
            DA.GetData(RunInput, ref run);
            DA.GetData(PenaltyInput, ref penalty);
            DA.GetData(PNormInput, ref pNorm);
            DA.GetData(FilterRadiusInput, ref filterRadius);
            DA.GetData(ToleranceInput, ref tolerance);
            DA.GetData(MinStiffnessInput, ref minStiffness);
            DA.GetData(MoveInput, ref move);
            DA.GetData(ThresholdInput, ref threshold);
            DA.GetData(SystemInput, ref system);
            int stepValue = -1;
            int? step = DA.GetData(StepInput, ref stepValue) && stepValue >= 0 ? stepValue : (int?)null;

            if (model == null || model.Bricks.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The model has no solids to optimise. Give it the AlpacaModel output of Assemble Model, " +
                                                                "with SSP Bricks or tetrahedra.");
                return;
            }

            if (items.Any(item => item == null) || items.Any(item => !(item is ElasticMaterial)))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Materials takes Elastic Isotropic Materials only.");
                return;
            }
            var materials = items.Cast<ElasticMaterial>().ToList();
            if (materials.Count < 2)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "One material makes this a single-material optimisation: Topology Optimisation does that faster.");

            var shares = fractions.Count == 0
                ? Enumerable.Repeat(0.3 / materials.Count, materials.Count).ToArray()
                : Enumerable.Range(0, materials.Count).Select(i => fractions[Math.Min(i, fractions.Count - 1)]).ToArray();
            if (fractions.Count > materials.Count)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"VolumeFractions has {fractions.Count} values for {materials.Count} materials: the last are not used.");

            var settings = new MultiMaterialSettings
            {
                Moduli = materials.Select(m => m.E).ToArray(),
                VolumeFractions = shares,
                Penalty = penalty,
                PNorm = pNorm,
                FilterRadius = filterRadius,
                MaxIterations = iterations,
                Tolerance = tolerance,
                MinStiffness = minStiffness,
                Move = move,
            };
            string problem =
                materials.Any(m => !(m.E > 0.0)) ? "Every material needs a positive Young's modulus." :
                shares.Any(f => !(f > 0.0 && f <= 1.0)) ? "Each of VolumeFractions has to be more than 0 and at most 1." :
                !(shares.Sum() <= 1.0 + 1e-12) ? $"VolumeFractions add up to {shares.Sum():0.###}: together the materials can fill the design volume once at most." :
                iterations < 1 ? "Iterations has to be at least 1." :
                !(penalty >= 1.0) ? "Penalty has to be at least 1." :
                !(pNorm >= 1.0) ? "PNorm has to be at least 1." :
                filterRadius < 0.0 ? "FilterRadius cannot be negative." :
                !(tolerance > 0.0) ? "Tolerance has to be positive." :
                !(minStiffness > 0.0 && minStiffness < 1.0) ? "MinStiffness has to be more than 0 and less than 1." :
                !(move > 0.0 && move <= 1.0) ? "Move has to be more than 0 and at most 1." :
                !(threshold > 0.0 && threshold < 1.0) ? "Threshold has to be between 0 and 1." :
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
            var own = model.Bricks.Select(b => b.Material).OfType<ElasticMaterial>().Select(m => m.Nu).Distinct().ToList();
            if (materials.Any(m => !own.Contains(m.Nu)))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Only the Young's moduli of Materials count: every solid keeps the Poisson's ratio of its own material " +
                    $"({string.Join(", ", own.Select(nu => nu.ToString("0.###", CultureInfo.InvariantCulture)))}), not those of Materials.");
            var kept = Kept(keep, model);

            // What makes a run: everything but Iterations and Tolerance, as in Topology Optimisation; the materials by their moduli.
            string key = string.Join("|", new object[]
            {
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(model), string.Join(",", kept.Select(b => b.Id)),
                string.Join(",", settings.Moduli.Select(e => e.ToString("R", CultureInfo.InvariantCulture))),
                string.Join(",", shares.Select(f => f.ToString("R", CultureInfo.InvariantCulture))),
                penalty, pNorm, filterRadius, minStiffness, move, systemObj.Type,
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
                    if (Prepare(DA, model, kept, "Multi-Material Topology takes", "MultiTopology", out var tcl, out var cells, out var openSees, out var folder, out var stem))
                    {
                        var start = new double[materials.Count * cells.Count];
                        Run = TopologyRun.Start(this, key, model, tcl, cells, settings, start,
                            (analyse, stop, progress, before, frames) => MultiMaterialOptimiser.Run(cells, settings, analyse, stop, progress, before, frames),
                            systemObj, openSees, folder, stem, previous);
                    }
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

            Show(DA, Run, materials, threshold, step);
        }

        /// <summary>The outputs of the design after iteration <paramref name="step"/>, or of the latest, and what the run is doing.</summary>
        private void Show(IGH_DataAccess DA, TopologyRun run, List<ElasticMaterial> materials, double threshold, int? step)
        {
            var (densities, history, shown) = Shown(run, step);
            var model = run.Model;
            var cells = run.Cells;
            int count = cells.Count, nm = materials.Count;

            if (run.Failure != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, run.Failure);

            // Each element's material: the one of the largest density, when it reaches the threshold. A kept solid is
            // of its own material; it shows as the one of Materials it is, or the nearest in stiffness.
            var bricks = model.Bricks;
            var choice = new int[count];
            var largest = new double[count];
            var solid = new bool[count];
            for (int e = 0; e < count; e++)
            {
                if (cells[e].Passive)
                {
                    int same = materials.FindIndex(m => ReferenceEquals(m, bricks[e].Material));
                    choice[e] = same >= 0 ? same : Enumerable.Range(0, nm).OrderBy(i => Math.Abs(Math.Log(materials[i].E / cells[e].E))).First();
                    largest[e] = 1.0;
                    solid[e] = true;
                    continue;
                }
                int best = 0;
                for (int i = 1; i < nm; i++)
                    if (densities[i * count + e] > densities[best * count + e])
                        best = i;
                choice[e] = best;
                largest[e] = densities[best * count + e];
                solid[e] = largest[e] >= threshold;
            }

            var design = DesignOf(run, solid, largest, shown, threshold);
            var pieces = design.Pieces;
            RemarkOnPieces(pieces);

            // Each material's surface outlines what Solid holds of it, as Topology Optimisation's does.
            var colours = Enumerable.Range(0, nm).Select(i => materials[i].Color ?? Palette[i % Palette.Length]).ToList();
            var meshes = new List<Mesh>();
            int crumbs = 0;
            for (int i = 0; i < nm; i++)
            {
                var outline = new double[count];
                for (int e = 0; e < count; e++)
                {
                    double own = cells[e].Passive ? (choice[e] == i ? 1.0 : 0.0) : densities[i * count + e];
                    if (solid[e] && !pieces.Kept[e])
                        own = 0.0;
                    else if (!solid[e] && pieces.Kept[e] && choice[e] == i)
                        own = 1.0;
                    outline[e] = own;
                }
                meshes.Add(Outline(cells, outline, model, threshold, out int some));
                crumbs += some;
            }
            RemarkOnCrumbs(crumbs);
            _surfaces = meshes.Zip(colours, (m, c) => (m, c)).ToList();

            int MaterialOf(int e) => pieces.Kept[e] ? choice[e] : -1;
            IMultiDimensionMaterial Assigned(int e) => cells[e].Passive || MaterialOf(e) < 0 ? null : materials[MaterialOf(e)];

            Set(DA, "Surface", meshes);
            Set(DA, "Colour", colours);
            Set(DA, "Solid", Enumerable.Range(0, count).Where(e => pieces.Kept[e]).Select(e => TopologyDesign.Copy(bricks[e], Assigned(e))));
            Set(DA, "Material", Enumerable.Range(0, count).Select(MaterialOf));
            Set(DA, "Element", bricks);
            Set(DA, "Compliance", history.Select(it => it.Compliance));

            var density = new GH_Structure<GH_Number>();
            var volume = new GH_Structure<GH_Number>();
            for (int i = 0; i < nm; i++)
            {
                var path = new GH_Path(i);
                density.AppendRange(Enumerable.Range(0, count).Select(e => new GH_Number(densities[i * count + e])), path);
                volume.AppendRange(history.Select(it => new GH_Number(it.MaterialVolumes?[i] ?? double.NaN)), path);
            }
            DA.SetDataTree(Params.IndexOfOutputParam("Density"), density);
            DA.SetDataTree(Params.IndexOfOutputParam("Volume"), volume);

            Set(DA, "Converged", new[] { run.Result?.Converged ?? false });
            Set(DA, "History", history);
            Set(DA, "log", new[] { run.Log });
            Set(DA, "Step", new[] { shown });

            if (!run.IsRunning)
            {
                var analysed = design.Model(run.Model, cells, Assigned);
                foreach (var (level, text) in design.Messages)
                    AddRuntimeMessage(level, text);
                if (analysed != null)
                    Set(DA, "AlpacaModel", new[] { analysed });
            }

            ShowProgress(run, history.Count,
                done => $"The compliance was still changing after {done} iterations. The outputs are the last design; raise Iterations to go on from there.");
        }

        private List<(Mesh mesh, Color colour)> _surfaces = new List<(Mesh, Color)>();

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            // The surfaces in their materials' colours, rather than the outputs' preview.
            if (this.Hidden || this.Locked) return;
            foreach (var (mesh, colour) in _surfaces)
                if (mesh != null && mesh.Faces.Count > 0)
                    args.Display.DrawMeshShaded(mesh, new DisplayMaterial(colour, this.Attributes.Selected ? 0.3 : 0.0));
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
        }

        public override bool IsPreviewCapable => true;

        public override BoundingBox ClippingBox
        {
            get
            {
                var box = BoundingBox.Empty;
                foreach (var (mesh, _) in _surfaces)
                    if (mesh != null)
                        box.Union(mesh.GetBoundingBox(false));
                return box.IsValid ? box : base.ClippingBox;
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Multi_Material_Topology__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{6D2A9E47-1C8B-4E35-A9F0-58B3C7D16A22}");
    }
}
