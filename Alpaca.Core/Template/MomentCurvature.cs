using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Alpaca4d.Section;

namespace Alpaca4d.Template
{
    public partial class MomentCurvature
    {
        public static string OutputFolder = "FiberResults";
        public static string ForceFilePath = "FiberResults/MKsectionForce.out";
        public static string DeformationFilePath = "FiberResults/MKsectionDef.out";
        /// <summary>
        /// Where every fibre's stress and strain history goes - one file for the whole
        /// section, five columns per fibre per step. See <see cref="Alpaca4d.Recorder.FiberData"/>
        /// for why it is not a file per fibre.
        /// </summary>
        public static string FiberDataFilePath = "FiberResults/MKfiberData.out";

        /// <summary>
        /// Printed by the deck when the axial step does not converge. Nothing is recorded
        /// in that case: the recorders are only opened once the axial load is held.
        /// </summary>
        public const string AxialFailedMarker = "ALPACA_MK_AXIAL_FAILED";

        /// <summary>
        /// Printed by the deck when the curvature ramp stops early. What was reached is
        /// still recorded and still worth reading - a section that fails halfway is a
        /// result, not an error - so this is a marker and not a non-zero exit.
        /// </summary>
        public const string CurvatureIncompleteMarker = "ALPACA_MK_CURVATURE_INCOMPLETE";

        /// <summary>
        /// Printed by the deck when it stops itself at the first increment where a fibre has reached
        /// its ultimate strain. The curve up to there is recorded, as on any other stop.
        /// </summary>
        public const string UltimateMarker = "ALPACA_MK_ULTIMATE_REACHED";

        /// <summary>
        /// The section tag the deck uses. Hard-coded because the deck holds one section.
        /// </summary>
        private const int SectionTag = 1;

        /// <summary>
        /// Creates the folder the recorders write into, and clears what a previous run
        /// left there so a shorter curve cannot be read as a longer one.
        ///
        /// Separate from <see cref="Define"/>, and called after the caller has settled on
        /// a working directory: both this and the recorder paths in the deck are relative
        /// to the current directory, so they have to be resolved against the same one.
        /// </summary>
        public static void PrepareOutputFolder()
        {
            PrepareOutputFolder(Directory.GetCurrentDirectory());
        }

        /// <summary>As <see cref="PrepareOutputFolder()"/>, under <paramref name="root"/>: where the deck runs.</summary>
        public static void PrepareOutputFolder(string root)
        {
            string folder = Path.Combine(root, OutputFolder);
            Directory.CreateDirectory(folder);

            foreach (var file in Directory.GetFiles(folder))
                File.Delete(file);
        }

        /// <summary>
        /// A moment-curvature deck: one zero-length fibre section, a held axial force, and a
        /// curvature ramp about the section's local y or z axis.
        /// </summary>
        /// <param name="fiber">The section. Its own Id is not used - the deck holds one section.</param>
        /// <param name="axialForce">Positive is tension.</param>
        /// <param name="dof">"y" or "z", or a direction in degrees; see <see cref="Direction"/>.</param>
        /// <param name="maxPhi">The curvature to ramp to.</param>
        /// <param name="numIncr">Increments to reach <paramref name="maxPhi"/> in.</param>
        public static string Define(FiberSection fiber, double axialForce, string dof, double maxPhi, int numIncr = 1000)
        {
            return Define(fiber, axialForce, Direction(dof), maxPhi, numIncr, null, false);
        }

        /// <summary>
        /// A moment-curvature deck: one zero-length fibre section, a held axial force, and a
        /// curvature ramp in the moment direction <paramref name="angle"/>.
        ///
        /// The moment is held to that direction and the curvature across it is left free, so a
        /// section bent off its axes turns its neutral axis as it needs to. The element is turned
        /// by -angle about its axis, which puts the section's direction u = (cos angle, sin angle)
        /// on the global Y axis; the deck then pushes the rotation about Y and leaves the one about
        /// Z free and unloaded, as it always did for bending about y. Section forces and
        /// deformations are recorded in the section's own axes, whatever the element's.
        /// </summary>
        /// <param name="angle">The moment direction in the section's y-z plane, radians from y.</param>
        /// <param name="maxPhi">The curvature along that direction to ramp to.</param>
        /// <param name="ultimateRatioTcl">
        /// A proc from <see cref="InteractionDomain.UltimateRatioTcl"/>, named alpacaUltimate, or null.
        /// </param>
        /// <param name="stopAtUltimate">Stop at the first increment where alpacaUltimate reaches 1.</param>
        public static string Define(FiberSection fiber, double axialForce, double angle, double maxPhi, int numIncr,
                                    string ultimateRatioTcl, bool stopAtUltimate)
        {
            if (fiber == null)
                throw new ArgumentNullException("fiber");

            if (numIncr < 1)
                throw new ArgumentOutOfRangeException("numIncr", "A moment-curvature analysis needs at least one increment.");

            if (stopAtUltimate && string.IsNullOrWhiteSpace(ultimateRatioTcl))
                throw new ArgumentException("Stopping at the ultimate state needs the strain limits to check.", "ultimateRatioTcl");

            var clean = "wipe\n";
            var builder = "model BasicBuilder -ndm 3 -ndf 6\n";

            var node1 = "node 1 0 0 0 \n";
            var node2 = "node 2 0 0 0\n";

            var fix1 = "fix 1 1 1 1 1 1 1\n";
            var fix2 = "fix 2 0 1 1 1 0 0\n";

            var materialList = new List<Generic.IMaterial>();
            materialList.AddRange(fiber.PointFibers.Select(x => x.Material));
            materialList.AddRange(fiber.Layers.Select(x => x.Material));
            materialList.AddRange(fiber.Patches.Select(x => x.Material));
            var uniqueMaterial = materialList.Where(x => x != null).Distinct();

            var material = String.Join("", uniqueMaterial.Select(x => x.WriteTcl()));

            var fiberSection = fiber.WriteTcl(SectionTag);

            // Local x along global X; local y turned by -angle about it, so the section's u lands on
            // global Y. At angle 0 this is the element's default orientation.
            double c = Math.Cos(angle), s = -Math.Sin(angle);
            if (Math.Abs(c) < 1e-15) c = 0.0;
            if (Math.Abs(s) < 1e-15) s = 0.0;
            var element = $"element zeroLengthSection 1 1 2 {SectionTag} -orient 1 0 0 0 {TclNumber.Write(c)} {TclNumber.Write(s)}\n";

            var timeSeriesAxial = "timeSeries Constant 1\n";
            var patternAxial = "pattern Plain 1 1 {\n\t"
                        + $"load 2 {TclNumber.Write(axialForce)} 0 0 0 0 0}}\n";

            var integrator = "integrator LoadControl 0\n";
            var system = "system BandGeneral\n";

            // NormDispIncr, not NormUnbalance: an unbalance tolerance is an absolute force
            // and so means something different in every unit system. 1e-10 N.mm is not
            // reachable at all - a clean eight-fibre section stalls at a norm around 1e-9 -
            // and the deck used to abandon the curve there.
            var test = "test NormDispIncr 1.0e-8 1000\n";
            var number = "numberer RCM\n";
            var constraints = "constraints Plain\n";
            var algorithm = "algorithm Newton\n";
            var analysis = "analysis Static\n";

            // analyze returns 0 or an error flag, and OpenSees exits 0 either way, so the
            // deck has to say so itself. Nothing is recorded yet, hence the bare exit.
            var analyze = "if {[analyze 1] != 0} {\n"
                        + $"\tputs \"{AxialFailedMarker}\"\n"
                        + "\twipe\n"
                        + "\texit 1\n"
                        + "}\n";
            var lc = "loadConst -time 0.0\n";

            var recorder1 = $"recorder Element -file {ForceFilePath} -ele 1 section force\n";
            var recorder2 = $"recorder Element -file {DeformationFilePath} -ele 1 section deformation\n";
            var recorder3 = Alpaca4d.Recorder.FiberData();

            var timeSeriesMoment = "timeSeries Linear 2\n";
            var patternMoment = "pattern Plain 2 2 {\n\t"
            + "load 2 0 0 0 0 1 0}\n";

            var increment = $"set alpacaIncrement {TclNumber.Write(maxPhi / numIncr)}\n";
            var integratorDisp = "integrator DisplacementControl 2 5 $alpacaIncrement\n";

            // One increment at a time, so that a step that does not converge gets another go before
            // the curve is given up on: ten steps of a tenth, then a line search, then the initial
            // tangent - what usually takes a fibre section past a kink in a material's curve. A
            // ramp that still stops early leaves a usable curve behind, so this reports and carries
            // on to the wipe that flushes the recorders.
            var loop = new System.Text.StringBuilder();
            loop.Append($"for {{set alpacaStep 1}} {{$alpacaStep <= {numIncr}}} {{incr alpacaStep}} {{\n");
            loop.Append("\tset ok [analyze 1]\n");
            loop.Append("\tif {$ok != 0} {\n");
            loop.Append("\t\tintegrator DisplacementControl 2 5 [expr {$alpacaIncrement / 10.0}]\n");
            loop.Append("\t\tset ok [analyze 10]\n");
            loop.Append("\t\tintegrator DisplacementControl 2 5 $alpacaIncrement\n");
            loop.Append("\t}\n");
            loop.Append("\tif {$ok != 0} {\n");
            loop.Append("\t\talgorithm NewtonLineSearch\n");
            loop.Append("\t\tset ok [analyze 1]\n");
            loop.Append("\t\talgorithm Newton\n");
            loop.Append("\t}\n");
            loop.Append("\tif {$ok != 0} {\n");
            loop.Append("\t\talgorithm ModifiedNewton -initial\n");
            loop.Append("\t\tset ok [analyze 1]\n");
            loop.Append("\t\talgorithm Newton\n");
            loop.Append("\t}\n");
            loop.Append("\tif {$ok != 0} {\n");
            loop.Append($"\t\tputs \"{CurvatureIncompleteMarker}\"\n");
            loop.Append("\t\tbreak\n");
            loop.Append("\t}\n");
            if (stopAtUltimate)
            {
                loop.Append("\tif {[alpacaUltimate {*}[eleResponse 1 section deformation]] >= 1.0} {\n");
                loop.Append($"\t\tputs \"{UltimateMarker}\"\n");
                loop.Append("\t\tbreak\n");
                loop.Append("\t}\n");
            }
            loop.Append("}\n");

            return Deck(
                clean, builder, node1, node2, fix1, fix2,
                material, fiberSection, element,
                timeSeriesAxial, patternAxial,
                integrator, system, test, number, constraints, algorithm, analysis,
                analyze, lc,
                recorder1, recorder2, recorder3,
                timeSeriesMoment, patternMoment,
                ultimateRatioTcl ?? "",
                increment, integratorDisp, loop.ToString(),
                clean);
        }

        /// <summary>
        /// Joins the pieces of a deck, ending each one on its own line.
        ///
        /// Not plain concatenation. A piece that came without a trailing newline - the
        /// fibre recorders, joined line by line - swallowed the first line of whatever
        /// followed it, and what followed was "timeSeries Linear 2". The pattern that
        /// referred to it then failed with "none found with tag: 2" and no curvature was
        /// ever applied, whatever else the deck said. Terminating here rather than in
        /// twenty-odd string literals is the difference between one place to get it right
        /// and twenty.
        /// </summary>
        private static string Deck(params string[] chunks)
        {
            var sb = new System.Text.StringBuilder();

            foreach (var chunk in chunks)
            {
                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                sb.Append(chunk.TrimEnd('\r', '\n'));
                sb.Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// The moment direction a Direction input names, in radians from the local y axis: "y" is
        /// 0, "z" is 90 degrees, and a number is degrees, whatever the machine's decimal separator.
        /// Anything else is rejected rather than quietly taken to mean y.
        /// </summary>
        public static double Direction(string dof)
        {
            if (string.Equals(dof?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                return 0.0;

            if (string.Equals(dof?.Trim(), "z", StringComparison.OrdinalIgnoreCase))
                return 0.5 * Math.PI;

            if (dof != null && double.TryParse(dof.Trim(), System.Globalization.NumberStyles.Float,
                                               System.Globalization.CultureInfo.InvariantCulture, out double degrees) &&
                !double.IsNaN(degrees) && !double.IsInfinity(degrees))
                return degrees * Math.PI / 180.0;

            throw new ArgumentException(
                "Direction has to be \"y\", \"z\" or an angle in degrees from y, not \"" + dof + "\".", "dof");
        }

        /// <summary>
        /// Which local axis a direction names. Case-insensitive, because a value list is
        /// not the only thing that can reach this input, and anything else is rejected
        /// rather than quietly taken to mean z.
        /// </summary>
        public static bool IsBendingAboutY(string dof)
        {
            if (string.Equals(dof, "y", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(dof, "z", StringComparison.OrdinalIgnoreCase))
                return false;

            throw new ArgumentException(
                "Direction has to be \"y\" or \"z\", not \"" + dof + "\".", "dof");
        }
    }
}
