using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Rhino.Geometry;
using Alpaca4d.Optimisation;

static class TopologyMethods
{
    static int fails;

    static void Check(bool ok, string what)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
        if (!ok) fails++;
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
        public Func<int, int, int, bool> Passive;
        public double E = 1.0, Nu = 0.3;

        public double Work(double[] u) => Loads.Sum(l => l.fz * u[3 * (l.node - 1) + 2]);
    }

    /// <summary>
    /// A cantilever nx long, nz high and ny deep, held at x = 0 and loaded by -1 at every node of the
    /// bottom edge of its free end: top3d's, and Zheng et al.'s 3D cantilever, with their nely as nz.
    /// </summary>
    static Problem Cantilever(int nx, int nz, int ny, string name = "cantilever")
    {
        var grid = new Grid(nx, ny, nz);
        var problem = new Problem { Name = name, Grid = grid };
        for (int k = 0; k <= nz; k++)
            for (int j = 0; j <= ny; j++)
                problem.Fixes.Add((grid.Node(0, j, k), "1 1 1"));
        for (int j = 0; j <= ny; j++)
            problem.Loads.Add((grid.Node(nx, j, 0), -1.0));
        return problem;
    }

    /// <summary>
    /// esoL.m's case 2, the clamped cantilever, one element deep: nx by nz, held along x = 0 and loaded by -1
    /// at the middle of its free end, at every node through the depth.
    /// </summary>
    static Problem ClampedCantilever(int nx, int nz, int ny = 1)
    {
        var grid = new Grid(nx, ny, nz);
        var problem = new Problem { Name = "beso-cantilever", Grid = grid };
        for (int k = 0; k <= nz; k++)
            for (int j = 0; j <= ny; j++)
                problem.Fixes.Add((grid.Node(0, j, k), "1 1 1"));
        for (int j = 0; j <= ny; j++)
            problem.Loads.Add((grid.Node(nx, j, nz / 2), -1.0 / (ny + 1)));
        return problem;
    }

    /// <summary>
    /// Tools/Examples/TopologyBridge.gh's block, at n bricks per metre: steel 16 x 2 x 4 m, pinned along both ends of
    /// its bottom, its top layer a deck kept solid under 10 kN/m2. Units kN, m.
    /// </summary>
    static Problem Bridge(int n)
    {
        var grid = new Grid(16 * n, 2 * n, 4 * n, 1.0 / n);
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
        return problem;
    }

    /// <summary>The lattice points of a face from height k0 to k1.</summary>
    delegate IEnumerable<(int i, int j, int k)> FaceBetween(int k0, int k1);

    /// <summary>A beam's end: where it is, which way it points, and its shear and hogging moment.</summary>
    class BeamEnd
    {
        public FaceBetween Face;
        public Vector3d Normal;
        public double Shear, Moment;
    }

    /// <summary>
    /// Bricks on a lattice of cubes of side Size, any set of them: a design domain and the members that arrive at it.
    /// Nodes are numbered from 1 over the lattice points the bricks use, so OpenSees meets no loose node.
    /// </summary>
    class Voxels
    {
        public readonly double Size;
        public readonly List<(int i, int j, int k)> Bricks = new List<(int, int, int)>();
        public readonly HashSet<(int, int, int)> Kept = new HashSet<(int, int, int)>();
        readonly Dictionary<(int, int, int), int> nodes = new Dictionary<(int, int, int), int>();
        readonly HashSet<(int, int, int)> taken = new HashSet<(int, int, int)>();
        public readonly List<(int node, string dofs)> Fixes = new List<(int, string)>();
        public readonly List<(int node, Vector3d force)> Loads = new List<(int, Vector3d)>();

        public Voxels(double size) { Size = size; }

        public void Box(int i0, int i1, int j0, int j1, int k0, int k1, bool kept)
        {
            for (int k = k0; k < k1; k++)
                for (int j = j0; j < j1; j++)
                    for (int i = i0; i < i1; i++)
                        if (taken.Add((i, j, k)))
                        {
                            Bricks.Add((i, j, k));
                            if (kept) Kept.Add((i, j, k));
                        }
        }

        public int Node(int i, int j, int k)
        {
            if (!nodes.TryGetValue((i, j, k), out int tag))
                nodes[(i, j, k)] = tag = nodes.Count + 1;
            return tag;
        }

        int[] Corners((int i, int j, int k) c) => new[]
        {
            Node(c.i, c.j, c.k), Node(c.i + 1, c.j, c.k), Node(c.i + 1, c.j + 1, c.k), Node(c.i, c.j + 1, c.k),
            Node(c.i, c.j, c.k + 1), Node(c.i + 1, c.j, c.k + 1), Node(c.i + 1, c.j + 1, c.k + 1), Node(c.i, c.j + 1, c.k + 1),
        };

        /// <summary>The lattice points of a face of a box, i0..i1 by j0..j1 by k0..k1 with one of the three fixed.</summary>
        public IEnumerable<(int i, int j, int k)> Face(int i0, int i1, int j0, int j1, int k0, int k1)
        {
            for (int k = k0; k <= k1; k++)
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                        yield return (i, j, k);
        }

        public void Fix(IEnumerable<(int i, int j, int k)> points)
        {
            foreach (var p in points)
                Fixes.Add((Node(p.i, p.j, p.k), "1 1 1"));
        }

        /// <summary>A force shared equally among the points.</summary>
        public void Load(IEnumerable<(int i, int j, int k)> points, Vector3d total)
        {
            var list = points.ToList();
            foreach (var p in list)
                Loads.Add((Node(p.i, p.j, p.k), total / list.Count));
        }

        /// <summary>
        /// A moment about a horizontal axis through the middle of a face k0..k1 high, as a couple: the top half of the face
        /// pulled out along <paramref name="normal"/> and the bottom half pushed in, each by 2M/d spread evenly, d the
        /// face's depth - the two halves' middles are d/2 apart. Positive is a beam's hogging moment, its top in tension.
        /// The Grasshopper examples load a beam end the same way.
        /// </summary>
        public void Moment(FaceBetween face, int k0, int k1, Vector3d normal, double moment)
        {
            double force = 2.0 * moment / ((k1 - k0) * Size);
            int middle = (k0 + k1) / 2;
            Load(face(middle, k1), normal * force);
            Load(face(k0, middle), -normal * force);
        }

        public List<SolidCell> Cells(double e)
        {
            var cells = Bricks.Select((c, n) => new SolidCell(n + 1, SolidShape.Hexahedron, Corners(c),
                new Point3d((c.i + 0.5) * Size, (c.j + 0.5) * Size, (c.k + 0.5) * Size), Size * Size * Size, e)
            { Passive = Kept.Contains(c) }).ToList();
            return cells;
        }

        public int NodeCount => nodes.Count;

        public List<string> Tcl(double e, double nu)
        {
            var corners = Bricks.Select(c => Corners(c)).ToList();   // every node numbered before the nodes are written
            var tcl = new List<string> { "wipe\n", "model BasicBuilder -ndm 3 -ndf 3\n" };
            foreach (var n in nodes.OrderBy(n => n.Value))
                tcl.Add($"node {n.Value} {N(n.Key.Item1 * Size)} {N(n.Key.Item2 * Size)} {N(n.Key.Item3 * Size)}\n");
            foreach (var f in Fixes)
                tcl.Add($"fix {f.node} {f.dofs}\n");
            tcl.Add($"nDMaterial ElasticIsotropic 1 {N(e)} {N(nu)} 0.0\n");
            for (int n = 0; n < Bricks.Count; n++)
                tcl.Add($"element stdBrick {n + 1} {string.Join(" ", corners[n])} 1\n");
            tcl.Add("timeSeries Linear 1\n");
            tcl.Add("pattern Plain 1 1 {\n" + string.Concat(Loads.Select(l => $"    load {l.node} {N(l.force.X)} {N(l.force.Y)} {N(l.force.Z)}\n")) + "}\n");
            return tcl;
        }
    }

    /// <summary>
    /// A node of a space truss, Tools/Examples/TopologyTrussNode's: a 1 m cube of n bricks a side (n a multiple of 10)
    /// where nine members meet. The chords along x pull out with 450 and 250 kN, those along y push in with 300 and 150
    /// kN - unequal, as a space truss's chords are on either side of a node, the difference going down the diagonals -
    /// a post on top brings 600 kN down, and four diagonals leave from the bottom corners to the supports. Each member is
    /// a stub of kept bricks, 0.2 m square and 0.2 m long. Units kN, m.
    /// </summary>
    static Voxels TrussNode(int n)
    {
        var v = new Voxels(1.0 / n);
        int s = n / 5, a = (n - s) / 2, b = a + s, l = n / 5;
        v.Box(0, n, 0, n, 0, n, false);
        v.Box(n, n + l, a, b, a, b, true);          // chord +x
        v.Box(-l, 0, a, b, a, b, true);             // chord -x
        v.Box(a, b, n, n + l, a, b, true);          // chord +y
        v.Box(a, b, -l, 0, a, b, true);             // chord -y
        v.Box(a, b, a, b, n, n + l, true);          // post
        foreach (int i in new[] { 0, n - s })
            foreach (int j in new[] { 0, n - s })
                v.Box(i, i + s, j, j + s, -l, 0, true);   // diagonals, down from the bottom corners
        v.Load(v.Face(n + l, n + l, a, b, a, b), new Vector3d(450, 0, 0));
        v.Load(v.Face(-l, -l, a, b, a, b), new Vector3d(-250, 0, 0));
        v.Load(v.Face(a, b, n + l, n + l, a, b), new Vector3d(0, -300, 0));
        v.Load(v.Face(a, b, -l, -l, a, b), new Vector3d(0, 150, 0));
        v.Load(v.Face(a, b, a, b, n + l, n + l), new Vector3d(0, 0, -600));
        foreach (int i in new[] { 0, n - s })
            foreach (int j in new[] { 0, n - s })
                v.Fix(v.Face(i, i + s, j, j + s, -l, -l));
        return v;
    }

    /// <summary>
    /// An interior beam-column joint, Tools/Examples/TopologyBeamColumnJoint's: a 1 m cube of n bricks a side (n a
    /// multiple of 10) between a column, 0.4 m square, held at its foot and loaded with 1500 kN at its head, and four
    /// beams, 0.2 m wide and 0.4 m deep, from 0.4 to 0.8 m up, each bringing its shear and a hogging moment - 150 kN and
    /// 150 kNm from +x, 150 kN and 80 kNm from -x, 100 kN and 100 kNm from +y and -y - so the joint takes an unbalanced
    /// moment into the column. Stubs 0.2 m long. Units kN, m.
    /// </summary>
    static Voxels BeamColumnJoint(int n)
    {
        var v = new Voxels(1.0 / n);
        int c = 2 * n / 5, c0 = (n - c) / 2, c1 = c0 + c;
        int w = n / 5, w0 = (n - w) / 2, w1 = w0 + w;
        int d0 = 2 * n / 5, d1 = 4 * n / 5;
        int l = n / 5;
        v.Box(0, n, 0, n, 0, n, false);
        v.Box(c0, c1, c0, c1, -l, 0, true);         // column below
        v.Box(c0, c1, c0, c1, n, n + l, true);      // column above
        v.Box(n, n + l, w0, w1, d0, d1, true);      // beam +x
        v.Box(-l, 0, w0, w1, d0, d1, true);         // beam -x
        v.Box(w0, w1, n, n + l, d0, d1, true);      // beam +y
        v.Box(w0, w1, -l, 0, d0, d1, true);         // beam -y
        v.Fix(v.Face(c0, c1, c0, c1, -l, -l));
        v.Load(v.Face(c0, c1, c0, c1, n + l, n + l), new Vector3d(0, 0, -1500));
        var beams = new[]
        {
            new BeamEnd { Face = (k0, k1) => v.Face(n + l, n + l, w0, w1, k0, k1), Normal = Vector3d.XAxis, Shear = 150.0, Moment = 150.0 },
            new BeamEnd { Face = (k0, k1) => v.Face(-l, -l, w0, w1, k0, k1), Normal = -Vector3d.XAxis, Shear = 150.0, Moment = 80.0 },
            new BeamEnd { Face = (k0, k1) => v.Face(w0, w1, n + l, n + l, k0, k1), Normal = Vector3d.YAxis, Shear = 100.0, Moment = 100.0 },
            new BeamEnd { Face = (k0, k1) => v.Face(w0, w1, -l, -l, k0, k1), Normal = -Vector3d.YAxis, Shear = 100.0, Moment = 100.0 },
        };
        foreach (var beam in beams)
        {
            v.Load(beam.Face(d0, d1), new Vector3d(0, 0, -beam.Shear));
            v.Moment(beam.Face, d0, d1, beam.Normal, beam.Moment);
        }
        return v;
    }

    static TopologyAnalysis Start(Voxels v, string name, string openSees, string folder, List<SolidCell> cells, double e, double nu) =>
        new TopologyAnalysis(openSees, folder, name, v.Tcl(e, nu), cells, v.NodeCount, new Alpaca4d.SystemEquation("Auto"), TimeSpan.FromMinutes(10));

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
            Bench(args.Skip(3).ToArray(), openSees, folder);
            return;
        }

        MmaChecks();
        InterpolationChecks();
        DerivativeChecks(openSees, folder);
        MultiMaterialRun(openSees, folder);
        BesoRun(openSees, folder);
        BesoBreakdown(openSees, folder);
        BesoBreakdownInTwoSteps(openSees, folder);

        Console.WriteLine(fails == 0 ? "\nAll checks passed." : $"\n{fails} check(s) FAILED.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>
    /// The small test problem of Svanberg's note, section 6: three variables, two constraints, from
    /// (4, 3, 2). The note's table gives each iterate to four decimals.
    /// </summary>
    static void MmaChecks()
    {
        Section("MMA: the test problem of Svanberg's note, section 6");
        var mma = new Mma(3, 2, 1.0, new[] { 0.0, 0.0 }, new[] { 1000.0, 1000.0 }, new[] { 1.0, 1.0 });
        var x = new[] { 4.0, 3.0, 2.0 };
        var xmin = new[] { 0.0, 0.0, 0.0 };
        var xmax = new[] { 5.0, 5.0, 5.0 };
        var expected = new[]
        {
            new[] { 2.3903, 1.8057, 0.9929 },
            new[] { 2.0385, 1.7624, 1.2417 },
            new[] { 2.0178, 1.7786, 1.2392 },
            new[] { 2.0176, 1.7794, 1.2383 },
            new[] { 2.0176, 1.7798, 1.2378 },
            new[] { 2.0175, 1.7800, 1.2376 },
        };
        double worst = 0.0;
        for (int k = 0; k < expected.Length; k++)
        {
            double f0 = x.Sum(v => v * v);
            var df0 = x.Select(v => 2.0 * v).ToArray();
            var f = new[]
            {
                Sq(x[0] - 5) + Sq(x[1] - 2) + Sq(x[2] - 1) - 9.0,
                Sq(x[0] - 3) + Sq(x[1] - 4) + Sq(x[2] - 3) - 9.0,
            };
            var df = new[]
            {
                new[] { 2 * (x[0] - 5), 2 * (x[1] - 2), 2 * (x[2] - 1) },
                new[] { 2 * (x[0] - 3), 2 * (x[1] - 4), 2 * (x[2] - 3) },
            };
            x = mma.Update(x, xmin, xmax, f0, df0, f, df);
            double gap = Enumerable.Range(0, 3).Max(j => Math.Abs(x[j] - expected[k][j]));
            worst = Math.Max(worst, gap);
            Console.WriteLine($"    k = {k + 2}: {x[0]:0.0000} {x[1]:0.0000} {x[2]:0.0000}   (note: {string.Join(" ", expected[k].Select(v => v.ToString("0.0000")))})");
        }
        Check(worst < 0.6e-4, $"every iterate is the note's to its four decimals (largest gap {worst:G2})");
        Check(Math.Abs(x.Sum(v => v * v) - 8.7702) < 1e-4, $"and ends at the note's f0 = 8.7702 ({x.Sum(v => v * v):0.0000})");
    }

    /// <summary>The projection and the interpolation at points where the answer is known.</summary>
    static void InterpolationChecks()
    {
        Section("Multi-material: the projection and the mapping-based interpolation");
        foreach (double beta in new[] { 1.0, 8.0, 32.0 })
            Check(Math.Abs(MultiMaterialOptimiser.Project(0.0, beta)) < 1e-12 && Math.Abs(MultiMaterialOptimiser.Project(1.0, beta) - 1.0) < 1e-12
                  && Math.Abs(MultiMaterialOptimiser.Project(0.5, beta) - 0.5) < 1e-12,
                  $"at sharpness {beta} the projection keeps 0, 1/2 and 1");
        double h = 1e-6, worst = 0.0;
        foreach (double beta in new[] { 1.0, 8.0, 32.0 })
            foreach (double t in new[] { 0.1, 0.3, 0.5, 0.55, 0.9 })
            {
                double fd = (MultiMaterialOptimiser.Project(t + h, beta) - MultiMaterialOptimiser.Project(t - h, beta)) / (2 * h);
                worst = Math.Max(worst, Math.Abs(fd - MultiMaterialOptimiser.ProjectionSlope(t, beta)) / Math.Max(1.0, Math.Abs(fd)));
            }
        Check(worst < 1e-6, $"its slope is its derivative (largest gap {worst:G2})");
        Check(Math.Abs(MultiMaterialOptimiser.Sharpness(new MultiMaterialSettings(), 50) - 1) < 1e-15
              && Math.Abs(MultiMaterialOptimiser.Sharpness(new MultiMaterialSettings(), 51) - 2) < 1e-15
              && Math.Abs(MultiMaterialOptimiser.Sharpness(new MultiMaterialSettings(), 251) - 32) < 1e-15
              && Math.Abs(MultiMaterialOptimiser.Sharpness(new MultiMaterialSettings(), 400) - 32) < 1e-15,
              "the sharpness doubles every 50 iterations from 1, as the paper's, and stops at 32");
    }

    /// <summary>
    /// The derivatives of the compliance and of the volumes by the design densities, against central
    /// differences, through OpenSees: the interpolation, the projection, the filter and the energies together.
    /// </summary>
    static void DerivativeChecks(string openSees, string folder)
    {
        Section("Multi-material: derivatives against finite differences, through OpenSees");
        var problem = Cantilever(6, 3, 2, "derivatives");
        var cells = problem.Grid.Cells(problem.E);
        var settings = new MultiMaterialSettings
        {
            Moduli = new[] { 1.0, 2.0, 5.0 },
            VolumeFractions = new[] { 0.2, 0.15, 0.1 },
            FilterRadius = 1.5,
        };
        var random = new Random(7);
        var x = Enumerable.Range(0, 3 * cells.Count).Select(_ => 0.1 + 0.8 * random.NextDouble()).ToArray();
        using (var analysis = Start(problem, openSees, folder, cells))
        {
            foreach (double beta in new[] { 1.0, 4.0 })
            {
                var (c, dc, _, dv) = MultiMaterialOptimiser.Derivatives(cells, settings, analysis.Energies, x, beta);
                double worst = 0.0, worstVolume = 0.0;
                double step = 1e-5;
                foreach (int j in new[] { 0, 17, 36, 50, 75, 107 })
                {
                    var up = (double[])x.Clone(); up[j] += step;
                    var down = (double[])x.Clone(); down[j] -= step;
                    var plus = MultiMaterialOptimiser.Derivatives(cells, settings, analysis.Energies, up, beta);
                    var minus = MultiMaterialOptimiser.Derivatives(cells, settings, analysis.Energies, down, beta);
                    double fd = (plus.compliance - minus.compliance) / (2 * step);
                    worst = Math.Max(worst, Math.Abs(fd - dc[j]) / Math.Max(Math.Abs(dc[j]), 1e-3 * dc.Max(Math.Abs)));
                    for (int k = 0; k < 3; k++)
                    {
                        double fdv = (plus.volumes[k] - minus.volumes[k]) / (2 * step);
                        worstVolume = Math.Max(worstVolume, Math.Abs(fdv - dv[k][j]) / Math.Max(Math.Abs(dv[k][j]), 1e-6));
                    }
                }
                Check(worst < 1e-4, $"sharpness {beta}: the compliance's derivatives are its central differences (compliance {c:G6}, largest gap {worst:G2})");
                Check(worstVolume < 1e-6, $"sharpness {beta}: and so are the volumes' (largest gap {worstVolume:G2})");
            }
        }
    }

    static double Discreteness(TopologyResult result, int cells, IEnumerable<int> design)
    {
        // The share of design elements that are, to within 0.1, one material alone or void.
        int clear = 0, total = 0;
        foreach (int e in design)
        {
            var psi = Enumerable.Range(0, result.Materials).Select(i => result.Densities[i * cells + e]).ToArray();
            double largest = psi.Max(), rest = psi.Sum() - largest;
            if ((largest > 0.9 || largest < 0.1) && rest < 0.1)
                clear++;
            total++;
        }
        return (double)clear / total;
    }

    /// <summary>A short run of two materials: the volumes, the compliance, a clear result, and going on.</summary>
    static void MultiMaterialRun(string openSees, string folder)
    {
        Section("Multi-material: a short run of two materials, E = 1 and 5");
        var problem = Cantilever(30, 10, 2, "multimaterial");
        problem.Passive = (i, j, k) => i == 29 && k == 0;
        var cells = problem.Grid.Cells(problem.E, problem.Passive);
        Func<int, MultiMaterialSettings> Settings = iterations => new MultiMaterialSettings
        {
            Moduli = new[] { 1.0, 5.0 },
            VolumeFractions = new[] { 0.15, 0.15 },
            FilterRadius = 1.5,
            MaxIterations = iterations,
            SharpenEvery = 10,
            MaxSharpness = 16.0,
            Tolerance = 0.0,
        };
        using (var analysis = Start(problem, openSees, folder, cells))
        {
            var frames = new DensityHistory();
            var result = MultiMaterialOptimiser.Run(cells, Settings(60), analysis.Energies, frames: frames);
            var history = result.History;
            foreach (var it in history.Where(it => it.Iteration % 10 == 0 || it.Iteration == 1))
                Console.WriteLine("    " + it);
            var last = history.Last();
            Check(history.Count == 60, $"it runs its 60 iterations ({history.Count})");
            Check(last.MaterialVolumes.Zip(new[] { 0.15, 0.15 }, (v, f) => Math.Abs(v - f)).Max() < 2e-3,
                  $"each material fills its share ({string.Join(", ", last.MaterialVolumes.Select(v => v.ToString("0.0000")))})");
            Check(last.Compliance < 0.6 * history.First().Compliance, $"the compliance comes down ({history.First().Compliance:G5} to {last.Compliance:G5})");
            var design = Enumerable.Range(0, cells.Count).Where(i => !cells[i].Passive).ToList();
            double clear = Discreteness(result, cells.Count, design);
            Check(clear > 0.8, $"{clear:P1} of the elements end one material alone or void, after 10 iterations at the sharpest");
            double stiff = design.Sum(e => result.Densities[cells.Count + e]), soft = design.Sum(e => result.Densities[e]);
            Check(frames.Count == 61 && frames.Elements == 2 * cells.Count, $"61 frames of both materials' indicators ({frames.Count} of {frames.Elements})");
            Check(Enumerable.Range(0, cells.Count).Where(i => cells[i].Passive).All(i => result.Densities[i] == 0.0 && result.Densities[cells.Count + i] == 0.0),
                  "the kept elements have no material of the list");

            var first = MultiMaterialOptimiser.Run(cells, Settings(25), analysis.Energies);
            var onward = MultiMaterialOptimiser.Run(cells, Settings(40), analysis.Energies, previous: first);
            var whole = MultiMaterialOptimiser.Run(cells, Settings(40), analysis.Energies);
            double gap = onward.Densities.Zip(whole.Densities, (a, b) => Math.Abs(a - b)).Max();
            Check(gap == 0.0 && onward.History.Count == 40, $"25 iterations and then 15 more are 40 in one go (largest gap {gap:G2})");
        }
    }

    /// <summary>A BESO run to its end: solid or void, the volume schedule, convergence, and going on.</summary>
    static void BesoRun(string openSees, string folder)
    {
        Section("BESO: a cantilever to half its volume");
        var problem = Cantilever(30, 10, 2, "beso");
        problem.Passive = (i, j, k) => i == 29 && k == 0;
        var cells = problem.Grid.Cells(problem.E, problem.Passive);
        Func<int, BesoSettings> Settings = iterations => new BesoSettings { VolumeFraction = 0.5, FilterRadius = 3.0, MaxIterations = iterations };
        double xMin = Math.Pow(1e-9, 1.0 / 3.0);
        using (var analysis = Start(problem, openSees, folder, cells))
        {
            var frames = new DensityHistory();
            var result = BesoOptimiser.Run(cells, Settings(150), analysis.Energies, frames: frames);
            var history = result.History;
            foreach (var it in history.Where(it => it.Iteration % 10 == 0 || it.Iteration == 1 || it.Iteration == history.Count))
                Console.WriteLine("    " + it);
            Check(result.Converged, $"it converges, in {history.Count} iterations");
            Check(result.Densities.All(d => d == 1.0 || d == xMin), "every element ends solid or void");
            Check(Enumerable.Range(0, cells.Count).Where(i => cells[i].Passive).All(i => result.Densities[i] == 1.0), "the kept elements stay solid");
            var settings = Settings(150);
            int designCount = cells.Count(c => !c.Passive);
            double worst = history.Max(it => Math.Abs(it.Volume - BesoOptimiser.TargetVolume(settings, it.Iteration - 1)));
            Check(worst <= 2.0 / designCount, $"each design analysed has the volume its update aimed at, to two elements (largest gap {worst * designCount:0.#} elements)");
            Check(Math.Abs(history.Last().Volume - 0.5) <= 2.0 / designCount, $"and the last has half the volume ({history.Last().Volume:0.0000})");
            Check(history.Last().Change <= 0.001, $"the last five compliances are within 0.1% of the five before ({history.Last().Change:G3})");
            Check(history.Take(34).All(it => double.IsNaN(it.Change)), "no convergence is tested before the volume is reached (iteration 35)");
            Check(history.Last().Compliance > history.First().Compliance, $"taking material away makes it softer ({history.First().Compliance:G5} to {history.Last().Compliance:G5})");
            Check(frames.Count == history.Count + 1, $"a frame per iteration and the start ({frames.Count})");

            var first = BesoOptimiser.Run(cells, Settings(20), analysis.Energies);
            var onward = BesoOptimiser.Run(cells, Settings(45), analysis.Energies, previous: first);
            var whole = BesoOptimiser.Run(cells, Settings(45), analysis.Energies);
            bool same = onward.Densities.SequenceEqual(whole.Densities) && onward.History.Select(it => it.Compliance).SequenceEqual(whole.History.Select(it => it.Compliance));
            Check(same, "20 iterations and then 25 more are 45 in one go");
        }
    }

    /// <summary>
    /// The bridge at 2 bricks per metre with too wide a filter, 2 m in a block 4 m high: a thin member's energy is averaged
    /// away and the member is cut through; the run settles on what is left, and says it broke rather than converged.
    /// </summary>
    static void BesoBreakdown(string openSees, string folder)
    {
        Section("BESO: a design that breaks apart and is not mended");
        var problem = Bridge(2);
        var cells = problem.Grid.Cells(problem.E, problem.Passive);
        using (var analysis = Start(problem, openSees, folder, cells))
        {
            var result = BesoOptimiser.Run(cells, new BesoSettings { VolumeFraction = 0.2, FilterRadius = 2.0, MaxIterations = 200 }, analysis.Energies);
            var h = result.History.Select(it => it.Compliance).ToList();
            int broke = result.BrokeAfter ?? 0;
            Check(broke > 1 && !result.Converged && !result.Stopped && h.Count < 200,
                  $"it ends after {h.Count} iterations, broken after iteration {broke}, not converged");
            Check(broke > 1 && h[broke] > BesoOptimiser.Breakdown * h[broke - 1] && h.Last() > BesoOptimiser.Mended * h[broke - 1],
                  $"the design after iteration {broke} is {(broke > 1 ? h[broke] / h[broke - 1] : 0):G3} times softer than the one before, and the last {(broke > 1 ? h.Last() / h[broke - 1] : 0):G3} times");
        }
        Section("BESO: the same bridge with the default filter, two elements");
        using (var analysis = Start(problem, openSees, folder, cells))
        {
            var result = BesoOptimiser.Run(cells, new BesoSettings { VolumeFraction = 0.2, MaxIterations = 200 }, analysis.Energies);
            double jump = Enumerable.Range(1, result.History.Count - 1).Max(k => result.History[k].Compliance / result.History[k - 1].Compliance);
            // A member nearly cut and brought back shows as a jump the run recovers from: BESO's bringing elements back at work.
            Check(result.Converged && result.BrokeAfter == null,
                  $"converges in {result.History.Count} iterations at {result.History.Last().Compliance:G4}, nothing broken (largest jump {jump:0.00} times, recovered)");
        }
    }

    /// <summary>
    /// The beam-column joint at 10 bricks a side, by BESO: the compliance rises 4.3 times at iteration 109, under
    /// Breakdown, then a million times at 110, and comes back only to eight times what it was. Measured against the
    /// design just before the big jump, that looked mended; measured against the lowest of the five before, it is not.
    /// </summary>
    static void BesoBreakdownInTwoSteps(string openSees, string folder)
    {
        Section("BESO: a member cut in two steps, the beam-column joint at 10 bricks a side");
        var joint = BeamColumnJoint(10);
        var cells = joint.Cells(210e6);
        using (var analysis = Start(joint, "joint-10", openSees, folder, cells, 210e6, 0.3))
        {
            var result = BesoOptimiser.Run(cells, new BesoSettings { VolumeFraction = 0.2, MaxIterations = 300 }, analysis.Energies);
            var h = result.History.Select(it => it.Compliance).ToList();
            int broke = result.BrokeAfter ?? 0;
            Check(broke > 1 && !result.Converged && !result.Stopped,
                  $"it ends after {h.Count} iterations, broken after iteration {broke}, not converged");
            Check(broke > 1 && h[broke] > BesoOptimiser.Mended * h[broke - 1] && h[broke] < BesoOptimiser.Breakdown * h[broke - 1]
                  && h.Skip(broke).Max() > BesoOptimiser.Breakdown * h[broke - 1] && h.Last() > BesoOptimiser.Mended * h[broke - 1],
                  $"iteration {broke}'s design, {h[Math.Max(0, broke - 1)]:G4}, is the last whole one: the next is {(broke > 1 ? h[broke] / h[broke - 1] : 0):G3} " +
                  $"times softer, then {(broke > 1 ? h.Skip(broke).Max() / h[broke - 1] : 0):G3}, and the last {(broke > 1 ? h.Last() / h[broke - 1] : 0):G3}");
        }
    }

    /// <summary>The moduli of Zheng et al.'s examples: 1 and 5 for two materials, 1, 2 and 5 for three, 1 to 5 for five.</summary>
    static double[] ZhengModuli(int materials) =>
        materials == 2 ? new[] { 1.0, 5.0 } : materials == 3 ? new[] { 1.0, 2.0, 5.0 } : materials == 5 ? new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }
        : throw new ArgumentException("Zheng et al. have examples of 2, 3 and 5 materials.");

    /// <summary>
    ///     bench zheng &lt;materials&gt; [nx nz ny] [rmin] [paper|auto]
    ///         Zheng et al.'s 3D cantilever, 60 x 30 x 15 by default as in their Appendix B: 300 iterations,
    ///         a volume of 0.5/materials each, filter radius 5, the objective unscaled as theirs ("paper") or
    ///         scaled as the component does ("auto").
    ///     bench beso-cantilever [nx nz] [volfrac] [rmin]
    ///         esoL.m's clamped cantilever, esoL(100,50,0.6,0.02,6,2), one element deep.
    ///     bench beso-3d [nx nz ny]
    ///         BESO on a 3D cantilever, for the numpy reference to follow.
    /// </summary>
    static void Bench(string[] a, string openSees, string folder)
    {
        string name = a[0];
        if (name == "zheng")
        {
            int materials = int.Parse(a[1]);
            int nx = a.Length > 4 ? int.Parse(a[2]) : 60, nz = a.Length > 4 ? int.Parse(a[3]) : 30, ny = a.Length > 4 ? int.Parse(a[4]) : 15;
            double rmin = a.Length > 5 ? double.Parse(a[5], CultureInfo.InvariantCulture) : 5.0;
            bool paper = !(a.Length > 6 && a[6] == "auto");
            var problem = Cantilever(nx, nz, ny, $"zheng-{materials}-{nx}x{nz}x{ny}-r{rmin}-{(paper ? "paper" : "auto")}");
            var cells = problem.Grid.Cells(problem.E);
            var settings = new MultiMaterialSettings
            {
                Moduli = ZhengModuli(materials),
                VolumeFractions = Enumerable.Repeat(0.5 / materials, materials).ToArray(),
                FilterRadius = rmin,
                MaxIterations = 300,
                Tolerance = 0.0,
                ObjectiveScale = paper ? 1.0 : (double?)null,
            };
            Console.WriteLine($"{problem.Name}: {cells.Count} bricks, E = {string.Join(", ", settings.Moduli)}, volumes {0.5 / materials:0.####} each");
            Run(problem, cells, openSees, folder, analyse => MultiMaterialOptimiser.Run(cells, settings, analyse, progress: Print));
        }
        else if (name == "beso-cantilever" || name == "beso-3d")
        {
            bool flat = name == "beso-cantilever";
            int nx = a.Length > 2 ? int.Parse(a[1]) : flat ? 100 : 40, nz = a.Length > 2 ? int.Parse(a[2]) : flat ? 50 : 20;
            int ny = !flat && a.Length > 3 ? int.Parse(a[3]) : flat ? 1 : 4;
            double volfrac = flat && a.Length > 3 ? double.Parse(a[3], CultureInfo.InvariantCulture) : flat ? 0.6 : 0.4;
            double rmin = flat && a.Length > 4 ? double.Parse(a[4], CultureInfo.InvariantCulture) : flat ? 6.0 : 3.0;
            var problem = flat ? ClampedCantilever(nx, nz) : Cantilever(nx, nz, ny, "beso-3d");
            var cells = problem.Grid.Cells(problem.E);
            var settings = new BesoSettings { VolumeFraction = volfrac, EvolutionRate = 0.02, FilterRadius = rmin, MaxIterations = 300 };
            Console.WriteLine($"{problem.Name}: {nx} x {nz} x {ny} = {cells.Count} bricks, volume {volfrac}, rmin {rmin}");
            Run(problem, cells, openSees, folder, analyse => BesoOptimiser.Run(cells, settings, analyse, progress: Print));
        }
        else if (name == "bridge")
        {
            // Tools/Examples/TopologyBridge.gh's block, by SIMP, BESO and MMA with one material. BRIDGE_N sets the bricks per
            // metre (3), BESO_ER and BESO_R BESO's evolutionary ratio and filter radius (0.02, and 0 for the default),
            // BESO_ONLY=1 runs BESO alone.
            var problem = Bridge(int.Parse(Environment.GetEnvironmentVariable("BRIDGE_N") ?? "3"));
            var grid = problem.Grid;
            var cells = problem.Grid.Cells(problem.E, problem.Passive);
            using (var analysis = Start(problem, openSees, folder, cells))
            {
                if (Environment.GetEnvironmentVariable("BESO_ONLY") != "1")
                {
                    var simp = TopologyOptimiser.Run(cells, new TopologySettings { VolumeFraction = 0.2, MaxIterations = 100 }, analysis.Energies);
                    Console.WriteLine($"SIMP: {simp.History.Count} iterations, first {simp.History.First().Compliance:G5}, last {simp.History.Last().Compliance:G5}");
                }
                double er = double.Parse(Environment.GetEnvironmentVariable("BESO_ER") ?? "0.02", CultureInfo.InvariantCulture);
                double radius = double.Parse(Environment.GetEnvironmentVariable("BESO_R") ?? "0", CultureInfo.InvariantCulture);
                var beso = BesoOptimiser.Run(cells, new BesoSettings { VolumeFraction = 0.2, MaxIterations = 300, EvolutionRate = er, FilterRadius = radius }, analysis.Energies);
                foreach (var it in beso.History.Where(it => it.Iteration % 10 == 0 || it.Iteration == 1))
                    Console.WriteLine("  " + it);
                Console.WriteLine($"BESO (rate {er}, radius {beso.FilterRadius:0.###}): {beso.History.Count} iterations, converged {beso.Converged}, " +
                                  $"best {beso.History.Min(it => it.Compliance):G4}, largest jump {Enumerable.Range(1, beso.History.Count - 1).Max(k => beso.History[k].Compliance / beso.History[k - 1].Compliance):G3}x, last {beso.History.Last().Compliance:G5}");
                if (Environment.GetEnvironmentVariable("BESO_ONLY") == "1") return;
                var steelOnly = MultiMaterialOptimiser.Run(cells, new MultiMaterialSettings { Moduli = new[] { 210e6 }, VolumeFractions = new[] { 0.2 } }, analysis.Energies);
                Console.WriteLine($"Multi-material, steel alone: {steelOnly.History.Count} iterations, last {steelOnly.History.Last().Compliance:G5}");
            }
        }
        else if (name == "node")
        {
            // A node where members meet, in space, by all three methods: node truss|joint [n] [simp|beso|multi ...].
            string kind = a.Length > 1 ? a[1] : "truss";
            int n = a.Length > 2 ? int.Parse(a[2]) : 20;
            var methods = a.Length > 3 ? a.Skip(3).ToList() : new List<string> { "simp", "beso", "multi" };
            bool truss = kind == "truss";
            var v = truss ? TrussNode(n) : BeamColumnJoint(n);
            double e = 210e6, nu = 0.3;
            var cells = v.Cells(e);
            double fraction = truss ? 0.12 : 0.2;
            Console.WriteLine($"{kind} node: {cells.Count(c => !c.Passive)} design bricks and {cells.Count(c => c.Passive)} kept, {v.NodeCount} nodes, " +
                              $"{v.Loads.Count} loaded and {v.Fixes.Count} fixed");
            string stem = $"node-{kind}-{n}";
            File.Delete(Path.Combine(folder, stem + "_summary.csv"));
            File.WriteAllLines(Path.Combine(folder, stem + "_cells.csv"),
                cells.Select(c => $"{N(c.Centroid.X)},{N(c.Centroid.Y)},{N(c.Centroid.Z)},{(c.Passive ? 1 : 0)}"));
            using (var analysis = Start(v, stem, openSees, folder, cells, e, nu))
            {
                foreach (var method in methods)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    TopologyResult result;
                    if (method == "simp")
                        result = TopologyOptimiser.Run(cells, new TopologySettings { VolumeFraction = fraction, MaxIterations = 200 }, analysis.Energies);
                    else if (method == "beso")
                        result = BesoOptimiser.Run(cells, new BesoSettings { VolumeFraction = fraction, MaxIterations = 300 }, analysis.Energies);
                    else
                        result = MultiMaterialOptimiser.Run(cells, new MultiMaterialSettings
                        {
                            Moduli = truss ? new[] { 210e6, 70e6 } : new[] { 210e6, 33e6 },
                            VolumeFractions = truss ? new[] { 0.04, 0.08 } : new[] { 0.06, 0.14 },
                        }, analysis.Energies);
                    var last = result.History.Last();
                    Console.WriteLine($"  {method}: {result.History.Count} iterations, converged {result.Converged}{(result.BrokeAfter != null ? $", broke after {result.BrokeAfter}" : "")}, " +
                                      $"compliance {N(result.History.First().Compliance)} to {N(last.Compliance)}, {clock.Elapsed.TotalSeconds:0} s");
                    File.WriteAllLines(Path.Combine(folder, $"{stem}-{method}_densities.csv"), result.Densities.Select(N));
                    File.AppendAllText(Path.Combine(folder, stem + "_summary.csv"),
                        $"{method},{result.History.Count},{N(result.History.First().Compliance)},{N(last.Compliance)}\n");
                }
            }
        }
        else if (name == "grasshopper")
        {
            // What ../TopologyOptimisation/GrasshopperChecks.cs expects of BESO Topology and Multi-Material Topology on
            // its small cantilever: 24 x 8 x 2 bricks, the column under the load kept.
            var problem = Cantilever(24, 8, 2, "grasshopper");
            problem.Passive = (i, j, k) => i == 23 && k <= 1;
            var cells = problem.Grid.Cells(problem.E, problem.Passive);
            using (var analysis = Start(problem, openSees, folder, cells))
            {
                var beso = BesoOptimiser.Run(cells, new BesoSettings { VolumeFraction = 0.5, FilterRadius = 3.0, MaxIterations = 200 }, analysis.Energies);
                Console.WriteLine($"BESO: {beso.History.Count} iterations, converged {beso.Converged}, broke after {beso.BrokeAfter}, first {N(beso.History.First().Compliance)}, last {N(beso.History.Last().Compliance)}");
                foreach (var it in beso.History.Skip(Math.Max(0, beso.History.Count - 5)))
                    Console.WriteLine("    " + it);
                var mm = MultiMaterialOptimiser.Run(cells, new MultiMaterialSettings
                {
                    Moduli = new[] { 1.0, 5.0 }, VolumeFractions = new[] { 0.15, 0.15 }, FilterRadius = 1.5, MaxIterations = 300,
                }, analysis.Energies);
                int count = cells.Count;
                var soft = Enumerable.Range(0, count).Count(e => !cells[e].Passive && mm.Densities[e] >= 0.5 && mm.Densities[e] >= mm.Densities[count + e]);
                var stiff = Enumerable.Range(0, count).Count(e => !cells[e].Passive && mm.Densities[count + e] >= 0.5 && mm.Densities[count + e] > mm.Densities[e]);
                Console.WriteLine($"Multi-material: {mm.History.Count} iterations, converged {mm.Converged}, first {N(mm.History.First().Compliance)}, " +
                                  $"30th {N(mm.History[29].Compliance)}, last {N(mm.History.Last().Compliance)}, " +
                                  $"volumes {string.Join(" ", mm.History.Last().MaterialVolumes.Select(N))}, {soft} soft and {stiff} stiff at 0.5");
            }
        }
        else if (name == "speed")
        {
            // The three methods timed on one problem at several sizes: speed bridge|truss|joint [sizes] [methods], each a
            // comma-separated list. Sizes are bricks per metre for the bridge (2,3,4,5), per side of the cube for the nodes
            // (10,20,30); methods are simp, beso and multi (two materials, as the examples), and multi1 (steel alone).
            string kind = a.Length > 1 ? a[1] : "truss";
            var sizes = (a.Length > 2 ? a[2] : kind == "bridge" ? "2,3,4,5" : "10,20,30").Split(',').Select(int.Parse).ToList();
            var methods = (a.Length > 3 ? a[3] : "simp,beso,multi").Split(',').ToList();
            Console.WriteLine($"{Environment.ProcessorCount} cores, OpenSees {openSees}");
            foreach (int n in sizes)
                foreach (var method in methods)
                    Speed(kind, n, method, openSees, folder);
        }
        else
            throw new ArgumentException($"No bench called {name}.");
    }

    /// <summary>
    /// One timed run, with an OpenSees of its own started inside the clock, as a component starts one. Each method keeps
    /// the share of the examples - 20% of the bridge's block below the deck and of the joint, 12% of the truss node -
    /// and the iterations they allow: 200 for SIMP, 300 for BESO and Multi-Material. Appends a line to speed_summary.csv
    /// and writes the run's history, with the wall clock at the end of each iteration.
    /// </summary>
    static void Speed(string kind, int n, string method, string openSees, string folder)
    {
        string stem = $"speed-{kind}-{n}-{method}";
        double fraction = kind == "truss" ? 0.12 : 0.2;
        double[] moduli = kind == "truss" ? new[] { 210e6, 70e6 } : new[] { 210e6, 33e6 };
        double[] shares = kind == "truss" ? new[] { 0.04, 0.08 } : kind == "joint" ? new[] { 0.06, 0.14 } : new[] { 0.05, 0.15 };

        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<SolidCell> cells;
        int nodes;
        TopologyAnalysis analysis;
        if (kind == "bridge")
        {
            var problem = Bridge(n);
            problem.Name = stem;
            cells = problem.Grid.Cells(problem.E, problem.Passive);
            nodes = problem.Grid.Nodes;
            analysis = Start(problem, openSees, folder, cells);
        }
        else
        {
            var v = kind == "truss" ? TrussNode(n) : kind == "joint" ? BeamColumnJoint(n) : throw new ArgumentException($"No problem called {kind}.");
            cells = v.Cells(210e6);
            analysis = Start(v, stem, openSees, folder, cells, 210e6, 0.3);
            nodes = v.NodeCount;
        }
        double started = clock.Elapsed.TotalSeconds;

        var wall = new List<double>();
        Action<TopologyIteration, double[]> tick = (it, densities) => wall.Add(clock.Elapsed.TotalSeconds);
        TopologyResult result;
        using (analysis)
        {
            if (method == "simp")
                result = TopologyOptimiser.Run(cells, new TopologySettings { VolumeFraction = fraction, MaxIterations = 200 }, analysis.Energies, progress: tick);
            else if (method == "beso")
                result = BesoOptimiser.Run(cells, new BesoSettings { VolumeFraction = fraction, MaxIterations = 300 }, analysis.Energies, progress: tick);
            else if (method == "multi")
                result = MultiMaterialOptimiser.Run(cells, new MultiMaterialSettings { Moduli = moduli, VolumeFractions = shares }, analysis.Energies, progress: tick);
            else if (method == "multi1")
                result = MultiMaterialOptimiser.Run(cells, new MultiMaterialSettings { Moduli = new[] { 210e6 }, VolumeFractions = new[] { fraction } }, analysis.Energies, progress: tick);
            else
                throw new ArgumentException($"No method called {method}.");
            clock.Stop();
        }

        // Start-up is making the model and starting OpenSees on it, and what the first solve takes over a usual one.
        var history = result.History;
        var solves = history.Select(it => it.AnalysisSeconds).ToList();
        double usual = Median(solves.Skip(1)), first = Math.Max(0.0, solves[0] - usual), startup = started + first;
        double total = clock.Elapsed.TotalSeconds, solving = solves.Sum() - first, updating = history.Sum(it => it.UpdateSeconds);
        double other = total - startup - solving - updating;
        double final = history.Last().Compliance;
        int settled = history.Count;
        while (settled > 1 && Math.Abs(history[settled - 2].Compliance - final) <= 0.01 * final)
            settled--;

        using (var file = new StreamWriter(Path.Combine(folder, stem + "_history.csv")))
        {
            file.WriteLine("iteration,compliance,volume,analysis_s,update_s,wall_s");
            for (int k = 0; k < history.Count; k++)
                file.WriteLine(string.Join(",", history[k].Iteration, N(history[k].Compliance), N(history[k].Volume),
                    N(history[k].AnalysisSeconds), N(history[k].UpdateSeconds), N(wall[k])));
        }

        string summary = Path.Combine(folder, "speed_summary.csv");
        if (!File.Exists(summary))
            File.WriteAllText(summary, "problem,n,bricks,design,dofs,method,iterations,converged,broke_after,total_s,startup_s,solve_s,update_s,other_s," +
                                       "solve_median_s,update_median_s,settled_iteration,settled_s,first_compliance,final_compliance\n");
        File.AppendAllText(summary, string.Join(",", kind, n, cells.Count, cells.Count(c => !c.Passive), 3 * nodes, method, history.Count,
            result.Converged ? 1 : 0, result.BrokeAfter?.ToString() ?? "", N(total), N(startup), N(solving), N(updating), N(other),
            N(usual), N(Median(history.Select(it => it.UpdateSeconds))), settled, N(wall[settled - 1]),
            N(history[0].Compliance), N(final)) + "\n");

        Console.WriteLine($"  {kind} {n}, {cells.Count} bricks, {method}: {history.Count} iterations{(result.Converged ? "" : " (not converged)")}" +
                          $"{(result.BrokeAfter != null ? $", broke after {result.BrokeAfter}" : "")}, " +
                          $"{total:0.0} s = start-up {startup:0.0} + solves {solving:0.0} + updates {updating:0.0} + other {other:0.0}; " +
                          $"a solve {usual:0.000} s, an update {Median(history.Select(it => it.UpdateSeconds)):0.000} s; " +
                          $"within 1% of its last compliance from iteration {settled}, {wall[settled - 1]:0.0} s");
    }

    static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0.0;
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : 0.5 * (sorted[middle - 1] + sorted[middle]);
    }

    static void Print(TopologyIteration it, double[] densities)
    {
        if (it.Iteration % 10 == 0 || it.Iteration == 1)
            Console.WriteLine("  " + it);
    }

    /// <summary>Runs a bench and writes its history and final densities, as TopologyOptimisation's benches do.</summary>
    static void Run(Problem problem, List<SolidCell> cells, string openSees, string folder, Func<CellEnergies, TopologyResult> optimise)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        TopologyResult result;
        using (var analysis = Start(problem, openSees, folder, cells))
            result = optimise(analysis.Energies);
        var last = result.History.Last();
        Console.WriteLine("  " + last);
        Console.WriteLine($"  {result.History.Count} iterations, converged {result.Converged}, total {clock.Elapsed.TotalSeconds:0.0} s");

        using (var history = new StreamWriter(Path.Combine(folder, problem.Name + "_history.csv")))
        {
            history.WriteLine("iteration,compliance,volume,change,materials,sharpness,analysis_s,update_s");
            foreach (var it in result.History)
                history.WriteLine(string.Join(",", it.Iteration, N(it.Compliance), N(it.Volume), N(it.Change),
                    it.MaterialVolumes == null ? "" : string.Join(" ", it.MaterialVolumes.Select(N)), it.Sharpness.HasValue ? N(it.Sharpness.Value) : "",
                    N(it.AnalysisSeconds), N(it.UpdateSeconds)));
        }
        File.WriteAllLines(Path.Combine(folder, problem.Name + "_densities.csv"), result.Densities.Select(N));
        Console.WriteLine($"  wrote {problem.Name}_history.csv and _densities.csv to {folder}");
    }

    static double Sq(double v) => v * v;
}
