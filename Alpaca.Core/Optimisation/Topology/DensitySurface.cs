using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// The surface where the density of a topology optimisation crosses a threshold: the outline of
    /// the material it keeps, closed where that material reaches the edge of the design domain.
    ///
    /// The density is known per element. It is spread to the corners as the mean of the elements
    /// around each one, by volume, and kept at the element's centre as the element's own, and each
    /// element is cut into tetrahedra over those points - a brick into 24, through the middles of its
    /// faces and its own middle; a tetrahedron into 4, through its middle. Inside each tetrahedron the
    /// density is taken as linear, so the surface crosses it as a flat triangle or two (marching
    /// tetrahedra), and neighbouring tetrahedra cut a shared edge at the same point. Where the domain
    /// ends, the part of its boundary inside the material closes the surface.
    ///
    /// Plain lists, not a Rhino mesh, so that it is built and tested without Rhino's native library.
    /// </summary>
    public sealed class DensitySurface
    {
        public List<Point3d> Vertices { get; } = new List<Point3d>();

        /// <summary>Triangles, as three indexes into <see cref="Vertices"/>, each facing out of the material.</summary>
        public List<int[]> Triangles { get; } = new List<int[]>();

        // The points the tetrahedra are made of, and the density at each.
        private readonly List<Point3d> _points = new List<Point3d>();
        private readonly List<double> _values = new List<double>();
        private readonly Dictionary<long, int> _cut = new Dictionary<long, int>();
        private readonly double _threshold;

        // A brick's faces, by its corners in OpenSees order, each seen from outside counter-clockwise.
        private static readonly int[][] HexFaces =
        {
            new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
            new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 },
        };

        // A tetrahedron's faces, by its corners: the face opposite each corner.
        private static readonly int[][] TetFaces =
        {
            new[] { 1, 2, 3 }, new[] { 0, 2, 3 }, new[] { 0, 1, 3 }, new[] { 0, 1, 2 },
        };

        /// <param name="cells">The elements, as the optimisation had them.</param>
        /// <param name="densities">Each element's physical density, in the order of the cells.</param>
        /// <param name="nodes">Where each node is, by its tag.</param>
        /// <param name="threshold">The density the surface is drawn at.</param>
        public DensitySurface(IReadOnlyList<SolidCell> cells, IReadOnlyList<double> densities, Func<int, Point3d> nodes, double threshold)
        {
            _threshold = threshold;

            // The corners: each the volume-weighted mean of the elements on it.
            var point = new Dictionary<int, int>();
            var weight = new List<double>();
            for (int c = 0; c < cells.Count; c++)
            {
                var cell = cells[c];
                for (int k = 0; k < SolidCell.CornerCount(cell.Shape); k++)
                {
                    int tag = cell.Nodes[k];
                    if (!point.TryGetValue(tag, out int at))
                    {
                        point[tag] = at = _points.Count;
                        _points.Add(nodes(tag));
                        _values.Add(0.0);
                        weight.Add(0.0);
                    }
                    _values[at] += cell.Volume * densities[c];
                    weight[at] += cell.Volume;
                }
            }
            for (int i = 0; i < _values.Count; i++)
                _values[i] /= weight[i];

            // Which faces have an element on one side only: the boundary of the domain.
            var faceUse = new Dictionary<string, int>();
            foreach (var cell in cells)
                foreach (var face in Faces(cell))
                {
                    string key = FaceKey(cell, face);
                    faceUse[key] = faceUse.TryGetValue(key, out int count) ? count + 1 : 1;
                }

            var faceCentre = new Dictionary<string, int>();
            for (int c = 0; c < cells.Count; c++)
            {
                var cell = cells[c];
                var corners = Enumerable.Range(0, SolidCell.CornerCount(cell.Shape)).Select(k => point[cell.Nodes[k]]).ToArray();
                int centre = Add(cell.Centroid, densities[c]);

                foreach (var face in Faces(cell))
                {
                    string key = FaceKey(cell, face);
                    bool boundary = faceUse[key] == 1;
                    var ring = face.Select(k => corners[k]).ToArray();

                    // A triangle is a face already; a quadrilateral is split at its middle, which both
                    // bricks on it find, so they cut it the same way.
                    var triangles = new List<int[]>();
                    if (ring.Length == 3)
                        triangles.Add(ring);
                    else
                    {
                        if (!faceCentre.TryGetValue(key, out int middle))
                        {
                            var p = new Point3d(ring.Average(i => _points[i].X), ring.Average(i => _points[i].Y), ring.Average(i => _points[i].Z));
                            faceCentre[key] = middle = Add(p, ring.Average(i => _values[i]));
                        }
                        for (int e = 0; e < 4; e++)
                            triangles.Add(new[] { ring[e], ring[(e + 1) % 4], middle });
                    }

                    foreach (var t in triangles)
                    {
                        March(t[0], t[1], t[2], centre);
                        if (boundary)
                            Cap(t, centre);
                    }
                }
            }
        }

        private static IEnumerable<int[]> Faces(SolidCell cell) => cell.Shape == SolidShape.Hexahedron ? HexFaces : TetFaces;

        private static string FaceKey(SolidCell cell, int[] face) =>
            string.Join(",", face.Select(k => cell.Nodes[k]).OrderBy(tag => tag));

        private int Add(Point3d p, double value)
        {
            _points.Add(p);
            _values.Add(value);
            return _points.Count - 1;
        }

        private bool Inside(int point) => _values[point] >= _threshold;

        /// <summary>
        /// Where the surface cuts the segment between two points, one inside and one out: one vertex per
        /// segment. A point exactly at the threshold - a node between four solid bricks and four void
        /// ones, at 0.5 - is cut at itself, so the cuts of the segments from it all sit on it, as
        /// vertices of their own: the triangles stay joined edge to edge, and only some have no area.
        /// </summary>
        private int Cut(int a, int b)
        {
            if (a > b) (a, b) = (b, a);
            long key = ((long)a << 32) | (uint)b;
            if (_cut.TryGetValue(key, out int vertex))
                return vertex;

            double va = _values[a], vb = _values[b];
            double t = Math.Max(0.0, Math.Min(1.0, (_threshold - va) / (vb - va)));
            Vertices.Add(_points[a] + t * (_points[b] - _points[a]));
            _cut[key] = Vertices.Count - 1;
            return Vertices.Count - 1;
        }

        /// <summary>
        /// The surface through one tetrahedron: nothing, a triangle, or a quadrilateral as two. Which way
        /// it faces is found from the middles of the edges it cuts rather than from the cuts themselves,
        /// which may all sit on one point; the middles never do, and lie on the same side of every
        /// corner as the cuts.
        /// </summary>
        private void March(int a, int b, int c, int d)
        {
            var all = new[] { a, b, c, d };
            var inside = all.Where(Inside).ToArray();
            var outside = all.Where(p => !Inside(p)).ToArray();
            if (inside.Length == 0 || outside.Length == 0)
                return;

            // Out of the material is the way from the inside points to the outside ones.
            var outward = Mean(outside) - Mean(inside);

            if (inside.Length == 1 || outside.Length == 1)
            {
                int lone = inside.Length == 1 ? inside[0] : outside[0];
                var others = inside.Length == 1 ? outside : inside;
                bool flip = Facing(outward, Middle(lone, others[0]), Middle(lone, others[1]), Middle(lone, others[2]));
                Face(flip, Cut(lone, others[0]), Cut(lone, others[1]), Cut(lone, others[2]));
            }
            else
            {
                // Two in, two out: the four cut edges make a quadrilateral, in this order around it.
                bool flip = Facing(outward, Middle(inside[0], outside[0]), Middle(inside[0], outside[1]), Middle(inside[1], outside[1]));
                int p = Cut(inside[0], outside[0]), q = Cut(inside[0], outside[1]);
                int r = Cut(inside[1], outside[1]), s = Cut(inside[1], outside[0]);
                Face(flip, p, q, r);
                Face(flip, p, r, s);
            }
        }

        /// <summary>
        /// The part of a boundary triangle that is inside the material, facing out of the domain. The
        /// part keeps the triangle's own order round it, so it faces the way the triangle does.
        /// </summary>
        private void Cap(int[] triangle, int centre)
        {
            bool flip = Facing(Mean(triangle) - _points[centre], _points[triangle[0]], _points[triangle[1]], _points[triangle[2]]);
            var polygon = new List<int>();
            for (int e = 0; e < 3; e++)
            {
                int p = triangle[e], q = triangle[(e + 1) % 3];
                if (Inside(p))
                    polygon.Add(Corner(p));
                if (Inside(p) != Inside(q))
                    polygon.Add(Cut(p, q));
            }

            for (int k = 1; k + 1 < polygon.Count; k++)
                Face(flip, polygon[0], polygon[k], polygon[k + 1]);
        }

        private Point3d Middle(int a, int b) => 0.5 * (_points[a] + _points[b]);

        /// <summary>True when the triangle a, b, c has to be turned round to face <paramref name="outward"/>.</summary>
        private static bool Facing(Vector3d outward, Point3d a, Point3d b, Point3d c) =>
            Vector3d.CrossProduct(b - a, c - a) * outward < 0.0;

        // A corner of the domain boundary that is inside the material, as a vertex of the surface.
        private readonly Dictionary<int, int> _corner = new Dictionary<int, int>();

        private int Corner(int point)
        {
            if (!_corner.TryGetValue(point, out int vertex))
            {
                Vertices.Add(_points[point]);
                _corner[point] = vertex = Vertices.Count - 1;
            }
            return vertex;
        }

        private Point3d Mean(IEnumerable<int> points)
        {
            var list = points.ToList();
            return new Point3d(list.Average(i => _points[i].X), list.Average(i => _points[i].Y), list.Average(i => _points[i].Z));
        }

        /// <summary>A triangle, turned round when <paramref name="flip"/>.</summary>
        private void Face(bool flip, int a, int b, int c)
        {
            Triangles.Add(flip ? new[] { a, c, b } : new[] { a, b, c });
        }
    }
}
