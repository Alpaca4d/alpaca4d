// What Legend writes beside each colour, against what View Results paints.
//
// Legend is fed the palette and the values View Results drew, and has to put each label where
// the model shows that colour. It used to label the edges of N equal bands, while View Results
// puts colour k at min + k * span / (N - 1) - so the two read up to a colour apart, and nothing
// looked wrong. Both now go through ColourScale, and the labels are pinned here against the
// stops View Results paints by.
//
// Also the label format, which printed any displacement in metres as 0.00, and the preset
// gradients, which stopped short of their own top colour.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;

class LegendTest
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

    /// <summary>ColourScale is internal to the Grasshopper assembly, so it is reached by reflection.</summary>
    static MethodInfo LabelValues, LabelFormat, ScaleStops, Gradient;

    static List<double> Values(double min, double max, int count)
    {
        return (List<double>)LabelValues.Invoke(null, new object[] { min, max, count });
    }

    static string Format(double value, double min, double max)
    {
        return (string)LabelFormat.Invoke(null, new object[] { value, min, max });
    }

    static string Labels(double min, double max, int count)
    {
        return string.Join(", ", Values(min, max, count).Select(x => Format(x, min, max)));
    }

    static List<double> Stops(List<Color> palette, double min, double max)
    {
        var gradient = (SortedDictionary<double, Color>)ScaleStops.Invoke(null, new object[] { palette, min, max });
        return gradient.Keys.ToList();
    }

    static List<Color> Preset(int index, int step = 10)
    {
        return (List<Color>)Gradient.Invoke(null, new object[] { index, step });
    }

    static void Main(string[] args)
    {
        var gh = Assembly.LoadFrom(args.Length > 0 ? args[0] : "Alpaca4d.Gh.gha");
        var core = Assembly.LoadFrom(args.Length > 1 ? args[1] : "Alpaca4d.Core.dll");

        var scale = gh.GetType("Alpaca4d.Gh.ColourScale", true);
        LabelValues = scale.GetMethod("LabelValues", BindingFlags.NonPublic | BindingFlags.Static);
        LabelFormat = scale.GetMethod("Format", BindingFlags.NonPublic | BindingFlags.Static);
        ScaleStops = scale.GetMethod("Stops", BindingFlags.NonPublic | BindingFlags.Static);
        Gradient = core.GetType("Alpaca4d.Colors", true).GetMethod("Gradient", BindingFlags.Public | BindingFlags.Static);

        Section("Each label sits where View Results puts its colour");
        {
            var ranges = new[]
            {
                Tuple.Create(0.0, 9.0),
                Tuple.Create(-1.0, 1.0),
                Tuple.Create(-0.0034, 0.0012),
                Tuple.Create(0.0, 2.5e8),
                Tuple.Create(-350.0, -20.0),
            };

            foreach (int count in new[] { 2, 3, 10, 17 })
            {
                var palette = Enumerable.Range(0, count).Select(k => Color.FromArgb(k, k, k)).ToList();
                foreach (var range in ranges)
                {
                    var values = Values(range.Item1, range.Item2, count);
                    var stops = Stops(palette, range.Item1, range.Item2);
                    double tolerance = 1e-9 * (range.Item2 - range.Item1);

                    Check(values.Count == stops.Count && values.Zip(stops, (a, b) => Math.Abs(a - b) <= tolerance).All(x => x),
                          $"{count} colours over [{range.Item1}, {range.Item2}]: {Labels(range.Item1, range.Item2, count)}");
                }
            }
        }

        Section("The values View Results hands Legend end on its range exactly");
        {
            // Legend labels from the smallest of its Values to the largest, so the ends have to be
            // the range View Results painted over, not min + span rounded a hair past it.
            foreach (var range in new[] { Tuple.Create(-0.0034, 0.0012), Tuple.Create(-0.3, 0.1), Tuple.Create(0.07, 0.31) })
            {
                var values = new[] { 2, 3, 10, 17 }.Select(count => Values(range.Item1, range.Item2, count)).ToList();
                Check(values.All(x => x.First() == range.Item1 && x.Last() == range.Item2),
                      $"[{range.Item1}, {range.Item2}] in 2, 3, 10 and 17 colours ends on {string.Join(", ", values.Select(x => x.Last().ToString("R")))}");
            }
        }

        Section("A flat range is one swatch, at the value the model shows in the first colour");
        {
            var palette = Enumerable.Range(0, 10).Select(k => Color.FromArgb(k, k, k)).ToList();
            var values = Values(5.0, 5.0, 10);
            var stops = Stops(palette, 5.0, 5.0);

            Check(values.Count == 1 && values[0] == 5.0, $"one value, 5 ({values.Count} values)");
            Check(stops[0] == 5.0, "View Results puts the first colour at that same value");
        }

        Section("A range across zero labels zero as 0, not as rounding left it");
        {
            var values = Values(-1.0, 1.0, 11);
            Check(Format(values[5], -1.0, 1.0) == "0", $"the middle of eleven over [-1, 1] reads {Format(values[5], -1.0, 1.0)}");

            values = Values(-2.5e8, 2.5e8, 11);
            Check(Format(values[5], -2.5e8, 2.5e8) == "0", $"the middle of eleven over [-2.5e8, 2.5e8] reads {Format(values[5], -2.5e8, 2.5e8)}");

            Check(Format(-0.0002, -1.0, 1.0) == "0", $"and a value below the legend's step reads 0, not -0: {Format(-0.0002, -1.0, 1.0)}");
        }

        Section("Every label of a legend reads to the same step, four figures of its larger end");
        {
            Check(Labels(-0.0034, 0.0012, 17).Split(',').All(x => !x.Contains("E")),
                  $"a displacement in metres stays in decimals throughout: {Labels(-0.0034, 0.0012, 17)}");
            Check(Format(0.00005, -0.0034, 0.0012) == "0.00005", $"so the label near zero reads 0.00005, not 5E-05: {Format(0.00005, -0.0034, 0.0012)}");
            Check(Format(0.0012, 0.0, 0.0012) == "0.0012", $"a displacement in metres: 0.0012 reads {Format(0.0012, 0.0, 0.0012)}");
            Check(Labels(-1.0, 1.0, 10) == "-1, -0.778, -0.556, -0.333, -0.111, 0.111, 0.333, 0.556, 0.778, 1",
                  $"over [-1, 1] to three decimals, ends without trailing zeros: {Labels(-1.0, 1.0, 10)}");
            Check(Labels(-350.0, -20.0, 4) == "-350, -240, -130, -20", $"over [-350, -20]: {Labels(-350.0, -20.0, 4)}");
            Check(Labels(0.0, 12345.0, 3) == "0, 6173, 12345", $"over [0, 12345] in whole numbers: {Labels(0.0, 12345.0, 3)}");
            Check(Format(1.5e-5, 0.0, 1.5e-5) == "1.5E-05", $"a rotation in radians: 1.5e-5 reads {Format(1.5e-5, 0.0, 1.5e-5)}");
            Check(Labels(0.0, 2.5e8, 4) == "0, 8.333E+07, 1.667E+08, 2.5E+08", $"a stress in pascals: {Labels(0.0, 2.5e8, 4)}");
            Check(Format(5.0, 5.0, 5.0) == "5" && Format(0.0, 0.0, 0.0) == "0", "a flat range reads its one value");
        }

        Section("Every preset gradient runs from its own first colour to its own last");
        {
            for (int index = 0; index <= 17; index++)
            {
                var all = Preset(index, 101);
                var ten = Preset(index);
                Check(ten.Count == 10 && ten[0] == all[0] && ten[9] == all[100],
                      $"gradient {index}: {ten.Count} colours, ends {ten[0].R},{ten[0].G},{ten[0].B} and {ten[9].R},{ten[9].G},{ten[9].B}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
