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
    /// Legend, as two components that differ only in their GUID: <see cref="Legend"/>, and the
    /// one 0.12.0-beta.1 shipped with a Range input, kept hidden so its definitions still open
    /// (see <see cref="Legend_obsolete"/>).
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

        private const string ValuesDescription =
            "Numbers the labels run from the smallest to the largest of - the Values output of View Results, " +
            "which follows the Range given to it. To label a range of your own, give its two ends.\n" +
            "One value more than there are colours, as View Results gives with Iso-surfaces on, labels the edges between the colours rather than the colours.";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Title", "Title", "Legend title", GH_ParamAccess.item, "");
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Values", "Values", ValuesDescription, GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colors", "Colors", "Legend colors, from the low end to the high - the Colors output of View Results, so the legend and the model agree. " +
                "Fewer than two fall back to the default gradient, as in View Results.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Position", "Pos", "0: Left, 1: Right, 2: Bottom Center", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Scale", "Scale", "Size scale factor. The legend shrinks below it where it would not fit the viewport.", GH_ParamAccess.item, 1.5);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
        }

        /// <summary>
        /// Values was called Data, and renamed after the View Results output wired into it. A file
        /// puts back the name, nickname and description each input was saved with, so one saved
        /// before would go on showing Data: renamed here, the nickname too unless it was given one
        /// of its own.
        /// </summary>
        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (!base.Read(reader)) return false;

            var values = Params.Input.Count > 1 ? Params.Input[1] : null;
            if (values != null && values.Name == "Data")
            {
                values.Name = "Values";
                values.Description = ValuesDescription;
                if (values.NickName == "Data") values.NickName = "Values";
            }

            return true;
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
            var finite = data.Where(x => !double.IsNaN(x) && !double.IsInfinity(x)).ToList();
            if (finite.Count > 0)
            {
                min = finite.Min();
                max = finite.Max();
            }

            // One more value than colours is View Results with iso-surfaces on, handing over the
            // edges of its bands: a label at each edge, between the swatches. A flat range is one
            // value either way, and one swatch.
            bool edges = data.Count == colors.Count + 1;
            var values = ColourScale.LabelValues(min, max, colors.Count + (edges ? 1 : 0));
            int swatches = values.Count > 1 && edges ? values.Count - 1 : values.Count;

            _conduit.Update(title, colors.Take(swatches).ToList(), values.Select(value => ColourScale.Format(value, min, max)).ToList(), position, scale);
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

            // Only the size of a measured box is used, so text is measured in the middle of the
            // viewport, where no part of the box can be off the screen.
            private Point2d _measureAt;

            public LegendConduit(Func<bool> shouldDraw)
            {
                _shouldDraw = shouldDraw;
            }

            /// <summary>
            /// One label per colour, each read at the middle of its swatch - or one more than
            /// that, the edges of bands, each read at the line between two swatches.
            /// </summary>
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
                _measureAt = new Point2d(0.5 * width, 0.5 * height);

                if (_position == 2)
                    DrawHorizontal(e.Display, width, height);
                else
                    DrawVertical(e.Display, width, height);
            }

            /// <summary>Labels on the edges between swatches, not on the swatches: see <see cref="Update"/>.</summary>
            private bool Edges => _labels.Count == _colors.Count + 1;

            /// <summary>A column of swatches with their labels to the right, at the left or right edge.</summary>
            private void DrawVertical(DisplayPipeline display, int width, int height)
            {
                int count = _colors.Count;
                double unit = 15.0 * _scale;
                double gap = 0.5 * unit;
                int textPx = Px(1.2 * unit);
                int titlePx = Px(1.8 * unit);

                bool hasTitle = !string.IsNullOrWhiteSpace(_title);
                var titleBox = hasTitle ? Measure(display, _title, titlePx) : Size.Empty;
                double titleBlock = hasTitle ? titleBox.Height + gap : 0.0;

                // A label on the top edge stands half its height above the column, and one on the
                // bottom edge half below, so the column is a label shorter and starts half lower.
                double overhang = Edges ? textPx : 0.0;

                double available = height * (1.0 - MarginTop - MarginBottomVertical) - titleBlock - overhang;
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

                y += 0.5 * overhang;

                for (int k = 0; k < count; k++)
                {
                    int swatchTop = Px(y + k * length);
                    int swatchBottom = Px(y + (k + 1) * length);
                    display.Draw2dRectangle(new Rectangle(Px(x), swatchTop, Px(unit), swatchBottom - swatchTop), Ink, Stroke, _colors[k]);
                }

                for (int k = 0; k < _labels.Count; k++)
                {
                    double at = y + (Edges ? k : k + 0.5) * length;
                    DrawText(display, _labels[k], boxes[k], textPx, x + unit + gap, at - 0.5 * boxes[k].Height);
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

                // Labels on the edges stand half a swatch past either end of the row.
                double length = Math.Min(3.5 * unit, width * (1.0 - 2.0 * MarginSide) / (Edges ? count + 1 : count));
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
                var titleBox = hasTitle ? Measure(display, _title, titlePx) : Size.Empty;

                // Stacked up from the bottom margin: title, then the bar, then the labels.
                double titleTop = height * (1.0 - MarginBottomHorizontal) - titleBox.Height;
                double barTop = (hasTitle ? titleTop - gap : titleTop) - unit;
                double x = 0.5 * (width - count * length);

                for (int k = 0; k < count; k++)
                {
                    int swatchLeft = Px(x + k * length);
                    int swatchRight = Px(x + (k + 1) * length);
                    display.Draw2dRectangle(new Rectangle(swatchLeft, Px(barTop), swatchRight - swatchLeft, Px(unit)), Ink, Stroke, _colors[k]);
                }

                for (int k = 0; k < _labels.Count; k++)
                {
                    double at = x + (Edges ? k : k + 0.5) * length;
                    DrawText(display, _labels[k], boxes[k], textPx, at - 0.5 * boxes[k].Width, barTop - gap - boxes[k].Height);
                }

                if (hasTitle)
                    DrawText(display, _title, titleBox, titlePx, 0.5 * (width - titleBox.Width), titleTop);
            }

            /// <summary>The size of <paramref name="text"/> drawn <paramref name="height"/> pixels high.</summary>
            private Size Measure(DisplayPipeline display, string text, int height)
            {
                var box = display.Measure2dText(text, _measureAt, true, 0.0, height, FontFace);
                return new Size(Math.Abs(box.Width), Math.Abs(box.Height));
            }

            /// <summary>
            /// Draws <paramref name="text"/> in a box of <paramref name="size"/> with its top-left at
            /// (x, y), centred in it. Text that is not centred hangs from a corner: Rhino's
            /// documentation says the lower-left, the hand-tuned offsets this legend first had only
            /// worked from the top-left, and placing it by the corner Measure2dText reports put the
            /// title well off in every position. Centred text has no corner to disagree about.
            /// </summary>
            private static void DrawText(DisplayPipeline display, string text, Size size, int height, double x, double y)
            {
                display.Draw2dText(text, Ink, new Point2d(x + 0.5 * size.Width, y + 0.5 * size.Height), true, height, FontFace);
            }

            private static int Px(double value) => (int)Math.Round(value);
        }
    }

    /// <summary>
    /// The GUID Legend had up to 0.11.0, and has again now that its inputs are the same five, so
    /// definitions from then open as the current Legend.
    /// </summary>
    public class Legend : LegendBase
    {
        public override Guid ComponentGuid => new Guid("{A6B3B5C9-DB8D-4B6A-9E7E-63C6D2E2F9F5}");
    }
}
