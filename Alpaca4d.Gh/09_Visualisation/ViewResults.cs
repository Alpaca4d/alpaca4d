using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Result;
using Alpaca4d.UI;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// One component for looking at results, in place of one per kind of result.
    ///
    /// The five view components it stands in for each read one thing and draw it their own way, so
    /// a model with beams, shells and solids in it needs three of them side by side, three colour
    /// gradients, three ranges, and no way to say "show me only this part". What is actually wanted
    /// is nearly always one question at a time - this quantity, on these elements, at this step -
    /// and that is one component with a dropdown, not five wired in parallel.
    ///
    /// Two things it does that none of them do. Elements can be filtered in the same language the
    /// numerical components already use - an ElementId, a wildcard over them, a tag, a regular
    /// expression - and what is filtered out is ghosted rather than hidden, so the part being looked
    /// at stays in the model it belongs to. And the deformed shape is a toggle over any of it rather
    /// than a component of its own, so a stress can be read on the shape it belongs to.
    ///
    /// WIP, and named so: it is meant to grow into the replacement for the others, and until it has
    /// been used in anger on real models they should stay where they are.
    /// </summary>
    public class ViewResults : GH_ExtendableComponent
    {
        private Alpaca4d.Model _model;
        private ResultField _field;
        private SortedDictionary<double, Color> _gradient;
        private double _min, _max;

        /// <summary>The length one unit of beam force is drawn at; see <see cref="DiagramUnit"/>.</summary>
        private double _diagramUnit;

        // What gets drawn, worked out in SolveInstance and held for the viewport.
        private readonly List<Mesh> _shaded = new List<Mesh>();
        private readonly List<Mesh> _ghostMeshes = new List<Mesh>();
        private readonly List<Curve> _ghostCurves = new List<Curve>();
        private readonly List<Tuple<Curve, Color>> _curves = new List<Tuple<Curve, Color>>();
        private readonly List<Mesh> _diagrams = new List<Mesh>();
        private readonly List<Tuple<Line, Color>> _arrows = new List<Tuple<Line, Color>>();

        // How far it stands off towards the camera rides with each label, so a value on an extruded
        // beam is not drawn inside the section it belongs to.
        private readonly List<Tuple<Point3d, string, double>> _labels = new List<Tuple<Point3d, string, double>>();

        // Beams drawn solid. The coloured one has a ring at every colour stop, so its edges are
        // drawn from a second one with a ring at each end only - the stops would bury them in lines.
        private readonly List<Mesh> _beamSolids = new List<Mesh>();
        private readonly List<Mesh> _beamEdges = new List<Mesh>();

        // A section's outline, flattened once per solve rather than once per beam that uses it.
        private readonly Dictionary<IUniaxialSection, List<Polyline>> _outlines = new Dictionary<IUniaxialSection, List<Polyline>>();

        private MenuDropDown _ddFamily;
        private MenuDropDown _ddComponent;
        private MenuDropDown _ddLayer;
        private MenuDropDown _ddReaction;
        private MenuDropDown _ddAxes;
        private MenuCheckBox _ckDeformed;
        private MenuSlider _slDeformScale;
        private MenuCheckBox _ckAnimate;
        private MenuCheckBox _ckExtruded;
        private MenuSlider _slDiagramScale;
        private MenuCheckBox _ckWires;
        private MenuCheckBox _ckValues;
        private MenuSlider _slTextSize;

        // A caption is hidden with the control it names, so both are held.
        private MenuStaticText _txLayer;
        private MenuStaticText _txReaction;
        private MenuStaticText _txAxes;
        private MenuStaticText _txDeformScale;
        private MenuStaticText _txDiagramScale;
        private MenuStaticText _txTextSize;

        /// <summary>The colour everything outside the filter is drawn in.</summary>
        private static readonly Color GhostColour = Color.FromArgb(150, 150, 150);

        public ViewResults()
          : base("View Results_WIP (Alpaca4d)", "View Results_WIP",
            "Draw any result on the model: displacements, beam force diagrams, beam stresses, shell " +
            "forces, shell stresses, solid stresses, reactions.\n" +
            "Pick the result and the component from the dropdowns. ElementId shows only some of the " +
            "elements and greys the rest, so what you are looking at stays in the model around it.\n" +
            "Work in progress, and meant to grow into the replacement for the separate view components.",
            "Alpaca4d", "09_Visualisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to draw, or which mode after a natural vibration analysis.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.Filter + "\nWhatever is left out is greyed rather than hidden.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colors", "Colors", "Gradient to colour with, from the low end to the high. Connect the Colors component for a ready-made one.\nBeam force diagrams ignore it and use the colour that belongs to the force.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntervalParameter("Range", "Range", "Range the gradient is stretched over. Left empty it fits what is drawn, which is what makes two steps impossible to compare - set it to compare them.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_StringParam("Info", "Info", "What is drawn, and the range it spans.");
            pManager.Register_DoubleParam("Values", "Values", "Every value drawn. Feed it to the Data input of Legend.");
            pManager.Register_ColourParam("Colors", "Colors", "The gradient used. Feed it to the Colors input of Legend, so the legend and the model agree.");
        }

        #region UI

        /// <summary>A caption above a slider, because a slider on its own shows a number and no name.</summary>
        private static MenuStaticText Caption(string text)
        {
            return new MenuStaticText { Text = text };
        }

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            // Only the menus that hold controls of their own. A menu carrying nothing but an input
            // plug reads as a heading that does nothing when it is collapsed, which is exactly what
            // the first cut of this component did with its Colour menu - so Colors, Range, Step and
            // ElementId are ordinary inputs on the body, where a Grasshopper user looks for inputs.
            var resultMenu = new GH_ExtendableMenu(0, "Result") { Name = "Result", Header = "What to draw" };
            var resultPanel = new MenuPanel(0, "result_panel");

            _ddFamily = new MenuDropDown(0, "Family", "Family") { VisibleItemCount = 6 };
            foreach (var family in ResultField.MenuOrder)
                _ddFamily.AddItem(ResultField.FamilyNames[(int)family], ResultField.FamilyNames[(int)family]);
            _ddFamily.ValueChanged += OnFamilyChanged;

            _ddComponent = new MenuDropDown(1, "Component", "Component") { VisibleItemCount = 8 };
            _ddComponent.ValueChanged += OnWidgetChanged;

            _ddLayer = new MenuDropDown(2, "Layer", "Layer") { VisibleItemCount = 3 };
            foreach (var name in Alpaca4d.Result.Read.LayerNames)
                _ddLayer.AddItem(name, name);
            _ddLayer.ValueChanged += OnWidgetChanged;

            _ddReaction = new MenuDropDown(3, "ReactionStyle", "ReactionStyle") { VisibleItemCount = 3 };
            foreach (var name in ResultField.ReactionStyles)
                _ddReaction.AddItem(name, name);
            _ddReaction.ValueChanged += OnWidgetChanged;

            _ddAxes = new MenuDropDown(4, "Axes", "Axes") { VisibleItemCount = 2 };
            foreach (var name in ResultField.StressAxes)
                _ddAxes.AddItem(name, name);
            _ddAxes.ValueChanged += OnWidgetChanged;

            _txLayer = Caption("Layer through the thickness");
            _txReaction = Caption("Reactions drawn as");
            _txAxes = Caption("Read in which axes");

            resultPanel.AddControl(Caption("Result"));
            resultPanel.AddControl(_ddFamily);
            resultPanel.AddControl(Caption("Component"));
            resultPanel.AddControl(_ddComponent);
            resultPanel.AddControl(_txLayer);
            resultPanel.AddControl(_ddLayer);
            resultPanel.AddControl(_txReaction);
            resultPanel.AddControl(_ddReaction);
            resultPanel.AddControl(_txAxes);
            resultPanel.AddControl(_ddAxes);
            resultMenu.AddControl(resultPanel);
            resultMenu.Expand();
            attr.AddMenu(resultMenu);

            var displayMenu = new GH_ExtendableMenu(1, "Display") { Name = "Display", Header = "How to draw it" };
            var displayPanel = new MenuPanel(1, "display_panel");

            _ckDeformed = new MenuCheckBox(0, "Deformed", "Deformed shape");
            _ckDeformed.ValueChanged += OnWidgetChanged;

            _slDeformScale = new MenuSlider(0, "DeformScale", 0.0, 200.0, 1.0, 1);
            _slDeformScale.ValueChanged += OnWidgetChanged;

            _ckAnimate = new MenuCheckBox(1, "Animate", AnimateLabel);
            _ckAnimate.ValueChanged += OnAnimateChanged;

            _ckExtruded = new MenuCheckBox(4, "Extruded", "Extruded beams");
            _ckExtruded.ValueChanged += OnWidgetChanged;

            _slDiagramScale = new MenuSlider(1, "DiagramScale", 0.0, 20.0, 1.0, 2);
            _slDiagramScale.ValueChanged += OnWidgetChanged;

            _ckWires = new MenuCheckBox(2, "Wires", "Element edges") { Active = true };
            _ckWires.ValueChanged += OnWidgetChanged;

            _ckValues = new MenuCheckBox(3, "Values", "Show values");
            _ckValues.ValueChanged += OnWidgetChanged;

            _slTextSize = new MenuSlider(2, "TextSize", 0.05, 5.0, 0.5, 2);
            _slTextSize.ValueChanged += OnWidgetChanged;

            _txDeformScale = Caption("Deformation scale");
            _txDiagramScale = Caption("Diagram and arrow scale");
            _txTextSize = Caption("Value text size");

            displayPanel.AddControl(_ckDeformed);
            displayPanel.AddControl(_txDeformScale);
            displayPanel.AddControl(_slDeformScale);
            displayPanel.AddControl(_ckAnimate);
            displayPanel.AddControl(_ckExtruded);
            displayPanel.AddControl(_txDiagramScale);
            displayPanel.AddControl(_slDiagramScale);
            displayPanel.AddControl(_ckWires);
            displayPanel.AddControl(_ckValues);
            displayPanel.AddControl(_txTextSize);
            displayPanel.AddControl(_slTextSize);
            displayMenu.AddControl(displayPanel);
            attr.AddMenu(displayMenu);

            attr.MinWidth = 230f;

            SyncComponentList();
            UpdateVisibility();
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            if (_ddFamily == null) return;

            _ddFamily.ValueChanged += OnFamilyChanged;
            _ddComponent.ValueChanged += OnWidgetChanged;
            _ddLayer.ValueChanged += OnWidgetChanged;
            _ddReaction.ValueChanged += OnWidgetChanged;
            _ddAxes.ValueChanged += OnWidgetChanged;
            _ckDeformed.ValueChanged += OnWidgetChanged;
            _slDeformScale.ValueChanged += OnWidgetChanged;
            _ckAnimate.ValueChanged += OnAnimateChanged;
            _ckExtruded.ValueChanged += OnWidgetChanged;
            _slDiagramScale.ValueChanged += OnWidgetChanged;
            _ckWires.ValueChanged += OnWidgetChanged;
            _ckValues.ValueChanged += OnWidgetChanged;
            _slTextSize.ValueChanged += OnWidgetChanged;

            // Saved before the menu had an order of its own, the Result dropdown's position was
            // the family's place in the enum. Turned into its place in the menu, before the
            // Component list below is rebuilt for it.
            if (_savedInEnumOrder)
            {
                var families = (ResultFamily[])Enum.GetValues(typeof(ResultFamily));
                int saved = _ddFamily.Value;
                if (saved >= 0 && saved < families.Length)
                    _ddFamily.Value = Array.IndexOf(ResultField.MenuOrder, families[saved]);
                _savedInEnumOrder = false;
            }

            // A file never reopens mid-animation: the checkbox is saved, the timer is not.
            _ckAnimate.Active = false;
            _ckAnimate.Tag = AnimateLabel;

            // The dropdown remembers which entry was chosen but not what the entries were, and the
            // Component list depends on the family - so it is rebuilt before the saved index is
            // applied to it. Visibility is not saved either: it follows from the rest.
            SyncComponentList();
            UpdateVisibility();
        }

        private void OnWidgetChanged(object sender, EventArgs e)
        {
            UpdateVisibility();
            ExpireSolution(true);
        }

        private void OnFamilyChanged(object sender, EventArgs e)
        {
            SyncComponentList();
            UpdateVisibility();
            ExpireSolution(true);
        }

        /// <summary>
        /// Puts away every control the current result has no use for.
        ///
        /// One component covering six kinds of result has more knobs than any one of them needs,
        /// and a menu that shows all of them at once asks the reader to work out which ones are
        /// live - a layer to read a stress at, when the result is a reaction. Hidden rather than
        /// greyed, so the menu closes up and the component is as short as the question is.
        /// </summary>
        private void UpdateVisibility()
        {
            if (_ddLayer == null) return;

            var family = Family;

            Show(ResultField.HasLayers(family), _txLayer, _ddLayer);
            Show(ResultField.HasAxes(family), _txAxes, _ddAxes);
            Show(ResultField.HasReactionStyle(family), _txReaction, _ddReaction);
            Show(ResultField.HasScale(family), _txDiagramScale, _slDiagramScale);

            // A scale and an animation over a shape that is not being deformed do nothing.
            bool deformed = _ckDeformed?.Active ?? false;
            Show(deformed, _txDeformScale, _slDeformScale, _ckAnimate);

            Show(_ckValues?.Active ?? false, _txTextSize, _slTextSize);

            // Nothing is animating a shape that is no longer being deformed.
            if (!deformed && (_ckAnimate?.Active ?? false))
                StopAnimation();

            Attributes?.ExpireLayout();
            Grasshopper.Instances.ActiveCanvas?.Refresh();
        }

        /// <summary>
        /// An invisible dropdown keeps whatever bounds it last had, and an expanded one would go on
        /// drawing its list over the canvas, so it is closed on the way out.
        /// </summary>
        private static void Show(bool visible, params GH_Attr_Widget[] widgets)
        {
            foreach (var widget in widgets)
            {
                if (widget == null) continue;

                widget.Visible = visible;

                var dropdown = widget as MenuDropDown;
                if (!visible && dropdown != null) dropdown.expanded = false;
            }
        }

        /// <summary>
        /// Rebuilds the Component dropdown for the family in force, keeping the selection where the
        /// new list is long enough to hold it.
        /// </summary>
        private void SyncComponentList()
        {
            if (_ddComponent == null) return;

            var names = ResultField.ComponentNames(Family);
            int wanted = _ddComponent.Value;

            _ddComponent.Items.Clear();
            foreach (var name in names)
                _ddComponent.AddItem(name, name);

            _ddComponent.Value = wanted < names.Length ? wanted : 0;
        }

        private ResultFamily Family
        {
            get
            {
                int value = _ddFamily?.Value ?? 0;
                var families = ResultField.MenuOrder;
                return value >= 0 && value < families.Length ? families[value] : ResultFamily.Displacement;
            }
        }

        /// <summary>
        /// Marks a saved file as holding the Result dropdown in menu order. A file without it was
        /// saved when the dropdown listed the families in enum order, and is converted on load.
        /// </summary>
        private const string MenuOrderKey = "ResultMenuOrder";

        private bool _savedInEnumOrder;

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetInt32(MenuOrderKey, 1);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            // Before base.Read, which reads the dropdown and then calls OnComponentLoaded.
            _savedInEnumOrder = !reader.ItemExists(MenuOrderKey);
            return base.Read(reader);
        }

        #endregion

        #region Animation

        private const string AnimateLabel = "Animate";
        private const string StopLabel = "Stop animation";

        /// <summary>How many stops the deformation scale makes on its way across and back.</summary>
        private const int AnimationSteps = 20;

        private System.Windows.Forms.Timer _timer;
        private int _frame;

        private void OnAnimateChanged(object sender, EventArgs e)
        {
            if (_ckAnimate.Active) StartAnimation();
            else StopAnimation();

            UpdateVisibility();
        }

        private void StartAnimation()
        {
            _ckAnimate.Tag = StopLabel;

            // Nothing to watch otherwise - the scale would sweep over a shape that is not moving.
            if (_ckDeformed != null) _ckDeformed.Active = true;

            _frame = 0;

            if (_timer == null)
            {
                // A WinForms timer ticks on the UI thread, which is the only thread a Grasshopper
                // solution may be expired from.
                _timer = new System.Windows.Forms.Timer { Interval = 60 };
                _timer.Tick += OnTick;
            }

            _timer.Start();
        }

        private void StopAnimation()
        {
            _timer?.Stop();

            if (_ckAnimate != null)
            {
                _ckAnimate.Active = false;
                _ckAnimate.Tag = AnimateLabel;
            }

            Grasshopper.Instances.ActiveCanvas?.Invalidate();
        }

        /// <summary>
        /// One frame: the scale walks from the slider's own minimum to its maximum and back, so
        /// setting the slider to 0-100 sweeps 0 to 100 to 0. The slider moves with it, which is
        /// what makes it obvious where in the sweep the picture is.
        /// </summary>
        private void OnTick(object sender, EventArgs e)
        {
            if (_slDeformScale == null) { StopAnimation(); return; }

            _frame = (_frame + 1) % (2 * AnimationSteps);

            // Out on the first half, back on the second.
            int position = _frame <= AnimationSteps ? _frame : 2 * AnimationSteps - _frame;

            double low = _slDeformScale.MinValue;
            double high = _slDeformScale.MaxValue;
            _slDeformScale.Value = low + (high - low) * position / (double)AnimationSteps;

            ExpireSolution(true);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopAnimation();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                StopAnimation();

            base.DocumentContextChanged(document, context);
        }

        #endregion

        #region Solving

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            Clear();
        }

        private void Clear()
        {
            _shaded.Clear();
            _ghostMeshes.Clear();
            _ghostCurves.Clear();
            _curves.Clear();
            _diagrams.Clear();
            _arrows.Clear();
            _labels.Clear();
            _beamSolids.Clear();
            _beamEdges.Clear();
            _outlines.Clear();
        }

        // Animating re-solves twenty times a second, and every solve would otherwise reopen the
        // recorder file twice. Only the scale changes between frames, so the two reads are kept
        // against the question that produced them.
        private string _cacheKey;
        private ResultField _cachedField;
        private Dictionary<int, Vector3d> _cachedDisplacement;

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _model = null;
            _field = null;
            Clear();

            if (!DA.GetData(0, ref _model) || _model == null) return;

            // Needs a license whatever the model. Refused mid-animation, the timer would go on
            // re-solving twenty times a second only to be refused each time, so it stops too.
            if (!LicenseGate.Allows(this, "View Results"))
            {
                _model = null;
                StopAnimation();
                return;
            }

            int step = 0;
            DA.GetData(1, ref step);

            var filter = ElementFilterInput.Read(DA, 2, this);

            var palette = new List<Color>();
            if (!DA.GetDataList(3, palette) || palette.Count < 2)
                palette = Alpaca4d.Colors.Gradient(0);

            var family = Family;
            int component = _ddComponent?.Value ?? 0;
            int layer = _ddLayer?.Value ?? 0;
            bool localAxes = (_ddAxes?.Value ?? 0) == 1;

            var key = $"{_model.GetHashCode()}|{family}|{component}|{layer}|{step}|{localAxes}";
            if (key != _cacheKey)
            {
                try
                {
                    _cachedField = ResultField.Read(_model, family, component, layer, step, localAxes);
                    _cachedDisplacement = NodalOffsets(step);
                }
                catch (Exception ex)
                {
                    _cacheKey = null;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    return;
                }

                _cacheKey = key;
            }

            _field = _cachedField;

            // The range is fitted to what is on screen, not to the whole model: a filter narrowed
            // to one beam is asking about that beam, and a gradient stretched over a maximum
            // somewhere else would paint it all one colour.
            var shown = ShownValues(filter).ToList();

            var range = new Interval();
            if (DA.GetData(4, ref range))
            {
                _min = range.Min;
                _max = range.Max;
            }
            else
            {
                _min = shown.Count > 0 ? shown.Min() : 0.0;
                _max = shown.Count > 0 ? shown.Max() : 0.0;
            }

            _gradient = BuildGradient(palette, _min, _max);

            Dictionary<int, Vector3d> displacement = null;
            if (_ckDeformed?.Active ?? false)
            {
                double scale = _slDeformScale?.Value ?? 1.0;
                displacement = _cachedDisplacement.ToDictionary(x => x.Key, x => x.Value * scale);
            }

            Build(filter, family, component, displacement);

            DA.SetData(0, $"{ResultField.FamilyNames[(int)family]} - {_field.Label}, " +
                          $"step {step}, min {_min:G4}, max {_max:G4}");
            DA.SetDataList(1, shown);
            DA.SetDataList(2, palette);

            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        /// <summary>Displacement at every node, at true size. Scaled when it is used, not here.</summary>
        private Dictionary<int, Vector3d> NodalOffsets(int step)
        {
            var offsets = new Dictionary<int, Vector3d>();

            foreach (var entry in _model.NodalDisplacements(step))
            {
                if (entry.Key.HasValue)
                    offsets[entry.Key.Value] = entry.Value;
            }

            return offsets;
        }

        /// <summary>The values on the elements that passed the filter, which is what the range fits.</summary>
        private IEnumerable<double> ShownValues(ElementFilter filter)
        {
            if (filter.MatchesEverything || _field.Reactions != null)
                return _field.Values;

            if (_field.ByNode != null)
            {
                // A node has no identifier of its own, so it is shown when an element that reaches
                // it is shown. Anything else would filter the displacement field by nothing.
                var nodes = new HashSet<int>();
                foreach (var element in _model.Elements.Where(filter.Matches))
                    foreach (var tag in NodesOf(element))
                        nodes.Add(tag);

                return _field.ByNode.Where(entry => nodes.Contains(entry.Key)).Select(entry => entry.Value);
            }

            var kept = new HashSet<int>(_model.Elements.Where(filter.Matches)
                                                      .Where(x => x.Id.HasValue)
                                                      .Select(x => x.Id.Value));

            return _field.ByElement.Where(entry => kept.Contains(entry.Key)).SelectMany(entry => entry.Value);
        }

        private static IEnumerable<int> NodesOf(IElement element)
        {
            var beam = element as IBeam;
            if (beam != null)
            {
                if (beam.INode.HasValue) yield return beam.INode.Value;
                if (beam.JNode.HasValue) yield return beam.JNode.Value;
                yield break;
            }

            var shell = element as IShell;
            if (shell != null && shell.IndexNodes != null)
            {
                foreach (var tag in shell.IndexNodes.Where(x => x.HasValue)) yield return tag.Value;
                yield break;
            }

            var brick = element as IBrick;
            if (brick != null && brick.IndexNodes != null)
            {
                foreach (var tag in brick.IndexNodes.Where(x => x.HasValue)) yield return tag.Value;
            }
        }

        private static SortedDictionary<double, Color> BuildGradient(List<Color> palette, double min, double max)
        {
            var gradient = new SortedDictionary<double, Color>();

            // A flat field - every value the same, which an unloaded component gives - would
            // otherwise divide by zero and colour nothing.
            double span = max - min;
            double step = span > 0.0 ? span / (palette.Count - 1) : 1.0;
            double at = min;

            foreach (var colour in palette)
            {
                if (!gradient.ContainsKey(at))
                {
                    gradient.Add(at, colour);
                    at += step;
                }
            }

            return gradient;
        }

        private Point3d Move(Point3d point, int? node, Dictionary<int, Vector3d> displacement)
        {
            Vector3d offset;
            if (displacement != null && node.HasValue && displacement.TryGetValue(node.Value, out offset))
                return point + offset;

            return point;
        }

        #endregion

        #region Building what gets drawn

        /// <summary>
        /// Sorts every element into one of three piles: drawn in colour because it passed the
        /// filter and the result has a value for it, drawn plain because it passed but this result
        /// says nothing about it, or ghosted.
        ///
        /// Ghosting is not optional. The whole point of filtering here rather than upstream is that
        /// the part being read stays in the model around it, and a filter that simply hid the rest
        /// would be the same as wiring a smaller model in.
        /// </summary>
        private void Build(ElementFilter filter, ResultFamily family, int component,
                           Dictionary<int, Vector3d> displacement)
        {
            if (family == ResultFamily.BeamForce)
                _diagramUnit = DiagramUnit(filter);

            foreach (var beam in _model.Beams)
                BuildBeam(beam, filter.Matches(beam), family, component, displacement);

            foreach (var shell in _model.Shells)
                BuildFace(shell.Mesh, shell.Id, shell.IndexNodes, filter.Matches(shell), family, displacement,
                          family == ResultFamily.ShellForce || family == ResultFamily.ShellStress,
                          midEdge: shell.ElementClass == Alpaca4d.Element.ElementClass.ASDShellT3);

            foreach (var brick in _model.Bricks)
                BuildFace(brick.Mesh, brick.Id, brick.IndexNodes, filter.Matches(brick), family, displacement,
                          family == ResultFamily.BrickStress);

            if (family == ResultFamily.Reaction)
                BuildReactions(component);
        }

        private void BuildBeam(IBeam beam, bool kept, ResultFamily family, int component,
                               Dictionary<int, Vector3d> displacement)
        {
            var start = Move(beam.Curve.PointAtStart, beam.INode, displacement);
            var end = Move(beam.Curve.PointAtEnd, beam.JNode, displacement);
            var line = new LineCurve(start, end);

            if (!kept)
            {
                var ghosts = Rings(beam, start, end, new[] { 0.0, 1.0 });
                if (ghosts == null)
                {
                    _ghostCurves.Add(line);
                    return;
                }

                // Ghosts are drawn shaded rather than in false colour, and shading needs normals.
                foreach (var stack in ghosts)
                {
                    var ghost = Alpaca4d.Utils.CreateLoft(stack);
                    ghost.Normals.ComputeNormals();
                    _ghostMeshes.Add(ghost);
                }
                return;
            }

            double lift = Lift(beam);

            if (family == ResultFamily.Displacement && _field.ByNode != null)
            {
                // Two nodes and a colour at each, so the beam is drawn as a handful of segments
                // with the colour walked between them. Anything less and a beam reads as one flat
                // colour while the shells beside it are graded.
                double from = ValueAtNode(beam.INode);
                double to = ValueAtNode(beam.JNode);
                const int steps = 8;

                var samples = Enumerable.Range(0, steps + 1)
                                        .Select(i => Tuple.Create(i / (double)steps, from + (to - from) * i / steps))
                                        .ToList();
                Paint(beam, start, end, samples);

                Label((start + end) * 0.5, Math.Max(from, to), lift);
                return;
            }

            if (family == ResultFamily.BeamForce)
            {
                Plain(beam, start, end, line);

                List<double> forces;
                if (beam.Id.HasValue && _field.ByElement != null && _field.ByElement.TryGetValue(beam.Id.Value, out forces))
                {
                    var diagram = Diagram(beam, start, end, forces, component, _diagramUnit);
                    if (diagram != null) _diagrams.Add(diagram);

                    // One label per beam, at whichever station carries the most. Every station
                    // labelled is unreadable on anything but a single member.
                    if (forces != null && forces.Count > 0)
                    {
                        int worst = 0;
                        for (int i = 1; i < forces.Count; i++)
                            if (Math.Abs(forces[i]) > Math.Abs(forces[worst])) worst = i;

                        // At the tip of the diagram, where the value is drawn, not on the beam.
                        var at = start + (end - start) * Stations(beam, forces.Count)[worst];
                        Label(at + DiagramDirection(beam, start, end, component) * forces[worst] * _diagramUnit,
                              forces[worst]);
                    }
                }

                return;
            }

            if (family == ResultFamily.BeamStress)
            {
                List<double> stresses;
                if (beam.Id.HasValue && _field.ByElement != null && _field.ByElement.TryGetValue(beam.Id.Value, out stresses)
                    && stresses != null && stresses.Count > 0)
                {
                    BuildBeamField(beam, start, end, stresses, lift);
                    return;
                }
            }

            // A beam under a shell or solid result has no value of its own. Drawn plain rather
            // than coloured, because colouring it would mean picking a number it does not have.
            Plain(beam, start, end, line);
        }

        /// <summary>A beam with no value to show: a grey line, or the same light grey as a face with none.</summary>
        private void Plain(IBeam beam, Point3d start, Point3d end, LineCurve line)
        {
            var grey = Color.FromArgb(210, 210, 210);
            if (!Extrude(beam, start, end, new[] { 0.0, 1.0 }, new[] { grey, grey }))
                _curves.Add(Tuple.Create((Curve)line, Color.DimGray));
        }

        /// <summary>
        /// A value painted along a beam, given at points from 0 at its start to 1 at its end. Drawn
        /// as short lines each in the colour of its middle, or with Extruded beams on, as the beam's
        /// section carried along it with a ring at every point in the colour there.
        /// </summary>
        private void Paint(IBeam beam, Point3d start, Point3d end, List<Tuple<double, double>> samples)
        {
            if (Extrude(beam, start, end, samples.Select(x => x.Item1).ToList(),
                        samples.Select(x => Colour(x.Item2)).ToList()))
                return;

            var axis = end - start;
            for (int i = 0; i < samples.Count - 1; i++)
            {
                var segment = new LineCurve(start + axis * samples[i].Item1, start + axis * samples[i + 1].Item1);
                _curves.Add(Tuple.Create((Curve)segment, Colour((samples[i].Item2 + samples[i + 1].Item2) * 0.5)));
            }
        }

        /// <summary>
        /// The beam's section carried from start to end with a ring at each of `at`, coloured with
        /// the colour that goes with it. False, and nothing added, when the beam stays a line.
        /// </summary>
        private bool Extrude(IBeam beam, Point3d start, Point3d end, IList<double> at, IList<Color> colours)
        {
            var rings = Rings(beam, start, end, at);
            if (rings == null) return false;

            foreach (var stack in rings)
            {
                var solid = Alpaca4d.Utils.CreateLoft(stack);

                // The loft lays its vertices down a ring at a time, so a vertex's ring is its index
                // over the ring size.
                int perRing = solid.Vertices.Count / stack.Count;
                if (perRing == 0) continue;

                var vertexColours = new Color[solid.Vertices.Count];
                for (int v = 0; v < vertexColours.Length; v++)
                    vertexColours[v] = colours[Math.Min(v / perRing, colours.Count - 1)];

                solid.VertexColors.SetColors(vertexColours);
                _beamSolids.Add(solid);

                _beamEdges.Add(Alpaca4d.Utils.CreateLoft(new List<Polyline> { stack[0], stack[stack.Count - 1] }));
            }

            return true;
        }

        /// <summary>
        /// Every outline of the beam's section stood up at each of `at` along it - one list of
        /// rings per outline, because a hollow section has two and a double angle two apart.
        ///
        /// Square to the line from start to end, by <see cref="LocalAxes"/>, so a section on the
        /// deformed shape stays a section and does not shear with the deformation.
        ///
        /// Null when the beam is to stay a line: Extruded beams is off, or there is no section
        /// outline or local z to stand one up with.
        /// </summary>
        private List<List<Polyline>> Rings(IBeam beam, Point3d start, Point3d end, IList<double> at)
        {
            if (!(_ckExtruded?.Active ?? false)) return null;

            var outlines = Outlines(beam);
            if (outlines == null) return null;

            Vector3d y, z;
            if (!LocalAxes(beam, start, end, out y, out z)) return null;

            // The same frame Model View stands a section in: its x along local z, its y along
            // local y.
            var axis = end - start;

            var rings = new List<List<Polyline>>();
            foreach (var outline in outlines)
            {
                var stack = new List<Polyline>();
                foreach (var t in at)
                {
                    var ring = new Polyline(outline);
                    ring.Transform(Transform.PlaneToPlane(Plane.WorldXY, new Plane(start + axis * t, z, y)));
                    stack.Add(ring);
                }
                rings.Add(stack);
            }

            return rings;
        }

        /// <summary>
        /// The beam's local y and z, the axes its forces are reported in, square to the line from
        /// start to end rather than to the beam as it was - on the deformed shape, a diagram or a
        /// section stays square to the beam that is drawn.
        ///
        /// z is the ZAxis the beam was given, projected off the beam, because OpenSees only asks
        /// that vector to lie in the x-z plane; y is z cross x, as OpenSees makes it. False when
        /// there is no z to start from, or it runs along the beam.
        /// </summary>
        private static bool LocalAxes(IBeam beam, Point3d start, Point3d end, out Vector3d y, out Vector3d z)
        {
            y = Vector3d.Unset;
            z = Vector3d.Unset;
            if (beam.GeomTransf == null) return false;

            var x = end - start;
            if (!x.Unitize()) return false;

            z = beam.GeomTransf.LocalZ;
            z -= x * (z * x);
            if (!z.Unitize()) return false;

            y = Vector3d.CrossProduct(z, x);
            return true;
        }

        /// <summary>
        /// Where each of a beam's sections sits, from 0 at its start to 1 at its end, by the beam's
        /// own integration; evenly spaced when that does not say, or names a different count.
        /// </summary>
        private static IReadOnlyList<double> Stations(IBeam beam, int count)
        {
            IReadOnlyList<double> stations = null;
            if (beam.BeamIntegration != null && beam.Curve != null)
                stations = beam.BeamIntegration.SectionLocations(beam.Curve.PointAtStart.DistanceTo(beam.Curve.PointAtEnd));

            if (stations == null || stations.Count != count)
                stations = Enumerable.Range(0, count)
                                     .Select(i => count > 1 ? i / (double)(count - 1) : 0.5)
                                     .ToList();

            return stations;
        }

        /// <summary>The section's outlines as polylines in its own plane, or null when it has none.</summary>
        private List<Polyline> Outlines(IBeam beam)
        {
            if (beam.Section == null) return null;

            List<Polyline> outlines;
            if (_outlines.TryGetValue(beam.Section, out outlines)) return outlines;

            outlines = (beam.Section.Curves ?? new List<Curve>())
                .Where(x => x != null)
                .Select(x => x.ToPolyline(0, 0, 0, 0)?.ToPolyline())
                .Where(x => x != null && x.Count > 1)
                .ToList();

            if (outlines.Count == 0) outlines = null;
            _outlines[beam.Section] = outlines;
            return outlines;
        }

        /// <summary>How far a label on this beam stands off its axis: clear of the section when it is drawn solid.</summary>
        private double Lift(IBeam beam)
        {
            if (!(_ckExtruded?.Active ?? false)) return 0.0;

            var outlines = Outlines(beam);
            if (outlines == null) return 0.0;

            return outlines.SelectMany(x => x).Select(x => x.DistanceTo(Point3d.Origin)).DefaultIfEmpty(0.0).Max() * 1.2;
        }

        /// <summary>
        /// A value at every integration section, painted along the beam: each section's colour sits
        /// where the section does, and the colour is walked between neighbouring ones.
        ///
        /// Placed by the beam's own integration rather than at equal steps. Newton-Cotes spaces its
        /// sections evenly, but HingeRadau puts one a short way in from each end and two over the
        /// middle, and at equal steps the section just inside a hinge would be drawn a fifth of the
        /// way along the beam rather than where it is.
        /// </summary>
        private void BuildBeamField(IBeam beam, Point3d start, Point3d end, List<double> values, double lift)
        {
            var stations = Stations(beam, values.Count);
            var axis = end - start;

            var samples = new List<Tuple<double, double>>();

            if (values.Count == 1)
            {
                samples.Add(Tuple.Create(0.0, values[0]));
                samples.Add(Tuple.Create(1.0, values[0]));
            }
            else
            {
                // Out to the ends as well: the first and last section need not sit on them.
                var at = new List<double> { 0.0 };
                var value = new List<double> { values[0] };
                for (int i = 0; i < values.Count; i++)
                {
                    at.Add(stations[i]);
                    value.Add(values[i]);
                }
                at.Add(1.0);
                value.Add(values[values.Count - 1]);

                const int steps = 4;
                for (int i = 0; i < at.Count - 1; i++)
                {
                    if (at[i + 1] - at[i] <= 0.0) continue;

                    for (int k = 0; k < steps; k++)
                    {
                        double a = k / (double)steps;
                        samples.Add(Tuple.Create(at[i] + (at[i + 1] - at[i]) * a, value[i] + (value[i + 1] - value[i]) * a));
                    }
                }
                samples.Add(Tuple.Create(1.0, value[value.Count - 1]));
            }

            Paint(beam, start, end, samples);

            int worst = 0;
            for (int i = 1; i < values.Count; i++)
                if (Math.Abs(values[i]) > Math.Abs(values[worst])) worst = i;

            Label(start + axis * stations[worst], values[worst], lift);
        }

        private void BuildFace(Mesh source, int? tag, List<int?> nodes, bool kept, ResultFamily family,
                               Dictionary<int, Vector3d> displacement, bool coloured, bool midEdge = false)
        {
            if (source == null) return;

            var mesh = source.DuplicateMesh();

            if (displacement != null && nodes != null)
            {
                for (int i = 0; i < mesh.Vertices.Count && i < nodes.Count; i++)
                    mesh.Vertices.SetVertex(i, Move(new Point3d(mesh.Vertices[i]), nodes[i], displacement));
            }

            if (!kept)
            {
                _ghostMeshes.Add(mesh);
                return;
            }

            mesh.VertexColors.Clear();
            double label = 0.0;
            bool hasLabel = false;

            if (family == ResultFamily.Displacement && _field.ByNode != null && nodes != null)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    double value = ValueAtNode(i < nodes.Count ? nodes[i] : null);
                    mesh.VertexColors.Add(Colour(value));
                    if (Math.Abs(value) > Math.Abs(label)) label = value;
                }

                hasLabel = true;
            }
            else if (coloured && tag.HasValue && _field.ByElement != null && _field.ByElement.ContainsKey(tag.Value))
            {
                // One value per integration point, each painted on the corner it sits by - a
                // quad's point i is next to its node i. A face with more corners than it has
                // stations reuses the last.
                //
                // An ASDShellT3's three points are not by its corners but at the middles of its
                // edges, point i on the edge opposite node i, so a corner takes the mean of the two
                // on the edges that meet at it. Painted point i on corner i, every value sat at the
                // far side of the triangle from where it was read.
                var values = _field.ByElement[tag.Value];
                bool edges = midEdge && values.Count == 3 && mesh.Vertices.Count == 3;
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    double value = edges
                        ? (values[0] + values[1] + values[2] - values[i]) * 0.5
                        : values[Math.Min(i, values.Count - 1)];
                    mesh.VertexColors.Add(Colour(value));
                }

                foreach (var value in values)
                    if (Math.Abs(value) > Math.Abs(label)) label = value;

                hasLabel = true;
            }
            else
            {
                // Passed the filter, but this result has nothing to say about it.
                mesh.VertexColors.CreateMonotoneMesh(Color.FromArgb(210, 210, 210));
            }

            _shaded.Add(mesh);

            if (hasLabel)
                Label(mesh.GetBoundingBox(false).Center, label);
        }

        /// <summary>
        /// The reaction at every support, drawn the way the Reactions dropdown asks for.
        ///
        /// The first cut always drew the resultant, whichever component was chosen. On a model with
        /// any horizontal reaction that means picking Fz and getting an arrow pointing off at an
        /// angle - right magnitude in the Values output, wrong picture. An arrow that says Fz now
        /// runs along z and nowhere else, and carries the sign, so a downward reaction points down.
        /// </summary>
        private void BuildReactions(int component)
        {
            if (_field.Reactions == null || _field.Reactions.Count == 0) return;

            int style = _ddReaction?.Value ?? 0;

            double largest = _field.Reactions.Select(x => Math.Abs(x.Item3)).DefaultIfEmpty(0.0).Max();
            if (largest <= 0.0) return;

            double scale = Reach() / largest * (_slDiagramScale?.Value ?? 1.0);

            // A moment is not a force, and pointing an arrow along one is already a convention
            // rather than a picture. It is at least drawn from the same axes.
            bool moment = component >= 4;

            foreach (var reaction in _field.Reactions)
            {
                var plane = reaction.Item1;
                var local = reaction.Item2;
                var axes = new[] { plane.XAxis, plane.YAxis, plane.ZAxis };
                var parts = new[] { local.X, local.Y, local.Z };

                if (style == 2)
                {
                    // One arrow per axis, so a support carrying load three ways shows all three.
                    for (int i = 0; i < 3; i++)
                        Arrow(plane.Origin, axes[i] * parts[i] * scale, parts[i], moment);
                }
                else if (style == 1 || component % 4 == 0)
                {
                    // The resultant, which is also what "Force" and "Moment" mean.
                    var world = axes[0] * local.X + axes[1] * local.Y + axes[2] * local.Z;
                    Arrow(plane.Origin, world * scale, world.Length, moment);
                }
                else
                {
                    // The one component the dropdown names, along its own axis and with its sign.
                    int axis = component % 4 - 1;
                    Arrow(plane.Origin, axes[axis] * parts[axis] * scale, parts[axis], moment);
                }
            }
        }

        /// <summary>
        /// One arrow from a support. Drawn outwards along the reaction, which OpenSees reports as
        /// the support's action on the structure - a downward load on a cantilever gives an upward
        /// reaction, measured, so an upward arrow is the picture an engineer expects.
        /// </summary>
        private void Arrow(Point3d at, Vector3d offset, double value, bool moment)
        {
            if (offset.Length <= 0.0) return;

            var line = new Line(at, at + offset);
            _arrows.Add(Tuple.Create(line, Colour(Math.Abs(value))));

            // A double head marks a moment, the usual way of telling one from a force.
            if (moment)
                _arrows.Add(Tuple.Create(new Line(at + offset * 0.75, at + offset * 0.95), Colour(Math.Abs(value))));

            Label(line.To, value);
        }

        /// <summary>The way a positive value of a beam force is drawn; see <see cref="Diagram"/>.</summary>
        private static Vector3d DiagramDirection(IBeam beam, Point3d start, Point3d end, int component)
        {
            Vector3d y, z;
            if (!LocalAxes(beam, start, end, out y, out z)) return Vector3d.ZAxis;

            return component == 2 || component == 4 ? z : component == 5 ? -y : y;
        }

        /// <summary>
        /// The length one unit of beam force is drawn at: the largest force on the beams that passed
        /// the filter stands off the model as far as the largest reaction arrow does.
        ///
        /// One scale for the whole model. Each beam used to be fitted to its own largest force, so
        /// every element of a beam split into ten drew the same height - the end ones carrying the
        /// full shear and the middle ones a tenth of it - and the diagram climbed in a sawtooth
        /// the numbers beside it did not.
        /// </summary>
        private double DiagramUnit(ElementFilter filter)
        {
            if (_field.ByElement == null) return 0.0;

            double largest = _model.Beams
                .Where(beam => beam.Id.HasValue && filter.Matches(beam) && _field.ByElement.ContainsKey(beam.Id.Value))
                .SelectMany(beam => _field.ByElement[beam.Id.Value])
                .Select(Math.Abs)
                .DefaultIfEmpty(0.0)
                .Max();

            return largest > 0.0 ? Reach() / largest * (_slDiagramScale?.Value ?? 1.0) : 0.0;
        }

        /// <summary>
        /// How far the largest diagram or reaction arrow stands off the model: a fixed share of the
        /// model's size, which keeps it readable whether the model is a bracket or a bridge.
        /// </summary>
        private double Reach()
        {
            var box = _model.UniquePoints != null && _model.UniquePoints.Count > 0
                ? new BoundingBox(_model.UniquePoints)
                : BoundingBox.Unset;

            return box.IsValid && box.Diagonal.Length > 0.0 ? box.Diagonal.Length * 0.08 : 1.0;
        }

        private void Label(Point3d at, double value, double lift = 0.0)
        {
            if (!(_ckValues?.Active ?? false)) return;

            _labels.Add(Tuple.Create(at, value.ToString("G4"), lift));
        }

        private double ValueAtNode(int? node)
        {
            double value;
            if (node.HasValue && _field.ByNode != null && _field.ByNode.TryGetValue(node.Value, out value))
                return value;

            return _min;
        }

        private Color Colour(double value)
        {
            return _gradient != null && _gradient.Count > 0
                ? Alpaca4d.Colors.GetColor(value, _gradient)
                : Color.Gray;
        }

        /// <summary>
        /// A force diagram: the values stood off the beam in the plane the force acts in, closed
        /// back onto it at both ends so the area reads as the diagram it is.
        ///
        /// Vy and Mz along local y, Vz and My along local z - Mz bends the beam in its x-y plane
        /// and My in its x-z one. N and Torsion act in no plane of their own and go in the x-y
        /// one, the plane of the strong-axis diagrams. Drawing all six along z, as the first cut
        /// did, put Vy and Mz in the plane at right angles to the one they belong to.
        ///
        /// Moments on the side they put in tension. A positive My stretches the +z side, so it
        /// is drawn towards +z; a positive Mz compresses the +y side, so it is drawn towards -y -
        /// on a beam with local y up, sagging hangs below it. The others are drawn towards the
        /// positive axis.
        ///
        /// <paramref name="unit"/> is the length one unit of force is drawn at, the same for every
        /// beam - see <see cref="DiagramUnit"/>.
        /// </summary>
        private static Mesh Diagram(IBeam beam, Point3d start, Point3d end, List<double> forces,
                                    int component, double unit)
        {
            if (forces == null || forces.Count < 2 || unit <= 0.0) return null;
            if (forces.All(x => x == 0.0)) return null;

            var offset = DiagramDirection(beam, start, end, component);

            var stations = Stations(beam, forces.Count);
            var mesh = new Mesh();
            var colours = new List<Color>();
            var axis = end - start;

            int Vertex(Point3d at, Color colour)
            {
                mesh.Vertices.Add(at);
                colours.Add(colour);
                return mesh.Vertices.Count - 1;
            }

            for (int i = 0; i < forces.Count - 1; i++)
            {
                var a = start + axis * stations[i];
                var b = start + axis * stations[i + 1];
                double fa = forces[i];
                double fb = forces[i + 1];
                var ca = ResultField.BeamForceColour(component, fa);
                var cb = ResultField.BeamForceColour(component, fb);

                if (fa * fb < 0.0)
                {
                    // Changing sign between the two: closed onto the beam where it crosses zero, as
                    // a triangle either side. One quad would fold over itself across the beam.
                    var zero = a + (b - a) * (fa / (fa - fb));
                    mesh.Faces.AddFace(Vertex(a, ca), Vertex(zero, ca), Vertex(a + offset * fa * unit, ca));
                    mesh.Faces.AddFace(Vertex(zero, cb), Vertex(b, cb), Vertex(b + offset * fb * unit, cb));
                }
                else
                {
                    mesh.Faces.AddFace(Vertex(a, ca), Vertex(b, cb),
                                       Vertex(b + offset * fb * unit, cb), Vertex(a + offset * fa * unit, ca));
                }
            }

            mesh.VertexColors.SetColors(colours.ToArray());
            mesh.Normals.ComputeNormals();

            return mesh;
        }

        #endregion

        #region Viewport

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            if (this.Hidden || this.Locked || _model == null) return;

            // The solid geometry first and the ghosts last: a transparent material blends against
            // what is already in the depth buffer, so drawing it first would let it hide the very
            // elements it is meant to sit behind.
            foreach (var mesh in _shaded)
                args.Display.DrawMeshFalseColors(mesh);

            foreach (var mesh in _beamSolids)
                args.Display.DrawMeshFalseColors(mesh);

            foreach (var mesh in _diagrams)
                args.Display.DrawMeshFalseColors(mesh);

            var ghost = new DisplayMaterial(GhostColour, 0.75);
            foreach (var mesh in _ghostMeshes)
                args.Display.DrawMeshShaded(mesh, ghost);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (this.Hidden || this.Locked || _model == null) return;

            var ghostLine = Color.FromArgb(90, GhostColour);

            foreach (var curve in _ghostCurves)
                args.Display.DrawCurve(curve, ghostLine, 1);

            if (_ckWires?.Active ?? false)
            {
                foreach (var mesh in _ghostMeshes)
                    args.Display.DrawMeshWires(mesh, ghostLine, 1);

                foreach (var mesh in _shaded)
                    args.Display.DrawMeshWires(mesh, Color.Black, 1);

                foreach (var mesh in _beamEdges)
                    args.Display.DrawMeshWires(mesh, Color.Black, 1);
            }

            foreach (var mesh in _diagrams)
                args.Display.DrawMeshWires(mesh, Color.Black, 1);

            foreach (var curve in _curves)
                args.Display.DrawCurve(curve.Item1, curve.Item2, 3);

            foreach (var arrow in _arrows)
                args.Display.DrawArrow(arrow.Item1, arrow.Item2);

            if (_labels.Count > 0)
            {
                double height = _slTextSize?.Value ?? 0.5;

                var colour = AlpacaSettings.Colour(DisplayItem.Values);

                var towardsCamera = -args.Viewport.CameraDirection;
                towardsCamera.Unitize();

                foreach (var label in _labels)
                {
                    // Squared to the camera, so a number reads from wherever the model is spun to.
                    var plane = new Plane(label.Item1 + towardsCamera * label.Item3, args.Viewport.CameraX, args.Viewport.CameraY);
                    args.Display.Draw3dText(label.Item2, colour, plane, height, "Arial", false, false);
                }
            }
        }

        public override bool IsPreviewCapable => true;

        public override BoundingBox ClippingBox
        {
            get
            {
                var box = BoundingBox.Empty;

                foreach (var mesh in _shaded) box.Union(mesh.GetBoundingBox(false));
                foreach (var mesh in _beamSolids) box.Union(mesh.GetBoundingBox(false));
                foreach (var mesh in _ghostMeshes) box.Union(mesh.GetBoundingBox(false));
                foreach (var mesh in _diagrams) box.Union(mesh.GetBoundingBox(false));
                foreach (var curve in _ghostCurves) box.Union(curve.GetBoundingBox(false));
                foreach (var curve in _curves) box.Union(curve.Item1.GetBoundingBox(false));
                foreach (var arrow in _arrows) box.Union(arrow.Item1.BoundingBox);

                // Labels too, grown by how far they stand off and how tall they are. A box that
                // stops short of what is drawn lets Rhino put a clipping plane through it.
                if (_labels.Count > 0)
                {
                    double reach = _labels.Max(x => x.Item3) + 2.0 * (_slTextSize?.Value ?? 0.5);
                    foreach (var label in _labels)
                        box.Union(new BoundingBox(label.Item1 - new Vector3d(reach, reach, reach),
                                                  label.Item1 + new Vector3d(reach, reach, reach)));
                }

                return box.IsValid ? box : base.ClippingBox;
            }
        }

        #endregion

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Deformed_Model__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{4D8A0F26-3B71-4E59-9C84-A2F70D51B6E3}");
    }
}
