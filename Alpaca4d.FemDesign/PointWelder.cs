using System;
using System.Collections.Generic;

using Rhino.Geometry;

namespace Alpaca4d.FemDesign
{
    /// <summary>
    /// Numbers points so that two within a tolerance of each other get the same number, as Assemble Model
    /// takes them to be one node. The first point given a number is the one kept.
    /// </summary>
    internal sealed class PointWelder
    {
        private readonly double tolerance;
        private readonly Dictionary<(long, long, long), List<int>> cells = new Dictionary<(long, long, long), List<int>>();

        public PointWelder(double tolerance)
        {
            this.tolerance = tolerance > 0 ? tolerance : 1e-6;
        }

        public List<Point3d> Points { get; } = new List<Point3d>();

        /// <summary>The number of the point at <paramref name="point"/>, a new one if there is none.</summary>
        public int Weld(Point3d point)
        {
            int found = Find(point);
            if (found >= 0)
                return found;

            int index = Points.Count;
            Points.Add(point);
            var cell = Cell(point);
            if (!cells.TryGetValue(cell, out var list))
                cells[cell] = list = new List<int>();
            list.Add(index);
            return index;
        }

        /// <summary>The number of the point at <paramref name="point"/>, or -1 if there is none.</summary>
        public int Find(Point3d point)
        {
            var (cx, cy, cz) = Cell(point);
            int best = -1;
            double bestDistance = tolerance;
            for (long x = cx - 1; x <= cx + 1; x++)
                for (long y = cy - 1; y <= cy + 1; y++)
                    for (long z = cz - 1; z <= cz + 1; z++)
                    {
                        if (!cells.TryGetValue((x, y, z), out var list))
                            continue;
                        foreach (int index in list)
                        {
                            double distance = Points[index].DistanceTo(point);
                            if (distance <= bestDistance)
                            {
                                best = index;
                                bestDistance = distance;
                            }
                        }
                    }
            return best;
        }

        private (long, long, long) Cell(Point3d p)
            => ((long)Math.Floor(p.X / tolerance), (long)Math.Floor(p.Y / tolerance), (long)Math.Floor(p.Z / tolerance));
    }
}
