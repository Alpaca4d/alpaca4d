using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Alpaca4d.Seismic
{
    /// <summary>
    /// What Response Spectrum Analysis appends to the model, and the reading of the modal report it
    /// leaves behind.
    ///
    /// OpenSees' responseSpectrumAnalysis pushes the model into one mode at a time, as far as the spectrum
    /// takes it - φ Γ Sa / ω² - and commits it, so the recorder writes the displacements, the forces and
    /// the reactions of that mode as a step. It is run here at a spectral acceleration of one for every
    /// mode: the response is linear in Sa, so <see cref="SpectrumCases"/> multiplies each step by the
    /// spectrum read at its mode's period exactly, rather than handing OpenSees a curve to interpolate.
    /// </summary>
    public static class SpectrumAnalysis
    {
        /// <summary>
        /// The Tcl after the model: the eigen analysis, the modal report the spectrum analysis needs and
        /// writes to <paramref name="reportFile"/>, then each of the first <paramref name="modes"/> modes
        /// for each of <paramref name="axes"/> (0, 1, 2 for X, Y, Z) in turn - mode n of the k-th axis is
        /// recorder step k·modes + n.
        /// </summary>
        public static string WriteTcl(string solver, int modes, IEnumerable<int> axes, string reportFile)
        {
            var tcl = new StringBuilder();
            tcl.Append(Eigen.WriteTcl(solver, Solved(solver, modes)));
            tcl.Append($"modalProperties -file {TclPath.Write(reportFile)} -unorm\n");
            foreach (int axis in axes)
                for (int mode = 1; mode <= modes; mode++)
                    tcl.Append($"responseSpectrumAnalysis {axis + 1} -Tn {{1.0}} -Sa {{1.0}} -mode {mode}\n");
            return tcl.ToString();
        }

        /// <summary>
        /// How many modes the eigen analysis is asked for, to analyse <paramref name="modes"/> of them.
        /// ARPACK asked for a single mode can hand back an eigenvalue of zero without a word - OpenSees
        /// 3.8 does on a frame with a slab - so it is asked for two and the second is left out: each mode
        /// is run on its own, so only those asked for become steps.
        /// </summary>
        public static int Solved(string solver, int modes) =>
            IsArpack(solver) ? Math.Max(2, modes) : modes;

        public static bool IsArpack(string solver) =>
            solver != null && solver.IndexOf("Arpack", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Why ARPACK found no modes, when what OpenSees printed says it could not build its Lanczos basis -
        /// _saupd info -9999 - or null. It needs about twice as many degrees of freedom with mass as modes:
        /// a frame with its mass on four nodes has twelve, and gives six modes at most.
        /// </summary>
        public static string ArpackShort(string output, int modes)
        {
            if (output == null || output.IndexOf("_saupd info = -9999", StringComparison.Ordinal) < 0)
                return null;
            return $"ARPACK could not find {modes} modes: it needs about twice as many degrees of freedom carrying mass as " +
                   "modes, and the model has fewer. Ask for fewer Vibration Modes - a model with few masses has few modes, and " +
                   "they may already move all of it - or pick -fullGenLapack, which finds every mode of a small model.";
        }

        /// <summary>
        /// The masses the modal report gives: the total free mass along X, Y and Z (its section 4), and
        /// the participating mass of each of the first <paramref name="modes"/> modes along each (section
        /// 7) - the report also holds a mode solved only to keep ARPACK right (<see cref="Solved"/>). Null
        /// when the report does not hold them.
        /// </summary>
        public static ModalMasses ReadMasses(IEnumerable<string> report, int modes = int.MaxValue)
        {
            var sections = Eigen.ReportSections(report);
            var free = Rows(sections[Array.IndexOf(Eigen.ReportSectionNumbers, 4)], hasMode: false);
            var participating = Rows(sections[Array.IndexOf(Eigen.ReportSectionNumbers, 7)], hasMode: true).Take(modes).ToList();
            if (free.Count == 0 || participating.Count == 0 || free[0].Length < 3 || participating.Any(row => row.Length < 3))
                return null;

            return new ModalMasses(free[0].Take(3).ToArray(), participating.Select(row => row.Take(3).ToArray()).ToList());
        }

        /// <summary>The numbers of a report section, a row per line, the MODE column left off.</summary>
        private static List<double[]> Rows(List<string> section, bool hasMode)
        {
            var rows = new List<double[]>();
            foreach (var line in section)
            {
                var text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal) || text.StartsWith("*", StringComparison.Ordinal))
                    continue;

                var numbers = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(token => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : double.NaN)
                    .ToArray();
                if (numbers.Length == 0 || numbers.Any(double.IsNaN))
                    continue;

                rows.Add(hasMode ? numbers.Skip(1).ToArray() : numbers);
            }
            return rows;
        }
    }

    /// <summary>The masses the modes move, from the modal report.</summary>
    public class ModalMasses
    {
        /// <summary>The mass on free degrees of freedom along X, Y and Z, in the model's mass unit.</summary>
        public IReadOnlyList<double> Free { get; }

        /// <summary>Per mode, the mass it moves along X, Y and Z.</summary>
        public IReadOnlyList<double[]> Participating { get; }

        public ModalMasses(double[] free, List<double[]> participating)
        {
            Free = free;
            Participating = participating;
        }

        /// <summary>The share of the free mass along <paramref name="axis"/> that all the modes together move.</summary>
        public double Ratio(int axis) =>
            Free[axis] > 0.0 ? Participating.Sum(mode => mode[axis]) / Free[axis] : 0.0;

        /// <summary>Each mode's participating mass along <paramref name="axis"/>.</summary>
        public IReadOnlyList<double> Along(int axis) => Participating.Select(mode => mode[axis]).ToList();
    }
}
