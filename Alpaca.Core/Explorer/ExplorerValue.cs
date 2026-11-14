using System;
using System.Globalization;

namespace Alpaca4d.Explorer
{
    /// <summary>
    /// One named value of a design: an input it was made from, or a result it was measured by.
    ///
    /// The value is held as the text that goes into data.csv, so that what the components show,
    /// what is written and what decides whether a design is already recorded are the same string.
    /// Numbers are written the invariant way: a study run on a machine that writes 1,5 has to load
    /// on one that writes 1.5, and a comma inside a number would also split the CSV column.
    /// </summary>
    public sealed class ExplorerValue
    {
        public string Name { get; }
        public string Value { get; }

        public ExplorerValue(string name, string value)
        {
            Name = name ?? "";
            Value = value ?? "";
        }

        public ExplorerValue(string name, double value) : this(name, Format(value)) { }

        /// <summary>
        /// A number as it is written to data.csv. Fifteen significant digits, so a slider's 0.3
        /// stays 0.3 rather than 0.29999999999999999; NaN and infinities are left empty, which the
        /// explorer reads as "no data" rather than as a number that would stretch an axis.
        /// </summary>
        public static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "";
            return value.ToString("G15", CultureInfo.InvariantCulture);
        }

        public static string Format(decimal value)
        {
            return Format(Convert.ToDouble(value));
        }

        public override string ToString() => $"{Name}: {Value}";
    }
}
