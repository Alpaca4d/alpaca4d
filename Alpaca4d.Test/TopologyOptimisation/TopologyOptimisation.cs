// Topology optimisation (Alpaca4d.Optimisation.TopologyOptimiser, TopologyAnalysis).
//
// The pieces first - the density filter against top3d's, its chain rule, the OpenSees that stays open -
// then whole optimisations of top3d's own problems on a grid of bricks, solved by OpenSees. The grid
// and its deck are written here, as Assemble would write them for SSP Bricks drawn as cubes, since
// assembling needs the native Rhino library.
//
//     mono TopologyOptimisation.exe <OpenSees> <folder>                 the checks
//     mono TopologyOptimisation.exe <OpenSees> <folder> bench <name>    one benchmark, written to <folder>
//
// The benchmarks write <name>_history.csv and <name>_densities.csv, which reference.py compares with
// its own top3d.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Alpaca4d;
using Alpaca4d.Optimisation;
using Rhino.Geometry;

class TopologyOptimisationTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Section(string title) => Console.WriteLine($"\n{title}");

    static string N(double x) => x.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// A block of unit cubes, nx along the span (x), ny deep (y), nz high (z): top3d's nelx, nelz
    /// and nely, with z up. Nodes and elements are tagged from 1, x fastest.
    /// </summary>
    class Grid
    {
        public readonly int Nx, Ny, Nz;
        public readonly double Size;
        public Grid(int nx, int ny, int nz, double size = 1.0) { Nx = nx; Ny = ny; Nz = nz; Size = size; }

        public int Nodes => (Nx + 1) * (Ny + 1) * (Nz + 1);
        public int Node(int i, int j, int k) => 1 + i + j * (Nx + 1) + k * (Nx + 1) * (Ny + 1);
        public int Element(int i, int j, int k) => 1 + i + j * Nx + k * Nx * Ny;

        public IEnumerable<(int i, int j, int k)> Elements()
        {
            for (int k = 0; k < Nz; k++)
                for (int j = 0; j < Ny; j++)
                    for (int i = 0; i < Nx; i++)
                        yield return (i, j, k);
        }

        /// <summary>Counter-clockwise underneath, seen from above, then the same on top: OpenSees's brick order.</summary>
        public int[] Corners(int i, int j, int k) => new[]
        {
            Node(i, j, k), Node(i + 1, j, k), Node(i + 1, j + 1, k), Node(i, j + 1, k),
            Node(i, j, k + 1), Node(i + 1, j, k + 1), Node(i + 1, j + 1, k + 1), Node(i, j + 1, k + 1),
        };

        public List<SolidCell> Cells(double e, Func<int, int, int, bool> passive = null) => Elements()
            .Select(c => new SolidCell(Element(c.i, c.j, c.k), SolidShape.Hexahedron, Corners(c.i, c.j, c.k),
                new Point3d((c.i + 0.5) * Size, (c.j + 0.5) * Size, (c.k + 0.5) * Size), Size * Size * Size, e)
            { Passive = passive != null && passive(c.i, c.j, c.k) })
            .ToList();

        /// <summary>The deck as Assemble writes it, with each brick already a stdBrick.</summary>
        public List<string> Tcl(double e, double nu, IEnumerable<(int node, string dofs)> fixes, IEnumerable<(int node, double fz)> loads)
        {
            var tcl = new List<string> { "wipe\n", "model BasicBuilder -ndm 3 -ndf 3\n" };
            for (int k = 0; k <= Nz; k++)
                for (int j = 0; j <= Ny; j++)
                    for (int i = 0; i <= Nx; i++)
                        tcl.Add($"node {Node(i, j, k)} {N(i * Size)} {N(j * Size)} {N(k * Size)}\n");
            foreach (var fix in fixes)
                tcl.Add($"fix {fix.node} {fix.dofs}\n");
            tcl.Add($"nDMaterial ElasticIsotropic 1 {N(e)} {N(nu)} 0.0\n");
            foreach (var c in Elements())
                tcl.Add($"element stdBrick {Element(c.i, c.j, c.k)} {string.Join(" ", Corners(c.i, c.j, c.k))} 1\n");
            tcl.Add("timeSeries Linear 1\n");
            tcl.Add("pattern Plain 1 1 {\n" + string.Concat(loads.Select(l => $"    load {l.node} 0.0 0.0 {N(l.fz)}\n")) + "}\n");
            return tcl;
        }
    }

    /// <summary>A topology problem: the grid, its supports and loads, and the settings.</summary>
    class Problem
    {
        public string Name;
        public Grid Grid;
        public List<(int node, string dofs)> Fixes = new List<(int, string)>();
        public List<(int node, double fz)> Loads = new List<(int, double)>();
        public TopologySettings Settings = new TopologySettings();
        public Func<int, int, int, bool> Passive;
        public double E = 1.0, Nu = 0.3;

        public double Work(double[] u) => Loads.Sum(l => l.fz * u[3 * (l.node - 1) + 2]);
    }

    /// <summary>
    /// top3d's own problem, top3d(60,20,4,0.3,3,1.5): a cantilever 60 long, 20 high and 4 deep, held
    /// at x = 0 and loaded by -1 at every node of the bottom edge of its free end.
    /// </summary>
    static Problem Cantilever(int nx = 60, int nz = 20, int ny = 4)
    {
        var grid = new Grid(nx, ny, nz);
        var problem = new Problem { Name = "cantilever", Grid = grid };
        for (int k = 0; k <= nz; k++)
            for (int j = 0; j <= ny; j++)
                problem.Fixes.Add((grid.Node(0, j, k), "1 1 1"));
        for (int j = 0; j <= ny; j++)
            problem.Loads.Add((grid.Node(nx, j, 0), -1.0));
        problem.Settings = new TopologySettings { VolumeFraction = 0.3, Penalty = 3.0, FilterRadius = 1.5, MaxIterations = 200, Tolerance = 0.01 };
        return problem;
    }

    /// <summary>
    /// A simply supported beam, 120 long, 20 high and 4 deep: pinned along the bottom edge at x = 0,
    /// on rollers along the bottom edge at the other end, and loaded by -1 at every node of the top
    /// edge at mid-span. The MBB beam drawn whole instead of halved.
    /// </summary>
    static Problem SimplySupported(int nx = 120, int nz = 20, int ny = 4)
    {
        var grid = new Grid(nx, ny, nz);
        var problem = new Problem { Name = "simply-supported", Grid = grid };
        for (int j = 0; j <= ny; j++)
        {
            problem.Fixes.Add((grid.Node(0, j, 0), "1 1 1"));
            problem.Fixes.Add((grid.Node(nx, j, 0), "0 1 1"));
            problem.Loads.Add((grid.Node(nx / 2, j, nz), -1.0));
        }
        problem.Settings = new TopologySettings { VolumeFraction = 0.3, Penalty = 3.0, FilterRadius = 1.5, MaxIterations = 200, Tolerance = 0.01 };
        return problem;
    }

    /// <summary>
    /// Tools/Examples/TopologyBridge.gh, at three bricks per metre as it opens, or as many as given: a steel
    /// block 16 x 2 x 4 m, pinned along both ends of its bottom, its top layer a deck kept solid under
    /// 10 kN/m2. Units kN, m.
    /// </summary>
    static Problem Bridge(int perMetre = 3)
    {
        var grid = new Grid(16 * perMetre, 2 * perMetre, 4 * perMetre, 1.0 / perMetre);
        var problem = new Problem { Name = "bridge", Grid = grid, E = 210e6, Nu = 0.3 };
        for (int j = 0; j <= grid.Ny; j++)
        {
            problem.Fixes.Add((grid.Node(0, j, 0), "1 1 1"));
            problem.Fixes.Add((grid.Node(grid.Nx, j, 0), "1 1 1"));
        }
        int top = (grid.Nx + 1) * (grid.Ny + 1);
        for (int j = 0; j <= grid.Ny; j++)
            for (int i = 0; i <= grid.Nx; i++)
                problem.Loads.Add((grid.Node(i, j, grid.Nz), -10.0 * 16.0 * 2.0 / top));
        problem.Passive = (i, j, k) => k == grid.Nz - 1;
        problem.Settings = new TopologySettings { VolumeFraction = 0.2, MaxIterations = 100 };
        return problem;
    }

    /// <summary>
    /// A cube of n bricks a side, held at its four bottom corners and loaded at the middle of its top: a
    /// result in three dimensions, unlike the cantilever and the beam, which are the same through their
    /// depth. The column of elements under the load is kept.
    /// </summary>
    static Problem Tower(int n = 16)
    {
        var grid = new Grid(n, n, n);
        var problem = new Problem { Name = "tower", Grid = grid };
        foreach (int i in new[] { 0, n - 1 })
            foreach (int j in new[] { 0, n - 1 })
                for (int di = 0; di <= 1; di++)
                    for (int dj = 0; dj <= 1; dj++)
                        problem.Fixes.Add((grid.Node(i + di, j + dj, 0), "1 1 1"));
        int c = n / 2;
        for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
                problem.Loads.Add((grid.Node(c + di, c + dj, n), -1.0));
        problem.Passive = (i, j, k) => k == n - 1 && Math.Abs(i - c + 0.5) < 1.5 && Math.Abs(j - c + 0.5) < 1.5;
        problem.Settings = new TopologySettings { VolumeFraction = 0.08, FilterRadius = 1.5, MaxIterations = 100 };
        return problem;
    }

    static TopologyAnalysis Start(Problem problem, string openSees, string folder, List<SolidCell> cells)
    {
        var tcl = problem.Grid.Tcl(problem.E, problem.Nu, problem.Fixes, problem.Loads);
        return new TopologyAnalysis(openSees, folder, problem.Name, tcl, cells, problem.Grid.Nodes,
                                    new Alpaca4d.SystemEquation("Auto"), TimeSpan.FromMinutes(10));
    }

    static void Main(string[] args)
    {
        string openSees = args[0], folder = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(folder);

        if (args.Length >= 4 && args[2] == "bench")
        {
            Bench(args[3], openSees, folder, args.Length >= 5 ? int.Parse(args[4]) : 0);
            return;
        }

        FilterChecks();
        SurfaceChecks();
        ProtocolChecks(openSees, folder);
        EnergyChecks(openSees, folder);
        SmallRun(openSees, folder);
        GoingOn(openSees, folder);
        Frames(openSees, folder);

        Console.WriteLine(fails == 0 ? "\nAll checks passed." : $"\n{fails} check(s) FAILED.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static void FilterChecks()
    {
        Section("Density filter");
        var grid = new Grid(6, 5, 4);
        var cells = grid.Cells(1.0);
        var filter = new DensityFilter(cells, 1.5);

        // top3d's H on a grid: rmin - distance, over the 3x3x3 box, which keeps the element itself
        // (1.5), its six face neighbours (0.5) and its twelve edge neighbours (1.5 - sqrt 2).
        Check(filter.MaxNeighbours == 19, $"an inner element has 19 neighbours, as in top3d (got {filter.MaxNeighbours})");

        var unit = filter.Apply(Enumerable.Repeat(1.0, cells.Count).ToArray());
        Check(unit.All(v => Math.Abs(v - 1.0) < 1e-14), "a uniform density filters to itself");

        // One solid element in the middle: its own weight over the sum of all of them, top3d's H_ii / Hs_i.
        int middle = grid.Element(3, 2, 2) - 1;
        var spike = new double[cells.Count];
        spike[middle] = 1.0;
        var spread = filter.Apply(spike);
        double hs = 1.5 + 6 * 0.5 + 12 * (1.5 - Math.Sqrt(2.0));
        Check(Math.Abs(spread[middle] - 1.5 / hs) < 1e-14, $"the element keeps 1.5/Hs of itself ({spread[middle]:0.000000} = {1.5 / hs:0.000000})");
        int face = grid.Element(4, 2, 2) - 1;
        Check(Math.Abs(spread[face] - 0.5 / hs) < 1e-14, "a face neighbour gets 0.5/Hs of it");

        // Back is the transpose of Apply: <Apply x, y> = <x, Back y>.
        var random = new Random(7);
        var x = cells.Select(_ => random.NextDouble()).ToArray();
        var y = cells.Select(_ => random.NextDouble() - 0.5).ToArray();
        double left = filter.Apply(x).Zip(y, (a, b) => a * b).Sum();
        double right = x.Zip(filter.Back(y), (a, b) => a * b).Sum();
        Check(Math.Abs(left - right) < 1e-12 * Math.Abs(left), $"Back is the transpose of Apply ({left:G15} vs {right:G15})");

        // Elements of different sizes count by volume: a big neighbour pulls harder than a small one.
        var mixed = new List<SolidCell>
        {
            new SolidCell(1, SolidShape.Tetrahedron, new[] { 1, 2, 3, 4 }, new Point3d(0, 0, 0), 1.0, 1.0),
            new SolidCell(2, SolidShape.Tetrahedron, new[] { 1, 2, 3, 5 }, new Point3d(1, 0, 0), 8.0, 1.0),
        };
        var weighted = new DensityFilter(mixed, 2.0).Apply(new[] { 0.0, 1.0 });
        Check(Math.Abs(weighted[0] - 8.0 / 10.0) < 1e-14, $"neighbours weigh by volume (got {weighted[0]}, 0.8 expected)");
        Check(Math.Abs(DensityFilter.DefaultRadius(cells) - 1.5) < 1e-12, "the default radius is 1.5 elements on a grid of cubes");
    }

    /// <summary>
    /// Whether the triangles close up: every edge in exactly two triangles, once each way, which is
    /// also what makes the facing consistent. And the volume they enclose, by the divergence theorem.
    /// </summary>
    static (bool closed, double volume) Closure(DensitySurface surface)
    {
        var directed = new Dictionary<(int, int), int>();
        foreach (var t in surface.Triangles)
            for (int e = 0; e < 3; e++)
            {
                var edge = (t[e], t[(e + 1) % 3]);
                directed[edge] = directed.TryGetValue(edge, out int n) ? n + 1 : 1;
            }
        bool closed = directed.All(kv => kv.Value == 1 && directed.TryGetValue((kv.Key.Item2, kv.Key.Item1), out int back) && back == 1);

        double volume = 0.0;
        foreach (var t in surface.Triangles)
        {
            var a = surface.Vertices[t[0]]; var b = surface.Vertices[t[1]]; var c = surface.Vertices[t[2]];
            volume += Vector3d.CrossProduct(new Vector3d(b), new Vector3d(c)) * new Vector3d(a) / 6.0;
        }
        return (closed, volume);
    }

    static void SurfaceChecks()
    {
        Section("Density surface");
        var grid = new Grid(10, 6, 8, 0.5);
        var cells = grid.Cells(1.0);
        int nx = grid.Nx + 1, ny = grid.Ny + 1;
        Func<int, Point3d> node = tag =>
            new Point3d(((tag - 1) % nx) * grid.Size, ((tag - 1) / nx % ny) * grid.Size, ((tag - 1) / (nx * ny)) * grid.Size);

        // All solid: the surface is the boundary of the domain.
        var r = Closure(new DensitySurface(cells, cells.Select(_ => 1.0).ToList(), node, 0.5));
        Check(r.closed, "a solid domain's surface is closed");
        Check(Math.Abs(r.volume - 5.0 * 3.0 * 4.0) < 1e-9, $"and encloses the domain ({r.volume:0.000000} of 60)");

        // A solid block of 4 x 2 x 4 elements inside empty ones: the iso-surface at 0.5 rounds its
        // corners off, so it encloses about the block, and never more than the block and its halo.
        var block = grid.Elements().Select(c => c.i >= 3 && c.i < 7 && c.j >= 2 && c.j < 4 && c.k >= 2 && c.k < 6 ? 1.0 : 0.0).ToList();
        r = Closure(new DensitySurface(cells, block, node, 0.5));
        double blockVolume = 4 * 2 * 4 * 0.125;
        Check(r.closed, "a block of material inside the domain gives a closed surface");
        Check(r.volume > 0.6 * blockVolume && r.volume < 1.3 * blockVolume, $"around the block's volume ({r.volume:0.000} for {blockVolume:0.000})");

        // The block touching the domain's side: the surface is capped there and still closed.
        var side = grid.Elements().Select(c => c.i < 4 && c.j >= 2 && c.j < 4 ? 1.0 : 0.0).ToList();
        r = Closure(new DensitySurface(cells, side, node, 0.5));
        Check(r.closed, "material reaching the edge of the domain is capped there and closed");

        // A density field with noise: whatever it is, the surface closes.
        var random = new Random(11);
        var noise = cells.Select(_ => random.NextDouble()).ToList();
        r = Closure(new DensitySurface(cells, noise, node, 0.5));
        Check(r.closed, "a random density field still gives a closed surface");
        Check(r.volume > 0.0 && r.volume < 60.0, $"inside the domain ({r.volume:0.000} of 60)");

        // Tetrahedra: a cube of six, all solid.
        var corners = new[] { new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0), new Point3d(0, 1, 0),
                              new Point3d(0, 0, 1), new Point3d(1, 0, 1), new Point3d(1, 1, 1), new Point3d(0, 1, 1) };
        int[][] six = { new[] { 0, 1, 2, 6 }, new[] { 0, 2, 3, 6 }, new[] { 0, 3, 7, 6 }, new[] { 0, 7, 4, 6 }, new[] { 0, 4, 5, 6 }, new[] { 0, 5, 1, 6 } };
        var tets = six.Select((t, n) => new SolidCell(n + 1, SolidShape.Tetrahedron, t.Select(k => k + 1).ToArray(),
            new Point3d(t.Average(k => corners[k].X), t.Average(k => corners[k].Y), t.Average(k => corners[k].Z)), 1.0 / 6.0, 1.0)).ToList();
        r = Closure(new DensitySurface(tets, tets.Select(_ => 1.0).ToList(), tag => corners[tag - 1], 0.5));
        Check(r.closed && Math.Abs(r.volume - 1.0) < 1e-12, $"six solid tetrahedra close up into their cube ({r.volume:0.000000})");
        r = Closure(new DensitySurface(tets, new[] { 1.0, 0.0, 1.0, 0.0, 1.0, 0.0 }, tag => corners[tag - 1], 0.5));
        Check(r.closed, "and every other one solid still closes");
    }

    static void ProtocolChecks(string openSees, string folder)
    {
        Section("OpenSees kept open");
        string deck = Path.Combine(folder, "protocol.tcl");
        File.WriteAllText(deck, "set alpacaValue 41\n" + LiveOpenSees.CommandLoop);
        using (var live = new LiveOpenSees(openSees, deck, TimeSpan.FromSeconds(30)))
        {
            Check(live.Send("incr alpacaValue") == null, "a command that works answers with nothing");
            string problem = live.Send("no_such_command 1 2");
            Check(problem != null && problem.Contains("no_such_command"), $"one that fails answers with its error ({problem})");
            Check(live.Send("if {$alpacaValue != 42} { error \"lost the value\" }") == null, "and OpenSees stays open, with its state");
            string multiLine = live.Send("error \"one\\ntwo\"");
            Check(multiLine == "one two", $"an error over several lines comes back on one ({multiLine})");
        }

        File.WriteAllText(deck, "set broken [\n" + LiveOpenSees.CommandLoop);
        try
        {
            using (new LiveOpenSees(openSees, deck, TimeSpan.FromSeconds(30))) { }
            Check(false, "a deck that stops before its loop is refused");
        }
        catch (InvalidOperationException ex)
        {
            Check(ex.Message.Contains("OpenSees"), "a deck that stops before its loop is refused, with what OpenSees printed");
        }
    }

    /// <summary>The energies OpenSees's forces give are the work of the loads, element by element.</summary>
    static void EnergyChecks(string openSees, string folder)
    {
        Section("Element energies");
        var problem = Cantilever(8, 4, 2);
        problem.Name = "energy";
        var cells = problem.Grid.Cells(problem.E);
        using (var analysis = Start(problem, openSees, folder, cells))
        {
            var random = new Random(3);
            var moduli = cells.Select(_ => Math.Pow(random.NextDouble(), 3) + 1e-3).ToArray();
            var energies = analysis.Energies(moduli);
            double work = problem.Work(analysis.Displacements);
            Check(Math.Abs(energies.Sum() - work) < 1e-9 * Math.Abs(work),
                $"the energies of uneven moduli add up to F.u ({energies.Sum():G12} vs {work:G12})");
            Check(energies.All(w => w > 0.0), "every element's energy is positive");

            // A stiffer element stores less for the same strain: halving every modulus doubles every energy.
            var halved = analysis.Energies(moduli.Select(e => e / 2.0).ToArray());
            double worst = energies.Zip(halved, (a, b) => Math.Abs(b / a - 2.0)).Max();
            Check(worst < 1e-9, $"halving every modulus doubles every energy (worst {worst:G3})");

            // Solving the first design again gives the same answer: nothing is left over from the last one.
            var again = analysis.Energies(moduli);
            double drift = energies.Zip(again, (a, b) => Math.Abs(a - b) / a).Max();
            Check(drift < 1e-12, $"solving a design again gives the same energies (worst {drift:G3})");

            // The uniform cantilever against Euler-Bernoulli plus shear: a sanity bound, not an exact value.
            analysis.Energies(cells.Select(_ => 1.0).ToArray());
            double tip = -analysis.Displacements[3 * (problem.Grid.Node(8, 1, 0) - 1) + 2];
            double inertia = 2.0 * Math.Pow(4.0, 3) / 12.0, area = 8.0, load = 3.0;
            double beam = load * Math.Pow(8.0, 3) / (3.0 * inertia) + load * 8.0 / (5.0 / 6.0 * area / (2 * 1.3));
            Check(tip > 0.7 * beam && tip < 1.1 * beam, $"the solid cantilever's tip is near beam theory ({tip:0.00} vs {beam:0.00})");
        }
    }

    /// <summary>A small optimisation from end to end: it converges, keeps the volume, and keeps passive elements.</summary>
    static void SmallRun(string openSees, string folder)
    {
        Section("A small optimisation");
        var problem = Cantilever(24, 8, 2);
        problem.Name = "small";
        // The column of elements under the load kept solid.
        problem.Passive = (i, j, k) => i == 23 && k <= 1;
        problem.Settings.MaxIterations = 60;
        var cells = problem.Grid.Cells(problem.E, problem.Passive);

        TopologyResult result;
        using (var analysis = Start(problem, openSees, folder, cells))
            result = TopologyOptimiser.Run(cells, problem.Settings, analysis.Energies);

        var design = Enumerable.Range(0, cells.Count).Where(i => !cells[i].Passive).ToList();
        double volume = design.Sum(i => result.Densities[i]) / design.Count;
        Check(Math.Abs(volume - 0.3) < 2e-3, $"the design keeps 30% of the volume ({volume:0.0000})");
        Check(cells.Where(c => c.Passive).All(c => result.Densities[cells.IndexOf(c)] == 1.0), "passive elements stay solid");
        var history = result.History;
        Check(history.Last().Compliance < 0.5 * history.First().Compliance,
            $"the compliance falls from {history.First().Compliance:0.0} to {history.Last().Compliance:0.0}");
        // Sigmund's measure of non-discreteness, 4 x (1 - x) on average: 0.84 for the uniform 0.3 the run
        // starts from, 0 for nothing but solid and void. The filter keeps a grey band about a radius wide
        // around every member, and on this small grid a member is only two or three elements thick, so
        // much of it stays grey; the benchmarks, on finer grids, end at 0.27 and 0.23, as top3d does.
        double grey = design.Average(i => 4.0 * result.Densities[i] * (1.0 - result.Densities[i]));
        Check(grey < 0.6, $"the densities move away from grey to solid and void (non-discreteness {grey:0.000}, from 0.84)");
        Console.WriteLine($"  {history.Count} iterations, converged {result.Converged}, compliance {N(history.First().Compliance)} first, " +
                          $"{N(history.Last().Compliance)} last (GrasshopperChecks.cs solves the same problem)");
    }

    /// <summary>A run stopped after 15 iterations and taken on to 40 is the run of 40, iteration for iteration.</summary>
    static void GoingOn(string openSees, string folder)
    {
        Section("Going on from where a run stopped");
        var problem = Cantilever(16, 6, 2);
        problem.Name = "going-on";
        var cells = problem.Grid.Cells(problem.E);

        TopologyResult whole, first, rest;
        problem.Settings.MaxIterations = 40;
        using (var analysis = Start(problem, openSees, folder, cells))
            whole = TopologyOptimiser.Run(cells, problem.Settings, analysis.Energies);

        problem.Settings.MaxIterations = 15;
        using (var analysis = Start(problem, openSees, folder, cells))
            first = TopologyOptimiser.Run(cells, problem.Settings, analysis.Energies);
        Check(first.History.Count == 15 && !first.Converged && first.Design != null, "a run of 15 stops short, with its design densities");

        // In a new OpenSees, as after Iterations is raised in Grasshopper.
        problem.Settings.MaxIterations = 40;
        int reported = 0;
        using (var analysis = Start(problem, openSees, folder, cells))
            rest = TopologyOptimiser.Run(cells, problem.Settings, analysis.Energies, progress: (it, x) => reported++, previous: first);

        Check(rest.History.Count == whole.History.Count, $"taken on to 40, it makes as many iterations as one run ({rest.History.Count}, {whole.History.Count})");
        Check(reported == rest.History.Count - 15 && rest.History[15].Iteration == 16, $"analysing only the ones after the 15th ({reported})");
        double worst = rest.History.Zip(whole.History, (a, b) => Math.Abs(a.Compliance / b.Compliance - 1.0)).Max();
        Check(worst < 1e-9, $"with the same compliance every iteration (worst {worst:G3} apart)");
        double far = rest.Densities.Zip(whole.Densities, (a, b) => Math.Abs(a - b)).Max();
        Check(far < 1e-9 && rest.Converged == whole.Converged, $"and the same design at the end (densities {far:G3} apart)");

        // Nothing left to do: a run asked to go on to where it already is returns it as it was.
        var again = TopologyOptimiser.Run(cells, new TopologySettings { MaxIterations = 15, VolumeFraction = 0.3, FilterRadius = 1.5 },
                                          moduli => { throw new InvalidOperationException("analysed"); }, previous: first);
        Check(again.History.Count == 15 && again.Densities.SequenceEqual(first.Densities), "taken on to where it already is, it analyses nothing");
    }

    /// <summary>The design after every iteration, kept small, read back as it was.</summary>
    static void Frames(string openSees, string folder)
    {
        Section("Every iteration's design");

        // The store on its own: whole frames and changes, read back to the byte, any frame in any order.
        var random = new Random(5);
        var store = new DensityHistory();
        var made = new List<double[]>();
        var field = Enumerable.Range(0, 500).Select(_ => random.NextDouble()).ToArray();
        for (int f = 0; f < 40; f++)
        {
            field = field.Select(d => Math.Max(0.0, Math.Min(1.0, d + 0.05 * (random.NextDouble() - 0.5)))).ToArray();
            made.Add(field);
            store.Add(field);
        }
        double worst = 0.0;
        foreach (int f in new[] { 39, 0, 17, 16, 15, 31, 32, 1 })
            worst = Math.Max(worst, store[f].Zip(made[f], (a, b) => Math.Abs(a - b)).Max());
        Check(worst <= 0.5 / 255.0 + 1e-15, $"any frame reads back to within half a byte, 0.002 ({worst:G3})");
        Check(store[5].Zip(made[5], (a, b) => a == DensityHistory.Quantise(b) / 255.0).All(same => same), "exactly the byte it was stored as");
        var copy = store.Copy();
        copy.Add(field);
        Check(store.Count == 40 && copy.Count == 41, "a copy goes on apart from what it was copied from");

        // A run keeps one frame per iteration and the one it starts from, and going on carries them on.
        var problem = Cantilever(16, 6, 2);
        problem.Name = "frames";
        var cells = problem.Grid.Cells(problem.E);
        problem.Settings.MaxIterations = 12;
        TopologyResult first, rest;
        var frames = new DensityHistory();
        var steps = new List<double[]>();
        using (var analysis = Start(problem, openSees, folder, cells))
            first = TopologyOptimiser.Run(cells, problem.Settings, analysis.Energies, progress: (it, x) => steps.Add(x), frames: frames);
        Check(frames.Count == 13, $"a run of 12 iterations keeps 13 designs, the first the starting one ({frames.Count})");
        Check(frames[0].All(d => Math.Abs(d - 0.3) < 0.5 / 255.0), "frame 0 is the uniform 0.3 the run starts from");
        double off = Enumerable.Range(1, 12).Max(k => frames[k].Zip(steps[k - 1], (a, b) => Math.Abs(a - b)).Max());
        Check(off <= 0.5 / 255.0 + 1e-15, $"frame k is the design after iteration k ({off:G3} apart at most)");

        problem.Settings.MaxIterations = 20;
        var more = first.Frames.Copy();
        using (var analysis = Start(problem, openSees, folder, cells))
            rest = TopologyOptimiser.Run(cells, problem.Settings, analysis.Energies, previous: first, frames: more);
        Check(more.Count == 21 && first.Frames.Count == 13, $"going on to 20 keeps 21, and leaves the first run's 13 alone ({more.Count}, {first.Frames.Count})");
        Check(more[20].Zip(rest.Densities, (a, b) => Math.Abs(a - b)).Max() <= 0.5 / 255.0 + 1e-15, "the last frame is the run's last design");
    }

    static void Bench(string name, string openSees, string folder, int iterations)
    {
        var problem = name == "cantilever" ? Cantilever() : name == "simply-supported" ? SimplySupported()
            : name == "cantilever-20k" ? Cantilever(80, 30, 8) : name == "bridge" ? Bridge() : name == "tower" ? Tower()
            : name.StartsWith("bridge-") ? Bridge(int.Parse(name.Substring(7))) : throw new ArgumentException(name);
        if (name != problem.Name)
            problem.Name = name;
        if (iterations > 0)
            problem.Settings.MaxIterations = iterations;

        var cells = problem.Grid.Cells(problem.E, problem.Passive);
        Console.WriteLine($"{problem.Name}: {cells.Count} elements, {3 * problem.Grid.Nodes} dofs, {openSees}");

        var clock = Stopwatch.StartNew();
        TopologyResult result;
        double startSeconds;
        using (var analysis = Start(problem, openSees, folder, cells))
        {
            startSeconds = clock.Elapsed.TotalSeconds;
            result = TopologyOptimiser.Run(cells, problem.Settings, analysis.Energies,
                progress: (it, x) => { if (it.Iteration % 10 == 0 || it.Iteration == 1) Console.WriteLine("  " + it); },
                frames: new DensityHistory());
        }

        // How small every iteration's design is kept, against doubles and against plain bytes.
        var kept = result.Frames;
        long whole = 0;
        for (int k = 0; k < kept.Count; k++)
        {
            var bytes = kept[k].Select(DensityHistory.Quantise).ToArray();
            using (var packed = new MemoryStream())
            {
                using (var deflate = new System.IO.Compression.DeflateStream(packed, System.IO.Compression.CompressionLevel.Optimal, true))
                    deflate.Write(bytes, 0, bytes.Length);
                whole += packed.Length;
            }
        }
        Console.WriteLine($"  {kept.Count} designs kept in {kept.Bytes / 1024.0:0} KB: as doubles {8.0 * kept.Count * cells.Count / 1048576.0:0.0} MB, " +
                          $"as bytes {kept.Count * cells.Count / 1024.0:0} KB, each byte frame deflated whole {whole / 1024.0:0} KB; " +
                          $"{8.0 * kept.Count * cells.Count / kept.Bytes:0}x smaller than doubles");

        var history = new StringBuilder("iteration,compliance,volume,change,analysis_s,update_s\n");
        foreach (var it in result.History)
            history.AppendLine(string.Join(",", it.Iteration, N(it.Compliance), N(it.Volume), N(it.Change), N(it.AnalysisSeconds), N(it.UpdateSeconds)));
        File.WriteAllText(Path.Combine(folder, problem.Name + "_history.csv"), history.ToString());

        var densities = new StringBuilder("i,j,k,density\n");
        var g = problem.Grid;
        int at = 0;
        foreach (var c in g.Elements())
            densities.AppendLine($"{c.i},{c.j},{c.k},{N(result.Densities[at++])}");
        File.WriteAllText(Path.Combine(folder, problem.Name + "_densities.csv"), densities.ToString());

        // How much the surface at 0.5 encloses, against the material and the elements at 0.5 or more; and
        // the surface itself, as an OBJ to look at.
        int nx = g.Nx + 1, ny = g.Ny + 1;
        Func<int, Point3d> node = tag => new Point3d((tag - 1) % nx * g.Size, (tag - 1) / nx % ny * g.Size, (tag - 1) / (nx * ny) * g.Size);
        var mesh = new DensitySurface(cells, result.Densities, node, 0.5);
        var surface = Closure(mesh);
        double material = result.Densities.Select((d, i) => d * cells[i].Volume).Sum();
        Console.WriteLine($"  surface at 0.5: closed {surface.closed}, encloses {surface.volume:0.0}; material {material:0.0}; " +
                          $"elements at 0.5 or more {result.Densities.Count(d => d >= 0.5)}");
        var obj = new StringBuilder();
        foreach (var v in mesh.Vertices)
            obj.AppendLine($"v {N(v.X)} {N(v.Y)} {N(v.Z)}");
        foreach (var t in mesh.Triangles)
            obj.AppendLine($"f {t[0] + 1} {t[1] + 1} {t[2] + 1}");
        File.WriteAllText(Path.Combine(folder, problem.Name + "_surface.obj"), obj.ToString());

        var last = result.History.Last();
        Console.WriteLine($"  {last}");
        Console.WriteLine($"  {result.History.Count} iterations, converged {result.Converged}, OpenSees start {startSeconds:0.00} s, " +
                          $"total {clock.Elapsed.TotalSeconds:0.0} s, analysis {result.History.Sum(h => h.AnalysisSeconds):0.0} s, " +
                          $"update {result.History.Sum(h => h.UpdateSeconds):0.0} s");
    }
}
