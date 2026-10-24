using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Linear Buckling Analysis: the load factors at which the assembled model buckles under its
    /// loads, lowest first, and the shapes it buckles into. OpenSees has no such analysis; how it is
    /// made to give one is in <see cref="Alpaca4d.Buckling"/>.
    ///
    /// The model handed on is the one the analysis ran - beams split as Divisions asks - with the
    /// buckled shapes as its steps, step 0 the first mode, so Nodal Displacements and View Results
    /// read them as they read a static analysis.
    /// </summary>
    public class LinearBuckling : GH_Component
    {
        public LinearBuckling()
          : base("Linear Buckling Analysis (Alpaca4d)", "Linear Buckling",
            "Finds the load factors at which the model buckles under its loads, lowest first - the first is the " +
            "critical load factor α_cr - and the shapes it buckles into.\n" +
            "The loads are those of the assembled model at load factor 1, as one step of Run Analysis applies them. " +
            "The analysis is linear: elastic, about the undeformed shape, with the axial forces of a first-order analysis.\n" +
            "For it every beam gets a PDelta transformation. Beam elements buckle in bending only: lateral-torsional " +
            "and torsional buckling need warping, which they do not have.\n" +
            "Connect the assembled model, then feed the buckled model to Nodal Displacements or View Results: its " +
            "steps are the buckling modes, step 0 the first.",
            "Alpaca4d", "07_Analysis")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        private const int ModelInput = 0, ModesInput = 1, DivisionsInput = 2;
        private const int ModelOutput = 0, FactorsOutput = 1, LogOutput = 2;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel",
                "The assembled model, from Assemble Model, with the loads to buckle it under. Not from Run Analysis.",
                GH_ParamAccess.item);
            pManager.AddIntegerParameter("Buckling Modes", "Buckling Modes",
                "How many of the lowest buckling modes to find.",
                GH_ParamAccess.item, 1);
            pManager.AddIntegerParameter("Divisions", "Divisions",
                "How many elements every beam is split into for the analysis; 1 leaves them as they are.\n" +
                "A PDelta beam only feels the sway of its chord, so a member that buckles on its own, between its " +
                "ends, needs several: about 85 % / n² too high with n elements to a half wave - 4 elements give 5 %, " +
                "8 give 1.3 %, and twice the elements a quarter of the error. The sway of a frame needs none. The " +
                "split model is the one handed on; the one connected is not changed.",
                GH_ParamAccess.item, 1);
            pManager[ModesInput].Optional = true;
            pManager[DivisionsInput].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel",
                "The buckled model: its steps are the buckling modes, step 0 the first, each scaled to a largest " +
                "translation of 1. Feed it to Nodal Displacements or View Results. Its beams are split as Divisions asks.",
                GH_ParamAccess.item);
            pManager.AddNumberParameter("Load Factors", "Load Factors",
                "One per mode, lowest first: what the loads are multiplied by for the model to buckle. The first is α_cr; " +
                "below 1 the model buckles before its loads are reached.",
                GH_ParamAccess.list);
            pManager.AddGenericParameter("log", "log", "What OpenSees printed. Read it when the analysis fails.", GH_ParamAccess.list);
        }

        /// <summary>
        /// Whether an OpenSees prints its matrices sparse, by its path and when it was built: asking
        /// takes a run of its own, and the answer only changes with the executable.
        /// </summary>
        private static readonly Dictionary<string, (DateTime built, bool sparse)> PrintsSparse =
            new Dictionary<string, (DateTime, bool)>();

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var model = new Model();
            if (!DA.GetData(ModelInput, ref model)) return;

            int modes = 1, divisions = 1;
            DA.GetData(ModesInput, ref modes);
            DA.GetData(DivisionsInput, ref divisions);
            if (modes < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Buckling Modes must be at least 1; 1 is used.");
                modes = 1;
            }
            if (divisions < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Divisions must be at least 1; the beams are left whole.");
                divisions = 1;
            }

            // An analysed model's deck ends in "wipe", and what it holds is no longer the model.
            if (model.IsAnalysed || model.IsBuckling)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "This AlpacaModel has already been analysed. Connect the Linear Buckling Analysis to the AlpacaModel " +
                    "output of Assemble Model.");
                return;
            }

            if (!LicenseGate.AllowsModel(this, model))
                return;

            var files = AnalysisFiles.For(this, DA);
            string recorderPath = files.OwnFile("recorder_buckling", ".mpco");

            // Runs a deck file through the configured OpenSees.
            Buckling.OpenSeesRunner run = deckPath => new Model { FileName = deckPath }.RunOpenSees();

            bool sparse;
            Buckling.Outcome outcome;
            try
            {
                sparse = CanPrintSparse(files.Folder, run);
                outcome = Buckling.Analyse(model.Tcl, model.LoadPatternTclStart, divisions, modes, model.Nodes.Count,
                                           files.Folder, $"buckling_{files.Run}", recorderPath, sparse, run);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            DA.SetDataList(LogOutput, new List<string> { outcome.Log });
            Report(outcome, model, divisions, modes);

            if (outcome.Failure != null && outcome.Shapes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, outcome.Failure);
                return;
            }
            if (outcome.Failure != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, outcome.Failure);

            var factors = outcome.Factors.ToList();
            DA.SetDataList(FactorsOutput, factors);
            if (outcome.Shapes.Count > 0)
                DA.SetData(ModelOutput, Buckled(model, outcome, recorderPath));
        }

        private static bool CanPrintSparse(string folder, Buckling.OpenSeesRunner run)
        {
            string openSees = Alpaca4d.Application.OpenSees;
            if (string.IsNullOrWhiteSpace(openSees) || !System.IO.File.Exists(openSees))
                return false; // running the deck will say what is wrong with the path

            var built = System.IO.File.GetLastWriteTimeUtc(openSees);
            lock (PrintsSparse)
            {
                if (PrintsSparse.TryGetValue(openSees, out var known) && known.built == built)
                    return known.sparse;
            }
            bool sparse = Buckling.CanPrintSparse(folder, run);
            lock (PrintsSparse)
                PrintsSparse[openSees] = (built, sparse);
            return sparse;
        }

        /// <summary>What the analysis changed to run, and what it found, as messages.</summary>
        private void Report(Buckling.Outcome outcome, Model model, int divisions, int modes)
        {
            var deck = outcome.Deck;
            if (deck == null)
                return;

            if (deck.TransformsMadePDelta > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{deck.TransformsMadePDelta} beam transformation{(deck.TransformsMadePDelta == 1 ? " was" : "s were")} made " +
                    "PDelta for the analysis: that is what gives a beam a geometric stiffness.");
            if (deck.Solids > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Solid elements have no geometric stiffness in OpenSees: they stiffen the model without ever softening under load.");
            if (deck.OneWayMaterials > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Tension-only and compression-only bars and one-way springs are taken as they are at zero load, and linear: " +
                    "every bar and spring holds both ways, and a spring with a gap holds nothing. Buckling is linear; whether a bar goes " +
                    "slack is a question for a non-linear analysis.");
            if (deck.PatternsDropped > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{deck.PatternsDropped} ground motion pattern{(deck.PatternsDropped == 1 ? " was" : "s were")} left out: they load nothing in a static step.");
            if (divisions == 1 && model.Beams.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "The beams are not split. That is enough for the sway of a frame; a member that buckles on its own, between its " +
                    "ends, needs Divisions of 4 or more.");
            foreach (var warning in deck.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            var solution = outcome.Solution;
            if (solution == null)
                return;

            if (!solution.Converged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The load factors did not settle in {solution.Iterations} iterations; they are the last estimate.");

            var factors = solution.Factors;
            if (factors.Length == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Nothing buckles under these loads: no member is compressed by them.");
            else if (factors.Length < modes)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Only {factors.Length} buckling mode{(factors.Length == 1 ? "" : "s")} found under these loads.");
            if (factors.Length > 0 && factors[0] < 1.0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The first load factor is {factors[0]:0.###}: the model buckles before its loads are reached.");
            if (outcome.Asymmetry > 1e-6)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"The stiffness matrices are not quite symmetric ({outcome.Asymmetry:0.#e0}); their symmetric parts were used.");
        }

        /// <summary>
        /// The model as the analysis ran it, carrying the buckled shapes: the split nodes after the
        /// model's own, the split beams as their pieces, and the recorder of the shapes.
        /// </summary>
        private static Model Buckled(Model model, Buckling.Outcome outcome, string recorderPath)
        {
            var deck = outcome.Deck;
            int modelNodes = model.Nodes.Count;
            var buckled = model.ShallowCopy();

            buckled.Nodes = new List<Alpaca4d.Element.Node>(model.Nodes);
            foreach (var node in deck.SplitNodes)
                buckled.Nodes.Add(new Alpaca4d.Element.Node(Buckling.OutputTag(deck, node.Tag, modelNodes), node.X, node.Y, node.Z) { Ndf = 6 });

            if (deck.Pieces.Count > 0)
            {
                var beams = new List<IBeam>();
                var elements = new List<IElement>();
                foreach (var element in model.Elements)
                {
                    if (element is IBeam beam && beam.Id.HasValue && deck.Pieces.TryGetValue(beam.Id.Value, out var pieces))
                    {
                        var split = pieces.Select(piece => Piece(beam, piece, deck, modelNodes)).ToList();
                        beams.AddRange(split);
                        elements.AddRange(split);
                    }
                    else
                    {
                        if (element is IBeam whole) beams.Add(whole);
                        elements.Add(element);
                    }
                }
                buckled.Beams = beams;
                buckled.Elements = elements;
            }

            buckled.Recorders = new List<IRecorder> { new Alpaca4d.Recorder(recorderPath, new[] { "displacement", "rotation" }, new string[0]) };
            buckled.IsAnalysed = true;
            buckled.IsStatic = true;
            buckled.IsTransient = false;
            buckled.IsModal = false;
            buckled.IsBuckling = true;
            buckled.BucklingFactors = outcome.Factors.ToList();
            buckled.NumberOfModes = outcome.Factors.Length;
            return buckled;
        }

        /// <summary>A piece of a split beam, for drawing: straight between its nodes, with the beam's section and axes.</summary>
        private static IBeam Piece(IBeam beam, Buckling.Piece piece, Buckling.Deck deck, int modelNodes)
        {
            var a = deck.Nodes[piece.INode];
            var b = deck.Nodes[piece.JNode];
            var line = new LineCurve(new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, b.Y, b.Z));
            var transf = new Alpaca4d.Element.GeomTransf(Alpaca4d.Element.GeomTransfType.PDelta, line, beam.GeomTransf.LocalZ);
            return new Alpaca4d.Element.ForceBeamColumn(line, beam.Section, transf)
            {
                Id = piece.Tag,
                INode = Buckling.OutputTag(deck, piece.INode, modelNodes),
                JNode = Buckling.OutputTag(deck, piece.JNode, modelNodes),
                ElementId = beam.ElementId,
                Color = beam.Color,
            };
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Linear_Buckling_Analysis__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{6E2B9C41-8D57-4A3F-B1E0-7C94D2A6F815}");
    }
}
