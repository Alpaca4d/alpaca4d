using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Eto.Drawing;
using Eto.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

using Alpaca4d.Optimisation;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The window of Non-Linear Optimisation: the algorithm and when to stop, Start and Stop,
    /// and every design as it is solved, plotted against the best so far.
    ///
    /// Eto rather than Windows Forms, as Galapagos's editor is in Rhino 8, so that it is the same
    /// window on Mac. It stays open beside the canvas while the run goes on: the run hands each
    /// design to the UI thread and waits, so the window redraws between designs.
    /// </summary>
    internal sealed class OptimisationWindow : Form
    {
        private static readonly Dictionary<NonLinearOptimisation, OptimisationWindow> Open = new Dictionary<NonLinearOptimisation, OptimisationWindow>();

        // The colours of the links on the canvas, from Alpaca4d's palette: the best so far in the
        // objective's purple, a design that breaks a constraint in the constraints' orange.
        private static readonly Color BestColour = Eto(NonLinearOptimisationAttributes.ObjectiveColour);
        private static readonly Color InfeasibleColour = Eto(NonLinearOptimisationAttributes.ConstraintColour);
        private static readonly Color ProblemColour = Eto(Alpaca4d.Colors.AlpacaRed);

        private static Color Eto(System.Drawing.Color colour) => Color.FromArgb(colour.R, colour.G, colour.B);

        private readonly NonLinearOptimisation _owner;
        private OptimisationRun _run;
        private bool _loading;

        // Text that wraps is given a width. Measured for a layout, an Eto label is as wide as its
        // text on one line, whatever its wrap mode, unless it has a width of its own; without one
        // the description alone made the window as wide as the screen on Mac.
        private const int WindowWidth = 520;
        private const int TextWidth = WindowWidth - 2 * 14;      // inside the padding
        private const int FieldWidth = TextWidth - 90;           // the second column of the fields

        private readonly DropDown _algorithm = new DropDown();
        private readonly Label _description = new Label { Wrap = WrapMode.Word, TextAlignment = TextAlignment.Left, Width = FieldWidth };
        private readonly RadioButtonList _goal = new RadioButtonList { Orientation = Orientation.Horizontal, Spacing = new Size(12, 0) };
        private readonly NumericStepper _evaluations = new NumericStepper { MinValue = 0, MaxValue = 10000000, DecimalPlaces = 0, Increment = 100, Width = 90 };
        private readonly NumericStepper _time = new NumericStepper { MinValue = 0, MaxValue = 10000000, DecimalPlaces = 0, Increment = 60, Width = 90 };
        private readonly CheckBox _useStopValue = new CheckBox { Text = "when the objective reaches" };
        private readonly TextBox _stopValue = new TextBox { Width = 90 };
        private readonly TextBox _variableTolerance = new TextBox { Width = 70 };
        private readonly TextBox _objectiveTolerance = new TextBox { Width = 70 };
        private readonly NumericStepper _seed = new NumericStepper { MinValue = 0, MaxValue = int.MaxValue, DecimalPlaces = 0, Width = 90 };
        private readonly Label _links = new Label { Wrap = WrapMode.Word, TextAlignment = TextAlignment.Left, Width = TextWidth };
        private readonly Label _warnings = new Label { Wrap = WrapMode.Word, TextAlignment = TextAlignment.Left, Width = TextWidth, TextColor = InfeasibleColour, Visible = false };
        private readonly Button _start = new Button { Text = "Start" };
        private readonly Button _stop = new Button { Text = "Stop" };
        private readonly Button _reinstate = new Button { Text = "Reinstate best" };
        private readonly Button _copy = new Button { Text = "Copy history" };
        private readonly Drawable _chart = new Drawable { Height = 180 };
        private readonly Label _progress = new Label { Wrap = WrapMode.None, TextAlignment = TextAlignment.Left };
        private readonly Label _result = new Label { Wrap = WrapMode.Word, TextAlignment = TextAlignment.Left, Width = TextWidth, Visible = false };

        private OptimisationWindow(NonLinearOptimisation owner)
        {
            _owner = owner;
            Title = "Alpaca4d - Non-Linear Optimisation";
            Minimizable = false;
            Maximizable = false;
            Resizable = true;
            Topmost = true;
            Padding = new Padding(14);
            ClientSize = new Size(WindowWidth, 640);

            _goal.Items.Add("Minimise");
            _goal.Items.Add("Maximise");
            foreach (var info in AlgorithmInfo.All) _algorithm.Items.Add(info.Label);

            // Two columns for the settings: names, and what is set. Everything after them spans the
            // window in a column of its own. (DynamicLayout's AddSeparateRow nests its row inside a
            // cell of the table around it, which put all of it in the narrow first column.)
            var fields = new TableLayout { Spacing = new Size(10, 8) };
            fields.Rows.Add(Field("Algorithm", _algorithm));
            fields.Rows.Add(Field(null, _description));
            fields.Rows.Add(Field("Goal", _goal));
            fields.Rows.Add(Field("Stop after", Horizontal(_evaluations, Text("evaluations (0: no limit)"))));
            fields.Rows.Add(Field(null, Horizontal(_time, Text("seconds (0: no limit)"))));
            fields.Rows.Add(Field(null, Horizontal(_useStopValue, _stopValue)));
            fields.Rows.Add(Field("Tolerances", Horizontal(Text("variables"), _variableTolerance, Text("objective"), _objectiveTolerance, Text("relative"))));
            fields.Rows.Add(Field("Seed", Horizontal(_seed, Text("for CRS2-LM and ISRES"))));

            var buttons = new TableLayout { Spacing = new Size(6, 0) };
            buttons.Rows.Add(new TableRow(new TableCell(_start), new TableCell(_stop), new TableCell(null, true), new TableCell(_reinstate), new TableCell(_copy)));

            var content = new TableLayout { Spacing = new Size(0, 10) };
            content.Rows.Add(new TableRow(fields));
            content.Rows.Add(new TableRow(_links));
            content.Rows.Add(new TableRow(_warnings));
            content.Rows.Add(new TableRow(buttons));
            content.Rows.Add(new TableRow(_chart) { ScaleHeight = true });
            content.Rows.Add(new TableRow(_progress));
            content.Rows.Add(new TableRow(_result));
            Content = content;

            LoadSettings();
            RefreshLinks();

            _algorithm.SelectedIndexChanged += (s, e) => SaveSettings();
            _goal.SelectedIndexChanged += (s, e) => SaveSettings();
            _evaluations.ValueChanged += (s, e) => SaveSettings();
            _time.ValueChanged += (s, e) => SaveSettings();
            _useStopValue.CheckedChanged += (s, e) => SaveSettings();
            _stopValue.TextChanged += (s, e) => SaveSettings();
            _variableTolerance.TextChanged += (s, e) => SaveSettings();
            _objectiveTolerance.TextChanged += (s, e) => SaveSettings();
            _seed.ValueChanged += (s, e) => SaveSettings();

            _start.Click += (s, e) => StartRun();
            _stop.Click += (s, e) => _run?.Stop();
            _reinstate.Click += (s, e) => { if (_run?.Best != null) _run.Apply(_run.Best.Values); };
            _copy.Click += (s, e) => { if (_run != null) new Clipboard().Text = _run.ToCsv(); };
            _chart.Paint += PaintChart;
            KeyDown += (s, e) =>
            {
                if (e.Key == Keys.Escape && _run != null && _run.IsActive)
                {
                    _run.Stop();
                    e.Handled = true;
                }
            };

            Closed += (s, e) =>
            {
                // A run nobody can watch is stopped: the best design so far is kept.
                _run?.Stop();
                DetachRun();
                Open.Remove(_owner);
            };

            if (_owner.Run != null) AttachRun(_owner.Run);
            ShowState();
        }

        public static void ShowFor(NonLinearOptimisation owner)
        {
            if (Open.TryGetValue(owner, out var window))
            {
                window.BringToFront();
                return;
            }
            window = new OptimisationWindow(owner);
            Open[owner] = window;
            window.PlaceNearMouse();
            window.Show();
        }

        public static void CloseFor(NonLinearOptimisation owner)
        {
            if (Open.TryGetValue(owner, out var window)) window.Close();
        }

        /// <summary>After a link changes on the canvas.</summary>
        public static void RefreshFor(NonLinearOptimisation owner)
        {
            if (Open.TryGetValue(owner, out var window)) window.RefreshLinks();
        }

        /// <summary>
        /// Opens beside the mouse, where the optimiser was double-clicked, and inside the screen.
        /// Grasshopper's own CenterFormOnCursor is built against Rhino's Eto rather than the one
        /// Alpaca4d compiles against, so it is not used.
        /// </summary>
        private void PlaceNearMouse()
        {
            try
            {
                var mouse = Mouse.Position;
                var area = Screen.FromPoint(mouse).WorkingArea;
                float x = Math.Min(Math.Max(area.Left, mouse.X - ClientSize.Width / 2f), area.Right - ClientSize.Width - 40);
                float y = Math.Min(Math.Max(area.Top, mouse.Y - 120), area.Bottom - ClientSize.Height - 60);
                Location = new Point((int)x, (int)y);
            }
            catch (Exception)
            {
                // wherever the platform puts a new window will do
            }
        }

        /// <summary>A row of the settings: a name, right-aligned and never wrapped, and what it names, taking the rest of the width.</summary>
        private static TableRow Field(string name, Control control)
        {
            var label = name == null ? null : new Label
            {
                Text = name,
                Wrap = WrapMode.None,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return new TableRow(new TableCell(label), new TableCell(control, true));
        }

        /// <summary>A few words beside a control. Eto labels wrap by default, which splits short ones mid-word on Mac.</summary>
        private static Label Text(string text) => new Label { Text = text, Wrap = WrapMode.None, VerticalAlignment = VerticalAlignment.Center };

        /// <summary>Shows a line of text, or hides it when there is none, so an empty one takes no room.</summary>
        private static void Say(Label label, string text)
        {
            label.Text = text ?? "";
            label.Visible = !string.IsNullOrEmpty(text);
        }

        private static StackLayout Horizontal(params Control[] controls)
        {
            var stack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var control in controls) stack.Items.Add(control);
            return stack;
        }

        #region Settings

        private void LoadSettings()
        {
            _loading = true;
            var s = _owner.Settings;
            _algorithm.SelectedIndex = Math.Max(0, AlgorithmInfo.All.ToList().FindIndex(a => a.Algorithm == s.Algorithm));
            _goal.SelectedIndex = s.Maximise ? 1 : 0;
            _evaluations.Value = s.MaxEvaluations;
            _time.Value = s.MaxTime;
            _useStopValue.Checked = s.UseStopValue;
            _stopValue.Text = s.UseStopValue ? Number(s.StopValue) : "";
            _variableTolerance.Text = Number(s.VariableTolerance);
            _objectiveTolerance.Text = Number(s.ObjectiveTolerance);
            _seed.Value = s.Seed;
            _loading = false;
            ShowAlgorithm();
        }

        private void SaveSettings()
        {
            if (_loading) return;
            var s = _owner.Settings;
            if (_algorithm.SelectedIndex >= 0) s.Algorithm = AlgorithmInfo.All[_algorithm.SelectedIndex].Algorithm;
            s.Maximise = _goal.SelectedIndex == 1;
            s.MaxEvaluations = (int)_evaluations.Value;
            s.MaxTime = _time.Value;
            // Ticked without a number in the box is no stop value, rather than a stale one.
            bool hasStop = TryNumber(_stopValue, out double stop);
            if (hasStop) s.StopValue = stop;
            s.UseStopValue = _useStopValue.Checked == true && hasStop;
            if (TryNumber(_variableTolerance, out double xtol) && xtol >= 0) s.VariableTolerance = xtol;
            if (TryNumber(_objectiveTolerance, out double ftol) && ftol >= 0) s.ObjectiveTolerance = ftol;
            s.Seed = (uint)Math.Max(0, _seed.Value);

            ShowAlgorithm();
            _owner.OnPingDocument()?.Modified();
            _owner.Attributes?.ExpireLayout();
            Grasshopper.Instances.InvalidateCanvas();
        }

        private void ShowAlgorithm()
        {
            var info = AlgorithmInfo.Of(_owner.Settings.Algorithm);
            _description.Text = info.Description + (info.TakesConstraints ? " Takes constraints." : "");
            _seed.Enabled = info.IsStochastic && !Running;
            _stopValue.Enabled = _useStopValue.Checked == true && !Running;
        }

        private static string Number(double value) => value.ToString("G", CultureInfo.InvariantCulture);

        /// <summary>A number as typed, with a point or the machine's own decimal separator; the box turns red when it is not one.</summary>
        private static bool TryNumber(TextBox box, out double value)
        {
            string text = box.Text?.Trim() ?? "";
            bool ok = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
            ok = ok && !double.IsNaN(value) && !double.IsInfinity(value);
            box.TextColor = ok || text.Length == 0 ? SystemColors.ControlText : ProblemColour;
            return ok;
        }

        #endregion

        #region Links and state

        private bool Running => _run != null && _run.IsActive;

        private void RefreshLinks()
        {
            var document = _owner.OnPingDocument();
            if (document == null) return;

            int sliders = 0, pools = 0, genes = 0, missing = 0;
            foreach (var id in _owner.Variables)
            {
                var obj = document.FindObject(id, true);
                if (obj == null) missing++;
                else if (obj is GH_NumberSlider) sliders++;
                else
                {
                    pools++;
                    genes += OptimisationVariables.From(obj)?.Count ?? 0;
                }
            }
            var parts = new List<string>();
            if (sliders > 0) parts.Add(sliders == 1 ? "1 slider" : $"{sliders} sliders");
            if (pools > 0) parts.Add(pools == 1 ? $"a gene pool of {genes}" : $"{pools} gene pools of {genes} genes in all");
            string variables = parts.Count == 0 ? "No variables" : string.Join(" and ", parts);
            if (missing > 0) variables += $" ({missing} deleted)";

            var objective = _owner.Objective == Guid.Empty ? null : document.FindParameter(_owner.Objective);
            string objectiveText = objective == null ? "no objective" : $"objective {OptimisationRun.Describe(objective)}";
            int constraints = _owner.Constraints.Count(id => document.FindParameter(id) != null);
            string constraintText = constraints == 0 ? "no constraints" : constraints == 1 ? "1 constraint parameter" : $"{constraints} constraint parameters";

            _links.Text = $"{variables} · {objectiveText} · {constraintText}";
        }

        private void StartRun()
        {
            if (Running) return;
            var warnings = new List<string>();
            var run = _owner.StartRun(warnings, out string problem);
            if (run == null)
            {
                Say(_result, problem);
                _result.TextColor = ProblemColour;
                return;
            }
            Say(_warnings, string.Join("\n\n", warnings));
            AttachRun(run);
            ShowState();
        }

        private void AttachRun(OptimisationRun run)
        {
            DetachRun();
            _run = run;
            _run.Progress += OnProgress;
            _run.Finished += OnFinished;
            _chart.Invalidate();
        }

        private void DetachRun()
        {
            if (_run == null) return;
            _run.Progress -= OnProgress;
            _run.Finished -= OnFinished;
            _run = null;
        }

        private DateTime _lastDrawn = DateTime.MinValue;

        private void OnProgress(object sender, EventArgs e)
        {
            // Every design updates the numbers; the chart, at most ten times a second.
            ShowProgress();
            if (DateTime.UtcNow - _lastDrawn > TimeSpan.FromSeconds(0.1))
            {
                _lastDrawn = DateTime.UtcNow;
                _chart.Invalidate();
            }
        }

        private void OnFinished(object sender, EventArgs e)
        {
            ShowState();
            _chart.Invalidate();
        }

        private void ShowState()
        {
            bool running = Running;
            foreach (Control control in new Control[] { _algorithm, _goal, _evaluations, _time, _useStopValue, _variableTolerance, _objectiveTolerance })
                control.Enabled = !running;
            ShowAlgorithm();
            _start.Enabled = !running;
            _stop.Enabled = running;
            _reinstate.Enabled = !running && _run?.Best != null;
            _copy.Enabled = _run != null && _run.History.Count > 0;
            ShowProgress();

            if (_run == null || running)
            {
                Say(_result, running ? "Running. Stop, or Esc, keeps the best design found so far." : "");
                _result.TextColor = SystemColors.ControlText;
                return;
            }
            string ending = _run.Failure ?? (_run.StoppedByUser ? "The optimisation was stopped." : OptimisationResults.Describe(_run.Result ?? OptimisationResult.Failure));
            Say(_result, _run.Best == null ? ending : ending + " The variables are on the best design found.");
            _result.TextColor = _run.Failure != null ? ProblemColour : SystemColors.ControlText;
        }

        private void ShowProgress()
        {
            if (_run == null)
            {
                _progress.Text = "";
                return;
            }
            string count = _run.MaxEvaluations > 0 ? $"{_run.History.Count} of {_run.MaxEvaluations} evaluations" : $"{_run.History.Count} evaluations";
            int repeated = _run.History.Count - _run.Solutions;
            if (repeated > 0) count += $" ({repeated} repeated, not solved again)";
            string best = _run.Best == null ? "" : $" · best {OptimisationRun.Format(_run.Best.Objective)}{(_run.Best.Feasible ? "" : " (constraints not met)")}";
            _progress.Text = $"{count}{best} · {Duration(_run.Elapsed)}";
        }

        private static string Duration(TimeSpan time)
        {
            if (time.TotalHours >= 1) return $"{(int)time.TotalHours} h {time.Minutes:00} min";
            if (time.TotalMinutes >= 1) return $"{(int)time.TotalMinutes} min {time.Seconds:00} s";
            return $"{time.TotalSeconds:0} s";
        }

        #endregion

        #region Chart

        /// <summary>Every design's objective, in grey (orange when it breaks a constraint), and the best so far as a line.</summary>
        private void PaintChart(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            float w = _chart.Width, h = _chart.Height;
            var text = SystemColors.ControlText;
            var faint = SystemColors.DisabledText;
            var font = SystemFonts.Default(8);
            g.FillRectangle(SystemColors.ControlBackground, 0, 0, w, h);

            var history = _run?.History;
            var points = history?.Where(p => !double.IsNaN(p.Objective) && !double.IsInfinity(p.Objective)).ToList();
            if (points == null || points.Count == 0)
            {
                const string empty = "Every design the optimisation solves is plotted here.";
                var size = g.MeasureString(font, empty);
                g.DrawText(font, faint, (w - size.Width) / 2, (h - size.Height) / 2, empty);
                return;
            }

            const float left = 64, right = 12, top = 10, bottom = 22;
            float pw = Math.Max(1, w - left - right), ph = Math.Max(1, h - top - bottom);
            double min = points.Min(p => p.Objective), max = points.Max(p => p.Objective);
            if (max - min < 1e-12 * Math.Max(1, Math.Abs(max)))
            {
                min -= 1;
                max += 1;
            }
            int count = Math.Max(history.Count, 2);
            Func<int, float> X = n => left + pw * (n - 1) / (count - 1);
            Func<double, float> Y = v => top + ph * (float)((max - v) / (max - min));

            using (var axis = new Pen(faint, 1))
            {
                g.DrawLine(axis, left, top, left, top + ph);
                g.DrawLine(axis, left, top + ph, left + pw, top + ph);
            }
            g.DrawText(font, faint, 4, top - 2, OptimisationRun.Format(max));
            g.DrawText(font, faint, 4, top + ph - 10, OptimisationRun.Format(min));
            g.DrawText(font, faint, left, top + ph + 4, "1");
            string last = history.Count.ToString(CultureInfo.InvariantCulture);
            g.DrawText(font, faint, left + pw - g.MeasureString(font, last).Width, top + ph + 4, last);

            var dot = Color.FromArgb(faint.Rb, faint.Gb, faint.Bb, 150);
            foreach (var p in points)
                g.FillEllipse(p.Feasible ? dot : InfeasibleColour, X(p.Number) - 2, Y(p.Objective) - 2, 4, 4);

            // the best so far, as a step line
            OptimisationEvaluation best = null;
            PointF? previous = null;
            using (var pen = new Pen(BestColour, 2))
            {
                foreach (var p in history)
                {
                    if (!_run.IsBetter(p, best)) continue;
                    best = p;
                    var here = new PointF(X(p.Number), Y(p.Objective));
                    if (previous.HasValue)
                    {
                        g.DrawLine(pen, previous.Value.X, previous.Value.Y, here.X, previous.Value.Y);
                        g.DrawLine(pen, here.X, previous.Value.Y, here.X, here.Y);
                    }
                    previous = here;
                }
                if (previous.HasValue) g.DrawLine(pen, previous.Value.X, previous.Value.Y, X(history.Count), previous.Value.Y);
            }
            if (best != null && !double.IsInfinity(best.Objective))
                g.FillEllipse(BestColour, X(best.Number) - 4, Y(best.Objective) - 4, 8, 8);
        }

        #endregion
    }
}
