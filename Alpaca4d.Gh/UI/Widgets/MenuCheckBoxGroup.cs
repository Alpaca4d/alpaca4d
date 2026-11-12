using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;

namespace Alpaca4d.UIWidgets
{
    /// <summary>
    /// Several checkboxes as one control, any number of them ticked - none too. In a column, each
    /// is drawn as <see cref="MenuCheckBox"/> draws one, its label on the left and its box on the
    /// right; in a row, box and label side by side, the row shared out evenly between them, and
    /// carried on to a second row when the menu is too narrow for one. A row is what a short list of
    /// short names wants: six beam forces in one line rather than six. In a grid, as many columns as
    /// the menu has room for, each as wide as the widest of its boxes and labels, so that the boxes
    /// line up down the rows: what names of different lengths want, "X" beside "Load patterns".
    ///
    /// Square boxes, or round ones, with a dot when ticked; framed, if asked, in a rounded box of their
    /// own, as a menu's panel is, so that they read as one group.
    ///
    /// Or one choice among them, as radio buttons are: <see cref="Single"/>. A click ticks the box
    /// clicked and unticks the rest, and one is always ticked: what a drop-down would otherwise pick,
    /// all the choices in sight.
    ///
    /// Saved as a chunk of its own, so its boxes cannot be confused with the plain checkboxes of the
    /// panel it is in. A file saved before a box was added keeps that box's default.
    /// </summary>
    public class MenuCheckBoxGroup : GH_Attr_Widget
    {
        public enum LayoutDirection
        {
            Vertical,
            Horizontal,
            Grid
        }

        private readonly List<string> _labels = new List<string>();
        private readonly List<bool> _active = new List<bool>();
        private readonly List<string> _tooltips = new List<string>();

        // Worked out in Layout: where each box is drawn, and the stretch of the menu that ticks it.
        private Rectangle[] _boxes = new Rectangle[0];
        private RectangleF[] _cells = new RectangleF[0];

        private readonly Size _checkSize;
        private readonly float _padding;

        /// <summary>The box pressed, to be ticked when the mouse comes up over it; -1 for none.</summary>
        private int _pressed = -1;

        /// <summary>Column or row. A column unless set otherwise.</summary>
        public LayoutDirection Direction { get; set; }

        /// <summary>The space between two boxes, along the row or down the column.</summary>
        public float Space { get; set; }

        /// <summary>Round boxes, ticked with a dot, rather than square ones.</summary>
        public bool Round { get; set; }

        /// <summary>How far in from the menu's left edge the boxes start: to show they belong to the control above.</summary>
        public float Indent { get; set; }

        /// <summary>The boxes in a rounded frame of their own, as a menu's panel is.</summary>
        public bool Framed { get; set; }

        /// <summary>The room between the frame and the boxes in it, on each side.</summary>
        private float Pad => Framed ? 4f : 0f;

        /// <summary>
        /// How many columns a grid has - 3 keeps X, Y, Z in a row - the menu made wide enough for them;
        /// 0 for as many as the menu has room for, the rows then as even as they can be: four in two
        /// rows of two rather than three and one.
        /// </summary>
        public int Columns { get; set; }

        /// <summary>One box ticked, always: a click ticks the one clicked and unticks the rest.</summary>
        public bool Single { get; set; }

        /// <summary>The box ticked - the first, if more are - or 0 if none is. Setting it ticks that box alone, without raising <see cref="ValueChanged"/>.</summary>
        public int Value
        {
            get
            {
                for (int i = 0; i < _active.Count; i++)
                    if (_active[i]) return i;
                return 0;
            }
            set
            {
                if (_active.Count > 0) TickOnly(Math.Max(0, Math.Min(_active.Count - 1, value)));
            }
        }

        /// <summary>The label of the box ticked; empty when the group has none.</summary>
        public string Picked => _labels.Count == 0 ? "" : _labels[Value];

        public event ValueChangeEventHandler ValueChanged;

        public MenuCheckBoxGroup(int index, string id)
            : base(index, id)
        {
            _checkSize = WidgetServer.Instance.CheckBoxSize;
            _padding = WidgetServer.Instance.CheckBoxPadding;
            Direction = LayoutDirection.Vertical;
            Space = 5f;
        }

        /// <summary>A box at the end, with its label and, shown when the pointer rests on it, a longer name.</summary>
        public void Add(string label, bool active = false, string tooltip = null)
        {
            _labels.Add(label);
            _active.Add(active);
            _tooltips.Add(tooltip ?? label);
        }

        public int Count => _labels.Count;

        public IReadOnlyList<string> Labels => _labels;

        /// <summary>Whether box <paramref name="i"/> is ticked. Setting it does not raise <see cref="ValueChanged"/>, as a click does.</summary>
        public bool this[int i]
        {
            get => _active[i];
            set => _active[i] = value;
        }

        /// <summary>The boxes ticked, by their place in the group.</summary>
        public IEnumerable<int> Ticked => Enumerable.Range(0, _active.Count).Where(i => _active[i]);

        /// <summary>Box <paramref name="i"/> ticked alone.</summary>
        public void TickOnly(int i)
        {
            for (int k = 0; k < _active.Count; k++)
                _active[k] = k == i;
        }

        private Size LabelSize(string label) => TextRenderer.MeasureText(label, WidgetServer.Instance.TextFont);

        /// <summary>A box and its label, side by side: the most one of them needs.</summary>
        private SizeF CellSize()
        {
            float width = _checkSize.Width, height = _checkSize.Height;
            foreach (var label in _labels)
            {
                var size = LabelSize(label);
                width = Math.Max(width, _checkSize.Width + _padding + size.Width);
                height = Math.Max(height, size.Height);
            }
            return new SizeF(width, height);
        }

        /// <summary>Box <paramref name="i"/> and its label, side by side.</summary>
        private float CellWidth(int i) => _checkSize.Width + _padding + LabelSize(_labels[i]).Width;

        /// <summary>The room between two columns of a grid: more than a row's, whose cells have spare width.</summary>
        private float GridGap => 2f * Space;

        /// <summary>
        /// A grid's columns, each as wide as the widest box and label in it: as many as fit in
        /// <paramref name="width"/>, one at least.
        /// </summary>
        private float[] GridColumns(float width)
        {
            int count = _labels.Count;
            for (int n = Columns > 0 ? Math.Min(Columns, count) : count; n >= 1; n--)
            {
                var columns = ColumnWidths(n);
                if (n == 1 || columns.Sum() + (n - 1) * GridGap <= width + 0.5f)
                {
                    // As few rows as fit, and as many columns as those rows need: 2 x 2, not 3 + 1.
                    if (Columns <= 0)
                    {
                        int rows = (count + n - 1) / n;
                        columns = ColumnWidths((count + rows - 1) / rows);
                    }
                    return columns;
                }
            }
            return new float[] { CellSize().Width };
        }

        /// <summary>The width of each of <paramref name="n"/> columns: the widest box and label in it.</summary>
        private float[] ColumnWidths(int n)
        {
            var columns = new float[n];
            for (int i = 0; i < _labels.Count; i++)
                columns[i % n] = Math.Max(columns[i % n], CellWidth(i));
            return columns;
        }

        /// <summary>How many boxes go in one row, at the width the group has been given.</summary>
        private int PerRow(float width)
        {
            if (Direction != LayoutDirection.Horizontal || _labels.Count == 0) return 1;

            float cell = CellSize().Width;
            int fit = (int)Math.Floor((width + Space) / (cell + Space));
            return Math.Max(1, Math.Min(_labels.Count, fit));
        }

        /// <summary>Where each box and its label go, at the width the group has been given, from its top left corner.</summary>
        private RectangleF[] Cells(float width)
        {
            int count = _labels.Count;
            var cells = new RectangleF[count];
            if (count == 0) return cells;

            var cell = CellSize();
            float left = Indent + Pad, top = Pad;
            width = Math.Max(1f, width - Indent - 2f * Pad);

            if (Direction == LayoutDirection.Grid)
            {
                var columns = GridColumns(width);
                int n = columns.Length;
                for (int i = 0; i < count; i++)
                {
                    int row = i / n, column = i % n;
                    float x = left;
                    for (int c = 0; c < column; c++) x += columns[c] + GridGap;
                    cells[i] = new RectangleF(x, top + row * (cell.Height + Space), columns[column], cell.Height);
                }
                return cells;
            }

            int perRow = PerRow(width);
            float cellWidth = Direction == LayoutDirection.Vertical ? width : (width - (perRow - 1) * Space) / perRow;
            for (int i = 0; i < count; i++)
            {
                int row = i / perRow, column = i % perRow;
                cells[i] = new RectangleF(left + column * (cellWidth + Space), top + row * (cell.Height + Space), cellWidth, cell.Height);
            }
            return cells;
        }

        private float HeightAt(float width)
        {
            var cells = Cells(width);
            return cells.Length == 0 ? 0f : cells.Max(c => c.Bottom) + Pad;
        }

        public override SizeF ComputeMinSize()
        {
            var cell = CellSize();
            float frame = 2f * Pad;
            if (Direction == LayoutDirection.Vertical)
                return new SizeF(Indent + frame + cell.Width, HeightAt(Indent + frame + cell.Width));

            // A grid of so many columns asks for the room they need.
            if (Direction == LayoutDirection.Grid && Columns > 0 && _labels.Count > 0)
            {
                int n = Math.Min(Columns, _labels.Count);
                var columns = ColumnWidths(n);
                float width = Indent + frame + columns.Sum() + (n - 1) * GridGap;
                return new SizeF(width, HeightAt(width));
            }

            // As narrow as one box: a row too long for the menu goes on in the next one.
            return new SizeF(Indent + frame + cell.Width, cell.Height + frame);
        }

        public override void PostUpdateBounds(out float outHeight)
        {
            outHeight = HeightAt(Width);
        }

        public override void Layout()
        {
            var cells = Cells(Width);
            int count = cells.Length;

            _cells = new RectangleF[count];
            _boxes = new Rectangle[count];
            for (int i = 0; i < count; i++)
            {
                var at = cells[i];
                at.Offset(CanvasPivot.X, CanvasPivot.Y);
                _cells[i] = at;

                // In a column the box sits at the right, as a MenuCheckBox's does; in a row, before its label.
                float x = Direction == LayoutDirection.Vertical ? at.Right - _checkSize.Width : at.X;
                float y = at.Y + (at.Height - _checkSize.Height) / 2f;
                _boxes[i] = new Rectangle((int)x, (int)y, _checkSize.Width, _checkSize.Height);
            }
        }

        public override void Render(WidgetRenderArgs args)
        {
            var graphics = args.Canvas.Graphics;
            int alpha = GH_Canvas.ZoomFadeLow;
            if (Framed && _labels.Count > 0)
                DrawFrame(graphics, alpha);
            using (var pen = new Pen(Color.FromArgb(alpha, 0, 0, 0), 1.5f))
            using (var paper = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            using (var ink = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
            {
                for (int i = 0; i < _boxes.Length && i < _labels.Count; i++)
                {
                    var box = _boxes[i];
                    var size = LabelSize(_labels[i]);
                    float x = Direction == LayoutDirection.Vertical ? _cells[i].X : box.Right + _padding;
                    float y = _cells[i].Y + (_cells[i].Height - size.Height) / 2f;
                    graphics.DrawString(_labels[i], WidgetServer.Instance.TextFont, ink, new PointF(x, y));

                    if (Round)
                    {
                        var smoothing = graphics.SmoothingMode;
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        graphics.FillEllipse(paper, box);
                        graphics.DrawEllipse(pen, box);
                        if (_active[i])
                        {
                            float inset = _padding / 2f + 0.5f;
                            graphics.FillEllipse(ink, new RectangleF(box.X + inset, box.Y + inset, box.Width - 2 * inset, box.Height - 2 * inset));
                        }
                        graphics.SmoothingMode = smoothing;
                        continue;
                    }

                    graphics.FillRectangle(paper, box);
                    graphics.DrawRectangle(pen, box);
                    if (_active[i])
                    {
                        int inset = (int)(_padding / 2.0);
                        graphics.FillRectangle(ink, new Rectangle(box.X + inset, box.Y + inset, box.Width - 2 * inset, box.Height - 2 * inset));
                    }
                }
            }
        }

        /// <summary>
        /// The rounded frame round the boxes: a little lighter than the panel or frame the group is in,
        /// edged in the grey a menu's panel is edged in.
        /// </summary>
        private void DrawFrame(Graphics graphics, int alpha)
        {
            var frame = new RectangleF(CanvasPivot.X + Indent, CanvasPivot.Y, Width - Indent, Height);
            WidgetFrame.Draw(graphics, frame, WidgetFrame.Fill(this, alpha), alpha);
        }

        /// <summary>The box whose stretch of the menu holds <paramref name="point"/>, label included; -1 for none.</summary>
        private int At(PointF point)
        {
            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i].Contains(point)) return i;
            return -1;
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            _pressed = At(e.CanvasLocation);
            return _pressed >= 0 ? GH_ObjectResponse.Handled : GH_ObjectResponse.Ignore;
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            int i = At(e.CanvasLocation);
            if (i >= 0 && i == _pressed && !(Single && _active[i] && Ticked.Count() == 1))
            {
                if (Single) TickOnly(i);
                else _active[i] = !_active[i];
                ValueChanged?.Invoke(this, new EventArgs());
            }

            _pressed = -1;
            return GH_ObjectResponse.Release;
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            return GH_ObjectResponse.Capture;
        }

        public override bool Write(GH_IWriter writer)
        {
            var chunk = writer.CreateChunk("CheckBoxGroup", Index);
            chunk.SetInt32("Count", _active.Count);
            for (int i = 0; i < _active.Count; i++)
                chunk.SetBoolean("Active", i, _active[i]);
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            var chunk = reader.FindChunk("CheckBoxGroup", Index);
            if (chunk == null) return true;

            int count = Math.Min(_active.Count, chunk.GetInt32("Count"));
            for (int i = 0; i < count; i++)
                _active[i] = chunk.GetBoolean("Active", i);
            if (Single) Value = Value;   // one ticked, whatever the file says
            return true;
        }

        public override GH_Attr_Widget IsTtipPoint(PointF pt)
        {
            return At(pt) >= 0 ? this : null;
        }

        public override void TooltipSetup(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
        {
            int i = At(canvasPoint);
            e.Icon = null;
            e.Title = i >= 0 ? _tooltips[i] + " (Checkbox)" : _name;
            e.Text = _header;
            e.Description = i >= 0 ? (_active[i] ? "ON" : "OFF") : "";
        }
    }
}
