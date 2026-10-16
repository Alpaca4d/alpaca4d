using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;
using Rhino;

using Alpaca4d.Optimisation;

namespace Alpaca4d.Gh
{
    /// <summary>One design the optimisation evaluated.</summary>
    internal sealed class OptimisationEvaluation
    {
        /// <summary>1 for the first evaluation the algorithm asked for, and so on.</summary>
        public int Number;

        /// <summary>The variables as the sliders and gene pools took them.</summary>
        public double[] Values;

        public double Objective;

        /// <summary>One value per constraint, each met when it is &lt;= 0.</summary>
        public double[] Constraints;

        public bool Feasible;

        /// <summary>The same design as an earlier one, so its values were reused rather than solved again.</summary>
        public bool Repeated;
    }

    /// <summary>
    /// One run of Non-Linear Optimisation.
    ///
    /// The algorithm runs on a thread of its own and asks for designs one at a time. Each is set
    /// and solved on Rhino's UI thread, where Grasshopper must be used, while the algorithm waits;
    /// between designs the UI thread is free, so Rhino stays responsive, the window redraws, and
    /// Stop and Esc are answered. Galapagos runs its solver the same way, and so this works the
    /// same on Windows and on Mac.
    ///
    /// Most designs are solved silently. Every half second one is solved with the usual redraw,
    /// so the canvas and the viewports follow the run without a redraw slowing every design down.
    ///
    /// Sliders round to their decimals, so the algorithm can ask for two designs that come out
    /// the same; the second is not solved again. When the run ends, the variables are put back
    /// on the best design found, and one Undo puts them back where they were before it started.
    /// </summary>
    internal sealed class OptimisationRun
    {
        private static readonly HashSet<GH_Document> Running = new HashSet<GH_Document>();

        /// <summary>Whether an optimisation is running in this document.</summary>
        public static bool IsRunning(GH_Document document) => document != null && Running.Contains(document);

        private readonly NonLinearOptimisation _owner;
        private readonly GH_Document _document;
        private readonly List<OptimisationVariables> _variables;
        private readonly IGH_Param _objective;
        private readonly List<IGH_Param> _constraints;
        private readonly OptimisationSettings _settings;
        private readonly Dictionary<string, OptimisationEvaluation> _solved = new Dictionary<string, OptimisationEvaluation>();
        private readonly Stopwatch _clock = new Stopwatch();
        private Optimiser _optimiser;
        private DateTime _lastShown = DateTime.MinValue;
        private volatile bool _stop;
        private bool _abandoned;

        // Read on the algorithm's thread, written inside Invoke, which waits for it.
        private double[] _lastRequest;
        private OptimisationEvaluation _last;

        private OptimisationRun(NonLinearOptimisation owner, GH_Document document, List<OptimisationVariables> variables,
            IGH_Param objective, List<IGH_Param> constraints, OptimisationSettings settings)
        {
            _owner = owner;
            _document = document;
            _variables = variables;
            _objective = objective;
            _constraints = constraints;
            _settings = settings;
            Dimension = variables.Sum(v => v.Count);
            Names = variables.SelectMany(v => Enumerable.Range(0, v.Count).Select(v.Name)).ToList();
            Start = variables.SelectMany(v => Enumerable.Range(0, v.Count).Select(v.Value)).ToArray();
        }

        public AlgorithmInfo Algorithm => AlgorithmInfo.Of(_settings.Algorithm);
        public bool Maximise => _settings.Maximise;
        public int MaxEvaluations => _settings.MaxEvaluations;

        /// <summary>How many variables.</summary>
        public int Dimension { get; }

        /// <summary>How many constraint values each design gives.</summary>
        public int ConstraintCount { get; private set; }

        public IReadOnlyList<string> Names { get; }

        /// <summary>The variables when the run started.</summary>
        public double[] Start { get; }

        /// <summary>Every design evaluated, in order. Only touched on the UI thread.</summary>
        public List<OptimisationEvaluation> History { get; } = new List<OptimisationEvaluation>();

        /// <summary>The best design so far: feasible before infeasible, then by objective.</summary>
        public OptimisationEvaluation Best { get; private set; }

        /// <summary>How many designs were solved, the repeated ones left out.</summary>
        public int Solutions { get; private set; }

        public bool IsActive { get; private set; }
        public bool StoppedByUser { get; private set; }
        public OptimisationResult? Result { get; private set; }

        /// <summary>What went wrong, when something did.</summary>
        public string Failure { get; private set; }

        public TimeSpan Elapsed => _clock.Elapsed;

        /// <summary>After each design, on the UI thread.</summary>
        public event EventHandler Progress;

        /// <summary>When the run is over and the best design is back, on the UI thread.</summary>
        public event EventHandler Finished;

        #region Before the run

        /// <summary>
        /// A run of what <paramref name="owner"/> is linked to, or null with what stops it in
        /// <paramref name="problem"/>. Things worth knowing that do not stop it go in
        /// <paramref name="warnings"/>. Reads the design as it is, so call it on the UI thread.
        /// </summary>
        public static OptimisationRun Prepare(NonLinearOptimisation owner, List<string> warnings, out string problem)
        {
            problem = null;
            var document = owner.OnPingDocument();
            if (document == null)
            {
                problem = "The optimiser is not in a document.";
                return null;
            }
            // The toolbar's Disable Solver is the static flag; Enabled is only off for a document
            // the canvas is not showing. Either way NewSolution would do nothing.
            if (!GH_Document.EnableSolutions || !document.Enabled)
            {
                problem = "The Grasshopper solver is disabled. Enable it to optimise.";
                return null;
            }
            if (IsRunning(document))
            {
                problem = "An optimisation is already running in this document. Stop it first.";
                return null;
            }
            if (ExplorerFlight.IsFlying(document))
            {
                problem = "Explorer Inputs is flying a study in this document. Stop it first.";
                return null;
            }

            var variables = new List<OptimisationVariables>();
            foreach (var id in owner.Variables)
            {
                var obj = document.FindObject(id, true);
                if (obj == null)
                {
                    problem = "A variable is no longer in the document. Right-click Variables ▸ Remove Variable to unlink it, or link the variables again.";
                    return null;
                }
                var variable = OptimisationVariables.From(obj);
                if (variable == null)
                {
                    problem = $"{obj.NickName} is not a number slider or a gene pool, so it cannot be a variable.";
                    return null;
                }
                if (variable.Count > 0) variables.Add(variable);
            }
            if (variables.Count == 0)
            {
                problem = "There is nothing to optimise. Drag from the Variables grip, on the left of the optimiser, to the number sliders the objective depends on.";
                return null;
            }

            if (owner.Objective == Guid.Empty)
            {
                problem = "There is no objective. Drag from the Objective grip, under the optimiser, to the number to minimise or maximise.";
                return null;
            }
            var objective = document.FindParameter(owner.Objective);
            if (objective == null)
            {
                problem = "The objective is no longer in the document. Drag from the Objective grip to the number to minimise or maximise.";
                return null;
            }

            var constraints = new List<IGH_Param>();
            foreach (var id in owner.Constraints)
            {
                var constraint = document.FindParameter(id);
                if (constraint == null)
                {
                    problem = "A constraint is no longer in the document. Right-click Constraints ▸ Remove Constraint to unlink it, or link the constraints again.";
                    return null;
                }
                constraints.Add(constraint);
            }

            var settings = owner.Settings.Copy();
            var info = AlgorithmInfo.Of(settings.Algorithm);
            if (constraints.Count > 0 && !info.TakesConstraints)
            {
                problem = $"{info.Name} cannot take constraints. Choose COBYLA or ISRES, which can, or unlink the constraints.";
                return null;
            }

            var run = new OptimisationRun(owner, document, variables, objective, constraints, settings);

            // The design as it is now: that the objective can be read, and how many constraint
            // values there are, which must stay the same for every design.
            document.NewSolution(false);
            if (!run.ReadObjective(out _, out problem)) return null;
            if (!run.ReadConstraints(out double[] now, out problem)) return null;
            run.ConstraintCount = now.Length;
            if (constraints.Count > 0 && now.Length == 0)
                warnings.Add("The constraints have no values now, so the optimisation runs without them.");

            if (!info.IsGlobal)
            {
                // A local algorithm feels its way with small steps, which a coarse slider rounds away.
                var coarse = variables
                    .SelectMany(v => Enumerable.Range(0, v.Count).Select(i => new { Name = v.Name(i), Steps = (v.Upper(i) - v.Lower(i)) / v.Step(i) }))
                    .Where(v => v.Steps > 0 && v.Steps < 1000)
                    .Select(v => v.Name)
                    .Distinct()
                    .ToList();
                if (coarse.Count > 0)
                    warnings.Add($"{List(coarse)} {(coarse.Count == 1 ? "has" : "have")} fewer than 1000 steps between {(coarse.Count == 1 ? "its" : "their")} bounds. " +
                                 $"{info.Name} feels its way with small steps, which a coarse slider rounds away: give {(coarse.Count == 1 ? "it" : "them")} more decimals, or choose a global algorithm.");
            }
            return run;
        }

        private static string List(IList<string> names)
        {
            var shown = names.Take(4).ToList();
            string list = shown.Count == 1 ? shown[0] : string.Join(", ", shown.Take(shown.Count - 1)) + " and " + shown.Last();
            return names.Count > 4 ? string.Join(", ", shown) + $" and {names.Count - 4} more" : list;
        }

        #endregion

        #region Running

        /// <summary>Starts the run. On the UI thread.</summary>
        public void Begin()
        {
            Running.Add(_document);
            IsActive = true;

            // One Undo puts the variables back where they were before the run.
            var record = new GH_UndoRecord("Non-Linear Optimisation");
            foreach (var variable in _variables)
                record.AddAction(new GH_GenericObjectAction(variable.Object));
            _document.UndoServer.PushUndoRecord(record);

            _optimiser = _settings.CreateOptimiser(Dimension);
            var x = new double[Dimension];
            int k = 0;
            foreach (var variable in _variables)
                for (int i = 0; i < variable.Count; i++, k++)
                {
                    _optimiser.LowerBounds[k] = variable.Lower(i);
                    _optimiser.UpperBounds[k] = variable.Upper(i);
                    x[k] = Math.Min(Math.Max(Start[k], variable.Lower(i)), variable.Upper(i));
                }
            if (ConstraintCount > 0)
                _optimiser.AddInequalityConstraints(Constraints, new double[ConstraintCount]);

            RhinoApp.EscapeKeyPressed += OnEscape;
            _clock.Start();
            Task.Run(() => Optimise(x));
        }

        /// <summary>Stops after the design being solved, and goes back to the best. Any thread.</summary>
        public void Stop()
        {
            if (!IsActive) return;
            StoppedByUser = true;
            _stop = true;
            _optimiser?.ForceStop();
        }

        /// <summary>Stops without touching the document again: it is going away.</summary>
        public void Abandon()
        {
            _abandoned = true;
            Stop();
        }

        private void OnEscape(object sender, EventArgs e) => Stop();

        // On the algorithm's thread.
        private void Optimise(double[] x)
        {
            OptimisationResult result;
            string error = null;
            try
            {
                result = _optimiser.Optimise(Objective, x, out _);
            }
            catch (Exception ex)
            {
                result = OptimisationResult.Failure;
                error = $"The optimisation stopped on an error: {ex.Message}";
            }
            Eto.Forms.Application.Instance.AsyncInvoke(() => End(result, error));
        }

        // On the algorithm's thread.
        private double Objective(double[] x)
        {
            var evaluation = Evaluate(x);
            if (evaluation == null)
            {
                _optimiser.ForceStop();
                return double.NaN;
            }
            return evaluation.Objective;
        }

        // On the algorithm's thread. COBYLA and ISRES ask for the constraints right after the
        // objective at the same point, which is then not solved twice.
        private void Constraints(double[] x, double[] result)
        {
            var evaluation = _last != null && _lastRequest != null && _lastRequest.SequenceEqual(x) ? _last : Evaluate(x);
            if (evaluation == null)
            {
                _optimiser.ForceStop();
                for (int i = 0; i < result.Length; i++) result[i] = double.NaN;
                return;
            }
            Array.Copy(evaluation.Constraints, result, result.Length);
        }

        // On the algorithm's thread: hands the design to the UI thread and waits for it.
        private OptimisationEvaluation Evaluate(double[] x)
        {
            if (_stop) return null;
            var request = (double[])x.Clone();
            OptimisationEvaluation evaluation = null;
            Eto.Forms.Application.Instance.Invoke(() =>
            {
                try
                {
                    evaluation = EvaluateHere(request);
                }
                catch (Exception ex)
                {
                    Failure = $"The optimisation stopped on an error: {ex.Message}";
                    evaluation = null;
                }
            });
            _lastRequest = request;
            _last = evaluation;
            return evaluation;
        }

        // On the UI thread.
        private OptimisationEvaluation EvaluateHere(double[] x)
        {
            if (_stop || _abandoned) return null;
            if (GH_Document.IsEscapeKeyDown())
            {
                Stop();
                return null;
            }
            if (!GH_Document.EnableSolutions || !_document.Enabled)
            {
                Failure = "The optimisation stopped: the Grasshopper solver was disabled.";
                return null;
            }
            foreach (var variable in _variables)
                if (_document.FindObject(variable.Object.InstanceGuid, true) == null)
                {
                    Failure = $"The optimisation stopped: the {variable.Kind} {variable.Object.NickName} was deleted.";
                    return null;
                }

            var values = new double[Dimension];
            int k = 0;
            foreach (var variable in _variables)
            {
                for (int i = 0; i < variable.Count; i++, k++)
                    values[k] = variable.Set(i, x[k]);
                variable.Expire();
            }

            int number = History.Count + 1;
            string key = Key(values);
            if (_solved.TryGetValue(key, out var earlier))
            {
                var repeat = new OptimisationEvaluation
                {
                    Number = number,
                    Values = values,
                    Objective = earlier.Objective,
                    Constraints = earlier.Constraints,
                    Feasible = earlier.Feasible,
                    Repeated = true,
                };
                Record(repeat);
                return repeat;
            }

            bool show = DateTime.UtcNow - _lastShown > TimeSpan.FromSeconds(0.5);
            if (show) _lastShown = DateTime.UtcNow;
            _document.NewSolution(false, show ? GH_SolutionMode.Default : GH_SolutionMode.Silent);
            Solutions++;

            // Esc in the middle of a solution aborts it, half solved: what it gave is not the design.
            if (_document.AbortRequested)
            {
                Stop();
                return null;
            }

            if (!ReadObjective(out double objective, out string failure) || !ReadConstraints(out double[] constraints, out failure))
            {
                Failure = $"{failure}\n\nThis happened with {Describe(values)}. The best design found before it is kept.";
                return null;
            }

            var evaluation = new OptimisationEvaluation
            {
                Number = number,
                Values = values,
                Objective = objective,
                Constraints = constraints,
                Feasible = constraints.All(c => c <= 0),
            };
            _solved[key] = evaluation;
            Record(evaluation);
            return evaluation;
        }

        private void Record(OptimisationEvaluation evaluation)
        {
            History.Add(evaluation);
            if (IsBetter(evaluation, Best)) Best = evaluation;
            Progress?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Feasible beats infeasible; two infeasible, the less infeasible; two feasible, the better objective.</summary>
        public bool IsBetter(OptimisationEvaluation a, OptimisationEvaluation b)
        {
            if (a == null || double.IsNaN(a.Objective)) return false;
            if (b == null) return true;
            if (a.Feasible != b.Feasible) return a.Feasible;
            if (!a.Feasible) return Violation(a) < Violation(b);
            return Maximise ? a.Objective > b.Objective : a.Objective < b.Objective;
        }

        private static double Violation(OptimisationEvaluation e) => e.Constraints.Where(c => c > 0).Sum();

        // On the UI thread, once the algorithm has returned.
        private void End(OptimisationResult result, string error)
        {
            _clock.Stop();
            RhinoApp.EscapeKeyPressed -= OnEscape;
            Running.Remove(_document);
            IsActive = false;
            Result = result;
            if (Failure == null) Failure = error;

            if (!_abandoned && Best != null)
            {
                try
                {
                    Apply(Best.Values);
                }
                catch (Exception ex)
                {
                    Failure = Failure ?? $"The best design could not be put back: {ex.Message}";
                }
            }

            Finished?.Invoke(this, EventArgs.Empty);
            _owner.RunEnded(this);
        }

        /// <summary>Sets the variables to <paramref name="values"/> and solves. On the UI thread, not while running.</summary>
        public void Apply(double[] values)
        {
            if (IsActive || values == null || values.Length != Dimension) return;
            if (_document.FindObject(_owner.InstanceGuid, true) == null) return;
            int k = 0;
            foreach (var variable in _variables)
            {
                if (_document.FindObject(variable.Object.InstanceGuid, true) == null)
                {
                    k += variable.Count;
                    continue;
                }
                for (int i = 0; i < variable.Count; i++, k++)
                    variable.Set(i, values[k]);
                variable.Expire();
            }
            _document.NewSolution(false);
        }

        #endregion

        #region Reading the definition

        private bool ReadObjective(out double value, out string failure)
        {
            value = double.NaN;
            failure = null;
            var numbers = new List<double>();
            int other = 0;
            foreach (var goo in _objective.VolatileData.AllData(true))
            {
                double d = double.NaN;
                if (goo != null && GH_Convert.ToDouble(goo, out d, GH_Conversion.Both) && !double.IsNaN(d)) numbers.Add(d);
                else other++;
            }
            if (numbers.Count == 1 && other == 0)
            {
                value = numbers[0];
                return true;
            }
            string name = Describe(_objective);
            if (numbers.Count == 0 && other == 0)
                failure = $"The objective, {name}, has no value. Check the components that compute it for errors.";
            else if (numbers.Count == 0)
                failure = $"The objective, {name}, is not a number.";
            else
                failure = $"The objective, {name}, has {numbers.Count + other} values. It must have one: link the sum, the largest or whichever single number is to be minimised or maximised.";
            return false;
        }

        private bool ReadConstraints(out double[] values, out string failure)
        {
            failure = null;
            var all = new List<double>();
            foreach (var param in _constraints)
                foreach (var goo in param.VolatileData.AllData(true))
                {
                    double d = double.NaN;
                    if (goo == null || !GH_Convert.ToDouble(goo, out d, GH_Conversion.Both) || double.IsNaN(d))
                    {
                        values = null;
                        failure = $"The constraint {Describe(param)} has a value that is not a number.";
                        return false;
                    }
                    all.Add(d);
                }
            values = all.ToArray();
            if (History.Count > 0 || _optimiser != null)
            {
                if (values.Length != ConstraintCount)
                {
                    failure = $"The constraints gave {values.Length} values for this design and {ConstraintCount} for the first. Every design must give the same number of them.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>A parameter by its name, and its component's when it belongs to one.</summary>
        internal static string Describe(IGH_Param param)
        {
            var top = param.Attributes?.GetTopLevel?.DocObject;
            if (top != null && !ReferenceEquals(top, param)) return $"{param.NickName} of {top.NickName}";
            return param.NickName;
        }

        private string Describe(double[] values)
        {
            var parts = values.Take(6).Select((v, i) => $"{Names[i]} = {Format(v)}").ToList();
            if (values.Length > 6) parts.Add($"and {values.Length - 6} more");
            return string.Join(", ", parts);
        }

        internal static string Format(double value)
        {
            if (double.IsNaN(value)) return "-";
            if (double.IsInfinity(value)) return value > 0 ? "∞" : "-∞";
            return value.ToString("G6", CultureInfo.InvariantCulture);
        }

        private static string Key(double[] values) => string.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

        /// <summary>The history as comma-separated values, one design a line, for a spreadsheet.</summary>
        public string ToCsv()
        {
            var lines = new List<string>();
            var header = new List<string> { "evaluation" };
            header.AddRange(Names.Select(Quote));
            header.Add("objective");
            header.AddRange(Enumerable.Range(1, ConstraintCount).Select(i => $"constraint {i}"));
            if (ConstraintCount > 0) header.Add("feasible");
            header.Add("repeated");
            lines.Add(string.Join(",", header));
            foreach (var e in History)
            {
                var cells = new List<string> { e.Number.ToString(CultureInfo.InvariantCulture) };
                cells.AddRange(e.Values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
                cells.Add(e.Objective.ToString("R", CultureInfo.InvariantCulture));
                cells.AddRange(e.Constraints.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
                if (ConstraintCount > 0) cells.Add(e.Feasible ? "true" : "false");
                cells.Add(e.Repeated ? "true" : "false");
                lines.Add(string.Join(",", cells));
            }
            return string.Join("\n", lines) + "\n";
        }

        private static string Quote(string cell) => cell.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? cell : "\"" + cell.Replace("\"", "\"\"") + "\"";

        #endregion
    }
}
