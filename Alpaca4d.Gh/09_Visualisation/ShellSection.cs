using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Result;
using Alpaca4d.UI;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// A cut drawn across shells, and the forces carried through it: a diagram along the cut, the
    /// forces per length at points of it, and their totals.
    ///
    /// The working is <see cref="Alpaca4d.Result.ShellSection"/>'s. This reads the shells and their
    /// forces out of the model once per question, cuts it with each curve it is given, and draws.
    /// </summary>
    public class ShellSectionForces : GH_ExtendableComponent
    {
        // One SectionCut per curve, gathered over the solution and drawn once it is complete, so
        // every curve's diagram is drawn to the same scale.
        private readonly List<SectionCut> _cuts = new List<SectionCut>();
        private Alpaca4d.Model _model;

        // The shells read for the last question asked of them: reading the recorder is the slow
        // part, and a solution cutting ten curves, or a slider moving one, asks the same one.
        private string _sectionKey;
        private Alpaca4d.Result.ShellSection _section;

        // What gets drawn.
        private Mesh _diagram;
        private readonly List<Polyline> _lines = new List<Polyline>();
        private readonly List<Tuple<Line, Color>> _axes = new List<Tuple<Line, Color>>();
        private readonly List<Tuple<Point3d, string>> _labels = new List<Tuple<Point3d, string>>();
        private BoundingBox _box = BoundingBox.Empty;

        private MenuDropDown _ddComponent;
        private MenuDropDown _ddForceUnit;
        private MenuDropDown _ddMomentUnit;
        private MenuCheckBox _ckSmooth;
        private MenuSlider _slScale;
        private MenuCheckBox _ckAxes;
        private MenuCheckBox _ckValues;
        private MenuSlider _slTextSize;
        private MenuStaticText _txTextSize;

        public ShellSectionForces()
          : base("Shell Section (Alpaca4d)", "Shell Section",
            "Draw a section across shells and read the forces through it, as a diagram along the cut " +
            "and as totals.\n" +
            "The curve is swept along Direction until it meets the shells; without a Direction it is swept " +
            "square to the shell nearest its start, which cuts a shell along a curve drawn on it.\n" +
            "The cut has axes of its own: x square to it in the shell's plane, to the left of the curve " +
            "looking along Direction; y along the curve; z the shell's normal on the side Direction points to. " +
            "pxx, pxy, vxz, mxx and mxy are the forces per length on the face of the cut, named as in Shell " +
            "Forces. N, Vy, Vz, Mx, My and Mz are their totals, about the centre of the cut, read like Beam " +
            "Forces for a beam whose axis is x. Both are what the side x points to puts on the other.\n" +
            "Reads ASD Shell elements.",
            "Alpaca4d", "09_Visualisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Curve", "Curve",
                "Where to cut: a line or polyline drawn on the shells, or beside them to be swept onto them along " +
                "Direction. Any other curve is followed by a polyline close to it. Its direction sets which way y runs.",
                GH_ParamAccess.item);
            pManager.AddVectorParameter("Direction", "Direction",
                "Which way to sweep the curve to meet the shells. The cut is where the curve, slid both ways along " +
                "this, crosses them: a line drawn in plan and swept vertically cuts every wall it crosses, top to " +
                "bottom. Also sets which side is positive: z is the shell's normal on the side this points to.\n" +
                "Empty: square to the shell nearest the curve's start, on the side its local z points to.",
                GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to read.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.Filter + "\nOnly the shells it matches are cut.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Cut", "Cut", "Where the shells are cut: a polyline per unbroken stretch, one branch each.", GH_ParamAccess.tree);
            pManager.AddPointParameter("Points", "Points",
                "The points the forces are read at: both ends and the middle of the cut through each shell. Unsmoothed, " +
                "a point where two shells meet is listed twice, once with each one's forces.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("pxx", "pxx", $"[{Units.Force}/{Units.Length}] Membrane force across the cut, along x. Positive is tension.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("pxy", "pxy", $"[{Units.Force}/{Units.Length}] In-plane shear along the cut, along y.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("vxz", "vxz", $"[{Units.Force}/{Units.Length}] Transverse shear on the cut, along z.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("mxx", "mxx", $"[{Units.Force}{Units.Length}/{Units.Length}] Bending moment whose stresses run across the cut. Positive puts the +z face in tension.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("mxy", "mxy", $"[{Units.Force}{Units.Length}/{Units.Length}] Twisting moment on the cut.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("N", "N", $"[{Units.Force}] Total force across the cut, along x. Positive is tension.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Vy", "Vy", $"[{Units.Force}] Total in-plane shear, along y.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Vz", "Vz", $"[{Units.Force}] Total shear square to the shells, along z.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mx", "Mx", $"[{Units.Force}{Units.Length}] Torsion about x, right-hand rule.", GH_ParamAccess.item);
            pManager.AddNumberParameter("My", "My", $"[{Units.Force}{Units.Length}] Bending about y: the shells bent out of their plane. Positive puts the +z side in tension.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mz", "Mz", $"[{Units.Force}{Units.Length}] Bending about z: the shells bent in their plane, as a wall is. Positive puts the +y end in compression.", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Axes", "Axes", "The axes of the totals, at the centre of the cut the moments are taken about: x, y and z, each the mean over the cut.", GH_ParamAccess.item);
        }

        #region UI

        private static MenuStaticText Caption(string text) => new MenuStaticText { Text = text };

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            var diagramMenu = new GH_ExtendableMenu(0, "Diagram") { Name = "Diagram", Header = "What to draw" };
            var diagramPanel = new MenuPanel(0, "diagram_panel");

            _ddComponent = new MenuDropDown(0, "Component", "Component") { VisibleItemCount = 5 };
            foreach (var name in Alpaca4d.Result.ShellSection.ComponentNames)
                _ddComponent.AddItem(name, name);
            _ddComponent.Value = 3;
            _ddComponent.ValueChanged += OnWidgetChanged;

            _ddForceUnit = UnitDropDown(1, Quantity.ForcePerLength);
            _ddMomentUnit = UnitDropDown(2, Quantity.MomentPerLength);

            _ckSmooth = new MenuCheckBox(0, "Smooth", "Smooth across shells") { Active = true };
            _ckSmooth.ValueChanged += OnWidgetChanged;

            _slScale = new MenuSlider(0, "DiagramScale", 0.0, 20.0, 1.0, 2);
            _slScale.ValueChanged += OnWidgetChanged;

            _ckAxes = new MenuCheckBox(1, "Axes", "Section axes") { Active = true };
            _ckAxes.ValueChanged += OnWidgetChanged;

            _ckValues = new MenuCheckBox(2, "Values", "Show values");
            _ckValues.ValueChanged += OnWidgetChanged;

            _slTextSize = new MenuSlider(1, "TextSize", 0.05, 5.0, 0.5, 2);
            _slTextSize.ValueChanged += OnWidgetChanged;
            _txTextSize = Caption("Value text size");

            diagramPanel.AddControl(Caption("Component"));
            diagramPanel.AddControl(_ddComponent);
            diagramPanel.AddControl(Caption("Unit"));
            diagramPanel.AddControl(_ddForceUnit);
            diagramPanel.AddControl(_ddMomentUnit);
            diagramPanel.AddControl(_ckSmooth);
            diagramPanel.AddControl(Caption("Diagram scale"));
            diagramPanel.AddControl(_slScale);
            diagramPanel.AddControl(_ckAxes);
            diagramPanel.AddControl(_ckValues);
            diagramPanel.AddControl(_txTextSize);
            diagramPanel.AddControl(_slTextSize);
            diagramMenu.AddControl(diagramPanel);
            diagramMenu.Expand();
            attr.AddMenu(diagramMenu);

            attr.MinWidth = 200f;

            UpdateVisibility();
        }

        private MenuDropDown UnitDropDown(int index, Quantity quantity)
        {
            var units = ResultUnits.Of(quantity);
            var dropdown = new MenuDropDown(index, "Unit" + quantity, "Unit") { VisibleItemCount = units.Length };
            foreach (var unit in units)
                dropdown.AddItem(unit.Symbol, unit.Caption);
            dropdown.Value = ResultUnits.Default(quantity);
            dropdown.ValueChanged += OnWidgetChanged;
            return dropdown;
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            if (_ddComponent == null) return;

            _ddComponent.ValueChanged += OnWidgetChanged;
            _ddForceUnit.ValueChanged += OnWidgetChanged;
            _ddMomentUnit.ValueChanged += OnWidgetChanged;
            _ckSmooth.ValueChanged += OnWidgetChanged;
            _slScale.ValueChanged += OnWidgetChanged;
            _ckAxes.ValueChanged += OnWidgetChanged;
            _ckValues.ValueChanged += OnWidgetChanged;
            _slTextSize.ValueChanged += OnWidgetChanged;

            UpdateVisibility();
        }

        private void OnWidgetChanged(object sender, EventArgs e)
        {
            UpdateVisibility();
            ExpireSolution(true);
        }

        /// <summary>Only the unit of the component drawn, and the text size only with values on.</summary>
        private void UpdateVisibility()
        {
            if (_ddComponent == null) return;

            bool moment = Quantity == Quantity.MomentPerLength;
            Show(!moment, _ddForceUnit);
            Show(moment, _ddMomentUnit);
            Show(_ckValues?.Active ?? false, _txTextSize, _slTextSize);

            Attributes?.ExpireLayout();
            Instances.ActiveCanvas?.Refresh();
        }

        private static void Show(bool visible, params GH_Attr_Widget[] widgets)
        {
            foreach (var widget in widgets)
            {
                widget.Visible = visible;
                if (!visible && widget is MenuDropDown dropdown) dropdown.expanded = false;
            }
        }

        /// <summary>The component drawn, in the order of ShellSection.ComponentNames.</summary>
        private int Component => _ddComponent?.Value ?? 3;

        /// <summary>The moments, mxx and mxy, are moments per length; the rest forces per length.</summary>
        private Quantity Quantity => Component == 3 || Component == 4 ? Quantity.MomentPerLength : Quantity.ForcePerLength;

        private int UnitIndex => Quantity == Quantity.MomentPerLength
            ? _ddMomentUnit?.Value ?? ResultUnits.Default(Quantity.MomentPerLength)
            : _ddForceUnit?.Value ?? ResultUnits.Default(Quantity.ForcePerLength);

        #endregion

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();

            _cuts.Clear();
            _model = null;
            _diagram = null;
            _lines.Clear();
            _axes.Clear();
            _labels.Clear();
            _box = BoundingBox.Empty;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Alpaca4d.Model model = null;
            if (!DA.GetData(0, ref model) || model == null) return;

            Curve curve = null;
            if (!DA.GetData(1, ref curve) || curve == null) return;

            var direction = Vector3d.Unset;
            bool directed = DA.GetData(2, ref direction) && direction.IsValid && direction.Length > 0.0;

            int step = 0;
            DA.GetData(3, ref step);

            var terms = new List<string>();
            DA.GetDataList(4, terms);

            var section = Section(model, step, terms, DA);
            if (section == null) return;

            var polyline = PolylineOf(curve);
            if (polyline == null || polyline.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The curve has no length to cut with.");
                return;
            }

            if (!directed)
                direction = section.NormalNear(polyline[0]);

            SectionCut cut;
            try
            {
                cut = section.Cut(polyline, direction);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            if (cut.Runs.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    directed
                        ? "The curve, swept along Direction, crosses none of the shells. A curve drawn on the shells wants a Direction square to them, or none."
                        : "The curve, swept square to the shell nearest it, crosses none of the shells. Give a Direction that takes it onto them.");
                return;
            }

            _model = model;
            _cuts.Add(cut);

            // A branch per unbroken stretch, under one per curve. Every curve of a list shares the
            // branch Grasshopper puts their totals in, so the curve's place in it is added: the
            // stretches of the second curve of {0} go in {0;1;...}, beside its N, the second of {0}.
            var cuts = new DataTree<Curve>();
            var points = new DataTree<Point3d>();
            var values = Enumerable.Range(0, 5).Select(_ => new DataTree<double>()).ToArray();

            var root = DA.ParameterTargetPath(7).AppendElement(DA.ParameterTargetIndex(7));
            for (int r = 0; r < cut.Runs.Count; r++)
            {
                var run = cut.Runs[r];
                var path = root.AppendElement(r);

                cuts.Add(new PolylineCurve(run.Cut), path);
                points.AddRange(run.Stations.Select(s => s.Point), path);
                for (int c = 0; c < 5; c++)
                    values[c].AddRange(run.Stations.Select(s => s.Component(c)), path);
            }

            DA.SetDataTree(0, cuts);
            DA.SetDataTree(1, points);
            for (int c = 0; c < 5; c++)
                DA.SetDataTree(2 + c, values[c]);

            DA.SetData(7, cut.N);
            DA.SetData(8, cut.Vy);
            DA.SetData(9, cut.Vz);
            DA.SetData(10, cut.Mx);
            DA.SetData(11, cut.My);
            DA.SetData(12, cut.Mz);
            DA.SetData(13, cut.Axes);
        }

        /// <summary>
        /// The model's ASD shells with their forces, ready to be cut: read once per model, step,
        /// filter and smoothing, however many curves cut them. Null, with a message, when there is
        /// nothing to cut.
        /// </summary>
        private Alpaca4d.Result.ShellSection Section(Alpaca4d.Model model, int step, List<string> terms, IGH_DataAccess DA)
        {
            bool smooth = _ckSmooth?.Active ?? true;
            string key = $"{model.GetHashCode()}|{step}|{smooth}|{string.Join("\u0001", terms)}";

            var filter = ElementFilterInput.Read(DA, 4, this);

            if (key == _sectionKey && _section != null)
                return _section;

            _sectionKey = null;
            _section = null;

            var shells = model.Shells
                .Where(x => x.ElementClass == Element.ElementClass.ASDShellQ4 || x.ElementClass == Element.ElementClass.ASDShellT3)
                .ToList();

            if (shells.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The model holds no ASD Shell elements to cut.");
                return null;
            }

            var kept = ElementFilterInput.Select(filter, shells, model, "ASD shells", this);
            if (kept.Count == 0) return null;

            Dictionary<int, double[][]> forces;
            try
            {
                forces = Alpaca4d.Result.Read.ShellForcesByTag(model, step);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return null;
            }

            var faces = new List<SectionFace>();
            foreach (int i in kept)
            {
                var shell = shells[i];
                if (!shell.Id.HasValue || shell.IndexNodes == null || !forces.TryGetValue(shell.Id.Value, out var gauss))
                    continue;

                var nodes = shell.Mesh.Vertices.ToPoint3dArray();
                if (nodes.Length != shell.IndexNodes.Count || shell.IndexNodes.Any(x => !x.HasValue))
                    continue;

                var localX = shell is Element.ASDShellQ4 quad ? quad.LocalX
                           : shell is Element.ASDShellT3 triangle ? triangle.LocalX
                           : Vector3d.Zero;

                faces.Add(SectionFace.Of(shell.Id.Value, nodes, shell.IndexNodes.Select(x => x.Value).ToList(),
                                         localX, gauss, shell.Section?.Thickness ?? 0.0));
            }

            if (faces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "The recorder file holds no shell forces. Switch \"section.force\" on in the MPCO Recorder component and run the analysis again.");
                return null;
            }

            _section = new Alpaca4d.Result.ShellSection(faces, smooth);
            _sectionKey = key;
            return _section;
        }

        /// <summary>The curve as a polyline: itself if it is one, otherwise one within the document's tolerance of it.</summary>
        private static List<Point3d> PolylineOf(Curve curve)
        {
            if (curve.TryGetPolyline(out var polyline))
                return polyline.ToList();

            double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
            var approximation = curve.ToPolyline(tolerance, RhinoMath.ToRadians(2.0), 0.0, 0.0);
            return approximation != null && approximation.TryGetPolyline(out polyline) ? polyline.ToList() : null;
        }

        #region Drawing

        /// <summary>Everything drawn, built once every curve has been cut, to one scale.</summary>
        protected override void AfterSolveInstance()
        {
            base.AfterSolveInstance();

            if (_model == null || _cuts.Count == 0) return;

            int component = Component;
            var unit = ResultUnits.At(Quantity, UnitIndex);
            double factor = ResultUnits.Factor(Quantity, UnitIndex);
            double reach = Reach();

            double largest = _cuts.SelectMany(x => x.Runs).SelectMany(x => x.Stations)
                                  .Select(x => Math.Abs(x.Component(component)))
                                  .DefaultIfEmpty(0.0).Max();
            double length = largest > 0.0 ? reach / largest * (_slScale?.Value ?? 1.0) : 0.0;

            var meshes = new List<Mesh>();

            foreach (var cut in _cuts)
            {
                foreach (var run in cut.Runs)
                {
                    _lines.Add(run.Cut);

                    var diagram = Diagram(run.Stations, component, length);
                    if (diagram != null) meshes.Add(diagram);

                    if (_ckValues?.Active ?? false)
                        LabelExtremes(run.Stations, component, length, factor, unit.Symbol);
                }

                if ((_ckAxes?.Active ?? true) && cut.Axes.IsValid)
                {
                    double size = reach * 0.4;
                    var o = cut.Axes.Origin;
                    _axes.Add(Tuple.Create(new Line(o, o + cut.Axes.XAxis * size), Color.FromArgb(220, 40, 40)));
                    _axes.Add(Tuple.Create(new Line(o, o + cut.Axes.YAxis * size), Color.FromArgb(40, 170, 40)));
                    _axes.Add(Tuple.Create(new Line(o, o + cut.Axes.ZAxis * size), Color.FromArgb(40, 80, 220)));
                }
            }

            _diagram = Joined.Of(meshes);
            _box = Extent();
        }

        /// <summary>
        /// The diagram of one stretch: each value stood off its point along the cut's z, and closed
        /// back onto the cut. Where two shells meet unsmoothed, the two values at one point draw the
        /// step between them. Coloured the way a beam force diagram is, by its sign.
        /// </summary>
        private static Mesh Diagram(List<SectionStation> stations, int component, double length)
        {
            if (stations.Count < 2 || length <= 0.0) return null;

            int colourOf = BeamForceComponent(component);
            var mesh = new Mesh();
            var colours = new List<Color>();

            int Vertex(Point3d at, Color colour)
            {
                mesh.Vertices.Add(at);
                colours.Add(colour);
                return mesh.Vertices.Count - 1;
            }

            for (int i = 0; i + 1 < stations.Count; i++)
            {
                var a = stations[i];
                var b = stations[i + 1];
                if (a.Point.DistanceTo(b.Point) <= 0.0) continue;

                double fa = a.Component(component);
                double fb = b.Component(component);
                var ta = a.Point + a.Z * fa * length;
                var tb = b.Point + b.Z * fb * length;
                var ca = ResultField.BeamForceColour(colourOf, fa);
                var cb = ResultField.BeamForceColour(colourOf, fb);

                if (fa * fb < 0.0)
                {
                    // Changing sign: closed onto the cut where it crosses zero, a triangle either side.
                    var zero = a.Point + (b.Point - a.Point) * (fa / (fa - fb));
                    mesh.Faces.AddFace(Vertex(a.Point, ca), Vertex(zero, ca), Vertex(ta, ca));
                    mesh.Faces.AddFace(Vertex(zero, cb), Vertex(b.Point, cb), Vertex(tb, cb));
                }
                else
                {
                    mesh.Faces.AddFace(Vertex(a.Point, ca), Vertex(b.Point, cb), Vertex(tb, cb), Vertex(ta, ca));
                }
            }

            if (mesh.Faces.Count == 0) return null;

            mesh.VertexColors.SetColors(colours.ToArray());
            mesh.Normals.ComputeNormals();
            return mesh;
        }

        /// <summary>
        /// The beam force a component of the cut is drawn in the colours of: the membrane force as N,
        /// the shears as Vy and Vz, the bending moment as My, the twist as torsion.
        /// </summary>
        private static int BeamForceComponent(int component)
        {
            switch (component)
            {
                case 1: return 1;
                case 2: return 2;
                case 3: return 4;
                case 4: return 3;
                default: return 0;
            }
        }

        /// <summary>The largest and the smallest value of a stretch, written at the tip of the diagram.</summary>
        private void LabelExtremes(List<SectionStation> stations, int component, double length, double factor, string symbol)
        {
            if (stations.Count == 0) return;

            var high = stations.OrderByDescending(s => s.Component(component)).First();
            var low = stations.OrderBy(s => s.Component(component)).First();

            foreach (var s in high.Point.Equals(low.Point) ? new[] { high } : new[] { high, low })
            {
                double value = s.Component(component);
                _labels.Add(Tuple.Create(s.Point + s.Z * value * length, $"{value * factor:G4} {symbol}"));
            }
        }

        /// <summary>How far the largest value stands off the cut: the share of the model's size Model View's diagrams use.</summary>
        private double Reach()
        {
            var box = _model.UniquePoints != null && _model.UniquePoints.Count > 0
                ? new BoundingBox(_model.UniquePoints)
                : BoundingBox.Unset;

            return box.IsValid && box.Diagonal.Length > 0.0 ? box.Diagonal.Length * 0.08 : 1.0;
        }

        private BoundingBox Extent()
        {
            var box = BoundingBox.Empty;
            if (_diagram != null) box.Union(_diagram.GetBoundingBox(false));
            foreach (var line in _lines) box.Union(line.BoundingBox);
            foreach (var axis in _axes) box.Union(axis.Item1.BoundingBox);

            if (_labels.Count > 0)
            {
                double reach = 2.0 * (_slTextSize?.Value ?? 0.5);
                foreach (var label in _labels)
                    box.Union(new BoundingBox(label.Item1 - new Vector3d(reach, reach, reach), label.Item1 + new Vector3d(reach, reach, reach)));
            }

            return box;
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            // Nothing of the outputs: the diagram and the cut are drawn here, in colours of their own.
            if (this.Hidden || this.Locked || _diagram == null) return;
            args.Display.DrawMeshFalseColors(_diagram);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (this.Hidden || this.Locked) return;

            if (_diagram != null)
                args.Display.DrawMeshWires(_diagram, Color.Black, 1);

            var cutColour = this.Attributes.Selected ? args.WireColour_Selected : Color.FromArgb(40, 40, 40);
            foreach (var line in _lines)
                args.Display.DrawPolyline(line, cutColour, 3);

            foreach (var axis in _axes)
                args.Display.DrawArrow(axis.Item1, axis.Item2);

            if (_labels.Count > 0)
            {
                double height = _slTextSize?.Value ?? 0.5;
                var colour = AlpacaSettings.Colour(DisplayItem.Values);
                foreach (var label in _labels)
                {
                    var plane = new Plane(label.Item1, args.Viewport.CameraX, args.Viewport.CameraY);
                    args.Display.Draw3dText(label.Item2, colour, plane, height, "Arial", false, false);
                }
            }
        }

        public override bool IsPreviewCapable => true;

        public override BoundingBox ClippingBox => _box.IsValid ? _box : base.ClippingBox;

        public override bool IsBakeCapable => _diagram != null || _lines.Count > 0;

        /// <summary>The diagram, in its colours, and the cut.</summary>
        public override void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
        {
            if (doc == null) return;
            att = att ?? doc.CreateDefaultAttributes();

            if (_diagram != null)
                obj_ids.Add(doc.Objects.AddMesh(_diagram, att));

            foreach (var line in _lines)
                obj_ids.Add(doc.Objects.AddPolyline(line, att));
        }

        #endregion

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Shell_Section__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{14ACA00D-AF9A-4C33-BD42-CBCAAE0C05C1}");
    }
}
