using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using GH_IO.Serialization;
using Grasshopper.Kernel;

using Alpaca4d.Optimisation;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Non-Linear Optimisation: DoDo's NL-Opt, brought into Alpaca4d.
    ///
    /// Like Galapagos it is an object on the canvas rather than a component: it is linked to the
    /// sliders it moves and the number it optimises without wires, because a wire from the
    /// objective back to the sliders would be a loop. Drag from the Variables grip to the sliders
    /// (or Galapagos gene pools), from the Objective grip to the number to minimise or maximise,
    /// and, for COBYLA and ISRES, from the Constraints grip to numbers that must end up &lt;= 0.
    /// Double-click it to choose the algorithm and run it.
    ///
    /// DoDo drove NLopt through a 32-bit Windows wrapper, so its NL-Opt ran on no 64-bit Rhino and
    /// on no Mac. Here the algorithms are NLopt's derivative-free ones ported to C#
    /// (Alpaca4d.Optimisation), and the window is Eto, so the same object runs everywhere. The
    /// gradient-based algorithms DoDo listed are gone: a Grasshopper definition gives no gradient.
    /// </summary>
    public class NonLinearOptimisation : GH_DocumentObject
    {
        public NonLinearOptimisation()
          : base("Non-Linear Optimisation (Alpaca4d)", "NL-Opt",
            "Minimises or maximises a number by moving number sliders, with NLopt's derivative-free algorithms.\n" +
            "Drag from Variables to the sliders, from Objective to the number, and from Constraints to numbers that must end up <= 0. " +
            "Double-click to choose the algorithm and run.",
            "Alpaca4d", "12_Optimisation")
        {
        }

        public override Guid ComponentGuid => new Guid("{7C1E2A5D-3B84-4F6A-9D0E-5A2B8C4F1E63}");
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Non_Linear_Optimisation__Alpaca4d_;

        /// <summary>The sliders and gene pools the optimiser moves, in order.</summary>
        public List<Guid> Variables { get; } = new List<Guid>();

        /// <summary>The parameter whose single number is minimised or maximised.</summary>
        public Guid Objective { get; set; }

        /// <summary>Parameters whose every number must end up &lt;= 0.</summary>
        public List<Guid> Constraints { get; } = new List<Guid>();

        internal OptimisationSettings Settings { get; } = new OptimisationSettings();

        /// <summary>The run going on now, or the last one, for the window to show. Not saved.</summary>
        internal OptimisationRun Run { get; private set; }

        internal bool IsRunning => Run != null && Run.IsActive;

        /// <summary>What the optimiser shows under itself on the canvas.</summary>
        internal string Status
        {
            get
            {
                var info = AlgorithmInfo.Of(Settings.Algorithm);
                if (Run == null) return $"{info.Name} · {(Settings.Maximise ? "max" : "min")}";
                if (Run.IsActive) return $"{Run.History.Count} evaluations";
                return Run.Best == null ? info.Name : $"Best {OptimisationRun.Format(Run.Best.Objective)}";
            }
        }

        public override void CreateAttributes()
        {
            m_attributes = new NonLinearOptimisationAttributes(this);
        }

        /// <summary>Opens the window that sets up and runs the optimisation.</summary>
        public void ShowWindow() => OptimisationWindow.ShowFor(this);

        /// <summary>
        /// Sets the optimiser up as its window would, for scripts that build definitions. The
        /// algorithm is its name in <see cref="OptimisationAlgorithm"/>: DirectL, Crs2Lm, Isres,
        /// Cobyla, Bobyqa, NelderMead or Sbplx. Typed arguments, so that Python 3 and IronPython
        /// both hand them over as they are meant.
        /// </summary>
        public void Configure(string algorithm, bool maximise, int maxEvaluations, int seed)
        {
            Settings.Algorithm = (OptimisationAlgorithm)Enum.Parse(typeof(OptimisationAlgorithm), algorithm, true);
            Settings.Maximise = maximise;
            Settings.MaxEvaluations = Math.Max(0, maxEvaluations);
            Settings.Seed = (uint)Math.Max(0, seed);
            Attributes?.ExpireLayout();
            OptimisationWindow.RefreshFor(this);
        }

        /// <summary>
        /// Checks what the optimiser is linked to and starts a run, or tells the user what stops it.
        /// Returns the run, or null.
        /// </summary>
        internal OptimisationRun StartRun(List<string> warnings, out string problem)
        {
            problem = null;
            if (IsRunning) return Run;
            // Needs a license whatever the problem. Linking and setting up are free; running is not.
            problem = LicenseGate.Refusal("Non-Linear Optimisation");
            if (problem != null) return null;
            var run = OptimisationRun.Prepare(this, warnings, out problem);
            if (run == null) return null;
            Run = run;
            run.Progress += (sender, e) => Attributes?.ExpireLayout();
            run.Begin();
            Attributes?.ExpireLayout();
            Grasshopper.Instances.InvalidateCanvas();
            return run;
        }

        internal void RunEnded(OptimisationRun run)
        {
            if (Run != run) return;
            Attributes?.ExpireLayout();
            Grasshopper.Instances.InvalidateCanvas();
        }

        public override bool AppendMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "Open…", (sender, e) => ShowWindow());
            if (IsRunning)
                Menu_AppendItem(menu, "Stop", (sender, e) => Run.Stop());
            Menu_AppendSeparator(menu);
            return base.AppendMenuItems(menu);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            Run?.Abandon();
            OptimisationWindow.CloseFor(this);
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
            {
                Run?.Abandon();
                OptimisationWindow.CloseFor(this);
            }
            base.DocumentContextChanged(document, context);
        }

        public override bool Write(GH_IWriter writer)
        {
            var chunk = writer.CreateChunk("NonLinearOptimisation");
            chunk.SetGuid("objective", Objective);
            chunk.SetInt32("variable_count", Variables.Count);
            for (int i = 0; i < Variables.Count; i++) chunk.SetGuid("variable", i, Variables[i]);
            chunk.SetInt32("constraint_count", Constraints.Count);
            for (int i = 0; i < Constraints.Count; i++) chunk.SetGuid("constraint", i, Constraints[i]);
            Settings.Write(chunk.CreateChunk("Settings"));
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            Variables.Clear();
            Constraints.Clear();
            Objective = Guid.Empty;
            var chunk = reader.FindChunk("NonLinearOptimisation");
            if (chunk != null)
            {
                if (chunk.ItemExists("objective")) Objective = chunk.GetGuid("objective");
                int variables = chunk.ItemExists("variable_count") ? chunk.GetInt32("variable_count") : 0;
                for (int i = 0; i < variables; i++) Variables.Add(chunk.GetGuid("variable", i));
                int constraints = chunk.ItemExists("constraint_count") ? chunk.GetInt32("constraint_count") : 0;
                for (int i = 0; i < constraints; i++) Constraints.Add(chunk.GetGuid("constraint", i));
                var settings = chunk.FindChunk("Settings");
                if (settings != null) Settings.Read(settings);
            }
            return base.Read(reader);
        }
    }
}
