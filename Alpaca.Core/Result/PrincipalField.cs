using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

namespace Alpaca4d.Result
{
    /// <summary>
    /// The principal directions over a set of shell faces, which Principal Stress Lines follows.
    ///
    /// Each face's strain is taken at its centre: the membrane strain from the nodal translations, or
    /// the bending curvature from the nodal rotations. The centre is where a four-node face's strain is
    /// most accurate. In bending, a face's own interpolation gives a shear that is wrong everywhere
    /// but there, and that shear turns the directions. The centre values are averaged into each corner
    /// from the faces round that node that lie within <see cref="CreaseAngle"/> of the face, and
    /// interpolated across it. The field is then continuous from face to face, where one taken face by
    /// face jumps at every edge.
    ///
    /// For an isotropic elastic material the principal stresses lie along the principal strains, and
    /// the larger along the larger, so the strain gives the directions directly: Poisson's ratio does
    /// not enter. A material that is not isotropic, or no longer elastic, breaks that, and the lines
    /// then follow the strain rather than the stress.
    ///
    /// The faces are joined through the nodes they share, so faces Assemble welded together are one
    /// surface here too, whatever their own vertices say.
    /// </summary>
    public class PrincipalField
    {
        /// <summary>Faces folded further apart than this at a node do not share their strain there.</summary>
        public const double CreaseAngle = 30.0;

        /// <summary>One vertex per node, one face per shell face kept.</summary>
        public Mesh Mesh { get; }

        /// <summary>The mean length of the faces' edges, which the line tracer takes its default sizes from.</summary>
        public double EdgeLength { get; }

        /// <summary>The largest half principal difference of any face: what "small" is measured against.</summary>
        public double Largest { get; }

        /// <summary>How many faces were left out because they have no area.</summary>
        public int Degenerate { get; }

        /// <summary>
        /// How many faces in bending were read the other way up from their own vertex order, to agree
        /// with their neighbours. A face's bending is only "up" or "down" against its normal, and
        /// neighbours that disagree about which way is up would cancel each other's curvature.
        /// </summary>
        public int Flipped { get; }

        readonly Vector3d[] _x, _y, _n;
        readonly double[][] _corner;   // per face, per corner: xx, yy, zz, xy, yz, zx

        /// <param name="nodes">The node positions.</param>
        /// <param name="faces">Each face as three or four indexes into <paramref name="nodes"/>, in its own vertex order.</param>
        /// <param name="values">Per node: the translation, or for <paramref name="bending"/> the rotation vector.</param>
        /// <param name="bending">Follow the bending curvature rather than the membrane strain.</param>
        public PrincipalField(IList<Point3d> nodes, IList<int[]> faces, IList<Vector3d> values, bool bending)
        {
            Mesh = new Mesh();
            Mesh.Vertices.UseDoublePrecisionVertices = true;
            foreach (var p in nodes)
                Mesh.Vertices.Add(p);

            var kept = new List<int[]>();
            foreach (var face in faces)
            {
                if (face.Length < 3 || face.Length > 4 || face.Distinct().Count() != face.Length || Area(nodes, face) <= 0.0)
                {
                    Degenerate++;
                    continue;
                }
                kept.Add(face);
                if (face.Length == 4) Mesh.Faces.AddFace(face[0], face[1], face[2], face[3]);
                else Mesh.Faces.AddFace(face[0], face[1], face[2]);
            }

            int count = kept.Count;
            int flipped = 0;
            var flip = bending ? Orientation(nodes, kept, out flipped) : new bool[count];
            Flipped = flipped;

            _x = new Vector3d[count]; _y = new Vector3d[count]; _n = new Vector3d[count];
            var centre = new double[count][];
            var area = new double[count];
            double edges = 0; int edgeCount = 0;
            for (int f = 0; f < count; f++)
            {
                var face = kept[f];
                var p = face.Select(i => nodes[i]).ToArray();
                Frame(p, flip[f], out _x[f], out _y[f], out _n[f]);
                area[f] = Area(nodes, face);
                centre[f] = CentreStrain(p, face.Select(i => values[i]).ToArray(), _x[f], _y[f], bending);
                for (int k = 0; k < p.Length; k++) { edges += p[k].DistanceTo(p[(k + 1) % p.Length]); edgeCount++; }
            }
            EdgeLength = edgeCount > 0 ? edges / edgeCount : 0.0;
            Largest = Enumerable.Range(0, count).Select(f => Strength(centre[f], _x[f], _y[f])).DefaultIfEmpty(0.0).Max();

            // Each corner: the area-weighted mean of the faces round its node that lie within the crease angle.
            var round = new List<int>[nodes.Count];
            for (int f = 0; f < count; f++)
                foreach (var i in kept[f]) (round[i] ??= new List<int>()).Add(f);
            double cos = Math.Cos(CreaseAngle * Math.PI / 180.0);
            _corner = new double[count][];
            for (int f = 0; f < count; f++)
            {
                var face = kept[f];
                _corner[f] = new double[6 * face.Length];
                for (int k = 0; k < face.Length; k++)
                {
                    double weight = 0;
                    foreach (var g in round[face[k]])
                    {
                        if (_n[g] * _n[f] < cos) continue;
                        for (int c = 0; c < 6; c++) _corner[f][6 * k + c] += area[g] * centre[g][c];
                        weight += area[g];
                    }
                    for (int c = 0; c < 6; c++) _corner[f][6 * k + c] /= weight;
                }
            }
        }

        /// <summary>
        /// The principal direction of <paramref name="family"/> (1, the larger; 2, the smaller) at the
        /// point of the mesh closest to <paramref name="point"/>. Its sign means nothing: a principal
        /// direction is a line, not an arrow.
        /// </summary>
        public bool Evaluate(Point3d point, int family, out Point3d onMesh, out Vector3d direction, out Vector3d normal, out double strength)
        {
            onMesh = point; direction = Vector3d.Unset; normal = Vector3d.Unset; strength = 0;
            var mp = Mesh.ClosestMeshPoint(point, 0.0);
            if (mp == null || mp.FaceIndex < 0 || mp.FaceIndex >= _corner.Length) return false;

            int f = mp.FaceIndex;
            var corner = _corner[f];
            var t = new double[6];
            for (int k = 0; k < corner.Length / 6; k++)
                for (int c = 0; c < 6; c++) t[c] += mp.T[k] * corner[6 * k + c];

            double a = Along(t, _x[f], _x[f]), b = Along(t, _y[f], _y[f]), s = Along(t, _x[f], _y[f]);
            double angle = 0.5 * Math.Atan2(2 * s, a - b) + (family == 2 ? Math.PI / 2 : 0.0);
            direction = Math.Cos(angle) * _x[f] + Math.Sin(angle) * _y[f];
            normal = _n[f];
            strength = Math.Sqrt(0.25 * (a - b) * (a - b) + s * s);
            onMesh = mp.Point;
            return true;
        }

        // ---------------------------------------------------------------- one face

        static double Area(IList<Point3d> nodes, int[] face)
        {
            var p = face.Select(i => nodes[i]).ToArray();
            var cross = p.Length == 4
                ? Vector3d.CrossProduct(p[2] - p[0], p[3] - p[1])
                : Vector3d.CrossProduct(p[1] - p[0], p[2] - p[0]);
            return 0.5 * cross.Length;
        }

        /// <summary>
        /// The face's own axes: the normal of its vertex order (through the diagonals of a four-node
        /// face, which is the mean plane of a warped one), turned over if <paramref name="flip"/>, and x
        /// along its first edge.
        /// </summary>
        static void Frame(Point3d[] p, bool flip, out Vector3d x, out Vector3d y, out Vector3d n)
        {
            n = p.Length == 4 ? Vector3d.CrossProduct(p[2] - p[0], p[3] - p[1]) : Vector3d.CrossProduct(p[1] - p[0], p[2] - p[0]);
            n.Unitize();
            if (flip) n = -n;
            x = p[1] - p[0];
            x -= (x * n) * n;
            x.Unitize();
            y = Vector3d.CrossProduct(n, x);
        }

        /// <summary>
        /// The strain at the face's centre as a tensor in space. Membrane: from the translations along
        /// the face. Bending: the curvature, from the rotations about the face's own axes - a rotation
        /// about y moves the face along x as the slope -dw/dx does, so (ry, -rx) is differentiated as
        /// the translations are, giving kappa = -grad grad w: positive where the face the normal points
        /// to is stretched.
        /// </summary>
        static double[] CentreStrain(Point3d[] p, Vector3d[] v, Vector3d x, Vector3d y, bool bending)
        {
            var o = Point3d.Origin; foreach (var q in p) o += (Vector3d)q; o /= p.Length;
            var px = p.Select(q => (q - o) * x).ToArray();
            var py = p.Select(q => (q - o) * y).ToArray();
            var ux = new double[p.Length]; var uy = new double[p.Length];
            for (int k = 0; k < p.Length; k++)
            {
                double vx = v[k] * x, vy = v[k] * y;
                if (bending) { ux[k] = vy; uy[k] = -vx; } else { ux[k] = vx; uy[k] = vy; }
            }

            var dx = new double[p.Length]; var dy = new double[p.Length];
            if (p.Length == 4)
            {
                // The four-node shape functions' derivatives at the centre, in the face's vertex order.
                double[] dxi = { -0.25, 0.25, 0.25, -0.25 }, deta = { -0.25, -0.25, 0.25, 0.25 };
                double j11 = 0, j12 = 0, j21 = 0, j22 = 0;
                for (int k = 0; k < 4; k++) { j11 += dxi[k] * px[k]; j12 += dxi[k] * py[k]; j21 += deta[k] * px[k]; j22 += deta[k] * py[k]; }
                double det = j11 * j22 - j12 * j21;
                for (int k = 0; k < 4; k++) { dx[k] = (j22 * dxi[k] - j12 * deta[k]) / det; dy[k] = (-j21 * dxi[k] + j11 * deta[k]) / det; }
            }
            else
            {
                double twice = (px[1] - px[0]) * (py[2] - py[0]) - (px[2] - px[0]) * (py[1] - py[0]);
                dx[0] = (py[1] - py[2]) / twice; dx[1] = (py[2] - py[0]) / twice; dx[2] = (py[0] - py[1]) / twice;
                dy[0] = (px[2] - px[1]) / twice; dy[1] = (px[0] - px[2]) / twice; dy[2] = (px[1] - px[0]) / twice;
            }

            double exx = 0, eyy = 0, gxy = 0;
            for (int k = 0; k < p.Length; k++) { exx += dx[k] * ux[k]; eyy += dy[k] * uy[k]; gxy += dy[k] * ux[k] + dx[k] * uy[k]; }
            double exy = gxy / 2;

            // exx x(x) + eyy y(x)y + exy (x(x)y + y(x)x), kept as xx, yy, zz, xy, yz, zx.
            return new[]
            {
                exx * x.X * x.X + eyy * y.X * y.X + 2 * exy * x.X * y.X,
                exx * x.Y * x.Y + eyy * y.Y * y.Y + 2 * exy * x.Y * y.Y,
                exx * x.Z * x.Z + eyy * y.Z * y.Z + 2 * exy * x.Z * y.Z,
                exx * x.X * x.Y + eyy * y.X * y.Y + exy * (x.X * y.Y + y.X * x.Y),
                exx * x.Y * x.Z + eyy * y.Y * y.Z + exy * (x.Y * y.Z + y.Y * x.Z),
                exx * x.Z * x.X + eyy * y.Z * y.X + exy * (x.Z * y.X + y.Z * x.X),
            };
        }

        static double Along(double[] t, Vector3d a, Vector3d b) =>
            a.X * (t[0] * b.X + t[3] * b.Y + t[5] * b.Z) +
            a.Y * (t[3] * b.X + t[1] * b.Y + t[4] * b.Z) +
            a.Z * (t[5] * b.X + t[4] * b.Y + t[2] * b.Z);

        static double Strength(double[] t, Vector3d x, Vector3d y)
        {
            double a = Along(t, x, x), b = Along(t, y, y), s = Along(t, x, y);
            return Math.Sqrt(0.25 * (a - b) * (a - b) + s * s);
        }

        // ---------------------------------------------------------------- orientation

        /// <summary>
        /// Which faces to read the other way up so that every face agrees with its neighbours: two
        /// faces agree when they run along the edge they share in opposite directions. Within each
        /// connected piece the way most of its area already faces wins, so a mesh that is consistent
        /// is left as it is.
        /// </summary>
        static bool[] Orientation(IList<Point3d> nodes, List<int[]> faces, out int flipped)
        {
            int count = faces.Count;
            var area = faces.Select(face => Area(nodes, face)).ToArray();
            var edges = new Dictionary<(int, int), List<(int face, bool forward)>>();
            for (int f = 0; f < count; f++)
            {
                var face = faces[f];
                for (int k = 0; k < face.Length; k++)
                {
                    int a = face[k], b = face[(k + 1) % face.Length];
                    var key = a < b ? (a, b) : (b, a);
                    if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<(int, bool)>();
                    list.Add((f, a < b));
                }
            }
            var neighbours = new List<(int face, bool same)>[count];
            for (int f = 0; f < count; f++) neighbours[f] = new List<(int, bool)>();
            foreach (var list in edges.Values)
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        // Running the shared edge the same way means one of the two is upside down.
                        bool same = list[i].forward == list[j].forward;
                        neighbours[list[i].face].Add((list[j].face, same));
                        neighbours[list[j].face].Add((list[i].face, same));
                    }

            var flip = new bool[count];
            var seen = new bool[count];
            flipped = 0;
            for (int start = 0; start < count; start++)
            {
                if (seen[start]) continue;
                var piece = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(start); seen[start] = true;
                while (queue.Count > 0)
                {
                    int f = queue.Dequeue(); piece.Add(f);
                    foreach (var (g, same) in neighbours[f])
                    {
                        if (seen[g]) continue;
                        seen[g] = true;
                        flip[g] = flip[f] ^ same;
                        queue.Enqueue(g);
                    }
                }
                double turned = piece.Where(f => flip[f]).Sum(f => area[f]);
                double total = piece.Sum(f => area[f]);
                if (turned > total / 2)
                    foreach (var f in piece) flip[f] = !flip[f];
                flipped += piece.Count(f => flip[f]);
            }
            return flip;
        }
    }
}
