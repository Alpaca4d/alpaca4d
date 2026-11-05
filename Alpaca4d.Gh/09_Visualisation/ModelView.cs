using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Result;
using Alpaca4d.UI;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The model and its results in one component.
    ///
    /// Model View drew the model - its elements in their section's colour, ids, axes, releases,
    /// loads and supports - and View Results drew a result, so looking at both took two components
    /// wired to the same model and two sets of menus to keep in step. Here the Results menu has a
    /// checkbox for each result View Results drew, and every one ticked is drawn at once: beam
    /// forces as diagrams over shells coloured by their stress, with the reactions at the supports.
    /// With none ticked, the elements are coloured by element, section or material, as Colour by in
    /// the Elements menu says. The ids, axes, releases, loads and supports go on top of either, so a
    /// reaction can be read beside the support it comes from.
    ///
    /// What is drawn is also handed out as geometry, coloured meshes and lines, so that a copy of it
    /// can be moved beside the model, or baked.
    ///
    /// The results are built in ModelView.Results.cs and the ids, axes, loads and supports drawn in
    /// ModelView.Overlays.cs.
    /// </summary>
    public partial class ModelView : GH_ExtendableComponent
    {
        private Alpaca4d.Model _model;
        private List<Color> _palette;

        // The result being built, out of _shown, and the scale it is coloured over: what the code
        // that builds the drawing reads. Set by Use, one result after another.
        private ResultField _field;
        private SortedDictionary<double, Color> _gradient;
        private double _min, _max;

        /// <summary>Iso-surfaces: each colour a band of values, not a point on a gradient. See <see cref="IsoBands"/>.</summary>
        private bool _banded;

        /// <summary>The length one unit of beam force is drawn at; see <see cref="DiagramUnit"/>.</summary>
        private double _diagramUnit;

        /// <summary>The results this solution drew, in the order of the Results menu. Empty for the model alone.</summary>
        private readonly List<ShownResult> _shown = new List<ShownResult>();

        /// <summary>The result each element shown is coloured by; an element with none is drawn plain.</summary>
        private readonly Dictionary<IElement, ShownResult> _painter = new Dictionary<IElement, ShownResult>();

        /// <summary>The elements that passed the ElementId filter; the rest are ghosted.</summary>
        private ElementFilter _filter;

        /// <summary>The colour each element is drawn in with no result, by Colour by.</summary>
        private Alpaca4d.Visualisation.ElementColours _colours;

        // What gets drawn, worked out in SolveInstance and held for the viewport.
        private readonly List<Mesh> _shaded = new List<Mesh>();

        // The elements' own faces, for their edges: with iso-surfaces on, what is shaded is cut
        // into bands, and its wires would draw every cut.
        private readonly List<Mesh> _faceEdges = new List<Mesh>();
        private readonly List<Mesh> _ghostMeshes = new List<Mesh>();
        private readonly JoinedLines _ghostCurves = new JoinedLines();
        private readonly JoinedLines _curves = new JoinedLines();
        private readonly List<Mesh> _diagrams = new List<Mesh>();
        private readonly List<Tuple<Line, Color>> _arrows = new List<Tuple<Line, Color>>();

        // Every value that can be written on the model, where, and how far it stands off towards the
        // camera so that a value on an extruded beam is not drawn inside the section it belongs to.
        // Kept whether Show values is ticked or not, so ticking it is a redraw and not a solution.
        private readonly List<Tuple<Point3d, double, double>> _labels = new List<Tuple<Point3d, double, double>>();

        // Beams drawn solid. The coloured one has a ring at every colour stop, so its edges are
        // drawn from a second one with a ring at each end only - the stops would bury them in lines.
        private readonly List<Mesh> _beamSolids = new List<Mesh>();
        private readonly List<Mesh> _beamEdges = new List<Mesh>();

        // The mesh lists above, each joined into one once the solution is complete, for Rhino to draw
        // in one call (see Joined) - a face at a time, a solid block was hundreds of thousands of calls
        // a redraw. And what everything drawn spans, worked out then too rather than every time Rhino
        // asks for it, which it does on every redraw. Null and empty while there is nothing to draw.
        private Mesh _shadedJoined, _faceEdgesJoined, _ghostJoined, _diagramsJoined, _beamSolidsJoined, _beamEdgesJoined;
        private BoundingBox _box = BoundingBox.Empty;

        // ── Results ──────────────────────────────────────────────────────────

        /// <summary>
        /// One result's controls in the Results menu: its checkbox, and under it, shown while it is
        /// ticked, the component to draw and the unit to draw it in.
        /// </summary>
        internal sealed class ResultControls
        {
            public ResultFamily Family;
            public MenuCheckBox Tick;

            /// <summary>The one component to draw - or for beam forces, null, and <see cref="Components"/>.</summary>
            public MenuDropDown Component;

            /// <summary>
            /// Beam forces only: a checkbox per force, in a row, as many ticked as there are
            /// diagrams to draw. A diagram stands off the beam rather than colouring it, so six can
            /// be drawn on one beam where two colours cannot.
            /// </summary>
            public MenuCheckBoxGroup Components;

            /// <summary>
            /// One per kind of quantity, by Quantity, null for the kinds the result has none of, and
            /// only the ones of the components picked are shown: each remembers its own unit, so
            /// going from N to My and back finds N still in the unit it was in.
            /// </summary>
            public MenuDropDown[] Units;

            /// <summary>The components to draw, by their place in the result's list.</summary>
            public IEnumerable<int> Picked => Components != null ? Components.Ticked : new[] { Component.Value };

            public IEnumerable<Quantity> Quantities => Picked.Select(c => ResultField.QuantityOf(Family, c)).Distinct();

            public MenuDropDown UnitOf(int component) => Units[(int)ResultField.QuantityOf(Family, component)];

            /// <summary>One component picked alone, as a click picks it.</summary>
            public void Pick(int component)
            {
                if (Components != null) Components.TickOnly(component);
                else Component.Value = component;
            }

            /// <summary>The control the component is picked with.</summary>
            public GH_Attr_Widget Picker => (GH_Attr_Widget)Components ?? Component;
        }

        /// <summary>The results' controls, in the order of the Results menu.</summary>
        internal ResultControls[] _results;

        internal MenuCheckBox _ckDeformed;
        internal MenuSlider _slDeformScale;
        internal MenuCheckBox _ckAnimate;

        // Under the one result that has a use for each.
        internal MenuSlider _slDiagramScale;
        internal MenuDropDown _ddLayer;
        internal MenuDropDown _ddAxes;
        internal MenuDropDown _ddReaction;
        internal MenuSlider _slArrowScale;

        internal MenuCheckBox _ckBands;
        internal MenuCheckBox _ckValues;

        // ── Elements ─────────────────────────────────────────────────────────
        internal MenuCheckBox _ckExtruded;
        internal MenuCheckBox _ckWires;
        internal MenuCheckBox _ckNodeIds;
        internal MenuCheckBox _ckElementIds;
        internal MenuCheckBox _ckSectionNames;
        internal MenuCheckBox _ckLocalAxes;
        internal MenuSlider _slAxesSize;
        internal MenuCheckBox _ckReleases;
        internal MenuSlider _slReleaseSize;
        internal MenuDropDown _ddColourBy;
        internal MenuSlider _slTextSize;

        // ── Loads and Supports ───────────────────────────────────────────────
        internal MenuCheckBox _ckShowLoads;
        internal MenuCheckBox _ckShowLoadValues;
        internal MenuSlider _slLoadScale;
        internal MenuCheckBox _ckShowSupports;
        internal MenuSlider _slSupportScale;

        // A caption is hidden with the control it names, so both are held.
        private MenuStaticText _txDeformScale;
        private MenuStaticText _txDiagramScale;
        private MenuStaticText _txLayer;
        private MenuStaticText _txAxes;
        private MenuStaticText _txReaction;
        private MenuStaticText _txArrowScale;
        private MenuStaticText _txColourBy;
        private MenuStaticText _txTextSize;
        private MenuStaticText _txAxesSize;
        private MenuStaticText _txReleaseSize;

        // The Loads menu, which the LoadPattern input is drawn in.
        private GH_ExtendableMenu _loadsMenu;

        /// <summary>The colour everything outside the filter is drawn in.</summary>
        private static readonly Color GhostColour = Color.FromArgb(150, 150, 150);

        public ModelView()
          : base("Model View (Alpaca4d)", "ModelView",
            "Draw the model and its results in the viewport.\n" +
            "Tick one or more results under Results - displacements, beam forces and stresses, shell forces and " +
            "stresses, solid stresses, reactions - to draw them together, each with its own component and unit. " +
            "Under Beam forces, tick any of N, Vy, Vz, T, My and Mz to draw their diagrams together. " +
            "They need a model from Run Analysis or another analysis. Each element is coloured by one result: an " +
            "element's own stress or force before the displacement, which colours the rest.\n" +
            "With none ticked, the elements are coloured by element, section or material.\n" +
            "Node and element ids, section names, local axes, end releases, loads and supports can be drawn over " +
            "either, from the Elements, Loads and Supports menus.\n" +
            "ElementId shows only some of the elements and greys the rest, so what you are looking at stays in the " +
            "model around it.\n" +
            "Geometry hands out what is drawn as coloured meshes and lines - move it, or bake it.\n" +
            "With Extruded on, beam stresses that differ across a section - σx, σMy, σMz, τV, τT, Von Mises - " +
            "are painted point by point over it; drawn as lines, each section shows its worst.\n" +
            "A vibration or buckling mode is drawn at deformation scale 1 with its largest movement as far off " +
            "the model as the diagrams stand; any other displacement at true size. On a natural vibration or " +
            "buckling model the scale runs from -2 to 2, so Animate swings the mode both ways.",
            "Alpaca4d", "09_Visualisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        public override void CreateAttributes()
        {
            base.CreateAttributes(); // creates GH_ExtendableComponentAttributes and calls Setup()

            // The load pattern is drawn in the Loads menu, beside what it filters. Registered here,
            // where the parameters exist both for a new component and for one read from a file.
            if (_loadsMenu != null && Params.Input.Count > LoadPatternInput)
                _loadsMenu.RegisterInputPlug(new ExtendedPlug(Params.Input[LoadPatternInput]));
        }

        /// <summary>Where LoadPattern sits among the inputs: last, as it is drawn in the Loads menu.</summary>
        private const int LoadPatternInput = 5;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The model to draw: from Assemble to see it alone, or from Run Analysis or another analysis to see its results as well. " +
                "Results are read out of the recorder file the analysed model points at.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to draw, or which mode after a natural vibration or buckling analysis.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.Filter + "\nWhatever is left out is greyed rather than hidden.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colors", "Colors", "Gradient to colour the results with, from the low end to the high. Connect the Colors component for a ready-made one.\nWith Iso-surfaces on, each colour is one band of values.\nBeam force diagrams ignore it and use the colour that belongs to the force.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntervalParameter("Range", "Range", "Range each gradient is stretched over, in the unit picked for its result: one per result ticked, in the order of the Results menu, " +
                "beam forces left out as they have no gradient. A result given none fits what is drawn, which is what makes two steps impossible to compare - set it to compare them.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("LoadPattern", "LP", "Optional LoadPattern Id to filter load visualisation. If not provided all load patterns are shown.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("AlpacaModel", "AlpacaModel", "The model passed straight through, so this component can sit in the middle of a chain rather than at the end of one.");
            pManager.Register_GeometryParam("Geometry", "Geometry", "What is drawn, as geometry: the elements and the results on them as coloured meshes, beams drawn as lines as lines, " +
                "and force diagrams and reaction arrows. Move it to see a copy beside the model, or bake it. " +
                "Lines lose their colour, so draw the beams Extruded to keep it. Values, ids, axes, releases, loads and supports are not in it.");
            pManager.Register_StringParam("Info", "Info", "What is drawn: a line per result, with the unit it is in and the range it spans.");
            pManager.Register_DoubleParam("Values", "Values", "The value each colour stands for, one per colour of Colors, from the low end to the high - over Range when it is set - in the unit picked for the result. " +
                "A branch per result ticked, in the order of the Results menu, beam forces left out. " +
                "With Iso-surfaces on, the edges of the bands instead, one more than there are colours. Feed it to the Values input of Legend, so the legends and the model agree. Empty with no result.");
            pManager.Register_ColourParam("Colors", "Colors", "The gradient used, a branch for each branch of Values. Feed it to the Colors input of Legend, so the legends and the model agree.");
        }

        #region UI

        /// <summary>A caption above a control, because a dropdown or slider on its own shows a value and no name.</summary>
        private static MenuStaticText Caption(string text)
        {
            return new MenuStaticText { Text = text };
        }

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            // ── Results ──────────────────────────────────────────────────────
            var resultsMenu = new GH_ExtendableMenu(0, "Results") { Name = "Results", Header = "The results to draw, one or more" };
            var resultsPanel = new MenuPanel(0, "results_panel");

            _ckDeformed = new MenuCheckBox(0, "Deformed", "Deformed shape");
            _slDeformScale = new MenuSlider(0, "DeformScale", StaticScale.Min, StaticScale.Max, 1.0, StaticScale.Decimals);
            _ckAnimate = new MenuCheckBox(1, "Animate", AnimateLabel);
            _txDeformScale = Caption("Deformation scale");

            resultsPanel.AddControl(_ckDeformed);
            resultsPanel.AddControl(_txDeformScale);
            resultsPanel.AddControl(_slDeformScale);
            resultsPanel.AddControl(_ckAnimate);

            _slDiagramScale = new MenuSlider(1, "DiagramScale", 0.0, 20.0, 1.0, 2);
            _txDiagramScale = Caption("Diagram scale");

            _ddLayer = new MenuDropDown(7, "Layer", "Layer") { VisibleItemCount = 3 };
            foreach (var name in Alpaca4d.Result.Read.LayerNames)
                _ddLayer.AddItem(name, name);
            _txLayer = Caption("Layer through the thickness");

            _ddAxes = new MenuDropDown(8, "Axes", "Axes") { VisibleItemCount = 2 };
            foreach (var name in ResultField.StressAxes)
                _ddAxes.AddItem(name, name);
            _txAxes = Caption("Read in which axes");

            _ddReaction = new MenuDropDown(9, "ReactionStyle", "ReactionStyle") { VisibleItemCount = 3 };
            foreach (var name in ResultField.ReactionStyles)
                _ddReaction.AddItem(name, name);
            _txReaction = Caption("Reactions drawn as");

            _slArrowScale = new MenuSlider(2, "ArrowScale", 0.0, 20.0, 1.0, 2);
            _txArrowScale = Caption("Arrow scale");

            // Each result's checkbox, then what goes with it, shown while it is ticked.
            _results = ResultField.MenuOrder.Select(ResultControlsFor).ToArray();
            foreach (var result in _results)
            {
                resultsPanel.AddControl(result.Tick);
                resultsPanel.AddControl(result.Picker);
                foreach (var unit in result.Units.Where(x => x != null))
                    resultsPanel.AddControl(unit);

                switch (result.Family)
                {
                    case ResultFamily.BeamForce:
                        resultsPanel.AddControl(_txDiagramScale);
                        resultsPanel.AddControl(_slDiagramScale);
                        break;
                    case ResultFamily.ShellStress:
                        resultsPanel.AddControl(_txLayer);
                        resultsPanel.AddControl(_ddLayer);
                        break;
                    case ResultFamily.BrickStress:
                        resultsPanel.AddControl(_txAxes);
                        resultsPanel.AddControl(_ddAxes);
                        break;
                    case ResultFamily.Reaction:
                        resultsPanel.AddControl(_txReaction);
                        resultsPanel.AddControl(_ddReaction);
                        resultsPanel.AddControl(_txArrowScale);
                        resultsPanel.AddControl(_slArrowScale);
                        break;
                }
            }

            _ckBands = new MenuCheckBox(2, "IsoSurfaces", "Iso-surfaces");
            _ckValues = new MenuCheckBox(3, "Values", "Show values");
            resultsPanel.AddControl(_ckBands);
            resultsPanel.AddControl(_ckValues);

            resultsMenu.AddControl(resultsPanel);
            resultsMenu.Expand();
            attr.AddMenu(resultsMenu);

            // ── Elements ─────────────────────────────────────────────────────
            var elementsMenu = new GH_ExtendableMenu(1, "Elements") { Name = "Elements", Header = "Element display options" };
            var elementsPanel = new MenuPanel(1, "elements_panel");

            // On unless a saved file says otherwise: the section is the first thing to check.
            _ckExtruded = new MenuCheckBox(0, "Extruded", "Extruded") { Active = true };
            _ckWires = new MenuCheckBox(1, "Wires", "Element edges") { Active = true };
            _ckNodeIds = new MenuCheckBox(2, "NodeIds", "Node IDs");
            _ckElementIds = new MenuCheckBox(3, "ElementIds", "Element IDs");
            _ckSectionNames = new MenuCheckBox(4, "SectionNames", "Section Names");
            _ckLocalAxes = new MenuCheckBox(5, "LocalAxes", "Local Axes");
            // On unless a saved file says otherwise: it draws nothing where nothing is released.
            _ckReleases = new MenuCheckBox(6, "Releases", "End Releases") { Active = true };

            // How long the local axes are drawn, and how big the release circles and lines are
            // against a twentieth of a typical beam. Each shown only while it has something to size.
            _slAxesSize = new MenuSlider(1, "AxesSize", 0.1, 10.0, 1.0, 1);
            _slReleaseSize = new MenuSlider(2, "ReleaseSize", 0.1, 5.0, 1.0, 1);
            _txAxesSize = Caption("Axes size");
            _txReleaseSize = Caption("Release size");

            // With no result: each element in its own colour, or every element of a section, or of
            // a material, in one - the one the Section or Material Library gave it, or one picked
            // here. In the order of ColourBy, which the saved index is read back against.
            _ddColourBy = new MenuDropDown(0, "ColourBy", "ColourBy") { VisibleItemCount = 3 };
            foreach (var name in Enum.GetNames(typeof(Alpaca4d.Visualisation.ColourBy)))
                _ddColourBy.AddItem(name, name);
            _txColourBy = Caption("Colour by");

            // Height of every label - values, ids, section names, load values - in model units.
            _slTextSize = new MenuSlider(0, "TextSize", 0.05, 5.0, 0.5, 2);
            _txTextSize = Caption("Text size");

            elementsPanel.AddControl(_ckExtruded);
            elementsPanel.AddControl(_ckWires);
            elementsPanel.AddControl(_ckNodeIds);
            elementsPanel.AddControl(_ckElementIds);
            elementsPanel.AddControl(_ckSectionNames);
            elementsPanel.AddControl(_ckLocalAxes);
            elementsPanel.AddControl(_txAxesSize);
            elementsPanel.AddControl(_slAxesSize);
            elementsPanel.AddControl(_ckReleases);
            elementsPanel.AddControl(_txReleaseSize);
            elementsPanel.AddControl(_slReleaseSize);
            elementsPanel.AddControl(_txColourBy);
            elementsPanel.AddControl(_ddColourBy);
            elementsPanel.AddControl(_txTextSize);
            elementsPanel.AddControl(_slTextSize);
            elementsMenu.AddControl(elementsPanel);
            attr.AddMenu(elementsMenu);

            // ── Loads ────────────────────────────────────────────────────────
            var loadsMenu = new GH_ExtendableMenu(2, "Loads") { Name = "Loads", Header = "Load display options" };
            var loadsPanel = new MenuPanel(2, "loads_panel");

            _ckShowLoads = new MenuCheckBox(0, "ShowLoads", "Show Loads");
            // What each load is worth, written at the tail of its arrow; with or without the arrows.
            _ckShowLoadValues = new MenuCheckBox(1, "ShowLoadValues", "Show Values");
            _slLoadScale = new MenuSlider(0, "LoadScale", 0.1, 10.0, 1.0, 1) { Name = "Load Scale" };

            loadsPanel.AddControl(_ckShowLoads);
            loadsPanel.AddControl(_ckShowLoadValues);
            loadsPanel.AddControl(_slLoadScale);
            loadsMenu.AddControl(loadsPanel);
            _loadsMenu = loadsMenu;
            attr.AddMenu(loadsMenu);

            // ── Supports ─────────────────────────────────────────────────────
            var supportsMenu = new GH_ExtendableMenu(3, "Supports") { Name = "Supports", Header = "Support display options" };
            var supportsPanel = new MenuPanel(3, "supports_panel");

            _ckShowSupports = new MenuCheckBox(0, "ShowSupports", "Show Supports");
            _slSupportScale = new MenuSlider(0, "SupportScale", 0.1, 10.0, 1.0, 1) { Name = "Support Scale" };

            supportsPanel.AddControl(_ckShowSupports);
            supportsPanel.AddControl(_slSupportScale);
            supportsMenu.AddControl(supportsPanel);
            attr.AddMenu(supportsMenu);

            attr.MinWidth = 230f;

            // An upgraded Model View or View Results puts its settings on the controls just made.
            CarryOver?.Invoke(this);

            Subscribe();
            UpdateVisibility();
        }

        /// <summary>
        /// A result's checkbox, its Component dropdown and a Unit dropdown for each kind of quantity
        /// among its components.
        ///
        /// Numbered by the result's place in <see cref="ResultFamily"/>, which only ever grows at the
        /// end, rather than by its place in the menu, which may change: a file finds each control by
        /// its number.
        /// </summary>
        private static ResultControls ResultControlsFor(ResultFamily family)
        {
            int number = (int)family;
            var names = ResultField.ComponentNames(family);

            var result = new ResultControls
            {
                Family = family,
                Tick = new MenuCheckBox(10 + number, "Show" + family, ResultField.FamilyNames[number]),
                Units = new MenuDropDown[Enum.GetValues(typeof(Quantity)).Length]
            };

            if (family == ResultFamily.BeamForce)
            {
                // N picked to start with, as the dropdown it took over from started on N.
                result.Components = new MenuCheckBoxGroup(number, family + "Components") { Direction = MenuCheckBoxGroup.LayoutDirection.Horizontal };
                for (int i = 0; i < names.Length; i++)
                    result.Components.Add(ResultField.BeamForceShortNames[i], i == 0, names[i]);
            }
            else
            {
                result.Component = new MenuDropDown(number, family + "Component", "Component") { VisibleItemCount = Math.Min(8, names.Length) };
                foreach (var name in names)
                    result.Component.AddItem(name, name);
            }

            foreach (var quantity in Enumerable.Range(0, names.Length).Select(i => ResultField.QuantityOf(family, i)).Distinct())
            {
                var units = ResultUnits.Of(quantity);
                var dropdown = new MenuDropDown(10 + 10 * number + (int)quantity, family + "Unit" + quantity, "Unit") { VisibleItemCount = units.Length };
                foreach (var unit in units)
                    dropdown.AddItem(unit.Symbol, unit.Caption);

                // A file read after this puts back the unit it was saved with.
                dropdown.Value = ResultUnits.Default(quantity);
                result.Units[(int)quantity] = dropdown;
            }

            return result;
        }

        /// <summary>
        /// Settings for the controls, put on them as soon as <see cref="Setup"/> has made them. Set by
        /// <see cref="ModelViewUpgrader"/> and <see cref="ViewResultsUpgrader"/> on the component they
        /// put in place of an old one, as the controls are only made with the component's attributes.
        /// </summary>
        internal Action<ModelView> CarryOver;

        /// <summary>
        /// Every control told what to do when it changes: build again what it changes, or only draw
        /// again.
        ///
        /// What is drawn over the model - ids, axes, releases, loads, supports, values, edges - is
        /// worked out from what the solution built, so changing it draws the viewport again and
        /// leaves the solution alone. It used to run the solution again too, reading the recorder,
        /// building every mesh and solving everything downstream, to tick a box that changes none of
        /// it.
        /// </summary>
        private void Subscribe()
        {
            _ckAnimate.ValueChanged += OnAnimateChanged;

            foreach (var result in _results)
            {
                result.Tick.ValueChanged += OnResultTicked;
                Rebuild(result.Picker);
                foreach (var unit in result.Units.Where(x => x != null))
                    Rebuild(unit);
            }

            foreach (var widget in new GH_Attr_Widget[]
                     {
                         _ckDeformed, _slDeformScale, _slDiagramScale, _ddLayer, _ddAxes, _ddReaction, _slArrowScale, _ckBands,
                         _ckExtruded, _ddColourBy
                     })
                Rebuild(widget);

            foreach (var widget in new GH_Attr_Widget[]
                     {
                         _ckValues, _ckWires, _slTextSize,
                         _ckNodeIds, _ckElementIds, _ckSectionNames, _ckLocalAxes, _slAxesSize, _ckReleases, _slReleaseSize,
                         _ckShowLoads, _ckShowLoadValues, _slLoadScale, _ckShowSupports, _slSupportScale
                     })
                Redraw(widget);
        }

        private void Rebuild(GH_Attr_Widget widget) => Listen(widget, OnWidgetChanged);

        private void Redraw(GH_Attr_Widget widget) => Listen(widget, OnDisplayChanged);

        private static void Listen(GH_Attr_Widget widget, ValueChangeEventHandler handler)
        {
            switch (widget)
            {
                case MenuCheckBox check: check.ValueChanged += handler; break;
                case MenuDropDown dropdown: dropdown.ValueChanged += handler; break;
                case MenuSlider slider: slider.ValueChanged += handler; break;
                case MenuCheckBoxGroup group: group.ValueChanged += handler; break;
            }
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            if (_results == null) return;

            // A file never reopens mid-animation: the checkbox is saved, the timer is not.
            _ckAnimate.Active = false;
            _ckAnimate.Tag = AnimateLabel;

            // Visibility is not saved: it follows from the rest.
            UpdateVisibility();
        }

        private void OnWidgetChanged(object sender, EventArgs e)
        {
            UpdateVisibility();
            ExpireSolution(true);
        }

        /// <summary>
        /// A result ticked or unticked. Shell forces and shell stresses both colour the shells, and a
        /// face shows one colour, so ticking one unticks the other rather than leaving it ticked and
        /// drawing nothing.
        /// </summary>
        private void OnResultTicked(object sender, EventArgs e)
        {
            var ticked = _results.FirstOrDefault(x => x.Tick == sender);
            if (ticked != null && ticked.Tick.Active)
            {
                foreach (var other in _results.Where(x => x != ticked && SameFaces(x.Family, ticked.Family)))
                    other.Tick.Active = false;
            }

            OnWidgetChanged(sender, e);
        }

        /// <summary>Whether two results colour the same elements, so that only one of them can be seen.</summary>
        private static bool SameFaces(ResultFamily a, ResultFamily b)
        {
            bool shell(ResultFamily x) => x == ResultFamily.ShellForce || x == ResultFamily.ShellStress;
            return shell(a) && shell(b);
        }

        /// <summary>A control that changes only what is drawn over what was built: a redraw, with the extent it now spans.</summary>
        private void OnDisplayChanged(object sender, EventArgs e)
        {
            UpdateVisibility();
            _box = Extent();
            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        /// <summary>
        /// Puts away every control the current choice has no use for.
        ///
        /// One component covering the model and seven kinds of result has more knobs than any one of
        /// them needs, and a menu that shows all of them at once asks the reader to work out which
        /// ones are live - a layer to read a stress at, when the result is a reaction. Hidden rather
        /// than greyed, so the menu closes up and the component is as short as the question is.
        /// </summary>
        private void UpdateVisibility()
        {
            if (_results == null) return;

            foreach (var result in _results)
            {
                bool on = result.Tick.Active;
                Show(on, result.Picker);
                var units = result.Quantities.Select(q => result.Units[(int)q]).ToList();
                foreach (var unit in result.Units.Where(x => x != null))
                    Show(on && units.Contains(unit), unit);
            }

            Show(Ticked(ResultFamily.BeamForce), _txDiagramScale, _slDiagramScale);
            Show(Ticked(ResultFamily.ShellStress), _txLayer, _ddLayer);
            Show(Ticked(ResultFamily.BrickStress), _txAxes, _ddAxes);
            Show(Ticked(ResultFamily.Reaction), _txReaction, _ddReaction, _txArrowScale, _slArrowScale);

            // Beam force diagrams are coloured by the force, not by the gradient; and the model by
            // its elements.
            bool any = !ModelOnly;
            Show(_results.Any(x => x.Tick.Active && x.Family != ResultFamily.BeamForce), _ckBands);
            Show(any, _ckValues);
            Show(!any, _txColourBy, _ddColourBy);

            // A scale and an animation over a shape that is not being deformed do nothing.
            bool deformed = _ckDeformed?.Active ?? false;
            Show(deformed, _txDeformScale, _slDeformScale, _ckAnimate);

            Show(Labelled, _txTextSize, _slTextSize);
            Show(_ckLocalAxes?.Active ?? false, _txAxesSize, _slAxesSize);
            Show(_ckReleases?.Active ?? false, _txReleaseSize, _slReleaseSize);

            // Nothing is animating a shape that is no longer being deformed.
            if (!deformed && (_ckAnimate?.Active ?? false))
                StopAnimation();

            // Painted when the canvas next paints, rather than here and now: a change that runs the
            // solution has the canvas painted again at its end, and painting it twice for one click
            // took some 40 ms more on a busy canvas.
            Attributes?.ExpireLayout();
            Grasshopper.Instances.ActiveCanvas?.Invalidate();
        }

        /// <summary>Whether anything is written on the model, which is what Text size sizes.</summary>
        private bool Labelled =>
            (!ModelOnly && (_ckValues?.Active ?? false))
            || (_ckNodeIds?.Active ?? false) || (_ckElementIds?.Active ?? false)
            || (_ckSectionNames?.Active ?? false) || (_ckShowLoadValues?.Active ?? false);

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

        /// <summary>Whether no result is ticked: the elements coloured by Colour by, and no result.</summary>
        private bool ModelOnly => _results == null || !_results.Any(x => x.Tick.Active);

        private bool Ticked(ResultFamily family) => _results?.Any(x => x.Family == family && x.Tick.Active) ?? false;

        /// <summary>
        /// One result ticked on its own, at a component, as clicks in the Results menu would - or,
        /// given 0, none, for the model alone. For scripts that build definitions: a result is its
        /// place in the menu, from 1 for Displacement to 7 for Reactions.
        /// </summary>
        internal void ShowResult(int result, int component)
        {
            foreach (var each in _results)
                each.Tick.Active = false;

            if (result >= 1 && result <= _results.Length)
            {
                _results[result - 1].Tick.Active = true;
                _results[result - 1].Pick(component);
            }

            UpdateVisibility();
        }

        /// <summary>The Unit dropdown of a result, by its place in the menu from 1, for one kind of quantity, by its place in Quantity.</summary>
        internal MenuDropDown UnitOf(int result, int quantity) => _results[result - 1].Units[quantity];

        private Alpaca4d.Visualisation.ColourBy ColourBy =>
            (Alpaca4d.Visualisation.ColourBy)Math.Max(0, Math.Min(2, _ddColourBy?.Value ?? 0));

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
            _faceEdges.Clear();
            _ghostMeshes.Clear();
            _ghostCurves.Clear();
            _curves.Clear();
            _diagrams.Clear();
            _arrows.Clear();
            _labels.Clear();
            _beamSolids.Clear();
            _beamEdges.Clear();
            _outlines.Clear();
            _strips.Clear();
            _sections.Clear();
            _shown.Clear();
            _painter.Clear();
            _shadedJoined = _faceEdgesJoined = _ghostJoined = _diagramsJoined = _beamSolidsJoined = _beamEdgesJoined = null;
            _box = BoundingBox.Empty;
            ClearOverlays();
        }

        /// <summary>
        /// Joins what the solution built, a mesh per list, and lets the pieces go - one per face of every
        /// element, which on a large solid is a great many meshes to keep.
        /// </summary>
        private void Join()
        {
            _shadedJoined = Joined.Of(_shaded);
            _faceEdgesJoined = Joined.Of(_faceEdges);
            _ghostJoined = Joined.Of(_ghostMeshes);
            _diagramsJoined = Joined.Of(_diagrams);
            _beamSolidsJoined = Joined.Of(_beamSolids);
            _beamEdgesJoined = Joined.Of(_beamEdges);
            _shaded.Clear();
            _faceEdges.Clear();
            _ghostMeshes.Clear();
            _diagrams.Clear();
            _beamSolids.Clear();
            _beamEdges.Clear();
        }

        // Animating re-solves twenty times a second, and every solve would otherwise reopen the
        // recorder file. Only the scale changes between frames, so each read is kept against the
        // question that produced it, a result and a component at a time - and let go of when a
        // solution no longer asks for it.
        private readonly Dictionary<Tuple<ResultFamily, int>, Tuple<string, ResultField>> _fieldCache = new Dictionary<Tuple<ResultFamily, int>, Tuple<string, ResultField>>();
        private readonly HashSet<Tuple<ResultFamily, int>> _fieldsUsed = new HashSet<Tuple<ResultFamily, int>>();
        private string _displacementKey;
        private Dictionary<int, Vector3d> _cachedDisplacement;

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _model = null;
            _field = null;
            Clear();

            if (!DA.GetData(0, ref _model) || _model == null) return;

            // Needs a license whatever the model. Refused mid-animation, the timer would go on
            // re-solving twenty times a second only to be refused each time, so it stops too. The
            // model is let go of as well, or the viewport would go on drawing the last one this
            // component was allowed to show.
            if (!LicenseGate.Allows(this, "Model View"))
            {
                _model = null;
                StopAnimation();
                return;
            }

            DA.SetData(0, _model);

            FitScaleRange();

            int step = 0;
            DA.GetData(1, ref step);

            _filter = ElementFilterInput.Read(DA, 2, this);

            int loadPattern = -1;
            _loadPatternId = DA.GetData(LoadPatternInput, ref loadPattern) ? loadPattern : (int?)null;

            _releaseSize = Alpaca4d.Visualisation.ReleaseSymbols.Size(_model);

            var ticked = _results.Where(x => x.Tick.Active).ToList();
            if (ticked.Count > 0 && !_model.IsAnalysed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{string.Join(", ", ticked.Select(x => ResultField.FamilyNames[(int)x.Family]))} need an analysed model, from Run Analysis or another analysis. " +
                    "The model is drawn as it is; untick the results under Results to leave it at that.");
                ticked.Clear();
            }

            _fieldsUsed.Clear();
            bool ok = ticked.Count == 0 ? SolveModel(DA, step) : SolveResults(DA, step, ticked);

            // A result or a component unticked lets go of what was read for it.
            foreach (var key in _fieldCache.Keys.Where(x => !_fieldsUsed.Contains(x)).ToList())
                _fieldCache.Remove(key);

            Join();
            _box = Extent();

            if (ok)
                DA.SetDataList(1, Geometry());
        }

        /// <summary>
        /// No result: every element in the colour Colour by gives it, on the deformed shape when
        /// that is ticked and the model has one.
        /// </summary>
        private bool SolveModel(IGH_DataAccess DA, int step)
        {
            _banded = false;
            _colours = new Alpaca4d.Visualisation.ElementColours(_model, ColourBy);

            Dictionary<int, Vector3d> displacement = null;
            if (_ckDeformed?.Active ?? false)
            {
                if (_model.IsAnalysed)
                    displacement = Deformation(step);
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "Deformed shape needs an analysed model, from Run Analysis or another analysis. The model is drawn as it was built.");
            }

            BuildModel(_filter, displacement);

            DA.SetData(2, $"Model - coloured by {ColourBy}");
            return true;
        }

        /// <summary>
        /// A result as this solution draws it: what was read for it, and the scale it is coloured
        /// over. The code that builds the drawing reads these through <see cref="Use"/>.
        /// </summary>
        private sealed class ShownResult
        {
            public ResultFamily Family;
            public int Component;
            public ResultUnit Unit;
            public ResultField Field;
            public double Min, Max;
            public SortedDictionary<double, Color> Gradient;
            public bool Banded;
            public double DiagramUnit;

            /// <summary>Whether its scale comes from Range rather than from what is drawn, and whether anything was drawn to fit it to.</summary>
            public bool Ranged, Drawn;

            /// <summary>The elements it colours, or for beam forces, the beams it draws a diagram on.</summary>
            public readonly List<IElement> Elements = new List<IElement>();

            public string Name => ResultField.FamilyNames[(int)Family];

            /// <summary>Coloured by the gradient: everything but beam forces, whose diagrams have colours of their own.</summary>
            public bool Graded => Family != ResultFamily.BeamForce;

            /// <summary>Colours elements, rather than drawing beside them as diagrams and arrows do.</summary>
            public bool Paints => Family != ResultFamily.BeamForce && Family != ResultFamily.Reaction;
        }

        /// <summary>A result made the one the building code reads; see <see cref="ShownResult"/>.</summary>
        private void Use(ShownResult shown)
        {
            _field = shown.Field;
            _min = shown.Min;
            _max = shown.Max;
            _gradient = shown.Gradient;
            _banded = shown.Banded;
            _diagramUnit = shown.DiagramUnit;
        }

        /// <summary>
        /// The results ticked, read out of the recorder file of the analysed model and drawn on it
        /// together.
        ///
        /// A face shows one colour, so each element is coloured by one result: its own force or
        /// stress before the displacement, which colours whatever is left - shells by their stress
        /// and the beams around them by how far they moved. Beam forces and reactions colour
        /// nothing, and are drawn over whatever does.
        /// </summary>
        private bool SolveResults(IGH_DataAccess DA, int step, List<ResultControls> ticked)
        {
            var palette = new List<Color>();
            if (!DA.GetDataList(3, palette) || palette.Count < 2)
                palette = Alpaca4d.Colors.Gradient(0);
            _palette = palette;

            foreach (var controls in ticked)
                _shown.AddRange(Read(controls, step));

            if (_shown.Count == 0) return false;

            // Drawn solid, a beam has a section to paint a stress over, and one that differs across
            // it is worked out point by point there - before the range, which has to span it.
            var beamStress = _shown.FirstOrDefault(x => x.Family == ResultFamily.BeamStress);
            if (beamStress != null && (_ckExtruded?.Active ?? false)
                && ResultField.VariesOverSection(beamStress.Family, beamStress.Component) && beamStress.Field.BySection != null)
            {
                foreach (var beam in _model.Beams)
                {
                    BeamSectionField section;
                    if (!beam.Id.HasValue || beam.Curve == null || !beamStress.Field.BySection.TryGetValue(beam.Id.Value, out section))
                        continue;

                    Vector3d y, z;
                    var strips = Strips(beam, section.Shape);
                    if (strips != null && LocalAxes(beam, beam.Curve.PointAtStart, beam.Curve.PointAtEnd, out y, out z))
                        _sections[beam.Id.Value] = Tuple.Create(section, strips);
                }
            }

            SortElements();

            var ranges = new List<Interval>();
            DA.GetDataList(4, ranges);

            // The range is fitted to what is on screen, not to the whole model: a filter narrowed
            // to one beam is asking about that beam, and a gradient stretched over a maximum
            // somewhere else would paint it all one colour. Nor to elements another result colours.
            int painters = _shown.Count(x => x.Paints);
            int graded = 0;
            foreach (var shown in _shown)
            {
                Use(shown);
                bool everything = _filter.MatchesEverything && (!shown.Paints || painters == 1);
                var values = ShownValues(shown, everything).ToList();
                shown.Drawn = values.Count > 0;

                if (shown.Graded && graded < ranges.Count && ranges[graded].IsValid)
                {
                    shown.Min = ranges[graded].Min;
                    shown.Max = ranges[graded].Max;
                    shown.Ranged = true;
                }
                else
                {
                    shown.Min = values.Count > 0 ? values.Min() : 0.0;
                    shown.Max = values.Count > 0 ? values.Max() : 0.0;
                }

                if (shown.Graded) graded++;

                shown.Gradient = ColourScale.Stops(palette, shown.Min, shown.Max);
                shown.Banded = (_ckBands?.Active ?? false) && shown.Graded;

                if (shown.Family == ResultFamily.BeamForce)
                {
                    Use(shown);
                    shown.DiagramUnit = DiagramUnit(_filter);
                }
            }

            if (ranges.Count > graded)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Range has {ranges.Count} intervals for {graded} result{(graded == 1 ? "" : "s")} coloured by a gradient; the rest are not used.");

            // A result that lost every element to another, which would otherwise just not be there.
            var colouring = _shown.Where(x => x.Paints && x.Elements.Count > 0).Select(x => x.Name.ToLowerInvariant()).ToList();
            foreach (var shown in _shown.Where(x => x.Paints && x.Elements.Count == 0 && colouring.Count > 0))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{shown.Name} colour nothing here: every element shown is coloured by {string.Join(" and ", colouring)}. " +
                    "An element shows one colour; untick that to see these.");

            Dictionary<int, Vector3d> displacement = null;
            if (_ckDeformed?.Active ?? false)
            {
                try
                {
                    displacement = Deformation(step);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    return false;
                }
            }

            Build(_filter, displacement);

            DA.SetDataList(2, _shown.Select(x => $"{x.Name} - {x.Field.Label} [{x.Unit.Symbol}], step {step}, min {x.Min:G4}, max {x.Max:G4}"));

            // The scale, not the values drawn: Legend reads its range from the ends of what it is
            // given, and the values drawn knew nothing of a Range, so it took a second wire to put
            // Range into Legend as well. Nothing drawn and no Range gives no scale.
            //
            // With iso-surfaces, the edges of the bands, one more than there are colours, which
            // Legend takes as its cue to write them between its swatches rather than on them.
            //
            // A branch per result, so that each can have a Legend of its own.
            var scales = new GH_Structure<GH_Number>();
            var colours = new GH_Structure<GH_Colour>();
            int branch = 0;
            foreach (var shown in _shown.Where(x => x.Graded))
            {
                var path = DA.ParameterTargetPath(3).AppendElement(branch++);
                scales.EnsurePath(path);
                if (shown.Ranged || shown.Drawn)
                    scales.AppendRange(ColourScale.LabelValues(shown.Min, shown.Max, palette.Count + (shown.Banded ? 1 : 0)).Select(x => new GH_Number(x)), path);
                colours.AppendRange(palette.Select(x => new GH_Colour(x)), path);
            }
            DA.SetDataTree(3, scales);
            DA.SetDataTree(4, colours);
            return true;
        }

        /// <summary>
        /// One result ticked, read out of the recorder file in the unit picked for it: a shown
        /// result per component picked, which is one but for beam forces. None, with an error, when
        /// it cannot be read.
        /// </summary>
        private List<ShownResult> Read(ResultControls controls, int step)
        {
            var family = controls.Family;
            string name = ResultField.FamilyNames[(int)family];
            var picked = controls.Picked.ToList();
            if (picked.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{name} has none of its components ticked: tick one or more under it to draw them.");
                return new List<ShownResult>();
            }

            int layer = family == ResultFamily.ShellStress ? _ddLayer?.Value ?? 0 : 0;
            bool localAxes = family == ResultFamily.BrickStress && (_ddAxes?.Value ?? 0) == 1;

            int UnitIndex(int component) =>
                controls.UnitOf(component)?.Value ?? ResultUnits.Default(ResultField.QuantityOf(family, component));

            ResultUnit UnitOf(int component) => ResultUnits.At(ResultField.QuantityOf(family, component), UnitIndex(component));

            string KeyOf(int component) => $"{_model.GetHashCode()}|{layer}|{step}|{localAxes}|{UnitOf(component).Symbol}";

            var missing = picked.Where(c =>
            {
                Tuple<string, ResultField> cached;
                return !_fieldCache.TryGetValue(Tuple.Create(family, c), out cached) || cached.Item1 != KeyOf(c);
            }).ToList();

            if (missing.Count > 0)
            {
                try
                {
                    // Beam forces all come from one read, however many are ticked.
                    var fields = family == ResultFamily.BeamForce
                        ? ResultField.BeamForces(_model, missing, step)
                        : missing.ToDictionary(c => c, c => ResultField.Read(_model, family, c, layer, step, localAxes));

                    // Into the unit picked straight away, so everything after - the range, Range
                    // itself, the colours, the values written on the model, Values - is in it.
                    // The diagrams and arrows are sized against their own largest value, so they
                    // come out the same in any unit.
                    foreach (var entry in fields)
                    {
                        entry.Value.Scale(ResultUnits.Factor(ResultField.QuantityOf(family, entry.Key), UnitIndex(entry.Key)));
                        _fieldCache[Tuple.Create(family, entry.Key)] = Tuple.Create(KeyOf(entry.Key), entry.Value);
                    }
                }
                catch (Exception ex)
                {
                    foreach (var c in missing)
                        _fieldCache.Remove(Tuple.Create(family, c));
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{name}: {ex.Message}");
                    return new List<ShownResult>();
                }
            }

            return picked.Select(c =>
            {
                var key = Tuple.Create(family, c);
                _fieldsUsed.Add(key);
                return new ShownResult { Family = family, Component = c, Unit = UnitOf(c), Field = _fieldCache[key].Item2 };
            }).ToList();
        }

        /// <summary>
        /// The result each element shown is coloured by: the first painting result, in the order of
        /// the menu, that has a value for it - a beam's stress, a shell's force or stress, a solid's
        /// stress - and failing those, the displacement. Each beam force ticked takes every beam
        /// shown, to draw a diagram on.
        /// </summary>
        private void SortElements()
        {
            var painters = _shown.Where(x => x.Paints && x.Family != ResultFamily.Displacement)
                                 .Concat(_shown.Where(x => x.Family == ResultFamily.Displacement))
                                 .ToList();
            var forces = _shown.Where(x => x.Family == ResultFamily.BeamForce).ToList();

            foreach (var element in _model.Beams.Cast<IElement>().Concat(_model.Shells).Concat(_model.Bricks))
            {
                if (!_filter.Matches(element)) continue;

                var painter = painters.FirstOrDefault(x => HasValue(x, element));
                if (painter != null)
                {
                    painter.Elements.Add(element);
                    _painter[element] = painter;
                }

                if (element is IBeam)
                    foreach (var force in forces)
                        force.Elements.Add(element);
            }
        }

        private bool HasValue(ShownResult shown, IElement element)
        {
            var field = shown.Field;
            if (shown.Family == ResultFamily.Displacement)
                return field.ByNode != null;

            if (!element.Id.HasValue) return false;
            int tag = element.Id.Value;

            List<double> values;
            return (field.BySection != null && _sections.ContainsKey(tag))
                || (field.ByElement != null && field.ByElement.TryGetValue(tag, out values) && values != null && values.Count > 0);
        }

        /// <summary>
        /// The displacement of every node at the deformation scale, the mode fitted to the model.
        /// Read only with Deformed shape ticked - a stress drawn on the shape it was built in has no
        /// use for it - and kept against the model and step, as the field is.
        /// </summary>
        private Dictionary<int, Vector3d> Deformation(int step)
        {
            var key = $"{_model.GetHashCode()}|{step}";
            if (key != _displacementKey)
            {
                _displacementKey = null;
                _cachedDisplacement = NodalOffsets(step);
                _displacementKey = key;
            }

            double scale = (_slDeformScale?.Value ?? 1.0) * ModeFit();
            return _cachedDisplacement.ToDictionary(x => x.Key, x => x.Value * scale);
        }

        /// <summary>
        /// What is drawn, as geometry for the Geometry output: the meshes in the colours they are
        /// drawn in, and the lines without theirs, which a curve cannot carry. What is ghosted is
        /// given its grey as vertex colours, as a preview of a mesh draws those; it is drawn see-through
        /// here, which a mesh cannot carry either.
        /// </summary>
        private List<GeometryBase> Geometry()
        {
            var geometry = new List<GeometryBase>();

            foreach (var mesh in new[] { _shadedJoined, _beamSolidsJoined, _diagramsJoined })
                if (mesh != null) geometry.Add(mesh);

            if (_ghostJoined != null)
            {
                var ghost = _ghostJoined.DuplicateMesh();
                ghost.VertexColors.CreateMonotoneMesh(GhostColour);
                geometry.Add(ghost);
            }

            geometry.AddRange(_curves.Curves());
            geometry.AddRange(_ghostCurves.Curves());
            geometry.AddRange(_arrows.Select(arrow => new LineCurve(arrow.Item1)));

            return geometry;
        }

        #endregion

        #region Viewport

        // The base class is not called on to draw: it would draw the Geometry output a second time,
        // shaded in Grasshopper's own colours, over what is drawn here.

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            if (this.Hidden || this.Locked || _model == null) return;

            // The solid geometry first and the ghosts last: a transparent material blends against
            // what is already in the depth buffer, so drawing it first would let it hide the very
            // elements it is meant to sit behind.
            if (_shadedJoined != null)
                args.Display.DrawMeshFalseColors(_shadedJoined);

            if (_beamSolidsJoined != null)
                args.Display.DrawMeshFalseColors(_beamSolidsJoined);

            if (_diagramsJoined != null)
                args.Display.DrawMeshFalseColors(_diagramsJoined);

            if (_ghostJoined != null)
                args.Display.DrawMeshShaded(_ghostJoined, new DisplayMaterial(GhostColour, 0.75));
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (this.Hidden || this.Locked || _model == null) return;

            var ghostLine = Color.FromArgb(90, GhostColour);

            // The model's edges as Model View drew them, a little heavier than a result's, which
            // would otherwise be lost among the colours.
            bool model = _shown.Count == 0;
            int edge = model ? 2 : 1;

            _ghostCurves.Draw(args.Display, 1);

            if (_ckWires?.Active ?? false)
            {
                if (_ghostJoined != null)
                    args.Display.DrawMeshWires(_ghostJoined, ghostLine, 1);

                if (_faceEdgesJoined != null)
                    args.Display.DrawMeshWires(_faceEdgesJoined, Color.Black, edge);

                if (_beamEdgesJoined != null)
                    args.Display.DrawMeshWires(_beamEdgesJoined, Color.Black, edge);
            }

            if (_diagramsJoined != null)
                args.Display.DrawMeshWires(_diagramsJoined, Color.Black, 1);

            _curves.Draw(args.Display, model ? 2 : 3);

            foreach (var arrow in _arrows)
                args.Display.DrawArrow(arrow.Item1, arrow.Item2);

            if (!model && (_ckValues?.Active ?? false) && _labels.Count > 0)
            {
                double height = _slTextSize?.Value ?? 0.5;

                var colour = AlpacaSettings.Colour(DisplayItem.Values);

                var towardsCamera = -args.Viewport.CameraDirection;
                towardsCamera.Unitize();

                foreach (var label in _labels)
                {
                    // Squared to the camera, so a number reads from wherever the model is spun to.
                    var plane = new Plane(label.Item1 + towardsCamera * label.Item3, args.Viewport.CameraX, args.Viewport.CameraY);
                    args.Display.Draw3dText(label.Item2.ToString("G4"), colour, plane, height, "Arial", false, false);
                }
            }

            DrawOverlays(args);
        }

        public override bool IsPreviewCapable => true;

        public override BoundingBox ClippingBox => _box.IsValid ? _box : base.ClippingBox;

        /// <summary>
        /// Everything this component draws, with the scales and toggles in force, so Rhino can set
        /// its clipping planes around it. A box that stops short of what is drawn lets Rhino put a
        /// clipping plane through it; one far too big puts the near plane out in front of the
        /// camera, and zoomed in, cuts away the model - and everything else in the viewport with it,
        /// since the planes are shared.
        /// </summary>
        private BoundingBox Extent()
        {
            var box = BoundingBox.Empty;
            if (_model == null) return box;

            foreach (var mesh in new[] { _shadedJoined, _beamSolidsJoined, _ghostJoined, _diagramsJoined })
                if (mesh != null) box.Union(mesh.GetBoundingBox(false));
            box.Union(_curves.BoundingBox());
            box.Union(_ghostCurves.BoundingBox());
            foreach (var arrow in _arrows) box.Union(arrow.Item1.BoundingBox);

            // Labels too, grown by how far they stand off and how tall they are.
            if (_shown.Count > 0 && (_ckValues?.Active ?? false) && _labels.Count > 0)
            {
                double reach = _labels.Max(x => x.Item3) + 2.0 * (_slTextSize?.Value ?? 0.5);
                foreach (var label in _labels)
                    box.Union(new BoundingBox(label.Item1 - new Vector3d(reach, reach, reach),
                                              label.Item1 + new Vector3d(reach, reach, reach)));
            }

            box.Union(OverlayExtent());
            return box;
        }

        #endregion

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.model_View__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{F6D1EFD4-82A3-4A0F-B4B7-6DD0EE9FCADE}");
    }
}
