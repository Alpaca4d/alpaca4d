using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Alpaca4d.Section
{
    /// <summary>What a moment-curvature analysis gives back.</summary>
    public sealed class MomentCurvatureResult
    {
        /// <summary>The moment direction, radians from the local y axis.</summary>
        public double Angle { get; internal set; }

        /// <summary>The axial force held while the curvature is applied, kN, tension positive.</summary>
        public double HeldN { get; internal set; }

        /// <summary>The curvature along the direction the deck ramped to: given, or worked out.</summary>
        public double MaxPhi { get; internal set; }

        /// <summary>Whether MaxPhi was worked out from the section's domain rather than given.</summary>
        public bool MaxPhiFromDomain { get; internal set; }

        /// <summary>Each increment's section forces, kN and kN m, and deformations, as recorded.</summary>
        public List<double> N { get; } = new List<double>();
        public List<double> My { get; } = new List<double>();
        public List<double> Mz { get; } = new List<double>();
        public List<double> Axial { get; } = new List<double>();
        public List<double> CurvatureY { get; } = new List<double>();
        public List<double> CurvatureZ { get; } = new List<double>();

        /// <summary>The moment along the direction, My cos a + Mz sin a: the curve's M.</summary>
        public List<double> Moment { get; } = new List<double>();

        /// <summary>The curvature along the direction, κy cos a + κz sin a: the curve's κ.</summary>
        public List<double> Curvature { get; } = new List<double>();

        /// <summary>First yield of a fibre whose material yields - a bar, a steel plate - or NaN.</summary>
        public double YieldCurvature { get; internal set; } = double.NaN;
        public double YieldMoment { get; internal set; } = double.NaN;

        /// <summary>The first fibre at its ultimate strain, the limits of Interaction Domain; or NaN.</summary>
        public double UltimateCurvature { get; internal set; } = double.NaN;
        public double UltimateMoment { get; internal set; } = double.NaN;

        /// <summary>Ultimate over yield curvature; NaN without both.</summary>
        public double Ductility => UltimateCurvature / YieldCurvature;

        /// <summary>The secant stiffness to first yield, M / κ there, kN m²; NaN without it.</summary>
        public double YieldStiffness => YieldMoment / YieldCurvature;

        /// <summary>What reached its ultimate strain first, in words; null when nothing did.</summary>
        public string Failure { get; internal set; }

        /// <summary>The deck stopped itself at the ultimate state.</summary>
        public bool StoppedAtUltimate { get; internal set; }

        /// <summary>An increment did not converge and the curve stops short.</summary>
        public bool Incomplete { get; internal set; }

        /// <summary>The section's domain, with the limits the curve was read against.</summary>
        public InteractionDomain Domain { get; internal set; }

        /// <summary>The moment-curvature deck that ran, and what OpenSees printed for it and for the materials.</summary>
        public string Deck { get; internal set; }
        public string Log { get; internal set; }

        /// <summary>Things to tell the user that are not errors.</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>The direction in words: y, z, or degrees from y.</summary>
        public string DirectionName
        {
            get
            {
                double degrees = Angle * 180.0 / Math.PI;
                if (Math.Abs(degrees) < 1e-9) return "y";
                if (Math.Abs(degrees - 90.0) < 1e-9) return "z";
                return degrees.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " deg";
            }
        }

        /// <summary>What a Panel shows of the result.</summary>
        public override string ToString()
        {
            string text = $"Moment-curvature: N = {HeldN:0.##} kN, about {DirectionName}, {Curvature.Count} increments";
            if (!double.IsNaN(YieldCurvature))
                text += $"; first yield at κ {YieldCurvature:0.######}, M {YieldMoment:0.##}";
            if (!double.IsNaN(UltimateCurvature))
                text += $"; ultimate at κ {UltimateCurvature:0.######}, M {UltimateMoment:0.##}";
            if (!double.IsNaN(Ductility))
                text += $"; ductility {Ductility:0.##}";
            return text;
        }
    }

    /// <summary>
    /// A moment-curvature analysis, start to end: the section's materials traced and its strain
    /// limits worked out as Interaction Domain does, the curvature ramped in any moment direction
    /// under a held axial force, and the curve read for first yield, the ultimate state and the
    /// ductility between them.
    /// </summary>
    public static class MomentCurvatureAnalysis
    {
        public const string DeckFileName = "MomentCurvature.tcl";

        /// <param name="section">The section, which the deck is written from.</param>
        /// <param name="layout">Its layout, which the limits are read against.</param>
        /// <param name="axial">The held axial force, kN, tension positive.</param>
        /// <param name="angle">The moment direction, radians from the local y axis.</param>
        /// <param name="maxPhi">The curvature to ramp to; null to take it from the section's domain.</param>
        /// <param name="limits">εud and εcu, as Interaction Domain takes them.</param>
        /// <param name="stopAtUltimate">Stop at the first increment where a fibre reaches its ultimate strain.</param>
        /// <param name="folder">Where the deck runs: its recorders write under it, as OpenSees' working directory.</param>
        /// <param name="curvesFolder">Where the materials are traced.</param>
        public static MomentCurvatureResult Run(FiberSection section, SectionLayout layout, double axial, double angle,
                                                int numIncr, double? maxPhi, DomainSettings limits, bool stopAtUltimate,
                                                string folder, string curvesFolder, string curvesStem,
                                                Func<string, (string output, string error, int exitCode)> run)
        {
            if (numIncr < 1)
                throw new ArgumentOutOfRangeException(nameof(numIncr), "A moment-curvature analysis needs at least one increment.");
            if (maxPhi.HasValue && !(maxPhi.Value > 0.0))
                throw new ArgumentOutOfRangeException(nameof(maxPhi), "MaxPhi has to be a positive curvature, or left empty.");

            var result = new MomentCurvatureResult { Angle = angle, HeldN = axial };

            // A domain of one level is all the limits need, and it is what finds the ultimate
            // curvature to ramp to when none is given.
            var settings = new DomainSettings
            {
                SteelUltimateStrain = limits.SteelUltimateStrain,
                ConcreteUltimateStrain = limits.ConcreteUltimateStrain,
                CurveSteps = limits.CurveSteps,
                Levels = 1,
                PointsPerLevel = 8,
            };
            var domain = InteractionDomain.Analyse(layout, settings, curvesFolder, curvesStem, run, out _, out string curvesLog);
            result.Domain = domain;

            if (!(axial < domain.Nt && axial > domain.Nc))
                throw new InvalidOperationException(
                    $"An axial force of {axial:0.##} kN is beyond what the section carries, {domain.Nc:0.##} to {domain.Nt:0.##} kN, " +
                    "so no curvature can be applied under it.");

            double ux = Math.Cos(angle), uy = Math.Sin(angle);
            if (maxPhi.HasValue)
                result.MaxPhi = maxPhi.Value;
            else
            {
                // The domain's ultimate state at this N in this direction, reached along a straight
                // path; the deck's path, N first, ends close to it. Past it by a fifth to be sure of
                // reaching it, or twice it to show what follows when the deck is not to stop there.
                double estimate = double.NaN;
                if (domain.LineHits(axial, angle, out _, out var far))
                    estimate = ux * far.CurvatureY + uy * far.CurvatureZ;
                if (!(estimate > 0.0))
                    throw new InvalidOperationException(
                        "The section's domain gives no ultimate curvature in this direction at this axial force; give MaxPhi.");
                result.MaxPhi = (stopAtUltimate ? 1.2 : 2.0) * estimate;
                result.MaxPhiFromDomain = true;
            }

            Template.MomentCurvature.PrepareOutputFolder(folder);
            string deck = Template.MomentCurvature.Define(section, axial, angle, result.MaxPhi, numIncr,
                                                          domain.UltimateRatioTcl("alpacaUltimate"), stopAtUltimate);
            result.Deck = deck;
            string deckPath = Path.Combine(folder, DeckFileName);
            File.WriteAllText(deckPath, deck);

            var (output, error, exitCode) = run(deckPath);
            string log = string.Join(Environment.NewLine, new[] { output, error }.Where(x => !string.IsNullOrWhiteSpace(x)));
            result.Log = string.Join(Environment.NewLine, new[] { curvesLog, log }.Where(x => !string.IsNullOrWhiteSpace(x)));

            // Read in both streams: OpenSees 3.8 sends a deck's puts to stderr.
            if (log.Contains(Template.MomentCurvature.AxialFailedMarker))
                throw new InvalidOperationException("The axial step did not converge, so no curvature was applied. See the log output.");
            if (exitCode != 0)
                throw new InvalidOperationException("OpenSees stopped on an error in the deck. See the log output.");

            result.Incomplete = log.Contains(Template.MomentCurvature.CurvatureIncompleteMarker);
            result.StoppedAtUltimate = log.Contains(Template.MomentCurvature.UltimateMarker);

            var force = ReadHistory(Path.Combine(folder, Template.MomentCurvature.ForceFilePath));
            var deformation = ReadHistory(Path.Combine(folder, Template.MomentCurvature.DeformationFilePath));
            int rows = Math.Min(force.Count, deformation.Count);
            if (rows == 0)
                throw new InvalidOperationException("The analysis recorded nothing. See the log output.");

            // A FiberSectionGJ reports P, Mz, My, T, and its deformations in the matching order.
            for (int i = 0; i < rows; i++)
            {
                result.N.Add(At(force[i], 0));
                result.Mz.Add(At(force[i], 1));
                result.My.Add(At(force[i], 2));
                result.Axial.Add(At(deformation[i], 0));
                result.CurvatureZ.Add(At(deformation[i], 1));
                result.CurvatureY.Add(At(deformation[i], 2));
                result.Moment.Add(ux * result.My[i] + uy * result.Mz[i]);
                result.Curvature.Add(ux * result.CurvatureY[i] + uy * result.CurvatureZ[i]);
            }

            var yieldRatio = Enumerable.Range(0, rows)
                .Select(i => domain.YieldRatio(result.Axial[i], result.CurvatureY[i], result.CurvatureZ[i])).ToList();
            var ultimateRatio = Enumerable.Range(0, rows)
                .Select(i => domain.UltimateRatio(result.Axial[i], result.CurvatureY[i], result.CurvatureZ[i])).ToList();

            if (yieldRatio.All(double.IsNaN))
                result.Warnings.Add("No material of the section yields - none is Steel01 or ReinforcingSteel - so there is no yield point, and no ductility.");
            else if (Crossing(yieldRatio, result, true, out double ky, out double my))
            {
                result.YieldCurvature = ky;
                result.YieldMoment = my;
            }
            else
                result.Warnings.Add("No fibre yielded before the curve ended, so there is no yield point.");

            int ultimateRow = ultimateRatio.FindIndex(r => r >= 1.0);
            if (Crossing(ultimateRatio, result, false, out double ku, out double mu))
            {
                result.UltimateCurvature = ku;
                result.UltimateMoment = mu;
                result.Failure = domain.Governs(result.Axial[ultimateRow], result.CurvatureY[ultimateRow], result.CurvatureZ[ultimateRow]);
            }
            else if (!result.Incomplete)
                result.Warnings.Add(
                    $"No fibre reached its ultimate strain by a curvature of {result.MaxPhi:0.#####} 1/m: " +
                    (result.MaxPhiFromDomain ? "the deck's path ended past the domain's ultimate state, so give MaxPhi a larger value."
                                             : "raise MaxPhi, or leave it empty to take it from the section's domain."));

            return result;
        }

        /// <summary>
        /// Where a ratio along the curve first reaches 1: the curvature between the increments
        /// either side of it, and the moment there. False when it never does.
        /// </summary>
        /// <param name="alongBefore">
        /// Take the moment along the curve's slope before the crossing rather than across it. First
        /// yield is where the curve turns: read across the increment that yields, the moment came
        /// out 0.2% low on an HEA 300, and exact along the elastic slope it leaves.
        /// </param>
        private static bool Crossing(List<double> ratio, MomentCurvatureResult result, bool alongBefore,
                                     out double curvature, out double moment)
        {
            curvature = moment = double.NaN;
            int i = ratio.FindIndex(r => r >= 1.0);
            if (i < 0)
                return false;

            if (i == 0 || double.IsNaN(ratio[i - 1]))
            {
                curvature = result.Curvature[i];
                moment = result.Moment[i];
                return true;
            }

            double t = (1.0 - ratio[i - 1]) / (ratio[i] - ratio[i - 1]);
            curvature = result.Curvature[i - 1] + t * (result.Curvature[i] - result.Curvature[i - 1]);

            var k = result.Curvature;
            var m = result.Moment;
            if (alongBefore && i >= 2 && Math.Abs(k[i - 1] - k[i - 2]) > 0.0)
                moment = m[i - 1] + (curvature - k[i - 1]) * (m[i - 1] - m[i - 2]) / (k[i - 1] - k[i - 2]);
            else
                moment = m[i - 1] + t * (m[i] - m[i - 1]);
            return true;
        }

        private static double At(double[] row, int index) => index < row.Length ? row[index] : 0.0;

        /// <summary>
        /// A recorder file as one row of numbers per increment. A short or missing file gives what
        /// there is: a section that stops converging leaves a shorter file behind, which is a result.
        /// </summary>
        public static List<double[]> ReadHistory(string filePath)
        {
            var rows = new List<double[]>();
            if (!File.Exists(filePath))
                return rows;

            foreach (var line in File.ReadAllLines(filePath))
            {
                var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length == 0)
                    continue;
                rows.Add(values.Select(TclNumber.Read).ToArray());
            }
            return rows;
        }
    }
}
