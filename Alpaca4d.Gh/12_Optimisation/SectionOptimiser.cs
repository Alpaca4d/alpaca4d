using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

using Alpaca4d.Design;
using Alpaca4d.Generic;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Section Optimiser: sizes the beams of an assembled model from the sections of their design
    /// groups, or of their sections' families. It analyses the model, checks every beam taking part
    /// with every section of its list on the forces it carried, puts each on the first of the list that
    /// keeps it at or below MaxUtilisation, and analyses again until no section changes
    /// (<see cref="SectionSizing"/>).
    ///
    /// Each analysis is static, in one step, solved as Analysis Settings solves one by default: what
    /// a sizing needs, so nothing about it is asked for but how the equations are solved.
    ///
    /// A switcher with a single unit, as Utilisation is, for the sake of its two folded menus: Design,
    /// for how the members are made and held - set once and left alone - and for the governing check,
    /// groups, history and log; and Analysis, for the system of equations and the algorithm.
    /// </summary>
    public class SectionOptimiser : GH_SwitcherComponent
    {
        private const string Help =
            "Sizes beams from lists of sections. It analyses the model, checks every beam that takes part with every section of " +
            "its list on the forces the beam carried - EN 1993-1-1, as Utilisation does - puts it on the first section of the " +
            "list that keeps its utilisation at or below MaxUtilisation, and analyses again, as the new sections change the " +
            "self-weight and, in a statically indeterminate structure, where the forces go. It stops when no section changes, or " +
            "after Iterations analyses.\n" +
            "A beam's list is its Design Group's, in the order given. A beam in no group is sized within the family of the " +
            "section it starts with in Section Library - an HEA200 among the HEAs, a CHS among the CHS - smallest area first; " +
            "a section that is not from Section Library has no family, and keeps its place.\n" +
            "Every beam takes part unless ElementId says which. Those left out keep their sections, and are still checked.\n" +
            "A beam can move back up its list as well as down it. When a design comes round again the iterations after it only let " +
            "beams move on down their lists, which ends.\n" +
            "Each analysis is static, in one step. Buckling lengths, fabrication and partial factors are in the Design menu, the " +
            "solver in the Analysis menu. Steel only, as the check is. Press Escape to stop after the iteration under way.";

        public SectionOptimiser()
          : base("Section Optimiser (Alpaca4d)", "Section Optimiser", Help, "Alpaca4d", "12_Optimisation")
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

        // Where each input sits: the body's first, then the Design menu's, then the Analysis menu's.
        private const int ModelInput = 0, GroupsInput = 1, FilterInput = 2, MaxUtilisationInput = 3, IterationsInput = 4;
        private const int FabricationInput = 5, GammaM0Input = 6, GammaM1Input = 7;
        private const int LengthYInput = 8, LengthZInput = 9, LengthLTInput = 10, LoadInput = 11, SwayInput = 12;
        private const int SystemInput = 13, AlgorithmInput = 14;

        private const string PerBeam = "One value for every beam, or one per beam, in the order of the model's beams.";

        private const string DefaultSystem = "Auto", DefaultAlgorithm = "Newton";

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit("SectionOptimiser", "Section Optimiser", "Size beams from lists of sections.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Section_Optimiser__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "The assembled model, from Assemble Model. Its beams start from the sections they have.",
                GH_ParamAccess.item);

            unit.RegisterInputParam(new Param_GenericObject(), "DesignGroups", "DesignGroups",
                "Which beams to size from which sections: one Design Group per list. Optional: a beam in no group is sized within " +
                "the family of the section it starts with in Section Library - an HEA200 among the HEAs - smallest area first.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "ElementId", "ElementId",
                "Optimise only some of the beams. Left empty, every beam is.\n" +
                "Takes an ElementId (MyBeam_3), a wildcard over them (MyBeam*, Col_?), an element tag (47), or a regular " +
                "expression (regex:^Col), mixed freely in one list. The beams it leaves out keep their sections, whatever " +
                "group they are in, but are still analysed and checked.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "MaxUtilisation", "MaxUtilisation",
                "The utilisation each beam taking part should end up at or below: 1 is exactly at the limit, 0.8 keeps a fifth in hand.",
                GH_ParamAccess.item, new GH_Number(1.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Integer(), "Iterations", "Iterations",
                "How many analyses at most: each one sizes the beams on the forces it finds, for the next to check.",
                GH_ParamAccess.item, new GH_Integer(10));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "Fabrication", "Fabrication",
                "How the sections are made, which sets the buckling curves: Rolled (rolled I-sections, hot-finished hollow sections) " +
                "or Welded (welded I-sections, cold-formed hollow sections).",
                GH_ParamAccess.item, new GH_String("Rolled"));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "γM0", "γM0",
                "Partial factor for the resistance of cross-sections. 1.0 is the recommended value; a National Annex may set another.",
                GH_ParamAccess.item, new GH_Number(1.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "γM1", "γM1",
                "Partial factor for the resistance of members to instability. 1.0 is the recommended value; a National Annex may set another.",
                GH_ParamAccess.item, new GH_Number(1.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "LengthY", "LengthY",
                $"Buckling length L_cr about local y - the minor axis of an I [{Units.Length}]. " + PerBeam +
                " Left empty, each beam's own length, node to node. It stays with the beam whatever its section.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "LengthZ", "LengthZ",
                $"Buckling length L_cr about local z - the major axis of an I [{Units.Length}]. " + PerBeam +
                " Left empty, each beam's own length, node to node.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "LengthLT", "LengthLT",
                $"Distance between lateral restraints, for lateral-torsional buckling [{Units.Length}], as in Utilisation. " + PerBeam +
                " Left empty, each beam's own length: not safe for a cantilever, whose tip is not held.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "Load", "Load",
                "Where the load along each beam acts on its section, for lateral-torsional buckling: ShearCentre (the default), " +
                "Destabilising (or Top) or Stabilising (or Bottom). " + PerBeam,
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Boolean(), "Sway", "Sway",
                "The beam belongs to a frame that buckles by swaying: Cm = 0.9 about both axes (Table B.3). Off by default. " + PerBeam,
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "System", "System",
                "How the system of equations is stored and solved. Connect a 'ValueList'.\n" +
                "Auto is the default: Mumps on every core when the OpenSees in use has it, BandSPD when it does not. " +
                "Reach for UmfPack if a model refuses to solve - it assumes nothing about the matrix.\n" +
                "BandGen, BandSPD, ProfileSPD, SuperLU, UmfPack, SparseSYM, SparseSPD, SparseGeneral, FullGeneral, Mumps, Auto",
                GH_ParamAccess.item, new GH_String(DefaultSystem));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "Algorithm", "Algorithm",
                "How each analysis is iterated to equilibrium. Connect a 'ValueList'.\n" +
                "Newton is the default. Linear does one solve with no iteration: a little faster on an elastic model, and only " +
                "right on one.\n" +
                "Linear, Newton, NewtonLineSearch, ModifiedNewton, KrylovNewton, SecantNewton, BFGS, Broyden",
                GH_ParamAccess.item, new GH_String(DefaultAlgorithm));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterOutputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "The last design, analysed: feed it to Utilisation, Beam Forces or Model View. Empty when the first analysis failed.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Section", "Section",
                "Each beam's section in the last design, in the order of the Element output.");
            unit.RegisterOutputParam(new Param_Number(), "Utilisation", "Utilisation",
                "Each beam's utilisation in the last design, as Utilisation's Max. Empty where it could not be checked. " +
                "Torsional and torsional-flexural buckling (6.3.1.4) are not checked yet.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Element", "Element", ElementIdentity.ElementOutput);
            unit.RegisterOutputParam(new Param_Number(), "Mass", "Mass",
                $"The model's mass in each iteration, the last being the last design's [{Units.Mass}].");
            unit.RegisterOutputParam(new Param_Boolean(), "Converged", "Converged",
                "True when the last iteration changed nothing: every beam taking part has the first section of its list that works for it.");
            unit.RegisterOutputParam(new Param_String(), "Governing", "Governing", "Which check gives each beam's utilisation, or why there is none.");
            unit.RegisterOutputParam(new Param_String(), "Group", "Group", "Each beam's design group, by name - or Group 1, Group 2, ... in the order given, for one with none - or its family, HEA family, for one in no group; empty for a beam that takes no part.");
            unit.RegisterOutputParam(new Param_GenericObject(), "History", "History",
                "One item per iteration: mass, largest utilisation, beams above MaxUtilisation, sections changed, and how long the analysis and the checks took. " +
                "A Panel shows each as a line of text; Deconstruct Anything splits it into its values.");
            unit.RegisterOutputParam(new Param_String(), "log", "log", "What OpenSees printed in the last iteration. Read it when an analysis fails or warns.");

            // Folded away until they are opened.
            var design = new GH_ExtendableMenu(0, "SectionOptimiser_Design")
            {
                Name = "Design",
                Header = "Fabrication, partial factors, buckling lengths, load position and sway; governing check, groups, history and log"
            };
            for (int i = FabricationInput; i <= SwayInput; i++)
                design.RegisterInputPlug(unit.Inputs[i]);
            foreach (var name in new[] { "Governing", "Group", "History", "log" })
                design.RegisterOutputPlug(unit.Outputs.First(plug => plug.Parameter.Name == name));
            unit.AddMenu(design);

            var analysis = new GH_ExtendableMenu(1, "SectionOptimiser_Analysis")
            {
                Name = "Analysis",
                Header = "How each analysis is solved: static, one step, with this system of equations and algorithm"
            };
            analysis.RegisterInputPlug(unit.Inputs[SystemInput]);
            analysis.RegisterInputPlug(unit.Inputs[AlgorithmInput]);
            unit.AddMenu(analysis);
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            ValueListUtils.UpdateValueLists(this, SystemInput, Enum.GetNames(typeof(Alpaca4d.SystemEquation.SystemType)).ToList(), null);
            ValueListUtils.UpdateValueLists(this, AlgorithmInput, Enum.GetNames(typeof(Alpaca4d.Algorithm.AlgorithmType)).ToList(), null);
        }

        /// <summary>
        /// The settings of every analysis: static, in one step of load control, with Analysis Settings'
        /// defaults for the rest - Transformation constraints, RCM numbering, the EnergyIncr test - and
        /// the system and algorithm asked for. Null, with an error, for a name neither has.
        /// </summary>
        private Settings AnalysisSettings(string system, string algorithm)
        {
            SystemEquation systemObj;
            Algorithm algorithmObj;
            try { systemObj = new SystemEquation(string.IsNullOrWhiteSpace(system) ? DefaultSystem : system.Trim()); }
            catch (ArgumentException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"System \"{system}\" is not one of {string.Join(", ", Enum.GetNames(typeof(SystemEquation.SystemType)))}.");
                return null;
            }
            try { algorithmObj = new Algorithm(string.IsNullOrWhiteSpace(algorithm) ? DefaultAlgorithm : algorithm.Trim()); }
            catch (ArgumentException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Algorithm \"{algorithm}\" is not one of {string.Join(", ", Enum.GetNames(typeof(Algorithm.AlgorithmType)))}.");
                return null;
            }

            return new Settings(new Constraint("Transformation"), new Numberer("RCM"), systemObj, Alpaca4d.Test.EnergyIncr(),
                                algorithmObj, Alpaca4d.Integrator.LoadControl(1), new Analysis("Static"), new Alpaca4d.AnalysisStep(1));
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            var model = new Alpaca4d.Model();
            var groupItems = new List<object>();
            double maxUtilisation = 1.0;
            int iterations = 10;
            string system = DefaultSystem, algorithm = DefaultAlgorithm;
            var lengthsY = new List<double>();
            var lengthsZ = new List<double>();
            var lengthsLT = new List<double>();
            var loadTexts = new List<string>();
            var sways = new List<bool>();
            string fabricationText = "Rolled";
            double gammaM0 = 1.0, gammaM1 = 1.0;

            if (!DA.GetData(ModelInput, ref model)) return;
            DA.GetDataList(GroupsInput, groupItems);
            var filter = ElementFilterInput.Read(DA, FilterInput, this);
            DA.GetData(MaxUtilisationInput, ref maxUtilisation);
            DA.GetData(IterationsInput, ref iterations);
            DA.GetDataList(LengthYInput, lengthsY);
            DA.GetDataList(LengthZInput, lengthsZ);
            DA.GetDataList(LengthLTInput, lengthsLT);
            DA.GetDataList(LoadInput, loadTexts);
            DA.GetDataList(SwayInput, sways);
            DA.GetData(FabricationInput, ref fabricationText);
            DA.GetData(GammaM0Input, ref gammaM0);
            DA.GetData(GammaM1Input, ref gammaM1);
            DA.GetData(SystemInput, ref system);
            DA.GetData(AlgorithmInput, ref algorithm);

            if (model == null || model.Beams.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The model has no beams to size. Give it the AlpacaModel output of Assemble Model.");
                return;
            }

            var settings = AnalysisSettings(system, algorithm);
            if (settings == null)
                return;

            if (!(maxUtilisation > 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "MaxUtilisation has to be positive.");
                return;
            }

            if (iterations < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Iterations has to be at least 1.");
                return;
            }

            if (!UtilisationBase.TryFabrication(fabricationText, out var fabrication))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Fabrication \"{fabricationText}\" is not one of Rolled or Welded.");
                return;
            }

            if (gammaM0 <= 0.0 || gammaM1 <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "γM0 and γM1 have to be positive.");
                return;
            }

            var loads = new List<LoadPosition>();
            foreach (var text in loadTexts)
            {
                if (!UtilisationBase.TryLoad(text, out var load))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Load \"{text}\" is not one of ShearCentre, Destabilising or Stabilising.");
                    return;
                }
                loads.Add(load);
            }

            var beams = model.Beams;
            if (!UtilisationBase.Spread(this, lengthsY, beams.Count, "LengthY", out var eachY) ||
                !UtilisationBase.Spread(this, lengthsZ, beams.Count, "LengthZ", out var eachZ) ||
                !UtilisationBase.Spread(this, lengthsLT, beams.Count, "LengthLT", out var eachLT) ||
                !UtilisationBase.Spread(this, loads, beams.Count, "Load", out var eachLoad) ||
                !UtilisationBase.Spread(this, sways, beams.Count, "Sway", out var eachSway))
                return;

            if (lengthsY.Concat(lengthsZ).Concat(lengthsLT).Any(x => x <= 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A buckling length has to be positive.");
                return;
            }

            var designGroups = new List<DesignGroup>();
            foreach (var item in groupItems)
            {
                var value = item is GH_ObjectWrapper wrapper ? wrapper.Value : item;
                if (value is DesignGroup group)
                    designGroups.Add(group);
                else if (value != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{value.GetType().Name} is not a Design Group.");
                    return;
                }
            }

            int takingPart = filter.MatchesEverything ? beams.Count : filter.SelectIndices(beams).Count;
            if (takingPart == 0)
            {
                foreach (var problem in filter.Unmatched(model.Elements))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, problem);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "ElementId picks no beam of this model, so there is nothing to optimise.");
                return;
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            var groups = SectionSizing.Resolve(beams, designGroups, errors, warnings, filter, null, Alpaca4d.Section.SteelCatalogue.FamilyOf);
            foreach (var warning in warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            foreach (var error in errors)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
            if (errors.Count > 0)
                return;
            if (groups.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, (filter.MatchesEverything ? "No beam" : "No beam that ElementId picks") +
                    " is in a design group or has a section from Section Library, so there is nothing to size.");
                return;
            }

            if (!LicenseGate.Allows(this, "Section Optimiser"))
                return;

            // Node to node, the length OpenSees integrates over, as Utilisation takes it.
            var members = beams.Select(b => new SteelMember(b.Curve != null ? b.Curve.PointAtStart.DistanceTo(b.Curve.PointAtEnd) : 0.0)).ToList();
            for (int i = 0; i < beams.Count; i++)
            {
                var member = members[i];
                if (eachY[i].HasValue) member.BucklingLengthY = eachY[i].Value;
                if (eachZ[i].HasValue) member.BucklingLengthZ = eachZ[i].Value;
                if (eachLT[i].HasValue) member.LateralTorsionalLength = eachLT[i].Value;
                if (eachLoad[i].HasValue) member.Load = eachLoad[i].Value;
                if (eachSway[i].HasValue) member.Sway = eachSway[i].Value;
            }

            var options = new SizingOptions
            {
                Target = maxUtilisation,
                MaxRounds = iterations,
                Design = new DesignSettings { Fabrication = fabrication, GammaM0 = gammaM0, GammaM1 = gammaM1, Report = false },
                Members = members,
            };

            var files = AnalysisFiles.For(this, DA);
            string log = "";
            Model Analyse(Model assembled) => this.Analyse(assembled, settings, files, out log);

            SizingResult result;
            try
            {
                result = SectionSizing.Run(model, groups, options, Analyse, GH_Document.IsEscapeKeyDown);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                Set(DA, "log", new[] { log });
                return;
            }

            var names = result.Group.Select(g => g >= 0 ? groups[g].Label : "").ToList();

            Set(DA, "AlpacaModel", new[] { result.Model });
            Set(DA, "Section", result.Sections);
            Set(DA, "Utilisation", result.Utilisation.Select(u => u.HasValue && !double.IsNaN(u.Value) ? new GH_Number(u.Value) : null));
            Set(DA, "Element", result.Model.Beams);
            Set(DA, "Mass", result.Rounds.Select(round => round.Mass));
            Set(DA, "Converged", new[] { result.Converged });
            Set(DA, "Governing", result.Governing);
            Set(DA, "Group", names);
            Set(DA, "History", result.Rounds);
            Set(DA, "log", new[] { log });

            Explain(result, maxUtilisation, iterations);
        }

        /// <summary>
        /// Solves one iteration: a copy of the assembled model with a recorder of the optimiser's own and
        /// the settings written into its deck. Throws when the analysis fails, which ends the run.
        /// </summary>
        private Model Analyse(Model assembled, Settings settings, AnalysisFiles files, out string log)
        {
            // Named after this optimiser as well as the run: the results file is read again by whatever
            // the AlpacaModel output feeds, after the solution, and a second optimiser in the document
            // - one per element and one uniform, say - must not overwrite it meanwhile.
            string stem = "SectionOptimiser_" + this.InstanceGuid.ToString("N").Substring(0, 8);

            var run = assembled.ShallowCopy();
            run.Tcl = new List<string>(assembled.Tcl);
            run.FileName = files.OwnFile(stem, ".tcl");
            run.Settings = settings;

            // Its own, whatever recorders the model was given: the check needs the section forces, and
            // an iteration must not overwrite the results of a Run Analysis.
            run.IsStatic = true;
            run.Recorders = new List<IRecorder> { Alpaca4d.Recorder.MpcoStatic(files.OwnFile(stem, ".mpco")) };

            foreach (var recorder in run.Recorders)
                run.Tcl.Add(recorder.WriteTcl());
            run.Tcl.Add(settings.WriteTcl());
            run.Tcl.Add("wipe");

            run.Serialise();
            var (output, error, exitCode) = run.RunOpenSees();
            log = string.IsNullOrEmpty(error) ? output : error + "\n" + output;

            string failure = AnalysisOutcome.Failure(output, error, exitCode);
            if (failure != null)
                throw new Exception(failure);

            return run;
        }

        /// <summary>
        /// Warns, once per solve, when the run did not settle and which beams it could not size. Only
        /// warnings: how the run went otherwise is in History.
        /// </summary>
        private void Explain(SizingResult result, double maxUtilisation, int iterations)
        {
            var beams = result.Model.Beams;
            string Tags(IEnumerable<int> positions)
            {
                var tags = positions.Select(i => beams[i].Id?.ToString() ?? "?").ToList();
                return string.Join(", ", tags.Take(10)) + (tags.Count > 10 ? ", ..." : "");
            }

            int rounds = result.Rounds.Count;
            if (!result.Converged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, rounds < iterations
                    ? $"Stopped after iteration {rounds}, with sections still to change. The outputs are the design that iteration analysed."
                    : $"Sections were still changing after {iterations} iteration(s). The outputs are the last design analysed; raise Iterations to go on.");

            var grouped = Enumerable.Range(0, beams.Count).Where(i => result.Group[i] >= 0).ToList();

            var uncheckable = grouped.Where(i => !result.Utilisation[i].HasValue).ToList();
            if (uncheckable.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{uncheckable.Count} beam(s) could not be checked with any section of their list, and keep the section they have " +
                    $"(element {Tags(uncheckable)}). See their Governing output.");

            var over = grouped.Where(i => result.Utilisation[i].HasValue && result.Utilisation[i].Value > maxUtilisation).ToList();
            if (over.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{over.Count} beam(s) are above {maxUtilisation:0.###} with the best section their list has (element {Tags(over)}). " +
                    "Add stronger sections to their design group, or give the ones sized within a family a group of their own.");
        }

        /// <summary>An output, by name.</summary>
        private void Set(IGH_DataAccess DA, string name, System.Collections.IEnumerable data)
        {
            DA.SetDataList(this.Params.IndexOfOutputParam(name), data);
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Section_Optimiser__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{56302D3A-734F-4B03-899E-F6C273CF1B9F}");
    }
}
