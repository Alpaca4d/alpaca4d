using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Parameters;
using Rhino;

using Alpaca4d.Explorer;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Runs a design study: every combination of the sliders, value lists and panels wired into it,
    /// one solution each, for Explorer Record to save.
    ///
    /// It does what Colibri's Iterator did, with three differences that are the reason it is not
    /// simply Colibri:
    ///
    ///   - A design is recorded after its solution is over and Rhino has gone idle, not from inside
    ///     the solution. Grasshopper's preview is only up to date once the solution ends, so an image
    ///     taken from inside it could show the design before. Colibri shipped preview components of
    ///     its own to get round that; waiting is simpler, and works with any preview.
    ///   - Rhino stays responsive between designs. The study is driven from RhinoApp.Idle, one design
    ///     per turn, so the Fly button, Esc and the canvas all still answer.
    ///   - Designs already in the study are skipped without being solved, so a study that was stopped
    ///     goes on from where it stopped, and running it again costs nothing.
    ///
    /// Inputs come in pairs with outputs: In 1 feeds Val 1. A slider or a value list is moved where
    /// it is and the definition reads it as usual; a panel is not changed, so its line for the
    /// current design comes out of its Val output, which is what the definition should read.
    /// </summary>
    public class ExplorerInputs : GH_Component, IGH_VariableParameterComponent
    {
        private const int FixedInputs = 2;   // Fly, Subset
        private const int FixedOutputs = 2;  // Inputs, Name

        private const string SourceDescription =
            "A number slider, a value list or a panel to step through. Its name becomes the name of its axis in the explorer, so give it one.\n" +
            "A new input appears when the last one is used.";
        private const string ValueDescription =
            "The value of the input with the same name, for the design being solved. Read a panel's lines from here: the panel itself is not changed.";

        private ExplorerSource[] _sourceAt = new ExplorerSource[0];
        private List<ExplorerSource> _sources = new List<ExplorerSource>();
        private readonly Dictionary<Guid, int> _panelSteps = new Dictionary<Guid, int>();
        private ExplorerSubsetSettings _subset;
        private DesignSpace _space;
        private ExplorerFlight _flight;
        private bool? _flyWasOn;
        private bool _renamed;
        private string _lastRun = "";

        public ExplorerInputs()
          : base("Explorer Inputs (Alpaca4d)", "Explorer Inputs",
            "Runs a design study: every combination of the sliders, value lists and panels connected to it, " +
            "one solution each, for Explorer Record to save and the Alpaca4d Explorer to show.\n" +
            "Press Fly on the component, or set Fly to true, to start. Press it again, or Esc, to stop. " +
            "Designs already recorded are skipped, so a stopped study goes on where it left off.",
            "Alpaca4d", "12_Optimisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
            Params.ParameterSourcesChanged += OnSourcesChanged;
        }

        /// <summary>The nickname and tab every Alpaca4d component shows, with how the study stands under them.</summary>
        private string Status(string status) =>
            Alpaca4d.Gh.ComponentMessage.MyMessage(this) + (string.IsNullOrEmpty(status) ? "" : "\n" + status);

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Explorer_Inputs__Alpaca4d_;
        public override Guid ComponentGuid => new Guid("{1BB04F93-CCA6-44C5-9076-1E3B20918EC8}");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Fly", "Fly", "Starts the study when it turns true, as pressing Fly on the component does.", GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Subset", "Subset", "Runs only part of the study. Connect Explorer Subset.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddParameter(NewSourceInput(), "Input", "In 1", SourceDescription, GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Inputs", "Inputs", "The inputs of the design being solved, by name. Connect to the Inputs of Explorer Record.", GH_ParamAccess.list);
            pManager.AddTextParameter("Name", "Name", "A name for the design being solved, made of its input values, as Explorer Record names its files. Use it to name files of your own.", GH_ParamAccess.item);
            pManager.AddParameter(NewValueOutput(), "Value", "Val 1", ValueDescription, GH_ParamAccess.item);
        }

        public override void CreateAttributes()
        {
            m_attributes = new ExplorerInputsAttributes(this);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Needs a license whatever the study.
            if (!LicenseGate.Allows(this, "Explorer Inputs"))
            {
                Message = Status("");
                return;
            }

            bool fly = false;
            DA.GetData(0, ref fly);

            object subsetData = null;
            DA.GetData(1, ref subsetData);
            _subset = ExplorerGoo.As<ExplorerSubsetSettings>(subsetData);
            if (subsetData != null && _subset == null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Subset takes what Explorer Subset makes.");

            // While flying the sources stay as the flight found them: it is the one moving them.
            if (_flight == null)
            {
                var blockers = new List<string>();
                var warnings = new List<string>();
                _sources = GatherSources(blockers, warnings);
                foreach (var message in blockers) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
                foreach (var message in warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message);
                _space = BuildSpace();
                ShowSpace();
            }
            else
            {
                Message = Status(_flight.Progress);
            }

            var values = CurrentValues();
            DA.SetDataList(0, values);
            DA.SetData(1, StudyFile.FileStem(values.Select(value => value.Value).ToList()));
            for (int i = FixedInputs; i < _sourceAt.Length; i++)
            {
                int output = OutputFor(i);
                if (_sourceAt[i] != null && output < Params.Output.Count)
                    DA.SetData(output, _sourceAt[i].Goo);
            }

            // Fly turning true starts the study, once the solution it arrived in is over: its first
            // design cannot be solved inside this one. The first value read - a file opened with Fly
            // already true - is where it starts from, not a change.
            if (fly && _flyWasOn == false && _flight == null)
                WhenIdle(() => { if (_flight == null) Start(false); });
            _flyWasOn = fly;
        }

        protected override void AfterSolveInstance()
        {
            base.AfterSolveInstance();
            if (_renamed)
            {
                _renamed = false;
                Attributes.ExpireLayout();
            }
        }

        #region Sources

        /// <summary>
        /// What is wired in, as sources to step through, with the input and output of each named
        /// after it. Blockers are what stops a flight: two inputs with one name, or something wired
        /// in that cannot be stepped through. Warnings are what it can fly with.
        /// </summary>
        private List<ExplorerSource> GatherSources(List<string> blockers, List<string> warnings)
        {
            var sources = new List<ExplorerSource>();
            _sourceAt = new ExplorerSource[Params.Input.Count];
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = FixedInputs; i < Params.Input.Count; i++)
            {
                var input = Params.Input[i];
                string label = $"In {i - FixedInputs + 1}";
                if (input.SourceCount == 0)
                {
                    Rename(i, null);
                    continue;
                }
                if (input.SourceCount > 1)
                {
                    blockers.Add($"{label} has {input.SourceCount} wires. Connect one slider, value list or panel to each input.");
                    continue;
                }

                var param = input.Sources[0];
                int panelStep;
                _panelSteps.TryGetValue(param.InstanceGuid, out panelStep);
                var source = ExplorerSource.From(param, panelStep);
                if (source == null)
                {
                    blockers.Add($"{label} is wired to {param.NickName}, which is not a number slider, a value list or a panel, so there is nothing to step through.");
                    continue;
                }

                string name = NameOf(source, i);
                if (source.Name == null)
                    warnings.Add($"Give the {source.Kind} on {label} a name: it becomes the name of its axis in the explorer. Until then it is \"{name}\".");
                if (!names.Add(name))
                    blockers.Add($"Two inputs are called {name}. Give each a name of its own, or their columns cannot be told apart.");
                if (source.StepCount == 0)
                    blockers.Add($"The {source.Kind} {name} has nothing to step through.");

                _sourceAt[i] = source;
                sources.Add(source);
                Rename(i, name);
            }
            return sources;
        }

        private string NameOf(ExplorerSource source, int inputIndex)
        {
            return source.Name ?? $"Input {inputIndex - FixedInputs + 1}";
        }

        /// <summary>Names an input and its output after what is wired in, so the component reads like the study.</summary>
        private void Rename(int inputIndex, string name)
        {
            int number = inputIndex - FixedInputs + 1;
            var input = Params.Input[inputIndex];
            string inputName = name ?? $"In {number}";
            if (input.NickName != inputName)
            {
                input.NickName = inputName;
                _renamed = true;
            }

            int outputIndex = OutputFor(inputIndex);
            if (outputIndex >= Params.Output.Count) return;
            var output = Params.Output[outputIndex];
            string outputName = name ?? $"Val {number}";
            if (output.NickName != outputName)
            {
                output.NickName = outputName;
                _renamed = true;
            }
        }

        private DesignSpace BuildSpace()
        {
            return new DesignSpace(
                _sources.Select(source => source.StepCount).ToList(),
                _sources.Select(source => source.CurrentStep).ToList(),
                _subset?.Divisions.ToList(),
                _subset?.Ranges.ToList());
        }

        private void ShowSpace()
        {
            if (_sources.Count == 0)
            {
                Message = Status("Connect inputs");
                return;
            }
            if (_space.DivisionsIgnored)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Subset has {_subset.Divisions.Count} divisions for {_sources.Count} inputs. Give one per input, in order; until then every step is run.");
            if (_space.Overflows)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "These inputs have more combinations than can be counted. Take fewer steps of each with Explorer Subset, or give the sliders fewer decimals.");
            else if (_space.Count > 10000)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{Count(_space.Count)} designs is a long study. Explorer Subset can take fewer steps of each input.");

            string status = _space.Count == _space.Total
                ? $"{Count(_space.Count)} designs"
                : $"{Count(_space.Count)} of {(_space.Overflows ? "too many to count" : Count(_space.Total))} designs";
            if (!string.IsNullOrEmpty(_lastRun)) status += "\n" + _lastRun;
            Message = Status(status);
        }

        internal static string Count(long count) => count.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>The inputs of the design the sources are on now, by name.</summary>
        internal List<ExplorerValue> CurrentValues()
        {
            var values = new List<ExplorerValue>();
            for (int i = FixedInputs; i < _sourceAt.Length; i++)
                if (_sourceAt[i] != null)
                    values.Add(new ExplorerValue(NameOf(_sourceAt[i], i), _sourceAt[i].Value));
            return values;
        }

        /// <summary>Moves every source to its step of a design, without solving.</summary>
        internal void Apply(int[] steps)
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                _sources[i].Apply(steps[i]);
                if (_sources[i] is PanelSource)
                    _panelSteps[_sources[i].Param.InstanceGuid] = steps[i];
            }
            // A panel does not change when it is stepped, so nothing upstream would expire this.
            ExpireSolution(false);
        }

        #endregion

        #region Flying

        internal bool IsFlying => _flight != null;

        internal string ButtonText => _flight == null ? "Fly" : $"Stop  ·  {Count(_flight.Done)} of {Count(_flight.Count)}";

        /// <summary>The Fly button: starts a study, or stops the one running.</summary>
        internal void ToggleFlight(bool test)
        {
            if (_flight != null) _flight.Stop();
            else Start(test);
        }

        private void Start(bool test)
        {
            var document = OnPingDocument();
            if (document == null) return;
            // The toolbar's Disable Solver is the static flag; Enabled is only off for a document
            // the canvas is not showing. Either way NewSolution would do nothing, and the flight
            // would run through the study recording none of it.
            if (!GH_Document.EnableSolutions || !document.Enabled)
            {
                Tell("The Grasshopper solver is disabled. Enable it to fly.");
                return;
            }
            // The Fly button and the menu start a flight without a solution, so the license is
            // checked here as well; the solution has already opened the License window if it was due.
            string refusal = LicenseGate.Refusal("Explorer Inputs", showForm: false);
            if (refusal != null)
            {
                Tell(refusal);
                return;
            }

            var blockers = new List<string>();
            _sources = GatherSources(blockers, new List<string>());
            _space = BuildSpace();
            if (blockers.Count > 0)
            {
                Tell(string.Join("\n\n", blockers));
                return;
            }
            if (_sources.Count == 0 || _space.Count == 0)
            {
                Tell("There is nothing to fly through. Connect a number slider, a value list or a panel to In 1.");
                return;
            }
            if (!ExplorerRecord.AnyRecording(document) &&
                !Ask("No Explorer Record is recording, so the designs will be solved but not saved.\n\nFly anyway?"))
                return;

            IEnumerable<int[]> plan;
            long count;
            if (test)
            {
                var picked = Sample(_space.Count, 3);
                plan = picked.Select(ordinal => _space.Nth(ordinal)).ToList();
                count = picked.Count;
            }
            else
            {
                plan = _space.Enumerate();
                count = _space.Count;
            }

            _lastRun = "";
            _flight = new ExplorerFlight(this, document, plan, count);
            _flight.Start();
        }

        /// <summary>Called by the flight when it is over; <paramref name="summary"/> is null when it was cut short by the document closing.</summary>
        internal void FlightEnded(ExplorerFlight flight, string summary)
        {
            if (_flight != flight) return;
            _flight = null;
            if (summary == null) return;
            _lastRun = summary;
            ExpireSolution(true);
        }

        /// <summary>A few designs picked at random, to see the images and the 3D before a long study.</summary>
        private static List<long> Sample(long count, int how)
        {
            var random = new Random();
            var picked = new SortedSet<long>();
            while (picked.Count < Math.Min(how, count))
                picked.Add(Math.Min(count - 1, (long)(random.NextDouble() * count)));
            return picked.ToList();
        }

        private static void WhenIdle(Action action)
        {
            EventHandler handler = null;
            handler = (sender, e) =>
            {
                RhinoApp.Idle -= handler;
                action();
            };
            RhinoApp.Idle += handler;
        }

        private static void Tell(string message)
        {
            Rhino.UI.Dialogs.ShowMessage(message, "Explorer Inputs", Rhino.UI.ShowMessageButton.OK, Rhino.UI.ShowMessageIcon.Information);
        }

        private static bool Ask(string question)
        {
            return Rhino.UI.Dialogs.ShowMessage(question, "Explorer Inputs", Rhino.UI.ShowMessageButton.YesNo, Rhino.UI.ShowMessageIcon.Question)
                == Rhino.UI.ShowMessageResult.Yes;
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, _flight == null ? "Fly" : "Stop", (sender, e) => ToggleFlight(false));
            Menu_AppendItem(menu, "Test fly (3 designs at random)", (sender, e) => ToggleFlight(true), _flight == null);
            Menu_AppendSeparator(menu);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            _flight?.Abort();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                _flight?.Abort();
            base.DocumentContextChanged(document, context);
        }

        #endregion

        #region Variable parameters

        private static int OutputFor(int inputIndex) => inputIndex - FixedInputs + FixedOutputs;

        private static Param_GenericObject NewSourceInput()
        {
            return new Param_GenericObject
            {
                Name = "Input",
                NickName = "",
                Description = SourceDescription,
                Access = GH_ParamAccess.list,
                Optional = true,
                MutableNickName = false,
            };
        }

        private static Param_GenericObject NewValueOutput()
        {
            return new Param_GenericObject
            {
                Name = "Value",
                NickName = "",
                Description = ValueDescription,
                Access = GH_ParamAccess.item,
                MutableNickName = false,
            };
        }

        // Not while flying: the flight holds the inputs by position.
        public bool CanInsertParameter(GH_ParameterSide side, int index)
        {
            return _flight == null && side == GH_ParameterSide.Input && index >= FixedInputs;
        }

        public bool CanRemoveParameter(GH_ParameterSide side, int index)
        {
            return _flight == null && side == GH_ParameterSide.Input && index >= FixedInputs && Params.Input.Count > FixedInputs + 1;
        }

        public IGH_Param CreateParameter(GH_ParameterSide side, int index)
        {
            Params.RegisterOutputParam(NewValueOutput(), OutputFor(index));
            return NewSourceInput();
        }

        public bool DestroyParameter(GH_ParameterSide side, int index)
        {
            int output = OutputFor(index);
            if (output < Params.Output.Count)
                Params.UnregisterOutputParameter(Params.Output[output]);
            return true;
        }

        public void VariableParameterMaintenance()
        {
            for (int i = FixedInputs; i < Params.Input.Count; i++)
            {
                int number = i - FixedInputs + 1;
                var input = Params.Input[i];
                input.Name = "Input";
                input.Description = SourceDescription;
                input.Access = GH_ParamAccess.list;
                input.Optional = true;
                input.MutableNickName = false;
                if (string.IsNullOrEmpty(input.NickName) || input.SourceCount == 0)
                    input.NickName = $"In {number}";

                int outputIndex = OutputFor(i);
                while (outputIndex >= Params.Output.Count)
                    Params.RegisterOutputParam(NewValueOutput());
                var output = Params.Output[outputIndex];
                output.Name = "Value";
                output.Description = ValueDescription;
                output.Access = GH_ParamAccess.item;
                output.MutableNickName = false;
                if (string.IsNullOrEmpty(output.NickName) || input.SourceCount == 0)
                    output.NickName = $"Val {number}";
            }

            while (Params.Output.Count > OutputFor(Params.Input.Count))
                Params.UnregisterOutputParameter(Params.Output[Params.Output.Count - 1]);
        }

        /// <summary>Keeps one free input at the end, so there is always somewhere to wire the next slider.</summary>
        private void OnSourcesChanged(object sender, GH_ParamServerEventArgs e)
        {
            if (e.ParameterSide != GH_ParameterSide.Input || e.ParameterIndex < FixedInputs) return;
            if (Params.Input[Params.Input.Count - 1].SourceCount == 0) return;

            Params.RegisterOutputParam(NewValueOutput(), Params.Output.Count);
            Params.RegisterInputParam(NewSourceInput(), Params.Input.Count);
            VariableParameterMaintenance();
            Params.OnParametersChanged();
        }

        #endregion
    }

    /// <summary>The Fly button under Explorer Inputs.</summary>
    internal sealed class ExplorerInputsAttributes : GH_ComponentAttributes
    {
        private const int ButtonHeight = 22;
        private RectangleF _button;

        public ExplorerInputsAttributes(ExplorerInputs owner) : base(owner) { }

        private ExplorerInputs Inputs => (ExplorerInputs)Owner;

        protected override void Layout()
        {
            base.Layout();
            var bounds = GH_Convert.ToRectangle(Bounds);
            bounds.Height += ButtonHeight;
            var button = bounds;
            button.Y = bounds.Bottom - ButtonHeight;
            button.Height = ButtonHeight;
            button.Inflate(-2, -2);
            Bounds = bounds;
            _button = button;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            var palette = Inputs.IsFlying ? GH_Palette.Warning : GH_Palette.Black;
            using (var capsule = GH_Capsule.CreateTextCapsule(_button, _button, palette, Inputs.ButtonText, 2, 0))
                capsule.Render(graphics, Selected, Owner.Locked, false);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && _button.Contains(e.CanvasLocation) && !Owner.Locked)
            {
                Inputs.ToggleFlight(false);
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }
    }

    /// <summary>
    /// One run of a study. Each turn of RhinoApp.Idle it records the design just solved, moves the
    /// inputs to the next one and solves it; between turns Rhino redraws and answers the user.
    /// </summary>
    internal sealed class ExplorerFlight
    {
        private static readonly HashSet<GH_Document> Flying = new HashSet<GH_Document>();

        /// <summary>Whether a study is running in this document; Explorer Record then leaves recording to it.</summary>
        public static bool IsFlying(GH_Document document) => document != null && Flying.Contains(document);

        private readonly ExplorerInputs _owner;
        private readonly GH_Document _document;
        private readonly IEnumerator<int[]> _plan;
        private readonly Stopwatch _clock = new Stopwatch();
        private bool _stop;
        private bool _ended;

        public long Count { get; }
        public long Solved { get; private set; }
        public long Skipped { get; private set; }
        public long Done => Solved + Skipped;

        public ExplorerFlight(ExplorerInputs owner, GH_Document document, IEnumerable<int[]> plan, long count)
        {
            _owner = owner;
            _document = document;
            _plan = plan.GetEnumerator();
            Count = count;
        }

        public string Progress
        {
            get
            {
                string progress = $"Flying {ExplorerInputs.Count(Done)} of {ExplorerInputs.Count(Count)}";
                if (Solved < 2) return progress;
                double perDesign = _clock.Elapsed.TotalSeconds / Solved;
                return progress + $"\n{Duration(TimeSpan.FromSeconds(perDesign * (Count - Done)))} to go";
            }
        }

        public void Start()
        {
            Flying.Add(_document);
            _clock.Start();
            RhinoApp.Idle += OnIdle;
            RhinoApp.EscapeKeyPressed += OnEscape;
            // Whatever was noted in the solution that started the flight is saved first, while the
            // viewport still shows it.
            Turn(Advance);
        }

        /// <summary>Stops after the design being solved has been recorded.</summary>
        public void Stop() => _stop = true;

        /// <summary>Stops now, recording nothing more: the document is going away.</summary>
        public void Abort() => End(null);

        private void OnEscape(object sender, EventArgs e) => _stop = true;

        private void OnIdle(object sender, EventArgs e)
        {
            if (_ended || _document.SolutionState == GH_ProcessStep.Process) return;
            Turn(() =>
            {
                // Esc in the middle of a solution aborts it, half solved. What it noted is not the
                // design, so it is dropped, and Esc stops the flight as it would anywhere else.
                if (_document.AbortRequested)
                {
                    ExplorerRecord.DropAll(_document);
                    Solved = Math.Max(0, Solved - 1);
                    End("Stopped");
                    return;
                }
                Advance();
            });
        }

        /// <summary>Saves the design just solved, then moves on to the next, unless something says stop.</summary>
        private void Advance()
        {
            ExplorerRecord.CommitAll(_document);
            string failure = ExplorerRecord.Failure(_document);
            if (failure != null)
            {
                // Flying on would solve designs that are not saved.
                End("Stopped, a design could not be saved");
                Rhino.UI.Dialogs.ShowMessage($"The study stopped, because a design could not be saved.\n\n{failure}",
                    "Explorer Inputs", Rhino.UI.ShowMessageButton.OK, Rhino.UI.ShowMessageIcon.Warning);
            }
            else if (_stop || GH_Document.IsEscapeKeyDown())
                End("Stopped");
            else if (!GH_Document.EnableSolutions || !_document.Enabled)
                End("Stopped, the solver was disabled");
            else
                Next();
        }

        private void Turn(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                End("Stopped by an error");
                Rhino.UI.Dialogs.ShowMessage($"The study stopped: {ex.Message}", "Explorer Inputs",
                    Rhino.UI.ShowMessageButton.OK, Rhino.UI.ShowMessageIcon.Error);
            }
        }

        /// <summary>Moves to the next design not recorded yet and solves it, or ends the flight.</summary>
        private void Next()
        {
            while (_plan.MoveNext())
            {
                _owner.Apply(_plan.Current);
                if (ExplorerRecord.AllRecorded(_document, _owner.CurrentValues()))
                {
                    Skipped++;
                    continue;
                }
                Solved++;
                _document.NewSolution(false);
                return;
            }
            End("Done");
        }

        private void End(string how)
        {
            if (_ended) return;
            _ended = true;
            _clock.Stop();
            RhinoApp.Idle -= OnIdle;
            RhinoApp.EscapeKeyPressed -= OnEscape;
            Flying.Remove(_document);

            string summary = null;
            if (how != null)
            {
                summary = $"{how}: {ExplorerInputs.Count(Solved)} solved";
                if (Skipped > 0) summary += $", {ExplorerInputs.Count(Skipped)} already in";
                summary += $", {Duration(_clock.Elapsed)}";
            }
            _owner.FlightEnded(this, summary);
        }

        private static string Duration(TimeSpan time)
        {
            if (time.TotalHours >= 1) return $"{(int)time.TotalHours} h {time.Minutes:00} min";
            if (time.TotalMinutes >= 1) return $"{(int)time.TotalMinutes} min {time.Seconds:00} s";
            return $"{Math.Max(1, (int)Math.Round(time.TotalSeconds))} s";
        }
    }
}
