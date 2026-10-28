// Iso-surfaces in View Results: each colour one band of values, drawn flat.
//
// Snapping each corner to its band's colour would not do it - the display blends corner colours
// across a face - so View Results cuts every face where its value crosses into the next band and
// draws each piece in one colour. What is checked here is that cutting: that the pieces cover
// the face exactly, that each lies inside its band, and that beams are cut the same way. And
// that the band edges View Results hands Legend are where its colours change.
//
// Each test triangle stands its value up as its z, so a point's value can be read off the point.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Rhino.Geometry;

class IsoSurfacesTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    /// <summary>ColourScale and IsoBands are internal to the Grasshopper assembly, so they are reached by reflection.</summary>
    static MethodInfo BandOf, EdgeOf, LabelValues, SplitOf, AlongOf;

    static int Band(double value, double min, double max, int count)
    {
        return (int)BandOf.Invoke(null, new object[] { value, min, max, count });
    }

    static double Edge(int k, double min, double max, int count)
    {
        return (double)EdgeOf.Invoke(null, new object[] { k, min, max, count });
    }

    static List<double> Values(double min, double max, int count)
    {
        return (List<double>)LabelValues.Invoke(null, new object[] { min, max, count });
    }

    static List<Tuple<int, List<Point3d>>> Split(Point3d[] corners, double min, double max, int count)
    {
        var values = corners.Select(x => x.Z).ToArray();
        return (List<Tuple<int, List<Point3d>>>)SplitOf.Invoke(null, new object[] { corners, values, min, max, count });
    }

    static List<Tuple<double, double, int>> Along(List<Tuple<double, double>> samples, double min, double max, int count)
    {
        return (List<Tuple<double, double, int>>)AlongOf.Invoke(null, new object[] { samples, min, max, count });
    }

    /// <summary>The area of a flat polygon in space.</summary>
    static double Area(IList<Point3d> outline)
    {
        double x = 0, y = 0, z = 0;
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            x += a.Y * b.Z - a.Z * b.Y;
            y += a.Z * b.X - a.X * b.Z;
            z += a.X * b.Y - a.Y * b.X;
        }
        return 0.5 * Math.Sqrt(x * x + y * y + z * z);
    }

    /// <summary>Whether a value is in band k, give or take a hair; the first and last bands run on past the range.</summary>
    static bool InBand(double value, int k, double min, double max, int count)
    {
        double hair = 1e-9 * Math.Max(1.0, max - min);
        double low = k == 0 ? double.NegativeInfinity : Edge(k, min, max, count);
        double high = k == count - 1 ? double.PositiveInfinity : Edge(k + 1, min, max, count);
        return value >= low - hair && value <= high + hair;
    }

    static void Main(string[] args)
    {
        var gh = Assembly.LoadFrom(args.Length > 0 ? args[0] : "Alpaca4d.Gh.gha");
        var scale = gh.GetType("Alpaca4d.Gh.ColourScale", true);
        var bands = gh.GetType("Alpaca4d.Gh.IsoBands", true);
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        BandOf = scale.GetMethod("Band", flags);
        EdgeOf = scale.GetMethod("Edge", flags);
        LabelValues = scale.GetMethod("LabelValues", flags);
        SplitOf = bands.GetMethod("Split", flags);
        AlongOf = bands.GetMethod("Along", flags);

        Section("Each value falls in one of as many equal bands as there are colours");
        {
            Check(Band(0.0, 0.0, 10.0, 5) == 0 && Band(1.99, 0.0, 10.0, 5) == 0, "[0, 2) of [0, 10] in 5 bands is the first");
            Check(Band(2.0, 0.0, 10.0, 5) == 1 && Band(5.0, 0.0, 10.0, 5) == 2, "2 starts the second, 5 is in the third");
            Check(Band(10.0, 0.0, 10.0, 5) == 4, "the top of the range is in the last band, not one past it");
            Check(Band(-3.0, 0.0, 10.0, 5) == 0 && Band(13.0, 0.0, 10.0, 5) == 4,
                  "below the range is the first band and above it the last, as the gradient holds its end colours");
            Check(Band(5.0, 5.0, 5.0, 10) == 0, "a flat range is all the first band, as it is all the first colour");
            Check(Band(double.NaN, 0.0, 10.0, 5) == 0, "NaN is the first band rather than an exception");
            Check(Band(-0.5, -1.0, 0.0, 4) == 2, "a negative range: -0.5 of [-1, 0] in 4 bands is the third");
        }

        Section("The edges View Results hands Legend are where its colours change");
        {
            foreach (var range in new[] { Tuple.Create(0.0, 9.0), Tuple.Create(-0.0034, 0.0012), Tuple.Create(-350.0, -20.0), Tuple.Create(0.0, 2.5e8) })
            {
                foreach (int count in new[] { 2, 3, 10, 17 })
                {
                    double min = range.Item1, max = range.Item2;
                    double hair = 1e-9 * (max - min);
                    var edges = Values(min, max, count + 1);

                    bool placed = edges.Count == count + 1 && edges.First() == min && edges.Last() == max
                                  && Enumerable.Range(0, count + 1).All(k => Math.Abs(edges[k] - Edge(k, min, max, count)) <= hair);
                    bool changes = Enumerable.Range(1, count - 1).All(k => Band(edges[k] - hair, min, max, count) == k - 1
                                                                          && Band(edges[k] + hair, min, max, count) == k);

                    Check(placed && changes, $"{count} bands over [{min}, {max}]: {count + 1} edges, from min to max exactly, the colour changing at each");
                }
            }

            var flat = Values(5.0, 5.0, 11);
            Check(flat.Count == 1 && flat[0] == 5.0, "a flat range is still one value, which Legend draws as one swatch");
        }

        Section("A triangle is cut into its bands, which cover it exactly");
        {
            var random = new Random(7);
            int triangles = 0, covered = 0, inside = 0, ordered = 0, solid = 0;

            foreach (var range in new[] { Tuple.Create(0.0, 10.0), Tuple.Create(-1.0, 1.0), Tuple.Create(2.0, 3.0) })
            {
                foreach (int count in new[] { 2, 5, 10 })
                {
                    for (int n = 0; n < 200; n++)
                    {
                        double min = range.Item1, max = range.Item2, span = max - min;

                        // Values from a little below the range to a little above, so the open
                        // ends of the first and last bands are crossed too.
                        Func<double> value = () => min - 0.2 * span + 1.4 * span * random.NextDouble();
                        var corners = Enumerable.Range(0, 3)
                                                .Select(_ => new Point3d(random.NextDouble() * 4, random.NextDouble() * 4, value()))
                                                .ToArray();
                        if (Area(corners) < 1e-3) continue;

                        triangles++;
                        var pieces = Split(corners, min, max, count);

                        if (Math.Abs(pieces.Sum(x => Area(x.Item2)) - Area(corners)) <= 1e-9 * Area(corners)) covered++;
                        if (pieces.All(x => x.Item2.All(p => InBand(p.Z, x.Item1, min, max, count)))) inside++;
                        if (pieces.Zip(pieces.Skip(1), (a, b) => b.Item1 == a.Item1 + 1).All(x => x)
                            && pieces.First().Item1 == Band(corners.Min(p => p.Z), min, max, count)
                            && pieces.Last().Item1 == Band(corners.Max(p => p.Z), min, max, count)) ordered++;
                        if (pieces.All(x => x.Item2.Count >= 3 && x.Item2.Count <= 5 && Area(x.Item2) > 0.0)) solid++;
                    }
                }
            }

            Check(covered == triangles, $"the pieces' areas add up to the triangle's: {covered} of {triangles}");
            Check(inside == triangles, $"every corner of a piece is inside its band: {inside} of {triangles}");
            Check(ordered == triangles, $"one piece per band from the lowest corner's to the highest's, none skipped: {ordered} of {triangles}");
            Check(solid == triangles, $"every piece has three to five corners and some area: {solid} of {triangles}");
        }

        Section("A triangle in one band is left whole");
        {
            var corners = new[] { new Point3d(0, 0, 2.1), new Point3d(1, 0, 2.5), new Point3d(0, 1, 2.9) };
            var pieces = Split(corners, 0.0, 10.0, 5);
            Check(pieces.Count == 1 && pieces[0].Item1 == 1 && pieces[0].Item2.SequenceEqual(corners), "one piece, the triangle itself, in the second band");
        }

        Section("An edge through a corner leaves no sliver");
        {
            // 4 is the edge between the second band and the third of [0, 10] in 5.
            var corners = new[] { new Point3d(0, 0, 4.0), new Point3d(1, 0, 6.0), new Point3d(0, 1, 7.0) };
            var pieces = Split(corners, 0.0, 10.0, 5);
            Check(pieces.All(x => x.Item2.Count >= 3 && Area(x.Item2) > 0.0), $"{pieces.Count} piece(s), none of them flat");
            Check(Math.Abs(pieces.Sum(x => Area(x.Item2)) - Area(corners)) <= 1e-12, "and they still cover it");

            corners = new[] { new Point3d(0, 0, 4.0), new Point3d(1, 0, 7.0), new Point3d(0, 1, 2.0) };
            pieces = Split(corners, 0.0, 10.0, 5);
            Check(pieces.Count == 3 && pieces.Select(x => x.Item1).SequenceEqual(new[] { 1, 2, 3 }),
                  $"a corner on an edge with the others either side: bands {string.Join(", ", pieces.Select(x => x.Item1))}");
        }

        Section("A beam is cut into stretches of one band, where its value crosses an edge");
        {
            // Up from 0 to 10 over the first half and down to 3 over the second, in 5 bands of 2.
            var samples = new List<Tuple<double, double>> { Tuple.Create(0.0, 0.0), Tuple.Create(0.5, 10.0), Tuple.Create(1.0, 3.0) };
            var stretches = Along(samples, 0.0, 10.0, 5);

            Check(stretches.Select(x => x.Item3).SequenceEqual(new[] { 0, 1, 2, 3, 4, 3, 2, 1 }),
                  $"bands up and back down: {string.Join(", ", stretches.Select(x => x.Item3))}");
            Check(stretches.First().Item1 == 0.0 && stretches.Last().Item2 == 1.0
                  && stretches.Zip(stretches.Skip(1), (a, b) => a.Item2 == b.Item1).All(x => x),
                  "the stretches run end to end from 0 to 1");
            Check(Math.Abs(stretches[0].Item2 - 0.1) < 1e-12 && Math.Abs(stretches[1].Item2 - 0.2) < 1e-12,
                  $"cut where the value crosses 2 and 4, at 0.1 and 0.2: {stretches[0].Item2:R}, {stretches[1].Item2:R}");

            // On the way down, 10 to 3 over 0.5: the value crosses 8 at 0.5 + 0.5 * 2 / 7.
            Check(Math.Abs(stretches[4].Item2 - (0.5 + 0.5 * 2.0 / 7.0)) < 1e-12,
                  $"and 8 on the way down at {0.5 + 0.5 * 2.0 / 7.0:G6}: {stretches[4].Item2:G6}");

            var still = Along(new List<Tuple<double, double>> { Tuple.Create(0.0, 2.5), Tuple.Create(0.4, 3.0), Tuple.Create(1.0, 2.2) }, 0.0, 10.0, 5);
            Check(still.Count == 1 && still[0].Item1 == 0.0 && still[0].Item2 == 1.0 && still[0].Item3 == 1,
                  "a beam in one band is one stretch, the whole beam");
        }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
