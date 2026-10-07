using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Alpaca4d
{
    /// <summary>
    /// The eigen analysis Natural Vibration appends to a model, and the reading of its
    /// eigenvalues back out of what OpenSees prints.
    /// </summary>
    public static class Eigen
    {
        /// <summary>
        /// What the eigenvalues are printed behind. OpenSees routes Tcl's puts through its
        /// error stream, where the solvers write whatever they have to say as well -
        /// FullGenEigenSolver nearly always has something, about its workspace or a complex
        /// eigenvalue - so the eigenvalues go on a line of their own, behind a label nothing
        /// else prints, and only that line is read.
        /// </summary>
        public const string Label = "ALPACA4D_EIGENVALUES";

        /// <summary>
        /// Why <paramref name="solver"/> cannot give natural frequencies, or null when it can.
        /// </summary>
        public static string Unsuitable(string solver)
        {
            // OpenSees has it solve K phi = lambda phi alone. Asked for the vibration problem it
            // prints "only does standard problem", hands back an empty list and exits as if
            // nothing had happened.
            if (solver != null && solver.IndexOf("symmBandLapack", StringComparison.OrdinalIgnoreCase) >= 0)
                return "-symmBandLapack solves only the standard eigenvalue problem, without the mass, so it cannot " +
                       "give natural frequencies. Use -genBandArpack, or -fullGenLapack for every mode of a small model.";
            return null;
        }

        /// <summary>
        /// What OpenSees printed about trouble with the eigen analysis: the lines naming an eigen
        /// solver, and its warnings. For saying why no eigenvalues came back.
        /// </summary>
        public static List<string> SolverMessages(string output)
        {
            if (output == null)
                return new List<string>();
            return output.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.IndexOf("Eigen", StringComparison.Ordinal) >= 0 && !l.StartsWith(Label, StringComparison.Ordinal)
                            || l.StartsWith("WARNING", StringComparison.Ordinal))
                .ToList();
        }

        /// <summary>The Tcl that solves for <paramref name="modes"/> modes and prints their eigenvalues.</summary>
        public static string WriteTcl(string solver, int modes)
        {
            return $"set lambdaN [eigen {solver} {modes}]\n" +
                   $"puts \"{Label} $lambdaN\"\n";
        }

        /// <summary>
        /// The eigenvalues printed by <see cref="WriteTcl"/>, lowest first, from everything
        /// OpenSees printed. Null when they are not there - the eigen command never got to
        /// print them.
        /// </summary>
        public static List<double> Read(string output)
        {
            if (output == null)
                return null;

            string line = output.Split('\n')
                .Select(l => l.Trim())
                .LastOrDefault(l => l == Label || l.StartsWith(Label + " ", StringComparison.Ordinal));
            if (line == null)
                return null;

            return line.Substring(Label.Length)
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Number)
                .ToList();
        }

        /// <summary>
        /// The sections of the report <c>modalProperties</c> writes that Natural Vibration hands
        /// out, by their number in it: 2, eigenvalues, to 10, cumulative mass ratios.
        /// </summary>
        public static readonly int[] ReportSectionNumbers = { 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        /// <summary>
        /// The report split into its sections, one list of lines per entry of
        /// <see cref="ReportSectionNumbers"/>, each from its "* N. TITLE" line down to the blank
        /// line that ends it. A section the report does not have is an empty list in its own
        /// place.
        ///
        /// Found by number and ended by the blank line, rather than counted out a fixed number of
        /// lines and taken in turn - the way this was read before, which put every section after
        /// a missing one under the wrong output, and cut a section short if OpenSees added a line
        /// of explanation to it.
        /// </summary>
        public static List<List<string>> ReportSections(IEnumerable<string> lines)
        {
            var sections = ReportSectionNumbers.Select(_ => new List<string>()).ToList();
            List<string> current = null;

            foreach (var line in lines ?? Enumerable.Empty<string>())
            {
                int number = SectionNumber(line);
                if (number > 0)
                {
                    int slot = Array.IndexOf(ReportSectionNumbers, number);
                    current = slot >= 0 ? sections[slot] : null;
                    current?.Add(line);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    current = null;
                    continue;
                }

                current?.Add(line);
            }

            return sections;
        }

        /// <summary>The N of a "* N. TITLE" line, or 0 for any other line.</summary>
        private static int SectionNumber(string line)
        {
            if (line == null) return 0;

            var text = line.TrimStart();
            if (!text.StartsWith("*", StringComparison.Ordinal)) return 0;

            text = text.Substring(1).TrimStart();
            int dot = text.IndexOf('.');
            return dot > 0 && int.TryParse(text.Substring(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                ? number
                : 0;
        }

        private static double Number(string text)
        {
            // Tcl prints its doubles the C way whatever the machine's locale, so they are read
            // the same way: 1.5 is one and a half on a machine that writes 1,5 as well.
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return value;

            // and spells infinity its own way.
            switch (text.ToLowerInvariant())
            {
                case "inf":
                case "+inf":
                    return double.PositiveInfinity;
                case "-inf":
                    return double.NegativeInfinity;
                default:
                    return double.NaN;
            }
        }
    }
}
