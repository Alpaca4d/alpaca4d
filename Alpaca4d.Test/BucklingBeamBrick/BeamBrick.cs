// Linear Buckling Analysis, beams against bricks: the same structures drawn twice - as beams, whose
// stress stiffness OpenSees gives (PDelta), and as SSP bricks, whose stress stiffness
// Buckling.SolidStiffness adds in C# from the stresses OpenSees reports - and run through
// Buckling.Analyse as the component runs them. Two ways to the same factor: where they meet, both are
// right. See README.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Alpaca4d;

class BeamBrick
{
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail == "" ? "" : "  " + detail)}");
        if (!ok) fails++;
    }

    static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string F(double v) => double.IsNaN(v) ? "-" : v.ToString(v >= 1000 ? "0.0" : v >= 100 ? "0.00" : "0.000", CultureInfo.InvariantCulture);
    static string Pct(double got, double want) => double.IsNaN(got) || double.IsNaN(want) ? "-" :
        (100 * (got / want - 1)).ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " %";

    // kN and m, steel, as Alpaca4d writes them.
    const double E = 210e6, Nu = 0.3, G = E / (2 * (1 + Nu));

    /// <summary>
    /// A deck the way Assemble lays one out - nodes, supports, materials, sections, elements, ties,
    /// then the load pattern - one Tcl entry per object, with where the pattern begins.
    /// </summary>
    sealed class Deck
    {
        readonly Dictionary<(long, long, long), int> at = new Dictionary<(long, long, long), int>();
        readonly List<string> nodes = new List<string>(), fixes = new List<string>(), materials = new List<string>(),
                              sections = new List<string>(), elements = new List<string>(), ties = new List<string>(),
                              elementLoads = new List<string>();
        readonly Dictionary<int, double[]> loads = new Dictionary<int, double[]>();
        public readonly Dictionary<int, double[]> Coordinates = new Dictionary<int, double[]>();
        int element = 0, material = 0, section = 100;
        public readonly int Ndf;
        public int Bricks;

        public Deck(int ndf) { Ndf = ndf; }

        public int Node(double x, double y, double z)
        {
            var key = ((long)Math.Round(x * 1e7), (long)Math.Round(y * 1e7), (long)Math.Round(z * 1e7));
            if (!at.TryGetValue(key, out int tag))
            {
                at[key] = tag = at.Count + 1;
                nodes.Add($"node {tag} {N(x)} {N(y)} {N(z)}\n");
                Coordinates[tag] = new[] { x, y, z };
            }
            return tag;
        }

        public int Node(double[] p) => Node(p[0], p[1], p[2]);

        public void Fix(int node, string flags) => fixes.Add($"fix {node} {flags}\n");

        /// <summary>As RectangleCS.WriteTcl: an elastic section, shear deformable with α = 5/6.</summary>
        public int Section(Rectangle r)
        {
            sections.Add($"section Elastic {++section} {N(E)} {N(r.A)} {N(r.Iz)} {N(r.Iy)} {N(G)} {N(r.JAlpaca)} {N(5.0 / 6)} {N(5.0 / 6)}\n");
            return section;
        }

        /// <summary>As ForceBeamColumn.WriteTcl: a Linear transformation tagged like the element.</summary>
        public int Beam(int i, int j, int sec, double[] vecxz)
        {
            int tag = ++element;
            elements.Add($"geomTransf Linear {tag} {N(vecxz[0])} {N(vecxz[1])} {N(vecxz[2])}\n" +
                         $"element forceBeamColumn {tag} {i} {j} {tag} NewtonCotes {sec} 5 -mass 0\n");
            return tag;
        }

        /// <summary>As RigidLink.WriteTcl.</summary>
        public void RigidLink(int retained, int constrained) => ties.Add($"rigidLink beam {retained} {constrained}\n");

        /// <summary>As ElasticIsotropicMaterial.WriteTcl.</summary>
        public int Isotropic()
        {
            materials.Add($"nDMaterial ElasticIsotropic {++material} {N(E)} {N(Nu)} 0.0\n");
            return material;
        }

        /// <summary>
        /// As SSPbrick.WriteTcl, the corners in OpenSees' order - the bottom face, then the top, so
        /// that the brick's Jacobian is positive; turned round when they come the other way, as
        /// Utils.CleanHexahedron turns them - with its weight, γ per volume, an eighth on each node,
        /// as Assemble shares it.
        /// </summary>
        public int Brick(double[][] c, int mat, double gamma)
        {
            if (Jacobian(c, 0, 0, 0) < 0)
                c = new[] { c[0], c[3], c[2], c[1], c[4], c[7], c[6], c[5] };
            var tags = c.Select(Node).ToArray();
            int tag = ++element;
            Bricks++;
            elements.Add($"element SSPbrick {tag} {string.Join(" ", tags)} {mat}   \n");
            if (gamma != 0.0)
            {
                double share = gamma * Volume(c) / 8;
                foreach (var t in tags)
                    Load(t, 0, 0, -share);
            }
            return tag;
        }

        /// <summary>A force, and for a node of a beam a moment, added to what the node already carries.</summary>
        public void Load(int node, params double[] f)
        {
            if (!loads.TryGetValue(node, out var sum))
                loads[node] = sum = new double[Ndf];
            for (int i = 0; i < f.Length; i++)
                sum[i] += f[i];
        }

        /// <summary>A force per area on a face of four nodes, a quarter of it on each, as a bilinear face shares it.</summary>
        public void Pressure(int[] face, double[] perArea)
        {
            var p = face.Select(n => Coordinates[n]).ToArray();
            double area = 0.5 * Length(Cross(Minus(p[2], p[0]), Minus(p[3], p[1])));
            foreach (var n in face)
                Load(n, perArea[0] * area / 4, perArea[1] * area / 4, perArea[2] * area / 4);
        }

        /// <summary>As LineLoad.WriteTcl: a uniform load along a beam, in its local axes.</summary>
        public void BeamUniform(int ele, double wy, double wz, double wx) =>
            elementLoads.Add($"eleLoad -ele {ele} -type -beamUniform {N(wy)} {N(wz)} {N(wx)}");

        public (List<string> tcl, int patternStart, int nodes) Build()
        {
            var tcl = new List<string> { $"model BasicBuilder -ndm 3 -ndf {Ndf}\n" };
            tcl.AddRange(nodes); tcl.AddRange(fixes); tcl.AddRange(materials); tcl.AddRange(sections);
            tcl.AddRange(elements); tcl.AddRange(ties);
            int start = tcl.Count;
            var lines = loads.OrderBy(l => l.Key).Where(l => l.Value.Any(v => v != 0.0))
                             .Select(l => $"load {l.Key} {string.Join(" ", l.Value.Select(N))}")
                             .Concat(elementLoads);
            tcl.Add("timeSeries Linear 1 -factor 1.0\n" +
                    "pattern Plain 1 1 -fact 1 {\n" + string.Concat(lines.Select(l => "\t" + l + "\n")) + "}\n");
            return (tcl, start, at.Count);
        }
    }

    // ------------------------------------------------------------------ geometry

    static double[] Minus(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    static double Length(double[] a) => Math.Sqrt(Dot(a, a));

    static readonly int[] Xi = { -1, 1, 1, -1, -1, 1, 1, -1 }, Eta = { -1, -1, 1, 1, -1, -1, 1, 1 }, Zeta = { -1, -1, -1, -1, 1, 1, 1, 1 };

    /// <summary>det ∂x/∂ξ of a trilinear brick at (ξ, η, ζ).</summary>
    static double Jacobian(double[][] c, double p, double q, double r)
    {
        var j = new double[3][];
        for (int k = 0; k < 3; k++) j[k] = new double[3];
        for (int a = 0; a < 8; a++)
        {
            double dx = 0.125 * Xi[a] * (1 + Eta[a] * q) * (1 + Zeta[a] * r);
            double dy = 0.125 * Eta[a] * (1 + Xi[a] * p) * (1 + Zeta[a] * r);
            double dz = 0.125 * Zeta[a] * (1 + Xi[a] * p) * (1 + Eta[a] * q);
            for (int x = 0; x < 3; x++)
            {
                j[0][x] += dx * c[a][x];
                j[1][x] += dy * c[a][x];
                j[2][x] += dz * c[a][x];
            }
        }
        return Dot(j[0], Cross(j[1], j[2]));
    }

    static double Volume(double[][] c)
    {
        double g = 1 / Math.Sqrt(3), v = 0;
        foreach (var r in new[] { -g, g })
            foreach (var q in new[] { -g, g })
                foreach (var p in new[] { -g, g })
                    v += Math.Abs(Jacobian(c, p, q, r));
        return v;
    }

    /// <summary>
    /// Bricks on a structured grid: grid point (i, j, k) at <paramref name="at"/>, cell (i, j, k)
    /// kept when <paramref name="keep"/> says so.
    /// </summary>
    static void Bricks(Deck d, int ni, int nj, int nk, Func<int, int, int, double[]> at, int mat, double gamma,
                       Func<int, int, int, bool> keep = null)
    {
        for (int i = 0; i < ni; i++)
            for (int j = 0; j < nj; j++)
                for (int k = 0; k < nk; k++)
                {
                    if (keep != null && !keep(i, j, k))
                        continue;
                    var c = new double[8][];
                    for (int a = 0; a < 8; a++)
                        c[a] = at(i + (Xi[a] + 1) / 2, j + (Eta[a] + 1) / 2, k + (Zeta[a] + 1) / 2);
                    d.Brick(c, mat, gamma);
                }
    }

    /// <summary>Grid lines over segments of a line, each cut into its own number of cells.</summary>
    static double[] Lines(params (double from, double to, int cells)[] segments)
    {
        var lines = new List<double> { segments[0].from };
        foreach (var segment in segments)
            for (int c = 1; c <= segment.cells; c++)
                lines.Add(segment.from + (segment.to - segment.from) * c / segment.cells);
        return lines.ToArray();
    }

    /// <summary>A solid rectangle as a beam's section: its area, second moments and torsion constant.</summary>
    sealed class Rectangle
    {
        public double A, Iz, Iy, J, JAlpaca, Iw;

        /// <param name="along">The side along local y, which Iz bends it across.</param>
        /// <param name="across">The side along local z, which Iy bends it across.</param>
        public Rectangle(double along, double across)
        {
            A = along * across;
            Iz = across * along * along * along / 12;
            Iy = along * across * across * across / 12;
            double a = Math.Max(along, across), b = Math.Min(along, across);
            // Saint-Venant's series for a solid rectangle.
            double sum = 0;
            for (int n = 1; n < 400; n += 2)
                sum += Math.Tanh(n * Math.PI * a / (2 * b)) / Math.Pow(n, 5);
            J = a * b * b * b / 3 * (1 - 192 / Math.Pow(Math.PI, 5) * b / a * sum);
            // What RectangleCS writes.
            JAlpaca = a * b * b * b / (3 + 4.1 * Math.Pow(b / a, 1.5));
            // A narrow rectangle's warping constant, as thin-walled theory has it.
            Iw = a * a * a * b * b * b / 144;
        }
    }

    // ------------------------------------------------------------------ running

    static string Folder;
    static string OpenSees;
    static bool Sparse;

    static Buckling.OpenSeesRunner Runner(string openSees) => deckPath =>
    {
        var start = new ProcessStartInfo(openSees, "\"" + deckPath + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(deckPath),
        };
        using (var p = Process.Start(start))
        {
            var stdout = p.StandardOutput.ReadToEndAsync();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (stdout.Result, stderr, p.ExitCode);
        }
    };

    sealed class Run
    {
        public Buckling.Outcome Outcome;
        public double Seconds;
        public int Bricks;
        public double[] Factors => Outcome.Factors;
        public double First => Factors.Length > 0 ? Factors[0] : double.NaN;
    }

    static Run Analyse(Deck deck, int divisions, int modes)
    {
        var (tcl, start, nodes) = deck.Build();
        var clock = Stopwatch.StartNew();
        var outcome = Buckling.Analyse(tcl, start, divisions, modes, nodes, Folder, "buckling", null, Sparse, Runner(OpenSees));
        clock.Stop();
        if (outcome.Failure != null)
            Console.WriteLine("        (" + outcome.Failure + ")");
        foreach (var w in outcome.Deck.Warnings)
            Console.WriteLine("        warning: " + w);
        return new Run { Outcome = outcome, Seconds = clock.Elapsed.TotalSeconds, Bricks = deck.Bricks };
    }

    /// <summary>
    /// How alike two shapes are, 0 to 1: the modal assurance criterion of the beams' translations at
    /// their nodes against the bricks', each brick node counted at the beam node nearest it and those
    /// averaged - which is the translation of the section there.
    /// </summary>
    static double Mac(Run beam, Run brick)
    {
        if (beam.Outcome.Shapes.Count == 0 || brick.Outcome.Shapes.Count == 0)
            return double.NaN;
        var beamShape = beam.Outcome.Shapes[0];
        var beamNodes = beam.Outcome.Deck.Nodes.Values.ToList();
        var sums = new Dictionary<int, double[]>();
        var counts = new Dictionary<int, int>();
        foreach (var kv in brick.Outcome.Shapes[0])
        {
            var n = brick.Outcome.Deck.Nodes[kv.Key];
            var nearest = beamNodes.OrderBy(b => (b.X - n.X) * (b.X - n.X) + (b.Y - n.Y) * (b.Y - n.Y) + (b.Z - n.Z) * (b.Z - n.Z)).First();
            if (!sums.TryGetValue(nearest.Tag, out var s))
            {
                sums[nearest.Tag] = s = new double[3];
                counts[nearest.Tag] = 0;
            }
            for (int i = 0; i < 3; i++) s[i] += kv.Value[i];
            counts[nearest.Tag]++;
        }
        double ab = 0, aa = 0, bb = 0;
        foreach (var kv in sums)
        {
            var a = beamShape.TryGetValue(kv.Key, out var v) ? v : new double[6];
            for (int i = 0; i < 3; i++)
            {
                double b = kv.Value[i] / counts[kv.Key];
                ab += a[i] * b; aa += a[i] * a[i]; bb += b * b;
            }
        }
        return ab * ab / (aa * bb);
    }

    /// <summary>Which way a shape moves most: x, y or z.</summary>
    static string Way(Run run)
    {
        if (run.Outcome.Shapes.Count == 0) return "-";
        var sum = new double[3];
        foreach (var v in run.Outcome.Shapes[0].Values)
            for (int i = 0; i < 3; i++) sum[i] += v[i] * v[i];
        return "xyz"[Array.IndexOf(sum, sum.Max())].ToString();
    }

    sealed class Row
    {
        public string Name, Reference, Beams = "-";
        public double Exact = double.NaN, BeamR = double.NaN, BrickR, Mac = double.NaN;
        public int Bricks, Equations;
        public double Seconds;
    }

    static readonly List<Row> Rows = new List<Row>();

    /// <summary>A structure drawn as beams: its deck for n pieces a member, and whether the analysis splits it into them.</summary>
    sealed class BeamModel
    {
        public string Label = "";
        public Func<int, Deck> Build;
        /// <summary>True: the members are drawn whole and the analysis splits each into n; false: the deck draws the n itself.</summary>
        public bool Split = true;
    }

    static BeamModel Beams(Func<int, Deck> build, string label = "", bool split = true) => new BeamModel { Build = build, Label = label, Split = split };

    /// <summary>
    /// One structure both ways: the beams at n and 2n pieces a member, the bricks at a mesh and the
    /// mesh halved, each pair extrapolated as their error goes, h². The bricks are checked against
    /// the beams, and with the closed form where there is one. Given two beam models that bound the
    /// structure - its corners as points and as rigid - the bricks are checked to fall between them.
    /// </summary>
    static Row Compare(string name, BeamModel[] beams, int n,
                       Func<int, Deck> brick, int modes,
                       double exact, string reference, double tolerance, double beamTolerance = 0.002)
    {
        Console.WriteLine($"\n{name}");
        var row = new Row { Name = name, Exact = exact, Reference = reference };

        Run firstBeam = null;
        var labels = new List<string>();
        var extrapolations = new List<double>();
        foreach (var model in beams)
        {
            string label = model.Label;
            var b1 = Analyse(model.Build(n), model.Split ? n : 1, modes);
            var b2 = Analyse(model.Build(2 * n), model.Split ? 2 * n : 1, modes);
            double extrapolated = (4 * b2.First - b1.First) / 3;
            Console.WriteLine($"  beams{(label == "" ? "" : ", " + label)}, {n} / {2 * n} pieces a member: {F(b1.First)}  {F(b2.First)}  -> {F(extrapolated)}" +
                              $"   modes {string.Join(" ", b2.Factors.Select(F))}  ({Way(b2)})");
            if (model == beams[0] && !double.IsNaN(exact))
                Check($"beams{(label == "" ? "" : ", " + label)}, against {reference}", Math.Abs(extrapolated / exact - 1) < beamTolerance, Pct(extrapolated, exact));
            labels.Add(beams.Length > 1 ? $"{F(extrapolated)} ({label})" : F(extrapolated));
            extrapolations.Add(extrapolated);
            if (firstBeam == null)
            {
                firstBeam = b2;
                row.BeamR = extrapolated;
            }
        }
        row.Beams = string.Join("; ", labels);

        var s1 = Analyse(brick(1), 1, modes);
        var s2 = Analyse(brick(2), 1, modes);
        row.BrickR = (4 * s2.First - s1.First) / 3;
        row.Bricks = s2.Bricks; row.Equations = s2.Outcome.Equations; row.Seconds = s2.Seconds;
        Console.WriteLine($"  bricks, {s1.Bricks} / {s2.Bricks}: {F(s1.First)}  {F(s2.First)}  -> {F(row.BrickR)}" +
                          $"   modes {string.Join(" ", s2.Factors.Select(F))}  ({Way(s2)}; {s2.Outcome.Equations} equations, {s2.Seconds:0.0} s)");
        if (!double.IsNaN(exact))
            Console.WriteLine($"  {reference}: {F(exact)}");

        if (beams.Length == 1)
            Check("bricks against beams", Math.Abs(row.BrickR / row.BeamR - 1) < tolerance, Pct(row.BrickR, row.BeamR));
        else if (beams.Length == 2)
        {
            double low = extrapolations.Min(), high = extrapolations.Max(), share = (row.BrickR - extrapolations[0]) / (extrapolations[1] - extrapolations[0]);
            Check($"bricks between the beams, {beams[0].Label} and {beams[1].Label}", low < row.BrickR && row.BrickR < high,
                  $"{share:0%} of the way from {beams[0].Label} to {beams[1].Label}");
        }
        if (firstBeam != null)
        {
            row.Mac = Mac(firstBeam, s2);
            Check($"the same first shape, MAC {row.Mac:0.0000}", row.Mac > 0.98);
        }
        if (!double.IsNaN(exact) && beams.Length < 2)
            Check($"bricks against {reference}", Math.Abs(row.BrickR / exact - 1) < tolerance, Pct(row.BrickR, exact));
        Rows.Add(row);
        return row;
    }

    // ------------------------------------------------------------------ columns

    /// <summary>
    /// A column bx along X by by along Y, L tall, foot at the origin, the axis along Z. A foot or head
    /// "fixed" holds every node; "pinned" holds the line across it along Y, at x = 0, against moving
    /// along X (and the foot's along Z too), and its middle node along Y: it turns about Y, the weak
    /// way, and not about X. P is spread over the top face; γ is its weight per volume.
    /// </summary>
    static Deck BrickColumn(double bx, double by, double L, int nx, int ny, int nz, string foot, string head, double P, double gamma)
    {
        var d = new Deck(3);
        int mat = d.Isotropic();
        Func<int, int, int, double[]> at = (i, j, k) => new[] { -bx / 2 + bx * i / nx, -by / 2 + by * j / ny, L * k / nz };
        Bricks(d, nx, ny, nz, at, mat, gamma);
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
            {
                bool line = 2 * i == nx, middle = line && 2 * j == ny;
                if (foot == "fixed") d.Fix(d.Node(at(i, j, 0)), "1 1 1");
                else if (foot == "pinned" && line) d.Fix(d.Node(at(i, j, 0)), middle ? "1 1 1" : "1 0 1");
                if (head == "pinned" && line) d.Fix(d.Node(at(i, j, nz)), middle ? "1 1 0" : "1 0 0");
            }
        if (P != 0)
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    d.Pressure(new[] { at(i, j, nz), at(i + 1, j, nz), at(i + 1, j + 1, nz), at(i, j + 1, nz) }.Select(d.Node).ToArray(),
                               new[] { 0, 0, -P / (bx * by) });
        return d;
    }

    /// <summary>The same column as one forceBeamColumn, split into pieces by the analysis.</summary>
    static Deck BeamColumn(double bx, double by, double L, string foot, string head, double P, double gamma)
    {
        var d = new Deck(6);
        int a = d.Node(0, 0, 0), b = d.Node(0, 0, L);
        d.Fix(a, foot == "fixed" ? "1 1 1 1 1 1" : "1 1 1 1 0 1");
        if (head == "pinned") d.Fix(b, "1 1 0 0 0 1");
        // vecxz along X: local z is X, so Iy bends it along X - bx, the weak way.
        int e = d.Beam(a, b, d.Section(new Rectangle(by, bx)), new[] { 1.0, 0, 0 });
        if (P != 0) d.Load(b, 0, 0, -P, 0, 0, 0);
        if (gamma != 0) d.BeamUniform(e, 0, 0, -gamma * bx * by);
        return d;
    }

    /// <summary>Euler's load with the shear of a Timoshenko column, as Engesser has it: P_E / (1 + P_E / (κ G A)).</summary>
    static double Engesser(double x, double bx, double by, double L)
    {
        double pe = x * x * E * by * bx * bx * bx / 12 / (L * L);
        return pe / (1 + pe / (5.0 / 6 * G * bx * by));
    }

    static void Columns()
    {
        const double P = 1.0;   // the factors are loads, kN
        double bx = 0.1, by = 0.2;

        Compare("Cantilever column, 0.1 x 0.2 m, 2 m, a load on top",
                new[] { Beams(n => BeamColumn(bx, by, 2, "fixed", "free", P, 0)) }, 8,
                m => BrickColumn(bx, by, 2, 2 * m, 4 * m, 20 * m, "fixed", "free", P, 0), 2,
                Engesser(Math.PI / 2, bx, by, 2), "Euler, fixed-free", 0.01);

        Compare("Pinned-pinned column, 0.1 x 0.2 m, 4 m",
                new[] { Beams(n => BeamColumn(bx, by, 4, "pinned", "pinned", P, 0)) }, 8,
                m => BrickColumn(bx, by, 4, 2 * m, 4 * m, 40 * m, "pinned", "pinned", P, 0), 2,
                Engesser(Math.PI, bx, by, 4), "Euler, pinned-pinned", 0.005);

        Compare("Fixed-pinned column, 0.1 x 0.2 m, 3 m",
                new[] { Beams(n => BeamColumn(bx, by, 3, "fixed", "pinned", P, 0)) }, 8,
                m => BrickColumn(bx, by, 3, 2 * m, 4 * m, 30 * m, "fixed", "pinned", P, 0), 2,
                Engesser(4.493409457909064, bx, by, 3), "Euler, fixed-pinned", 0.01);

        // Stocky: L / b = 4, and shear takes 4 % off Euler. Both carry it - the beams through α = 5/6.
        Compare("Stocky cantilever, 0.2 x 0.3 m, 0.8 m",
                new[] { Beams(n => BeamColumn(0.2, 0.3, 0.8, "fixed", "free", P, 0)) }, 8,
                m => BrickColumn(0.2, 0.3, 0.8, 4 * m, 6 * m, 8 * m, "fixed", "free", P, 0), 2,
                Engesser(Math.PI / 2, 0.2, 0.3, 0.8), "Euler with shear (Engesser)", 0.015);

        // Greenhill: (q L)_cr = 7.837 EI / L², q the weight of a metre of it.
        double gamma = 78.5, L = 4;
        double greenhill = 7.837347 * E * by * bx * bx * bx / 12 / (L * L) / (gamma * bx * by * L);
        Compare("Cantilever under its own weight, 0.1 x 0.2 m, 4 m",
                new[] { Beams(n => BeamColumn(bx, by, L, "fixed", "free", 0, gamma)) }, 8,
                m => BrickColumn(bx, by, L, 2 * m, 4 * m, 40 * m, "fixed", "free", 0, gamma), 2,
                greenhill, "Greenhill", 0.01);
    }

    // ------------------------------------------------------------------ a frame

    /// <summary>
    /// A portal frame in the XZ plane, centre lines H tall and B wide, of members b in the plane and
    /// w across it, feet fixed, a load down on each column's head. Out of its plane it is free.
    ///
    /// Drawn on its centre lines, the beams meet at points; the bricks' corners are solid, and the
    /// columns stand only H - b/2 clear, the beam spans B - b clear. So the beams are drawn a second
    /// time with their corners rigid - each member stopping at the corner's face, a rigid link from
    /// there to the corner's middle. Neither is the solid frame: a solid corner is not rigid, the
    /// members' bending running on into it, which is why frame programs take a rigid zone of less
    /// than the whole joint. The bricks must fall between the two.
    /// </summary>
    static void Portal()
    {
        double H = 3, B = 4, b = 0.1, w = 0.3, P = 1.0;

        Func<bool, Deck> beams = rigid =>
        {
            var d = new Deck(6);
            int a = d.Node(0, 0, 0), c = d.Node(0, 0, H), e = d.Node(B, 0, H), f = d.Node(B, 0, 0);
            d.Fix(a, "1 1 1 1 1 1");
            d.Fix(f, "1 1 1 1 1 1");
            int member = d.Section(new Rectangle(w, b));   // local z along X for the columns, Z for the beam: Iy bends them in the plane
            var up = new[] { 1.0, 0, 0 };
            var across = new[] { 0, 0, 1.0 };
            if (!rigid)
            {
                d.Beam(a, c, member, up);
                d.Beam(f, e, member, up);
                d.Beam(c, e, member, across);
            }
            else
            {
                int c0 = d.Node(0, 0, H - b / 2), e0 = d.Node(B, 0, H - b / 2), c1 = d.Node(b / 2, 0, H), e1 = d.Node(B - b / 2, 0, H);
                d.Beam(a, c0, member, up);
                d.Beam(f, e0, member, up);
                d.Beam(c1, e1, member, across);
                foreach (var end in new[] { c0, c1 })
                    d.RigidLink(c, end);
                foreach (var end in new[] { e0, e1 })
                    d.RigidLink(e, end);
            }
            d.Load(c, 0, 0, -P, 0, 0, 0);
            d.Load(e, 0, 0, -P, 0, 0, 0);
            return d;
        };

        Func<int, Deck> bricks = m =>
        {
            var d = new Deck(3);
            int mat = d.Isotropic();
            int thick = 2 * m;
            double step = 0.1 / m;
            var xs = Lines((-b / 2, b / 2, thick), (b / 2, B - b / 2, (int)Math.Round((B - b) / step)), (B - b / 2, B + b / 2, thick));
            var ys = Lines((-w / 2, w / 2, 3 * m));
            var zs = Lines((0, H - b / 2, (int)Math.Round((H - b / 2) / step)), (H - b / 2, H + b / 2, thick));
            Func<int, int, int, double[]> at = (i, j, k) => new[] { xs[i], ys[j], zs[k] };
            Func<double, bool> inColumn = x => Math.Abs(x) < b / 2 + 1e-9 || Math.Abs(x - B) < b / 2 + 1e-9;
            Bricks(d, xs.Length - 1, ys.Length - 1, zs.Length - 1, at, mat, 0,
                   (i, j, k) => inColumn((xs[i] + xs[i + 1]) / 2) || Math.Abs((zs[k] + zs[k + 1]) / 2 - H) < b / 2);
            for (int i = 0; i < xs.Length; i++)
                for (int j = 0; j < ys.Length; j++)
                    if (inColumn(xs[i]))
                        d.Fix(d.Node(at(i, j, 0)), "1 1 1");
            int top = zs.Length - 1;
            for (int i = 0; i < xs.Length - 1; i++)
                if (inColumn((xs[i] + xs[i + 1]) / 2))
                    for (int j = 0; j < ys.Length - 1; j++)
                        d.Pressure(new[] { at(i, j, top), at(i + 1, j, top), at(i + 1, j + 1, top), at(i, j + 1, top) }.Select(d.Node).ToArray(),
                                   new[] { 0, 0, -P / (b * w) });
            return d;
        };

        // The sway alignment chart, fixed feet: tan x = -x G / 6, G = (I/H) / (I/B) at the head.
        double g = B / H, lo = Math.PI / 2 + 1e-9, hi = Math.PI - 1e-9;
        for (int k = 0; k < 200; k++)
        {
            double mid = (lo + hi) / 2;
            if (Math.Sign(Math.Tan(mid) + mid * g / 6) == Math.Sign(Math.Tan(lo) + lo * g / 6)) lo = mid; else hi = mid;
        }
        double root = (lo + hi) / 2;
        double chart = root * root * E * w * b * b * b / 12 / (H * H) / P;

        Compare("Portal frame, fixed feet, 3 x 4 m, 0.1 x 0.3 m members, a load on each head",
                new[] { Beams(n => beams(false), "centre lines"), Beams(n => beams(true), "rigid corners") }, 8, bricks, 3,
                chart, "the sway alignment chart (centre lines)", 0.005, 0.005);
    }

    // ------------------------------------------------------------------ an arch

    /// <summary>
    /// A circular arch, two hinged, of centre-line radius R over ±α from its crown, d deep and w wide,
    /// under its own weight - q per metre of arch. The hinges hold the line across each foot, at
    /// mid-depth: it turns about Y alone. There is no closed form for a vertical load that keeps its
    /// direction; the two models stand for each other.
    /// </summary>
    static void Arch()
    {
        double R = 5, alpha = Math.PI / 3, d = 0.1, w = 0.3, q = 1.0;
        Func<double, double, double, double[]> point = (r, y, theta) => new[] { r * Math.Sin(theta), y, r * Math.Cos(theta) - R * Math.Cos(alpha) };

        Func<int, Deck> beams = n =>
        {
            int segments = 4 * n;
            var deck = new Deck(6);
            int sec = deck.Section(new Rectangle(d, w));   // local y in the plane: Iz bends it there
            var tags = Enumerable.Range(0, segments + 1).Select(k => deck.Node(point(R, 0, -alpha + 2 * alpha * k / segments))).ToArray();
            deck.Fix(tags[0], "1 1 1 1 0 1");
            deck.Fix(tags[segments], "1 1 1 1 0 1");
            for (int k = 0; k < segments; k++)
            {
                int e = deck.Beam(tags[k], tags[k + 1], sec, new[] { 0, 1.0, 0 });
                var x = Minus(deck.Coordinates[tags[k + 1]], deck.Coordinates[tags[k]]);
                double ex = x[0] / Length(x), ez = x[2] / Length(x);
                // local z = Y, local y = Y × x = (ez, 0, -ex); the load (0, 0, -q) on them.
                deck.BeamUniform(e, q * ex, 0, -q * ez);
            }
            return deck;
        };

        Func<int, Deck> bricks = m =>
        {
            var deck = new Deck(3);
            int mat = deck.Isotropic();
            int nr = 2 * m, nw = 3 * m, na = 96 * m;
            Func<int, int, int, double[]> at = (i, j, k) => point(R - d / 2 + d * i / nr, -w / 2 + w * j / nw, -alpha + 2 * alpha * k / na);
            Bricks(deck, nr, nw, na, at, mat, q / (d * w));
            for (int j = 0; j <= nw; j++)
            {
                deck.Fix(deck.Node(at(nr / 2, j, 0)), "1 1 1");
                deck.Fix(deck.Node(at(nr / 2, j, na)), "1 1 1");
            }
            return deck;
        };

        Compare("Two-hinged arch, radius 5 m over 120 degrees, 0.1 x 0.3 m, its own weight",
                new[] { Beams(beams, split: false) }, 8, bricks, 2, double.NaN, "", 0.005);
    }

    // ------------------------------------------------------------------ lateral-torsional buckling

    /// <summary>
    /// Lateral-torsional buckling by Vlasov's theory of thin-walled beams, the reference for the
    /// narrow beams: the sideways bending v and the twist φ on cubic Hermite elements,
    /// U = ½∫(EIz v''² + GJ φ'² + EIw φ''²) dx, and the load's second-order work ∫ M v'' φ dx - the
    /// load on the centroid, so nothing for its height. The factor is 1 / the largest |μ| of
    /// K_g x = μ K x, found by power iteration on (K⁻¹ K_g)²: buckling one way or the other, the μ
    /// come in ± pairs. Fixed are the dofs of the first node (v, v', φ, φ' = 0, 1, 2, 3) in
    /// <paramref name="start"/>, of the last in <paramref name="end"/>.
    /// </summary>
    static double Vlasov(double L, double EIz, double GJ, double EIw, Func<double, double> moment, int[] start, int[] end, int n = 100)
    {
        int size = 4 * (n + 1);
        var k = new double[size, size];
        var kg = new double[size, size];
        double h = L / n;
        double[] gp = { -0.8611363115940526, -0.3399810435848563, 0.3399810435848563, 0.8611363115940526 };
        double[] gw = { 0.3478548451374538, 0.6521451548625461, 0.6521451548625461, 0.3478548451374538 };
        for (int e = 0; e < n; e++)
        {
            int[] v = { 4 * e, 4 * e + 1, 4 * e + 4, 4 * e + 5 }, p = { 4 * e + 2, 4 * e + 3, 4 * e + 6, 4 * e + 7 };
            for (int g = 0; g < 4; g++)
            {
                double s = (gp[g] + 1) / 2, w = gw[g] * h / 2, m = moment(e * h + s * h);
                double[] n0 = { 1 - 3 * s * s + 2 * s * s * s, h * (s - 2 * s * s + s * s * s), 3 * s * s - 2 * s * s * s, h * (s * s * s - s * s) };
                double[] n1 = { (6 * s * s - 6 * s) / h, 1 - 4 * s + 3 * s * s, (6 * s - 6 * s * s) / h, 3 * s * s - 2 * s };
                double[] n2 = { (12 * s - 6) / (h * h), (6 * s - 4) / h, (6 - 12 * s) / (h * h), (6 * s - 2) / h };
                for (int a = 0; a < 4; a++)
                    for (int b = 0; b < 4; b++)
                    {
                        k[v[a], v[b]] += EIz * n2[a] * n2[b] * w;
                        k[p[a], p[b]] += (GJ * n1[a] * n1[b] + EIw * n2[a] * n2[b]) * w;
                        kg[v[a], p[b]] += m * n2[a] * n0[b] * w;
                        kg[p[b], v[a]] += m * n2[a] * n0[b] * w;
                    }
            }
        }
        var free = Enumerable.Range(0, size).Where(i => !(i < 4 && start.Contains(i)) && !(i >= 4 * n && end.Contains(i - 4 * n))).ToArray();
        int f = free.Length;
        // Cholesky of K on the free dofs.
        var c = new double[f, f];
        for (int i = 0; i < f; i++)
            for (int j = 0; j <= i; j++)
            {
                double sum = k[free[i], free[j]];
                for (int q = 0; q < j; q++) sum -= c[i, q] * c[j, q];
                c[i, j] = i == j ? Math.Sqrt(sum) : sum / c[j, j];
            }
        Func<double[], double[]> apply = x =>
        {
            var y = new double[f];
            for (int i = 0; i < f; i++)
                for (int j = 0; j < f; j++)
                    y[i] += kg[free[i], free[j]] * x[j];
            for (int i = 0; i < f; i++) { for (int q = 0; q < i; q++) y[i] -= c[i, q] * y[q]; y[i] /= c[i, i]; }
            for (int i = f - 1; i >= 0; i--) { for (int q = i + 1; q < f; q++) y[i] -= c[q, i] * y[q]; y[i] /= c[i, i]; }
            return y;
        };
        var vector = Enumerable.Range(0, f).Select(i => 1.0 + 0.1 * Math.Sin(i)).ToArray();
        double mu2 = 0;
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            var next = apply(apply(vector));
            double norm = Math.Sqrt(next.Sum(x => x * x)), before = Math.Sqrt(vector.Sum(x => x * x));
            double estimate = norm / before;
            vector = next.Select(x => x / norm).ToArray();
            if (Math.Abs(estimate / mu2 - 1) < 1e-13) { mu2 = estimate; break; }
            mu2 = estimate;
        }
        return 1 / Math.Sqrt(mu2);
    }

    /// <summary>
    /// A deep narrow beam, L long along X, d deep (Z) and t thick (Y), as bricks: a cantilever, its
    /// root held at every node and a load P down spread over its end, or a beam on forks - the line
    /// down the middle of each end held sideways, its middle node down too, and the first along X -
    /// bent by end moments M, as a stress M z / I over each end face, shared as a linear stress is.
    /// </summary>
    static Deck NarrowBeam(double L, double depth, double t, int m, bool cantilever, double load)
    {
        var d = new Deck(3);
        int mat = d.Isotropic();
        int nx = 40 * m, ny = 2 * m, nz = 8 * m;
        Func<int, int, int, double[]> at = (i, j, k) => new[] { L * i / nx, -t / 2 + t * j / ny, -depth / 2 + depth * k / nz };
        Bricks(d, nx, ny, nz, at, mat, 0);
        if (cantilever)
        {
            for (int j = 0; j <= ny; j++)
                for (int k = 0; k <= nz; k++)
                    d.Fix(d.Node(at(0, j, k)), "1 1 1");
            for (int j = 0; j < ny; j++)
                for (int k = 0; k < nz; k++)
                    d.Pressure(new[] { at(nx, j, k), at(nx, j + 1, k), at(nx, j + 1, k + 1), at(nx, j, k + 1) }.Select(d.Node).ToArray(),
                               new[] { 0, 0, -load / (t * depth) });
            return d;
        }
        double inertia = t * depth * depth * depth / 12;
        foreach (int i in new[] { 0, nx })
        {
            for (int k = 0; k <= nz; k++)
                d.Fix(d.Node(at(i, ny / 2, k)), 2 * k != nz ? "0 1 0" : i == 0 ? "1 1 1" : "0 1 1");
            double sign = i == 0 ? -1 : 1;
            for (int j = 0; j < ny; j++)
                for (int k = 0; k < nz; k++)
                {
                    // ∫ N σ over the face, σ = M z / I linear in z: (2 σ1 + σ2) h / 6 at the lower nodes, half to each side.
                    double z1 = at(i, j, k)[2], z2 = at(i, j, k + 1)[2], h = z2 - z1, width = t / ny;
                    double s1 = sign * load * z1 / inertia, s2 = sign * load * z2 / inertia;
                    double lower = (2 * s1 + s2) * h / 6 * width / 2, upper = (s1 + 2 * s2) * h / 6 * width / 2;
                    foreach (int jj in new[] { j, j + 1 })
                    {
                        d.Load(d.Node(at(i, jj, k)), lower, 0, 0);
                        d.Load(d.Node(at(i, jj, k + 1)), upper, 0, 0);
                    }
                }
        }
        return d;
    }

    static void LateralTorsional()
    {
        double L = 2, depth = 0.2, t = 0.02, P = 1.0;
        var section = new Rectangle(t, depth);   // local y across it: Iz the weak way
        double EIz = E * section.Iz, GJ = G * section.J, EIw = E * section.Iw;

        // The beams see none of it: with no axial force PDelta has nothing to soften.
        Console.WriteLine("\nDeep narrow beams, as beams");
        foreach (bool held in new[] { true, false })
        {
            var d = new Deck(6);
            int a = d.Node(0, 0, 0), b = d.Node(L, 0, 0);
            d.Fix(a, held ? "1 1 1 1 1 1" : "1 1 1 1 0 0");
            if (!held) d.Fix(b, "0 1 1 1 0 0");
            d.Beam(a, b, d.Section(section), new[] { 0, 0, 1.0 });
            if (held) d.Load(b, 0, 0, -P, 0, 0, 0);
            else { d.Load(a, 0, 0, 0, 0, -P, 0); d.Load(b, 0, 0, 0, 0, P, 0); }
            var beam = Analyse(d, 8, 1);
            Check($"{(held ? "the cantilever" : "the beam on forks")}: nothing to buckle - PDelta has no lateral-torsional buckling",
                  beam.Outcome.Failure == null && beam.Factors.Length == 0, beam.Factors.Length > 0 ? F(beam.First) : "");
        }

        // A cantilever's root held whole holds its warping too, and the twist is greatest near the root,
        // where the moment is: 5 % on Timoshenko and Gere's 4.013, which leaves warping out.
        double timoshenko = 4.013 * Math.Sqrt(EIz * GJ) / (L * L) / P;
        double cantilever = Vlasov(L, EIz, GJ, EIw, x => -P * (L - x), new[] { 0, 1, 2, 3 }, new int[0]);
        double free = Vlasov(L, EIz, GJ, 0, x => -P * (L - x), new[] { 0, 1, 2 }, new int[0]);
        Console.WriteLine($"\n  Timoshenko and Gere, 4.013 sqrt(EIz GJ) / L^2: {F(timoshenko)}; Vlasov without warping {F(free)} " +
                          $"({Pct(free, timoshenko)}), with it held at the root {F(cantilever)} ({Pct(cantilever, timoshenko)})");
        Check("the Vlasov reference without warping is Timoshenko and Gere's", Math.Abs(free / timoshenko - 1) < 5e-4, Pct(free, timoshenko));
        Compare("Deep narrow cantilever, 0.02 x 0.2 m, 2 m, a load down on its end (lateral-torsional)",
                new BeamModel[0], 8, m => NarrowBeam(L, depth, t, m, true, P), 2,
                cantilever, "Vlasov, warping held at the root", 0.01).Beams = "no mode";

        double forks = Math.PI / L * Math.Sqrt(EIz * GJ * (1 + Math.PI * Math.PI * EIw / (GJ * L * L)));
        double forksVlasov = Vlasov(L, EIz, GJ, EIw, x => 1.0, new[] { 0, 2 }, new[] { 0, 2 });
        Check("the Vlasov reference on forks is the closed form", Math.Abs(forksVlasov / forks - 1) < 1e-6, Pct(forksVlasov, forks));
        Compare("Deep narrow beam on forks, 0.02 x 0.2 m, 2 m, equal end moments (lateral-torsional)",
                new BeamModel[0], 8, m => NarrowBeam(L, depth, t, m, false, 1.0), 2,
                forks, "pi / L sqrt(EIz GJ (1 + pi^2 EIw / GJ L^2))", 0.01).Beams = "no mode";
    }

    // ------------------------------------------------------------------ the table

    static void Table()
    {
        Console.WriteLine("\n| structure | closed form | beams | bricks | bricks / beams | bricks / closed form | MAC | bricks, equations, s |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var r in Rows)
            Console.WriteLine($"| {r.Name} | {F(r.Exact)} | {r.Beams} | {F(r.BrickR)} | {Pct(r.BrickR, r.BeamR)} | " +
                              $"{Pct(r.BrickR, r.Exact)} | {(double.IsNaN(r.Mac) ? "-" : r.Mac.ToString("0.000", CultureInfo.InvariantCulture))} | " +
                              $"{r.Bricks}, {r.Equations}, {r.Seconds:0} |");
    }

    static void Main(string[] args)
    {
        OpenSees = args.FirstOrDefault(File.Exists);
        if (OpenSees == null) { Console.WriteLine("usage: BeamBrick.exe /path/to/OpenSees [columns|portal|arch|lateral ...]"); Environment.Exit(2); }
        var only = args.Where(a => !File.Exists(a)).ToList();
        Folder = Path.Combine(Path.GetTempPath(), "alpaca4d_beambrick_" + Process.GetCurrentProcess().Id);
        Directory.CreateDirectory(Folder);
        try
        {
            Sparse = Buckling.CanPrintSparse(Folder, Runner(OpenSees));
            Console.WriteLine($"=== {OpenSees}: matrices printed {(Sparse ? "sparse" : "dense")} ===");
            if (!Sparse)
                Console.WriteLine("The finer brick meshes have more than the 4000 equations a dense print takes; use an OpenSees newer than 3.8.0.");
            var cases = new Dictionary<string, Action> { ["columns"] = Columns, ["portal"] = Portal, ["arch"] = Arch, ["lateral"] = LateralTorsional };
            foreach (var c in cases)
                if (only.Count == 0 || only.Contains(c.Key))
                    c.Value();
            Table();
        }
        finally
        {
            try { Directory.Delete(Folder, true); } catch (IOException) { }
        }
        Console.WriteLine(fails == 0 ? "\nAll checks passed." : $"\n{fails} check(s) failed.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
