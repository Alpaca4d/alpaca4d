using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Alpaca4d.Generic;
using Alpaca4d.Section;

namespace Alpaca4d.Template
{
    /// <summary>
    /// A deck that has OpenSees trace the stress-strain curve of every material a fibre section
    /// names, from zero to a strain either way, for <see cref="InteractionDomain"/>.
    ///
    /// Each material gets a zeroLength element of its own between a fixed node and a free one, and
    /// every free node is driven to the same displacement, which is the element's strain. The 3.8
    /// interpreter has no testUniaxialMaterial, which would have done the same with less.
    /// </summary>
    public static class MaterialCurves
    {
        /// <summary>Printed by the deck when a step does not go through; the curves are not read then.</summary>
        public const string FailedMarker = "ALPACA_CURVES_FAILED";

        /// <summary>The tag a fibre names for <paramref name="material"/>, which is the one sampled.</summary>
        public static int Tag(IMaterial material)
        {
            int? tag = Alpaca4d.Material.MinMax.FiberTag(material);
            if (!tag.HasValue)
                throw new InvalidOperationException("A material in the section has no tag.");
            return tag.Value;
        }

        /// <param name="materials">The materials, once each.</param>
        /// <param name="maxStrain">The strain each branch ends at, positive.</param>
        /// <param name="steps">The steps to it, either way: the curve's resolution.</param>
        /// <param name="tensionFile">Where the tension branch goes: one line a step, one stress a material.</param>
        /// <param name="compressionFile">Where the compression branch goes.</param>
        public static string Define(IList<IMaterial> materials, double maxStrain, int steps, string tensionFile, string compressionFile)
        {
            if (materials == null || materials.Count == 0)
                throw new ArgumentException("There are no materials to sample.", nameof(materials));
            if (!(maxStrain > 0.0))
                throw new ArgumentOutOfRangeException(nameof(maxStrain), "The strain to sample to has to be positive.");
            if (steps < 1)
                throw new ArgumentOutOfRangeException(nameof(steps), "A curve needs at least one step either way.");

            var sb = new StringBuilder();
            sb.Append("wipe\n");
            sb.Append("model BasicBuilder -ndm 1 -ndf 1\n");

            // A material declares its MinMax wrapper as well, when it has one, and the wrapper's tag
            // is the one a fibre names - so that is the one each element is given.
            foreach (var material in materials)
                sb.Append(material.WriteTcl().TrimEnd('\r', '\n')).Append('\n');

            var tags = materials.Select(Tag).ToList();

            // Every element's node is driven, and BandGeneral fails outright on a system with no
            // equations in it ("BandGenLinSOE::getX - vectX == 0"). One unloaded spring keeps an
            // equation in the system and changes nothing else.
            int dummyMaterial = tags.Concat(materials.Select(m => m.Id ?? 0)).Max() + 1;
            sb.Append($"uniaxialMaterial Elastic {dummyMaterial} 1.0\n");

            sb.Append("node 1 0.0\n");
            sb.Append("fix 1 1\n");
            for (int i = 0; i < tags.Count; i++)
            {
                sb.Append($"node {i + 2} 0.0\n");
                sb.Append($"element zeroLength {i + 1} 1 {i + 2} -mat {tags[i]} -dir 1\n");
            }
            int dummyNode = tags.Count + 2;
            sb.Append($"node {dummyNode} 0.0\n");
            sb.Append($"element zeroLength {tags.Count + 1} 1 {dummyNode} -mat {dummyMaterial} -dir 1\n");

            sb.Append("system BandGeneral\n");
            sb.Append("numberer Plain\n");
            sb.Append("constraints Transformation\n");
            sb.Append("test NormDispIncr 1.0e-12 10\n");
            sb.Append("algorithm Linear\n");
            sb.Append($"integrator LoadControl {TclNumber.Write(1.0 / steps)}\n");
            sb.Append("analysis Static\n");
            sb.Append("timeSeries Linear 1\n");

            // One line a step, the stress of each material in turn; the strain is the step's.
            sb.Append("proc alpacaBranch {file steps count} {\n");
            sb.Append("\tset f [open $file w]\n");
            sb.Append("\tfor {set k 0} {$k <= $steps} {incr k} {\n");
            sb.Append("\t\tif {$k > 0 && [analyze 1] != 0} {\n");
            sb.Append("\t\t\tclose $f\n");
            sb.Append($"\t\t\tputs \"{FailedMarker}\"\n");
            sb.Append("\t\t\texit 1\n");
            sb.Append("\t\t}\n");
            sb.Append("\t\tset line {}\n");
            sb.Append("\t\tfor {set i 1} {$i <= $count} {incr i} {\n");
            sb.Append("\t\t\tlappend line [lindex [eleResponse $i force] 1]\n");
            sb.Append("\t\t}\n");
            sb.Append("\t\tputs $f [join $line \" \"]\n");
            sb.Append("\t}\n");
            sb.Append("\tclose $f\n");
            sb.Append("}\n");

            AppendBranch(sb, 1, maxStrain, tags.Count, steps, tensionFile);

            // The compression branch starts from the material as it was before any strain, not
            // from one the tension branch has yielded.
            sb.Append("remove loadPattern 1\n");
            sb.Append("reset\n");
            sb.Append("setTime 0.0\n");

            AppendBranch(sb, 2, -maxStrain, tags.Count, steps, compressionFile);
            sb.Append("wipe\n");

            return sb.ToString();
        }

        private static void AppendBranch(StringBuilder sb, int pattern, double strain, int count, int steps, string file)
        {
            sb.Append($"pattern Plain {pattern} 1 {{\n");
            for (int i = 0; i < count; i++)
                sb.Append($"\tsp {i + 2} 1 {TclNumber.Write(strain)}\n");
            sb.Append("}\n");
            sb.Append($"alpacaBranch {TclPath.Write(file)} {steps} {count}\n");
        }

        /// <summary>The curves the deck wrote, by the tag each material's fibres name.</summary>
        public static Dictionary<int, StressStrainCurve> Read(IList<IMaterial> materials, double maxStrain, int steps,
                                                              string tensionFile, string compressionFile)
        {
            var tension = ReadBranch(tensionFile, materials.Count, steps);
            var compression = ReadBranch(compressionFile, materials.Count, steps);

            var curves = new Dictionary<int, StressStrainCurve>();
            for (int i = 0; i < materials.Count; i++)
                curves[Tag(materials[i])] = new StressStrainCurve(maxStrain, compression[i], tension[i]);
            return curves;
        }

        /// <summary>One array of stresses a material, steps + 1 long.</summary>
        private static double[][] ReadBranch(string file, int count, int steps)
        {
            if (!File.Exists(file))
                throw new InvalidOperationException($"OpenSees wrote no stress-strain curves to {file}.");

            var lines = File.ReadAllLines(file).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            if (lines.Count != steps + 1)
                throw new InvalidOperationException(
                    $"{Path.GetFileName(file)} has {lines.Count} lines of the {steps + 1} a curve needs.");

            var branch = new double[count][];
            for (int i = 0; i < count; i++)
                branch[i] = new double[steps + 1];

            for (int k = 0; k <= steps; k++)
            {
                var values = lines[k].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length != count)
                    throw new InvalidOperationException(
                        $"Line {k + 1} of {Path.GetFileName(file)} has {values.Length} stresses of the {count} it should.");

                for (int i = 0; i < count; i++)
                    branch[i][k] = TclNumber.Read(values[i]);
            }

            return branch;
        }

        /// <summary>
        /// Writes the deck to <paramref name="folder"/>, runs it, and reads the curves back.
        /// </summary>
        /// <param name="stem">What this run's files are called.</param>
        /// <param name="run">Runs OpenSees on a deck file and returns what it printed and its exit code.</param>
        /// <param name="deck">The deck that was run.</param>
        /// <param name="log">What OpenSees printed.</param>
        public static Dictionary<int, StressStrainCurve> Sample(IList<IMaterial> materials, double maxStrain, int steps,
                                                                string folder, string stem,
                                                                Func<string, (string output, string error, int exitCode)> run,
                                                                out string deck, out string log)
        {
            Directory.CreateDirectory(folder);
            string deckPath = Path.Combine(folder, stem + ".tcl");
            string tensionFile = Path.Combine(folder, stem + "_tension.out");
            string compressionFile = Path.Combine(folder, stem + "_compression.out");

            // A curve left by an earlier run must not pass for this one's.
            foreach (var file in new[] { tensionFile, compressionFile })
                if (File.Exists(file))
                    File.Delete(file);

            deck = Define(materials, maxStrain, steps, tensionFile, compressionFile);
            File.WriteAllText(deckPath, deck);

            var (output, error, exitCode) = run(deckPath);
            log = string.Join(Environment.NewLine, new[] { output, error }.Where(x => !string.IsNullOrWhiteSpace(x)));

            // In both streams: OpenSees 3.8 sends a deck's puts to stderr.
            if (exitCode != 0 || log.Contains(FailedMarker))
                throw new InvalidOperationException(
                    "OpenSees could not trace the materials' stress-strain curves. See the log output.");

            return Read(materials, maxStrain, steps, tensionFile, compressionFile);
        }
    }
}
