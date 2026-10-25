using Alpaca4d.TimeSeries;
using Alpaca4d.UIWidgets;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Gh
{
    public class ModelView : GH_ExtendableComponent
    {
        private Alpaca4d.Model _model = null;

        private List<Mesh> _beamMeshes = new List<Mesh>();
        private List<Mesh> _shellMeshes = new List<Mesh>();
        private List<Mesh> _brickMeshes = new List<Mesh>();

        // Beams with no outline to extrude, drawn as lines when Extruded is on rather than not at all.
        private readonly List<Alpaca4d.Generic.IBeam> _lineBeams = new List<Alpaca4d.Generic.IBeam>();

        private int? _loadPatternId = null;
        private HashSet<int> _visibleElementIds = null; // null = show all

        // Menu references – stored so plug registration can happen in CreateAttributes
        private GH_ExtendableMenu _elemMenu;
        private GH_ExtendableMenu _loadsMenu;

        // Widget controls – Elements menu
        private MenuCheckBox _ckExtruded;
        private MenuCheckBox _ckNodeIds;
        private MenuCheckBox _ckElementIds;
        private MenuCheckBox _ckSectionNames;
        private MenuCheckBox _ckLocalAxes;
        private MenuStaticText _txColourBy;
        private MenuDropDown _ddColourBy;
        private MenuStaticText _txTextSize;
        private MenuSlider   _slTextSize;
        private MenuStaticText _txAxesSize;
        private MenuSlider   _slAxesSize;
        private MenuCheckBox _ckReleases;
        private MenuStaticText _txReleaseSize;
        private MenuSlider   _slReleaseSize;

        // The radius of a release circle at Release size 1; see ReleaseSymbols.Size.
        private double _releaseSize;

        // What the viewport draws, worked out in SolveInstance; see Extent.
        private BoundingBox _box = BoundingBox.Empty;

        // The colour each element is drawn in, by the Colour by dropdown; worked out in SolveInstance.
        private Alpaca4d.Visualisation.ElementColours _colours;

        // Widget controls – Loads menu
        private MenuCheckBox _ckShowLoads;
        private MenuCheckBox _ckShowLoadValues;
        private MenuSlider   _slLoadScale;

        // Widget controls – Supports menu
        private MenuCheckBox _ckShowSupports;
        private MenuSlider   _slSupportScale;
        private MenuCheckBox _ckShowConstraints;

        public ModelView()
          : base("Model View (Alpaca4d)", "ModelView",
            "Visualise the assembled model in the viewport",
            "Alpaca4d", "09_Visualisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        public override void CreateAttributes()
        {
            base.CreateAttributes(); // creates GH_ExtendableComponentAttributes and calls Setup()

            // Register input plugs here – params are guaranteed to be populated at this point
            // in both the "new component" flow and the file-load flow.
            if (_elemMenu  != null && Params.Input.Count > 2)
                _elemMenu.RegisterInputPlug(new ExtendedPlug(Params.Input[2]));
            if (_loadsMenu != null && Params.Input.Count > 1)
                _loadsMenu.RegisterInputPlug(new ExtendedPlug(Params.Input[1]));
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The assembled Alpaca model", GH_ParamAccess.item);
            // index 1: LP – will appear as plug inside the Loads menu
            pManager.AddIntegerParameter("LoadPattern", "LP", "Optional LoadPattern Id to filter load visualisation. If not provided all load patterns are shown.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            // index 2: ElementIds – will appear as plug inside the Elements menu
            pManager.AddIntegerParameter("ElementIds", "ElemIds", "Optional list of Element Ids to show. If not provided all elements are shown.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("AlpacaModel", "AlpacaModel", "The model passed straight through, so this component can sit in the middle of a chain rather than at the end of one.");
        }

        #region UI Setup

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            // ── Elements ─────────────────────────────────────────────────────
            var elemMenu = new GH_ExtendableMenu(0, "Elements");
            elemMenu.Name = "Elements";
            elemMenu.Header = "Element display options";

            var elemPanel = new MenuPanel(0, "elements_panel");
            // On unless a saved file says otherwise: the section is the first thing to check.
            _ckExtruded     = new MenuCheckBox(0, "Extruded",     "Extruded") { Active = true };
            _ckNodeIds      = new MenuCheckBox(1, "NodeIds",      "Node IDs");
            _ckElementIds   = new MenuCheckBox(2, "ElementIds",   "Element IDs");
            _ckSectionNames = new MenuCheckBox(3, "SectionNames", "Section Names");
            _ckLocalAxes    = new MenuCheckBox(4, "LocalAxes",    "Local Axes");
            // On unless a saved file says otherwise: it draws nothing where nothing is released.
            _ckReleases     = new MenuCheckBox(5, "Releases",     "End Releases") { Active = true };

            _ckExtruded.ValueChanged     += OnWidgetChanged;
            _ckNodeIds.ValueChanged      += OnWidgetChanged;
            _ckElementIds.ValueChanged   += OnWidgetChanged;
            _ckSectionNames.ValueChanged += OnWidgetChanged;
            _ckLocalAxes.ValueChanged    += OnWidgetChanged;
            _ckReleases.ValueChanged     += OnWidgetChanged;

            elemPanel.AddControl(_ckExtruded);
            elemPanel.AddControl(_ckNodeIds);
            elemPanel.AddControl(_ckElementIds);
            elemPanel.AddControl(_ckSectionNames);
            elemPanel.AddControl(_ckLocalAxes);
            elemPanel.AddControl(_ckReleases);

            // Each element in its own colour, or every element of a section, or of a material, in
            // one - the one the Section or Material Library gave it, or one picked here. In the
            // order of ColourBy, which the saved index is read back against.
            _txColourBy = new MenuStaticText { Text = "Colour by" };
            _ddColourBy = new MenuDropDown(0, "ColourBy", "ColourBy") { VisibleItemCount = 3 };
            foreach (var name in Enum.GetNames(typeof(Alpaca4d.Visualisation.ColourBy)))
                _ddColourBy.AddItem(name, name);
            _ddColourBy.ValueChanged += OnWidgetChanged;
            elemPanel.AddControl(_txColourBy);
            elemPanel.AddControl(_ddColourBy);

            // Height of the ids and section names, in model units, as View Results sizes its
            // values; and how long the local axes are drawn. Each shown only while it has
            // something to size.
            _txTextSize = new MenuStaticText { Text = "Text size" };
            _slTextSize = new MenuSlider(0, "TextSize", 0.05, 5.0, 0.5, 2);
            _slTextSize.ValueChanged += OnWidgetChanged;
            _txAxesSize = new MenuStaticText { Text = "Axes size" };
            _slAxesSize = new MenuSlider(1, "AxesSize", 0.1, 10.0, 1.0, 1);
            _slAxesSize.ValueChanged += OnWidgetChanged;
            elemPanel.AddControl(_txTextSize);
            elemPanel.AddControl(_slTextSize);
            elemPanel.AddControl(_txAxesSize);
            elemPanel.AddControl(_slAxesSize);

            // How big the release circles and lines are drawn, against a twentieth of a typical beam.
            _txReleaseSize = new MenuStaticText { Text = "Release size" };
            _slReleaseSize = new MenuSlider(2, "ReleaseSize", 0.1, 5.0, 1.0, 1);
            _slReleaseSize.ValueChanged += OnWidgetChanged;
            elemPanel.AddControl(_txReleaseSize);
            elemPanel.AddControl(_slReleaseSize);

            elemMenu.AddControl(elemPanel);
            _elemMenu = elemMenu;         // store for plug registration in CreateAttributes
            attr.AddMenu(elemMenu);

            // ── Loads ─────────────────────────────────────────────────────────
            var loadsMenu = new GH_ExtendableMenu(1, "Loads");
            loadsMenu.Name = "Loads";
            loadsMenu.Header = "Load display options";

            var loadsPanel = new MenuPanel(1, "loads_panel");
            _ckShowLoads = new MenuCheckBox(0, "ShowLoads", "Show Loads");
            // What each load is worth, written at the tail of its arrow; with or without the arrows.
            _ckShowLoadValues = new MenuCheckBox(1, "ShowLoadValues", "Show Values");
            _slLoadScale = new MenuSlider(0, "LoadScale", 0.1, 10.0, 1.0, 1);
            _slLoadScale.Name = "Load Scale";

            _ckShowLoads.ValueChanged      += OnWidgetChanged;
            _ckShowLoadValues.ValueChanged += OnWidgetChanged;
            _slLoadScale.ValueChanged      += OnWidgetChanged;

            loadsPanel.AddControl(_ckShowLoads);
            loadsPanel.AddControl(_ckShowLoadValues);
            loadsPanel.AddControl(_slLoadScale);
            loadsMenu.AddControl(loadsPanel);
            _loadsMenu = loadsMenu;       // store for plug registration in CreateAttributes
            attr.AddMenu(loadsMenu);

            // ── Supports ──────────────────────────────────────────────────────
            var supportsMenu = new GH_ExtendableMenu(2, "Supports");
            supportsMenu.Name = "Supports";
            supportsMenu.Header = "Support display options";

            var supportsPanel = new MenuPanel(2, "supports_panel");
            _ckShowSupports    = new MenuCheckBox(0, "ShowSupports",    "Show Supports");
            _slSupportScale    = new MenuSlider(0, "SupportScale", 0.1, 10.0, 1.0, 1);
            _slSupportScale.Name = "Support Scale";
            _ckShowConstraints = new MenuCheckBox(1, "ShowConstraints", "Show Constraints");

            _ckShowSupports.ValueChanged    += OnWidgetChanged;
            _slSupportScale.ValueChanged    += OnWidgetChanged;
            _ckShowConstraints.ValueChanged += OnWidgetChanged;

            supportsPanel.AddControl(_ckShowSupports);
            supportsPanel.AddControl(_slSupportScale);
            supportsPanel.AddControl(_ckShowConstraints);
            supportsMenu.AddControl(supportsPanel);
            attr.AddMenu(supportsMenu);

            attr.MinWidth = 200f;

            UpdateVisibility();
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            if (_ckExtruded == null) return;

            _ckExtruded.ValueChanged     += OnWidgetChanged;
            _ckNodeIds.ValueChanged      += OnWidgetChanged;
            _ckElementIds.ValueChanged   += OnWidgetChanged;
            _ckSectionNames.ValueChanged += OnWidgetChanged;
            _ckLocalAxes.ValueChanged    += OnWidgetChanged;
            _ckShowLoads.ValueChanged    += OnWidgetChanged;
            _ckShowLoadValues.ValueChanged += OnWidgetChanged;
            _slLoadScale.ValueChanged    += OnWidgetChanged;
            _ckShowSupports.ValueChanged    += OnWidgetChanged;
            _slSupportScale.ValueChanged    += OnWidgetChanged;
            _ckShowConstraints.ValueChanged += OnWidgetChanged;
            _slTextSize.ValueChanged        += OnWidgetChanged;
            _slAxesSize.ValueChanged        += OnWidgetChanged;
            _ddColourBy.ValueChanged        += OnWidgetChanged;
            _ckReleases.ValueChanged        += OnWidgetChanged;
            _slReleaseSize.ValueChanged     += OnWidgetChanged;

            // Visibility is not saved: it follows from the checkboxes.
            UpdateVisibility();
        }

        private void OnWidgetChanged(object sender, EventArgs e)
        {
            UpdateVisibility();
            ExpireSolution(true);
        }

        /// <summary>The text and axes size sliders, each put away while it has nothing to size.</summary>
        private void UpdateVisibility()
        {
            if (_slTextSize == null) return;

            bool labelled = (_ckNodeIds?.Active ?? false) || (_ckElementIds?.Active ?? false)
                         || (_ckSectionNames?.Active ?? false) || (_ckShowLoadValues?.Active ?? false);
            bool axes = _ckLocalAxes?.Active ?? false;
            bool releases = _ckReleases?.Active ?? false;

            _txTextSize.Visible = labelled;
            _slTextSize.Visible = labelled;
            _txAxesSize.Visible = axes;
            _slAxesSize.Visible = axes;
            if (_slReleaseSize != null)
            {
                _txReleaseSize.Visible = releases;
                _slReleaseSize.Visible = releases;
            }

            Attributes?.ExpireLayout();
            Grasshopper.Instances.ActiveCanvas?.Refresh();
        }

        #endregion

        protected override void BeforeSolveInstance()
        {
            _beamMeshes.Clear();
            _shellMeshes.Clear();
            _brickMeshes.Clear();
            _lineBeams.Clear();
            _colours = null;
            _box = BoundingBox.Empty;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData(0, ref _model);
            if (_model == null) return;

            // Needs a license whatever the model. The model is let go of as well, or the viewport
            // would go on drawing the last one this component was allowed to show.
            if (!LicenseGate.Allows(this, "Model View"))
            {
                _model = null;
                return;
            }

            _loadPatternId = null;
            int lpId = -1;
            if (DA.GetData(1, ref lpId))
                _loadPatternId = lpId;

            _visibleElementIds = null;
            var elemIdList = new List<int>();
            if (DA.GetDataList(2, elemIdList) && elemIdList.Count > 0)
                _visibleElementIds = new HashSet<int>(elemIdList);

            _colours = new Alpaca4d.Visualisation.ElementColours(_model, ColourBy);
            _releaseSize = Alpaca4d.Visualisation.ReleaseSymbols.Size(_model);

            foreach (var item in _model.Beams)
            {
                if (_visibleElementIds != null && item.Id.HasValue && !_visibleElementIds.Contains(item.Id.Value)) continue;

                var beamMesh = ExtrudedBeam(item);
                if (beamMesh == null)
                {
                    _lineBeams.Add(item);
                    continue;
                }

                beamMesh.VertexColors.CreateMonotoneMesh(_colours.Of(item));
                _beamMeshes.Add(beamMesh);
            }
            foreach (var item in _model.Shells)
            {
                if (_visibleElementIds != null && item.Id.HasValue && !_visibleElementIds.Contains(item.Id.Value)) continue;

                var myMesh    = new Mesh();
                var meshTop   = item.Mesh.Offset( item.Section.Thickness / 2, true);
                var meshBottom= item.Mesh.Offset(-item.Section.Thickness / 2, true);
                if (meshTop != null) myMesh.Append(meshTop);
                if (meshBottom != null) myMesh.Append(meshBottom);

                // An offset Rhino could not make leaves the shell drawn flat, not missing.
                if (myMesh.Vertices.Count == 0) myMesh = item.Mesh.DuplicateMesh();
                myMesh.VertexColors.CreateMonotoneMesh(_colours.Of(item));
                _shellMeshes.Add(myMesh);
            }
            foreach (var item in _model.Bricks)
            {
                if (_visibleElementIds != null && item.Id.HasValue && !_visibleElementIds.Contains(item.Id.Value)) continue;

                var brick = item.Mesh;
                brick.VertexColors.CreateMonotoneMesh(_colours.Of(item));
                _brickMeshes.Add(brick);
            }

            DA.SetData(0, _model);

            _box = Extent();

            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (this.Hidden || this.Locked || _model == null) return;

            bool extruded       = _ckExtruded?.Active     ?? false;
            bool showLoads      = _ckShowLoads?.Active    ?? false;
            bool showLoadValues = _ckShowLoadValues?.Active ?? false;
            bool showSupports   = _ckShowSupports?.Active ?? false;
            bool showConstraints= _ckShowConstraints?.Active ?? false;
            bool showNodeIds    = _ckNodeIds?.Active      ?? false;
            bool showElemIds    = _ckElementIds?.Active   ?? false;
            bool showSecNames   = _ckSectionNames?.Active ?? false;
            bool showLocalAxes  = _ckLocalAxes?.Active    ?? false;
            bool showReleases   = _ckReleases?.Active     ?? false;
            double loadScale    = _slLoadScale?.Value     ?? 1.0;
            double supportScale = _slSupportScale?.Value  ?? 1.0;
            double textSize     = _slTextSize?.Value      ?? 0.5;
            double axesSize     = _slAxesSize?.Value      ?? 1.0;

            // ── Elements ─────────────────────────────────────────────────────
            if (extruded)
            {
                foreach (var mesh in _beamMeshes)
                {
                    args.Display.DrawMeshFalseColors(mesh);
                    args.Display.DrawMeshWires(mesh, System.Drawing.Color.Black, 2);
                }
                foreach (var beam in _lineBeams)
                    args.Display.DrawCurve(beam.Curve, Colour(beam));
                foreach (var mesh in _shellMeshes)
                {
                    args.Display.DrawMeshFalseColors(mesh);
                    args.Display.DrawMeshWires(mesh, System.Drawing.Color.Black, 2);
                }
                foreach (var mesh in _brickMeshes)
                {
                    args.Display.DrawMeshFalseColors(mesh);
                    args.Display.DrawMeshWires(mesh, System.Drawing.Color.Black, 2);
                }
            }
            else
            {
                foreach (var beam in _model.Beams)
                {
                    if (_visibleElementIds != null && beam.Id.HasValue && !_visibleElementIds.Contains(beam.Id.Value)) continue;
                    args.Display.DrawCurve(beam.Curve, Colour(beam));
                }
                foreach (var shell in _model.Shells)
                {
                    if (_visibleElementIds != null && shell.Id.HasValue && !_visibleElementIds.Contains(shell.Id.Value)) continue;
                    var mesh = shell.Mesh;
                    mesh.VertexColors.CreateMonotoneMesh(Colour(shell));
                    args.Display.DrawMeshFalseColors(mesh);
                    args.Display.DrawMeshWires(mesh, System.Drawing.Color.Black, 2);
                }
                foreach (var brick in _model.Bricks)
                {
                    if (_visibleElementIds != null && brick.Id.HasValue && !_visibleElementIds.Contains(brick.Id.Value)) continue;
                    args.Display.DrawMeshFalseColors(brick.Mesh);
                    args.Display.DrawMeshWires(brick.Mesh, System.Drawing.Color.Black, 2);
                }
            }

            // ── Node IDs ─────────────────────────────────────────────────────
            if (showNodeIds)
            {
                foreach (var node in _model.Nodes)
                    Label(args, $"N{node.Id}", AlpacaSettings.Colour(DisplayItem.NodeIds), node.Pos, textSize);
            }

            // ── Element IDs ──────────────────────────────────────────────────
            if (showElemIds)
            {
                foreach (var beam in _model.Beams)
                {
                    if (_visibleElementIds != null && beam.Id.HasValue && !_visibleElementIds.Contains(beam.Id.Value)) continue;
                    var mid = beam.Curve.PointAtNormalizedLength(0.5);
                    Label(args, $"E{beam.Id}", AlpacaSettings.Colour(DisplayItem.ElementIds), mid, textSize);
                }
                foreach (var shell in _model.Shells)
                {
                    if (_visibleElementIds != null && shell.Id.HasValue && !_visibleElementIds.Contains(shell.Id.Value)) continue;
                    var centroid = AreaMassProperties.Compute(shell.Mesh).Centroid;
                    Label(args, $"E{shell.Id}", AlpacaSettings.Colour(DisplayItem.ElementIds), centroid, textSize);
                }
                foreach (var brick in _model.Bricks)
                {
                    if (_visibleElementIds != null && brick.Id.HasValue && !_visibleElementIds.Contains(brick.Id.Value)) continue;
                    var centroid = VolumeMassProperties.Compute(brick.Mesh).Centroid;
                    Label(args, $"E{brick.Id}", AlpacaSettings.Colour(DisplayItem.ElementIds), centroid, textSize);
                }
            }

            // ── Section Names ────────────────────────────────────────────────
            if (showSecNames)
            {
                foreach (var beam in _model.Beams)
                {
                    if (_visibleElementIds != null && beam.Id.HasValue && !_visibleElementIds.Contains(beam.Id.Value)) continue;
                    var mid  = beam.Curve.PointAtNormalizedLength(0.5);
                    var name = beam.Section?.SectionName ?? beam.Section?.GetType().Name;
                    Label(args, name, AlpacaSettings.Colour(DisplayItem.SectionNames), mid, textSize);
                }
            }

            // ── Local Axes ───────────────────────────────────────────────────
            if (showLocalAxes)
            {
                foreach (var beam in _model.Beams)
                {
                    if (_visibleElementIds != null && beam.Id.HasValue && !_visibleElementIds.Contains(beam.Id.Value)) continue;

                    var mid    = beam.Curve.PointAtNormalizedLength(0.5);
                    var localX = beam.Curve.PointAtEnd - beam.Curve.PointAtStart;
                    localX.Unitize();

                    DrawElementAxes(args, mid, localX, beam.GeomTransf.LocalY, beam.GeomTransf.LocalZ,
                                    AxesReach(beam) * axesSize);
                }

                // Shells: fxx, mxx and sigma11 act along the red arrow (axis 1), fyy, myy and
                // sigma22 along the green one (axis 2), and the blue one is the normal (axis 3) -
                // the side a positive mxx or myy puts in tension, and the side the Top layer is on.
                //
                // Utils.ShellFrame rather than anything worked out here, because which frame a
                // shell reports in is not obvious - a quad's section axes are turned away from its
                // first edge even with no -local given.
                foreach (var shell in _model.Shells)
                {
                    if (_visibleElementIds != null && shell.Id.HasValue && !_visibleElementIds.Contains(shell.Id.Value)) continue;

                    Plane frame;
                    try
                    {
                        frame = shell.LocalPlane;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    DrawElementAxes(args, frame.Origin, frame.XAxis, frame.YAxis, frame.ZAxis,
                                    AxesReach(shell) * axesSize);
                }

                // Solids: sigma11 is the direct stress along the red arrow, sigma22 the green and
                // sigma33 the blue, when Brick Stresses reads them in local axes.
                foreach (var brick in _model.Bricks)
                {
                    if (_visibleElementIds != null && brick.Id.HasValue && !_visibleElementIds.Contains(brick.Id.Value)) continue;

                    var nodes = brick.Mesh.Vertices.ToPoint3dArray();

                    // Utils.SolidFrame, not brick.LocalPlane, even though the plane is built from
                    // exactly this. It is the same call the stress reader makes to rotate the
                    // tensor, so what is drawn here and what comes out of Brick Stresses cannot
                    // drift apart - there is one frame, read once, in one place.
                    (Vector3d X, Vector3d Y, Vector3d Z) frame;
                    try
                    {
                        frame = Alpaca4d.Utils.SolidFrame(nodes);
                    }
                    catch (Exception)
                    {
                        // A solid too degenerate to have a frame. Nothing to draw, and a viewport
                        // is the wrong place to complain about it - Assemble already does.
                        continue;
                    }

                    var origin = Point3d.Origin;
                    foreach (var node in nodes)
                        origin += node;
                    origin /= nodes.Length;

                    DrawElementAxes(args, origin, frame.X, frame.Y, frame.Z, AxesReach(brick) * axesSize);
                }
            }

            // ── End releases ─────────────────────────────────────────────────
            if (showReleases)
            {
                double size = _releaseSize * (_slReleaseSize?.Value ?? 1.0);
                var view = args.Viewport.CameraDirection;

                // Over the model, as the labels are: a circle smaller than the section would
                // otherwise vanish inside the extruded beam it marks.
                args.Display.PushDepthTesting(false);
                foreach (var beam in _model.Beams)
                {
                    if (_visibleElementIds != null && beam.Id.HasValue && !_visibleElementIds.Contains(beam.Id.Value)) continue;

                    var (circles, lines) = Alpaca4d.Visualisation.ReleaseSymbols.For(beam, size, view);
                    foreach (var (circle, colour) in circles)
                        args.Display.DrawCircle(circle, colour, 2);
                    foreach (var (line, colour) in lines)
                        args.Display.DrawLine(line, colour, 3);
                }
                args.Display.PopDepthTesting();
            }

            // ── Loads ─────────────────────────────────────────────────────────
            if (showLoads || showLoadValues)
            {
                var relevantPatterns = _loadPatternId.HasValue
                    ? _model.LoadPatterns.Where(x => x.Id == _loadPatternId.Value)
                    : (IEnumerable<Alpaca4d.Loads.LoadPattern>)_model.LoadPatterns;

                var patternLoads = relevantPatterns.SelectMany(x => x.Load);

                foreach (var pointLoad in patternLoads.OfType<Alpaca4d.Loads.PointLoad>())
                {
                    var arrow = pointLoad.Force * loadScale;
                    if (showLoads)
                        VisualisePointLoad(args, pointLoad.Pos, arrow);
                    string value = showLoadValues ? PointLoadValue(pointLoad) : "";
                    if (value.Length > 0)
                        Label(args, value, AlpacaSettings.Colour(DisplayItem.PointLoads), pointLoad.Pos - arrow, textSize);
                }

                foreach (var meshLoad in patternLoads.OfType<Alpaca4d.Loads.MeshLoad>())
                {
                    var forceValue = meshLoad.GlobalForce.Length;
                    var unitVector = meshLoad.GlobalForce / forceValue;
                    foreach (var shell in meshLoad.AppliedTo(_model.Shells))
                    {
                        if (showLoads)
                            VisualiseMeshLoad(args, shell.Mesh, forceValue, unitVector, loadScale);
                        if (showLoadValues && forceValue > 0.0)
                            Label(args, LoadValue(forceValue, $"{Units.Force}/{Units.Length}²"), AlpacaSettings.Colour(DisplayItem.AreaLoads),
                                  shell.Mesh.GetBoundingBox(false).Center + meshLoad.GlobalForce * loadScale, textSize);
                    }
                }

                foreach (var lineLoad in patternLoads.OfType<Alpaca4d.Loads.LineLoad>())
                {
                    var arrow = lineLoad.GlobalForce * loadScale;
                    foreach (var beam in lineLoad.AppliedTo(_model.Beams))
                    {
                        if (showLoads)
                            VisualiseLineLoad(args, beam.Curve, lineLoad.GlobalForce, loadScale);
                        // Once a beam, over the middle arrow, rather than over all seven.
                        if (showLoadValues && lineLoad.GlobalForce.Length > 0.0)
                            Label(args, LoadValue(lineLoad.GlobalForce.Length, $"{Units.Force}/{Units.Length}"), AlpacaSettings.Colour(DisplayItem.LineLoads),
                                  beam.Curve.PointAtNormalizedLength(0.5) - arrow, textSize);
                    }
                }
            }

            // ── Supports ─────────────────────────────────────────────────────
            if (showSupports)
            {
                var material = new Rhino.Display.DisplayMaterial(
                    System.Drawing.Color.White, System.Drawing.Color.White,
                    System.Drawing.Color.White, System.Drawing.Color.White, 1.0, 0.0);

                foreach (var support in _model.Supports)
                {
                    if (support.Geometry is Mesh)
                    {
                        var geo   = support.Geometry.DuplicateMesh();
                        var scale = Transform.Scale(Point3d.Origin, supportScale);
                        geo.Transform(scale);
                        geo.Transform(Transform.Translation(new Vector3d(support.Pos)));
                        args.Display.DrawMeshShaded(geo, material);
                        args.Display.DrawMeshWires(geo, System.Drawing.Color.Black, 2);
                    }
                    else if (support.Geometry is string label)
                    {
                        args.Display.DrawDot(support.Pos, label);
                    }
                }
            }
        }

        public override bool IsPreviewCapable => true;

        private Alpaca4d.Visualisation.ColourBy ColourBy =>
            (Alpaca4d.Visualisation.ColourBy)Math.Max(0, Math.Min(2, _ddColourBy?.Value ?? 0));

        private System.Drawing.Color Colour(Alpaca4d.Generic.IBeam beam) => _colours?.Of(beam) ?? beam.Color;

        private System.Drawing.Color Colour(Alpaca4d.Generic.IShell shell) => _colours?.Of(shell) ?? shell.Color;

        /// <summary>
        /// A label squared to the camera, <paramref name="height"/> tall in model units - the Text
        /// size slider, as View Results sizes its values.
        ///
        /// Drawn over the model rather than into it. An id sits on the axis of a beam or inside a
        /// solid, and with Extruded on, depth testing would hide it in the very element it names.
        /// </summary>
        private static void Label(IGH_PreviewArgs args, string text, System.Drawing.Color colour, Point3d at, double height)
        {
            var plane = new Plane(at, args.Viewport.CameraX, args.Viewport.CameraY);

            args.Display.PushDepthTesting(false);
            args.Display.Draw3dText(text, colour, plane, height, "Arial", false, false,
                                    Rhino.DocObjects.TextHorizontalAlignment.Center, Rhino.DocObjects.TextVerticalAlignment.Middle);
            args.Display.PopDepthTesting();
        }

        /// <summary>
        /// One set of element axes: red, green and blue for the first, second and third - x, y, z
        /// on a beam, 1, 2, 3 on a shell or a solid. Unlabelled, because the colours say which is
        /// which. Thick, with a head in proportion to the arrow, so Axes size makes the whole of
        /// it bigger rather than a longer hairline.
        /// </summary>
        private static void DrawElementAxes(IGH_PreviewArgs args, Point3d origin,
                                            Vector3d x, Vector3d y, Vector3d z, double length)
        {
            if (!(length > 0.0)) return;

            DrawAxis(args, new Line(origin, origin + x * length), System.Drawing.Color.Red);
            DrawAxis(args, new Line(origin, origin + y * length), System.Drawing.Color.Green);
            DrawAxis(args, new Line(origin, origin + z * length), System.Drawing.Color.Blue);
        }

        private static void DrawAxis(IGH_PreviewArgs args, Line line, System.Drawing.Color colour)
        {
            if (!line.IsValid || line.Length <= 0.0) return;

            args.Display.DrawLine(line, colour, 3);
            args.Display.DrawArrowHead(line.To, line.Direction, colour, 0.0, line.Length * 0.2);
        }

        /// <summary>
        /// A beam as its cross-section carried from one end to the other, stood up in the beam's
        /// local axes - the section's x along local z, its y along local y, as View Results and
        /// Beam Stresses read it. Every outline, so a hollow section shows its inside and a double
        /// angle both angles. Null when there is no outline to carry, and the beam is drawn as a line.
        /// Explorer Record draws a model's beams with it too, so the 3D view matches this one.
        /// </summary>
        internal static Mesh ExtrudedBeam(Alpaca4d.Generic.IBeam beam)
        {
            var curves = beam.Section?.Curves;
            if (curves == null || curves.Count == 0 || beam.GeomTransf == null) return null;

            var localY = beam.GeomTransf.LocalY;
            var localZ = beam.GeomTransf.LocalZ;
            var toStart = Transform.PlaneToPlane(Plane.WorldXY, new Plane(beam.Curve.PointAtStart, localZ, localY));
            var toEnd   = Transform.PlaneToPlane(Plane.WorldXY, new Plane(beam.Curve.PointAtEnd,   localZ, localY));

            var mesh = new Mesh();
            foreach (var curve in curves)
            {
                var outline = curve?.ToPolyline(0, 0, 0, 0)?.ToPolyline();
                if (outline == null || outline.Count < 2) continue;

                var start = new Polyline(outline);
                var end   = new Polyline(outline);
                start.Transform(toStart);
                end.Transform(toEnd);
                mesh.Append(Utils.CreateLoft(new List<Polyline> { start, end }));
            }

            return mesh.Vertices.Count > 0 ? mesh : null;
        }

        /// <summary>How long an element's axes are drawn at Axes size 1: in proportion to the element.</summary>
        private static double AxesReach(Alpaca4d.Generic.IBeam beam) => beam.Curve.GetLength() * 0.2;

        private static double AxesReach(Alpaca4d.Generic.IShell shell) => Math.Sqrt(AreaMassProperties.Compute(shell.Mesh).Area) * 0.35;

        // A quarter of the element's own reach, so the arrows stay inside a small brick and are
        // still visible on a large one.
        private static double AxesReach(Alpaca4d.Generic.IBrick brick) => brick.Mesh.GetBoundingBox(false).Diagonal.Length * 0.25;

        /// <summary>
        /// Everything this component draws, with the scales and toggles in force, so Rhino can set
        /// its clipping planes around it.
        ///
        /// This used to be a box two billion units across, so that nothing could fall outside it.
        /// But Rhino places its near clipping plane by the size of the scene, and a scene that big
        /// put it far out in front of the camera: zoomed in, the model was cut away - and with it
        /// everything else in the viewport, View Results included, since the planes are shared.
        /// </summary>
        private BoundingBox Extent()
        {
            var box = BoundingBox.Empty;
            if (_model == null) return box;

            foreach (var node in _model.Nodes) box.Union(new BoundingBox(node.Pos, node.Pos));
            foreach (var beam in _model.Beams) box.Union(beam.Curve.GetBoundingBox(false));
            foreach (var shell in _model.Shells) box.Union(shell.Mesh.GetBoundingBox(false));
            foreach (var brick in _model.Bricks) box.Union(brick.Mesh.GetBoundingBox(false));
            foreach (var mesh in _beamMeshes) box.Union(mesh.GetBoundingBox(false));
            foreach (var mesh in _shellMeshes) box.Union(mesh.GetBoundingBox(false));
            if (!box.IsValid) return box;

            var geometry = box;

            // A load is drawn as the geometry it acts on, moved back along it: arrows ending on a
            // node or a beam, a sheet standing off a shell.
            if ((_ckShowLoads?.Active ?? false) || (_ckShowLoadValues?.Active ?? false))
            {
                double scale = _slLoadScale?.Value ?? 1.0;
                foreach (var load in _model.LoadPatterns.SelectMany(x => x.Load))
                {
                    Vector3d shift;
                    if (load is Alpaca4d.Loads.PointLoad point) shift = -point.Force * scale;
                    else if (load is Alpaca4d.Loads.LineLoad line) shift = -line.GlobalForce * scale;
                    else if (load is Alpaca4d.Loads.MeshLoad sheet) shift = sheet.GlobalForce * scale;
                    else continue;

                    var moved = geometry;
                    moved.Transform(Transform.Translation(shift));
                    box.Union(moved);
                }
            }

            if (_ckShowSupports?.Active ?? false)
            {
                double scale = _slSupportScale?.Value ?? 1.0;
                foreach (var support in _model.Supports)
                {
                    box.Union(new BoundingBox(support.Pos, support.Pos));
                    if (support.Geometry is Mesh mesh)
                    {
                        var symbol = mesh.GetBoundingBox(false);
                        symbol.Transform(Transform.Scale(Point3d.Origin, scale));
                        symbol.Transform(Transform.Translation(new Vector3d(support.Pos)));
                        box.Union(symbol);
                    }
                }
            }

            // The axes stick out of their elements, and the labels out of their points.
            double reach = 0.0;
            if (_ckLocalAxes?.Active ?? false)
            {
                double size = _slAxesSize?.Value ?? 1.0;
                foreach (var beam in _model.Beams) reach = Math.Max(reach, AxesReach(beam) * size);
                foreach (var shell in _model.Shells) reach = Math.Max(reach, AxesReach(shell) * size);
                foreach (var brick in _model.Bricks) reach = Math.Max(reach, AxesReach(brick) * size);
            }
            if ((_ckNodeIds?.Active ?? false) || (_ckElementIds?.Active ?? false) || (_ckSectionNames?.Active ?? false)
                || (_ckShowLoadValues?.Active ?? false))
                reach = Math.Max(reach, 2.0 * (_slTextSize?.Value ?? 0.5));

            box.Inflate(reach);
            return box;
        }

        public override BoundingBox ClippingBox => _box.IsValid ? _box : base.ClippingBox;

        /// <summary>How wide a load arrow's shaft is drawn, in pixels. DrawArrow draws it at one, which was hard to pick out.</summary>
        private const int LoadArrowThickness = 3;

        /// <summary>A load arrow: a shaft <see cref="LoadArrowThickness"/> wide, and the head DrawArrow gives it.</summary>
        private static void DrawLoadArrow(IGH_PreviewArgs args, Line line, System.Drawing.Color colour)
        {
            args.Display.DrawLine(line, colour, LoadArrowThickness);
            args.Display.DrawArrowHead(line.To, line.Direction, colour, 24, 0);
        }

        /// <summary>A point load's force, and its moment when it has one.</summary>
        private static string PointLoadValue(Alpaca4d.Loads.PointLoad load)
        {
            var parts = new List<string>();
            if (load.Force.Length > 0.0) parts.Add(LoadValue(load.Force.Length, $"{Units.Force}"));
            if (load.Moment.Length > 0.0) parts.Add(LoadValue(load.Moment.Length, $"{Units.Force}·{Units.Length}"));
            return string.Join(", ", parts);
        }

        /// <summary>
        /// The size of a load, to four significant figures and without an exponent where a load
        /// could plausibly need one: 12.5 kN, 0.375 kN/m, 1250 kN.
        /// </summary>
        private static string LoadValue(double value, string unit)
        {
            string number = Math.Abs(value) >= 1000.0
                ? value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                : value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
            return $"{number} {unit}";
        }

        private static void VisualisePointLoad(IGH_PreviewArgs args, Point3d position, Vector3d magnitude)
        {
            if (magnitude.Length <= 0) return;
            var line = new Line(position, magnitude);
            var offset = new Vector3d(position.X - line.To.X, position.Y - line.To.Y, position.Z - line.To.Z);
            line.Transform(Transform.Translation(offset));
            DrawLoadArrow(args, line, AlpacaSettings.Colour(DisplayItem.PointLoads));
        }

        private static void VisualiseLineLoad(IGH_PreviewArgs args, Curve lineGeometry, Vector3d forceVector, double scale)
        {
            var color = AlpacaSettings.Colour(DisplayItem.LineLoads);
            const int divisions = 6;
            for (double t = 0.0; t <= 1.0; t += 1.0 / divisions)
            {
                var point     = lineGeometry.PointAtNormalizedLength(t);
                var magnitude = forceVector * scale;
                var line      = new Line(point, magnitude);
                var offset    = new Vector3d(point.X - line.To.X, point.Y - line.To.Y, point.Z - line.To.Z);
                line.Transform(Transform.Translation(offset));
                DrawLoadArrow(args, line, color);
            }
        }

        private static void VisualiseMeshLoad(IGH_PreviewArgs args, Mesh meshGeometry, double forceValue, Vector3d unitVector, double scale)
        {
            var meshPos  = meshGeometry.Offset(forceValue * scale, true, unitVector);
            meshPos.Faces.DeleteFaces(new List<int>(1));
            var color    = AlpacaSettings.Colour(DisplayItem.AreaLoads);
            var material = new Rhino.Display.DisplayMaterial(color, color, color, color, 0.0, 0.8);
            args.Display.DrawMeshShaded(meshPos, material);
            args.Display.DrawMeshWires(meshPos, color, 2);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.model_View__Alpaca4d_;
        public override Guid ComponentGuid => new Guid("{5EDB6364-D458-43D6-81E9-324DECA1FEA6}");
    }
}
