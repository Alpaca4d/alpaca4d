using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Legend, as two components that differ only in the Range input: <see cref="Legend"/>, which
    /// has it, and the one it replaced, kept hidden so older definitions open without Grasshopper
    /// reporting a missing input (see <see cref="Legend_obsolete"/>).
    /// </summary>
    public abstract class LegendBase : GH_Component
    {
        private readonly LegendConduit _conduit;

        protected LegendBase()
          : base("Legend (Alpaca4d)", "Legend",
            "Draw a fixed on-screen legend with colors and labels",
            "Alpaca4d", "09_Visualisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
            _conduit = new LegendConduit(ShouldDraw);
        }

        /// <summary>Whether the component has the Range input. Only the obsolete one does not.</summary>
        protected virtual bool HasRange => true;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Title", "Title", "Legend title", GH_ParamAccess.item, "");
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Data", "Data", "Numeric data the labels run from the smallest to the largest of." +
                (HasRange ? " Ignored when Range is set." : ""), GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colors", "Colors", "Legend colors, from the low end to the high - the list given to View Results, so the legend and the model agree. " +
                "Fewer than two fall back to the default gradient, as in View Results.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Position", "Pos", "0: Left, 1: Right, 2: Bottom Center", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Scale", "Scale", "Size scale factor. The legend shrinks below it where it would not fit the viewport.", GH_ParamAccess.item, 1.5);
            pManager[pManager.ParamCount - 1].Optional = true;

            if (HasRange)
            {
                pManager.AddIntervalParameter("Range", "Range", "Range the colors span, in place of the one Data gives. " +
                    "Set it to the Range given to View Results, or the labels will not match the model.", GH_ParamAccess.item);
                pManager[pManager.ParamCount - 1].Optional = true;
            }
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Locals rather than fields: an input that brings no data then leaves its default,
            // not whatever the last solution read.
            string title = string.Empty;
            var data = new List<double>();
            var colors = new List<Color>();
            int position = 0;
            double scale = 1.5;

            DA.GetData(0, ref title);
            DA.GetDataList(1, data);
            DA.GetDataList(2, colors);
            DA.GetData(3, ref position);
            DA.GetData(4, ref scale);

            if (!(scale > 0)) scale = 1.5;

            if (position < 0 || position > 2)
            {
                int clamped = Math.Max(0, Math.Min(2, position));
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Position is 0 (left), 1 (right) or 2 (bottom); {position} is read as {clamped}.");
                position = clamped;
            }

            // As View Results does: one colour is not a gradient, and the two have to agree.
            if (colors.Count < 2)
                colors = Alpaca4d.Colors.Gradient(0);

            double min = -1.0;
            double max = 1.0;
            var range = Interval.Unset;
            if (HasRange && DA.GetData(5, ref range) && range.IsValid)
            {
                min = range.Min;
                max = range.Max;
            }
            else
            {
                var finite = data.Where(x => !double.IsNaN(x) && !double.IsInfinity(x)).ToList();
                if (finite.Count > 0)
                {
                    min = finite.Min();
                    max = finite.Max();
                }
            }

            var values = ColourScale.LabelValues(min, max, colors.Count);
            _conduit.Update(title, colors.Take(values.Count).ToList(), values.Select(value => ColourScale.Format(value, min, max)).ToList(), position, scale);
            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        /// <summary>Whether the legend belongs on screen. Asked on every redraw.</summary>
        private bool ShouldDraw()
        {
            if (Hidden || Locked) return false;

            // The conduit draws outside Grasshopper's own preview, so the document-wide preview
            // settings have to be asked here as well.
            var document = OnPingDocument();
            if (document == null || document.PreviewMode == GH_PreviewMode.Disabled) return false;
            return document.PreviewFilter != GH_PreviewFilter.Selected || (Attributes?.Selected ?? false);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            _conduit.Enabled = true;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            _conduit.Enabled = false;
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            // Closing a document, or bringing another one forward, does not remove its components -
            // it only tells them. A conduit left on would keep the legend over whatever came next.
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                _conduit.Enabled = false;
            else if (context == GH_DocumentContext.Loaded)
                _conduit.Enabled = true;

            base.DocumentContextChanged(document, context);
        }

        protected override void BeforeSolveInstance()
        {
            List<string> resultTypes = new List<string> { "Left", "Right", "Bottom" };
            List<int> values = new List<int> { 0, 1, 2 };
            ValueListUtils.UpdateValueLists(this, 3, resultTypes, values);
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        public override bool IsPreviewCapable => true;
        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Legend__Alpaca4d_;

        /// <summary>
        /// Draws the legend in screen space, in every viewport. Sized from the Scale input, then
        /// shrunk where it would not fit: a size fixed in pixels against margins in percent ran
        /// off the edge of any viewport smaller than the one it was tuned on.
        /// </summary>
        private class LegendConduit : DisplayConduit
        {
            private const string FontFace = "Arial";
            private const int Stroke = 2;
            private const int MinTextPx = 8;

            // Fractions of the viewport. The top margin keeps clear of the viewport's name, and a
            // bar down the side keeps as far off the bottom, where the axes icon sits.
            private const double MarginSide = 0.025;
            private const double MarginTop = 0.075;
            private const double MarginBottomVertical = 0.075;
            private const double MarginBottomHorizontal = 0.025;

            private static readonly Color Ink = Color.Black;

            private readonly Func<bool> _shouldDraw;
            private string _title = string.Empty;
            private List<Color> _colors = new List<Color>();
            private List<string> _labels = new List<string>();
            private int _position;
            private double _scale = 1.5;

            public LegendConduit(Func<bool> shouldDraw)
            {
                _shouldDraw = shouldDraw;
            }

            /// <summary>One label per colour, each read at the middle of its swatch.</summary>
            public void Update(string title, List<Color> colors, List<string> labels, int position, double scale)
            {
                _title = title ?? string.Empty;
                _colors = colors;
                _labels = labels;
                _position = position;
                _scale = scale;
            }

            protected override void DrawForeground(DrawEventArgs e)
            {
                if (!_shouldDraw() || _colors.Count == 0) return;

                e.Viewport.GetScreenPort(out int left, out int right, out int bottom, out int top, out _, out _);
                int width = right - left;
                int height = bottom - top;

                if (_position == 2)
                    DrawHorizontal(e.Display, width, height);
                else
                    DrawVertical(e.Display, width, height);
            }

            /// <summary>A column of swatches with their labels to the right, at the left or right edge.</summary>
            private void DrawVertical(DisplayPipeline display, int width, int height)
            {
                int count = _colors.Count;
                double unit = 15.0 * _scale;
                double gap = 0.5 * unit;
                int textPx = Px(1.2 * unit);
                int titlePx = Px(1.8 * unit);

                bool hasTitle = !string.IsNullOrWhiteSpace(_title);
                var titleBox = hasTitle ? Measure(display, _title, titlePx) : Rectangle.Empty;
                double titleBlock = hasTitle ? titleBox.Height + gap : 0.0;

                double available = height * (1.0 - MarginTop - MarginBottomVertical) - titleBlock;
                double length = Math.Min(3.5 * unit, available / count);
                if (length < 1.0) return;

                // Each label is centred on its swatch, so it can be no taller than one.
                textPx = Math.Min(textPx, Math.Max(MinTextPx, (int)Math.Floor(length)));
                var boxes = _labels.Select(label => Measure(display, label, textPx)).ToList();

                // Right-aligned by the whole block, labels included: placing the swatches by the
                // margin left the labels to run off the edge.
                double blockWidth = Math.Max(unit + gap + boxes.Max(box => box.Width), titleBox.Width);
                double x = _position == 0 ? width * MarginSide : width * (1.0 - MarginSide) - blockWidth;
                double y = height * MarginTop;

                if (hasTitle)
                {
                    DrawText(display, _title, titleBox, titlePx, x, y);
                    y += titleBlock;
                }

                for (int k = 0; k < count; k++)
                {
                    int swatchTop = Px(y + k * length);
                    int swatchBottom = Px(y + (k + 1) * length);
                    display.Draw2dRectangle(new Rectangle(Px(x), swatchTop, Px(unit), swatchBottom - swatchTop), Ink, Stroke, _colors[k]);

                    double middle = y + (k + 0.5) * length;
                    DrawText(display, _labels[k], boxes[k], textPx, x + unit + gap, middle - 0.5 * boxes[k].Height);
                }
            }

            /// <summary>A row of swatches along the bottom, centred, labels above and the title below.</summary>
            private void DrawHorizontal(DisplayPipeline display, int width, int height)
            {
                int count = _colors.Count;
                double unit = 15.0 * _scale;
                double gap = 0.5 * unit;
                int textPx = Px(1.2 * unit);
                int titlePx = Px(1.8 * unit);

                double length = Math.Min(3.5 * unit, width * (1.0 - 2.0 * MarginSide) / count);
                if (length < 1.0) return;

                // Labels sit side by side over their swatches, so the text shrinks until the
                // widest of them fits over one.
                var boxes = _labels.Select(label => Measure(display, label, textPx)).ToList();
                double widest = boxes.Max(box => box.Width);
                if (widest > 0.9 * length)
                {
                    textPx = Math.Max(MinTextPx, (int)Math.Floor(textPx * 0.9 * length / widest));
                    boxes = _labels.Select(label => Measure(display, label, textPx)).ToList();
                }

                bool hasTitle = !string.IsNullOrWhiteSpace(_title);
                var titleBox = hasTitle ? Measure(display, _title, titlePx) : Rectangle.Empty;

                // Stacked up from the bottom margin: title, then the bar, then the labels.
                double titleTop = height * (1.0 - MarginBottomHorizontal) - titleBox.Height;
                double barTop = (hasTitle ? titleTop - gap : titleTop) - unit;
                double x = 0.5 * (width - count * length);

                for (int k = 0; k < count; k++)
                {
                    int swatchLeft = Px(x + k * length);
                    int swatchRight = Px(x + (k + 1) * length);
                    display.Draw2dRectangle(new Rectangle(swatchLeft, Px(barTop), swatchRight - swatchLeft, Px(unit)), Ink, Stroke, _colors[k]);

                    double middle = x + (k + 0.5) * length;
                    DrawText(display, _labels[k], boxes[k], textPx, middle - 0.5 * boxes[k].Width, barTop - gap - boxes[k].Height);
                }

                if (hasTitle)
                    DrawText(display, _title, titleBox, titlePx, 0.5 * (width - titleBox.Width), titleTop);
            }

            /// <summary>The box <paramref name="text"/> takes up, relative to the point it is drawn from.</summary>
            private static Rectangle Measure(DisplayPipeline display, string text, int height)
            {
                return display.Measure2dText(text, Point2d.Origin, false, 0.0, height, FontFace);
            }

            /// <summary>
            /// Draws <paramref name="text"/> with the top-left of its box at (x, y). Draw2dText
            /// places text by a corner of its own choosing, so the box is measured first and the
            /// text placed by that instead.
            /// </summary>
            private static void DrawText(DisplayPipeline display, string text, Rectangle box, int height, double x, double y)
            {
                display.Draw2dText(text, Ink, new Point2d(x - box.Left, y - box.Top), false, height, FontFace);
            }

            private static int Px(double value) => (int)Math.Round(value);
        }
    }

    public class Legend : LegendBase
    {
        public override Guid ComponentGuid => new Guid("{C942420B-9CE2-4898-BAFF-0A65A95DA4A7}");
    }
}
