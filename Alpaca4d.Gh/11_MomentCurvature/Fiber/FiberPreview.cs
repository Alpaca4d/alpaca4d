using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// What the fibre components draw, worked out once per solve and kept.
    ///
    /// The reason for the cache is that <c>Patch.Fibers</c> and <c>Layer.Fibers</c> are
    /// computed properties: reading Patch.Fibers explodes the mesh and runs
    /// AreaMassProperties.Compute over every face, every time. The previews used to read
    /// them on each viewport redraw - twice over, once in the child component and once in
    /// Fiber Section - so orbiting a dense patch recomputed every centroid several times
    /// a frame. <see cref="ClippingBox"/> would have made that worse again, being queried
    /// on its own schedule.
    ///
    /// It is also the one place the four components agree on how a fibre looks, which is
    /// what makes the overlap between a child's preview and Fiber Section's invisible.
    /// </summary>
    internal sealed class FiberPreview
    {
        private static readonly Color PointFiberColor = Color.Blue;
        private static readonly Color LayerColor = Color.Red;
        private static readonly Color LayerLineColor = Color.Black;
        private static readonly Color PatchWireColor = Color.Gray;
        private static readonly Color PatchFiberColor = Color.Black;

        /// <summary>
        /// How long the local axes are drawn, in pixels - so the same on screen whatever
        /// the zoom, and whatever the model's units are. What this replaced was half the
        /// bounding-box diagonal, which is metres in one model and millimetres in the
        /// next, and in both cases longer than the section it was labelling.
        /// </summary>
        private const int AxisLengthInPixels = 45;

        /// <summary>
        /// Label height in pixels. Draw2dText measures in pixels, so this is absolute:
        /// the 36 it used to be is heading size, and swamped the section it sat on.
        /// </summary>
        private const int LabelHeightInPixels = 12;

        /// <summary>Axes are never longer than this share of the section they belong to.</summary>
        private const double AxisShareOfSection = 0.4;

        private readonly List<Point3d> _pointFibers = new List<Point3d>();
        private readonly List<Point3d[]> _layers = new List<Point3d[]>();
        private readonly List<Mesh> _patchMeshes = new List<Mesh>();
        private readonly List<Point3d> _patchFibers = new List<Point3d>();

        /// <summary>Everything drawn, for the component's ClippingBox.</summary>
        public BoundingBox Box { get; private set; } = BoundingBox.Empty;

        /// <summary>Every fibre's area, and its area times its position: for the centroid.</summary>
        private double _area;
        private Vector3d _firstMoment;

        /// <summary>
        /// The area centroid of every fibre, steel included: the point OpenSees' fibre section takes
        /// its moments about, and so Interaction Domain's and Moment Curvature's. The middle of the
        /// box for fibres with no area.
        /// </summary>
        public Point3d Centroid => _area > 0.0 ? Point3d.Origin + _firstMoment / _area : Box.Center;

        public bool IsEmpty
        {
            get { return _pointFibers.Count == 0 && _layers.Count == 0 && _patchMeshes.Count == 0; }
        }

        public void Clear()
        {
            _pointFibers.Clear();
            _layers.Clear();
            _patchMeshes.Clear();
            _patchFibers.Clear();
            Box = BoundingBox.Empty;
            _area = 0.0;
            _firstMoment = Vector3d.Zero;
        }

        public void AddPointFibers(IEnumerable<Alpaca4d.Section.PointFiber> fibers)
        {
            if (fibers == null)
                return;

            foreach (var fiber in fibers.Where(f => f != null))
            {
                _pointFibers.Add(fiber.Pos);
                Grow(fiber.Pos);
                Weigh(fiber);
            }
        }

        public void AddLayers(IEnumerable<Alpaca4d.Section.Layer> layers)
        {
            if (layers == null)
                return;

            foreach (var layer in layers.Where(l => l != null && l.Curve != null))
            {
                var fibers = layer.Fibers;
                var positions = fibers.Select(f => f.Pos).ToArray();

                if (positions.Length == 0)
                    continue;

                _layers.Add(positions);
                foreach (var fiber in fibers)
                    Weigh(fiber);

                foreach (var position in positions)
                    Grow(position);
            }
        }

        public void AddPatches(IEnumerable<Alpaca4d.Section.Patch> patches)
        {
            if (patches == null)
                return;

            foreach (var patch in patches.Where(p => p != null && p.PatchGeometry != null))
            {
                _patchMeshes.Add(patch.PatchGeometry);
                Box = BoundingBox.Union(Box, patch.PatchGeometry.GetBoundingBox(false));

                foreach (var fiber in patch.Fibers)
                {
                    _patchFibers.Add(fiber.Pos);
                    Grow(fiber.Pos);
                    Weigh(fiber);
                }
            }
        }

        public void Draw(IGH_PreviewArgs args)
        {
            foreach (var mesh in _patchMeshes)
                args.Display.DrawMeshWires(mesh, PatchWireColor);

            foreach (var position in _patchFibers)
                args.Display.DrawPoint(position, PointStyle.RoundSimple, 1, PatchFiberColor);

            foreach (var layer in _layers)
            {
                foreach (var position in layer)
                    args.Display.DrawPoint(position, PointStyle.Pin, 3, LayerColor);

                // One polyline per layer. Flattening every layer into a single point list -
                // which is what SelectMany did - drew a line from the last bar of one layer
                // to the first bar of the next, straight across the section.
                if (layer.Length > 1)
                    args.Display.DrawDottedPolyline(layer, LayerLineColor, false);
            }

            foreach (var position in _pointFibers)
                args.Display.DrawPoint(position, PointStyle.Pin, 2, PointFiberColor);
        }

        /// <summary>
        /// The section's local axes, at its area centroid, with the moments about them.
        ///
        /// Which way they point follows the fibre coordinates, and those are OpenSees':
        /// "fiber $y $z" is written with the point's Rhino X first and its Rhino Y second,
        /// so local y runs along world X and local z along world Y. The arrows used to say
        /// the opposite, which left the preview and the Direction input disagreeing.
        ///
        /// They stand where the moments are taken about, the area centroid of every fibre, which
        /// is not the middle of the section once its bars or its shape are unsymmetric; they were
        /// drawn at the middle of the bounding box. My and Mz are moments about y and z by the
        /// right-hand rule, the beam's axis x = y x z coming out of the drawing: each axis gets the
        /// double arrowhead of a moment vector, and x the dot of an arrow pointing at the reader.
        /// Positive My stretches the fibres on the +z side, positive Mz those on the -y side.
        /// </summary>
        public void DrawLocalAxes(IGH_PreviewArgs args)
        {
            if (!Box.IsValid)
                return;

            var center = Centroid;
            var length = AxisLength(args, center);

            if (length <= 0.0)
                return;

            var y = new Vector3d(length, 0, 0);
            var z = new Vector3d(0, length, 0);
            var head = length * 0.12;

            args.Display.DrawLineArrow(new Line(center, y), Color.Green, 2, head);
            args.Display.DrawLineArrow(new Line(center, y * 0.84), Color.Green, 2, head);
            args.Display.DrawLineArrow(new Line(center, z), Color.Blue, 2, head);
            args.Display.DrawLineArrow(new Line(center, z * 0.84), Color.Blue, 2, head);

            args.Display.Draw2dText("y, My", Color.Green, center + y * 1.3, true, LabelHeightInPixels);
            args.Display.Draw2dText("z, Mz", Color.Blue, center + z * 1.22, true, LabelHeightInPixels);

            var radius = length * 0.09;
            args.Display.DrawCircle(new Circle(new Plane(center, Vector3d.ZAxis), radius), Color.Red, 2);
            args.Display.DrawPoint(center, PointStyle.RoundSimple, 2, Color.Red);
            args.Display.Draw2dText("x", Color.Red, center - (y + z) * 0.22, true, LabelHeightInPixels);
        }

        /// <summary>The clipping box of the geometry plus the room the axes need.</summary>
        public BoundingBox ClippingBoxWithAxes()
        {
            if (!Box.IsValid)
                return BoundingBox.Empty;

            var box = Box;
            box.Inflate(Math.Max(Box.Diagonal.Length * AxisShareOfSection, 1e-6));
            return box;
        }

        private double AxisLength(IGH_PreviewArgs args, Point3d center)
        {
            var ceiling = Box.Diagonal.Length * AxisShareOfSection;

            double pixelsPerUnit;
            var haveScale = args.Viewport != null
                            && args.Viewport.GetWorldToScreenScale(center, out pixelsPerUnit)
                            && pixelsPerUnit > 0.0;

            if (!haveScale)
                return ceiling;

            args.Viewport.GetWorldToScreenScale(center, out pixelsPerUnit);
            var onScreen = AxisLengthInPixels / pixelsPerUnit;

            // A section too small to have a diagonal - one fibre - still gets axes.
            return ceiling <= 0.0 ? onScreen : Math.Min(onScreen, ceiling);
        }

        private void Weigh(Alpaca4d.Section.PointFiber fiber)
        {
            if (!(fiber.Area > 0.0))
                return;
            _area += fiber.Area;
            _firstMoment += new Vector3d(fiber.Pos) * fiber.Area;
        }

        private void Grow(Point3d point)
        {
            var box = Box;
            box.Union(point);
            Box = box;
        }
    }
}
