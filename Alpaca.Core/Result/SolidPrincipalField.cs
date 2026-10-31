using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

namespace Alpaca4d.Result
{
    /// <summary>
    /// The principal stress directions inside a solid, which Principal Stress Lines 3D follows.
    ///
    /// The stress comes element by element: one value for an element that integrates at a single
    /// point (SSP Brick, the four node tetrahedron), or one at each node for a ten node tetrahedron,
    /// whose stress is linear. Each node takes the volume-weighted mean of what the elements round it
    /// give it, so the field is continuous where, taken element by element, it would jump at every
    /// face.
    ///
    /// To interpolate it, and to know which element a point is in, every element is cut into
    /// tetrahedra on points it shares with its neighbours, so that the pieces of two neighbours meet
    /// face to face: a ten node tetrahedron into eight, on its corner and edge nodes, and a brick into
    /// twenty-four, round its centre and the centres of its faces. A tetrahedron knows its four
    /// neighbours, one across each face, and a point is found by walking: from the tetrahedron it was
    /// last in, across whichever face it now lies beyond. Along a line that is a step or none. A face
    /// no other tetrahedron shares is the solid's boundary, where a line ends.
    ///
    /// Principal directions are those of the stress tensor, from the largest principal stress
    /// (family 1) to the smallest (family 3).
    /// </summary>
    public class SolidPrincipalField
    {
        /// <summary>The mean length of the elements' edges, which the line tracer takes its default sizes from.</summary>
        public double EdgeLength { get; }

        /// <summary>The largest principal difference at any node, largest less smallest: what "small" is measured against.</summary>
        public double Largest { get; }

        /// <summary>How many elements were left out because they have no volume, or are not a solid this knows.</summary>
        public int Degenerate { get; }

        /// <summary>The box round the solid.</summary>
        public BoundingBox Box { get; }

        /// <summary>How many tetrahedra the elements were cut into.</summary>
        public int Tetrahedra => _corners.Length / 4;

        /// <summary>A point counts as inside a tetrahedron when none of its volume coordinates is below minus this.</summary>
        const double Tolerance = 1e-9;

        readonly Point3d[] _points;
        readonly double[][] _stress;     // per point: xx, yy, zz, xy, yz, zx
        readonly int[] _corners;          // four per tetrahedron
        readonly int[] _neighbour;        // per tetrahedron and corner: the tetrahedron across the face opposite it, or -1
        readonly double[] _frame;         // per tetrahedron: its first corner, then the inverse of its edge matrix, row by row
        readonly RTree _tree = new RTree();

        /// <param name="nodes">The node positions.</param>
        /// <param name="elements">
        /// Each element as indexes into <paramref name="nodes"/>, in the solver's order: four for a
        /// tetrahedron; eight for a brick, the bottom face round then the top; ten for a ten node
        /// tetrahedron, corners first, then the nodes on edges 01, 12, 20, 03, 13 and 23.
        /// </param>
        /// <param name="stresses">
        /// Per element, the stress as xx, yy, zz, xy, yz, zx: one for the whole element, or one per
        /// node in the order of <paramref name="elements"/>.
        /// </param>
        public SolidPrincipalField(IList<Point3d> nodes, IList<int[]> elements, IList<double[][]> stresses)
        {
            var points = new List<Point3d>(nodes);
            var corners = new List<int>();
            var sum = new double[nodes.Count][];
            var weight = new double[nodes.Count];
            double edges = 0;
            int edgeCount = 0, degenerate = 0;

            // Points a brick adds, the centres of its faces shared with the brick on the other side,
            // each with the corners whose mean it takes.
            var faceCentre = new Dictionary<(int, int, int, int), int>();
            var meanOf = new List<int[]>();

            for (int e = 0; e < elements.Count; e++)
            {
                var element = elements[e];
                var stress = e < stresses.Count ? stresses[e] : null;
                if (element == null || stress == null || (stress.Length != 1 && stress.Length != element.Length) ||
                    stress.Any(s => s == null || s.Length != 6) || element.Any(n => n < 0 || n >= nodes.Count) ||
                    element.Distinct().Count() != element.Length)
                {
                    degenerate++;
                    continue;
                }

                int before = corners.Count, added = points.Count;
                switch (element.Length)
                {
                    case 4:
                        corners.AddRange(element);
                        break;
                    case 10:
                        CutTenNode(element, nodes, corners);
                        break;
                    case 8:
                        CutBrick(element, points, faceCentre, meanOf, nodes.Count, corners);
                        break;
                    default:
                        degenerate++;
                        continue;
                }

                double volume = 0;
                for (int t = before; t < corners.Count; t += 4)
                    volume += Math.Abs(Volume(points[corners[t]], points[corners[t + 1]], points[corners[t + 2]], points[corners[t + 3]]));
                if (volume <= 0)
                {
                    corners.RemoveRange(before, corners.Count - before);
                    degenerate++;
                    continue;
                }

                for (int k = 0; k < element.Length; k++)
                {
                    var s = stress.Length == 1 ? stress[0] : stress[k];
                    var into = sum[element[k]] ?? (sum[element[k]] = new double[6]);
                    for (int c = 0; c < 6; c++) into[c] += volume * s[c];
                    weight[element[k]] += volume;
                }

                int cornerCount = element.Length == 10 ? 4 : element.Length;
                foreach (var (a, b) in Edges(cornerCount))
                {
                    edges += nodes[element[a]].DistanceTo(nodes[element[b]]);
                    edgeCount++;
                }
            }

            // The nodes' stresses, then the face and brick centres' as the mean of their corners'.
            _stress = new double[points.Count][];
            double largest = 0;
            for (int n = 0; n < nodes.Count; n++)
            {
                if (weight[n] <= 0) continue;
                _stress[n] = new double[6];
                for (int c = 0; c < 6; c++) _stress[n][c] = sum[n][c] / weight[n];
            }
            for (int i = 0; i < meanOf.Count; i++)
            {
                var known = meanOf[i].Where(n => _stress[n] != null).ToArray();
                if (known.Length == 0) continue;
                var mean = new double[6];
                foreach (int n in known)
                    for (int c = 0; c < 6; c++) mean[c] += _stress[n][c] / known.Length;
                _stress[nodes.Count + i] = mean;
            }
            var values = new double[3];
            var vectors = new Vector3d[3];
            for (int n = 0; n < nodes.Count; n++)
            {
                if (_stress[n] == null) continue;
                Eigen(_stress[n], values, vectors);
                largest = Math.Max(largest, values[0] - values[2]);
            }

            _points = points.ToArray();
            _corners = corners.ToArray();
            Degenerate = degenerate;
            Largest = largest;
            EdgeLength = edgeCount > 0 ? edges / edgeCount : 0;

            // Each tetrahedron's frame for its volume coordinates, and its box in the tree.
            int count = _corners.Length / 4;
            _frame = new double[12 * count];
            var box = BoundingBox.Empty;
            for (int t = 0; t < count; t++)
            {
                var p0 = _points[_corners[4 * t]];
                var a = _points[_corners[4 * t + 1]] - p0;
                var b = _points[_corners[4 * t + 2]] - p0;
                var c = _points[_corners[4 * t + 3]] - p0;
                double det = a * Vector3d.CrossProduct(b, c);
                var r0 = Vector3d.CrossProduct(b, c) / det;
                var r1 = Vector3d.CrossProduct(c, a) / det;
                var r2 = Vector3d.CrossProduct(a, b) / det;
                int f = 12 * t;
                _frame[f] = p0.X; _frame[f + 1] = p0.Y; _frame[f + 2] = p0.Z;
                _frame[f + 3] = r0.X; _frame[f + 4] = r0.Y; _frame[f + 5] = r0.Z;
                _frame[f + 6] = r1.X; _frame[f + 7] = r1.Y; _frame[f + 8] = r1.Z;
                _frame[f + 9] = r2.X; _frame[f + 10] = r2.Y; _frame[f + 11] = r2.Z;

                var bb = new BoundingBox(new[] { p0, _points[_corners[4 * t + 1]], _points[_corners[4 * t + 2]], _points[_corners[4 * t + 3]] });
                _tree.Insert(bb, t);
                box.Union(bb);
            }
            Box = box;

            // Neighbours: two tetrahedra that have a face's three corners in common share it.
            _neighbour = new int[4 * count];
            var open = new Dictionary<(int, int, int), int>(4 * count);
            for (int t = 0; t < count; t++)
                for (int k = 0; k < 4; k++)
                {
                    _neighbour[4 * t + k] = -1;
                    var key = FaceKey(_corners[4 * t + (k + 1) % 4], _corners[4 * t + (k + 2) % 4], _corners[4 * t + (k + 3) % 4]);
                    if (open.TryGetValue(key, out int other))
                    {
                        _neighbour[4 * t + k] = other / 4;
                        _neighbour[other] = t;
                        open.Remove(key);
                    }
                    else
                        open[key] = 4 * t + k;
                }
        }

        /// <summary>Whether <paramref name="point"/> is inside the solid. <paramref name="hint"/> is the tetrahedron to start looking from, -1 for none, and comes back as the one it is in.</summary>
        public bool Contains(Point3d point, ref int hint)
        {
            var weights = new double[4];
            return Locate(point, ref hint, weights);
        }

        /// <summary>The stress at <paramref name="point"/>, as xx, yy, zz, xy, yz, zx; false outside the solid.</summary>
        public bool Stress(Point3d point, ref int hint, double[] stress)
        {
            var weights = new double[4];
            if (!Locate(point, ref hint, weights)) return false;
            for (int c = 0; c < 6; c++) stress[c] = 0;
            for (int k = 0; k < 4; k++)
            {
                var s = _stress[_corners[4 * hint + k]];
                for (int c = 0; c < 6; c++) stress[c] += weights[k] * s[c];
            }
            return true;
        }

        /// <summary>
        /// The principal stresses at <paramref name="point"/>, largest first, and their directions;
        /// false outside the solid.
        /// </summary>
        public bool Principal(Point3d point, ref int hint, double[] values, Vector3d[] vectors)
        {
            var stress = new double[6];
            if (!Stress(point, ref hint, stress)) return false;
            Eigen(stress, values, vectors);
            return true;
        }

        /// <summary>
        /// The direction of principal family <paramref name="family"/> at <paramref name="point"/>,
        /// and how far its principal stress stands from the nearest other one: the direction is only
        /// as well defined as that is large. False outside the solid.
        /// </summary>
        public bool Evaluate(Point3d point, int family, ref int hint, out Vector3d direction, out double strength)
        {
            var values = new double[3];
            var vectors = new Vector3d[3];
            direction = Vector3d.Unset;
            strength = 0;
            if (!Principal(point, ref hint, values, vectors)) return false;
            direction = vectors[family - 1];
            strength = family == 1 ? values[0] - values[1]
                     : family == 3 ? values[1] - values[2]
                     : Math.Min(values[0] - values[1], values[1] - values[2]);
            return true;
        }

        // ---------------------------------------------------------------- finding a point

        bool Locate(Point3d point, ref int hint, double[] weights)
        {
            int count = _corners.Length / 4;
            if (count == 0) return false;

            // Walk from the hint, across the face the point lies furthest beyond.
            if (hint >= 0 && hint < count)
            {
                int t = hint;
                for (int walk = 0; walk < 256; walk++)
                {
                    Weights(t, point, weights);
                    int worst = 0;
                    for (int k = 1; k < 4; k++)
                        if (weights[k] < weights[worst]) worst = k;
                    if (weights[worst] >= -Tolerance)
                    {
                        hint = t;
                        return true;
                    }
                    int next = _neighbour[4 * t + worst];
                    if (next < 0) break;
                    t = next;
                }
            }

            // Out of the hint's reach - a hole or a notch in the way, or no hint: look it up.
            int found = -1;
            var buffer = new double[4];
            var box = new BoundingBox(point, point);
            _tree.Search(box, (sender, e) =>
            {
                Weights(e.Id, point, buffer);
                if (buffer.Min() >= -Tolerance)
                {
                    found = e.Id;
                    e.Cancel = true;
                }
            });
            if (found < 0) return false;
            Weights(found, point, weights);
            hint = found;
            return true;
        }

        void Weights(int t, Point3d point, double[] weights)
        {
            int f = 12 * t;
            double x = point.X - _frame[f], y = point.Y - _frame[f + 1], z = point.Z - _frame[f + 2];
            weights[1] = _frame[f + 3] * x + _frame[f + 4] * y + _frame[f + 5] * z;
            weights[2] = _frame[f + 6] * x + _frame[f + 7] * y + _frame[f + 8] * z;
            weights[3] = _frame[f + 9] * x + _frame[f + 10] * y + _frame[f + 11] * z;
            weights[0] = 1.0 - weights[1] - weights[2] - weights[3];
        }

        // ---------------------------------------------------------------- cutting elements into tetrahedra

        /// <summary>
        /// A ten node tetrahedron into eight: one at each corner, on the three edge nodes round it,
        /// and the octahedron the edge nodes leave in the middle, cut along its shortest diagonal.
        /// Each face of the element is cut into four triangles on its own six nodes, the same four
        /// whichever element it belongs to.
        /// </summary>
        static void CutTenNode(int[] element, IList<Point3d> nodes, List<int> corners)
        {
            int Mid(int i, int j)
            {
                for (int e = 0; e < Utils.TetrahedronEdges.Length; e++)
                {
                    var edge = Utils.TetrahedronEdges[e];
                    if ((edge[0] == i && edge[1] == j) || (edge[0] == j && edge[1] == i))
                        return element[4 + e];
                }
                throw new ArgumentException("No such edge.");
            }

            for (int i = 0; i < 4; i++)
            {
                var others = Enumerable.Range(0, 4).Where(j => j != i).ToArray();
                corners.Add(element[i]);
                corners.Add(Mid(i, others[0]));
                corners.Add(Mid(i, others[1]));
                corners.Add(Mid(i, others[2]));
            }

            // The three diagonals of the octahedron join the middles of opposite edges.
            var pairs = new[] { (0, 1, 2, 3), (0, 2, 1, 3), (0, 3, 1, 2) };
            var best = pairs.OrderBy(p => nodes[Mid(p.Item1, p.Item2)].DistanceTo(nodes[Mid(p.Item3, p.Item4)])).First();
            var (a, b, c, d) = best;
            int top = Mid(a, b), bottom = Mid(c, d);
            var ring = new[] { Mid(a, c), Mid(b, c), Mid(b, d), Mid(a, d) };
            for (int k = 0; k < 4; k++)
            {
                corners.Add(top);
                corners.Add(bottom);
                corners.Add(ring[k]);
                corners.Add(ring[(k + 1) % 4]);
            }
        }

        static readonly int[][] BrickFaces =
        {
            new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
            new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 },
        };

        /// <summary>
        /// A brick into twenty-four: each face into four triangles round its centre, each triangle
        /// joined to the brick's centre. A face's centre is shared with the brick on its other side,
        /// so the two cut it alike, however the brick is warped.
        /// </summary>
        static void CutBrick(int[] element, List<Point3d> points, Dictionary<(int, int, int, int), int> faceCentre,
                             List<int[]> meanOf, int nodeCount, List<int> corners)
        {
            int centre = points.Count;
            points.Add(Mean(element.Select(n => points[n])));
            meanOf.Add(element.ToArray());

            foreach (var face in BrickFaces)
            {
                var ring = face.Select(k => element[k]).ToArray();
                var sorted = ring.OrderBy(n => n).ToArray();
                var key = (sorted[0], sorted[1], sorted[2], sorted[3]);
                if (!faceCentre.TryGetValue(key, out int middle))
                {
                    middle = points.Count;
                    points.Add(Mean(ring.Select(n => points[n])));
                    meanOf.Add(ring);
                    faceCentre[key] = middle;
                }
                for (int k = 0; k < 4; k++)
                {
                    corners.Add(centre);
                    corners.Add(middle);
                    corners.Add(ring[k]);
                    corners.Add(ring[(k + 1) % 4]);
                }
            }
        }

        static IEnumerable<(int, int)> Edges(int cornerCount)
        {
            if (cornerCount == 4)
                return Utils.TetrahedronEdges.Select(e => (e[0], e[1]));
            return new[] { (0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6), (6, 7), (7, 4), (0, 4), (1, 5), (2, 6), (3, 7) };
        }

        static Point3d Mean(IEnumerable<Point3d> points)
        {
            var sum = new Point3d(0, 0, 0);
            int n = 0;
            foreach (var p in points) { sum += p; n++; }
            return sum / n;
        }

        static double Volume(Point3d a, Point3d b, Point3d c, Point3d d) =>
            (b - a) * Vector3d.CrossProduct(c - a, d - a) / 6.0;

        static (int, int, int) FaceKey(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }

        // ---------------------------------------------------------------- principal stresses

        /// <summary>
        /// The eigenvalues of a symmetric tensor given as xx, yy, zz, xy, yz, zx, largest first, and
        /// their unit eigenvectors, by Jacobi rotations: slower than a closed form, and exact to
        /// rounding however close two eigenvalues come.
        /// </summary>
        public static void Eigen(double[] s, double[] values, Vector3d[] vectors)
        {
            var a = new double[3, 3]
            {
                { s[0], s[3], s[5] },
                { s[3], s[1], s[4] },
                { s[5], s[4], s[2] },
            };
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = a[0, 1] * a[0, 1] + a[0, 2] * a[0, 2] + a[1, 2] * a[1, 2];
                double diagonal = a[0, 0] * a[0, 0] + a[1, 1] * a[1, 1] + a[2, 2] * a[2, 2];
                if (off == 0 || off <= 1e-30 * diagonal) break;

                for (int p = 0; p < 2; p++)
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (a[p, q] == 0) continue;
                        double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                        double t = (theta >= 0 ? 1.0 : -1.0) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                        double c = 1 / Math.Sqrt(t * t + 1), sn = t * c;
                        for (int k = 0; k < 3; k++)
                        {
                            double kp = a[k, p], kq = a[k, q];
                            a[k, p] = c * kp - sn * kq;
                            a[k, q] = sn * kp + c * kq;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double pk = a[p, k], qk = a[q, k];
                            a[p, k] = c * pk - sn * qk;
                            a[q, k] = sn * pk + c * qk;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double kp = v[k, p], kq = v[k, q];
                            v[k, p] = c * kp - sn * kq;
                            v[k, q] = sn * kp + c * kq;
                        }
                    }
            }

            int i0 = 0, i1 = 1, i2 = 2;
            if (a[i0, i0] < a[i1, i1]) (i0, i1) = (i1, i0);
            if (a[i1, i1] < a[i2, i2]) (i1, i2) = (i2, i1);
            if (a[i0, i0] < a[i1, i1]) (i0, i1) = (i1, i0);
            int[] order = { i0, i1, i2 };
            for (int k = 0; k < 3; k++)
            {
                values[k] = a[order[k], order[k]];
                vectors[k] = new Vector3d(v[0, order[k]], v[1, order[k]], v[2, order[k]]);
                vectors[k].Unitize();
            }
        }
    }
}
