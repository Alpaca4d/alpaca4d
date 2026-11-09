using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Pushover Analysis: the model's own loads applied and held, then a lateral load pattern scaled
    /// up until one node has moved a set distance - the capacity curve of the structure, base shear
    /// against that node's displacement. How the deck goes about it is in <see cref="Alpaca4d.Pushover"/>.
    ///
    /// The model handed on is the one pushed, with every converged step recorded, so each point of the
    /// curve can be drawn in Model View and read with the result components by its Step.
    /// </summary>
    public class PushoverAnalysis : GH_Component
    {
        public PushoverAnalysis()
          : base("Pushover Analysis (Alpaca4d)", "Pushover",
            "Pushes the model sideways until it gives way: the capacity curve, base shear against the displacement of a control point.\n" +
            "The loads of the assembled model - its self-weight, its floors - are applied first and held. Then the lateral loads are " +
            "scaled up, step by step, until the control point has moved Target along Direction. The push is controlled by that " +
            "displacement, not by the load, so it goes on past the peak, where the structure softens.\n" +
            "For the beams to yield, give them fibre sections (Fiber Section, MomentCurvature tab) on ForceBeamColumn: an elastic " +
            "section stays elastic, and its curve is a straight line. With P-Delta on, the loads the columns carry push them further " +
            "as the frame sways.\n" +
            "Feed the pushed model to Model View or to the result components with a Step from the Step output to see any point of the curve.",
            "Alpaca4d", "07_Analysis")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        private static readonly List<string> Directions = new List<string> { "X", "Y", "Z" };

        private const int ModelInput = 0, PointInput = 1, DirectionInput = 2, TargetInput = 3, StepsInput = 4, LoadsInput = 5, PDeltaInput = 6;
        private const int ModelOutput = 0, DisplacementOutput = 1, ShearOutput = 2, FactorOutput = 3, StepOutput = 4,
                          HingesOutput = 5, HingePointsOutput = 6, YieldsAtOutput = 7, LogOutput = 8;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel",
                "The model, from Assemble Model, with the loads to hold while it is pushed: its self-weight and its floors. They are " +
                "applied in 10 steps first, at load factor 1. An analysed model is pushed as it was before it was analysed.",
                GH_ParamAccess.item);
            pManager.AddPointParameter("Control Point", "Control Point",
                $"The point whose displacement drives the push and makes the curve's x: a node of the model, usually on the roof [{Units.Length}].",
                GH_ParamAccess.item);
            pManager.AddTextParameter("Direction", "Direction",
                "The direction of the push: X, Y or Z, global.",
                GH_ParamAccess.item, Directions[0]);
            pManager.AddNumberParameter("Target", "Target",
                $"How far to push the control point along Direction, from where the model's own loads left it [{Units.Length}]. " +
                "Negative pushes the other way.",
                GH_ParamAccess.item);
            pManager.AddIntegerParameter("Steps", "Steps",
                "How many steps to reach Target in. A step that does not converge is tried with other algorithms, then in halves.",
                GH_ParamAccess.item, 100);
            pManager.AddGenericParameter("Lateral Loads", "Lateral Loads",
                "Point Loads giving the shape of the push: their forces at load factor 1, all scaled together - one per floor, " +
                "in proportion to its mass or its height, say. Leave empty to push the control point alone, with 1 kN along Direction.",
                GH_ParamAccess.list);
            pManager.AddBooleanParameter("P-Delta", "P-Delta",
                "Gives every beam with a Linear transformation a PDelta one for the push, so the loads held on the columns push " +
                "them further as the frame sways, and the curve comes down past its peak. Corotational beams are left as they are.",
                GH_ParamAccess.item, true);
            pManager[StepsInput].Optional = true;
            pManager[PDeltaInput].Optional = true;
            pManager[DirectionInput].Optional = true;
            pManager[LoadsInput].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel",
                "The pushed model, every converged step recorded: the model's own loads first, then the push. Feed it to Model " +
                "View or to the result components with a Step from the Step output.",
                GH_ParamAccess.item);
            pManager.AddNumberParameter("Displacement", "Displacement",
                $"The control point's displacement along Direction at each point of the curve, from where the model's own loads left it [{Units.Length}].",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Base Shear", "Base Shear",
                $"The base shear at each point of the curve: the reactions along Direction, added up and turned to resist the push [{Units.Force}].",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Load Factor", "Load Factor",
                "The factor on the Lateral Loads at each point of the curve.",
                GH_ParamAccess.list);
            pManager.AddIntegerParameter("Step", "Step",
                "The recorded step of each point of the curve, for Model View and the result components. The first point is the " +
                "model under its own loads, before the push.",
                GH_ParamAccess.list);
            pManager.AddTextParameter("Hinges", "Hinges",
                "Every point of a beam on a fibre section that yields during the push, in the order it does: which beam, where " +
                "along it, the point of the curve it yields at, and when its concrete crushes or a bar reaches its ultimate strain. " +
                "Model View draws them, ticked Plastic hinges, at whatever Step it draws.",
                GH_ParamAccess.list);
            pManager.AddPointParameter("Hinge Points", "Hinge Points",
                $"Where each of the Hinges is, on the model as it was built [{Units.Length}].",
                GH_ParamAccess.list);
            pManager.AddIntegerParameter("Yields At", "Yields At",
                "The point of the curve each of the Hinges yields at: its place in Displacement, Base Shear and Step.",
                GH_ParamAccess.list);
            pManager.AddTextParameter("log", "log",
                "What OpenSees printed, a line repeated written once with its count. Read it when the push stops short.",
                GH_ParamAccess.item);
        }

        protected override void BeforeSolveInstance()
        {
            ValueListUtils.UpdateValueLists(this, DirectionInput, Directions);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Model model = null;
            if (!DA.GetData(ModelInput, ref model) || model == null) return;

            Point3d control = Point3d.Unset;
            if (!DA.GetData(PointInput, ref control)) return;

            string direction = Directions[0];
            DA.GetData(DirectionInput, ref direction);
            int dof = Directions.FindIndex(d => string.Equals(d, direction?.Trim(), StringComparison.OrdinalIgnoreCase)) + 1;
            if (dof < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Direction is \"{direction}\". Give X, Y or Z.");
                return;
            }

            double target = 0.0;
            if (!DA.GetData(TargetInput, ref target)) return;
            if (!(Math.Abs(target) > 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Target is how far to push the control point, so it cannot be 0.");
                return;
            }

            int steps = 100;
            DA.GetData(StepsInput, ref steps);
            if (steps < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Steps must be at least 1; 1 is used.");
                steps = 1;
            }

            bool pDelta = true;
            DA.GetData(PDeltaInput, ref pDelta);

            var lateral = new List<Alpaca4d.Loads.PointLoad>();
            var given = new List<object>();
            DA.GetDataList(LoadsInput, given);
            foreach (var item in given)
            {
                object value = item is Grasshopper.Kernel.Types.IGH_Goo goo ? goo.ScriptVariable() : item;
                if (value is Alpaca4d.Loads.PointLoad load)
                    lateral.Add(load);
                else if (value != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Lateral Loads takes Point Loads; {value} is not one.");
                    return;
                }
            }

            // An analysed model's deck ends in "wipe", and what it holds is no longer the model; the
            // model it was analysed from is pushed instead.
            model = model.Unanalysed();
            if (model == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "This AlpacaModel has been analysed, and does not carry the model it was analysed from. " +
                    "Connect Pushover Analysis to the AlpacaModel output of Assemble Model.");
                return;
            }

            if (model.LoadPatterns.Any(pattern => pattern.PatternType == Alpaca4d.Loads.PatternType.UniformExcitation))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "The model has a ground motion (Uniform Excitation) among its loads. A pushover is static: give it the loads " +
                    "to hold - self-weight, floors - and nothing that moves the ground.");
                return;
            }

            if (!LicenseGate.AllowsModel(this, model))
                return;

            int controlNode = model.NodeTagNear(control, out int controlNdf);
            if (controlNode == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Control Point {Show(control)} is not on a node of the model. Put it on a beam end or a mesh vertex.");
                return;
            }

            var loads = new List<PushoverLoad>();
            if (lateral.Count == 0)
            {
                var force = new double[3];
                force[dof - 1] = Math.Sign(target);
                loads.Add(new PushoverLoad(controlNode, controlNdf, force[0], force[1], force[2]));
            }
            foreach (var load in lateral)
            {
                int node = model.NodeTagNear(load.Pos, out int ndf);
                if (node == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The lateral load at {Show(load.Pos)} is not on a node of the model.");
                    return;
                }
                loads.Add(new PushoverLoad(node, ndf, load.Force.X, load.Force.Y, load.Force.Z, load.Moment.X, load.Moment.Y, load.Moment.Z));
            }
            if (lateral.Count > 0 && loads.All(load => Math.Abs(load.Values[dof - 1]) == 0.0))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"None of the Lateral Loads has a force along {Directions[dof - 1]}, the direction of the push.");

            Remarks(model, pDelta);

            var files = AnalysisFiles.For(this, DA);
            string logPath = files.OwnFile("pushover", ".log");
            string curvePath = files.OwnFile("pushover_curve", ".out");
            int gravitySteps = model.LoadPatterns.Count > 0 ? 10 : 1;

            var pushed = model.ShallowCopy();
            pushed.BeforeAnalysis = model;
            pushed.FileName = files.OwnFile("pushover", ".tcl");
            pushed.Recorders = model.Recorders != null && model.Recorders.Count > 0
                ? model.Recorders.Select(item => item is Recorder mpco
                                             ? new Recorder(files.UserFile(mpco.FileName), mpco.NodeResults, mpco.ElementResults)
                                             : item).ToList<IRecorder>()
                : new List<IRecorder> { Recorder.MpcoPushover(files.OwnFile("recorder_pushover", ".mpco"), model) };
            pushed.Settings = null;
            pushed.IsStatic = true;
            pushed.IsTransient = false;
            pushed.IsModal = false;
            pushed.IsBuckling = false;

            pushed.Tcl = new List<string> { Pushover.LogTcl(logPath) };
            pushed.Tcl.AddRange(pDelta ? Pushover.WithPDelta(model.Tcl, out _) : model.Tcl);
            foreach (var recorder in pushed.Recorders)
                pushed.Tcl.Add(recorder.WriteTcl());
            pushed.Tcl.Add(Pushover.AnalysisTcl(controlNode, dof, target, steps, gravitySteps, loads,
                                                Pushover.FreeTag(model.Tcl), curvePath, logPath));

            foreach (var stale in new[] { curvePath, logPath })
                if (System.IO.File.Exists(stale))
                    System.IO.File.Delete(stale);

            string output, error;
            int exitCode;
            try
            {
                pushed.Serialise();
                (output, error, exitCode) = pushed.RunOpenSees();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var curve = Pushover.Read(curvePath, logPath, string.Join("\n", new[] { error, output }.Where(s => !string.IsNullOrWhiteSpace(s))),
                                      gravitySteps, target);
            DA.SetData(LogOutput, curve.Log);

            if (exitCode != 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "OpenSees stopped on an error in the deck. See the log output.");
                return;
            }
            if (curve.GravityFailed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "The model's own loads did not converge, so nothing was pushed. Run them with Run Analysis to see why, and see the log output.");
                return;
            }
            if (curve.Displacement.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The push recorded nothing. See the log output.");
                return;
            }

            if (curve.GravitySuspect)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Under the model's own loads a beam's fibre section had no stiffness left to invert, so the state the push starts " +
                    "from is not to be trusted. See the log output.");
            if (curve.Cut)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The push stopped at {curve.LastDisplacement:0.####} {Units.Length} of {target:0.####}: a beam's fibre section had no " +
                    "stiffness left to invert - all its fibres yielded with nothing to harden, or broken - and OpenSees goes on from " +
                    "there with forces that are not the materials'. The curve ends at the last step before it.");
            else if (curve.Stopped)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The push stopped at {curve.LastDisplacement:0.####} {Units.Length} of {target:0.####}: a step would not converge, " +
                    $"down to 1/{1 << Pushover.Halvings} of its size. The structure may have run out of strength - a mechanism, or " +
                    "fibres that broke - or it needs more Steps. The curve ends at the last step that converged.");

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Peak base shear {curve.PeakBaseShear:0.##} {Units.Force} at {curve.PeakDisplacement:0.####} {Units.Length}; " +
                $"{curve.Displacement.Count} points to {curve.LastDisplacement:0.####} {Units.Length}.");

            pushed.IsAnalysed = true;
            Hinges(DA, pushed, curve);
            DA.SetData(ModelOutput, pushed);
            DA.SetDataList(DisplacementOutput, curve.Displacement);
            DA.SetDataList(ShearOutput, curve.BaseShear);
            DA.SetDataList(FactorOutput, curve.LoadFactor);
            DA.SetDataList(StepOutput, curve.Step);
        }

        /// <summary>
        /// The plastic hinges of the push: where the beams on fibre sections yield, read off their section
        /// deformations at every point of the curve, in the order they yield.
        /// </summary>
        private void Hinges(IGH_DataAccess DA, Model pushed, PushoverCurve curve)
        {
            if (!Alpaca4d.Result.PlasticHinges.HasFibreBeams(pushed))
                return;

            List<Alpaca4d.Result.HingeEvent> events;
            try
            {
                events = Alpaca4d.Result.PlasticHinges.History(pushed, curve.Step);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The hinges were not read: " + ex.Message);
                return;
            }

            string Name(int b)
            {
                var beam = pushed.Beams[b];
                return string.IsNullOrWhiteSpace(beam.ElementId) ? $"element {beam.Id}" : $"{beam.ElementId.Trim()} (element {beam.Id})";
            }
            string At(int k) =>
                $"point {k}: {curve.Displacement[k] * 1000.0:0} mm, {curve.BaseShear[k]:0} {Units.Force}";

            var text = events.Select(e =>
                $"{Name(e.Beam)}, {Alpaca4d.Result.PlasticHinges.Where(e.Along)}: yields at {At(e.FirstYield)}" +
                (e.FirstUltimate >= 0 ? $"; {e.Failed} at {At(e.FirstUltimate)}" : "")).ToList();
            DA.SetDataList(HingesOutput, text);
            DA.SetDataList(HingePointsOutput, events.Select(e => e.Position));
            DA.SetDataList(YieldsAtOutput, events.Select(e => e.FirstYield));

            if (events.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No beam yielded: every fibre stayed below its yield strain.");
                return;
            }
            var first = events[0];
            int failed = events.Count(e => e.FirstUltimate >= 0);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{events.Count} hinge{(events.Count == 1 ? "" : "s")}; the first at {At(first.FirstYield)}, {Name(first.Beam)}, " +
                $"{Alpaca4d.Result.PlasticHinges.Where(first.Along)}" +
                (failed > 0 ? $"; {failed} reached the strain they fail at." : "."));
        }

        /// <summary>What is worth knowing about the model before it is pushed.</summary>
        private void Remarks(Model model, bool pDelta)
        {
            var frames = model.Beams.Where(beam => !(beam is Alpaca4d.Element.Truss)).ToList();
            var fibres = frames.Select(beam => beam.Section).OfType<Alpaca4d.Section.FiberSection>().Distinct().ToList();

            if (fibres.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "No beam has a fibre section, so none can yield: the curve comes out a straight line, unless something else of " +
                    "the model gives way. Give the beams a Fiber Section.");

            var flat = Pushover.WithoutHardening(fibres.SelectMany(section => section.Materials));
            if (flat.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{string.Join(", ", flat.Select(m => string.IsNullOrWhiteSpace(m.MatName) ? "A Steel01" : m.MatName))}: Steel01 with " +
                    "b = 0 has nothing to harden once it has yielded. A section whose fibres have all yielded then has no stiffness, " +
                    "and a force-based beam goes wrong on it before OpenSees says a word. Give b a small value, 0.001 to 0.01.");

            if (!pDelta && frames.Count > 0 && frames.All(beam => beam.GeomTransf?.Type == Alpaca4d.Element.GeomTransfType.Linear))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "P-Delta is off and every beam has a Linear transformation, so the push leaves out the second-order effects of " +
                    "the loads the columns carry.");
        }

        private static string Show(Point3d point) =>
            $"({point.X:0.###}, {point.Y:0.###}, {point.Z:0.###})";

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Pushover_Analysis__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{3F7A2D90-5C1B-4E86-9A4D-2B8E6C0F7135}");
    }
}
