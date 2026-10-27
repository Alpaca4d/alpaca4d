using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Rhino;

using Alpaca4d.Optimisation;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// One run of Topology Optimisation, on a thread of its own.
    ///
    /// A run takes minutes, so it does not hold the solution up: the component starts it and goes on,
    /// and the run solves itself again about once a second with the design it has reached, so the
    /// outputs and the preview follow it. Nothing in the run touches Grasshopper or Rhino's geometry -
    /// the cells and the deck are read off the model before it starts - so only the solution it asks
    /// for runs on Rhino's UI thread.
    /// </summary>
    internal sealed class TopologyRun
    {
        private readonly object _gate = new object();
        private readonly GH_Component _owner;
        private readonly Stopwatch _sinceShown = Stopwatch.StartNew();
        private volatile bool _stop;

        private double[] _densities;
        private readonly List<TopologyIteration> _history = new List<TopologyIteration>();

        /// <summary>What the run was started from: a run whose inputs have changed is stopped and started again.</summary>
        public string Key { get; }

        public Alpaca4d.Model Model { get; }
        public IReadOnlyList<SolidCell> Cells { get; }
        public TopologySettings Settings { get; }

        /// <summary>The design after every iteration, frame 0 the one the first run started from.</summary>
        public DensityHistory Frames { get; }

        public bool IsRunning { get; private set; } = true;
        public TopologyResult Result { get; private set; }
        public string Failure { get; private set; }
        public string Log { get; private set; } = "";

        private TopologyRun(GH_Component owner, string key, Alpaca4d.Model model, IReadOnlyList<SolidCell> cells, TopologySettings settings,
                            TopologyResult previous)
        {
            _owner = owner;
            Key = key;
            Model = model;
            Cells = cells;
            Settings = settings;
            _densities = previous?.Densities ?? cells.Select(cell => cell.Passive ? 1.0 : settings.VolumeFraction).ToArray();
            if (previous != null)
                _history.AddRange(previous.History);
            Frames = previous?.Frames?.Copy() ?? new DensityHistory();
        }

        /// <summary>
        /// Starts a run of the deck <paramref name="tcl"/> with OpenSees, writing its files as <paramref name="stem"/> in
        /// <paramref name="folder"/>: from the beginning, or from where <paramref name="previous"/>, a run of the same model and
        /// settings, stopped.
        /// </summary>
        public static TopologyRun Start(GH_Component owner, string key, Alpaca4d.Model model, List<string> tcl, List<SolidCell> cells,
                                        TopologySettings settings, SystemEquation system, string openSees, string folder, string stem,
                                        TopologyResult previous = null)
        {
            var run = new TopologyRun(owner, key, model, cells, settings, previous);
            int solidNodes = model.UniquePointsThreeNDF.Count;
            TimeSpan? timeout = Alpaca4d.Application.OpenSeesTimeout;

            Task.Run(() =>
            {
                TopologyAnalysis analysis = null;
                try
                {
                    analysis = new TopologyAnalysis(openSees, folder, stem, tcl, cells, solidNodes, system, timeout);
                    var result = TopologyOptimiser.Run(cells, settings, analysis.Energies, () => run._stop, run.Reached, previous, run.Frames);
                    run.Log = analysis.Log;
                    run.Result = result;
                    lock (run._gate)
                        run._densities = result.Densities;
                }
                catch (Exception ex)
                {
                    run.Log = analysis?.Log ?? "";
                    run.Failure = Alpaca4d.Element.TenNodeTetrahedron.SolverTooOld(ex.Message) ?? ex.Message;
                }
                finally
                {
                    analysis?.Dispose();
                    run.IsRunning = false;
                    run.Show();
                }
            });

            return run;
        }

        /// <summary>Ends the run after the iteration under way. Its outputs are those of the design reached.</summary>
        public void Stop() => _stop = true;

        public bool StopRequested => _stop;

        /// <summary>How many iterations the run has made, those of the run it went on from included.</summary>
        public int Done
        {
            get
            {
                lock (_gate)
                    return _history.Count;
            }
        }

        /// <summary>The physical densities of the latest design, and the iterations so far.</summary>
        public (double[] densities, List<TopologyIteration> history) Snapshot()
        {
            lock (_gate)
                return (_densities, _history.ToList());
        }

        private void Reached(TopologyIteration iteration, double[] densities)
        {
            lock (_gate)
            {
                _densities = densities;
                _history.Add(iteration);
            }

            if (_sinceShown.Elapsed.TotalSeconds >= 1.0)
                Show();
        }

        /// <summary>
        /// Asks for a solution of the owner, on the UI thread, unless something else replaced this run.
        /// From a task of its own, as InvokeOnUiThread may wait for the UI thread, and the run must not:
        /// one request at a time, as a busy UI thread would otherwise collect them.
        /// </summary>
        private void Show()
        {
            _sinceShown.Restart();
            if (Interlocked.Exchange(ref _showPending, 1) == 1)
                return;

            Task.Run(() => RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                Interlocked.Exchange(ref _showPending, 0);
                var document = _owner.OnPingDocument();
                if (document == null || !(_owner is TopologyOptimisation component) || component.Run != this)
                    return;
                document.ScheduleSolution(5, d => _owner.ExpireSolution(false));
            })));
        }

        private int _showPending;
    }
}
