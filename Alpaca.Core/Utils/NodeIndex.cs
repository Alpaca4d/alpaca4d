using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Alpaca4d
{
    /// <summary>
    /// The nodes of a model by where they stand, for finding the node at a point. Space is cut
    /// into cubes two tolerances wide, and each cube that holds a node is hashed: a node within
    /// tolerance of a point lies in one of the eight cubes on the point's side of its own, so a
    /// search costs the same however large the model is. Point3d.CullDuplicates, which this
    /// replaces, compared every point with every point kept before it, and an RTree search is a
    /// round trip into Rhino's native code with a callback for every hit.
    /// </summary>
    public sealed class NodeIndex
    {
        private readonly double _tolerance;
        private readonly double _toleranceSquared;
        private readonly double _cube;

        /// <summary>The first node in each cube that has one; the rest follow through <see cref="_next"/>.</summary>
        private readonly Dictionary<Cube, int> _first = new Dictionary<Cube, int>();

        /// <summary>The next node in the same cube, -1 for none.</summary>
        private readonly List<int> _next = new List<int>();

        /// <summary>The nodes, in the order they were added: a node's index is its place here.</summary>
        public List<Point3d> Points { get; } = new List<Point3d>();

        public double Tolerance => _tolerance;

        public int Count => Points.Count;

        /// <param name="tolerance">How far apart two points can be and still be one node. Zero, or
        /// anything not above it, makes only identical points one node.</param>
        public NodeIndex(double tolerance)
        {
            _tolerance = tolerance > 0.0 ? tolerance : 0.0;
            _toleranceSquared = _tolerance * _tolerance;
            _cube = _tolerance > 0.0 ? 2.0 * _tolerance : 1.0;
        }

        /// <summary>A node at <paramref name="point"/>, whatever already stands near it; its index.</summary>
        public int Add(Point3d point)
        {
            int index = Points.Count;
            var cube = CubeOf(point.X, point.Y, point.Z);
            _next.Add(_first.TryGetValue(cube, out int first) ? first : -1);
            _first[cube] = index;
            Points.Add(point);
            return index;
        }

        /// <summary>
        /// The node within tolerance of <paramref name="point"/>, or a new one there when there is
        /// none: what Point3d.CullDuplicates keeps, in the same order, for points added in turn.
        /// </summary>
        public int Weld(Point3d point)
        {
            int found = Nearest(point);
            return found >= 0 ? found : Add(point);
        }

        /// <summary>The node nearest <paramref name="point"/> within tolerance; -1 when there is none.</summary>
        public int Nearest(Point3d point)
        {
            int found = -1;
            if (Points.Count == 0)
                return found;

            double best = double.MaxValue;
            var cubes = Around(point);
            for (int c = 0; c < cubes.Length; c++)
            {
                if (!_first.TryGetValue(cubes[c], out int index))
                    continue;
                for (; index >= 0; index = _next[index])
                {
                    double distanceSquared = DistanceSquared(Points[index], point);
                    if (distanceSquared <= _toleranceSquared && distanceSquared < best)
                    {
                        best = distanceSquared;
                        found = index;
                    }
                }
            }
            return found;
        }

        /// <summary>Every node within tolerance of <paramref name="point"/>, in index order.</summary>
        public List<int> Within(Point3d point)
        {
            var found = new List<int>();
            if (Points.Count == 0)
                return found;

            foreach (var cube in Around(point))
            {
                if (!_first.TryGetValue(cube, out int index))
                    continue;
                for (; index >= 0; index = _next[index])
                    if (DistanceSquared(Points[index], point) <= _toleranceSquared)
                        found.Add(index);
            }
            found.Sort();
            return found;
        }

        /// <summary>
        /// The eight cubes a node within tolerance of <paramref name="point"/> can be in: its own,
        /// and on each axis the neighbour on the side of the cube's middle the point is past. The
        /// tolerance is half a cube, so it reaches no further.
        /// </summary>
        private Cube[] Around(Point3d point)
        {
            double x = point.X / _cube, y = point.Y / _cube, z = point.Z / _cube;
            long cx = (long)Math.Floor(x), cy = (long)Math.Floor(y), cz = (long)Math.Floor(z);
            long sx = x - cx < 0.5 ? -1 : 1, sy = y - cy < 0.5 ? -1 : 1, sz = z - cz < 0.5 ? -1 : 1;
            var cubes = _around;
            for (int i = 0; i < 8; i++)
                cubes[i] = new Cube(cx + ((i & 1) != 0 ? sx : 0), cy + ((i & 2) != 0 ? sy : 0), cz + ((i & 4) != 0 ? sz : 0));
            return cubes;
        }

        private readonly Cube[] _around = new Cube[8];

        private static double DistanceSquared(Point3d a, Point3d b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private Cube CubeOf(double x, double y, double z)
        {
            return new Cube((long)Math.Floor(x / _cube), (long)Math.Floor(y / _cube), (long)Math.Floor(z / _cube));
        }

        private readonly struct Cube : IEquatable<Cube>
        {
            private readonly long _x, _y, _z;

            public Cube(long x, long y, long z)
            {
                _x = x;
                _y = y;
                _z = z;
            }

            public bool Equals(Cube other) => _x == other._x && _y == other._y && _z == other._z;

            public override bool Equals(object obj) => obj is Cube other && Equals(other);

            public override int GetHashCode() => unchecked((int)(_x * 73856093L ^ _y * 19349663L ^ _z * 83492791L));
        }
    }
}
