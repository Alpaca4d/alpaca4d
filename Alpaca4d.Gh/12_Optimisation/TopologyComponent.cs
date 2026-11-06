using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Optimisation;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// What the three topology optimisation components share - Topology Optimisation (SIMP), Multi-Material
    /// Topology and BESO Topology: a run on a thread of its own that goes on, pauses and starts again as the
    /// inputs say, the deck read off the model, and the design shown as a surface, solids and a model.
    /// </summary>
    public abstract class TopologyComponent : GH_SwitcherComponent, ITopologyRunner
    {
        protected TopologyComponent(string name, string nickname, string description, string category, string subCategory)
          : base(name, nickname, description, category, subCategory)
        {
        }

        protected const string DefaultSystem = "Auto";

        /// <summary>The run in progress, or the last one.</summary>
        internal TopologyRun Run { get; set; }

        TopologyRun ITopologyRunner.Run => Run;

        private int _runs;

        private protected enum Going { On, OnFromWhereItStopped, FromTheBeginning }

        /// <summary>
        /// What Run on does with the run there is, given the inputs: lets it be - running, or finished with
        /// nothing left to do; goes on from where it stopped - it stopped short of <paramref name="iterations"/>,
        /// or was stopped, short of convergence under <paramref name="tolerance"/>; or starts again - nothing
        /// has run, the run was of other inputs, or it has already made more iterations than asked for now,
        /// and the design after fewer is not kept.
        ///
        /// A run under way takes a new Iterations and Tolerance as it goes: it reads them every iteration.
        /// </summary>
        /// <param name="settled">Whether an iteration met the method's test of convergence under a tolerance.</param>
        private protected static Going Next(TopologyRun run, string key, int iterations, double tolerance, Func<TopologyIteration, double, bool> settled)
        {
            if (run == null || run.Key != key)
                return Going.FromTheBeginning;

            if (run.IsRunning)
            {
                if (run.Done >= iterations)
                    return Going.FromTheBeginning;
                run.Settings.MaxIterations = iterations;
                run.Settings.Tolerance = tolerance;
                return Going.On;
            }

            var result = run.Result;
            if (result == null)
                return run.StopRequested ? Going.FromTheBeginning : Going.On;   // failed: again once Run is switched off and on

            int done = result.History.Count;
            if (done > iterations)
                return Going.FromTheBeginning;
            bool moving = done == 0 || !settled(result.History[done - 1], tolerance);
            if (done < iterations && moving)
                return Going.OnFromWhereItStopped;

            run.Settings.MaxIterations = iterations;
            run.Settings.Tolerance = tolerance;
            return Going.On;
        }

        /// <summary>The model's elements that are not solids, counted by kind - "2 beams and 1 shell" - or null when there are none.</summary>
        protected static string OtherElements(Alpaca4d.Model model)
        {
            string Count(int n, string one, string many) => n == 0 ? null : n == 1 ? $"1 {one}" : $"{n} {many}";

            int trusses = model.Beams.OfType<Alpaca4d.Element.Truss>().Count();
            var kinds = new[]
            {
                Count(model.Beams.Count - trusses, "beam", "beams"),
                Count(trusses, "truss", "trusses"),
                Count(model.Shells.Count, "shell", "shells"),
                Count(model.Links.Count, "link", "links"),
            }.Where(kind => kind != null).ToList();

            if (kinds.Count == 0)
                return null;
            return kinds.Count == 1 ? kinds[0] : string.Join(", ", kinds.Take(kinds.Count - 1)) + " and " + kinds.Last();
        }

        /// <summary>Warns of what the optimisation leaves as it is: elements that are not solids, and the self-weight.</summary>
        protected void WarnAboutModel(Alpaca4d.Model model)
        {
            string others = OtherElements(model);
            if (others != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The model also has {others}. Only solids are optimised: the rest keep their full stiffness and are analysed " +
                    "with the solids, but their strain energy is not in Compliance.");

            if (model.LoadPatterns.Any(pattern => pattern.Load != null && pattern.Load.Any(load => load is Alpaca4d.Loads.Gravity)))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "The model's self-weight is that of every solid, solid: it stays on the elements the optimisation empties. " +
                    "Leave Gravity out of the model to optimise for the other loads alone.");
        }

        /// <summary>The solids the Keep input names, warning of the names that match nothing.</summary>
        protected HashSet<IBrick> Kept(Alpaca4d.Result.ElementFilter keep, Alpaca4d.Model model)
        {
            var bricks = model.Bricks;
            var kept = keep.MatchesEverything ? new HashSet<IBrick>() : new HashSet<IBrick>(keep.SelectIndices(bricks).Select(i => bricks[i]));
            if (!keep.MatchesEverything && kept.Count == 0)
                foreach (var problem in keep.Unmatched(model.Elements))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, problem);
            return kept;
        }

        /// <summary>
        /// What every run starts from: the licence, OpenSees, the cells and the deck read off the model, and the
        /// folder and stem of its files. False, with an error, when it cannot start.
        /// </summary>
        /// <param name="what">The component's name, for the licence's message: "Topology Optimisation takes".</param>
        /// <param name="prefix">The start of the files' names: "Topology".</param>
        protected bool Prepare(IGH_DataAccess DA, Alpaca4d.Model model, HashSet<IBrick> kept, string what, string prefix,
                               out List<string> tcl, out List<SolidCell> cells, out string openSees, out string folder, out string stem)
        {
            tcl = null;
            cells = null;
            folder = stem = null;

            // Free up to License.FreeTopologyElementLimit elements. Past it, no license means no run.
            openSees = null;
            if (!LicenseGate.AllowsModel(this, model, Alpaca4d.License.License.FreeTopologyElementLimit, what))
                return false;

            openSees = Alpaca4d.Application.OpenSees;
            if (string.IsNullOrWhiteSpace(openSees) || !File.Exists(openSees))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "OpenSees is not where Alpaca4d looks for it. Set it in Alpaca4d → Settings in the Grasshopper menu.");
                return false;
            }

            try
            {
                tcl = TopologyDeck.From(model, kept.Contains, out cells);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return false;
            }

            if (cells.All(cell => cell.Passive))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Keep holds every solid, so there is nothing left to optimise.");
                return false;
            }

            folder = AnalysisFiles.For(this, DA).Folder;
            stem = prefix + "_" + this.InstanceGuid.ToString("N").Substring(0, 8) + "_" + (++_runs);
            return true;
        }

        /// <summary>
        /// Reads the System input into a system of equations; null, with an error, when it names none.
        /// </summary>
        protected SystemEquation SystemOf(string system)
        {
            try { return new SystemEquation(string.IsNullOrWhiteSpace(system) ? DefaultSystem : system.Trim()); }
            catch (ArgumentException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"System \"{system}\" is not one of {string.Join(", ", Enum.GetNames(typeof(SystemEquation.SystemType)))}.");
                return null;
            }
        }

        /// <summary>
        /// The densities to show and the iteration they are of: those of the design after iteration
        /// <paramref name="step"/>, from the frames kept, or the latest from the run itself, to the last digit,
        /// when it is null or beyond what has run.
        /// </summary>
        private protected static (double[] densities, List<TopologyIteration> history, int shown) Shown(TopologyRun run, int? step)
        {
            var (densities, history) = run.Snapshot();
            int shown = history.Count;
            if (step.HasValue && step.Value < history.Count && step.Value < run.Frames.Count)
            {
                shown = step.Value;
                densities = run.Frames[shown];
            }
            return (densities, history, shown);
        }

        /// <summary>
        /// The outputs of a run of one density per element - SIMP's or BESO's: the design after iteration
        /// <paramref name="step"/>, or the latest when it is null or beyond what has run, and what the run is doing.
        /// </summary>
        /// <param name="stillGoing">The warning of a run that stopped short of converging after the iterations given.</param>
        private protected void ShowDensities(IGH_DataAccess DA, TopologyRun run, double threshold, int? step, Func<int, string> stillGoing)
        {
            var (densities, history, shown) = Shown(run, step);
            var model = run.Model;
            var cells = run.Cells;

            if (run.Failure != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, run.Failure);

            var solid = Enumerable.Range(0, cells.Count).Select(i => cells[i].Passive || densities[i] >= threshold).ToArray();
            var design = DesignOf(run, solid, densities, shown, threshold);
            var pieces = design.Pieces;
            RemarkOnPieces(pieces);

            // The surface outlines what Solid holds: the loose pieces void, their neighbours staying under the
            // threshold, and the elements joining steps solid.
            var outline = densities.ToArray();
            for (int i = 0; i < outline.Length; i++)
            {
                if (solid[i] && !pieces.Kept[i])
                    outline[i] = 0.0;
                else if (!solid[i] && pieces.Kept[i])
                    outline[i] = 1.0;
            }
            var mesh = Outline(cells, outline, model, threshold, out int crumbs);
            RemarkOnCrumbs(crumbs);

            var bricks = model.Bricks;
            Set(DA, "Surface", new[] { mesh });
            // Copies: assembled again, the model's own would be renumbered under the run.
            Set(DA, "Solid", Enumerable.Range(0, bricks.Count).Where(i => pieces.Kept[i]).Select(i => TopologyDesign.Copy(bricks[i])));
            Set(DA, "Density", densities);
            Set(DA, "Element", bricks);
            Set(DA, "Compliance", history.Select(it => it.Compliance));
            Set(DA, "Volume", history.Select(it => it.Volume));
            Set(DA, "Converged", new[] { run.Result?.Converged ?? false });
            Set(DA, "History", history);
            Set(DA, "log", new[] { run.Log });
            Set(DA, "Step", new[] { shown });

            // The design as a model once the run has stopped: assembling it every second would slow the run down.
            if (!run.IsRunning)
            {
                var analysed = design.Model(run.Model, cells, null);
                foreach (var (level, text) in design.Messages)
                    AddRuntimeMessage(level, text);
                if (analysed != null)
                    Set(DA, "AlpacaModel", new[] { analysed });
            }

            ShowProgress(run, history.Count, stillGoing);
        }

        /// <summary>The component's message while the run goes, and the warning of a run stopped short.</summary>
        private protected void ShowProgress(TopologyRun run, int done, Func<int, string> stillGoing)
        {
            if (run.IsRunning)
                this.Message = $"Iteration {done + 1} of {run.Settings.MaxIterations}";
            else
            {
                this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
                if (run.Result != null && !run.Result.Converged)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, run.Result.Stopped
                        ? $"Paused after iteration {done}. The outputs are the design it reached; set Run to true to go on from there."
                        : stillGoing(done));
            }
        }

        protected void RemarkOnPieces(SolidPieces pieces)
        {
            if (pieces.LeftOut > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{pieces.LeftOut} solid element{(pieces.LeftOut == 1 ? "" : "s")} in {pieces.PiecesLeftOut} piece{(pieces.PiecesLeftOut == 1 ? " is" : "s are")} " +
                    "left out of Surface, Solid and AlpacaModel: no face joins them to a part the supports hold still.");
            if (pieces.Bridged > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{pieces.Bridged} element{(pieces.Bridged == 1 ? "" : "s")} under Threshold {(pieces.Bridged == 1 ? "is" : "are")} in Solid and AlpacaModel all the same: " +
                    "they join steps of a member that meet only along an edge or at a corner, where the member would hinge. Surface shows them solid.");
        }

        protected void RemarkOnCrumbs(int crumbs)
        {
            if (crumbs > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Surface leaves out {crumbs} crumb{(crumbs == 1 ? "" : "s")}, under a hundredth of the largest piece each: where a thin " +
                    "member fades out around Threshold its surface breaks up, though Solid joins its elements to the rest.");
        }

        /// <summary>
        /// The surface where <paramref name="densities"/> cross <paramref name="threshold"/>, closed where it meets
        /// the edge of the domain, without the <paramref name="crumbs"/> it breaks into where a thin member fades
        /// out around the threshold.
        /// </summary>
        protected static Mesh Outline(IReadOnlyList<SolidCell> cells, double[] densities, Alpaca4d.Model model, double threshold, out int crumbs)
        {
            crumbs = 0;
            var surface = new DensitySurface(cells, densities, tag => model.Nodes[tag - 1].Pos, threshold);
            var mesh = new Mesh();
            mesh.Vertices.AddVertices(surface.Vertices);
            foreach (var t in surface.Triangles)
                mesh.Faces.AddFace(t[0], t[1], t[2]);
            // Cuts that fell on a node at exactly the threshold stand on one point as vertices of their own.
            mesh.Vertices.CombineIdentical(true, true);
            mesh.Faces.CullDegenerateFaces();
            mesh.Normals.ComputeNormals();
            mesh.Compact();

            // Where a thin member fades out just around Threshold, the surface through the averaged
            // densities breaks into crumbs apart from the rest, though Solid joins their elements to it.
            var parts = mesh.SplitDisjointPieces();
            if (parts.Length > 1)
            {
                var volumes = parts.Select(part => Math.Abs(VolumeMassProperties.Compute(part, true, false, false, false)?.Volume ?? 0.0)).ToArray();
                double largest = volumes.Max();
                var whole = Enumerable.Range(0, parts.Length).Where(i => volumes[i] >= 0.01 * largest).ToList();
                crumbs = parts.Length - whole.Count;
                if (crumbs > 0)
                {
                    mesh = new Mesh();
                    foreach (int i in whole)
                        mesh.Append(parts[i]);
                    mesh.Normals.ComputeNormals();
                    mesh.Compact();
                }
            }
            return mesh;
        }

        protected void Set(IGH_DataAccess DA, string name, System.Collections.IEnumerable data)
        {
            DA.SetDataList(this.Params.IndexOfOutputParam(name), data);
        }

        /// <summary>
        /// A design shown: its pieces and, once asked for, its model with what assembling it had to say.
        /// Kept while the same design of the same run is shown, so that a solution that changes nothing
        /// neither finds the pieces again nor assembles them again.
        /// </summary>
        private protected sealed class ShownDesign
        {
            public TopologyRun Run;
            public int Step;
            public double Threshold;
            public SolidPieces Pieces;
            public readonly List<(GH_RuntimeMessageLevel level, string text)> Messages = new List<(GH_RuntimeMessageLevel, string)>();

            private bool _built;
            private Alpaca4d.Model _model;

            /// <summary>
            /// The design as a model, assembled, or null with a message saying why not; each solid kept of the
            /// material <paramref name="materials"/> gives it, or of its own when that is null or gives null.
            /// </summary>
            public Alpaca4d.Model Model(Alpaca4d.Model optimised, IReadOnlyList<SolidCell> cells, Func<int, IMultiDimensionMaterial> materials)
            {
                if (_built)
                    return _model;
                _built = true;

                if (!Pieces.Kept.Any(kept => kept))
                {
                    Messages.Add((GH_RuntimeMessageLevel.Warning, "No part of the design is held still by the supports, so AlpacaModel is empty."));
                    return null;
                }
                try
                {
                    _model = TopologyDesign.Build(optimised, cells, Pieces.Kept, out int supports, out int loads, materials);
                    if (supports > 0)
                        Messages.Add((GH_RuntimeMessageLevel.Remark,
                            $"{supports} support{(supports == 1 ? " stands" : "s stand")} where the design is void: AlpacaModel leaves {(supports == 1 ? "it" : "them")} out."));
                    if (loads > 0)
                        Messages.Add((GH_RuntimeMessageLevel.Warning,
                            $"{loads} point load{(loads == 1 ? " or mass stands" : "s or masses stand")} on nothing the design keeps, void or a piece left " +
                            $"out: AlpacaModel leaves {(loads == 1 ? "it" : "them")} out, so it does not carry every load the design was made for."));
                }
                catch (Exception ex)
                {
                    Messages.Add((GH_RuntimeMessageLevel.Warning, "The design could not be assembled as a model: " + ex.Message));
                }
                return _model;
            }
        }

        private ShownDesign _shown;

        /// <summary>
        /// The design of the elements <paramref name="solid"/> says are solid, joining steps through the densest by
        /// <paramref name="densities"/>; kept while the same one is shown.
        /// </summary>
        private protected ShownDesign DesignOf(TopologyRun run, bool[] solid, double[] densities, int step, double threshold)
        {
            if (_shown != null && _shown.Run == run && _shown.Step == step && _shown.Threshold == threshold)
                return _shown;
            _shown = new ShownDesign
            {
                Run = run,
                Step = step,
                Threshold = threshold,
                Pieces = TopologyDesign.Pieces(run.Model, run.Cells, solid, densities),
            };
            return _shown;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            Run?.Stop();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                Run?.Stop();
            base.DocumentContextChanged(document, context);
        }
    }
}
