using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// The density filter of Bruns and Tortorelli (2001), as top3d applies it: an element's physical
    /// density is the weighted mean of the design densities around it, the weight falling linearly
    /// from the radius R at the element's own centroid to nothing at R. Without it the optimum is a
    /// checkerboard, and finer meshes find finer and finer structure.
    ///
    /// top3d's mesh is a grid of equal cubes. Here elements differ in size, so each neighbour also
    /// counts by its volume: x~_i = sum_j H_ij v_j x_j / sum_j H_ij v_j, with H_ij = max(0, R - d_ij).
    /// On equal elements the volumes cancel and it is top3d's filter exactly.
    ///
    /// Neighbours are found through a grid of cubes the size of R, so building it is linear in the
    /// number of elements, whatever their shape - the neighbour search the tetrahedra of a Brep need.
    /// </summary>
    public sealed class DensityFilter
    {
        private readonly int[] _start;
        private readonly int[] _column;
        private readonly double[] _weight;   // H_ij v_j / sum_k H_ik v_k
        public double Radius { get; }
        public int Count { get; }

        /// <summary>The largest number of neighbours any element has, itself included.</summary>
        public int MaxNeighbours { get; }

        public DensityFilter(IReadOnlyList<SolidCell> cells, double radius)
        {
            if (!(radius > 0.0))
                throw new ArgumentException("The filter radius has to be positive.");

            Radius = radius;
            Count = cells.Count;

            var grid = new Dictionary<(long, long, long), List<int>>();
            (long, long, long) Key(Rhino.Geometry.Point3d p) =>
                ((long)Math.Floor(p.X / radius), (long)Math.Floor(p.Y / radius), (long)Math.Floor(p.Z / radius));

            for (int i = 0; i < cells.Count; i++)
            {
                var key = Key(cells[i].Centroid);
                if (!grid.TryGetValue(key, out var bucket))
                    grid[key] = bucket = new List<int>();
                bucket.Add(i);
            }

            _start = new int[cells.Count + 1];
            var column = new List<int>(cells.Count * 20);
            var weight = new List<double>(cells.Count * 20);
            var row = new List<(int j, double h)>();

            for (int i = 0; i < cells.Count; i++)
            {
                var centre = cells[i].Centroid;
                var (kx, ky, kz) = Key(centre);
                row.Clear();

                for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                for (long dz = -1; dz <= 1; dz++)
                {
                    if (!grid.TryGetValue((kx + dx, ky + dy, kz + dz), out var bucket))
                        continue;
                    foreach (int j in bucket)
                    {
                        double h = radius - centre.DistanceTo(cells[j].Centroid);
                        if (h > 0.0)
                            row.Add((j, h * cells[j].Volume));
                    }
                }

                // In the order of the elements, so a run does not depend on how the grid hashes.
                row.Sort((a, b) => a.j.CompareTo(b.j));
                double sum = row.Sum(entry => entry.h);
                foreach (var (j, h) in row)
                {
                    column.Add(j);
                    weight.Add(h / sum);
                }
                _start[i + 1] = column.Count;
                MaxNeighbours = Math.Max(MaxNeighbours, row.Count);
            }

            _column = column.ToArray();
            _weight = weight.ToArray();
        }

        /// <summary>The physical densities of the design densities <paramref name="x"/>.</summary>
        public double[] Apply(double[] x)
        {
            var result = new double[Count];
            for (int i = 0; i < Count; i++)
            {
                double sum = 0.0;
                for (int k = _start[i]; k < _start[i + 1]; k++)
                    sum += _weight[k] * x[_column[k]];
                result[i] = sum;
            }
            return result;
        }

        /// <summary>
        /// The chain rule through the filter: from the derivatives of something by the physical
        /// densities, its derivatives by the design densities. The transpose of <see cref="Apply"/>.
        /// </summary>
        public double[] Back(double[] byPhysical)
        {
            var result = new double[Count];
            for (int i = 0; i < Count; i++)
            {
                double g = byPhysical[i];
                if (g == 0.0)
                    continue;
                for (int k = _start[i]; k < _start[i + 1]; k++)
                    result[_column[k]] += _weight[k] * g;
            }
            return result;
        }

        /// <summary>
        /// top3d's rmin of 1.5 elements, in model units: 1.5 times the edge of a cube of the elements'
        /// mean volume. On a grid of cubes that is 1.5 elements exactly.
        /// </summary>
        public static double DefaultRadius(IReadOnlyList<SolidCell> cells)
        {
            double mean = cells.Average(cell => cell.Volume);
            return 1.5 * Math.Pow(mean, 1.0 / 3.0);
        }
    }
}
