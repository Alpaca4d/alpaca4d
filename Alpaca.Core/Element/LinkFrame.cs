using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Rhino.Geometry;
using Alpaca4d.Generic;

namespace Alpaca4d.Element
{
    /// <summary>
    /// The bits of bookkeeping the two spring elements share: how a local frame is built when none
    /// was given, how it is written out, and what makes a set of materials and directions valid.
    ///
    /// It works in vectors rather than planes throughout, and never builds a Plane of its own. A
    /// Plane is only a carrier here - all that is wanted of it are its two axes - and building one,
    /// or asking one whether it is valid, goes through RhinoCommon's native side. Reading the axes
    /// off one does not, so this way the tcl a spring writes can be checked without Rhino.
    /// </summary>
    internal static class LinkFrame
    {
        /// <summary>
        /// Whether a plane carries a frame at all. Plane.Unset arrives when the input was left
        /// empty, and its axes are zero.
        /// </summary>
        public static bool HasFrame(Plane plane)
        {
            return Usable(plane.XAxis) && Usable(plane.YAxis);
        }

        private static bool Usable(Vector3d vector)
        {
            var squared = vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z;

            return squared > 0.0 && !double.IsNaN(squared) && !double.IsInfinity(squared);
        }

        /// <summary>
        /// A frame with its x axis along <paramref name="axis"/>. Which way y points is arbitrary
        /// and only matters to a spring that treats its two cross directions differently, so it is
        /// taken from whichever global axis is furthest from being parallel to the link.
        /// </summary>
        public static void Along(Vector3d axis, out Vector3d x, out Vector3d y)
        {
            x = Unit(axis);

            var reference = Math.Abs(x * Vector3d.ZAxis) > 0.9 ? Vector3d.XAxis : Vector3d.ZAxis;
            y = Unit(Vector3d.CrossProduct(reference, x));
        }

        /// <summary>The axes a spring actually uses, given what was asked for and where it points.</summary>
        public static void Axes(Plane orient, Vector3d fallbackAxis, out Vector3d x, out Vector3d y)
        {
            if (HasFrame(orient))
            {
                x = Unit(orient.XAxis);
                y = Unit(orient.YAxis);
                return;
            }

            Along(fallbackAxis, out x, out y);
        }

        private static Vector3d Unit(Vector3d vector)
        {
            var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);

            return length > 0.0 ? new Vector3d(vector.X / length, vector.Y / length, vector.Z / length) : vector;
        }

        /// <summary>
        /// The six numbers after <c>-orient</c>: the local x axis, then a vector in the local x-y
        /// plane. OpenSees takes the second as a hint rather than a result - it re-squares it
        /// against x - so handing it an axis already square to x is exact.
        /// </summary>
        public static string Orient(Vector3d x, Vector3d y)
        {
            return Numbers(x, y);
        }

        /// <summary>
        /// The same, for an element that takes its local x from its own two nodes.
        ///
        /// A twoNodeLink already knows which way it runs, and given an x of its own it prints a
        /// warning for every element saying it is using that one instead - true even when the two
        /// agree to the last bit. Three numbers, the y hint alone, say the same thing without the
        /// warning, and the frame comes out identical. It is only when the frame really does point
        /// somewhere other than along the link that the six-number form is needed, and there the
        /// warning is telling the truth and worth keeping.
        /// </summary>
        public static string OrientAlong(Vector3d x, Vector3d y, Vector3d axis)
        {
            var along = Unit(axis);

            return Math.Abs(x * along) > 1.0 - 1e-9 ? Numbers(y) : Numbers(x, y);
        }

        private static string Numbers(params Vector3d[] vectors)
        {
            return string.Join(" ", vectors
                .SelectMany(vector => new[] { vector.X, vector.Y, vector.Z })
                .Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Everything that would otherwise reach OpenSees as an unhelpful parse error: no springs
        /// at all, a count that does not line up, a direction outside 1-6, or the same direction
        /// twice.
        /// </summary>
        public static void Check(List<IUniaxialMaterial> materials, List<int> directions, string what)
        {
            if (materials == null || materials.Count == 0)
                throw new Exception($"{what} needs at least one material - one per direction it acts in.");

            if (directions == null || directions.Count == 0)
                throw new Exception($"{what} needs at least one direction. 1, 2 and 3 translate along the " +
                                    "local x, y and z axes; 4, 5 and 6 rotate about them.");

            if (materials.Count != directions.Count)
                throw new Exception($"{what} was given {materials.Count} materials against " +
                                    $"{directions.Count} directions. Each direction takes one material.");

            foreach (var direction in directions)
            {
                if (direction < 1 || direction > 6)
                    throw new Exception($"{what} was given direction {direction}. Directions run 1 to 6: " +
                                        "1-3 translation along the local x, y and z axes, 4-6 rotation about them.");
            }

            if (directions.Distinct().Count() != directions.Count)
                throw new Exception($"{what} lists the same direction more than once. Give each direction " +
                                    "one material; to add two springs in parallel, use two links.");

            if (materials.Any(material => material == null))
                throw new Exception($"{what} was given an empty material. Every direction needs one.");
        }

        /// <summary>
        /// The same check against the degrees of freedom the node actually has. A node on a solid
        /// has three, and a rotational spring there would be attached to nothing.
        /// </summary>
        public static void CheckAgainstNdf(List<int> directions, int ndf, string what)
        {
            if (ndf >= 6)
                return;

            var beyond = directions.Where(direction => direction > ndf).ToList();
            if (beyond.Count == 0)
                return;

            throw new Exception($"{what} reaches a node with {ndf} degrees of freedom - a solid node, which " +
                                $"has translations only - but asks for direction {string.Join(", ", beyond)}. " +
                                $"Only directions 1 to {ndf} exist there.");
        }
    }
}
