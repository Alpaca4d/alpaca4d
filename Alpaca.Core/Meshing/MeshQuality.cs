using System;
using Rhino.Geometry;

namespace Alpaca4d.Meshing
{
    /// <summary>
    /// How well shaped an element is, from 1 for the best shape it can have down to 0 for a flat
    /// one: a regular tetrahedron, an equilateral triangle and a square are 1.
    /// </summary>
    public static class MeshQuality
    {
        /// <summary>Below this an element is poorly shaped enough to spoil the stresses around it.</summary>
        public const double Poor = 0.1;

        /// <summary>
        /// The mean ratio of a tetrahedron: 12 (3V)^(2/3) over the sum of its squared edges. It
        /// falls to 0 as the tetrahedron flattens, whichever way it does.
        /// </summary>
        public static double Tetrahedron(Point3d a, Point3d b, Point3d c, Point3d d)
        {
            var ab = b - a; var ac = c - a; var ad = d - a;
            double volume = Math.Abs(Vector3d.CrossProduct(ab, ac) * ad) / 6.0;
            double squares = ab.SquareLength + ac.SquareLength + ad.SquareLength +
                             (c - b).SquareLength + (d - b).SquareLength + (d - c).SquareLength;
            if (squares <= 0)
                return 0;
            return 12.0 * Math.Pow(3.0 * volume, 2.0 / 3.0) / squares;
        }

        /// <summary>4√3 A over the sum of the squared sides of a triangle.</summary>
        public static double Triangle(Point3d a, Point3d b, Point3d c)
        {
            double area = Vector3d.CrossProduct(b - a, c - a).Length / 2.0;
            double squares = (b - a).SquareLength + (c - b).SquareLength + (a - c).SquareLength;
            if (squares <= 0)
                return 0;
            return 4.0 * Math.Sqrt(3.0) * area / squares;
        }

        /// <summary>
        /// The smallest sine of a quadrangle's four corners, signed by the corner's turn against the
        /// quadrangle's normal: 1 for a rectangle, 0 for a corner gone flat, below 0 for one turned
        /// inside out.
        /// </summary>
        public static double Quadrangle(Point3d a, Point3d b, Point3d c, Point3d d)
        {
            var corners = new[] { a, b, c, d };
            // Divided by hand: Vector3d.Unitize is Rhino's native code.
            var normal = Vector3d.CrossProduct(c - a, d - b);
            double length = normal.Length;
            if (length <= 0)
                return 0;
            normal /= length;
            double worst = 1.0;
            for (int i = 0; i < 4; i++)
            {
                var here = corners[i];
                var next = corners[(i + 1) % 4] - here;
                var previous = corners[(i + 3) % 4] - here;
                double lengths = next.Length * previous.Length;
                if (lengths <= 0)
                    return 0;
                double sine = Vector3d.CrossProduct(next, previous) * normal / lengths;
                worst = Math.Min(worst, sine);
            }
            return worst;
        }
    }
}
