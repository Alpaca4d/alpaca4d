using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

using Alpaca4d.Optimisation;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// How Non-Linear Optimisation looks and answers on the canvas, after Galapagos: a capsule
    /// with a grip on the left for the variables and two underneath, for the objective and the
    /// constraints. Its links are drawn as dashed wires to what they point at, and when it is
    /// selected what it is linked to is boxed in the same colours.
    ///
    ///     ●  Variables          [icon]
    ///        Objective  Constraints
    ///            ●           ●
    /// </summary>
    internal sealed class NonLinearOptimisationAttributes : GH_Attributes<NonLinearOptimisation>
    {
        private enum Region { None, Variables, Objective, Constraints, Body }

        private const float Width = 150;
        private const float Row = 20;
        private const float StatusHeight = 14;

        private Region _tooltip = Region.None;
        private Guid _highlight = Guid.Empty;
        private RectangleF _box;
        private GH_Capsule _capsule;

        public NonLinearOptimisationAttributes(NonLinearOptimisation owner) : base(owner) { }

        #region Layout

        public RectangleF VariablesBox => new RectangleF(_box.X + 5, _box.Y, 90, Row);
        public RectangleF ObjectiveBox => new RectangleF(_box.X + 5, _box.Y + Row, 55, Row);
        public RectangleF ConstraintsBox => new RectangleF(_box.X + 62, _box.Y + Row, 62, Row);
        private RectangleF IconBox => new RectangleF(_box.Right - 30, _box.Y, 30, _box.Height);
        private RectangleF StatusBox => new RectangleF(_box.X - 20, _box.Y - StatusHeight - 3, _box.Width + 40, StatusHeight);

        public PointF VariablesGrip => new PointF(_box.X, _box.Y + 0.5f * Row);
        public PointF ObjectiveGrip => new PointF(ObjectiveBox.X + 0.5f * ObjectiveBox.Width, _box.Bottom);
        public PointF ConstraintsGrip => new PointF(ConstraintsBox.X + 0.5f * ConstraintsBox.Width, _box.Bottom);

        public override bool HasInputGrip => false;
        public override bool HasOutputGrip => false;

        protected override void Layout()
        {
            _box = new RectangleF(Pivot.X, Pivot.Y, Width, 2 * Row);
            var bounds = _box;
            bounds.Inflate(10, 10);
            bounds.Y -= StatusHeight;
            bounds.Height += StatusHeight;
            Bounds = bounds;
        }

        public override void ExpireLayout()
        {
            base.ExpireLayout();
            _capsule?.Dispose();
            _capsule = null;
        }

        public override bool IsPickRegion(PointF pt) => Bounds.Contains(pt);

        #endregion

        #region Colours

        // Alpaca4d's own palette (Alpaca4d.Colors): one brand colour per kind of link, the same in
        // the window's chart. Red is left out, as it is for the elements: it is the colour of
        // loads and of positive forces.
        internal static readonly Color VariableColour = Alpaca4d.Colors.AlpacaLightBlue;
        internal static readonly Color ObjectiveColour = Alpaca4d.Colors.AlpacaPurple;
        internal static readonly Color ConstraintColour = Alpaca4d.Colors.AlpacaOrange;

        // The body in Alpaca light green, the green of the logo's hindquarters, with dark-tech
        // labels (7.4 : 1). Grasshopper shows selection in green, which a green body would hide,
        // so a selected optimiser is lighter and outlined in dark tech instead. Orange while
        // running, as Explorer Inputs is while flying.
        private static readonly Color Body = Alpaca4d.Colors.AlpacaLightGreen;
        private static readonly Color BodyEdge = Color.FromArgb(105, 121, 29);
        private static readonly Color BodySelected = Color.FromArgb(198, 217, 110);
        private static readonly Color BodyText = Alpaca4d.Colors.DarkTech;
        private static readonly Color BodyTextFaint = Color.FromArgb(104, 118, 46);
        private static readonly Color SelectedEdge = Alpaca4d.Colors.DarkTech;

        private GH_PaletteStyle Style()
        {
            if (Owner.IsRunning)
                return new GH_PaletteStyle(Alpaca4d.Colors.AlpacaOrange, Selected ? SelectedEdge : Color.FromArgb(150, 70, 41), Alpaca4d.Colors.DarkTech);
            return Selected
                ? new GH_PaletteStyle(BodySelected, SelectedEdge, BodyText)
                : new GH_PaletteStyle(Body, BodyEdge, BodyText);
        }

        internal static Pen ThickWire(Color colour)
        {
            return new Pen(colour, 3f) { StartCap = LineCap.Round, CustomEndCap = WireArrow(), DashStyle = DashStyle.Dash };
        }

        private static Pen ThinWire(Color colour)
        {
            return new Pen(Color.FromArgb(60, colour), 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.RoundAnchor, DashStyle = DashStyle.Dash };
        }

        private static CustomLineCap WireArrow()
        {
            using (var path = new GraphicsPath())
            {
                path.AddLines(new[] { new PointF(-2, -2), new PointF(0, 0), new PointF(2, -2) });
                var cap = new CustomLineCap(null, path, LineCap.Flat);
                cap.SetStrokeCaps(LineCap.Round, LineCap.Round);
                return cap;
            }
        }

        #endregion

        #region Rendering

        protected override void PrepareForRender(GH_Canvas canvas)
        {
            if (_capsule != null) return;
            // The palette here only shapes the capsule; its colours come from Style() when it is drawn.
            _capsule = GH_Capsule.CreateCapsule(_box, GH_Palette.Black, 3, 6);
            _capsule.SetJaggedEdges(false, false);
            _capsule.AddInputGrip(VariablesGrip.Y);
            _capsule.AddInputGrip(ObjectiveGrip.X, ObjectiveGrip.Y);
            _capsule.AddInputGrip(ConstraintsGrip.X, ConstraintsGrip.Y);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Wires)
            {
                var document = Owner.OnPingDocument();
                if (document == null) return;
                if (Selected) RenderRemoteBoxes(graphics, document);
                RenderWires(graphics, document);
                return;
            }
            if (channel != GH_CanvasChannel.Objects || _capsule == null) return;

            var style = Style();
            _capsule.Render(graphics, style);
            // Selected: a 2 px outline, as the capsule's own edge is a hairline.
            if (Selected && _capsule.OutlineShape != null)
                using (var outline = new Pen(SelectedEdge, 2f))
                    graphics.DrawPath(outline, _capsule.OutlineShape);

            using (var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var text = new SolidBrush(style.Text))
            using (var faint = new SolidBrush(Owner.IsRunning ? style.Text : BodyTextFaint))
            {
                graphics.DrawString("Variables", GH_FontServer.StandardAdjusted, text, VariablesBox, format);
                graphics.DrawString("Objective", GH_FontServer.StandardAdjusted, text, ObjectiveBox, format);
                // dimmed until something is linked: most problems have no constraints
                graphics.DrawString("Constraints", GH_FontServer.StandardAdjusted, Owner.Constraints.Count > 0 ? text : faint, ConstraintsBox, format);
            }

            var icon = IconBox;
            int cx = (int)Math.Floor(0.5f * (icon.Left + icon.Right));
            int cy = (int)Math.Floor(0.5f * (icon.Top + icon.Bottom));
            graphics.DrawImage(Owner.Icon_24x24, new Rectangle(cx - 12, cy - 12, 24, 24));

            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                graphics.DrawString(Owner.Status, GH_FontServer.StandardItalic, Brushes.DimGray, StatusBox, format);
        }

        private void RenderRemoteBoxes(Graphics graphics, GH_Document document)
        {
            foreach (var id in Owner.Variables) RenderRemoteBox(graphics, document, id, VariableColour);
            RenderRemoteBox(graphics, document, Owner.Objective, ObjectiveColour);
            foreach (var id in Owner.Constraints) RenderRemoteBox(graphics, document, id, ConstraintColour);
        }

        private static void RenderRemoteBox(Graphics graphics, GH_Document document, Guid id, Color colour)
        {
            if (id == Guid.Empty) return;
            var obj = document.FindObject(id, true);
            if (obj == null) return;
            var box = GH_Convert.ToRectangle(obj.Attributes.Bounds);
            box.Inflate(3, 3);
            using (var fill = new SolidBrush(Color.FromArgb(70, colour)))
            using (var edge = new Pen(colour))
            {
                graphics.FillRectangle(fill, box);
                graphics.DrawRectangle(edge, box);
            }
        }

        private void RenderWires(Graphics graphics, GH_Document document)
        {
            using (var variable = Selected ? ThickWire(VariableColour) : ThinWire(VariableColour))
            using (var objective = Selected ? ThickWire(ObjectiveColour) : ThinWire(ObjectiveColour))
            using (var constraint = Selected ? ThickWire(ConstraintColour) : ThinWire(ConstraintColour))
            {
                foreach (var id in Owner.Variables)
                {
                    var obj = document.FindObject(id, true);
                    if (obj == null) continue;
                    var target = Target(obj);
                    using (var path = SourceWirePath(VariablesGrip, target)) graphics.DrawPath(variable, path);
                }
                RenderTargetWire(graphics, document, Owner.Objective, ObjectiveGrip, objective);
                foreach (var id in Owner.Constraints)
                    RenderTargetWire(graphics, document, id, ConstraintsGrip, constraint);
            }
        }

        private RectangleF Target(IGH_DocumentObject obj)
        {
            var box = obj.Attributes.Bounds;
            if (Selected) box.Inflate(5, 5);
            return box;
        }

        private void RenderTargetWire(Graphics graphics, GH_Document document, Guid id, PointF grip, Pen pen)
        {
            if (id == Guid.Empty) return;
            var obj = document.FindObject(id, false);
            if (obj == null) return;
            using (var path = obj.Attributes.IsTopLevel ? FloatingTargetWirePath(grip, Target(obj)) : LinkedTargetWirePath(grip, Target(obj)))
                graphics.DrawPath(pen, path);
        }

        /// <summary>From the variables grip, leaving to the left, to the nearest point of the target.</summary>
        internal static GraphicsPath SourceWirePath(PointF anchor, RectangleF target)
        {
            var end = GH_GraphicsUtil.BoxClosestPoint(anchor, target);
            float d = GH_GraphicsUtil.Distance(anchor, end);
            var path = new GraphicsPath();
            path.AddBezier(anchor, new PointF(anchor.X - 0.5f * d, anchor.Y), end, end);
            return path;
        }

        /// <summary>From a bottom grip, leaving downwards, to the top of a parameter on its own.</summary>
        internal static GraphicsPath FloatingTargetWirePath(PointF anchor, RectangleF target)
        {
            var end = new PointF(target.X + 0.5f * target.Width, target.Top);
            float d = Math.Max(5f, Math.Abs(anchor.Y - end.Y));
            var path = new GraphicsPath();
            path.AddBezier(anchor, new PointF(anchor.X, anchor.Y + d / 2), new PointF(end.X, end.Y - d / 2), end);
            return path;
        }

        /// <summary>From a bottom grip, leaving downwards, to the output of a component.</summary>
        internal static GraphicsPath LinkedTargetWirePath(PointF anchor, RectangleF target)
        {
            var end = new PointF(target.Right, target.Top + 0.5f * target.Height);
            float d = Math.Max(5f, Math.Abs(anchor.Y - end.Y));
            var path = new GraphicsPath();
            path.AddBezier(anchor, new PointF(anchor.X, anchor.Y + d / 2), new PointF(end.X + d / 2, end.Y), end);
            return path;
        }

        #endregion

        #region Mouse

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && !Owner.IsRunning)
            {
                if (GH_GraphicsUtil.Distance(e.CanvasLocation, VariablesGrip) <= 10)
                {
                    sender.ActiveInteraction = new VariableLinker(sender, e, Owner);
                    return GH_ObjectResponse.Handled;
                }
                if (GH_GraphicsUtil.Distance(e.CanvasLocation, ObjectiveGrip) <= 10)
                {
                    sender.ActiveInteraction = new ParameterLinker(sender, e, Owner, false);
                    return GH_ObjectResponse.Handled;
                }
                if (GH_GraphicsUtil.Distance(e.CanvasLocation, ConstraintsGrip) <= 10)
                {
                    sender.ActiveInteraction = new ParameterLinker(sender, e, Owner, true);
                    return GH_ObjectResponse.Handled;
                }
            }
            if (e.Button == MouseButtons.Right && !Owner.IsRunning)
            {
                if (VariablesBox.Contains(e.CanvasLocation))
                {
                    ShowVariablesMenu(e.ControlLocation);
                    return GH_ObjectResponse.Handled;
                }
                if (ConstraintsBox.Contains(e.CanvasLocation))
                {
                    ShowConstraintsMenu(e.ControlLocation);
                    return GH_ObjectResponse.Handled;
                }
                if (ObjectiveBox.Contains(e.CanvasLocation))
                {
                    ShowObjectiveMenu(e.ControlLocation);
                    return GH_ObjectResponse.Handled;
                }
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_box.Contains(e.CanvasLocation))
            {
                Owner.ShowWindow();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDoubleClick(sender, e);
        }

        #endregion

        #region Menus

        private void ShowVariablesMenu(Point at)
        {
            var document = Owner.OnPingDocument();
            if (document == null) return;
            var menu = NewMenu();

            var candidates = document.Objects.Where(OptimisationVariables.CanBe).ToList();
            var sliders = candidates.Where(o => o is GH_NumberSlider).ToList();
            var selected = candidates.Where(o => o.Attributes.Selected).ToList();
            var all = GH_DocumentObject.Menu_AppendItem(menu, "All Sliders", (s, e) => SetVariables(sliders), sliders.Count > 0);
            all.ToolTipText = "Link every number slider in the document";
            var some = GH_DocumentObject.Menu_AppendItem(menu, "Selected Sliders and Gene Pools", (s, e) => SetVariables(selected), selected.Count > 0);
            some.ToolTipText = "Link the number sliders and gene pools that are selected";
            GH_DocumentObject.Menu_AppendSeparator(menu);
            AppendRemoveMenu(menu, document, "Remove Variable", Owner.Variables, "Unlink every variable");
            menu.Show(Instances.ActiveCanvas, at);
        }

        private void ShowConstraintsMenu(Point at)
        {
            var document = Owner.OnPingDocument();
            if (document == null) return;
            var menu = NewMenu();
            AppendRemoveMenu(menu, document, "Remove Constraint", Owner.Constraints, "Unlink every constraint");
            menu.Show(Instances.ActiveCanvas, at);
        }

        private void ShowObjectiveMenu(Point at)
        {
            var menu = NewMenu();
            GH_DocumentObject.Menu_AppendItem(menu, "Unlink Objective", (s, e) =>
            {
                Owner.RecordUndoEvent("Unlink Objective");
                Owner.Objective = Guid.Empty;
                Changed();
            }, Owner.Objective != Guid.Empty);
            menu.Show(Instances.ActiveCanvas, at);
        }

        private ToolStripDropDownMenu NewMenu()
        {
            var menu = new ToolStripDropDownMenu { ShowItemToolTips = true, ShowImageMargin = true };
            menu.Closing += (s, e) =>
            {
                _highlight = Guid.Empty;
                Instances.ActiveCanvas.CanvasPostPaintObjects -= PaintHighlight;
                Instances.InvalidateCanvas();
            };
            Instances.ActiveCanvas.CanvasPostPaintObjects += PaintHighlight;
            return menu;
        }

        private void AppendRemoveMenu(ToolStripDropDownMenu menu, GH_Document document, string title, List<Guid> links, string removeAllTip)
        {
            var remove = GH_DocumentObject.Menu_AppendItem(menu, title);
            var removeAll = GH_DocumentObject.Menu_AppendItem(remove.DropDown, "Remove All", (s, e) =>
            {
                Owner.RecordUndoEvent(title);
                links.Clear();
                Changed();
            }, links.Count > 0);
            removeAll.ToolTipText = removeAllTip;
            foreach (var id in links.ToList())
            {
                var obj = document.FindObject(id, true);
                string name = obj == null ? "(deleted)" : obj is GH_NumberSlider slider ? slider.ImpliedNickName : obj is IGH_Param param ? OptimisationRun.Describe(param) : obj.NickName;
                var item = GH_DocumentObject.Menu_AppendItem(remove.DropDown, name, (s, e) =>
                {
                    Owner.RecordUndoEvent(title);
                    links.Remove(id);
                    Changed();
                });
                item.MouseEnter += (s, e) => { _highlight = id; Instances.InvalidateCanvas(); };
                item.MouseLeave += (s, e) => { _highlight = Guid.Empty; Instances.InvalidateCanvas(); };
            }
        }

        private void SetVariables(List<IGH_DocumentObject> objects)
        {
            Owner.RecordUndoEvent("Set Variables");
            Owner.Variables.Clear();
            Owner.Variables.AddRange(objects.Select(o => o.InstanceGuid));
            Changed();
        }

        private void Changed()
        {
            ExpireLayout();
            OptimisationWindow.RefreshFor(Owner);
            Instances.InvalidateCanvas();
        }

        private void PaintHighlight(GH_Canvas sender)
        {
            if (_highlight == Guid.Empty) return;
            var obj = Owner.OnPingDocument()?.FindObject(_highlight, true);
            if (obj == null) return;
            var box = GH_Convert.ToRectangle(obj.Attributes.Bounds);
            box.Inflate(3, 3);
            using (var pen = new Pen(Color.DarkRed, 3f))
                sender.Graphics.DrawRectangle(pen, box);
        }

        #endregion

        #region Tooltips

        public override bool TooltipEnabled => true;

        public override bool IsTooltipRegion(PointF pt)
        {
            _tooltip = VariablesBox.Contains(pt) ? Region.Variables
                     : ObjectiveBox.Contains(pt) ? Region.Objective
                     : ConstraintsBox.Contains(pt) ? Region.Constraints
                     : _box.Contains(pt) ? Region.Body
                     : Region.None;
            return _tooltip != Region.None;
        }

        public override void SetupTooltip(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
        {
            e.Icon = Owner.Icon_24x24;
            switch (_tooltip)
            {
                case Region.Variables:
                    e.Title = "Variables";
                    e.Text = "The number sliders, or Galapagos gene pools, the optimiser moves.";
                    e.Description =
                        "Drag from the grip on the left to a slider to link it; hold Shift to add one, Ctrl to remove one. " +
                        "Right-click for every slider in the document, or the selected ones.\n\n" +
                        "Each slider's bounds are the bounds of its variable, and the optimisation starts from where it is. " +
                        "A slider takes only the values its decimals allow, so give the local algorithms sliders with fine steps. " +
                        "When the run ends, the sliders are left on the best design, and Undo puts them back.";
                    break;
                case Region.Objective:
                    e.Title = "Objective";
                    e.Text = "The number to minimise or maximise.";
                    e.Description =
                        "Drag from the grip underneath to a parameter, or to the output of a component. " +
                        "It must hold one number for every design: link a sum or a largest value, not a list.";
                    break;
                case Region.Constraints:
                    e.Title = "Constraints";
                    e.Text = "Numbers that must end up less than or equal to zero.";
                    e.Description =
                        "Drag from the grip underneath to parameters; hold Shift to add one, Ctrl to remove one.\n\n" +
                        "Every number they hold is one constraint, met when it is <= 0: write a limit as value - limit, " +
                        "so a utilisation of at most 1 is utilisation - 1, and a list of utilisations is one constraint per member. " +
                        "Ratios such as deflection / limit - 1 keep constraints of different units comparable.\n\n" +
                        "Every design must give the same number of values. Only COBYLA and ISRES take constraints.";
                    break;
                case Region.Body:
                    e.Title = "Non-Linear Optimisation";
                    e.Text = "Double-click to choose the algorithm and run.";
                    e.Description = $"{AlgorithmInfo.Of(Owner.Settings.Algorithm).Label}, {(Owner.Settings.Maximise ? "maximising" : "minimising")}. " +
                                    "NLopt's derivative-free algorithms, ported to C# so they run on Windows and on Mac.";
                    break;
            }
        }

        #endregion
    }
}
