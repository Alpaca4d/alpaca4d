using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Canvas.Interaction;
using Grasshopper.Kernel;

namespace Alpaca4d.Gh
{
    /// <summary>How a drag from a grip changes the links: as a wire does, Shift adds and Ctrl removes.</summary>
    internal enum LinkMode { Replace, Add, Remove }

    /// <summary>What dragging from a grip of Non-Linear Optimisation shares, whatever it links.</summary>
    internal abstract class OptimisationLinker : GH_AbstractInteraction
    {
        protected NonLinearOptimisation Source;
        protected IGH_DocumentObject Target;
        protected PointF Point = new PointF(float.NaN, float.NaN);
        private LinkMode _mode = (LinkMode)(-1);

        protected OptimisationLinker(GH_Canvas canvas, GH_CanvasMouseEvent e, NonLinearOptimisation source) : base(canvas, e, true)
        {
            Source = source;
            FixCursor();
            canvas.StartAutoPan();
            canvas.CanvasPostPaintObjects += PaintWire;
        }

        /// <summary>Whether Shift and Ctrl mean anything for this link.</summary>
        protected virtual bool TakesMany => true;

        protected static LinkMode ModeFromKeys()
        {
            var keys = Control.ModifierKeys;
            if (keys == Keys.Shift) return LinkMode.Add;
            if (keys == Keys.Control) return LinkMode.Remove;
            return LinkMode.Replace;
        }

        // Galapagos's cursors, which Grasshopper keeps for linking without wires.
        private void FixCursor()
        {
            var mode = TakesMany ? ModeFromKeys() : LinkMode.Replace;
            if (mode == _mode) return;
            _mode = mode;
            string cursor = mode == LinkMode.Add ? "GH_AddGenome" : mode == LinkMode.Remove ? "GH_RemoveGenome" : "GH_Genome";
            try
            {
                Instances.CursorServer.AttachCursor(Canvas, cursor);
            }
            catch (Exception)
            {
                // a missing cursor is no reason to stop linking
            }
        }

        public override void Destroy()
        {
            m_canvas.CanvasPostPaintObjects -= PaintWire;
            m_canvas.StopAutoPan();
            base.Destroy();
        }

        /// <summary>What is under the mouse that can be linked, or null.</summary>
        protected abstract IGH_DocumentObject FindTarget(GH_Document document, PointF point);

        /// <summary>The wire from the grip to <paramref name="target"/>, or to the mouse.</summary>
        protected abstract GraphicsPath WirePath(RectangleF target, bool targetIsTopLevel);

        protected abstract Color WireColour { get; }

        /// <summary>Changes the links once the mouse is let go over a target.</summary>
        protected abstract void Link(IGH_DocumentObject target, LinkMode mode);

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            base.RespondToMouseMove(sender, e);
            FixCursor();
            if (!m_active) return GH_ObjectResponse.Ignore;
            var document = sender.Document;
            if (document == null) return GH_ObjectResponse.Ignore;
            Point = e.CanvasLocation;
            Target = FindTarget(document, Point);
            sender.Refresh();
            return GH_ObjectResponse.Capture;
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            Instances.CursorServer.ResetCursor(sender);
            if (m_active && Target != null && Source != null)
            {
                Link(Target, TakesMany ? ModeFromKeys() : LinkMode.Replace);
                Source.Attributes.ExpireLayout();
                OptimisationWindow.RefreshFor(Source);
                Instances.InvalidateCanvas();
            }
            return GH_ObjectResponse.Release;
        }

        public override GH_ObjectResponse RespondToKeyDown(GH_Canvas sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Cancel)
            {
                Source = null;
                Target = null;
                Instances.CursorServer.ResetCursor(sender);
                return GH_ObjectResponse.Release;
            }
            FixCursor();
            return GH_ObjectResponse.Ignore;
        }

        public override GH_ObjectResponse RespondToKeyUp(GH_Canvas sender, KeyEventArgs e)
        {
            FixCursor();
            return GH_ObjectResponse.Ignore;
        }

        private void PaintWire(GH_Canvas sender)
        {
            if (Source == null || float.IsNaN(Point.X) || float.IsNaN(Point.Y)) return;
            try
            {
                sender.Graphics.SmoothingMode = SmoothingMode.HighQuality;
                var target = Target != null ? Target.Attributes.Bounds : new RectangleF(Point, new SizeF(2, 2));
                bool topLevel = Target == null || Target.Attributes.IsTopLevel;
                using (var path = WirePath(target, topLevel))
                using (var pen = NonLinearOptimisationAttributes.ThickWire(WireColour))
                    if (path != null) sender.Graphics.DrawPath(pen, path);
            }
            catch (Exception)
            {
                // painting a wire must never break the canvas
            }
        }

        /// <summary>Applies <paramref name="mode"/> to a list of links, as Galapagos does to its genome.</summary>
        protected void Change(List<Guid> links, Guid id, LinkMode mode, string what)
        {
            switch (mode)
            {
                case LinkMode.Add:
                    if (links.Contains(id)) return;
                    Source.RecordUndoEvent($"Add {what}");
                    links.Add(id);
                    break;
                case LinkMode.Remove:
                    if (!links.Contains(id)) return;
                    Source.RecordUndoEvent($"Remove {what}");
                    links.Remove(id);
                    break;
                default:
                    if (links.Count == 1 && links[0] == id) return;
                    Source.RecordUndoEvent($"Set {what}");
                    links.Clear();
                    links.Add(id);
                    break;
            }
        }
    }

    /// <summary>From the Variables grip to number sliders and gene pools.</summary>
    internal sealed class VariableLinker : OptimisationLinker
    {
        public VariableLinker(GH_Canvas canvas, GH_CanvasMouseEvent e, NonLinearOptimisation source) : base(canvas, e, source) { }

        protected override Color WireColour => NonLinearOptimisationAttributes.VariableColour;

        protected override IGH_DocumentObject FindTarget(GH_Document document, PointF point)
        {
            var obj = document.FindObject(point, 3f);
            return OptimisationVariables.CanBe(obj) ? obj : null;
        }

        protected override GraphicsPath WirePath(RectangleF target, bool targetIsTopLevel)
        {
            var grip = ((NonLinearOptimisationAttributes)Source.Attributes).VariablesGrip;
            return NonLinearOptimisationAttributes.SourceWirePath(grip, target);
        }

        protected override void Link(IGH_DocumentObject target, LinkMode mode) => Change(Source.Variables, target.InstanceGuid, mode, "Variable");
    }

    /// <summary>
    /// From the Objective or the Constraints grip to a parameter: one on its own on the canvas, or
    /// the output of a component. Not a component's input, which only repeats what feeds it, and
    /// not a variable, which the optimiser sets rather than reads.
    /// </summary>
    internal sealed class ParameterLinker : OptimisationLinker
    {
        private readonly bool _constraints;

        public ParameterLinker(GH_Canvas canvas, GH_CanvasMouseEvent e, NonLinearOptimisation source, bool constraints) : base(canvas, e, source)
        {
            _constraints = constraints;
        }

        protected override bool TakesMany => _constraints;

        protected override Color WireColour => _constraints ? NonLinearOptimisationAttributes.ConstraintColour : NonLinearOptimisationAttributes.ObjectiveColour;

        protected override IGH_DocumentObject FindTarget(GH_Document document, PointF point)
        {
            var attributes = document.FindAttribute(point, false) ?? document.FindAttributeByGrip(point, false, 12);
            if (attributes?.DocObject is IGH_Param param && param.Kind != GH_ParamKind.input && !OptimisationVariables.CanBe(param))
                return param;
            return null;
        }

        protected override GraphicsPath WirePath(RectangleF target, bool targetIsTopLevel)
        {
            var attributes = (NonLinearOptimisationAttributes)Source.Attributes;
            var grip = _constraints ? attributes.ConstraintsGrip : attributes.ObjectiveGrip;
            return targetIsTopLevel
                ? NonLinearOptimisationAttributes.FloatingTargetWirePath(grip, target)
                : NonLinearOptimisationAttributes.LinkedTargetWirePath(grip, target);
        }

        protected override void Link(IGH_DocumentObject target, LinkMode mode)
        {
            if (_constraints)
            {
                Change(Source.Constraints, target.InstanceGuid, mode, "Constraint");
                return;
            }
            if (Source.Objective == target.InstanceGuid) return;
            Source.RecordUndoEvent("Set Objective");
            Source.Objective = target.InstanceGuid;
        }
    }
}
