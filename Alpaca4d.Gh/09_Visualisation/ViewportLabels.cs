using System;
using System.Collections.Generic;
using System.Drawing;

using Rhino.Display;
using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Text written on a model - ids, section names, load and result values - on a white plate, so
    /// that it reads on Rhino's grey background, on a light blue shell and on a result's colours
    /// alike. Written straight on the model, it took whatever it fell on: white ids vanished on the
    /// grey, and any colour but black on a shell.
    ///
    /// The height is in model units, the Text size slider: a label grows as the camera closes in,
    /// and its capitals are that tall. It is drawn over everything, so that an id on the axis of a
    /// beam or inside a solid is not hidden by the element it names: plate and text in 3D, squared
    /// to the camera, slid along the line of sight to just inside the near clipping plane. Depth
    /// testing off alone was not enough, nor a text dot or 2D text: Grasshopper draws the shaded
    /// meshes after the wires, and the shells were drawn over every label they were in front of.
    /// </summary>
    internal static class ViewportLabels
    {
        private const string Font = "Arial";

        /// <summary>See-through enough that a page of labels does not blot out the model under it.</summary>
        private static readonly Color Plate = Color.FromArgb(195, 255, 255, 255);

        /// <summary>A label whose capitals are smaller than this many pixels cannot be read: it is left out, which also spares a zoomed-out model thousands of them.</summary>
        private const double SmallestPixels = 5.0;

        /// <summary>The space round the text on its plate, as a share of the height of a capital.</summary>
        private const double Margin = 0.25;

        /// <summary>The size each text is measured at, once: measuring every label on every frame cost as much as drawing it.</summary>
        private const int MeasuredAt = 100;

        /// <summary>How wide each text is, in heights of a capital.</summary>
        private static readonly Dictionary<string, double> Widths = new Dictionary<string, double>();

        /// <summary>
        /// Draws <paramref name="text"/> centred on <paramref name="at"/>, its capitals
        /// <paramref name="height"/> tall in model units, moved up by <paramref name="lines"/>
        /// lines - down when negative - so that two labels on one point can sit one above the other.
        /// Given <paramref name="away"/>, the label is moved that way until its plate only touches
        /// <paramref name="at"/>: a load's value beyond the tail of its arrow rather than over it.
        /// </summary>
        public static void Draw(DisplayPipeline display, RhinoViewport viewport, string text, Color colour, Point3d at, double height,
                                double lines = 0.0, Vector3d away = default)
        {
            if (string.IsNullOrEmpty(text) || !(height > 0.0)) return;

            // Behind the camera, a point still lands somewhere on the screen.
            if (viewport.IsPerspectiveProjection && (at - viewport.CameraLocation) * viewport.CameraDirection <= 0.0) return;

            var across = viewport.CameraX;
            var up = viewport.CameraY;
            across.Unitize();
            up.Unitize();
            at += up * (lines * (1.0 + 2.5 * Margin) * height);

            if (!viewport.GetWorldToScreenScale(at, out double pixelsPerUnit) || height * pixelsPerUnit < SmallestPixels) return;

            double halfWidth = 0.5 * Width(display, text) * height + Margin * height;
            double halfHeight = (0.5 + Margin) * height;

            if (away.Length > 0.0)
            {
                // As far as the plate reaches that way on the screen, measured in the screen's plane.
                var flat = across * (away * across) + up * (away * up);
                if (flat.Length > 1e-9)
                {
                    flat.Unitize();
                    double toEdge = Math.Min(halfWidth / Math.Max(Math.Abs(flat * across), 1e-9), halfHeight / Math.Max(Math.Abs(flat * up), 1e-9));
                    at += flat * (toEdge + 0.15 * height);
                }
            }

            var screen = viewport.WorldToClient(at);
            var bounds = viewport.Bounds;
            double reach = halfWidth * pixelsPerUnit;
            if (screen.X + reach < 0 || screen.X - reach > bounds.Width || screen.Y + reach < 0 || screen.Y - reach > bounds.Height) return;

            at = InFront(viewport, at, out double shrink);
            height *= shrink;
            halfWidth *= shrink;
            halfHeight *= shrink;

            display.PushDepthTesting(false);
            display.DrawPolygon(new[]
            {
                at - across * halfWidth - up * halfHeight,
                at + across * halfWidth - up * halfHeight,
                at + across * halfWidth + up * halfHeight,
                at - across * halfWidth + up * halfHeight,
            }, Plate, true);
            display.Draw3dText(text, colour, new Plane(at, across, up), height, Font, false, false,
                               Rhino.DocObjects.TextHorizontalAlignment.Center, Rhino.DocObjects.TextVerticalAlignment.Middle);
            display.PopDepthTesting();
        }

        /// <summary>
        /// <paramref name="at"/> slid along its line of sight to just inside the near clipping
        /// plane, and by how much what is drawn there must shrink to look the same size: nothing
        /// can then be drawn in front of it, however late it is drawn.
        /// </summary>
        public static Point3d InFront(RhinoViewport viewport, Point3d at, out double shrink)
        {
            shrink = 1.0;
            if (!viewport.GetFrustum(out _, out _, out _, out _, out double near, out double far)) return at;

            var eye = viewport.CameraLocation;
            var look = viewport.CameraDirection;
            look.Unitize();
            double depth = (at - eye) * look;
            double front = near + 0.001 * (far - near);
            if (!(depth > front)) return at;

            if (!viewport.IsPerspectiveProjection) return at - look * (depth - front);

            shrink = front / depth;
            return eye + (at - eye) * shrink;
        }

        private static double Width(DisplayPipeline display, string text)
        {
            if (Widths.TryGetValue(text, out double width)) return width;

            // Result values change with every solution; their widths need not pile up for good.
            if (Widths.Count > 20000) Widths.Clear();

            double capital = Math.Abs(display.Measure2dText("H", Point2d.Origin, true, 0.0, MeasuredAt, Font).Height);
            double wide = Math.Abs(display.Measure2dText(text, Point2d.Origin, true, 0.0, MeasuredAt, Font).Width);
            width = capital > 0.0 ? wide / capital : text.Length;
            Widths[text] = width;
            return width;
        }
    }
}
