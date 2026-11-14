// Principal Stress Lines 3D checked inside a running Rhino 8 against closed-form solutions of three
// dimensional elasticity: a round bar twisted and pulled, whose lines are helices round its axis and
// rays out from it, and Boussinesq's point load on a half-space. The field and tracer are fed each
// solution's exact stresses on bricks and on ten node tetrahedra, then the component itself draws
// lines in a block OpenSees solved under Boussinesq's stresses, in its Solids unit. A line is measured by the angle
// between each of its segments and the exact principal direction at the segment's middle, and by
// its distance from the exact trajectory through the same seed. run.sh loads this beside the build
// under test and calls Run by reflection.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Loads;
using Alpaca4d.Material;
using Alpaca4d.Result;

public static class SolidStressLinesCheck
{
    static StringBuilder report;
    static int fails;
    static string folder, openSees;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>What the pictures are drawn from: name, then lines as lists of points, by family (0 exact).</summary>
    public static readonly List<(string name, int family, Polyline line)> Drawn = new List<(string, int, Polyline)>();

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        report.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Section(string title)
    {
        report.AppendLine();
        report.AppendLine(title);
    }

    public static string Run(string work, string solver)
    {
        folder = work;
        openSees = solver;
        report = new StringBuilder();
        fails = 0;
        Drawn.Clear();
        try
        {
            Eigen();
            ClosedForms();
            Robustness();
            FieldMode();
            Component();
            Beams();
        }
        catch (Exception e)
        {
            Check(false, "no exception: " + e);
        }
        report.AppendLine();
        report.AppendLine(fails == 0 ? "all 3D principal stress line checks passed" : $"{fails} 3D principal stress line check(s) failed");

        // What the pictures are drawn from: one line a row, name, family (0 exact), then the points.
        File.WriteAllLines(Path.Combine(work, "solid_lines.txt"), Drawn.Select(d => d.name + "|" + d.family + "|" +
            string.Join(";", d.line.Select(p => string.Format(Inv, "{0:R},{1:R},{2:R}", p.X, p.Y, p.Z)))));
        return report.ToString();
    }

    // ---------------------------------------------------------------- closed forms

    /// <summary>A closed-form stress field: xx, yy, zz, xy, yz, zx at a point, over a box.</summary>
    abstract class Exact
    {
        public string Name;
        public BoundingBox Box;
        public double Nu = 0.3;
        public abstract void Stress(Point3d p, double[] s);
        public virtual bool Inside(Point3d p) =>
            p.X >= Box.Min.X - 1e-12 && p.X <= Box.Max.X + 1e-12 && p.Y >= Box.Min.Y - 1e-12 && p.Y <= Box.Max.Y + 1e-12 &&
            p.Z >= Box.Min.Z - 1e-12 && p.Z <= Box.Max.Z + 1e-12;
        public double Size => Box.Diagonal.Length;

        double largest = -1;
        /// <summary>The largest principal difference over the box, sampled.</summary>
        public double Largest
        {
            get
            {
                if (largest >= 0) return largest;
                largest = 0;
                var v = new double[3]; var d = new Vector3d[3];
                for (int i = 0; i <= 20; i++)
                    for (int j = 0; j <= 20; j++)
                        for (int k = 0; k <= 20; k++)
                        {
                            var p = new Point3d(Box.Min.X + (Box.Max.X - Box.Min.X) * i / 20, Box.Min.Y + (Box.Max.Y - Box.Min.Y) * j / 20,
                                                Box.Min.Z + (Box.Max.Z - Box.Min.Z) * k / 20);
                            if (!Inside(p)) continue;
                            Principal(p, v, d);
                            largest = Math.Max(largest, v[0] - v[2]);
                        }
                return largest;
            }
        }

        public void Principal(Point3d p, double[] values, Vector3d[] vectors)
        {
            var s = new double[6];
            Stress(p, s);
            SolidPrincipalField.Eigen(s, values, vectors);
        }

        /// <summary>How far the family's principal stress stands from the nearest other one.</summary>
        public double Gap(Point3d p, int family)
        {
            var v = new double[3]; var d = new Vector3d[3];
            Principal(p, v, d);
            return family == 1 ? v[0] - v[1] : family == 3 ? v[1] - v[2] : Math.Min(v[0] - v[1], v[1] - v[2]);
        }

        public Vector3d Direction(Point3d p, int family)
        {
            var v = new double[3]; var d = new Vector3d[3];
            Principal(p, v, d);
            return d[family - 1];
        }

        double[] largestGap;
        /// <summary>The largest gap between the family's principal stress and the nearest other one over the box, sampled.</summary>
        public double LargestGap(int family)
        {
            if (largestGap == null)
            {
                largestGap = new double[4];
                for (int i = 0; i <= 20; i++)
                    for (int j = 0; j <= 20; j++)
                        for (int k = 0; k <= 20; k++)
                        {
                            var p = new Point3d(Box.Min.X + (Box.Max.X - Box.Min.X) * i / 20, Box.Min.Y + (Box.Max.Y - Box.Min.Y) * j / 20,
                                                Box.Min.Z + (Box.Max.Z - Box.Min.Z) * k / 20);
                            if (!Inside(p)) continue;
                            for (int f = 1; f <= 3; f++) largestGap[f] = Math.Max(largestGap[f], Gap(p, f));
                        }
            }
            return largestGap[family];
        }

        /// <summary>
        /// Where the direction is not unique: the family's principal stress within 2% of the nearest
        /// other one, measured against the largest that gap gets. Each family against its own: under
        /// Boussinesq's load the two smaller compressions stay close to one another everywhere, and
        /// measured against the large vertical one they would count as weak throughout.
        /// </summary>
        public bool Weak(Point3d p, int family) => Gap(p, family) < 0.02 * LargestGap(family);
    }

    /// <summary>
    /// A round bar along z, twisted - a shear K r round its axis - and pulled by S along it, seen
    /// through a square window round the axis. On each cylinder round the axis the largest and
    /// smallest principal stresses lie at a fixed angle to it, so their lines are helices; the middle
    /// one, nought, points straight out from the axis.
    /// </summary>
    sealed class TwistedBar : Exact
    {
        public double K = 1.0, S = 0.6;
        public TwistedBar() { Name = "twisted bar"; Box = new BoundingBox(-1, -1, 0, 1, 1, 6); }
        public override void Stress(Point3d p, double[] s)
        {
            s[0] = 0; s[1] = 0; s[2] = S; s[3] = 0; s[4] = K * p.X; s[5] = -K * p.Y;
        }
    }

    /// <summary>
    /// Timoshenko and Goodier's cantilever, 8 long and 2 deep, 0.5 wide: an end load spread as a
    /// parabola at x = 0, carried to the far end at x = 8; y up, z across the width. The plane stress
    /// solution, independent of z, and with Poisson's ratio nought exact in three dimensions too: the
    /// sides are free of stress.
    /// </summary>
    sealed class Cantilever : Exact
    {
        public double P = 100, C = 1, L = 8;
        public Cantilever() { Name = "cantilever"; Box = new BoundingBox(0, -1, -0.25, 8, 1, 0.25); Nu = 0; }
        public override void Stress(Point3d p, double[] s)
        {
            double i = 2 * C * C * C / 3;
            s[0] = -P * p.X * p.Y / i; s[1] = 0; s[2] = 0;
            s[3] = -P / (2 * i) * (C * C - p.Y * p.Y); s[4] = 0; s[5] = 0;
        }
    }

    /// <summary>
    /// Timoshenko and Goodier's simply supported beam under a uniform load q on its top, 10 long and
    /// 2 deep, 0.5 wide, x from midspan, y up: plane stress, and with Poisson's ratio nought exact in
    /// three dimensions. Its ends carry the parabolic shear of the reactions.
    /// </summary>
    sealed class SupportedBeam : Exact
    {
        public double Q = 10, C = 1, Half = 5;
        public SupportedBeam() { Name = "simply supported beam"; Box = new BoundingBox(-5, -1, -0.25, 5, 1, 0.25); Nu = 0; }
        public override void Stress(Point3d p, double[] s)
        {
            double i = 2 * C * C * C / 3, x = p.X, y = p.Y;
            s[0] = -Q / (2 * i) * (Half * Half - x * x) * y - Q / i * (y * y * y / 3 - C * C * y / 5);
            s[1] = Q / (2 * i) * (y * y * y / 3 - C * C * y - 2 * C * C * C / 3);
            s[2] = 0;
            s[3] = Q / (2 * i) * (C * C - y * y) * x;
            s[4] = 0; s[5] = 0;
        }
    }

    /// <summary>
    /// Boussinesq: a point load P pressing down at the origin on an elastic half-space below it, z
    /// up, seen through a block below the load (the load's own point is singular).
    /// </summary>
    sealed class Boussinesq : Exact
    {
        public double P = 1000;
        public Boussinesq() { Name = "Boussinesq"; Box = new BoundingBox(-2, -2, -3.5, 2, 2, -1); }
        public override void Stress(Point3d p, double[] s)
        {
            double x = p.X, y = p.Y, d = -p.Z;    // depth
            double r = Math.Sqrt(x * x + y * y), R = Math.Sqrt(r * r + d * d);
            double R5 = Math.Pow(R, 5);
            double sd = -3 * P * d * d * d / (2 * Math.PI * R5);
            double sr = P / (2 * Math.PI) * (-3 * r * r * d / R5 + (1 - 2 * Nu) / (R * (R + d)));
            double st = P * (1 - 2 * Nu) / (2 * Math.PI) * (d / (R * R * R) - 1 / (R * (R + d)));
            double trd = -3 * P * r * d * d / (2 * Math.PI * R5);
            double c = r > 0 ? x / r : 1, sn = r > 0 ? y / r : 0;
            s[0] = sr * c * c + st * sn * sn;
            s[1] = sr * sn * sn + st * c * c;
            s[2] = sd;
            s[3] = (sr - st) * sn * c;
            // Depth runs down, z up: the shears with z change sign.
            s[4] = -trd * sn;
            s[5] = -trd * c;
        }
    }

    // ---------------------------------------------------------------- meshes

    /// <summary>Nodes, solid elements in the solver's node order, and the boundary faces with their outward normals.</summary>
    sealed class Solid
    {
        public string Name;
        public List<Point3d> V = new List<Point3d>();
        public List<int[]> E = new List<int[]>();
        public List<(int[] face, Vector3d normal)> Boundary = new List<(int[], Vector3d)>();
        public double Edge;
    }

    static int Node(int i, int j, int k, int nx, int ny) => (k * (ny + 1) + j) * (nx + 1) + i;

    static List<Point3d> GridNodes(BoundingBox box, int nx, int ny, int nz)
    {
        var v = new List<Point3d>();
        for (int k = 0; k <= nz; k++)
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                    v.Add(new Point3d(box.Min.X + (box.Max.X - box.Min.X) * i / nx, box.Min.Y + (box.Max.Y - box.Min.Y) * j / ny,
                                      box.Min.Z + (box.Max.Z - box.Min.Z) * k / nz));
        return v;
    }

    /// <summary>A block of bricks; <paramref name="keep"/> leaves cells out to make a notch.</summary>
    static Solid Bricks(BoundingBox box, int nx, int ny, int nz, Func<int, int, int, bool> keep = null)
    {
        var g = new Solid { Name = $"bricks {nx}x{ny}x{nz}", V = GridNodes(box, nx, ny, nz) };
        for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (keep != null && !keep(i, j, k)) continue;
                    g.E.Add(new[]
                    {
                        Node(i, j, k, nx, ny), Node(i + 1, j, k, nx, ny), Node(i + 1, j + 1, k, nx, ny), Node(i, j + 1, k, nx, ny),
                        Node(i, j, k + 1, nx, ny), Node(i + 1, j, k + 1, nx, ny), Node(i + 1, j + 1, k + 1, nx, ny), Node(i, j + 1, k + 1, nx, ny),
                    });
                }
        // The block's own faces, for loads: a brick face on one of the six sides.
        var faces = new[] { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
        foreach (var e in g.E)
            foreach (var f in faces)
            {
                var face = f.Select(n => e[n]).ToArray();
                var normal = OnSide(box, face.Select(n => g.V[n]));
                if (normal.IsValid) g.Boundary.Add((face, normal));
            }
        g.Edge = Math.Min(Math.Min((box.Max.X - box.Min.X) / nx, (box.Max.Y - box.Min.Y) / ny), (box.Max.Z - box.Min.Z) / nz);
        return g;
    }

    /// <summary>
    /// A block of tetrahedra: each cell cut into six round its main diagonal (Kuhn), which cut
    /// shared faces alike. With <paramref name="tenNode"/> each gets a node at the middle of each edge,
    /// shared with its neighbours.
    /// </summary>
    static Solid Tetrahedra(BoundingBox box, int nx, int ny, int nz, bool tenNode)
    {
        var g = new Solid { Name = $"{(tenNode ? "ten node" : "four node")} tetrahedra {nx}x{ny}x{nz} (x6)", V = GridNodes(box, nx, ny, nz) };
        var middle = new Dictionary<(int, int), int>();
        int Mid(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!middle.TryGetValue(key, out int m))
            {
                m = g.V.Count;
                g.V.Add(0.5 * (g.V[a] + g.V[b]));
                middle[key] = m;
            }
            return m;
        }
        var axes = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    foreach (var order in axes)
                    {
                        var at = new[] { i, j, k };
                        var corners = new int[4];
                        corners[0] = Node(at[0], at[1], at[2], nx, ny);
                        for (int s = 0; s < 3; s++)
                        {
                            at[order[s]]++;
                            corners[s + 1] = Node(at[0], at[1], at[2], nx, ny);
                        }
                        // Positive volume, as the solver wants.
                        if ((g.V[corners[1]] - g.V[corners[0]]) * Vector3d.CrossProduct(g.V[corners[2]] - g.V[corners[0]], g.V[corners[3]] - g.V[corners[0]]) < 0)
                            (corners[1], corners[2]) = (corners[2], corners[1]);
                        if (!tenNode)
                        {
                            g.E.Add(corners);
                        }
                        else
                        {
                            var element = new int[10];
                            Array.Copy(corners, element, 4);
                            for (int e = 0; e < Utils.TetrahedronEdges.Length; e++)
                                element[4 + e] = Mid(corners[Utils.TetrahedronEdges[e][0]], corners[Utils.TetrahedronEdges[e][1]]);
                            g.E.Add(element);
                        }
                        for (int f = 0; f < 4; f++)
                        {
                            var tri = Enumerable.Range(0, 4).Where(c => c != f).Select(c => corners[c]).ToArray();
                            var normal = OnSide(box, tri.Select(n => g.V[n]));
                            if (!normal.IsValid) continue;
                            if (!tenNode) { g.Boundary.Add((tri, normal)); continue; }
                            g.Boundary.Add((new[] { tri[0], tri[1], tri[2], Mid(tri[0], tri[1]), Mid(tri[1], tri[2]), Mid(tri[2], tri[0]) }, normal));
                        }
                    }
        g.Edge = Math.Min(Math.Min((box.Max.X - box.Min.X) / nx, (box.Max.Y - box.Min.Y) / ny), (box.Max.Z - box.Min.Z) / nz);
        return g;
    }

    /// <summary>The outward normal of the side of the box all the points lie on, or Unset.</summary>
    static Vector3d OnSide(BoundingBox box, IEnumerable<Point3d> points)
    {
        var p = points.ToList();
        double tol = 1e-9 * box.Diagonal.Length;
        if (p.All(q => Math.Abs(q.X - box.Min.X) < tol)) return -Vector3d.XAxis;
        if (p.All(q => Math.Abs(q.X - box.Max.X) < tol)) return Vector3d.XAxis;
        if (p.All(q => Math.Abs(q.Y - box.Min.Y) < tol)) return -Vector3d.YAxis;
        if (p.All(q => Math.Abs(q.Y - box.Max.Y) < tol)) return Vector3d.YAxis;
        if (p.All(q => Math.Abs(q.Z - box.Min.Z) < tol)) return -Vector3d.ZAxis;
        if (p.All(q => Math.Abs(q.Z - box.Max.Z) < tol)) return Vector3d.ZAxis;
        return Vector3d.Unset;
    }

    static double[] At(Exact f, Point3d p) { var s = new double[6]; f.Stress(p, s); return s; }

    /// <summary>The exact stress at every node of every element, as a ten node tetrahedron gives it.</summary>
    static SolidPrincipalField AtNodes(Solid g, Exact f) =>
        new SolidPrincipalField(g.V, g.E, g.E.Select(e => e.Select(n => At(f, g.V[n])).ToArray()).ToList());

    /// <summary>The exact stress at each element's centre only, as an SSP Brick gives it.</summary>
    static SolidPrincipalField AtCentres(Solid g, Exact f) =>
        new SolidPrincipalField(g.V, g.E, g.E.Select(e =>
        {
            var c = new Point3d(0, 0, 0);
            int corners = e.Length == 10 ? 4 : e.Length;
            for (int k = 0; k < corners; k++) c += g.V[e[k]];
            return new[] { At(f, c / corners) };
        }).ToList());

    // ---------------------------------------------------------------- measuring a line

    /// <summary>The exact trajectory through a seed, in fine steps, stopped at the boundary or where the direction is not unique.</summary>
    static Polyline ExactLine(Exact f, Point3d seed, int family, double h)
    {
        var halves = new List<List<Point3d>>();
        foreach (int sign in new[] { 1, -1 })
        {
            var pts = new List<Point3d> { seed };
            var p = seed;
            var previous = f.Direction(seed, family) * sign;
            Vector3d Along(Point3d q, Vector3d r) { var d = f.Direction(q, family); return d * r < 0 ? -d : d; }
            for (double length = 0; length < 30 * f.Size; length += h)
            {
                var k1 = Along(p, previous);
                var k2 = Along(p + k1 * (h / 2), k1);
                var k3 = Along(p + k2 * (h / 2), k1);
                var k4 = Along(p + k3 * h, k1);
                var d = k1 + 2 * k2 + 2 * k3 + k4; d.Unitize();
                var q = p + d * h;
                if (!f.Inside(q)) break;
                pts.Add(q);
                if (length > 3 * h && q.DistanceTo(seed) < h) break;
                if (f.Weak(q, family)) break;
                previous = q - p; previous.Unitize();
                p = q;
            }
            halves.Add(pts);
        }
        halves[1].Reverse();
        halves[1].RemoveAt(halves[1].Count - 1);
        halves[1].AddRange(halves[0]);
        return new Polyline(halves[1]);
    }

    sealed class Measure { public List<double> Angles = new List<double>(); public double Distance; public double Length; }

    /// <summary>
    /// Angles of the segments outside the weak regions against the exact direction at their middles,
    /// and the largest distance from the exact trajectory over the stretch round the seed that stays
    /// out of them.
    /// </summary>
    static Measure MeasureLine(Exact f, Polyline line, Point3d seed, int family, double h, Func<Point3d, bool> zone = null)
    {
        var m = new Measure { Length = line.Length };
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var mid = 0.5 * (line[i] + line[i + 1]);
            if (!f.Inside(mid) || f.Weak(mid, family) || zone != null && !zone(mid)) continue;
            var seg = line[i + 1] - line[i];
            if (!seg.Unitize()) continue;
            double cos = Math.Min(1, Math.Abs(seg * f.Direction(mid, family)));
            m.Angles.Add(Math.Acos(cos) * 180 / Math.PI);
        }
        var exact = ExactLine(f, seed, family, h);
        int start = line.ClosestIndex(seed);
        foreach (int dir in new[] { 1, -1 })
            for (int i = start; i >= 0 && i < line.Count; i += dir)
            {
                if (f.Weak(line[i], family) || zone != null && !zone(line[i])) break;
                m.Distance = Math.Max(m.Distance, line[i].DistanceTo(exact.ClosestPoint(line[i])));
            }
        return m;
    }

    static double Mean(IEnumerable<double> v) => v.DefaultIfEmpty(double.NaN).Average();
    static double P95(List<double> v) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? double.NaN : s[Math.Min(s.Count - 1, (int)(0.95 * s.Count))]; }

    /// <summary>Single lines through each seed for all three families, checked against the limits given.</summary>
    static void Lines(string name, Exact f, Func<int, List<Polyline>> trace, Point3d[] seeds, double step,
                      double meanLimit, double p95Limit, double distanceLimit, int[] families = null, string picture = null, int[] reportOnly = null,
                      Func<Point3d, bool> zone = null)
    {
        foreach (int family in families ?? new[] { 1, 2, 3 })
        {
            var lines = trace(family);
            var angles = new List<double>();
            double distance = 0;
            int drawn = 0;
            for (int i = 0; i < seeds.Length; i++)
            {
                if (lines[i].Count < 2) continue;
                drawn++;
                var m = MeasureLine(f, lines[i], seeds[i], family, step / 10, zone);
                angles.AddRange(m.Angles);
                distance = Math.Max(distance, m.Distance);
                if (picture != null)
                {
                    Drawn.Add((picture, family, lines[i]));
                    Drawn.Add((picture, 0, ExactLine(f, seeds[i], family, step / 10)));
                }
            }
            double mean = Mean(angles), p95 = P95(angles);
            if (reportOnly != null && reportOnly.Contains(family))
            {
                report.AppendLine(string.Format(Inv, "  [ -- ] {0}, SL{1}: {2} of {3} lines, angle mean {4:F2} p95 {5:F2} deg, distance {6:F2}% of the size - not checked",
                                                name, family, drawn, seeds.Length, mean, p95, 100 * distance / f.Size));
                continue;
            }
            Check(drawn > 0 && mean < meanLimit && p95 < p95Limit && distance < distanceLimit * f.Size,
                  string.Format(Inv, "{0}, SL{1}: {2} of {3} lines, angle mean {4:F2} p95 {5:F2} deg (limits {6}, {7}), distance {8:F4} = {9:F2}% of the size (limit {10}%)",
                                name, family, drawn, seeds.Length, mean, p95, meanLimit, p95Limit, distance, 100 * distance / f.Size, 100 * distanceLimit));
        }
    }

    /// <summary>The bar's SL1 and SL3 lines stay on the cylinder through their seed. A line's two ends are cut along a chord, which runs inside it, and are left out.</summary>
    static void Helices(string name, Func<int, List<Polyline>> trace)
    {
        double drift = 0;
        foreach (int family in new[] { 1, 3 })
            foreach (var (seed, line) in BarSeeds.Zip(trace(family), (s, l) => (s, l)))
            {
                double r0 = Math.Sqrt(seed.X * seed.X + seed.Y * seed.Y);
                for (int i = 1; i + 1 < line.Count; i++)
                    drift = Math.Max(drift, Math.Abs(Math.Sqrt(line[i].X * line[i].X + line[i].Y * line[i].Y) - r0) / r0);
            }
        Check(drift < 0.002, string.Format(Inv, "{0}: SL1 and SL3 are helices, off their seed's radius by at most {1:F3}%", name, 100 * drift));
    }

    static List<Polyline> Trace(SolidPrincipalField field, int family, double step, Point3d[] seeds)
    {
        var tracer = new SolidStressLineTracer(field, family, step);
        return seeds.Select(tracer.Trace).ToList();
    }

    static readonly Point3d[] BarSeeds = { new Point3d(0.5, 0, 1), new Point3d(0, 0.7, 2), new Point3d(-0.4, -0.4, 3), new Point3d(0.3, 0.6, 4.5), new Point3d(0.8, 0.1, 2.5), new Point3d(-0.2, 0.35, 5.2) };
    static readonly Point3d[] BoussinesqSeeds = { new Point3d(0.5, 0, -1.5), new Point3d(1.0, 0.7, -2.0), new Point3d(-0.6, 1.2, -2.5), new Point3d(0.2, -0.9, -1.2), new Point3d(1.5, -1.2, -3.0), new Point3d(-1.1, -0.3, -1.8) };

    // ---------------------------------------------------------------- the eigen solver

    static void Eigen()
    {
        Section("Principal stresses of a tensor (Jacobi)");
        var random = new Random(7);
        double worst = 0;
        bool sorted = true, unit = true;
        var values = new double[3];
        var vectors = new Vector3d[3];
        var cases = new List<double[]>
        {
            new double[] { 1, 1, 2, 0, 0, 0 }, new double[] { 3, 3, 3, 0, 0, 0 }, new double[] { 0, 0, 0, 1, 0, 0 },
            new double[] { 1e6, 1, -1e6, 1e-3, 0, 2 },
        };
        for (int i = 0; i < 2000; i++)
            cases.Add(Enumerable.Range(0, 6).Select(_ => random.NextDouble() * 2 - 1).ToArray());
        foreach (var s in cases)
        {
            SolidPrincipalField.Eigen(s, values, vectors);
            double scale = s.Max(Math.Abs);
            for (int k = 0; k < 3; k++)
            {
                var v = vectors[k];
                var av = new Vector3d(s[0] * v.X + s[3] * v.Y + s[5] * v.Z, s[3] * v.X + s[1] * v.Y + s[4] * v.Z, s[5] * v.X + s[4] * v.Y + s[2] * v.Z);
                worst = Math.Max(worst, (av - values[k] * v).Length / scale);
                unit &= Math.Abs(v.Length - 1) < 1e-12;
            }
            sorted &= values[0] >= values[1] && values[1] >= values[2];
            unit &= Math.Abs(vectors[0] * vectors[1]) < 1e-9 && Math.Abs(vectors[1] * vectors[2]) < 1e-9 && Math.Abs(vectors[0] * vectors[2]) < 1e-9;
        }
        Check(worst < 1e-12 && sorted && unit,
              string.Format(Inv, "{0} tensors, repeated values among them: |S v - s v| at most {1:E1} of the largest entry, values largest first, vectors orthonormal",
                            cases.Count, worst));
    }

    // ---------------------------------------------------------------- the closed forms

    static void ClosedForms()
    {
        Section("Closed forms, fed their exact stresses (single lines, 6 seeds each)");

        // Both closed forms are in equilibrium, by central differences.
        foreach (Exact f in new Exact[] { new TwistedBar(), new Boussinesq(), new Cantilever(), new SupportedBeam() })
        {
            double worst = 0, h = 1e-4 * f.Size;
            var random = new Random(3);
            for (int i = 0; i < 50; i++)
            {
                var p = new Point3d(f.Box.Min.X + random.NextDouble() * (f.Box.Max.X - f.Box.Min.X), f.Box.Min.Y + random.NextDouble() * (f.Box.Max.Y - f.Box.Min.Y),
                                    f.Box.Min.Z + random.NextDouble() * (f.Box.Max.Z - f.Box.Min.Z));
                double[] D(Vector3d dir) { var a = At(f, p + dir * h); var b = At(f, p - dir * h); return a.Zip(b, (x, y) => (x - y) / (2 * h)).ToArray(); }
                var dx = D(Vector3d.XAxis); var dy = D(Vector3d.YAxis); var dz = D(Vector3d.ZAxis);
                var div = new Vector3d(dx[0] + dy[3] + dz[5], dx[3] + dy[1] + dz[4], dx[5] + dy[4] + dz[2]);
                worst = Math.Max(worst, div.Length * f.Size / f.Largest);
            }
            Check(worst < 1e-5, string.Format(Inv, "{0}: in equilibrium, |div S| x size / largest difference at most {1:E1}", f.Name, worst));
        }

        // The bar's lines against helices: each stays on its cylinder.
        var bar = new TwistedBar();
        foreach (var (g, name) in new[] { (Bricks(bar.Box, 8, 8, 24), "bricks"), (Tetrahedra(bar.Box, 6, 6, 18, true), "ten node tetrahedra") })
        {
            var field = AtNodes(g, bar);
            double step = g.Edge / 3;
            Lines($"twisted bar, {name}, stress at the nodes", bar, k => Trace(field, k, step, BarSeeds), BarSeeds, step, 0.5, 1.5, 0.01,
                  picture: name == "bricks" ? "bar" : null);
            Helices($"twisted bar, {name}", k => Trace(field, k, step, BarSeeds));
        }

        // Boussinesq, with the stress at every node and with one per element (as an SSP Brick gives it).
        var b = new Boussinesq();
        var tenNode = Tetrahedra(b.Box, 8, 8, 5, true);
        var bricks = Bricks(b.Box, 16, 16, 10);
        double tetStep = tenNode.Edge / 3, brickStep = bricks.Edge / 3;
        var timer = Stopwatch.StartNew();
        var tetField = AtNodes(tenNode, b);
        double built = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var tetLines = Enumerable.Range(1, 3).Select(k => Trace(tetField, k, tetStep, BoussinesqSeeds)).ToList();
        double traced = timer.Elapsed.TotalMilliseconds;
        report.AppendLine(string.Format(Inv, "  Under Boussinesq's load the two smaller compressions, SL1 and SL2, differ by at most {0:F1}% of the largest principal difference in this block:",
                                        100 * b.LargestGap(1) / b.Largest));
        report.AppendLine("  their directions turn on a difference the mesh cannot resolve, so only SL3 is held to limits here; the twisted bar holds all three.");
        Lines($"Boussinesq, {tenNode.Name}, stress at the nodes", b, k => tetLines[k - 1], BoussinesqSeeds, tetStep, 1.0, 3.0, 0.02, picture: "boussinesq", reportOnly: new[] { 1, 2 });
        Lines($"Boussinesq, {bricks.Name}, one stress per element", b, k => Trace(AtCentres(bricks, b), k, brickStep, BoussinesqSeeds), BoussinesqSeeds, brickStep, 1.5, 4.0, 0.02, reportOnly: new[] { 1, 2 });
        int steps = tetLines.Sum(l => l.Sum(p => Math.Max(0, p.Count - 1)));
        report.AppendLine(string.Format(Inv, "  field of {0} tetrahedra built in {1:F0} ms; 18 lines, {2} steps, traced in {3:F0} ms ({4:F1} us a step, 4 look-ups each)",
                                        tetField.Tetrahedra, built, steps, traced, 1000 * traced / Math.Max(1, steps)));
    }

    // ---------------------------------------------------------------- robustness

    static void Robustness()
    {
        Section("Edges of the problem");

        // A notch: a line along x in its band leaves the solid at the notch and does not jump it.
        var box = new BoundingBox(0, 0, 0, 6, 4, 1);
        var notched = Bricks(box, 6, 4, 1, (i, j, k) => !(i >= 2 && i < 4 && j >= 2));
        var pull = notched.E.Select(e => new[] { new double[] { 1, 0, 0, 0, 0, 0 } }).ToList();
        var field = new SolidPrincipalField(notched.V, notched.E, pull);
        var line = new SolidStressLineTracer(field, 1, 0.1).Trace(new Point3d(0.5, 3, 0.5));
        double from = line.Count > 0 ? Math.Min(line[0].X, line[line.Count - 1].X) : double.NaN;
        double to = line.Count > 0 ? Math.Max(line[0].X, line[line.Count - 1].X) : double.NaN;
        Check(line.Count > 2 && Math.Abs(from) < 1e-6 && Math.Abs(to - 2) < 1e-6,
              string.Format(Inv, "a line beside a notch runs from x = {0:F3} to x = {1:F3}: to the notch's face, not across it", from, to));

        int hint = -1;
        Check(!field.Contains(new Point3d(3, 3, 0.5), ref hint) && new SolidStressLineTracer(field, 1, 0.1).Trace(new Point3d(3, 3, 0.5)).Count == 0,
              "a seed in the notch is outside the solid and draws nothing");

        // A flattened element is left out and counted; a ten node tetrahedron's eight pieces fill it.
        var flat = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0), new Point3d(1, 1, 0) };
        var withFlat = new SolidPrincipalField(flat, new List<int[]> { new[] { 0, 1, 2, 3 } }, new List<double[][]> { new[] { new double[6] } });
        Check(withFlat.Degenerate == 1 && withFlat.Tetrahedra == 0, $"a tetrahedron with no volume is left out and counted ({withFlat.Degenerate})");

        var bar = new TwistedBar();
        var four = Tetrahedra(bar.Box, 6, 6, 18, false);
        var fourField = AtNodes(four, bar);
        double step = four.Edge / 3;
        Lines("twisted bar, four node tetrahedra", bar, k => Trace(fourField, k, step, BarSeeds), BarSeeds, step, 0.5, 1.5, 0.01);
    }

    // ---------------------------------------------------------------- field mode

    static void FieldMode()
    {
        Section("Field mode");
        var bar = new TwistedBar();
        var g = Bricks(bar.Box, 8, 8, 24);
        var field = AtNodes(g, bar);
        foreach (double test in new[] { 0.0, 0.15 })
        {
            var counts = new List<int>();
            bool apart = true;
            var timer = Stopwatch.StartNew();
            foreach (int family in new[] { 1, 2, 3 })
            {
                var lines = new SolidStressLineTracer(field, family, g.Edge / 3, test).TraceField(new[] { new Point3d(0.3, 0.2, 3) }, 0.3);
                counts.Add(lines.Count);
                if (test > 0)
                    for (int i = 1; i < lines.Count; i++)
                        foreach (var p in lines[i])
                            for (int j = 0; j < i && apart; j++)
                                if (lines[j].Any(q => q.DistanceTo(p) < 0.99 * test)) apart = false;
                if (family == 1 && test > 0)
                    foreach (var l in lines) Drawn.Add(("bar field", 1, l));
            }
            Check(counts.All(c => c > 3) && apart,
                  string.Format(Inv, "twisted bar, separation 0.3{0}: {1} lines in {2:F0} ms{3}", test > 0 ? $", Test Dist. {test}" : "",
                                string.Join(" + ", counts), timer.Elapsed.TotalMilliseconds, test > 0 ? ", none closer than Test Dist. to one before it" : ""));
        }
    }

    // ---------------------------------------------------------------- through the component

    static void Component()
    {
        Section("Principal Stress Lines 3D on blocks OpenSees solved under the exact stresses on their faces");
        var bar = new TwistedBar();
        foreach (var g in new[] { Bricks(bar.Box, 8, 8, 24), Tetrahedra(bar.Box, 6, 6, 18, true) })
        {
            var model = ModelUnder(bar, g);
            if (model == null) continue;
            double step = g.Edge / 3;
            var (lines, ms, messages) = RunComponent(model, BarSeeds, step);
            foreach (var m in messages) report.AppendLine("    component: " + m);
            // An SSP Brick gives one stress, at its centre; averaged into the nodes, it is half an
            // element out along the boundary. A ten node tetrahedron gives its stress at its nodes.
            bool bricks = g.E[0].Length == 8;
            Lines($"twisted bar, {g.Name}, OpenSees", bar, k => lines[k - 1], BarSeeds, step, bricks ? 1.0 : 0.5, bricks ? 3.0 : 1.5, 0.01,
                  picture: bricks ? "bar opensees bricks" : "bar opensees tetrahedra");
            Helices($"twisted bar, {g.Name}, OpenSees", k => lines[k - 1]);
            report.AppendLine(string.Format(Inv, "    the component, results read and 18 lines drawn: {0:F0} ms", ms));
        }

        var b = new Boussinesq();
        foreach (var g in new[] { Bricks(b.Box, 16, 16, 10), Tetrahedra(b.Box, 8, 8, 5, true) })
        {
            var model = ModelUnder(b, g);
            if (model == null) continue;
            double step = g.Edge / 3;
            var (lines, ms, messages) = RunComponent(model, BoussinesqSeeds, step);
            foreach (var m in messages) report.AppendLine("    component: " + m);
            Lines($"Boussinesq, {g.Name}, OpenSees", b, k => lines[k - 1], BoussinesqSeeds, step, 2.0, 6.0, 0.03,
                  picture: g.E[0].Length == 8 ? "boussinesq opensees bricks" : "boussinesq opensees tetrahedra", reportOnly: new[] { 1, 2 });
            report.AppendLine(string.Format(Inv, "    the component, results read and 18 lines drawn: {0:F0} ms", ms));

            var outside = RunComponent(model, new[] { new Point3d(5, 5, 5), BoussinesqSeeds[0] }, step);
            // SL3, the vertical compression: the one family the load leaves well defined.
            Check(outside.lines[2][0].Count == 0 && outside.lines[2][1].Count > 2 && outside.messages.Any(m => m.Contains("outside the solid")),
                  "a seed outside the block draws nothing, one inside draws its line, and the component says so");
        }
    }

    // ---------------------------------------------------------------- beams

    static readonly Point3d[] CantileverSeeds = { new Point3d(2, 0, 0), new Point3d(5, 0, 0), new Point3d(3.1, 0.37, 0), new Point3d(5.3, -0.55, 0), new Point3d(1.2, 0.8, 0), new Point3d(7.0, -0.2, 0) };
    static readonly Point3d[] BeamSeeds = { new Point3d(-3.5, 0.3, 0), new Point3d(-1.0, -0.5, 0), new Point3d(0.8, 0.6, 0), new Point3d(2.5, 0.05, 0), new Point3d(4.0, -0.7, 0), new Point3d(-2.2, 0.85, 0) };

    /// <summary>
    /// The cantilever and the simply supported beam, each of SSP Bricks and of Ten Node Tetrahedra,
    /// twice: under the closed form's own stresses on every face, which makes it the answer
    /// everywhere; and held and loaded as a beam is drawn, where it is the answer only away from
    /// the supports and the loads (Saint-Venant), so the lines are measured only there.
    /// </summary>
    static void Beams()
    {
        Section("Beams of SSP Bricks and of Ten Node Tetrahedra, solved by OpenSees, against Timoshenko and Goodier");
        var cantilever = new Cantilever();
        var beam = new SupportedBeam();
        foreach (var (f, seeds, bricks, tetrahedra) in new (Exact, Point3d[], Solid, Solid)[]
        {
            (cantilever, CantileverSeeds, Bricks(cantilever.Box, 32, 8, 2), Tetrahedra(cantilever.Box, 16, 4, 2, true)),
            (beam, BeamSeeds, Bricks(beam.Box, 40, 8, 2), Tetrahedra(beam.Box, 20, 4, 2, true)),
        })
            foreach (var g in new[] { bricks, tetrahedra })
            {
                bool isBrick = g.E[0].Length == 8;
                string kind = isBrick ? "bricks" : "tetrahedra";
                double step = g.Edge / 3;

                var exactModel = ModelUnder(f, g);
                if (exactModel != null)
                {
                    var (lines, _, messages) = RunComponent(exactModel, seeds, step);
                    foreach (var m in messages) report.AppendLine("    component: " + m);
                    Lines($"{f.Name}, {g.Name}, the exact stresses on every face", f, k => lines[k - 1], seeds, step,
                          isBrick ? 1.0 : 0.5, isBrick ? 3.0 : 1.5, 0.01, picture: $"{f.Name} exact {kind}");
                }

                var (model, zone, what) = AsDrawn(f, g);
                if (model != null)
                {
                    var (lines, _, messages) = RunComponent(model, seeds, step);
                    foreach (var m in messages) report.AppendLine("    component: " + m);
                    Lines($"{f.Name}, {g.Name}, {what}, more than a depth from them", f, k => lines[k - 1], seeds, step,
                          1.5, 4.0, 0.02, picture: $"{f.Name} as drawn {kind}", zone: zone);

                    // A field through the whole beam, from one seed in its middle plane.
                    var middle = new Point3d(0.5 * (f.Box.Min.X + f.Box.Max.X) + 0.37, 0.21, 0);
                    var (field, ms, _) = RunComponent(model, new[] { middle }, step, false, 0.2, 0.1);
                    for (int k = 0; k < 3; k++)
                        foreach (var l in field[k]) Drawn.Add(($"{f.Name} field {kind}", k + 1, l));
                    Check(field.All(family => family.Count > 0),
                          string.Format(Inv, "{0}, {1}, {2}: a field of lines 0.2 apart, {3} in {4:F0} ms", f.Name, g.Name, what, string.Join(" + ", field.Select(family => family.Count)), ms));
                }
            }
    }

    /// <summary>
    /// The beam held and loaded as it would be drawn, of steel (Poisson's ratio 0.3): the
    /// cantilever clamped at x = 8 with its load spread evenly over the end at x = 0; the simply
    /// supported beam on the bottom edges of its two ends, under an even load on its top. Returns
    /// the model, where the closed form holds - more than a depth from the supports and the loads -
    /// and what it is.
    /// </summary>
    static (Model, Func<Point3d, bool>, string) AsDrawn(Exact f, Solid g)
    {
        double tol = 1e-6;
        var supports = new List<Support>();
        if (f is Cantilever c)
        {
            foreach (var p in g.V.Where(p => Math.Abs(p.X - c.L) < tol))
                supports.Add(new Support(p, true, true, true, false, false, false));
            var shear = new Vector3d(0, c.P / (2 * c.C), 0);
            var model = ModelOf(g, 0.3, (p, n) => n.X < -0.5 ? shear : Vector3d.Zero, supports);
            return (model, p => p.X >= 2 * c.C && p.X <= c.L - 2 * c.C, "clamped, an even end load");
        }
        var b = (SupportedBeam)f;
        double zMin = g.V.Min(p => p.Z);
        foreach (var p in g.V.Where(p => Math.Abs(p.Y + b.C) < tol))
        {
            if (Math.Abs(p.X + b.Half) < tol)
                supports.Add(new Support(p, true, true, Math.Abs(p.Z - zMin) < tol, false, false, false));
            else if (Math.Abs(p.X - b.Half) < tol)
                supports.Add(new Support(p, false, true, false, false, false, false));
        }
        var load = new Vector3d(0, -b.Q, 0);
        var beamModel = ModelOf(g, 0.3, (p, n) => n.Y > 0.5 ? load : Vector3d.Zero, supports);
        return (beamModel, p => Math.Abs(p.X) <= b.Half - 2 * b.C, "on its bottom edges, an even load on top");
    }

    /// <summary>
    /// A block under the closed form's stresses on its six faces - the tractions, as consistent nodal
    /// forces - held against rigid-body motion only, at three corners. The closed form is then the
    /// answer everywhere inside.
    /// </summary>
    static Model ModelUnder(Exact f, Solid g)
    {
        var o = f.Box.Min;
        var supports = new List<Support>
        {
            new Support(o, true, true, true, false, false, false),
            new Support(new Point3d(f.Box.Max.X, o.Y, o.Z), false, true, true, false, false, false),
            new Support(new Point3d(o.X, f.Box.Max.Y, o.Z), false, false, true, false, false, false),
        };
        return ModelOf(g, f.Nu, (p, n) => Traction(f, p, n), supports);
    }

    static Vector3d Traction(Exact f, Point3d p, Vector3d n)
    {
        var s = At(f, p);
        return new Vector3d(s[0] * n.X + s[3] * n.Y + s[5] * n.Z, s[3] * n.X + s[1] * n.Y + s[4] * n.Z, s[5] * n.X + s[4] * n.Y + s[2] * n.Z);
    }

    /// <summary>
    /// A solid of steel under <paramref name="traction"/> on its faces - a traction at a point of a
    /// face with an outward normal, as consistent nodal forces - and the supports given.
    /// </summary>
    static Model ModelOf(Solid g, double nu, Func<Point3d, Vector3d, Vector3d> traction, List<Support> supports)
    {
        double e = 2.1e8;
        var material = new ElasticIsotropicMaterial("steel", e, e / (2 * (1 + nu)), nu, 0.0);
        var elements = new List<IElement>();
        foreach (var element in g.E)
        {
            var mesh = new Mesh();
            if (element.Length == 8)
            {
                foreach (int n in element) mesh.Vertices.Add(g.V[n]);
                foreach (var q in new[] { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } })
                    mesh.Faces.AddFace(q[0], q[1], q[2], q[3]);
                elements.Add(new SSPbrick(Utils.CleanHexahedron(mesh), material));
            }
            else
            {
                for (int k = 0; k < 4; k++) mesh.Vertices.Add(g.V[element[k]]);
                mesh.Faces.AddFace(0, 2, 1); mesh.Faces.AddFace(0, 1, 3); mesh.Faces.AddFace(1, 2, 3); mesh.Faces.AddFace(2, 0, 3);
                elements.Add(new TenNodeTetrahedron(Utils.CleanTenNodeTetrahedron(mesh), material));
            }
        }

        var forces = new Dictionary<int, Vector3d>();
        void Add(int n, Vector3d v) => forces[n] = forces.TryGetValue(n, out var o) ? o + v : v;
        var gauss = new[] { -Math.Sqrt(0.6), 0, Math.Sqrt(0.6) };
        var gw = new[] { 5.0 / 9, 8.0 / 9, 5.0 / 9 };
        // Dunavant's seven point rule on a triangle, exact to degree five.
        var tri = new (double a, double b, double c, double w)[]
        {
            (1.0 / 3, 1.0 / 3, 1.0 / 3, 0.225),
            (0.059715871789770, 0.470142064105115, 0.470142064105115, 0.132394152788506),
            (0.470142064105115, 0.059715871789770, 0.470142064105115, 0.132394152788506),
            (0.470142064105115, 0.470142064105115, 0.059715871789770, 0.132394152788506),
            (0.797426985353087, 0.101286507323456, 0.101286507323456, 0.125939180544827),
            (0.101286507323456, 0.797426985353087, 0.101286507323456, 0.125939180544827),
            (0.101286507323456, 0.101286507323456, 0.797426985353087, 0.125939180544827),
        };
        foreach (var (face, normal) in g.Boundary)
        {
            var p = face.Select(n => g.V[n]).ToArray();
            if (face.Length == 4)
            {
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        double u = gauss[i], v = gauss[j];
                        var N = new[] { (1 - u) * (1 - v) / 4, (1 + u) * (1 - v) / 4, (1 + u) * (1 + v) / 4, (1 - u) * (1 + v) / 4 };
                        var x = new Point3d(0, 0, 0);
                        for (int k = 0; k < 4; k++) x += N[k] * p[k];
                        var du = (-(1 - v) * (Vector3d)p[0] + (1 - v) * (Vector3d)p[1] + (1 + v) * (Vector3d)p[2] - (1 + v) * (Vector3d)p[3]) / 4;
                        var dv = (-(1 - u) * (Vector3d)p[0] - (1 + u) * (Vector3d)p[1] + (1 + u) * (Vector3d)p[2] + (1 - u) * (Vector3d)p[3]) / 4;
                        double area = Vector3d.CrossProduct(du, dv).Length * gw[i] * gw[j];
                        var t = traction(x, normal);
                        for (int k = 0; k < 4; k++) Add(face[k], t * (N[k] * area));
                    }
            }
            else if (face.Length == 3)
            {
                double area = Vector3d.CrossProduct(p[1] - p[0], p[2] - p[0]).Length / 2;
                foreach (var (a, bb, c, w) in tri)
                {
                    var t = traction(new Point3d(a * p[0] + bb * p[1] + c * p[2]), normal);
                    Add(face[0], t * (a * area * w)); Add(face[1], t * (bb * area * w)); Add(face[2], t * (c * area * w));
                }
            }
            else
            {
                // A six node triangle: corners a, b, c, then the middles of ab, bc and ca.
                double area = Vector3d.CrossProduct(p[1] - p[0], p[2] - p[0]).Length / 2;
                foreach (var (a, bb, c, w) in tri)
                {
                    var t = traction(new Point3d(a * p[0] + bb * p[1] + c * p[2]), normal);
                    var N = new[] { a * (2 * a - 1), bb * (2 * bb - 1), c * (2 * c - 1), 4 * a * bb, 4 * bb * c, 4 * c * a };
                    for (int k = 0; k < 6; k++) Add(face[k], t * (N[k] * area * w));
                }
            }
        }
        var loads = forces.Where(kv => kv.Value.Length > 0)
            .Select(kv => (ILoad)new PointLoad(g.V[kv.Key], kv.Value, Vector3d.Zero, new Alpaca4d.TimeSeries.Linear(1.0))).ToList();

        return Solved(elements, supports, loads, (g.E[0].Length == 8 ? "bricks" : "tet10") + Guid.NewGuid().ToString("N").Substring(0, 6));
    }

    static Model Solved(List<IElement> elements, List<Support> supports, List<ILoad> loads, string name)
    {
        var model = new Model(elements, supports, new List<LoadPattern> { new LoadPattern(PatternType.Plain, new Alpaca4d.TimeSeries.Linear(1.0), loads) },
                              new List<IConstraint>(), new List<IRecorder>()) { Tollerance = 1e-4 };
        model.Assemble();
        var recorder = Recorder.MpcoStatic(Path.Combine(folder, name + ".mpco"));
        model.Recorders = new List<IRecorder> { recorder };
        model.Tcl.Add(recorder.WriteTcl());
        model.Tcl.Add("system UmfPack\nnumberer RCM\nconstraints Transformation\ntest NormDispIncr 1e-10 10\n" +
                      "algorithm Linear\nintegrator LoadControl 1.0\nanalysis Static\nif {[analyze 1] != 0} { puts \"ANALYSIS FAILED\" }\n");
        model.Tcl.Add("wipe");
        model.FileName = Path.Combine(folder, name + ".tcl");
        model.Serialise();
        using (var process = new Process())
        {
            process.StartInfo.FileName = openSees;
            process.StartInfo.Arguments = $"\"{model.FileName}\"";
            process.StartInfo.WorkingDirectory = folder;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            var output = stdout.Result + stderr;
            bool ok = process.ExitCode == 0 && !output.Contains("ANALYSIS FAILED");
            Check(ok, $"OpenSees solves the {name} model ({elements.Count} elements)" + (ok ? "" : ": " + output.Substring(Math.Max(0, output.Length - 600))));
            return ok ? model : null;
        }
    }

    // ---------------------------------------------------------------- running the component

    static IGH_Param In(GH_Component c, string name) =>
        c.Params.Input.FirstOrDefault(p => p.Name == name || p.NickName == name)
        ?? throw new Exception($"{c.Name} has no input {name}");

    static void Set<T>(GH_Component c, string input, params T[] values) where T : class, IGH_Goo
    {
        var param = (GH_PersistentParam<T>)In(c, input);
        param.PersistentData.Clear();
        foreach (var value in values) param.PersistentData.Append(value);
    }

    static (List<Polyline>[] lines, double ms, List<string> messages) RunComponent(Model model, Point3d[] seeds, double step,
                                                                                  bool single = true, double separation = 0, double test = 0)
    {
        var doc = new GH_Document();
        var c = new Alpaca4d.Gh.PrincipalStressLines();
        c.CreateAttributes();
        c.SwitchUnit("PrincipalStressLines3D", false, false);    // Principal Stress Lines' Solids unit
        doc.AddObject(c, false);
        var data = (GH_Structure<IGH_Goo>)In(c, "AlpacaModel").GetType().GetProperty("PersistentData").GetValue(In(c, "AlpacaModel"));
        data.Clear(); data.Append(new GH_ObjectWrapper(model));
        Set(c, "Seed", seeds.Select(s => new GH_Point(s)).ToArray());
        Set(c, "Step Tolerance", new GH_Number(step));
        Set(c, "Single Line", new GH_Boolean(single));
        Set(c, "Separation", new GH_Number(separation));
        Set(c, "Test Dist.", new GH_Number(test));
        doc.Enabled = true;
        var sw = Stopwatch.StartNew();
        doc.NewSolution(true, GH_SolutionMode.Silent);
        double ms = sw.Elapsed.TotalMilliseconds;
        List<Polyline> Read(int i) => c.Params.Output[i].VolatileData.AllData(false)
            .Select(goo => goo is GH_Curve gc && gc.Value != null && gc.Value.TryGetPolyline(out Polyline pl) ? pl : new Polyline()).ToList();
        var messages = c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => "error: " + m)
            .Concat(c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Select(m => "warning: " + m))
            .Concat(c.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Select(m => "remark: " + m)).ToList();
        return (new[] { Read(0), Read(1), Read(2) }, ms, messages);
    }
}
