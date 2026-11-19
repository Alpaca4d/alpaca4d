using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Rhino.Geometry;

namespace Alpaca4d.Meshing
{
    /// <summary>
    /// A gmsh .geo script, written a line at a time, with the parts both meshers share: points, and
    /// the size fields of the refinements.
    /// </summary>
    internal sealed class GeoScript
    {
        /// <summary>Tags of the points the mesh must have a node at. Far above any a STEP file brings.</summary>
        public const int FirstInclusionPoint = 1_000_001;

        /// <summary>Tags of the points refinements measure their distance from. Never part of the geometry.</summary>
        public const int FirstRefinementPoint = 20_000_001;

        private readonly StringBuilder text = new StringBuilder();

        public GeoScript Line(string line)
        {
            text.Append(line).Append('\n');
            return this;
        }

        public static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        public static string Path(string file) => file.Replace("\\", "/").Replace("\"", "\\\"");

        /// <summary>Points at <paramref name="points"/>, tagged from <paramref name="first"/>; the tag range, as gmsh writes it.</summary>
        public string Points(IReadOnlyList<Point3d> points, int first)
        {
            for (int i = 0; i < points.Count; i++)
                Line($"Point({first + i}) = {{{F(points[i].X)}, {F(points[i].Y)}, {F(points[i].Z)}}};");
            return points.Count == 1 ? $"{first}" : $"{first}:{first + points.Count - 1}";
        }

        /// <summary>Tags of the points and curves of inclusion curves.</summary>
        public const int FirstCurvePoint = 3_000_001, FirstCurve = 1_000_001;

        /// <summary>
        /// The inclusion curves as gmsh curves: straight ones as lines from point to point, the
        /// others as smooth splines through their points. Not through STEP: Rhino writes a line on
        /// its own as an unbounded LINE, which gmsh then tries to mesh from end to end, forever.
        /// </summary>
        public List<int> Curves(IReadOnlyList<InclusionCurve> curves)
        {
            var tags = new List<int>();
            int point = FirstCurvePoint;
            int curve = FirstCurve;
            foreach (var c in curves)
            {
                // A closed curve ends on the point it starts from - the same tag, which is what makes
                // gmsh close a spline, not a second point in the same place.
                int n = c.Points.Count;
                bool closed = n > 2 && c.Points[0].DistanceTo(c.Points[n - 1]) < 1e-12;
                int distinct = closed ? n - 1 : n;
                int first = point;
                Points(c.Points.Take(distinct).ToList(), point);
                point += distinct;
                Func<int, int> tag = i => first + i % distinct;

                if (c.Straight)
                {
                    for (int i = 0; i + 1 < n; i++)
                    {
                        Line($"Line({curve}) = {{{tag(i)}, {tag(i + 1)}}};");
                        tags.Add(curve++);
                    }
                }
                else
                {
                    Line($"Spline({curve}) = {{{string.Join(", ", Enumerable.Range(0, n).Select(tag))}}};");
                    tags.Add(curve++);
                }
            }
            return tags;
        }

        /// <summary>
        /// The refinements as gmsh size fields: for each, a Distance from its points and a Threshold
        /// of it, from its Size at the geometry to <paramref name="meshSize"/> at its reach; the
        /// smallest of them is the background size. gmsh takes the smaller of that and the sizes the
        /// geometry was given, so each solid or surface keeps its own size away from them.
        /// </summary>
        public void Refinements(IReadOnlyList<MeshRefinement> refinements, double meshSize)
        {
            if (refinements == null || refinements.Count == 0)
                return;

            int point = FirstRefinementPoint;
            var thresholds = new List<int>();
            int field = 1;
            foreach (var refinement in refinements)
            {
                string range = Points(refinement.Samples, point);
                point += refinement.Samples.Count;

                int distance = field++;
                int threshold = field++;
                Line($"Field[{distance}] = Distance;");
                Line($"Field[{distance}].PointsList = {{{range}}};");
                Line($"Field[{threshold}] = Threshold;");
                Line($"Field[{threshold}].InField = {distance};");
                Line($"Field[{threshold}].SizeMin = {F(Math.Min(refinement.Size, meshSize))};");
                Line($"Field[{threshold}].SizeMax = {F(meshSize)};");
                Line($"Field[{threshold}].DistMin = 0;");
                Line($"Field[{threshold}].DistMax = {F(refinement.ReachFor(meshSize))};");
                thresholds.Add(threshold);
            }

            int smallest = field;
            Line($"Field[{smallest}] = Min;");
            Line($"Field[{smallest}].FieldsList = {{{string.Join(", ", thresholds)}}};");
            Line($"Background Field = {smallest};");
        }

        public override string ToString() => text.ToString();
    }

    /// <summary>
    /// A curve the mesh must have a line of edges along: straight segments from point to point, or
    /// a smooth curve through its points.
    /// </summary>
    public sealed class InclusionCurve
    {
        public IReadOnlyList<Point3d> Points { get; }
        public bool Straight { get; }

        public InclusionCurve(IEnumerable<Point3d> points, bool straight)
        {
            Points = points.ToList();
            Straight = straight;
            if (Points.Count < 2)
                throw new ArgumentException("A curve needs two points at least.");
        }
    }
}
