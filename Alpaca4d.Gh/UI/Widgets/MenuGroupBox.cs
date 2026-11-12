using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;

namespace Alpaca4d.UIWidgets
{
    /// <summary>What widgets are added to: a menu's panel, or a group box in it.</summary>
    public interface IWidgetContainer
    {
        void AddControl(GH_Attr_Widget control);
    }

    /// <summary>
    /// Widgets in a rounded frame of their own, a little in from the menu's left edge, one under the
    /// other: what belongs to the control above it - the component of a result, its unit, its table -
    /// so that it reads as that control's. Shown only while one of its widgets is.
    ///
    /// Saved as a chunk of its own, the widgets in it saved inside it.
    /// </summary>
    public class MenuGroupBox : GH_Attr_Widget, IWidgetContainer
    {
        private readonly List<GH_Attr_Widget> _controls = new List<GH_Attr_Widget>();
        private GH_Attr_Widget _active;

        /// <summary>How far in from the menu's left edge the frame starts.</summary>
        public float Indent { get; set; }

        /// <summary>The room between the frame and the widgets in it.</summary>
        public float Pad { get; set; }

        /// <summary>The room between two widgets, one under the other.</summary>
        public float Space { get; set; }

        public MenuGroupBox(int index, string id)
            : base(index, id)
        {
            Indent = 10f;
            Pad = 4f;
            Space = 4f;
        }

        public void AddControl(GH_Attr_Widget control)
        {
            _controls.Add(control);
            control.Parent = this;
        }

        public IReadOnlyList<GH_Attr_Widget> Controls => _controls;

        /// <summary>Shown only while one of its widgets is, and not hidden itself.</summary>
        public override bool Visible
        {
            get => base.Visible && _controls.Any(c => c.Visible);
            set => base.Visible = value;
        }

        private IEnumerable<GH_Attr_Widget> Shown => _controls.Where(c => c.Visible);

        public override SizeF ComputeMinSize()
        {
            float width = 0f, height = 0f;
            int n = 0;
            foreach (var control in Shown)
            {
                var size = control.ComputeMinSize();
                width = Math.Max(width, size.Width);
                if (n++ > 0) height += Space;
                height += size.Height;
            }
            return new SizeF(Indent + 2f * Pad + width, 2f * Pad + height);
        }

        public override void Layout()
        {
            float x = CanvasPivot.X + Indent + Pad;
            float y = CanvasPivot.Y + Pad;
            float width = Math.Max(1f, Width - Indent - 2f * Pad);
            int n = 0;
            foreach (var control in Shown)
            {
                if (n++ > 0) y += Space;
                control.UpdateBounds(new PointF(x, y), width);
                control.Style = Style;
                control.Palette = Palette;
                control.Layout();
                y += control.Height;
            }
            Height = y + Pad - CanvasPivot.Y;
        }

        public override void Render(WidgetRenderArgs args)
        {
            var graphics = args.Canvas.Graphics;
            int alpha = GH_Canvas.ZoomFadeLow;
            var frame = new RectangleF(CanvasPivot.X + Indent, CanvasPivot.Y, Width - Indent, Height);
            WidgetFrame.Draw(graphics, frame, WidgetFrame.Fill(this, alpha), alpha);
            foreach (var control in Shown)
                control.OnRender(args);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            foreach (var control in Shown)
            {
                if (!control.Enabled || !control.Contains(e.CanvasLocation)) continue;
                var response = control.RespondToMouseDown(sender, e);
                if (response != GH_ObjectResponse.Ignore)
                {
                    _active = control;
                    return response;
                }
            }
            return GH_ObjectResponse.Ignore;
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_active == null) return GH_ObjectResponse.Ignore;
            var response = _active.RespondToMouseUp(sender, e);
            if (response == GH_ObjectResponse.Release || response == GH_ObjectResponse.Ignore)
                _active = null;
            return response;
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            return _active?.RespondToMouseMove(sender, e) ?? GH_ObjectResponse.Ignore;
        }

        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            foreach (var control in Shown)
                if (control.Enabled && control.Contains(e.CanvasLocation))
                    return control.RespondToMouseDoubleClick(sender, e);
            return GH_ObjectResponse.Ignore;
        }

        public override GH_Attr_Widget IsTtipPoint(PointF pt)
        {
            if (!CanvasBounds.Contains(pt)) return null;
            foreach (var control in Shown)
            {
                var tip = control.IsTtipPoint(pt);
                if (tip != null) return tip;
            }
            return null;
        }

        public override bool Write(GH_IWriter writer)
        {
            var chunk = writer.CreateChunk("Box", Index);
            foreach (var control in _controls)
                control.Write(chunk);
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            // A file saved before the box was there keeps its widgets' defaults.
            var chunk = reader.FindChunk("Box", Index);
            if (chunk == null) return true;
            foreach (var control in _controls)
                control.Read(chunk);
            return true;
        }

        public override string GetWidgetDescription()
        {
            return base.GetWidgetDescription() + "{\n" + string.Concat(_controls.Select(c => c.GetWidgetDescription() + "\n")) + "}";
        }
    }

    /// <summary>The rounded frame of a group box or a framed group of checkboxes.</summary>
    internal static class WidgetFrame
    {
        /// <summary>
        /// The frame's fill: the menu's panel lightened by two fifths for each frame it is in, its own
        /// included, so that a frame in a frame still stands out.
        /// </summary>
        public static Color Fill(GH_Attr_Widget widget, int alpha)
        {
            int depth = 0;
            for (var w = widget; w != null; w = w.Parent)
                if (w is MenuGroupBox) depth++;
            if (!(widget is MenuGroupBox)) depth++;

            var fill = widget.Style?.Fill ?? Color.FromArgb(200, 200, 200);
            double keep = Math.Pow(0.6, depth);
            int Lighter(int c) => (int)Math.Round(255 - (255 - c) * keep);
            return Color.FromArgb(alpha, Lighter(fill.R), Lighter(fill.G), Lighter(fill.B));
        }

        /// <summary>A rounded box filled with <paramref name="fill"/>, edged in the grey a menu's panel is edged in.</summary>
        public static void Draw(Graphics graphics, RectangleF frame, Color fill, int alpha)
        {
            if (frame.Width <= 1f || frame.Height <= 1f) return;

            const float radius = 3f;
            var smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Color.FromArgb(alpha, 80, 80, 80), 1f))
            {
                float d = 2f * radius;
                path.AddArc(frame.X, frame.Y, d, d, 180f, 90f);
                path.AddArc(frame.Right - d, frame.Y, d, d, 270f, 90f);
                path.AddArc(frame.Right - d, frame.Bottom - d, d, d, 0f, 90f);
                path.AddArc(frame.X, frame.Bottom - d, d, d, 90f, 90f);
                path.CloseFigure();
                graphics.FillPath(brush, path);
                graphics.DrawPath(pen, path);
            }
            graphics.SmoothingMode = smoothing;
        }
    }
}
