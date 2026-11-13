using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

using Alpaca4d.Generic;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Footfall Analysis: how much a floor or a stair shakes under a person walking, after SCI P354,
    /// worked out from the modes Natural Vibration Analysis found. No OpenSees run of its own: the
    /// modes are read mass-normalised from the recorder file, and the rest is
    /// <see cref="Alpaca4d.Footfall"/>.
    ///
    /// A switcher so that the inputs set once and left alone fold into the Frequency data menu, and
    /// the steady-state and transient results into one of their own: the body shows the model, where
    /// people walk (and, in full excitation, where it is felt), the footsteps, the walker and the
    /// weighting, and the response factor. Its units are the excitation methods, Self and Full.
    ///
    /// Inputs and outputs are read and written by name: the two units do not have the same ones.
    /// </summary>
    public class FootfallAnalysis : GH_SwitcherComponent
    {
        public FootfallAnalysis()
          : base("Footfall Analysis (Alpaca4d)", "Footfall",
            "How much a floor or a stair shakes under a person walking, as a response factor R: the weighted acceleration over " +
            "the threshold of perception. SCI P354's method, with its Fourier coefficients for walking (Table 3.1) or for stairs " +
            "(Table 3.2).\n" +
            "Connect the model from Natural Vibration Analysis, with modes up to 2 Hz past the cut-off frequency. Its mass should " +
            "be what the structure carries in use: P354 takes the self-weight, the superimposed dead load and 10% of the imposed load. " +
            "The self-weight is mass already; add the rest with Mass from Loads before Assemble Model.\n" +
            "Walk gives where people walk, Feel where the vibration is felt; each point takes the model's nearest node. With " +
            "nothing in Walk, the nodes of the shell elements are walked on - the slabs, treads and landings.\n" +
            "Self excitation: the walker is at the point the response is read at, which P354 takes as the worst place in most " +
            "cases. Full excitation: for each Feel point, the walker is tried at every Walk point, and the worst kept - walking " +
            "in a corridor can shake an office next to it.\n" +
            "Every pace from Minimum to Maximum Frequency is tried, and the worst kept. A floor whose fundamental frequency is " +
            "below Cut Off is checked for both the steady-state response (the harmonics of the walking force driving the modes) " +
            "and the transient one (each footfall setting the modes ringing), and the greater counts; a floor above it for the " +
            "transient alone. A stair is checked for the steady state alone, as SCI AD 406 advises.\n" +
            "The activity, the paces, the cut-off and the damping are in the Frequency data menu below the component.",
            "Alpaca4d", "07_Analysis")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        public override string UnitMenuName => "Excitation";

        public override string UnitMenuHeader => "Where the walker is, and where the response is read";

        protected override string DefaultEvaluationUnit => SelfUnit;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // All inputs belong to the evaluation units, see RegisterEvaluationUnits.
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // All outputs belong to the evaluation units, see RegisterEvaluationUnits.
        }

        private static readonly List<string> Activities = Enum.GetNames(typeof(FootfallActivity)).ToList();
        private static readonly List<string> Weightings = new List<string>
            { FrequencyWeighting.Wb.ToString(), FrequencyWeighting.Wg.ToString(), FrequencyWeighting.None.ToString() };

        private const string SelfUnit = "SelfExcitation", FullUnit = "FullExcitation";

        // The inputs: the body's, then the Frequency data menu's.
        private const string ModelInput = "AlpacaModel", WalkInput = "Walk", FeelInput = "Feel", FootstepsInput = "Footsteps",
            WalkerMassInput = "Walker Mass", WeightingInput = "Frequency Weighting";
        private const string ActivityInput = "Activity", MinimumInput = "Minimum Frequency", MaximumInput = "Maximum Frequency",
            StepsInput = "Frequency Steps", CutOffInput = "Cut Off", DampingInput = "Damping";

        // The outputs: the body's, then the Steady state and transient menu's.
        private const string FactorOutput = "Response Factor", PointOutput = "Point", CriticalOutput = "Critical Frequency",
            WalkerOutput = "Walker Point", ReportOutput = "Report", ModelOutput = "AlpacaModel";
        private const string SteadyFactorOutput = "Steady-State Factor", TransientFactorOutput = "Transient Factor",
            SteadyAccelerationOutput = "Steady-State Acceleration", TransientAccelerationOutput = "Transient Acceleration";

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            Register(mngr, full: false);
            Register(mngr, full: true);
        }

        private static void Register(EvaluationUnitManager mngr, bool full)
        {
            var defaults = new FootfallSettings();

            var unit = full
                ? new EvaluationUnit(FullUnit, "Full excitation",
                    "The walker at every Walk point in turn, the response read at every Feel point: walking in one place shakes another.")
                : new EvaluationUnit(SelfUnit, "Self excitation",
                    "The walker at the point the response is read at, which P354 takes as the worst place in most cases.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Footfall_Analysis__Alpaca4d_;
            mngr.RegisterUnit(unit);

            void Optional() => unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_GenericObject(), ModelInput, ModelInput,
                "The model from Natural Vibration Analysis: its modes and their frequencies.",
                GH_ParamAccess.item);

            unit.RegisterInputParam(new Param_Point(), WalkInput, WalkInput,
                (full
                    ? "Where people walk: corridors, circulation, a gym floor. The walker is tried at each, for every Feel point. "
                    : "Where people walk, and where the response is read: the walker shakes the point it stands on. ") +
                "Each point takes the model's nearest node: the vertices of the meshes the shells were made from are the nodes " +
                "themselves. Leave empty for the nodes of every shell element - slabs, treads, landings - or every node of a " +
                "model with no shells.",
                GH_ParamAccess.list);
            Optional();

            if (full)
            {
                unit.RegisterInputParam(new Param_Point(), FeelInput, FeelInput,
                    "Where the vibration is felt, and the response is read: offices, a laboratory, a ward. Each point takes the " +
                    "model's nearest node. Leave empty to read it at the Walk points.",
                    GH_ParamAccess.list);
                Optional();
            }

            unit.RegisterInputParam(new Param_Integer(), FootstepsInput, FootstepsInput,
                "How many steps the walker takes along the path - its length over the stride, about 0.75 m at 2 Hz. A steady-state " +
                "response builds up step by step, so a short path gives less: P354's resonance build-up factor, 1 - e^(-2π ζ N). " +
                "Leave empty for a path long enough to reach the steady state.",
                GH_ParamAccess.item);
            Optional();

            unit.RegisterInputParam(new Param_Number(), WalkerMassInput, WalkerMassInput,
                $"The walker's mass [{Units.Mass}]. P354 takes 76 kg, a weight of 746 N.",
                GH_ParamAccess.item, new GH_Number(defaults.WalkerMass));
            Optional();

            unit.RegisterInputParam(new Param_String(), WeightingInput, WeightingInput,
                "Connect a 'ValueList'. How much of each frequency a person " + (full ? "where it is felt " : "") + "feels, P354 Table 5.1.\n" +
                "Wb: offices, homes, wards, general laboratories, workshops, circulation and stairs.\n" +
                "Wg: critical areas - operating theatres, precision laboratories.\n" +
                "None: every frequency in full, which is conservative.",
                GH_ParamAccess.item, new GH_String(defaults.Weighting.ToString()));
            Optional();

            int firstMenuInput = unit.Inputs.Count;

            unit.RegisterInputParam(new Param_String(), ActivityInput, ActivityInput,
                "Connect a 'ValueList'. Which of P354's Fourier coefficients load the structure.\n" +
                "Walking: on a floor, Table 3.1, four harmonics; paces 1.8 to 2.2 Hz.\n" +
                "Stairs: up or down a stair, Table 3.2 from ISO 10137, two harmonics; paces up to 4.5 Hz on a fast descent. " +
                "Use with a Cut Off of 12 Hz and 0.5% damping for a welded steel stair.",
                GH_ParamAccess.item, new GH_String(defaults.Activity.ToString()));
            Optional();

            unit.RegisterInputParam(new Param_Number(), MinimumInput, MinimumInput,
                "The slowest pace tried, in footfalls per second [Hz]. P354: 1.8 Hz walking, 1.8 Hz alone in homes and operating " +
                "theatres; stairs from 1.2 Hz.",
                GH_ParamAccess.item, new GH_Number(defaults.MinimumFrequency));
            Optional();

            unit.RegisterInputParam(new Param_Number(), MaximumInput, MaximumInput,
                "The fastest pace tried [Hz]. P354: 2.2 Hz walking; 4.5 Hz on a stair.",
                GH_ParamAccess.item, new GH_Number(defaults.MaximumFrequency));
            Optional();

            unit.RegisterInputParam(new Param_Integer(), StepsInput, StepsInput,
                "How many paces are tried, evenly spaced from Minimum to Maximum Frequency, both included. A lightly damped " +
                "structure resonates over a narrow band of paces, so try them closely: 41 over 1.8 to 2.2 Hz is one every 0.01 Hz.",
                GH_ParamAccess.item, new GH_Integer(defaults.FrequencySteps));
            Optional();

            unit.RegisterInputParam(new Param_Number(), CutOffInput, CutOffInput,
                "P354's low to high frequency cut-off [Hz], Table 6.1: 10 for general floors and offices, 8 for enclosed spaces such " +
                "as operating theatres and homes, 12 for stairs. Below it a floor is checked for both responses, above it for the " +
                "transient alone. The steady state takes the modes up to 2 Hz past it, the transient those up to twice the fundamental.",
                GH_ParamAccess.item, new GH_Number(defaults.CutOff));
            Optional();

            unit.RegisterInputParam(new Param_Number(), DampingInput, DampingInput,
                "The damping ratio of every mode: 0.03 is 3%. P354 Table 4.1: 0.5% fully welded steel such as a stair, 1.1% a bare " +
                "floor, 3% a floor fitted out and furnished, 4.5% partitions across the modes.",
                GH_ParamAccess.item, new GH_Number(defaults.Damping));
            Optional();

            string at = full ? "Feel point" : "Walk point";
            unit.RegisterOutputParam(new Param_Number(), FactorOutput, FactorOutput,
                $"The response factor R at each {at}, " + (full ? "with the walker where and at the pace that make it worst" : "at its worst pace") +
                ". 1 is the threshold of perception. P354 Table 5.3: 8 for a general office, 4 for a busy one or a shopping mall, " +
                "32 for a stair in light use, 24 in heavy use.");
            unit.RegisterOutputParam(new Param_Point(), PointOutput, PointOutput,
                $"The node each {at} took, where its response is read, in the same order as the results [{Units.Length}].");
            unit.RegisterOutputParam(new Param_Number(), CriticalOutput, CriticalOutput,
                $"The pace that gives each {at} its response factor [Hz].");
            if (full)
                unit.RegisterOutputParam(new Param_Point(), WalkerOutput, WalkerOutput,
                    $"Where the walker is when each Feel point shakes the most: the Walk node that gives it its response factor [{Units.Length}].");
            unit.RegisterOutputParam(new Param_String(), ReportOutput, ReportOutput,
                "Where the walker went and where the response was read, the fundamental frequency, the modes taken, the worst " +
                "point, its pace and which response governs there.");

            int firstMenuOutput = unit.Outputs.Count;

            unit.RegisterOutputParam(new Param_Number(), SteadyFactorOutput, SteadyFactorOutput,
                "The response factor of the steady-state response alone, at its worst pace" + (full ? " and walker" : "") +
                ": the harmonics of the walking force driving the modes. Counted below the cut-off and on stairs.");
            unit.RegisterOutputParam(new Param_Number(), TransientFactorOutput, TransientFactorOutput,
                "The response factor of the transient response alone, at its worst pace" + (full ? " and walker" : "") +
                ": each footfall setting the modes ringing. Not worked out on stairs.");
            unit.RegisterOutputParam(new Param_Number(), SteadyAccelerationOutput, SteadyAccelerationOutput,
                $"The weighted rms acceleration of the steady-state response at each {at} [{Units.Length}/{Units.Time}²]. 0.005 is R = 1.");
            unit.RegisterOutputParam(new Param_Number(), TransientAccelerationOutput, TransientAccelerationOutput,
                $"The weighted rms acceleration of the transient response over one pace at each {at} [{Units.Length}/{Units.Time}²].");

            // Folded away until they are opened.
            var frequency = new GH_ExtendableMenu(0, "FootfallAnalysis_FrequencyData")
            {
                Name = "Frequency data",
                Header = "The activity, the walking paces tried, the cut-off frequency and the damping"
            };
            for (int i = firstMenuInput; i < unit.Inputs.Count; i++)
                frequency.RegisterInputPlug(unit.Inputs[i]);
            unit.AddMenu(frequency);

            var responses = new GH_ExtendableMenu(1, "FootfallAnalysis_Responses")
            {
                Name = "Steady state and transient",
                Header = "The two responses apart: their response factors and weighted rms accelerations"
            };
            for (int i = firstMenuOutput; i < unit.Outputs.Count; i++)
                responses.RegisterOutputPlug(unit.Outputs[i]);
            unit.AddMenu(responses);

            // Last, so that the outputs of files saved before it keep their places.
            unit.RegisterOutputParam(new Param_GenericObject(), ModelOutput, ModelOutput,
                "The model it was given, with what was found at each node on it - R, its steady-state and transient parts and their accelerations, " +
                "the critical frequency: for the AlpacaModel input of the Report, whose Footfall topic draws the model coloured by any of them and lists the worst.");
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            foreach (var (input, names) in new[] { (ActivityInput, Activities), (WeightingInput, Weightings) })
            {
                int index = Params.IndexOfInputParam(input);
                if (index >= 0)
                    ValueListUtils.UpdateValueLists(this, index, names);
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            if (unit == null) return;
            bool full = unit.Name == FullUnit;

            Model model = null;
            if (!DA.GetData(ModelInput, ref model) || model == null) return;

            if (!model.IsModal || model.Eigenvalues == null || model.Recorders == null || model.Recorders.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Connect the AlpacaModel output of Natural Vibration Analysis: Footfall Analysis works from its modes.");
                return;
            }

            if (!LicenseGate.AllowsModel(this, model))
                return;

            var settings = new FootfallSettings();

            string activityText = settings.Activity.ToString();
            DA.GetData(ActivityInput, ref activityText);
            string activityName = Activities.FirstOrDefault(a => string.Equals(a, activityText?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (activityName == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Activity is \"{activityText}\". Give {string.Join(" or ", Activities)}.");
                return;
            }
            settings.Activity = (FootfallActivity)Enum.Parse(typeof(FootfallActivity), activityName);

            string weightingText = settings.Weighting.ToString();
            DA.GetData(WeightingInput, ref weightingText);
            string weightingName = Weightings.FirstOrDefault(w => string.Equals(w, weightingText?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (weightingName == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Frequency Weighting is \"{weightingText}\". Give one of {string.Join(", ", Weightings)}.");
                return;
            }
            settings.Weighting = (FrequencyWeighting)Enum.Parse(typeof(FrequencyWeighting), weightingName);

            int footsteps = 0;
            if (DA.GetData(FootstepsInput, ref footsteps))
                settings.Footsteps = footsteps;

            double walker = settings.WalkerMass, min = settings.MinimumFrequency, max = settings.MaximumFrequency;
            double cutOff = settings.CutOff, damping = settings.Damping;
            int steps = settings.FrequencySteps;
            DA.GetData(WalkerMassInput, ref walker);
            DA.GetData(MinimumInput, ref min);
            DA.GetData(MaximumInput, ref max);
            DA.GetData(StepsInput, ref steps);
            DA.GetData(CutOffInput, ref cutOff);
            DA.GetData(DampingInput, ref damping);
            settings.WalkerMass = walker;
            settings.MinimumFrequency = min;
            settings.MaximumFrequency = max;
            settings.FrequencySteps = steps;
            settings.CutOff = cutOff;
            settings.Damping = damping;

            string problem = settings.Problem();
            if (problem != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
                return;
            }

            if (damping > 0.2)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Damping is {damping:0.###}, which is {damping * 100:0.#}%. Give a ratio: 3% is 0.03.");

            var table = Footfall.TablePaces(settings.Activity);
            if (min < table.Min - 1e-9 || max > table.Max + 1e-9)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"P354 gives its {settings.Activity.ToString().ToLowerInvariant()} coefficients for paces of {table.Min} to {table.Max} Hz. " +
                    "Paces outside that range use them as they are.");

            // Where the walker goes and where the response is read: a node for each point given.
            var positions = model.Nodes.Select(node => node.Pos).ToList();
            if (positions.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The model has no nodes.");
                return;
            }
            var walk = new List<Point3d>();
            DA.GetDataList(WalkInput, walk);
            var feel = new List<Point3d>();
            if (full)
                DA.GetDataList(FeelInput, feel);

            var where = new StringBuilder();
            int[] walked = Places(model, positions, walk, WalkInput, where);
            int[] read = full && feel.Count > 0 ? Places(model, positions, feel, FeelInput, where) : walked;
            if (full && feel.Count == 0)
                where.AppendLine("Feel: none given, so the response is read at the Walk nodes.");

            List<double[,]> shapes;
            try
            {
                shapes = Alpaca4d.Result.Read.MassNormalisedModes(model);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The modes could not be read: {ex.Message}");
                return;
            }

            int count = Math.Min(shapes.Count, model.Eigenvalues.Count);
            if (count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The model holds no modes.");
                return;
            }

            var frequencies = model.Eigenvalues.Take(count)
                .Select(e => e > 0.0 ? Math.Sqrt(e) / (2.0 * Math.PI) : 0.0)
                .ToList();
            var modes = shapes.Take(count)
                .Select(shape => Enumerable.Range(0, shape.GetLength(0)).Select(node => shape[node, settings.Direction]).ToArray())
                .ToList();

            int fundamental = Footfall.Fundamental(frequencies, shapes.Take(count).ToList(), settings.Direction);
            if (fundamental < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No mode has a frequency above 0 Hz.");
                return;
            }
            if (fundamental > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"The fundamental frequency is taken from mode {fundamental + 1}, {frequencies[fundamental]:0.###} Hz, the first to move " +
                    $"mostly vertically. Mode 1, {frequencies[0]:0.###} Hz, moves mostly sideways.");

            // Each node is worked out once, however many points took it; the outputs follow the points.
            int[] responseNodes = read.Distinct().ToArray();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = full
                ? Footfall.FullExcitation(frequencies, modes, frequencies[fundamental], settings, walked.Distinct().ToArray(), responseNodes)
                : Footfall.SelfExcitation(frequencies, modes, frequencies[fundamental], settings, responseNodes);
            clock.Stop();
            var slotOf = new Dictionary<int, int>();
            for (int i = 0; i < responseNodes.Length; i++)
                slotOf[responseNodes[i]] = i;
            int[] slots = read.Select(node => slotOf[node]).ToArray();

            // A mode at 0 Hz is one the eigen solver gave up on: ARPACK hands back zeros for the modes it
            // did not converge on. They are left out, and said so.
            int unsolved = frequencies.Count(f => !(f > 0.0));
            if (unsolved > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{unsolved} of the {count} modes came back at 0 Hz: the eigen solver did not converge on them, and they are left out. " +
                    "Ask Natural Vibration Analysis for fewer modes, or solve with -fullGenLapack.");

            // The modes asked for must reach as far as the responses take them.
            bool stairs = settings.Activity == FootfallActivity.Stairs;
            double needed = Math.Max(result.HighFrequency && !stairs ? 0.0 : cutOff + 2.0,
                                     result.TransientChecked || (stairs && result.HighFrequency) ? 2.0 * result.Fundamental : 0.0);
            double highest = frequencies.Max();
            if (highest < needed)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The highest mode solved is at {highest:0.##} Hz, but the response takes the modes up to " +
                    $"{needed:0.##} Hz. Ask Natural Vibration Analysis for more modes.");

            if (settings.Activity == FootfallActivity.Stairs && result.HighFrequency)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"The fundamental frequency, {result.Fundamental:0.##} Hz, is above the cut-off. The stair is still checked for its " +
                    "steady state, over the modes up to twice the fundamental: SCI AD 406 finds the transient prediction does not apply to stairs.");

            DA.SetDataList(FactorOutput, slots.Select(i => result.ResponseFactor[i]));
            DA.SetDataList(PointOutput, read.Select(node => positions[node]));
            DA.SetDataList(CriticalOutput, slots.Select(i => result.CriticalFrequency[i]));
            if (full)
                DA.SetDataList(WalkerOutput, slots.Select(i => positions[result.ExcitationNode[i]]));
            DA.SetDataList(SteadyFactorOutput, slots.Select(i => result.SteadyStateFactor[i]));
            DA.SetDataList(TransientFactorOutput, slots.Select(i => result.TransientFactor[i]));
            DA.SetDataList(SteadyAccelerationOutput, slots.Select(i => result.SteadyStateAcceleration[i]));
            DA.SetDataList(TransientAccelerationOutput, slots.Select(i => result.TransientAcceleration[i]));
            DA.SetData(ReportOutput, Report(result, frequencies, model, settings, where.ToString(), clock.Elapsed.TotalSeconds));

            // The model, with what was found on it, for the Report.
            var mapped = model.ShallowCopy();
            mapped.Footfall = new Alpaca4d.FootfallResponse
            {
                Nodes = read.Select(node => model.Nodes[node].Id ?? node).ToArray(),
                Factors = slots.Select(i => result.ResponseFactor[i]).ToArray(),
                CriticalFrequencies = slots.Select(i => result.CriticalFrequency[i]).ToArray(),
                SteadyStateFactors = slots.Select(i => result.SteadyStateFactor[i]).ToArray(),
                TransientFactors = slots.Select(i => result.TransientFactor[i]).ToArray(),
                SteadyStateAccelerations = slots.Select(i => result.SteadyStateAcceleration[i]).ToArray(),
                TransientAccelerations = slots.Select(i => result.TransientAcceleration[i]).ToArray(),
                TransientChecked = result.TransientChecked,
                Activity = settings.Activity.ToString(),
                Damping = settings.Damping
            };
            DA.SetData(ModelOutput, mapped);
        }

        /// <summary>
        /// The node each point takes, the nearest; with no points, the nodes of the shell elements, or every
        /// node of a model with none. Says which in <paramref name="where"/>, and warns of a point far from
        /// any node.
        /// </summary>
        private int[] Places(Model model, List<Point3d> positions, List<Point3d> points, string input, StringBuilder where)
        {
            if (points.Count == 0)
            {
                int[] shells = Footfall.ShellNodes(model);
                if (shells.Length > 0)
                {
                    where.AppendLine($"{input}: none given, so the {shells.Length} nodes of the shell elements.");
                    return shells;
                }
                where.AppendLine($"{input}: none given, and the model has no shell elements, so every node, {positions.Count}.");
                return Enumerable.Range(0, positions.Count).ToArray();
            }

            int[] nodes = Footfall.NearestNodes(positions, points, out double farthest);
            int distinct = nodes.Distinct().Count();
            where.AppendLine($"{input}: {points.Count} point{(points.Count == 1 ? "" : "s")} on {distinct} node{(distinct == 1 ? "" : "s")}, " +
                             $"the farthest {farthest:0.###} {Units.Length} from its node.");

            var box = new BoundingBox(positions);
            double size = box.IsValid ? box.Diagonal.Length : 0.0;
            if (farthest > 0.05 * size)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"A {input} point is {farthest:0.###} {Units.Length} from the nearest node, far for a model {size:0.##} {Units.Length} across. " +
                    "Each point takes its nearest node: check the points are on the structure.");
            return nodes;
        }

        private static string Report(FootfallResult result, IList<double> frequencies, Model model, FootfallSettings settings, string where, double seconds)
        {
            string Modes(int[] modes) => modes.Length == 0
                ? "none"
                : $"{modes.Length} ({frequencies[modes[0] - 1]:0.###} to {frequencies[modes.Last() - 1]:0.###} Hz)";
            string Node(int index)
            {
                var at = model.Nodes[index].Pos;
                return $"node {model.Nodes[index].Id ?? index + 1} ({at.X:0.###}, {at.Y:0.###}, {at.Z:0.###})";
            }

            var text = new StringBuilder();
            text.AppendLine((result.FullExcitation ? "Full excitation" : "Self excitation") +
                            $", {settings.Activity.ToString().ToLowerInvariant()} (P354 Table " +
                            $"{(settings.Activity == FootfallActivity.Stairs ? "3.2" : "3.1")}), {settings.Paces().Length} paces from " +
                            $"{settings.MinimumFrequency:0.###} to {settings.MaximumFrequency:0.###} Hz.");
            text.Append(where);
            text.AppendLine(result.FullExcitation
                ? $"The walker tried at {result.WalkedNodes} nodes for each of {result.ResponseNode.Length}: " +
                  $"{(double)result.WalkedNodes * result.ResponseNode.Length:#,0} pairs, in {seconds:0.##} s."
                : $"The walker at each of {result.ResponseNode.Length} nodes, the response read where it stands.");
            text.AppendLine($"Walker {settings.WalkerMass:0.#} kg, damping {settings.Damping * 100:0.##}%, weighting {settings.Weighting}, " +
                            (settings.Footsteps.HasValue
                                ? $"{settings.Footsteps} footsteps, build-up factor {Footfall.BuildUp(settings.Damping, settings.Footsteps):0.###}."
                                : "steady state reached, build-up factor 1."));
            text.AppendLine($"Fundamental frequency {result.Fundamental:0.###} Hz, " +
                            (result.HighFrequency ? "at or above" : "below") + $" the cut-off of {settings.CutOff:0.##} Hz: " +
                            (settings.Activity == FootfallActivity.Stairs ? "a stair, checked for its steady state." :
                             result.HighFrequency ? "a high-frequency floor, checked for its transient response." :
                             "a low-frequency floor, checked for both responses."));
            text.AppendLine($"Modes taken: steady state {Modes(result.SteadyStateModes)}; transient " +
                            (result.TransientChecked ? Modes(result.TransientModes) : "not checked") + ".");

            if (result.ResponseFactor.Length == 0)
                return text.ToString();

            int worst = 0;
            for (int i = 1; i < result.ResponseFactor.Length; i++)
                if (result.ResponseFactor[i] > result.ResponseFactor[worst])
                    worst = i;

            text.AppendLine($"Largest response factor: R = {result.ResponseFactor[worst]:0.##} at {Node(result.ResponseNode[worst])}" +
                            (result.FullExcitation ? $", with the walker at {Node(result.ExcitationNode[worst])}" : "") +
                            $", at a pace of {result.CriticalFrequency[worst]:0.###} Hz.");
            text.Append($"There the steady state gives R = {result.SteadyStateFactor[worst]:0.##} ({result.SteadyStateAcceleration[worst]:0.#####} m/s² rms)");
            text.AppendLine(result.TransientChecked
                ? $" and the transient R = {result.TransientFactor[worst]:0.##} ({result.TransientAcceleration[worst]:0.#####} m/s² rms)."
                : ".");
            return text.ToString();
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Footfall_Analysis__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{7E4C1A92-3D58-4B6F-A0E7-2C9F5B81D364}");
    }
}
