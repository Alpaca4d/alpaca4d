using System;
using System.Collections.Generic;

using Rhino.Geometry;

namespace Alpaca4d.Result
{
    /// <summary>Points bucketed in cubes, to ask cheaply whether any lies within a distance.</summary>
    internal sealed class PointHash
    {
        readonly double _cell;
        readonly Dictionary<(long, long, long), List<(Point3d p, int n)>> _cells = new Dictionary<(long, long, long), List<(Point3d, int)>>();
        int _count;

        public PointHash(double cell) { _cell = cell; }

        (long, long, long) Key(Point3d p) => ((long)Math.Floor(p.X / _cell), (long)Math.Floor(p.Y / _cell), (long)Math.Floor(p.Z / _cell));

        public void Add(Point3d p)
        {
            var key = Key(p);
            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<(Point3d, int)>();
            list.Add((p, _count++));
        }

        /// <summary>Whether a point lies within <paramref name="radius"/>, leaving out the last <paramref name="skipLast"/> added.</summary>
        public bool AnyWithin(Point3d p, double radius, int skipLast = 0)
        {
            int reach = (int)Math.Ceiling(radius / _cell);
            var (cx, cy, cz) = Key(p);
            double r2 = radius * radius;
            for (long i = cx - reach; i <= cx + reach; i++)
                for (long j = cy - reach; j <= cy + reach; j++)
                    for (long k = cz - reach; k <= cz + reach; k++)
                    {
                        if (!_cells.TryGetValue((i, j, k), out var list)) continue;
                        foreach (var (q, n) in list)
                            if (n < _count - skipLast && (q - p).SquareLength < r2) return true;
                    }
            return false;
        }
    }
}
