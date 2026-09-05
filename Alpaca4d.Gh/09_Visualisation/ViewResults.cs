using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Result;
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
    /// expression - and what is filtered out is not hidden but ghosted, so the part being looked at
    /// stays in the model it belongs to rather than floating on its own. And the deformed shape is
    /// a toggle over any of it rather than a component of its own, so a stress can be read on the
    /// shape it belongs to.
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
        private string _info = "";

        // What gets drawn, worked out in SolveInstance and held for the viewport.
        private readonly List<Mesh> _shaded = new List<Mesh>();
        private readonly List<Mesh> _ghostMeshes = new List<Mesh>();
        private readonly List<Curve> _ghostCurves = new List<Curve>();
        private readonly List<Tuple<Curve, Color>> _curves = new List<Tuple<Curve, Color>>();
        private readonly List<Mesh> _diagrams = new List<Mesh>();
        private readonly List<Tuple<Line, Color>> _arrows = new List<Tuple<Line, Color>>();

        private GH_ExtendableMenu _resultMenu;
        private GH_ExtendableMenu _filterMenu;
        private GH_ExtendableMenu _colourMenu;

        private MenuDropDown _ddFamily;
        private MenuDropDown _ddComponent;
        private MenuDropDown _ddLayer;
        private MenuCheckBox _ckDeformed;
        private MenuSlider _slDeformScale;
        private MenuSlider _slDiagramScale;
        private MenuCheckBox _ckGhost;
        private MenuCheckBox _ckWires;

        /// <summary>The colour everything outside the filter is drawn in.</summary>
        private static readonly Color GhostColour = Color.FromArgb(150, 150, 150);

        public ViewResults()
          : base("View Results_WIP (Alpaca4d)", "View Results_WIP",
            "Draw any result on the model: displacements, beam force diagrams, shell forces, shell " +
            "stresses, solid stresses, reactions.\n" +
            "Pick the result and the component from the dropdowns. ElementId shows only some of the " +
            "elements and greys the rest, so what you are looking at stays in the model around it.\n" +
            "Work in progress, and meant to grow into the replacement for the separate view components.",
            "Alpaca4d", "09_Visualisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        public override void CreateAttributes()
        {
            base.CreateAttributes();

            // The parameters become plugs inside the menus they belong to, so the component reads
            // as three questions rather than one row of six inputs. Registered here rather than in
            // Setup because that is the first point at which Params is populated, both for a new
            // component and for one restored from a file.
            if (_resultMenu != null && Params.Input.Count > 1)
                _resultMenu.RegisterInputPlug(new ExtendedPlug(Params.Input[1]));
            if (_filterMenu != null && Params.Input.Count > 2)
                _filterMenu.RegisterInputPlug(new ExtendedPlug(Params.Input[2]));
            if (_colourMenu != null && Params.Input.Count > 4)
            {
                _colourMenu.RegisterInputPlug(new ExtendedPlug(Params.Input[3]));
                _colourMenu.RegisterInputPlug(new ExtendedPlug(Params.Input[4]));
            }
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to draw, or which mode after a natural vibration analysis.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.Filter, GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colors", "Colors", "Gradient to colour with, from the low end to the high. Connect the Colors component for a ready-made one.", GH_ParamAccess.list);
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

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            var resultMenu = new GH_ExtendableMenu(0, "Result") { Name = "Result", Header = "What to draw" };
            var resultPanel = new MenuPanel(0, "result_panel");

            _ddFamily = new MenuDropDown(0, "Family", "Family") { VisibleItemCount = 6 };
            foreach (var name in ResultField.FamilyNames)
                _ddFamily.AddItem(name, name);
            _ddFamily.ValueChanged += OnFamilyChanged;

            _ddComponent = new MenuDropDown(1, "Component", "Component") { VisibleItemCount = 8 };
            _ddComponent.ValueChanged += OnWidgetChanged;

            _ddLayer = new MenuDropDown(2, "Layer", "Layer") { VisibleItemCount = 3 };
            foreach (var name in Alpaca4d.Result.Read.LayerNames)
                _ddLayer.AddItem(name, name);
            _ddLayer.ValueChanged += OnWidgetChanged;

            resultPanel.AddControl(_ddFamily);
            resultPanel.AddControl(_ddComponent);
            resultPanel.AddControl(_ddLayer);
            resultMenu.AddControl(resultPanel);
            resultMenu.Expand();
            _resultMenu = resultMenu;
            attr.AddMenu(resultMenu);

            var shapeMenu = new GH_ExtendableMenu(1, "Shape") { Name = "Shape", Header = "How to draw it" };
            var shapePanel = new MenuPanel(1, "shape_panel");

            _ckDeformed = new MenuCheckBox(0, "Deformed", "Deformed");
            _ckDeformed.ValueChanged += OnWidgetChanged;

            _slDeformScale = new MenuSlider(0, "DeformScale", 0.0, 500.0, 1.0, 1) { Name = "Deformation" };
            _slDeformScale.ValueChanged += OnWidgetChanged;

            _slDiagramScale = new MenuSlider(1, "DiagramScale", 0.0, 20.0, 1.0, 2) { Name = "Diagram" };
            _slDiagramScale.ValueChanged += OnWidgetChanged;

            _ckWires = new MenuCheckBox(1, "Wires", "Element edges") { Active = true };
            _ckWires.ValueChanged += OnWidgetChanged;

            shapePanel.AddControl(_ckDeformed);
            shapePanel.AddControl(_slDeformScale);
            shapePanel.AddControl(_slDiagramScale);
            shapePanel.AddControl(_ckWires);
            shapeMenu.AddControl(shapePanel);
            attr.AddMenu(shapeMenu);

            var filterMenu = new GH_ExtendableMenu(2, "Filter") { Name = "Filter", Header = "Which elements" };
            var filterPanel = new MenuPanel(2, "filter_panel");

            _ckGhost = new MenuCheckBox(0, "Ghost", "Ghost the rest") { Active = true };
            _ckGhost.ValueChanged += OnWidgetChanged;

            filterPanel.AddControl(_ckGhost);
            filterMenu.AddControl(filterPanel);
            _filterMenu = filterMenu;
            attr.AddMenu(filterMenu);

            var colourMenu = new GH_ExtendableMenu(3, "Colour") { Name = "Colour", Header = "Gradient and range" };
            _colourMenu = colourMenu;
            attr.AddMenu(colourMenu);

            attr.MinWidth = 210f;

            SyncComponentList();
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            if (_ddFamily == null) return;

            _ddFamily.ValueChanged += OnFamilyChanged;
            _ddComponent.ValueChanged += OnWidgetChanged;
            _ddLayer.ValueChanged += OnWidgetChanged;
            _ckDeformed.ValueChanged += OnWidgetChanged;
            _slDeformScale.ValueChanged += OnWidgetChanged;
            _slDiagramScale.ValueChanged += OnWidgetChanged;
            _ckGhost.ValueChanged += OnWidgetChanged;
            _ckWires.ValueChanged += OnWidgetChanged;

            // The dropdown remembers which entry was chosen but not what the entries were, and the
            // Component list depends on the family - so it is rebuilt before the saved index is
            // applied to it.
            SyncComponentList();
        }

        private void OnWidgetChanged(object sender, EventArgs e) => ExpireSolution(true);

        private void OnFamilyChanged(object sender, EventArgs e)
        {
            SyncComponentList();
            ExpireSolution(true);
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
                var families = (ResultFamily[])Enum.GetValues(typeof(ResultFamily));
                return value >= 0 && value < families.Length ? families[value] : ResultFamily.Displacement;
            }
        }

        #endregion

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
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _model = null;
            _field = null;
            Clear();

            if (!DA.GetData(0, ref _model) || _model == null) return;

            int step = 0;
            DA.GetData(1, ref step);

            var filter = ElementFilterInput.Read(DA, 2, this);

            var palette = new List<Color>();
            if (!DA.GetDataList(3, palette) || palette.Count < 2)
                palette = Alpaca4d.Colors.Gradient(0);

            var family = Family;

            try
            {
                _field = ResultField.Read(_model, family, _ddComponent?.Value ?? 0, _ddLayer?.Value ?? 0, step);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            // The range is fitted to what is on screen, not to the whole model: a filter narrowed
            // to one beam is asking about that beam, and a gradient stretched over a maximum
            // somewhere else would paint it all one colour.
            var shown = ShownValues(filter, family).ToList();

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

            var displacement = (_ckDeformed?.Active ?? false)
                ? Displaced(step, _slDeformScale?.Value ?? 1.0)
                : null;

            Build(filter, family, displacement);

            _info = $"{ResultField.FamilyNames[(int)family]} - {_field.Label}, " +
                    $"step {step}, min {_min:G4}, max {_max:G4}";

            // The dropdowns cannot be hidden one at a time, so a Layer left on something other
            // than the top of the list is worth a word - otherwise it reads as being in force.
            if (!ResultField.HasLayers(family) && (_ddLayer?.Value ?? 0) != 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Layer is set to {Alpaca4d.Result.Read.LayerNames[_ddLayer.Value]}, which only " +
                    "applies to Shell stresses. It is ignored here.");

            DA.SetData(0, _info);
            DA.SetDataList(1, shown);
            DA.SetDataList(2, palette);

            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        /// <summary>The values on the elements that passed the filter, which is what the range fits.</summary>
        private IEnumerable<double> ShownValues(ElementFilter filter, ResultFamily family)
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

        private Dictionary<int, Vector3d> Displaced(int step, double scale)
        {
            var displaced = new Dictionary<int, Vector3d>();

            foreach (var entry in _model.NodalDisplacements(step))
            {
                if (entry.Key.HasValue)
                    displaced[entry.Key.Value] = entry.Value * scale;
            }

            return displaced;
        }

        private Point3d Move(Point3d point, int? node, Dictionary<int, Vector3d> displacement)
        {
            Vector3d offset;
            if (displacement != null && node.HasValue && displacement.TryGetValue(node.Value, out offset))
                return point + offset;

            return point;
        }

        #region Building what gets drawn

        /// <summary>
        /// Sorts every element into one of three piles: drawn in colour because it passed the
        /// filter and the result has a value for it, drawn plain because it passed but this result
        /// says nothing about it, or ghosted.
        /// </summary>
        private void Build(ElementFilter filter, ResultFamily family, Dictionary<int, Vector3d> displacement)
        {
            bool ghost = _ckGhost?.Active ?? true;

            foreach (var beam in _model.Beams)
                BuildBeam(beam, filter.Matches(beam), family, displacement, ghost);

            foreach (var shell in _model.Shells)
                BuildFace(shell.Mesh, shell.Id, shell.IndexNodes, filter.Matches(shell), family, displacement, ghost,
                          family == ResultFamily.ShellForce || family == ResultFamily.ShellStress);

            foreach (var brick in _model.Bricks)
                BuildFace(brick.Mesh, brick.Id, brick.IndexNodes, filter.Matches(brick), family, displacement, ghost,
                          family == ResultFamily.BrickStress);

            if (family == ResultFamily.Reaction)
                BuildReactions();
        }

        private void BuildBeam(IBeam beam, bool kept, ResultFamily family,
                               Dictionary<int, Vector3d> displacement, bool ghost)
        {
            var start = Move(beam.Curve.PointAtStart, beam.INode, displacement);
            var end = Move(beam.Curve.PointAtEnd, beam.JNode, displacement);
            var line = new LineCurve(start, end);

            if (!kept)
            {
                if (ghost) _ghostCurves.Add(line);
                return;
            }

            if (family == ResultFamily.Displacement && _field.ByNode != null)
            {
                // Two nodes and a colour at each, so the beam is drawn as a handful of segments
                // with the colour walked between them. Anything less and a beam reads as one flat
                // colour while the shells beside it are graded.
                double from = ValueAtNode(beam.INode);
                double to = ValueAtNode(beam.JNode);
                const int steps = 8;

                for (int i = 0; i < steps; i++)
                {
                    double a = i / (double)steps;
                    double b = (i + 1) / (double)steps;
                    var segment = new LineCurve(start + (end - start) * a, start + (end - start) * b);
                    _curves.Add(Tuple.Create((Curve)segment, Colour(from + (to - from) * (a + b) * 0.5)));
                }

                return;
            }

            if (family == ResultFamily.BeamForce)
            {
                _curves.Add(Tuple.Create((Curve)line, Color.DimGray));

                List<double> forces;
                if (beam.Id.HasValue && _field.ByElement != null && _field.ByElement.TryGetValue(beam.Id.Value, out forces))
                {
                    var diagram = Diagram(beam, start, end, forces, _slDiagramScale?.Value ?? 1.0);
                    if (diagram != null) _diagrams.Add(diagram);
                }

                return;
            }

            // A beam under a shell or solid result has no value of its own. Drawn plain rather
            // than coloured, because colouring it would mean picking a number it does not have.
            _curves.Add(Tuple.Create((Curve)line, Color.DimGray));
        }

        private void BuildFace(Mesh source, int? tag, List<int?> nodes, bool kept, ResultFamily family,
                               Dictionary<int, Vector3d> displacement, bool ghost, bool coloured)
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
                if (ghost) _ghostMeshes.Add(mesh);
                return;
            }

            mesh.VertexColors.Clear();

            if (family == ResultFamily.Displacement && _field.ByNode != null && nodes != null)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                    mesh.VertexColors.Add(Colour(ValueAtNode(i < nodes.Count ? nodes[i] : null)));
            }
            else if (coloured && tag.HasValue && _field.ByElement != null && _field.ByElement.ContainsKey(tag.Value))
            {
                // One value per integration point, and a face with more corners than it has
                // stations reuses the last - which is what a triangle with three stations and
                // four vertices needs.
                var values = _field.ByElement[tag.Value];
                for (int i = 0; i < mesh.Vertices.Count; i++)
                    mesh.VertexColors.Add(Colour(values[Math.Min(i, values.Count - 1)]));
            }
            else
            {
                // Passed the filter, but this result has nothing to say about it.
                mesh.VertexColors.CreateMonotoneMesh(Color.FromArgb(210, 210, 210));
            }

            _shaded.Add(mesh);
        }

        private void BuildReactions()
        {
            if (_field.Reactions == null || _field.Reactions.Count == 0) return;

            // Scaled so the largest reaction is a fixed fraction of the model, which keeps the
            // arrows readable whether the model is a bracket or a bridge.
            double largest = _field.Reactions.Select(x => Math.Abs(x.Item3)).DefaultIfEmpty(0.0).Max();
            if (largest <= 0.0) return;

            var box = _model.UniquePoints != null && _model.UniquePoints.Count > 0
                ? new BoundingBox(_model.UniquePoints)
                : BoundingBox.Unset;
            double reach = box.IsValid ? box.Diagonal.Length * 0.08 : 1.0;
            double scale = reach / largest * (_slDiagramScale?.Value ?? 1.0);

            foreach (var reaction in _field.Reactions)
            {
                var plane = reaction.Item1;
                var local = reaction.Item2;

                // Back into world axes to draw it, having been resolved onto the support's own to
                // read it. Drawn from the support outwards, so the arrow points the way the
                // support pushes on the structure.
                var world = plane.XAxis * local.X + plane.YAxis * local.Y + plane.ZAxis * local.Z;
                if (world.Length <= 0.0) continue;

                _arrows.Add(Tuple.Create(new Line(plane.Origin, plane.Origin + world * scale),
                                         Colour(reaction.Item3)));
            }
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
        /// A force diagram: the values stood off the beam along its local z, closed back onto it at
        /// both ends so the area reads as the diagram it is.
        /// </summary>
        private Mesh Diagram(IBeam beam, Point3d start, Point3d end, List<double> forces, double scale)
        {
            if (forces == null || forces.Count < 2 || scale <= 0.0) return null;

            double largest = forces.Select(Math.Abs).Max();
            if (largest <= 0.0) return null;

            // Sized against the beam rather than against the model, so a short member and a long
            // one in the same model both read.
            double reach = start.DistanceTo(end) * 0.25 * scale / largest;
            var offset = beam.GeomTransf != null ? beam.GeomTransf.LocalZ : Vector3d.ZAxis;
            offset.Unitize();

            var mesh = new Mesh();
            var axis = end - start;

            for (int i = 0; i < forces.Count; i++)
            {
                var at = start + axis * (i / (double)(forces.Count - 1));
                mesh.Vertices.Add(at);
                mesh.Vertices.Add(at + offset * forces[i] * reach);
            }

            for (int i = 0; i < forces.Count - 1; i++)
            {
                int a = 2 * i;
                mesh.Faces.AddFace(a, a + 2, a + 3, a + 1);
            }

            // Coloured from the same gradient as everything else rather than from a fixed pair
            // for positive and negative, so that one Legend reads for whatever is on screen and a
            // diagram can be compared against a shell beside it.
            var colours = forces.SelectMany(value => new[] { Colour(value), Colour(value) }).ToArray();

            mesh.VertexColors.SetColors(colours);
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
            }

            foreach (var mesh in _diagrams)
                args.Display.DrawMeshWires(mesh, Color.Black, 1);

            foreach (var curve in _curves)
                args.Display.DrawCurve(curve.Item1, curve.Item2, 3);

            foreach (var arrow in _arrows)
                args.Display.DrawArrow(arrow.Item1, arrow.Item2);
        }

        public override bool IsPreviewCapable => true;

        public override BoundingBox ClippingBox
        {
            get
            {
                var box = BoundingBox.Empty;

                foreach (var mesh in _shaded) box.Union(mesh.GetBoundingBox(false));
                foreach (var mesh in _ghostMeshes) box.Union(mesh.GetBoundingBox(false));
                foreach (var mesh in _diagrams) box.Union(mesh.GetBoundingBox(false));
                foreach (var curve in _ghostCurves) box.Union(curve.GetBoundingBox(false));
                foreach (var curve in _curves) box.Union(curve.Item1.GetBoundingBox(false));
                foreach (var arrow in _arrows) box.Union(arrow.Item1.BoundingBox);

                return box.IsValid ? box : base.ClippingBox;
            }
        }

        #endregion

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Deformed_Model__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{4D8A0F26-3B71-4E59-9C84-A2F70D51B6E3}");
    }
}
