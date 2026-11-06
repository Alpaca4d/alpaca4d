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
    /// short names wants: six beam forces in one line rather than six.
    ///
    /// Saved as a chunk of its own, so its boxes cannot be confused with the plain checkboxes of the
    /// panel it is in. A file saved before a box was added keeps that box's default.
    /// </summary>
    public class MenuCheckBoxGroup : GH_Attr_Widget
    {
        public enum LayoutDirection
        {
            Vertical,
            Horizontal
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

        /// <summary>How many boxes go in one row, at the width the group has been given.</summary>
        private int PerRow(float width)
        {
            if (Direction == LayoutDirection.Vertical || _labels.Count == 0) return 1;

            float cell = CellSize().Width;
            int fit = (int)Math.Floor((width + Space) / (cell + Space));
            return Math.Max(1, Math.Min(_labels.Count, fit));
        }

        private float HeightAt(float width)
        {
            if (_labels.Count == 0) return 0f;

            int rows = (int)Math.Ceiling(_labels.Count / (double)PerRow(width));
            return rows * CellSize().Height + (rows - 1) * Space;
        }

        public override SizeF ComputeMinSize()
        {
            var cell = CellSize();
            if (Direction == LayoutDirection.Vertical)
                return new SizeF(cell.Width, HeightAt(cell.Width));

            // As narrow as one box: a row too long for the menu goes on in the next one.
            return new SizeF(cell.Width, cell.Height);
        }

        public override void PostUpdateBounds(out float outHeight)
        {
            outHeight = HeightAt(Width);
        }

        public override void Layout()
        {
            int count = _labels.Count;
            int perRow = PerRow(Width);
            var cell = CellSize();
            float cellWidth = Direction == LayoutDirection.Vertical ? Width : (Width - (perRow - 1) * Space) / perRow;

            _cells = new RectangleF[count];
            _boxes = new Rectangle[count];
            for (int i = 0; i < count; i++)
            {
                int row = i / perRow, column = i % perRow;
                var at = new RectangleF(CanvasPivot.X + column * (cellWidth + Space), CanvasPivot.Y + row * (cell.Height + Space), cellWidth, cell.Height);
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
            if (i >= 0 && i == _pressed)
            {
                _active[i] = !_active[i];
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
