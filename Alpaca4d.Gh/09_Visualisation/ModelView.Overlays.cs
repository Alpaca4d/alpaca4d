using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Geometry;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    // What Model View draws over the elements, whatever they are coloured by: node and element ids,
    // section names, local axes, end releases, loads, masses and supports.
    //
    // Worked out the first time they are needed after a solution and kept until the next, rather
    // than on every redraw. Model View used to work them out in DrawViewportWires - an area centroid
    // for every shell's id, an offset mesh for every area load, a frame for every element's axes -
    // so on a plate of 3000 shells each frame took 44 to 165 ms with one of them ticked, and the
    // viewport crawled when it was turned.
    public partial class ModelView
    {
        /// <summary>The load patterns whose loads are drawn, or null for all of them.</summary>
        private List<Alpaca4d.Loads.LoadPattern> _loadPatterns;

        /// <summary>The radius of a release circle at Release size 1; see ReleaseSymbols.Size.</summary>
        private double _releaseSize;

        private List<Tuple<string, Point3d>> _nodeLabels;
        private List<Tuple<string, Point3d>> _elementLabels;
        private List<Tuple<string, Point3d>> _sectionLabels;
        private List<(Point3d Origin, Vector3d X, Vector3d Y, Vector3d Z, double Reach)> _axes;

        // The loads at the Load scale they were drawn for, and the supports at theirs.
        private LoadPicture _loads;

        /// <summary>How far each node moved on the shape the solution drew, or null as it was built: the loads ride on it.</summary>
        private Dictionary<int, Vector3d> _drawnDisplacement;
        private SupportPicture _supports;

        private void ClearOverlays()
        {
            _nodeLabels = _elementLabels = _sectionLabels = null;
            _axes = null;
            _loads = null;
            _supports = null;
        }

        /// <summary>The elements the ids, axes and releases are drawn for: those the filter kept.</summary>
        private bool Shown(IElement element) => _filter == null || _filter.Matches(element);

        private void DrawOverlays(IGH_PreviewArgs args)
        {
            double textSize = _slTextSize?.Value ?? 0.5;

            if (_ckNodeIds?.Active ?? false)
                DrawLabels(args, NodeLabels(), AlpacaSettings.Colour(DisplayItem.NodeIds), textSize);

            // A beam's id and its section are written at its middle: one above the other when both are.
            bool both = (_ckElementIds?.Active ?? false) && (_ckSectionNames?.Active ?? false);

            if (_ckElementIds?.Active ?? false)
                DrawLabels(args, ElementLabels(), AlpacaSettings.Colour(DisplayItem.ElementIds), textSize, both ? 0.5 : 0.0);

            if (_ckSectionNames?.Active ?? false)
                DrawLabels(args, SectionLabels(), AlpacaSettings.Colour(DisplayItem.SectionNames), textSize, both ? -0.5 : 0.0);

            if (_ckLocalAxes?.Active ?? false)
                DrawAxes(args, _slAxesSize?.Value ?? 1.0);

            if (_ckReleases?.Active ?? false)
                DrawReleases(args);

            bool showLoads = _ckShowLoads?.Active ?? false;
            bool showLoadValues = _ckShowLoadValues?.Active ?? false;
            if (showLoads || showLoadValues)
                Loads().Draw(args, showLoads, showLoadValues, textSize);

            if (_ckShowSupports?.Active ?? false)
                Supports().Draw(args);
        }

        /// <summary>What the overlays switched on span, at the scales in force.</summary>
        private BoundingBox OverlayExtent()
        {
            var box = BoundingBox.Empty;
            if (_model == null) return box;

            if ((_ckShowLoads?.Active ?? false) || (_ckShowLoadValues?.Active ?? false))
                box.Union(Loads().Extent);

            if (_ckShowSupports?.Active ?? false)
                box.Union(Supports().Extent);

            // The axes stick out of their elements, and the labels out of their points.
            double reach = 0.0;
            if (_ckLocalAxes?.Active ?? false)
                reach = Axes().Select(x => x.Reach).DefaultIfEmpty(0.0).Max() * (_slAxesSize?.Value ?? 1.0);
            if (Labelled)
                reach = Math.Max(reach, 2.0 * (_slTextSize?.Value ?? 0.5));

            if (reach > 0.0 && _model.UniquePoints != null && _model.UniquePoints.Count > 0)
            {
                var model = new BoundingBox(_model.UniquePoints);
                model.Inflate(reach);
                box.Union(model);
            }

            return box;
        }

        #region Ids, names and axes

        private List<Tuple<string, Point3d>> NodeLabels()
        {
            return _nodeLabels ?? (_nodeLabels = _model.Nodes.Select(node => Tuple.Create($"N{node.Id}", node.Pos)).ToList());
        }

        private List<Tuple<string, Point3d>> ElementLabels()
        {
            if (_elementLabels != null) return _elementLabels;

            _elementLabels = new List<Tuple<string, Point3d>>();
            foreach (var beam in _model.Beams.Where(Shown))
                _elementLabels.Add(Tuple.Create($"E{beam.Id}", beam.Curve.PointAtNormalizedLength(0.5)));
            foreach (var shell in _model.Shells.Where(Shown))
                _elementLabels.Add(Tuple.Create($"E{shell.Id}", AreaMassProperties.Compute(shell.Mesh).Centroid));
            foreach (var brick in _model.Bricks.Where(Shown))
                _elementLabels.Add(Tuple.Create($"E{brick.Id}", VolumeMassProperties.Compute(brick.Mesh).Centroid));
            return _elementLabels;
        }

        private List<Tuple<string, Point3d>> SectionLabels()
        {
            return _sectionLabels ?? (_sectionLabels = _model.Beams.Where(Shown)
                .Select(beam => Tuple.Create(beam.Section?.SectionName ?? beam.Section?.GetType().Name, beam.Curve.PointAtNormalizedLength(0.5)))
                .ToList());
        }

        /// <summary>
        /// Every element's axes, where they are drawn from and how long at Axes size 1.
        ///
        /// Shells: pxx, mxx and sigma11 act along the red arrow (axis 1), pyy, myy and sigma22 along
        /// the green one (axis 2), and the blue one is the normal (axis 3) - the side a positive mxx
        /// or myy puts in tension, and the side the Top layer is on. The shell's LocalPlane rather
        /// than anything worked out here, because which frame a shell reports in is not obvious - a
        /// quad's section axes are turned away from its first edge even with no -local given.
        ///
        /// Solids: sigma11 is the direct stress along the red arrow, sigma22 the green and sigma33 the
        /// blue, when Brick Stresses reads them in local axes. Utils.SolidFrame, not brick.LocalPlane,
        /// even though the plane is built from exactly this: it is the same call the stress reader
        /// makes to rotate the tensor, so what is drawn here and what comes out of Brick Stresses
        /// cannot drift apart.
        /// </summary>
        private List<(Point3d Origin, Vector3d X, Vector3d Y, Vector3d Z, double Reach)> Axes()
        {
            if (_axes != null) return _axes;

            _axes = new List<(Point3d, Vector3d, Vector3d, Vector3d, double)>();

            foreach (var beam in _model.Beams.Where(Shown))
            {
                var localX = beam.Curve.PointAtEnd - beam.Curve.PointAtStart;
                localX.Unitize();
                _axes.Add((beam.Curve.PointAtNormalizedLength(0.5), localX, beam.GeomTransf.LocalY, beam.GeomTransf.LocalZ, AxesReach(beam)));
            }

            foreach (var shell in _model.Shells.Where(Shown))
            {
                Plane frame;
                try
                {
                    frame = shell.LocalPlane;
                }
                catch (Exception)
                {
                    continue;
                }

                _axes.Add((frame.Origin, frame.XAxis, frame.YAxis, frame.ZAxis, AxesReach(shell)));
            }

            foreach (var brick in _model.Bricks.Where(Shown))
            {
                var nodes = brick.Mesh.Vertices.ToPoint3dArray();

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

                _axes.Add((origin, frame.X, frame.Y, frame.Z, AxesReach(brick)));
            }

            return _axes;
        }

        /// <summary>How long an element's axes are drawn at Axes size 1: in proportion to the element.</summary>
        private static double AxesReach(IBeam beam) => beam.Curve.GetLength() * 0.2;

        private static double AxesReach(IShell shell) => Math.Sqrt(AreaMassProperties.Compute(shell.Mesh).Area) * 0.35;

        // A quarter of the element's own reach, so the arrows stay inside a small brick and are
        // still visible on a large one.
        private static double AxesReach(IBrick brick) => brick.Mesh.GetBoundingBox(false).Diagonal.Length * 0.25;

        /// <summary>Labels <paramref name="height"/> tall in model units - the Text size slider - each on its plate; see <see cref="ViewportLabels"/>.</summary>
        private static void DrawLabels(IGH_PreviewArgs args, IEnumerable<Tuple<string, Point3d>> labels, Color colour, double height, double lines = 0.0)
        {
            foreach (var label in labels)
                ViewportLabels.Draw(args.Display, args.Viewport, label.Item1, colour, label.Item2, height, lines);
        }

        /// <summary>
        /// Every element's axes: red, green and blue for the first, second and third - x, y, z on a
        /// beam, 1, 2, 3 on a shell or a solid. Unlabelled, because the colours say which is which.
        /// Thick, with a head in proportion to the arrow, so Axes size makes the whole of it bigger
        /// rather than a longer hairline. The shafts of each colour in one call.
        /// </summary>
        private void DrawAxes(IGH_PreviewArgs args, double size)
        {
            var shafts = new[] { new List<Line>(), new List<Line>(), new List<Line>() };
            foreach (var axes in Axes())
            {
                double length = axes.Reach * size;
                if (!(length > 0.0)) continue;

                shafts[0].Add(new Line(axes.Origin, axes.Origin + axes.X * length));
                shafts[1].Add(new Line(axes.Origin, axes.Origin + axes.Y * length));
                shafts[2].Add(new Line(axes.Origin, axes.Origin + axes.Z * length));
            }

            var colours = new[] { Color.Red, Color.Green, Color.Blue };
            for (int i = 0; i < 3; i++)
            {
                var lines = shafts[i].Where(line => line.IsValid && line.Length > 0.0).ToList();
                args.Display.DrawLines(lines, colours[i], 3);
                foreach (var line in lines)
                    args.Display.DrawArrowHead(line.To, line.Direction, colours[i], 0.0, line.Length * 0.2);
            }
        }

        /// <summary>
        /// The end releases: a circle for a released rotation, two lines for a released
        /// translation. Each frame, as the lines turn to face the camera. Over the model, as the
        /// labels are: a circle smaller than the section would otherwise vanish inside the extruded
        /// beam it marks.
        /// </summary>
        private void DrawReleases(IGH_PreviewArgs args)
        {
            double size = _releaseSize * (_slReleaseSize?.Value ?? 1.0);
            var view = args.Viewport.CameraDirection;

            args.Display.PushDepthTesting(false);
            foreach (var beam in _model.Beams.Where(Shown))
            {
                var (circles, lines) = Alpaca4d.Visualisation.ReleaseSymbols.For(beam, size, view);
                foreach (var (circle, colour) in circles)
                    args.Display.DrawCircle(circle, colour, 2);
                foreach (var (line, colour) in lines)
                    args.Display.DrawLine(line, colour, 3);
            }
            args.Display.PopDepthTesting();
        }

        /// <summary>
        /// A beam as its cross-section carried from one end to the other, stood up in the beam's
        /// local axes - the section's x along local z, its y along local y, as Beam Stresses reads
        /// it. Every outline, so a hollow section shows its inside and a double angle both angles.
        /// Null when there is no outline to carry, and the beam is drawn as a line. Explorer Record
        /// draws a model's beams with it, so its 3D view matches this one.
        /// </summary>
        internal static Mesh ExtrudedBeam(IBeam beam)
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

        #endregion

        #region Loads and supports

        /// <summary>The loads, made for the Load scale and pattern in force, or again when either has changed.</summary>
        private LoadPicture Loads()
        {
            double scale = _slLoadScale?.Value ?? 1.0;
            if (_loads == null || _loads.Scale != scale)
                _loads = new LoadPicture(_model, _loadPatterns, scale, _drawnDisplacement);
            return _loads;
        }

        /// <summary>
        /// The load patterns the LoadPattern input picks: by name, whatever its case and the spaces
        /// round it, as Mass from Loads looks them up, then by Id. A number wired in is read as the
        /// same text typed, and a Load Pattern wired in by its Id. Null when nothing is given, which
        /// draws them all; empty, with a warning saying what there is, when nothing matches.
        /// </summary>
        private List<Alpaca4d.Loads.LoadPattern> LoadPatterns(IGH_DataAccess DA)
        {
            Grasshopper.Kernel.Types.IGH_Goo goo = null;
            if (!DA.GetData(LoadPatternInput, ref goo) || goo == null)
                return null;

            var patterns = _model.LoadPatterns ?? new List<Alpaca4d.Loads.LoadPattern>();
            var value = goo.ScriptVariable();
            if (value is Alpaca4d.Loads.LoadPattern wired)
            {
                var same = patterns.Where(x => x.Id == wired.Id).ToList();
                if (same.Count == 0)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"The Load Pattern wired into LoadPattern (Id {wired.Id}) is not in this model, so none of its loads are drawn. Wire it into Assemble, or give its Name instead.");
                return same;
            }

            string text;
            switch (value)
            {
                case string s: text = s; break;
                case int i: text = i.ToString(CultureInfo.InvariantCulture); break;
                case long l: text = l.ToString(CultureInfo.InvariantCulture); break;
                case double d: text = d.ToString(CultureInfo.InvariantCulture); break;
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"LoadPattern takes a load pattern's Name or Id, or the Load Pattern itself. Given: {value?.GetType().Name ?? goo.TypeName}.");
                    return new List<Alpaca4d.Loads.LoadPattern>();
            }
            if (string.IsNullOrWhiteSpace(text))
                return null;

            text = text.Trim();
            var found = patterns.Where(x => Alpaca4d.Loads.MassFromLoads.Names(x.Name, text)).ToList();
            if (found.Count == 0 && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                found = patterns.Where(x => x.Id == id).ToList();

            if (found.Count == 0)
            {
                var known = patterns.Select(x => string.IsNullOrWhiteSpace(x.Name) ? $"Id {x.Id}" : $"\"{x.Name.Trim()}\" (Id {x.Id})").ToList();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"No load pattern is named or numbered \"{text}\", so no loads are drawn. " +
                    (known.Count > 0 ? $"The model's are {string.Join(", ", known)}." : "The model has no load patterns."));
            }
            return found;
        }

        /// <summary>The supports, made for the Support scale in force, or again when it has changed.</summary>
        private SupportPicture Supports()
        {
            double scale = _slSupportScale?.Value ?? 1.0;
            if (_supports == null || _supports.Scale != scale)
                _supports = new SupportPicture(_model, scale);
            return _supports;
        }

        /// <summary>
        /// The loads of a model at one Load scale: arrows ending on the node, beam or shell they
        /// push, a see-through sheet over each loaded shell on the side the load comes from, an arc
        /// for each moment, a row of arrows over the model for its own weight, a dot for each mass,
        /// and what each is worth, at the tail of its arrow.
        ///
        /// Each kind of load is drawn to fit the model: at Load scale 1 the largest point load's
        /// arrow is <see cref="Fit"/> of the model's size, the largest line and area loads' a share
        /// of that, the rest in proportion, whatever the loads are worth. Load scale used to be metres of arrow per kN, so that 50 kN on a frame 8 m wide
        /// stood 50 m tall and its value was off the screen, while an area load of 3 kN/m² beside it
        /// stood 3 m off.
        ///
        /// A load given element by element - a Linear Load wired to a list of beams is one load a
        /// beam - is still written once: the pieces with the same force that touch are taken
        /// together, and spread their arrows evenly over the whole of them.
        /// </summary>
        private sealed class LoadPicture
        {
            public readonly double Scale;
            public BoundingBox Extent = BoundingBox.Empty;

            private readonly List<Tuple<Line, Color>> _arrows = new List<Tuple<Line, Color>>();
            private readonly List<Tuple<Line, Color>> _tails = new List<Tuple<Line, Color>>();
            private readonly List<(Arc Arc, Point3d Tip, Vector3d Tangent, Color Colour)> _moments = new List<(Arc, Point3d, Vector3d, Color)>();
            private readonly List<Tuple<Point3d, Color>> _masses = new List<Tuple<Point3d, Color>>();
            private readonly List<(string Text, Color Colour, Point3d At, Vector3d Away, double Lines)> _values = new List<(string, Color, Point3d, Vector3d, double)>();
            private readonly Mesh _sheets;

            /// <summary>The largest point load's arrow at Load scale 1, as a share of the model's diagonal.</summary>
            private const double Fit = 0.15;

            /// <summary>
            /// The largest line load's arrows and the largest area load's, as a share of the largest
            /// point load's: shorter, as a drawing has them, so that a point load in a span and the
            /// line load along it, or a slab's load and its edge beams', are not written in one place.
            /// </summary>
            private const double LineShare = 0.6, AreaShare = 0.4;

            /// <summary>
            /// The self-weight's largest arrows, on a node and along a beam, as a share of the largest
            /// point load's and line load's: shorter, so that a load on the same node or beam stands
            /// out of them with its value.
            /// </summary>
            private const double GravityShare = 0.5;

            /// <summary>
            /// The shortest arrow, as a share of the largest of its kind: one load a hundred times
            /// smaller than another would otherwise be a dot under its value.
            /// </summary>
            private const double Shortest = 0.1;

            /// <summary>How far apart the arrows over a beam or a shell are, as a share of the model's diagonal.</summary>
            private const double Spacing = 0.05;

            /// <summary>How wide a load arrow's shaft is drawn, in pixels. DrawArrow draws it at one, which was hard to pick out.</summary>
            private const int ArrowThickness = 3;

            /// <summary>
            /// How far each node moved on the shape drawn, by its tag: the deformed shape's, when
            /// Deformed shape is ticked, so that every load rides on the point it acts on; null when the
            /// model is drawn as it was built.
            /// </summary>
            private readonly Dictionary<int, Vector3d> _displacement;
            private readonly Alpaca4d.Model _model;

            /// <param name="patterns">The patterns whose loads are drawn, or null for all of the model's.</param>
            public LoadPicture(Alpaca4d.Model model, IEnumerable<Alpaca4d.Loads.LoadPattern> patterns, double scale, Dictionary<int, Vector3d> displacement = null)
            {
                Scale = scale;
                _model = model;
                _displacement = displacement != null && displacement.Count > 0 ? displacement : null;

                patterns = patterns ?? model.LoadPatterns;
                // A ground motion's pattern has no loads of its own.
                var loads = patterns.Where(x => x.Load != null).SelectMany(x => x.Load).ToList();

                var box = model.UniquePoints != null && model.UniquePoints.Count > 0 ? new BoundingBox(model.UniquePoints) : BoundingBox.Unset;
                double size = box.IsValid && box.Diagonal.Length > 0.0 ? box.Diagonal.Length : 1.0;
                double reach = size * Fit * scale;
                double spacing = size * Spacing;
                double tolerance = Math.Max(model.Tollerance, size * 1e-6);

                // The self-weight first, so that a load given on the same node is drawn over it.
                Gravity(loads.OfType<Alpaca4d.Loads.Gravity>().ToList(), model, reach * GravityShare, spacing, tolerance);
                PointLoads(loads.OfType<Alpaca4d.Loads.PointLoad>().ToList(), reach);
                LineLoads(loads.OfType<Alpaca4d.Loads.LineLoad>().ToList(), model, reach * LineShare, spacing, tolerance, AlpacaSettings.Colour(DisplayItem.LineLoads));
                _sheets = AreaLoads(loads.OfType<Alpaca4d.Loads.MeshLoad>().ToList(), model, reach * AreaShare, spacing, tolerance);
                if (_sheets != null) Extent.Union(_sheets.GetBoundingBox(false));
                Masses(model.Mass);
            }

            /// <summary>An arrow on the node for a force, an arc round it for a moment, each with its value.</summary>
            private void PointLoads(List<Alpaca4d.Loads.PointLoad> loads, double reach)
            {
                var colour = AlpacaSettings.Colour(DisplayItem.PointLoads);
                double largestForce = loads.Select(x => x.Force.Length).DefaultIfEmpty(0.0).Max();

                foreach (var load in loads)
                {
                    var at = load.Pos + Moved(load.Pos);
                    if (load.Force.Length > 0.0)
                    {
                        var arrow = load.Force / load.Force.Length * Length(load.Force.Length, largestForce, reach);
                        Arrow(new Line(at - arrow, at), colour);
                        Value(LoadValue(load.Force.Length, $"{Units.Force}"), colour, at - arrow, -arrow);
                    }

                    if (load.Moment.Length > 0.0)
                        Moment(at, load.Moment, reach * 0.4, colour);
                }
            }

            /// <summary>
            /// Three quarters of a circle round the moment's axis, turning the way the moment does
            /// by the right-hand rule, with its head at the end and its value outside it.
            /// </summary>
            private void Moment(Point3d at, Vector3d moment, double radius, Color colour)
            {
                var arc = new Arc(new Plane(at, moment), radius, 1.5 * Math.PI);
                var curve = arc.ToNurbsCurve();
                _moments.Add((arc, curve.PointAtEnd, curve.TangentAtEnd, colour));
                Extent.Union(arc.BoundingBox());

                var middle = arc.MidPoint;
                Value(LoadValue(moment.Length, $"{Units.Force}·{Units.Length}"), colour, middle, middle - at);
            }

            /// <summary>
            /// Arrows spread along each run of beams with the same load, a line along their tails,
            /// and the load's value once a run, over its middle.
            /// </summary>
            private void LineLoads(List<Alpaca4d.Loads.LineLoad> loads, Alpaca4d.Model model, double reach, double spacing, double tolerance, Color colour, bool eachValueOnce = false)
            {
                var pieces = loads.Where(load => load.GlobalForce.Length > 0.0)
                                  .SelectMany(load => load.AppliedTo(model.Beams).Select(beam => (Beam: beam, Force: load.GlobalForce)))
                                  .ToList();
                if (pieces.Count == 0) return;

                double largest = pieces.Max(x => x.Force.Length);
                var ends = pieces.Select(x => new[] { x.Beam.Curve.PointAtStart, x.Beam.Curve.PointAtEnd }).ToList();
                var written = new List<(string Value, Point3d At, Vector3d Away)>();

                foreach (var run in Touching(pieces.Select(x => x.Force).ToList(), ends, tolerance))
                {
                    var force = pieces[run[0]].Force;
                    var arrow = force / force.Length * Length(force.Length, largest, reach);

                    // Where the run stops first, so that thinning out the arrows never drops them.
                    var spread = new Spread(spacing * 0.6);
                    var moved = new Dictionary<(long, long, long), Vector3d>();
                    var count = new Dictionary<(long, long, long), int>();
                    foreach (int i in run)
                        foreach (var end in ends[i])
                        {
                            var key = Key(end, tolerance);
                            count[key] = count.TryGetValue(key, out int n) ? n + 1 : 1;
                        }
                    foreach (int i in run)
                        for (int e = 0; e < 2; e++)
                            if (count[Key(ends[i][e], tolerance)] == 1)
                            {
                                spread.Add(ends[i][e]);
                                moved[Key(ends[i][e], tolerance)] = Moved(pieces[i].Beam, e);
                            }

                    foreach (int i in run)
                    {
                        var beam = pieces[i].Beam;
                        var curve = beam.Curve;
                        int steps = Math.Max(1, (int)Math.Ceiling(curve.GetLength() / spacing));
                        for (int k = 0; k <= steps; k++)
                        {
                            var point = curve.PointAtNormalizedLength(k / (double)steps);
                            spread.Add(point);
                            moved[Key(point, tolerance)] = Moved(beam, k / (double)steps);
                        }
                        Tail(new Line(curve.PointAtStart + Moved(beam, 0.0) - arrow, curve.PointAtEnd + Moved(beam, 1.0) - arrow), colour);
                    }

                    foreach (var point in spread.Kept)
                    {
                        var on = point + (moved.TryGetValue(Key(point, tolerance), out var by) ? by : Vector3d.Zero);
                        Arrow(new Line(on - arrow, on), colour);
                    }

                    var middles = run.Select(i => pieces[i].Beam.Curve.PointAtNormalizedLength(0.5) + Moved(pieces[i].Beam, 0.5)).ToList();
                    written.Add((LoadValue(force.Length, $"{Units.Force}/{Units.Length}"), Nearest(middles, Centre(middles)) - arrow, -arrow));
                }

                // Once a run, or once a value: the self-weight of a frame is a run a column, all of the same weight.
                foreach (var same in written.GroupBy(x => eachValueOnce ? x.Value : x.Value + "@" + x.At))
                {
                    var at = Nearest(same.Select(x => x.At).ToList(), Centre(same.Select(x => x.At).ToList()));
                    Value(same.First().Value, colour, at, same.First(x => x.At == at).Away);
                }
            }

            /// <summary>
            /// For each group of touching shells with the same load, a sheet over each of them on the
            /// side the load comes from, arrows spread from the sheet down onto the shells, and its
            /// value once, on the sheet over its middle.
            /// </summary>
            private Mesh AreaLoads(List<Alpaca4d.Loads.MeshLoad> loads, Alpaca4d.Model model, double reach, double spacing, double tolerance)
            {
                var colour = AlpacaSettings.Colour(DisplayItem.AreaLoads);
                var pieces = loads.Where(load => load.GlobalForce.Length > 0.0)
                                  .SelectMany(load => load.AppliedTo(model.Shells).Select(shell => (Shell: shell, Force: load.GlobalForce)))
                                  .ToList();
                if (pieces.Count == 0) return null;

                double largest = pieces.Max(x => x.Force.Length);
                var corners = pieces.Select(x => x.Shell.Mesh.Vertices.ToPoint3dArray()).ToList();
                var sheets = new List<Mesh>();

                foreach (var group in Touching(pieces.Select(x => x.Force).ToList(), corners, tolerance))
                {
                    var force = pieces[group[0]].Force;
                    var lift = -force / force.Length * Length(force.Length, largest, reach);
                    var move = Transform.Translation(lift);

                    // The shells' middles first, then their corners, kept where there is room - as
                    // the model was built, so that a deformed shape keeps the same arrows - and moved
                    // with their nodes afterwards, a middle by its corners' mean.
                    var spread = new Spread(spacing * 0.6);
                    var moved = new Dictionary<(long, long, long), Vector3d>();
                    var middles = new List<Point3d>();
                    foreach (int i in group)
                    {
                        var nodes = pieces[i].Shell.IndexNodes;
                        var middle = Centre(corners[i]);
                        var shift = Vector3d.Zero;
                        for (int k = 0; k < corners[i].Length; k++) shift += Moved(nodes, k);
                        moved[Key(middle, tolerance)] = shift / corners[i].Length;
                        middles.Add(middle + shift / corners[i].Length);
                        spread.Add(middle);
                    }
                    foreach (int i in group)
                        for (int k = 0; k < corners[i].Length; k++)
                        {
                            spread.Add(corners[i][k]);
                            moved[Key(corners[i][k], tolerance)] = Moved(pieces[i].Shell.IndexNodes, k);
                        }

                    foreach (int i in group)
                    {
                        var sheet = pieces[i].Shell.Mesh.DuplicateMesh();
                        for (int k = 0; k < sheet.Vertices.Count && k < corners[i].Length; k++)
                            sheet.Vertices.SetVertex(k, corners[i][k] + Moved(pieces[i].Shell.IndexNodes, k));
                        sheet.Transform(move);
                        sheets.Add(sheet);
                    }

                    foreach (var point in spread.Kept)
                    {
                        var on = point + (moved.TryGetValue(Key(point, tolerance), out var by) ? by : Vector3d.Zero);
                        Arrow(new Line(on + lift, on), colour);
                    }

                    Value(LoadValue(force.Length, $"{Units.Force}/{Units.Length}²"), colour, Nearest(middles, Centre(middles)) + lift, lift);
                }

                return Joined.Of(sheets);
            }

            /// <summary>
            /// The model's own weight as OpenSees takes it, the loads Assemble writes for it, in the
            /// Gravity colour: along each beam, as a line load is drawn, and on the nodes of a shell,
            /// a solid or a truss, one arrow a node, the shares of every element meeting there added
            /// up, and each value they come to written once.
            /// </summary>
            private void Gravity(List<Alpaca4d.Loads.Gravity> loads, Alpaca4d.Model model, double reach, double spacing, double tolerance)
            {
                if (loads.Count == 0 || model.Elements == null || model.UniquePoints == null) return;

                var colour = AlpacaSettings.Colour(DisplayItem.Gravity);
                var applied = loads.SelectMany(load => model.CreateGravityLoad(load)).ToList();
                LineLoads(applied.OfType<Alpaca4d.Loads.LineLoad>().ToList(), model, reach * LineShare, spacing, tolerance, colour, eachValueOnce: true);

                // By where the node is: a solid's node and the beam's or shell's tied to it are one point.
                var nodes = applied.OfType<Alpaca4d.Loads.PointLoad>()
                                   .GroupBy(load => Key(load.Pos, tolerance))
                                   .Select(node => (At: node.First().Pos + Moved(node.First().Pos), Force: node.Aggregate(Vector3d.Zero, (sum, load) => sum + load.Force)))
                                   .Where(node => node.Force.Length > 0.0)
                                   .ToList();
                if (nodes.Count == 0) return;

                double largest = nodes.Max(x => x.Force.Length);
                var tails = new List<(string Value, Point3d At, Vector3d Arrow)>();
                foreach (var node in nodes)
                {
                    var arrow = node.Force / node.Force.Length * Length(node.Force.Length, largest, reach);
                    Arrow(new Line(node.At - arrow, node.At), colour);
                    tails.Add((LoadValue(node.Force.Length, $"{Units.Force}"), node.At - arrow, arrow));
                }

                // Each value once, as a line load's is once a run: a slab's nodes carry three or four
                // values between them, and written on every node they hid each other and the slab.
                // On the node nearest the middle of those that carry it, out of the way of the values
                // already written - the beams' along the same edge, say.
                foreach (var same in tails.GroupBy(x => x.Value))
                {
                    var centre = Centre(same.Select(x => x.At).ToList());
                    var order = same.OrderBy(x => x.At.DistanceToSquared(centre)).ToList();
                    var tail = order.FirstOrDefault(x => _values.All(v => v.At.DistanceTo(x.At) > 2.0 * spacing));
                    if (tail.Value == null) tail = order[0];
                    Value(same.Key, colour, tail.At, -tail.Arrow);
                }
            }

            /// <summary>A dot on each mass, its value under it.</summary>
            private void Masses(List<Alpaca4d.Loads.MassLoad> masses)
            {
                if (masses == null) return;
                var colour = AlpacaSettings.Colour(DisplayItem.Masses);
                foreach (var mass in masses)
                {
                    var m = mass.TransMass;
                    if (m.Length == 0.0 && mass.RotationMass.Length == 0.0) continue;

                    var at = mass.Pos + Moved(mass.Pos);
                    _masses.Add(Tuple.Create(at, colour));
                    Extent.Union(at);

                    string value = m.X == m.Y && m.Y == m.Z
                        ? LoadValue(m.X, "kg")
                        : string.Join(", ", new[] { m.X, m.Y, m.Z }.Select(x => LoadValue(x, "").Trim())) + " kg";
                    _values.Add((value, colour, at, Vector3d.Zero, -0.8));
                }
            }

            /// <summary>How far the node at <paramref name="point"/> moved on the shape drawn; nothing as the model was built.</summary>
            private Vector3d Moved(Point3d point)
            {
                if (_displacement == null) return Vector3d.Zero;
                return Node(_model.NodeTagNear(point, out _));
            }

            /// <summary>
            /// How far the point <paramref name="t"/> of the way along a beam moved: the beam is drawn
            /// straight between its two ends, wherever they went.
            /// </summary>
            private Vector3d Moved(Alpaca4d.Generic.IBeam beam, double t)
            {
                if (_displacement == null) return Vector3d.Zero;
                return (1.0 - t) * Node(beam.INode) + t * Node(beam.JNode);
            }

            /// <summary>How far the <paramref name="k"/>th corner of a shell moved, by its node.</summary>
            private Vector3d Moved(List<int?> nodes, int k)
            {
                if (_displacement == null || nodes == null || k >= nodes.Count) return Vector3d.Zero;
                return Node(nodes[k]);
            }

            private Vector3d Node(int? tag) =>
                tag.HasValue && _displacement.TryGetValue(tag.Value, out var offset) ? offset : Vector3d.Zero;

            /// <summary>How long an arrow for <paramref name="value"/> is, when the largest of its kind is <paramref name="largest"/>.</summary>
            private static double Length(double value, double largest, double reach)
            {
                return reach * Math.Max(Shortest, value / largest);
            }

            private void Arrow(Line line, Color colour)
            {
                _arrows.Add(Tuple.Create(line, colour));
                Extent.Union(line.BoundingBox);
            }

            private void Tail(Line line, Color colour)
            {
                _tails.Add(Tuple.Create(line, colour));
                Extent.Union(line.BoundingBox);
            }

            /// <summary>A value written at <paramref name="at"/>, or beyond it <paramref name="away"/> from what it belongs to.</summary>
            private void Value(string text, Color colour, Point3d at, Vector3d away)
            {
                _values.Add((text, colour, at, away, 0.0));
                Extent.Union(at);
            }

            public void Draw(IGH_PreviewArgs args, bool arrows, bool values, double textSize)
            {
                if (arrows)
                {
                    if (_sheets != null)
                        args.Display.DrawMeshWires(_sheets, AlpacaSettings.Colour(DisplayItem.AreaLoads), 1);

                    foreach (var arrow in _arrows)
                    {
                        args.Display.DrawLine(arrow.Item1, arrow.Item2, ArrowThickness);
                        args.Display.DrawArrowHead(arrow.Item1.To, arrow.Item1.Direction, arrow.Item2, 24, 0);
                    }

                    foreach (var tail in _tails)
                        args.Display.DrawLine(tail.Item1, tail.Item2, 2);

                    foreach (var moment in _moments)
                    {
                        args.Display.DrawArc(moment.Arc, moment.Colour, ArrowThickness);
                        args.Display.DrawArrowHead(moment.Tip, moment.Tangent, moment.Colour, 20, 0);
                    }

                    // In front of everything, as the labels are: a mass sits on a node, inside the
                    // beams and shells that meet there.
                    foreach (var mass in _masses)
                        args.Display.DrawPoint(ViewportLabels.InFront(args.Viewport, mass.Item1, out _), Rhino.Display.PointStyle.RoundSimple, 7, mass.Item2);
                }

                if (values)
                    foreach (var value in _values)
                        ViewportLabels.Draw(args.Display, args.Viewport, value.Text, value.Colour, value.At, textSize, value.Lines, value.Away);
            }

            /// <summary>
            /// The area loads' see-through sheets, from DrawViewportMeshes after the elements:
            /// Grasshopper draws a component's meshes after its wires, so drawn with the arrows a
            /// sheet hid the shells behind it instead of tinting them.
            /// </summary>
            public void DrawSheets(IGH_PreviewArgs args)
            {
                if (_sheets == null) return;
                var colour = AlpacaSettings.Colour(DisplayItem.AreaLoads);
                args.Display.DrawMeshShaded(_sheets, new Rhino.Display.DisplayMaterial(colour, colour, colour, colour, 0.0, 0.8));
            }

            /// <summary>
            /// The pieces of a load in groups: those with the same force that touch, at a point of
            /// <paramref name="points"/> within <paramref name="tolerance"/> of each other.
            /// </summary>
            private static List<List<int>> Touching(List<Vector3d> forces, List<Point3d[]> points, double tolerance)
            {
                var parent = Enumerable.Range(0, forces.Count).ToArray();
                int Root(int i)
                {
                    while (parent[i] != i) i = parent[i] = parent[parent[i]];
                    return i;
                }

                var seen = new Dictionary<(Vector3d, (long, long, long)), int>();
                for (int i = 0; i < forces.Count; i++)
                    foreach (var point in points[i])
                    {
                        var key = (forces[i], Key(point, tolerance));
                        if (seen.TryGetValue(key, out int other))
                            parent[Root(i)] = Root(other);
                        else
                            seen[key] = i;
                    }

                return Enumerable.Range(0, forces.Count).GroupBy(Root).Select(group => group.ToList()).ToList();
            }

            private static (long, long, long) Key(Point3d point, double tolerance)
            {
                return ((long)Math.Round(point.X / tolerance), (long)Math.Round(point.Y / tolerance), (long)Math.Round(point.Z / tolerance));
            }

            private static Point3d Centre(IReadOnlyCollection<Point3d> points)
            {
                var sum = Point3d.Origin;
                foreach (var point in points) sum += point;
                return sum / points.Count;
            }

            private static Point3d Nearest(List<Point3d> points, Point3d to)
            {
                return points.OrderBy(point => point.DistanceToSquared(to)).First();
            }

            /// <summary>Points kept at least a gap apart: each one closer than that to one already kept is dropped.</summary>
            private sealed class Spread
            {
                private readonly double _gap;
                private readonly Dictionary<(long, long, long), List<Point3d>> _cells = new Dictionary<(long, long, long), List<Point3d>>();
                public readonly List<Point3d> Kept = new List<Point3d>();

                public Spread(double gap)
                {
                    _gap = gap;
                }

                public void Add(Point3d point)
                {
                    var cell = Key(point, _gap);
                    for (long x = cell.Item1 - 1; x <= cell.Item1 + 1; x++)
                        for (long y = cell.Item2 - 1; y <= cell.Item2 + 1; y++)
                            for (long z = cell.Item3 - 1; z <= cell.Item3 + 1; z++)
                                if (_cells.TryGetValue((x, y, z), out var near) && near.Any(other => other.DistanceTo(point) < _gap))
                                    return;

                    if (!_cells.TryGetValue(cell, out var kept)) _cells[cell] = kept = new List<Point3d>();
                    kept.Add(point);
                    Kept.Add(point);
                }
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
        }

        /// <summary>The supports of a model at one Support scale: each one's symbol, white with black edges, or its label.</summary>
        private sealed class SupportPicture
        {
            public readonly double Scale;
            public BoundingBox Extent = BoundingBox.Empty;

            private readonly Mesh _symbols;
            private readonly List<Tuple<Point3d, string>> _labels = new List<Tuple<Point3d, string>>();

            private static readonly Rhino.Display.DisplayMaterial White = new Rhino.Display.DisplayMaterial(
                Color.White, Color.White, Color.White, Color.White, 1.0, 0.0);

            public SupportPicture(Alpaca4d.Model model, double scale)
            {
                Scale = scale;

                var symbols = new List<Mesh>();
                foreach (var support in model.Supports)
                {
                    Extent.Union(support.Pos);

                    if (support.Geometry is Mesh mesh)
                    {
                        var symbol = mesh.DuplicateMesh();
                        symbol.Transform(Transform.Scale(Point3d.Origin, scale));
                        symbol.Transform(Transform.Translation(new Vector3d(support.Pos)));
                        symbols.Add(symbol);
                    }
                    else if (support.Geometry is string label)
                    {
                        _labels.Add(Tuple.Create(support.Pos, label));
                    }
                }

                _symbols = Joined.Of(symbols);
                if (_symbols != null) Extent.Union(_symbols.GetBoundingBox(false));
            }

            public void Draw(IGH_PreviewArgs args)
            {
                if (_symbols != null)
                {
                    args.Display.DrawMeshShaded(_symbols, White);
                    args.Display.DrawMeshWires(_symbols, Color.Black, 2);
                }

                foreach (var label in _labels)
                    args.Display.DrawDot(label.Item1, label.Item2);
            }
        }

        #endregion
    }
}
