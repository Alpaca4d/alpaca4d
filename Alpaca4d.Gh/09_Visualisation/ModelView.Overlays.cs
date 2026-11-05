using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Geometry;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    // What Model View draws over the elements, whatever they are coloured by: node and element ids,
    // section names, local axes, end releases, loads and supports.
    //
    // Worked out the first time they are needed after a solution and kept until the next, rather
    // than on every redraw. Model View used to work them out in DrawViewportWires - an area centroid
    // for every shell's id, an offset mesh for every area load, a frame for every element's axes -
    // so on a plate of 3000 shells each frame took 44 to 165 ms with one of them ticked, and the
    // viewport crawled when it was turned.
    public partial class ModelView
    {
        private int? _loadPatternId;

        /// <summary>The radius of a release circle at Release size 1; see ReleaseSymbols.Size.</summary>
        private double _releaseSize;

        private List<Tuple<string, Point3d>> _nodeLabels;
        private List<Tuple<string, Point3d>> _elementLabels;
        private List<Tuple<string, Point3d>> _sectionLabels;
        private List<(Point3d Origin, Vector3d X, Vector3d Y, Vector3d Z, double Reach)> _axes;

        // The loads at the Load scale they were drawn for, and the supports at theirs.
        private LoadPicture _loads;
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

            if (_ckElementIds?.Active ?? false)
                DrawLabels(args, ElementLabels(), AlpacaSettings.Colour(DisplayItem.ElementIds), textSize);

            if (_ckSectionNames?.Active ?? false)
                DrawLabels(args, SectionLabels(), AlpacaSettings.Colour(DisplayItem.SectionNames), textSize);

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
        /// Shells: fxx, mxx and sigma11 act along the red arrow (axis 1), fyy, myy and sigma22 along
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

        private static void DrawLabels(IGH_PreviewArgs args, IEnumerable<Tuple<string, Point3d>> labels, Color colour, double height)
        {
            DrawLabels(args, labels.Select(label => Tuple.Create(label.Item1, colour, label.Item2)), height);
        }

        /// <summary>
        /// Labels squared to the camera, <paramref name="height"/> tall in model units - the Text
        /// size slider.
        ///
        /// Drawn over the model rather than into it. An id sits on the axis of a beam or inside a
        /// solid, and with Extruded on, depth testing would hide it in the very element it names.
        /// Turned off once for all of them, not once a label.
        /// </summary>
        private static void DrawLabels(IGH_PreviewArgs args, IEnumerable<Tuple<string, Color, Point3d>> labels, double height)
        {
            args.Display.PushDepthTesting(false);
            foreach (var label in labels)
            {
                if (string.IsNullOrEmpty(label.Item1)) continue;

                var plane = new Plane(label.Item3, args.Viewport.CameraX, args.Viewport.CameraY);
                args.Display.Draw3dText(label.Item1, label.Item2, plane, height, "Arial", false, false,
                                        Rhino.DocObjects.TextHorizontalAlignment.Center, Rhino.DocObjects.TextVerticalAlignment.Middle);
            }
            args.Display.PopDepthTesting();
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
                _loads = new LoadPicture(_model, _loadPatternId, scale);
            return _loads;
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
        /// The loads of a model at one Load scale: arrows ending on the node or beam they push, a
        /// see-through sheet standing off each loaded shell, and what each is worth, written at the
        /// tail of its arrow.
        /// </summary>
        private sealed class LoadPicture
        {
            public readonly double Scale;
            public BoundingBox Extent = BoundingBox.Empty;

            private readonly List<Tuple<Line, Color>> _arrows = new List<Tuple<Line, Color>>();
            private readonly List<Tuple<string, Color, Point3d>> _values = new List<Tuple<string, Color, Point3d>>();
            private readonly Mesh _sheets;

            /// <summary>How wide a load arrow's shaft is drawn, in pixels. DrawArrow draws it at one, which was hard to pick out.</summary>
            private const int ArrowThickness = 3;

            public LoadPicture(Alpaca4d.Model model, int? pattern, double scale)
            {
                Scale = scale;

                var patterns = pattern.HasValue
                    ? model.LoadPatterns.Where(x => x.Id == pattern.Value)
                    : (IEnumerable<Alpaca4d.Loads.LoadPattern>)model.LoadPatterns;
                var loads = patterns.SelectMany(x => x.Load).ToList();

                foreach (var load in loads.OfType<Alpaca4d.Loads.PointLoad>())
                {
                    var arrow = load.Force * scale;
                    if (arrow.Length > 0.0)
                        Arrow(new Line(load.Pos - arrow, load.Pos), AlpacaSettings.Colour(DisplayItem.PointLoads));

                    string value = PointLoadValue(load);
                    if (value.Length > 0)
                        Value(value, AlpacaSettings.Colour(DisplayItem.PointLoads), load.Pos - arrow);
                }

                var sheets = new List<Mesh>();
                foreach (var load in loads.OfType<Alpaca4d.Loads.MeshLoad>())
                {
                    var force = load.GlobalForce.Length;
                    if (!(force > 0.0)) continue;

                    var direction = load.GlobalForce / force;
                    foreach (var shell in load.AppliedTo(model.Shells))
                    {
                        var sheet = shell.Mesh.Offset(force * scale, true, direction);
                        if (sheet != null)
                        {
                            sheet.Faces.DeleteFaces(new List<int>(1));
                            sheets.Add(sheet);
                        }

                        Value(LoadValue(force, $"{Units.Force}/{Units.Length}²"), AlpacaSettings.Colour(DisplayItem.AreaLoads),
                              shell.Mesh.GetBoundingBox(false).Center + load.GlobalForce * scale);
                    }
                }
                _sheets = Joined.Of(sheets);
                if (_sheets != null) Extent.Union(_sheets.GetBoundingBox(false));

                foreach (var load in loads.OfType<Alpaca4d.Loads.LineLoad>())
                {
                    var arrow = load.GlobalForce * scale;
                    if (!(arrow.Length > 0.0)) continue;

                    foreach (var beam in load.AppliedTo(model.Beams))
                    {
                        const int divisions = 6;
                        for (int i = 0; i <= divisions; i++)
                        {
                            var point = beam.Curve.PointAtNormalizedLength(i / (double)divisions);
                            Arrow(new Line(point - arrow, point), AlpacaSettings.Colour(DisplayItem.LineLoads));
                        }

                        // Once a beam, over the middle arrow, rather than over all seven.
                        Value(LoadValue(load.GlobalForce.Length, $"{Units.Force}/{Units.Length}"), AlpacaSettings.Colour(DisplayItem.LineLoads),
                              beam.Curve.PointAtNormalizedLength(0.5) - arrow);
                    }
                }
            }

            private void Arrow(Line line, Color colour)
            {
                _arrows.Add(Tuple.Create(line, colour));
                Extent.Union(line.BoundingBox);
            }

            private void Value(string text, Color colour, Point3d at)
            {
                _values.Add(Tuple.Create(text, colour, at));
                Extent.Union(at);
            }

            public void Draw(IGH_PreviewArgs args, bool arrows, bool values, double textSize)
            {
                if (arrows)
                {
                    foreach (var arrow in _arrows)
                    {
                        args.Display.DrawLine(arrow.Item1, arrow.Item2, ArrowThickness);
                        args.Display.DrawArrowHead(arrow.Item1.To, arrow.Item1.Direction, arrow.Item2, 24, 0);
                    }

                    if (_sheets != null)
                    {
                        var colour = AlpacaSettings.Colour(DisplayItem.AreaLoads);
                        args.Display.DrawMeshShaded(_sheets, new Rhino.Display.DisplayMaterial(colour, colour, colour, colour, 0.0, 0.8));
                        args.Display.DrawMeshWires(_sheets, colour, 2);
                    }
                }

                if (values)
                    DrawLabels(args, _values, textSize);
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
