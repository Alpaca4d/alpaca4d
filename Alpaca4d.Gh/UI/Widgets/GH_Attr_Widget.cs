using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using System.Drawing;
using System.Windows.Forms;

namespace Alpaca4d.UIWidgets
{
    public abstract class GH_Attr_Widget
    {
        /// <summary>
        /// Global Variables
        /// </summary>
        protected RectangleF bounds;
        protected RectangleF canvasBounds;
        protected GH_Attr_Widget parent;
        protected PointF transformation;
        protected GH_PaletteStyle style;
        protected GH_Palette palette;
        protected int _index;
        protected bool _enabled = true;

        protected bool _visible = true;
        protected string _description;
        protected string _header;
        protected string _name;
        protected bool _showToolTip = true;
        protected PointF transfromation;
        public RectangleF CanvasBounds => canvasBounds;
        public RectangleF Bounds => bounds;
        /// <summary>
        /// Class Properties
        /// </summary>
        public virtual bool ShowToolTip
        {
            get
            {
                return _showToolTip;
            }
            set
            {
                _showToolTip = value;
            }
        }
        public virtual string Name
        {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
            }
        }
        public virtual string Desciption
        {
            get
            {
                return _description;
            }
            set
            {
                _description = value;
            }
        }
        public virtual string Header
        {
            get
            {
                return _header;
            }
            set
            {
                _header = value;
            }
        }
        public virtual GH_Attr_Widget Parent
        {
            get
            {
                return parent;
            }
            set
            {
                parent = value;
            }
        }
        public float Width
        {
            get
            {
                return bounds.Width;
            }
            set
            {
                bounds.Width = value;
                UpdateCanvasBounds();
            }
        }
        public float Height
        {
            get
            {
                return bounds.Height;
            }
            set
            {
                bounds.Height = value;
                UpdateCanvasBounds();
            }
        }
        public virtual int Index => _index;
        /// <summary>
        /// Whether the control takes part in the menu at all.
        ///
        /// Different from <see cref="Enabled"/>, which greys a control out but keeps its space: an
        /// invisible one is skipped by the layout, so the menu closes up around it and the
        /// component gets shorter. That is what lets one component offer a control that only some
        /// of its modes have any use for - a layer to read a stress at, when the result being read
        /// is not a stress - without the menu filling up with options that do nothing.
        ///
        /// Not saved. Visibility follows from the state of the other controls, and a component
        /// works it out again from those when it loads.
        /// </summary>
        public virtual bool Visible
        {
            get
            {
                return _visible;
            }
            set
            {
                _visible = value;
            }
        }

        public virtual bool Enabled
        {
            get
            {
                return _enabled;
            }
            set
            {
                _enabled = value;
            }
        }
        public virtual GH_MenuCollection TopCollection
        {
            get
            {
                GH_Attr_Widget gH_Attr_Widget = Parent.parent;
                while (!(gH_Attr_Widget is GH_ExtendableMenu) && gH_Attr_Widget.Parent != null)
                {
                    gH_Attr_Widget = gH_Attr_Widget.Parent;
                }
                if (gH_Attr_Widget != null)
                {
                    return ((GH_ExtendableMenu)gH_Attr_Widget).Collection;
                }
                return null;
            }
        }
        public GH_PaletteStyle Style
        {
            get
            {
                return style;
            }
            set
            {
                style = value;
            }
        }
        public GH_Palette Palette
        {
            get
            {
                return palette;
            }
            set
            {
                palette = value;
            }
        }
        public PointF Transform
        {
            get
            {
                return transformation;
            }
            set
            {
                transformation = value;
            }
        }
        public PointF CanvasPivot
        {
            get
            {
                return new PointF(CanvasBounds.X, CanvasBounds.Y);
            }
        }
        /// <summary>
        /// Class Methods
        /// </summary>
        public GH_Attr_Widget(int index, string id)
        {
            _index = index;
        }
        public static Rectangle Convert(RectangleF rect)
        {
            return new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        }
        public static RectangleF Shrink(RectangleF rect, float left, float right, float top, float bot)
        {
            return new RectangleF(rect.Left + left, rect.Top + top, rect.Width - (left + right), rect.Height - (top + bot));
        }
        public void OnRender(WidgetRenderArgs args)
        {
            Render(args);
        }
        public abstract void Render(WidgetRenderArgs args);
        public virtual bool Contains(PointF pt)
        {
            return CanvasBounds.Contains(pt);
        }
        public virtual GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            return 0;
        }
        public virtual GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            return 0;
        }
        public virtual GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            return 0;
        }
        public virtual GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            return 0;
        }
        public virtual GH_ObjectResponse RespondToKeyDown(GH_Canvas sender, KeyEventArgs e)
        {
            return 0;
        }
        public virtual bool Write(GH_IWriter writer)
        {
            return true;
        }

        public virtual bool Read(GH_IReader reader)
        {
            return true;
        }
        public abstract SizeF ComputeMinSize();
        public void UpdateBounds(PointF transform, float width)
        {
            Transform = transform;
            bounds.Width = width;
            UpdateCanvasBounds();
            PostUpdateBounds(out float outHeight);
            Height = outHeight;
        }
        private void UpdateCanvasBounds()
        {
            canvasBounds = new RectangleF(Transform.X, Transform.Y, bounds.Width, bounds.Height);
        }
        public virtual void PostUpdateBounds(out float outHeight)
        {
            outHeight = Height;
        }
        public virtual void Layout()
        {
        }
        public virtual GH_Attr_Widget IsTtipPoint(PointF pt)
        {
            return null;
        }
        public virtual void TooltipSetup(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
        {
        }
        public virtual string GetWidgetDescription()
        {
            return GetType().Name + " name" + Name + " index:" + Index;
        }
    }
}
