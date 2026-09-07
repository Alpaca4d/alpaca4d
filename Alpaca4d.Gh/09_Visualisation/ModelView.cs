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

        // Widget controls – Loads menu
        private MenuCheckBox _ckShowLoads;
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
            pManager.Register_StringParam("Info", "Info", "The Tcl the model has written so far, one line per entry - the script Run Analysis hands to OpenSees.");
        }

        #region UI Setup

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            // ── Elements ─────────────────────────────────────────────────────
            var elemMenu = new GH_ExtendableMenu(0, "Elements");
            elemMenu.Name = "Elements";
            elemMenu.Header = "Element display options";

            var elemPanel = new MenuPanel(0, "elements_panel");
            _ckExtruded     = new MenuCheckBox(0, "Extruded",     "Extruded");
            _ckNodeIds      = new MenuCheckBox(1, "NodeIds",      "Node IDs");
            _ckElementIds   = new MenuCheckBox(2, "ElementIds",   "Element IDs");
            _ckSectionNames = new MenuCheckBox(3, "SectionNames", "Section Names");
            _ckLocalAxes    = new MenuCheckBox(4, "LocalAxes",    "Local Axes");

            _ckExtruded.ValueChanged     += OnWidgetChanged;
            _ckNodeIds.ValueChanged      += OnWidgetChanged;
            _ckElementIds.ValueChanged   += OnWidgetChanged;
            _ckSectionNames.ValueChanged += OnWidgetChanged;
            _ckLocalAxes.ValueChanged    += OnWidgetChanged;

            elemPanel.AddControl(_ckExtruded);
            elemPanel.AddControl(_ckNodeIds);
            elemPanel.AddControl(_ckElementIds);
            elemPanel.AddControl(_ckSectionNames);
            elemPanel.AddControl(_ckLocalAxes);
            elemMenu.AddControl(elemPanel);
            _elemMenu = elemMenu;         // store for plug registration in CreateAttributes
            attr.AddMenu(elemMenu);

            // ── Loads ─────────────────────────────────────────────────────────
            var loadsMenu = new GH_ExtendableMenu(1, "Loads");
            loadsMenu.Name = "Loads";
            loadsMenu.Header = "Load display options";

            var loadsPanel = new MenuPanel(1, "loads_panel");
            _ckShowLoads = new MenuCheckBox(0, "ShowLoads", "Show Loads");
            _slLoadScale = new MenuSlider(0, "LoadScale", 0.1, 10.0, 1.0, 1);
            _slLoadScale.Name = "Load Scale";

            _ckShowLoads.ValueChanged += OnWidgetChanged;
            _slLoadScale.ValueChanged += OnWidgetChanged;

            loadsPanel.AddControl(_ckShowLoads);
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
            _slLoadScale.ValueChanged    += OnWidgetChanged;
            _ckShowSupports.ValueChanged    += OnWidgetChanged;
            _slSupportScale.ValueChanged    += OnWidgetChanged;
            _ckShowConstraints.ValueChanged += OnWidgetChanged;
        }

        private void OnWidgetChanged(object sender, EventArgs e) => ExpireSolution(true);

        #endregion

        protected override void BeforeSolveInstance()
        {
            _beamMeshes.Clear();
            _shellMeshes.Clear();
            _brickMeshes.Clear();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData(0, ref _model);
            if (_model == null) return;

            _loadPatternId = null;
            int lpId = -1;
            if (DA.GetData(1, ref lpId))
                _loadPatternId = lpId;

            _visibleElementIds = null;
            var elemIdList = new List<int>();
            if (DA.GetDataList(2, elemIdList) && elemIdList.Count > 0)
                _visibleElementIds = new HashSet<int>(elemIdList);

            foreach (var item in _model.Beams)
            {
                if (_visibleElementIds != null && item.Id.HasValue && !_visibleElementIds.Contains(item.Id.Value)) continue;

                var curves = item.Section?.Curves;
                if (curves == null || curves.Count == 0) continue;

                var curve   = item.Curve;
                var section = curves[0];

                var localY = item.GeomTransf.LocalY;
                var localZ = item.GeomTransf.LocalZ;
                var planeStart = new Plane(curve.PointAtStart, localZ, localY);
                var planeEnd   = new Plane(curve.PointAtEnd,   localZ, localY);

                var transfEnd   = Transform.PlaneToPlane(Plane.WorldXY, planeStart);
                var transfStart = Transform.PlaneToPlane(Plane.WorldXY, planeEnd);

                var sectionStart = section.DuplicateCurve();
                var sectionEnd   = section.DuplicateCurve();
                sectionStart.Transform(transfStart);
                sectionEnd.Transform(transfEnd);

                var polyStart = sectionStart.ToPolyline(0, 0, 0, 0).ToPolyline();
                var polyEnd   = sectionEnd.ToPolyline(0, 0, 0, 0).ToPolyline();

                var beamMesh = Utils.CreateLoft(new List<Polyline> { polyStart, polyEnd });
                beamMesh.VertexColors.CreateMonotoneMesh(item.Color);
                _beamMeshes.Add(beamMesh);
            }
            foreach (var item in _model.Shells)
            {
                if (_visibleElementIds != null && item.Id.HasValue && !_visibleElementIds.Contains(item.Id.Value)) continue;

                var myMesh    = new Mesh();
                var meshTop   = item.Mesh.Offset( item.Section.Thickness / 2, true);
                var meshBottom= item.Mesh.Offset(-item.Section.Thickness / 2, true);
                myMesh.Append(meshTop);
                myMesh.Append(meshBottom);
                myMesh.VertexColors.CreateMonotoneMesh(item.Color);
                _shellMeshes.Add(myMesh);
            }
            foreach (var item in _model.Bricks)
            {
                if (_visibleElementIds != null && item.Id.HasValue && !_visibleElementIds.Contains(item.Id.Value)) continue;

                var brick = item.Mesh;
                brick.VertexColors.CreateMonotoneMesh(item.Color);
                _brickMeshes.Add(brick);
            }

            DA.SetData(0, _model);
            DA.SetDataList(1, _model.Tcl);

            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (this.Hidden || this.Locked || _model == null) return;

            bool extruded       = _ckExtruded?.Active     ?? false;
            bool showLoads      = _ckShowLoads?.Active    ?? false;
            bool showSupports   = _ckShowSupports?.Active ?? false;
            bool showConstraints= _ckShowConstraints?.Active ?? false;
            bool showNodeIds    = _ckNodeIds?.Active      ?? false;
            bool showElemIds    = _ckElementIds?.Active   ?? false;
            bool showSecNames   = _ckSectionNames?.Active ?? false;
            bool showLocalAxes  = _ckLocalAxes?.Active    ?? false;
            double loadScale    = _slLoadScale?.Value     ?? 1.0;
            double supportScale = _slSupportScale?.Value  ?? 1.0;

            // ── Elements ─────────────────────────────────────────────────────
            if (extruded)
            {
                foreach (var mesh in _beamMeshes)
                {
                    args.Display.DrawMeshFalseColors(mesh);
                    args.Display.DrawMeshWires(mesh, System.Drawing.Color.Black, 2);
                }
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
                    args.Display.DrawCurve(beam.Curve, beam.Color);
                }
                foreach (var shell in _model.Shells)
                {
                    if (_visibleElementIds != null && shell.Id.HasValue && !_visibleElementIds.Contains(shell.Id.Value)) continue;
                    var mesh = shell.Mesh;
                    mesh.VertexColors.CreateMonotoneMesh(shell.Color);
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
                    args.Display.Draw2dText($"N{node.Id}", System.Drawing.Color.White, node.Pos, true, 12);
            }

            // ── Element IDs ──────────────────────────────────────────────────
            if (showElemIds)
            {
                foreach (var beam in _model.Beams)
                {
                    if (_visibleElementIds != null && beam.Id.HasValue && !_visibleElementIds.Contains(beam.Id.Value)) continue;
                    var mid = beam.Curve.PointAtNormalizedLength(0.5);
                    args.Display.Draw2dText($"E{beam.Id}", System.Drawing.Color.Yellow, mid, true, 12);
                }
                foreach (var shell in _model.Shells)
                {
                    if (_visibleElementIds != null && shell.Id.HasValue && !_visibleElementIds.Contains(shell.Id.Value)) continue;
                    var centroid = AreaMassProperties.Compute(shell.Mesh).Centroid;
                    args.Display.Draw2dText($"E{shell.Id}", System.Drawing.Color.Yellow, centroid, true, 12);
                }
                foreach (var brick in _model.Bricks)
                {
                    if (_visibleElementIds != null && brick.Id.HasValue && !_visibleElementIds.Contains(brick.Id.Value)) continue;
                    var centroid = VolumeMassProperties.Compute(brick.Mesh).Centroid;
                    args.Display.Draw2dText($"E{brick.Id}", System.Drawing.Color.Yellow, centroid, true, 12);
                }
            }

            // ── Section Names ────────────────────────────────────────────────
            if (showSecNames)
            {
                foreach (var beam in _model.Beams)
                {
                    if (_visibleElementIds != null && beam.Id.HasValue && !_visibleElementIds.Contains(beam.Id.Value)) continue;
                    var mid  = beam.Curve.PointAtNormalizedLength(0.5);
                    var name = (beam.Section as Alpaca4d.Section.ISection)?.SectionName
                            ?? (beam.Section as Alpaca4d.Section.ElasticSection)?.SectionName
                            ?? beam.Section.GetType().Name;
                    args.Display.Draw2dText(name, System.Drawing.Color.Cyan, mid, true, 11);
                }
            }

            // ── Local Axes ───────────────────────────────────────────────────
            if (showLocalAxes)
            {
                foreach (var beam in _model.Beams)
                {
                    if (_visibleElementIds != null && beam.Id.HasValue && !_visibleElementIds.Contains(beam.Id.Value)) continue;

                    var mid    = beam.Curve.PointAtNormalizedLength(0.5);
                    var axLen  = beam.Curve.GetLength() * 0.2;
                    var localX = beam.Curve.PointAtEnd - beam.Curve.PointAtStart;
                    localX.Unitize();
                    var localY = beam.GeomTransf.LocalY;
                    var localZ = beam.GeomTransf.LocalZ;

                    args.Display.DrawArrow(new Line(mid, mid + localX * axLen), System.Drawing.Color.Red,   12, 0);
                    args.Display.DrawArrow(new Line(mid, mid + localY * axLen), System.Drawing.Color.Green, 12, 0);
                    args.Display.DrawArrow(new Line(mid, mid + localZ * axLen), System.Drawing.Color.Blue,  12, 0);

                    args.Display.Draw2dText("X", System.Drawing.Color.Red,   mid + localX * axLen * 1.1, true, 10);
                    args.Display.Draw2dText("Y", System.Drawing.Color.Green, mid + localY * axLen * 1.1, true, 10);
                    args.Display.Draw2dText("Z", System.Drawing.Color.Blue,  mid + localZ * axLen * 1.1, true, 10);
                }

                // Shells, labelled 1, 2 and 3 to match the components they belong to: fxx and
                // sigma11 act along the red arrow, fyy and sigma22 along the green one, and the
                // blue one is the normal the moments turn about.
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
                                    Math.Sqrt(AreaMassProperties.Compute(shell.Mesh).Area) * 0.35);
                }

                // Solids, labelled 1, 2 and 3 rather than X, Y and Z, because that is what the
                // stress components they belong to are called - sigma11 is the direct stress along
                // this red arrow. A beam's are x, y and z because that is what OpenSees calls a
                // beam's; the two conventions are different and are drawn differently.
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

                    // A quarter of the element's own reach, so the arrows stay inside a small brick
                    // and are still visible on a large one.
                    var axLen = brick.Mesh.GetBoundingBox(false).Diagonal.Length * 0.25;

                    DrawElementAxes(args, origin, frame.X, frame.Y, frame.Z, axLen);
                }
            }

            // ── Loads ─────────────────────────────────────────────────────────
            if (showLoads)
            {
                var relevantPatterns = _loadPatternId.HasValue
                    ? _model.LoadPatterns.Where(x => x.Id == _loadPatternId.Value)
                    : (IEnumerable<Alpaca4d.Loads.LoadPattern>)_model.LoadPatterns;

                var patternLoads = relevantPatterns.SelectMany(x => x.Load);

                foreach (var pointLoad in patternLoads.OfType<Alpaca4d.Loads.PointLoad>())
                    VisualisePointLoad(args, pointLoad.Pos, pointLoad.Force * loadScale);

                foreach (var meshLoad in patternLoads.OfType<Alpaca4d.Loads.MeshLoad>())
                {
                    var forceValue = meshLoad.GlobalForce.Length;
                    var unitVector = meshLoad.GlobalForce / forceValue;
                    if (meshLoad.Element == null)
                    {
                        foreach (var shell in _model.Shells)
                            VisualiseMeshLoad(args, shell.Mesh, forceValue, unitVector, loadScale);
                    }
                    else
                    {
                        VisualiseMeshLoad(args, meshLoad.Element.Mesh, forceValue, unitVector, loadScale);
                    }
                }

                foreach (var lineLoad in patternLoads.OfType<Alpaca4d.Loads.LineLoad>())
                {
                    if (lineLoad.Element != null)
                    {
                        VisualiseLineLoad(args, lineLoad.Element.Curve, lineLoad.GlobalForce, loadScale);
                    }
                    else
                    {
                        foreach (var beam in _model.Beams)
                            VisualiseLineLoad(args, beam.Curve, lineLoad.GlobalForce, loadScale);
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

        /// <summary>
        /// One set of element axes, drawn as three arrows numbered 1, 2 and 3.
        ///
        /// Numbered rather than lettered because that is what the components they belong to are
        /// called - sigma11 and fxx act along the red one. A beam's axes are drawn X, Y and Z next
        /// to these, which is not an inconsistency: OpenSees names a beam's local axes that way and
        /// a shell's and a solid's stress components the other, and pretending otherwise would make
        /// one of the two labels wrong.
        /// </summary>
        private static void DrawElementAxes(IGH_PreviewArgs args, Point3d origin,
                                            Vector3d x, Vector3d y, Vector3d z, double length)
        {
            if (!(length > 0.0)) return;

            args.Display.DrawArrow(new Line(origin, origin + x * length), System.Drawing.Color.Red,   12, 0);
            args.Display.DrawArrow(new Line(origin, origin + y * length), System.Drawing.Color.Green, 12, 0);
            args.Display.DrawArrow(new Line(origin, origin + z * length), System.Drawing.Color.Blue,  12, 0);

            args.Display.Draw2dText("1", System.Drawing.Color.Red,   origin + x * length * 1.1, true, 10);
            args.Display.Draw2dText("2", System.Drawing.Color.Green, origin + y * length * 1.1, true, 10);
            args.Display.Draw2dText("3", System.Drawing.Color.Blue,  origin + z * length * 1.1, true, 10);
        }

        public override BoundingBox ClippingBox => new BoundingBox(
            new Point3d(-1e9, -1e9, -1e9),
            new Point3d( 1e9,  1e9,  1e9));

        private static void VisualisePointLoad(IGH_PreviewArgs args, Point3d position, Vector3d magnitude)
        {
            if (magnitude.Length <= 0) return;
            var line = new Line(position, magnitude);
            var offset = new Vector3d(position.X - line.To.X, position.Y - line.To.Y, position.Z - line.To.Z);
            line.Transform(Transform.Translation(offset));
            args.Display.DrawArrow(line, System.Drawing.Color.IndianRed, 24, 0);
        }

        private static void VisualiseLineLoad(IGH_PreviewArgs args, Curve lineGeometry, Vector3d forceVector, double scale)
        {
            var color = System.Drawing.Color.DarkSeaGreen;
            const int divisions = 6;
            for (double t = 0.0; t <= 1.0; t += 1.0 / divisions)
            {
                var point     = lineGeometry.PointAtNormalizedLength(t);
                var magnitude = forceVector * scale;
                var line      = new Line(point, magnitude);
                var offset    = new Vector3d(point.X - line.To.X, point.Y - line.To.Y, point.Z - line.To.Z);
                line.Transform(Transform.Translation(offset));
                args.Display.DrawArrow(line, color, 24, 0);
            }
        }

        private static void VisualiseMeshLoad(IGH_PreviewArgs args, Mesh meshGeometry, double forceValue, Vector3d unitVector, double scale)
        {
            var meshPos  = meshGeometry.Offset(forceValue * scale, true, unitVector);
            meshPos.Faces.DeleteFaces(new List<int>(1));
            var color    = System.Drawing.Color.OrangeRed;
            var material = new Rhino.Display.DisplayMaterial(color, color, color, color, 0.0, 0.8);
            args.Display.DrawMeshShaded(meshPos, material);
            args.Display.DrawMeshWires(meshPos, System.Drawing.Color.OrangeRed, 2);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.model_View__Alpaca4d_;
        public override Guid ComponentGuid => new Guid("{5EDB6364-D458-43D6-81E9-324DECA1FEA6}");
    }
}
