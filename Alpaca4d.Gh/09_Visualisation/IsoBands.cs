using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// A result drawn as iso-surfaces: one flat colour per band of values (see
    /// <see cref="ColourScale.Band"/>) in place of the colours blended across the range.
    ///
    /// Giving each corner the colour of its band is not enough, because the display blends the
    /// corner colours across the face, and a face with a corner in each of two bands comes out as
    /// the gradient between them. So each face is cut where its value crosses from one band into
    /// the next - the value running straight across it, as it does between the corners of a
    /// smooth plot - and each piece is drawn in its band's colour alone.
    ///
    /// Apart from View Results, and with nothing in it that needs Rhino running bar the mesh
    /// built at the end, so the cutting can be checked outside Rhino.
    /// </summary>
    internal static class IsoBands
    {
        /// <summary>
        /// <paramref name="mesh"/> cut into bands and coloured, one value per vertex, for
        /// DrawMeshFalseColors. No piece shares a vertex with its neighbour in another band, so
        /// no colour bleeds across an edge between bands.
        /// </summary>
        internal static Mesh Paint(Mesh mesh, IList<double> values, IList<Color> palette, double min, double max)
        {
            var banded = new Mesh();
            var colours = new List<Color>();
            int count = palette.Count;

            void Add(int band, List<Point3d> outline)
            {
                int first = banded.Vertices.Count;
                foreach (var point in outline)
                {
                    banded.Vertices.Add(point);
                    colours.Add(palette[band]);
                }

                for (int i = 1; i < outline.Count - 1; i++)
                    banded.Faces.AddFace(first, first + i, first + i + 1);
            }

            void Cut(Point3d[] corners, double[] at)
            {
                foreach (var piece in Split(corners, at, min, max, count))
                    Add(piece.Item1, piece.Item2);
            }

            foreach (var face in mesh.Faces)
            {
                var a = (Point3d)mesh.Vertices[face.A];
                var b = (Point3d)mesh.Vertices[face.B];
                var c = (Point3d)mesh.Vertices[face.C];
                double va = values[face.A], vb = values[face.B], vc = values[face.C];

                if (face.IsTriangle)
                {
                    Cut(new[] { a, b, c }, new[] { va, vb, vc });
                    continue;
                }

                var d = (Point3d)mesh.Vertices[face.D];
                double vd = values[face.D];

                int band = ColourScale.Band(va, min, max, count);
                if (ColourScale.Band(vb, min, max, count) == band && ColourScale.Band(vc, min, max, count) == band
                    && ColourScale.Band(vd, min, max, count) == band)
                {
                    Add(band, new List<Point3d> { a, b, c, d });
                    continue;
                }

                // Four triangles about the middle rather than two across a diagonal, which would
                // bend every band edge one way or the other depending on the diagonal picked.
                var middle = (a + b + c + d) * 0.25;
                double vm = (va + vb + vc + vd) * 0.25;
                Cut(new[] { a, b, middle }, new[] { va, vb, vm });
                Cut(new[] { b, c, middle }, new[] { vb, vc, vm });
                Cut(new[] { c, d, middle }, new[] { vc, vd, vm });
                Cut(new[] { d, a, middle }, new[] { vd, va, vm });
            }

            banded.VertexColors.SetColors(colours.ToArray());
            return banded;
        }

        /// <summary>
        /// One triangle cut into the bands its value runs through, given at its three corners:
        /// each piece's band and its outline, three to five corners in the triangle's own order.
        /// </summary>
        internal static List<Tuple<int, List<Point3d>>> Split(Point3d[] corners, double[] values, double min, double max, int count)
        {
            var pieces = new List<Tuple<int, List<Point3d>>>();

            int lowest = ColourScale.Band(values.Min(), min, max, count);
            int highest = ColourScale.Band(values.Max(), min, max, count);

            if (lowest == highest)
            {
                pieces.Add(Tuple.Create(lowest, corners.ToList()));
                return pieces;
            }

            var triangle = corners.Zip(values, Tuple.Create).ToList();

            for (int band = lowest; band <= highest; band++)
            {
                // The lowest band needs no floor and the highest no ceiling: everything is above
                // the one and below the other. Which also keeps out anything beyond the range,
                // where the first and last bands run on.
                var piece = triangle;
                if (band > lowest) piece = Clip(piece, ColourScale.Edge(band, min, max, count), above: true);
                if (band < highest) piece = Clip(piece, ColourScale.Edge(band + 1, min, max, count), above: false);

                var outline = Distinct(piece.Select(x => x.Item1).ToList());
                if (outline.Count >= 3)
                    pieces.Add(Tuple.Create(band, outline));
            }

            return pieces;
        }

        /// <summary>
        /// What of a polygon lies above <paramref name="level"/>, or below it, with the value
        /// running straight along each side: Sutherland-Hodgman against one level.
        /// </summary>
        private static List<Tuple<Point3d, double>> Clip(List<Tuple<Point3d, double>> polygon, double level, bool above)
        {
            var kept = new List<Tuple<Point3d, double>>();

            bool Inside(Tuple<Point3d, double> x) => above ? x.Item2 >= level : x.Item2 <= level;

            for (int i = 0; i < polygon.Count; i++)
            {
                var from = polygon[i];
                var to = polygon[(i + 1) % polygon.Count];
                bool fromIn = Inside(from), toIn = Inside(to);

                if (fromIn != toIn)
                {
                    // Weighted from both ends, so a crossing at either end is that corner exactly
                    // and Distinct can take it out.
                    double t = (level - from.Item2) / (to.Item2 - from.Item2);
                    kept.Add(Tuple.Create(from.Item1 * (1.0 - t) + to.Item1 * t, level));
                }

                if (toIn) kept.Add(to);
            }

            return kept;
        }

        /// <summary>The outline without a corner repeated next to itself, which a band edge through a corner leaves.</summary>
        private static List<Point3d> Distinct(List<Point3d> outline)
        {
            var distinct = new List<Point3d>();
            foreach (var point in outline)
                if (distinct.Count == 0 || distinct[distinct.Count - 1] != point)
                    distinct.Add(point);

            while (distinct.Count > 1 && distinct[0] == distinct[distinct.Count - 1])
                distinct.RemoveAt(distinct.Count - 1);

            return distinct;
        }

        /// <summary>
        /// A value given at points along a line, from 0 at its start to 1 at its end, as stretches
        /// of one band each: where each starts, where it ends, and its band. The value runs straight
        /// between the points, so a stretch ends where the value crosses into the next band.
        /// </summary>
        internal static List<Tuple<double, double, int>> Along(IList<Tuple<double, double>> samples, double min, double max, int count)
        {
            var stretches = new List<Tuple<double, double, int>>();
            if (samples.Count == 0) return stretches;

            double from = samples[0].Item1;
            int band = ColourScale.Band(samples[0].Item2, min, max, count);

            for (int i = 1; i < samples.Count; i++)
            {
                double ta = samples[i - 1].Item1, va = samples[i - 1].Item2;
                double tb = samples[i].Item1, vb = samples[i].Item2;
                int next = ColourScale.Band(vb, min, max, count);

                // A band changing means the value did, so there is no dividing by zero.
                while (band != next)
                {
                    int step = next > band ? 1 : -1;
                    double edge = ColourScale.Edge(step > 0 ? band + 1 : band, min, max, count);
                    double t = ta + (tb - ta) * (edge - va) / (vb - va);
                    t = Math.Max(from, Math.Min(tb, t));

                    stretches.Add(Tuple.Create(from, t, band));
                    from = t;
                    band += step;
                }
            }

            stretches.Add(Tuple.Create(from, samples[samples.Count - 1].Item1, band));
            return stretches;
        }
    }
}
