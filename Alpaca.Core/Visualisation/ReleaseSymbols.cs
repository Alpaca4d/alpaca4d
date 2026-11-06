using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Rhino.Geometry;
using Alpaca4d.Element;
using Alpaca4d.Generic;

namespace Alpaca4d.Visualisation
{
    /// <summary>
    /// What Model View draws at a beam end that is released, coloured like the local axes - red for
    /// x, green for y, blue for z:
    ///
    /// <list type="bullet">
    /// <item>a rotation that is free is a circle in the plane the end turns in: a released Rz is a blue
    /// circle in the beam's x-y plane, the hinge an elevation shows; Ry a green one in x-z; Rx, the
    /// twist, a red ring round the bar;</item>
    /// <item>a translation that is free is "=": two short lines along the direction the end may slide,
    /// side by side - across the bar for Ty and Tz, along it for Tx, a sleeve. They are turned to face
    /// the camera, so the pair never closes into one line.</item>
    /// </list>
    ///
    /// The circles sit just inside the end, the lines a little further in. A truss carries no moment
    /// about any axis, so both its ends get all three circles: a ball joint.
    /// </summary>
    public static class ReleaseSymbols
    {
        public static readonly Color X = Color.Red;
        public static readonly Color Y = Color.Green;
        public static readonly Color Z = Color.Blue;

        /// <summary>
        /// A size for every symbol of a model: the circles' radius, a twentieth of its typical beam,
        /// so one long member does not swell the marks on all the others.
        /// </summary>
        public static double Size(Model model)
        {
            var lengths = model.Beams.Select(beam => beam.Curve.PointAtStart.DistanceTo(beam.Curve.PointAtEnd))
                                     .Where(length => length > 0.0).OrderBy(length => length).ToList();
            return lengths.Count == 0 ? 0.0 : lengths[lengths.Count / 2] * 0.05;
        }

        /// <summary>The releases at a beam's two ends, held everywhere when it has none.</summary>
        public static (Release start, Release end) ReleasesOf(IBeam beam)
        {
            if (beam is BeamWithHinges hinged)
                return (hinged.ReleaseI, hinged.ReleaseJ);
            if (beam is Truss)
            {
                var ballJoint = new Release(true, true, true, false, false, false);
                return (ballJoint, ballJoint);
            }
            return (Release.FullFixed, Release.FullFixed);
        }

        /// <summary>
        /// The circles and lines for one beam, <paramref name="size"/> the circles' radius and
        /// <paramref name="view"/> the camera direction the "=" turns to face.
        /// </summary>
        public static (List<(Circle circle, Color colour)> circles, List<(Line line, Color colour)> lines) For(IBeam beam, double size, Vector3d view)
        {
            var circles = new List<(Circle, Color)>();
            var lines = new List<(Line, Color)>();
            if (!(size > 0.0) || beam.Curve == null || beam.GeomTransf == null)
                return (circles, lines);

            var start = beam.Curve.PointAtStart;
            var end = beam.Curve.PointAtEnd;
            var x = end - start;
            double length = x.Length;
            if (!x.Unitize())
                return (circles, lines);
            var y = beam.GeomTransf.LocalY;
            var z = beam.GeomTransf.LocalZ;
            y.Unitize();
            z.Unitize();

            // Circles a radius and a half in, lines two radii past them - pulled in proportion on a
            // beam too short for that, so the marks of its two ends never meet in the middle.
            double rotationAt = 1.6 * size, translationAt = 4.0 * size;
            double fit = Math.Min(1.0, 0.45 * length / translationAt);
            rotationAt *= fit;
            translationAt *= fit;
            double radius = size * fit;

            var (releaseI, releaseJ) = ReleasesOf(beam);
            End(start, x, releaseI);
            End(end, -x, releaseJ);
            return (circles, lines);

            void End(Point3d node, Vector3d inward, Release release)
            {
                var centre = node + inward * rotationAt;
                if (!release.Rx) circles.Add((new Circle(new Plane(centre, y, z), radius), X));
                if (!release.Ry) circles.Add((new Circle(new Plane(centre, x, z), radius), Y));
                if (!release.Rz) circles.Add((new Circle(new Plane(centre, x, y), radius), Z));

                var slide = node + inward * translationAt;
                if (!release.Tx) Pair(slide, x, X);
                if (!release.Ty) Pair(slide, y, Y);
                if (!release.Tz) Pair(slide, z, Z);
            }

            // Two lines along the free direction, a gap apart across it and across the line of sight.
            void Pair(Point3d centre, Vector3d along, Color colour)
            {
                var across = Vector3d.CrossProduct(along, view);
                if (across.Length < 0.2 * Math.Max(view.Length, 1e-12))
                    across = Math.Abs(along * y) < 0.5 ? y : z;   // looking straight down the slide
                across.Unitize();

                var half = along * radius;
                var gap = across * (0.35 * radius);
                lines.Add((new Line(centre + gap - half, centre + gap + half), colour));
                lines.Add((new Line(centre - gap - half, centre - gap + half), colour));
            }
        }
    }
}
