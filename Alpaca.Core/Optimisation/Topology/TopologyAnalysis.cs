using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Alpaca4d.Element;
using Alpaca4d.Generic;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// The analysis of a topology optimisation: one OpenSees that builds the model once and then, for
    /// each design, sets every solid's modulus, solves again, and writes the displacements and the
    /// element forces. The energy of an element is u_e . f_e, its nodal displacements against the
    /// forces it puts on its nodes - which is u_e . K_e u_e for any linear element, whatever its
    /// formulation, so nothing here knows how an element is integrated.
    ///
    /// A modulus is set with setParameter, which every solid passes to the material at each of its
    /// integration points. Each element holds copies of its material of its own, so one material
    /// line serves every element, and the moduli need no material per element and no bins.
    /// </summary>
    public sealed class TopologyAnalysis : IDisposable
    {
        private readonly LiveOpenSees _openSees;
        private readonly IReadOnlyList<SolidCell> _cells;
        private readonly int _nodes;
        private readonly string _moduliFile;
        private readonly string[] _displacementFiles, _forceFiles;

        /// <summary>How many load cases each design is solved for: 1, or the model's load combinations (<see cref="TopologyLoadCases"/>).</summary>
        public int Cases { get; }

        /// <summary>The nodal displacements of the last design solved, three per solid node, by node tag from 1: of the first load case.</summary>
        public double[] Displacements => CaseDisplacements?[0];

        /// <summary>The nodal displacements of the last design solved, as <see cref="Displacements"/>, one array per load case.</summary>
        public double[][] CaseDisplacements { get; private set; }

        /// <summary>
        /// Writes the deck into <paramref name="folder"/> and starts OpenSees on it.
        /// </summary>
        /// <param name="model">The model's Tcl, nodes to loads, with each cell's element line as the cell is to be solved.</param>
        /// <param name="solidNodes">How many three degree of freedom nodes there are: the nodes tagged 1 to this.</param>
        /// <param name="system">The system of equations; null for Auto.</param>
        /// <param name="cases">
        /// How many load cases: 1 for the model's loads as they are, or as many as the model's combinations, with the
        /// deck's patterns written for them by <see cref="TopologyLoadCases.Write(Model, IReadOnlyList{string})"/>.
        /// </param>
        public TopologyAnalysis(string openSees, string folder, string stem, IEnumerable<string> model,
                                IReadOnlyList<SolidCell> cells, int solidNodes, SystemEquation system, TimeSpan? timeout, int cases = 1)
        {
            if (cases < 1)
                throw new ArgumentException("There has to be a load case at least.");
            _cells = cells;
            _nodes = solidNodes;
            Cases = cases;
            _moduliFile = Path.Combine(folder, stem + "_moduli.txt");
            // One case writes the files it always wrote; several, a pair each.
            string Of(string name, int k) => Path.Combine(folder, stem + "_" + name + (cases == 1 ? "" : "_" + (k + 1)) + ".bin");
            _displacementFiles = Enumerable.Range(0, cases).Select(k => Of("displacements", k)).ToArray();
            _forceFiles = Enumerable.Range(0, cases).Select(k => Of("forces", k)).ToArray();

            string deck = Path.Combine(folder, stem + ".tcl");
            File.WriteAllText(deck, Deck(model, cells, solidNodes, system ?? new SystemEquation("Auto"), cases));
            _openSees = new LiveOpenSees(openSees, deck, timeout);
        }

        /// <summary>What OpenSees printed so far.</summary>
        public string Log => _openSees.Log;

        /// <summary>
        /// The deck: the model, a static analysis in one step of load control solved by Linear - the
        /// model is elastic, so one solve is the answer - and a procedure that solves one design, then
        /// the loop that waits for it to be called.
        ///
        /// With several load cases the procedure solves the design once for each: from rest, the time set to
        /// k - 1, to time k, where the patterns' series hold case k's factors (<see cref="TopologyLoadCases"/>).
        /// One case is solved as it always was.
        /// </summary>
        public static string Deck(IEnumerable<string> model, IReadOnlyList<SolidCell> cells, int solidNodes, SystemEquation system, int cases = 1)
        {
            var deck = new StringBuilder();
            foreach (var line in model)
            {
                deck.Append(line);
                if (!line.EndsWith("\n"))
                    deck.Append('\n');
            }

            deck.Append("constraints Transformation\n");
            deck.Append("numberer RCM\n");
            deck.Append(system.WriteTcl());
            deck.Append("test NormDispIncr 1e-8 10\n");
            deck.Append("algorithm Linear\n");
            deck.Append("integrator LoadControl 1\n");
            deck.Append("analysis Static\n");

            deck.Append("set alpacaCells {").Append(string.Join(" ", cells.Select(cell => cell.Tag))).Append("}\n");
            if (cases > 1)
            {
                deck.Append(
$@"proc alpacaSolve {{moduli displacements forces}} {{
    set file [open $moduli r]
    set values [read $file]
    close $file
    foreach tag $::alpacaCells value $values {{
        setParameter -val $value -ele $tag E
    }}
    set case 0
    foreach displacement $displacements force $forces {{
        reset
        setTime $case
        incr case
        set ok [analyze 1]
        if {{$ok < 0}} {{
            error ""analyze returned $ok in load case $case""
        }}
        recorder Node -binary $displacement -nodeRange 1 {solidNodes} -dof 1 2 3 disp
        recorder Element -binary $force -ele {{*}}$::alpacaCells forces
        record
        remove recorders
    }}
}}
");
                deck.Append(LiveOpenSees.CommandLoop);
                return deck.ToString();
            }
            deck.Append(
$@"proc alpacaSolve {{moduli displacements forces}} {{
    set file [open $moduli r]
    set values [read $file]
    close $file
    foreach tag $::alpacaCells value $values {{
        setParameter -val $value -ele $tag E
    }}
    reset
    set ok [analyze 1]
    if {{$ok < 0}} {{
        error ""analyze returned $ok""
    }}
    recorder Node -binary $displacements -nodeRange 1 {solidNodes} -dof 1 2 3 disp
    recorder Element -binary $forces -ele {{*}}$::alpacaCells forces
    record
    remove recorders
}}
");
            deck.Append(LiveOpenSees.CommandLoop);
            return deck.ToString();
        }

        /// <summary>
        /// Solves the design with these moduli, one per cell, and returns u_e . f_e for each cell, summed over the
        /// load cases: of the one case, the case's own.
        /// </summary>
        public double[] Energies(double[] moduli) => Sum(CaseEnergies(moduli));

        /// <summary>The energies of the cells, element by element, added up over the cases; the one case's own when there is one.</summary>
        public static double[] Sum(double[][] cases)
        {
            if (cases.Length == 1)
                return cases[0];
            var sum = (double[])cases[0].Clone();
            for (int k = 1; k < cases.Length; k++)
                for (int c = 0; c < sum.Length; c++)
                    sum[c] += cases[k][c];
            return sum;
        }

        /// <summary>Solves the design with these moduli, one per cell, and returns u_e . f_e for each cell, one array per load case.</summary>
        public double[][] CaseEnergies(double[] moduli)
        {
            if (moduli.Length != _cells.Count)
                throw new ArgumentException($"{moduli.Length} moduli for {_cells.Count} elements.");

            File.WriteAllText(_moduliFile, string.Join(" ", moduli.Select(e => e.ToString("R", CultureInfo.InvariantCulture))));
            foreach (var file in _displacementFiles.Concat(_forceFiles))
                File.Delete(file);

            string command = Cases == 1
                ? $"alpacaSolve {TclPath.Write(_moduliFile)} {TclPath.Write(_displacementFiles[0])} {TclPath.Write(_forceFiles[0])}"
                : $"alpacaSolve {TclPath.Write(_moduliFile)} [list {string.Join(" ", _displacementFiles.Select(TclPath.Write))}] " +
                  $"[list {string.Join(" ", _forceFiles.Select(TclPath.Write))}]";
            string problem = _openSees.Send(command);
            if (problem != null)
                throw new InvalidOperationException("The analysis failed: " + problem + "\n" + Log);

            var displacements = new double[Cases][];
            var energies = new double[Cases][];
            for (int k = 0; k < Cases; k++)
            {
                var u = ReadBinary(_displacementFiles[k], 3 * _nodes);
                var f = ReadBinary(_forceFiles[k], _cells.Sum(cell => 3 * cell.Nodes.Count));
                displacements[k] = u;
                energies[k] = EnergiesOf(u, f);
            }
            CaseDisplacements = displacements;
            return energies;
        }

        private double[] EnergiesOf(double[] u, double[] f)
        {
            var energies = new double[_cells.Count];
            int at = 0;
            for (int c = 0; c < _cells.Count; c++)
            {
                double sum = 0.0;
                foreach (int node in _cells[c].Nodes)
                {
                    int dof = 3 * (node - 1);
                    sum += u[dof] * f[at] + u[dof + 1] * f[at + 1] + u[dof + 2] * f[at + 2];
                    at += 3;
                }
                energies[c] = sum;
            }
            return energies;
        }

        /// <summary>
        /// One record of a binary recorder: the doubles as they lie in memory, then a newline. Little
        /// endian on every machine OpenSees runs on.
        /// </summary>
        private static double[] ReadBinary(string path, int count)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8 * count)
                throw new InvalidOperationException($"{Path.GetFileName(path)} holds {bytes.Length / 8} values, not {count}.");

            var values = new double[count];
            Buffer.BlockCopy(bytes, 0, values, 0, 8 * count);
            return values;
        }

        /// <summary>Closes OpenSees and deletes the files of the last design; the deck stays, to be read.</summary>
        public void Dispose()
        {
            _openSees.Dispose();
            foreach (var file in new[] { _moduliFile }.Concat(_displacementFiles).Concat(_forceFiles))
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>The cells and the deck of an assembled model's solids, for <see cref="TopologyAnalysis"/>.</summary>
    public static class TopologyDeck
    {
        /// <summary>
        /// The model's Tcl with each SSP Brick written as a stdBrick on the same nodes, and a cell for
        /// every solid, in the order of <see cref="Model.Bricks"/>.
        ///
        /// Why not the SSP Brick itself: it works out its stabilisation stiffness once, from the modulus
        /// it starts with (SSPbrick::GetStab, from setDomain), so a modulus set afterwards reaches only
        /// the part of its stiffness that comes from the strain at its centre. A void element would keep
        /// all of its stiffness against the modes the stabilisation holds. stdBrick integrates at eight
        /// points, each with its own copy of the material, and so follows a new modulus entirely. It is
        /// the element top3d optimises with, too - the eight node brick of full integration. The
        /// tetrahedra need no stand-in: both pass a modulus to all their integration points.
        /// </summary>
        public static List<string> From(Model model, Func<IBrick, bool> passive, out List<SolidCell> cells)
        {
            cells = new List<SolidCell>();
            var lines = new Dictionary<string, string>();

            foreach (var brick in model.Bricks)
            {
                if (!(brick.Material is Alpaca4d.Material.ElasticIsotropicMaterial material))
                    throw new ArgumentException($"Element {brick.Id} is not of an elastic isotropic material. " +
                                                "Topology optimisation scales the modulus of an Elastic Isotropic Material.");

                var shape = ShapeOf(brick);
                var nodes = brick.IndexNodes.Select(tag => tag ?? throw new ArgumentException($"Element {brick.Id} has no nodes: assemble the model first.")).ToList();
                var vertices = brick.Mesh.Vertices.ToPoint3dArray();
                int corners = SolidCell.CornerCount(shape);
                var centroid = new Rhino.Geometry.Point3d(
                    vertices.Take(corners).Average(v => v.X), vertices.Take(corners).Average(v => v.Y), vertices.Take(corners).Average(v => v.Z));

                cells.Add(new SolidCell(brick.Id ?? 0, shape, nodes, centroid, Utils.MeshVolume(brick.Mesh), material.E)
                {
                    Passive = passive != null && passive(brick),
                });

                string line = brick.WriteTcl();
                lines[line] = shape == SolidShape.Hexahedron
                    ? $"element stdBrick {brick.Id} {string.Join(" ", nodes)} {brick.Material.Id}\n"
                    : line;
            }

            int replaced = 0;
            var tcl = model.Tcl.Select(item =>
            {
                if (!lines.TryGetValue(item, out var own))
                    return item;
                replaced++;
                return own;
            }).ToList();

            if (replaced != model.Bricks.Count)
                throw new InvalidOperationException($"Found {replaced} of the {model.Bricks.Count} solid elements in the model's deck. Assemble the model again.");

            return tcl;
        }

        private static SolidShape ShapeOf(IBrick brick)
        {
            switch (brick.ElementClass)
            {
                case ElementClass.SSPBrick: return SolidShape.Hexahedron;
                case ElementClass.TenNodeTetrahedron: return SolidShape.QuadraticTetrahedron;
                case ElementClass.FourNodeTetrahedron: return SolidShape.Tetrahedron;
                default: throw new ArgumentException($"Element {brick.Id} is a {brick.ElementClass}, which topology optimisation does not take.");
            }
        }
    }
}
