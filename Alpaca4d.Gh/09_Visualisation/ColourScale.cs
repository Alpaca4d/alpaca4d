using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Where each colour of a gradient sits on the values, for View Results, which paints by it,
    /// and Legend, which labels by it. In one place because the two have to agree, and because
    /// it has no Grasshopper in it, so it can be checked outside Rhino.
    /// </summary>
    internal static class ColourScale
    {
        /// <summary>
        /// The palette laid over [<paramref name="min"/>, <paramref name="max"/>]: the first colour
        /// at min, the last at max, the rest evenly between. Values in between blend the two
        /// colours either side (see Colors.GetColor).
        /// </summary>
        internal static SortedDictionary<double, Color> Stops(List<Color> palette, double min, double max)
        {
            var gradient = new SortedDictionary<double, Color>();

            // A flat field - every value the same, which an unloaded component gives - would
            // otherwise divide by zero and colour nothing.
            double span = max - min;
            double step = span > 0.0 ? span / (palette.Count - 1) : 1.0;
            double at = min;

            foreach (var colour in palette)
            {
                if (!gradient.ContainsKey(at))
                {
                    gradient.Add(at, colour);
                    at += step;
                }
            }

            return gradient;
        }

        /// <summary>
        /// The value each of <paramref name="count"/> colours stands for, as <see cref="Stops"/>
        /// places them. Labelling the edges of <paramref name="count"/> equal bands instead put
        /// each colour up to a band away from where the model shows it. A flat range has the one
        /// value, which is all the model shows: everything is the first colour.
        ///
        /// The last is <paramref name="max"/> itself, not min + span, which can round a hair off:
        /// View Results hands these to Legend, which reads its range back from the ends.
        /// </summary>
        internal static List<double> LabelValues(double min, double max, int count)
        {
            var values = new List<double>();
            double span = max - min;
            if (count < 2 || !(span > 0.0))
            {
                values.Add(min);
                return values;
            }

            for (int k = 0; k < count - 1; k++)
                values.Add(min + span * k / (count - 1));
            values.Add(max);

            return values;
        }

        /// <summary>
        /// The band <paramref name="value"/> falls in, from 0 at the low end, when [min, max] is
        /// cut into <paramref name="count"/> equal bands - one per colour, for iso-surfaces. Below
        /// the range is the first band and above it the last, as the gradient holds its end
        /// colours; a flat range is all the first band, as it is all the first colour.
        /// </summary>
        internal static int Band(double value, double min, double max, int count)
        {
            double span = max - min;
            if (count < 2 || !(span > 0.0) || double.IsNaN(value)) return 0;

            double at = Math.Floor((value - min) / span * count);
            return at < 0.0 ? 0 : at >= count ? count - 1 : (int)at;
        }

        /// <summary>
        /// Where band <paramref name="k"/> of <see cref="Band"/> starts, the edge it shares with
        /// band k - 1; edge <paramref name="count"/> is max. The same values LabelValues gives for
        /// one more than <paramref name="count"/>, which is what View Results hands Legend so it
        /// writes them at the edges between its swatches.
        /// </summary>
        internal static double Edge(int k, double min, double max, int count)
        {
            return k >= count ? max : min + (max - min) * k / count;
        }

        /// <summary>
        /// <paramref name="value"/> to four significant figures of the larger end of the range,
        /// so every label of a legend reads to the same step. Two fixed decimals printed every
        /// displacement in metres as 0.00; four figures of each label's own put a 5E-05 among
        /// 0.0012 and -0.0034, and the hair rounding leaves where a range straddles zero
        /// printed as 1.1E-16.
        /// </summary>
        internal static string Format(double value, double min, double max)
        {
            double largest = Math.Max(Math.Abs(min), Math.Abs(max));
            if (!(largest > 0.0) || double.IsInfinity(largest))
                return value.ToString("G4", CultureInfo.InvariantCulture);

            int exponent = (int)Math.Floor(Math.Log10(largest));
            if (Math.Abs(value) < 0.5 * Math.Pow(10.0, exponent - 3))
                return "0";

            if (exponent < -4 || exponent > 4)
                return value.ToString("0.###E+00", CultureInfo.InvariantCulture);

            string format = exponent >= 3 ? "0" : "0." + new string('#', 3 - exponent);
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
