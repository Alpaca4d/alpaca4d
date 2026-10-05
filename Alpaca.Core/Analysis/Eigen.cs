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
