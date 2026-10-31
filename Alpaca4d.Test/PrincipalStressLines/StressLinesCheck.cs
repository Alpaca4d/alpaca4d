// Principal Stress Lines checked inside a running Rhino 8 against closed-form solutions of elasticity:
// the Timoshenko cantilever, Kirsch's plate with a hole, and Navier's simply supported plate. The
// field and tracer are fed each solution's exact nodal values, then the component itself draws lines
// on models OpenSees solved. A line is measured by the angle between each of its segments and the
// exact principal direction at the segment's middle, and by its distance from the exact trajectory
// through the same seed. run.sh loads this beside the build under test and calls Run by reflection.
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
using Alpaca4d.Section;

public static class StressLinesCheck
{
    static StringBuilder report;
    static int fails;
    static string folder, openSees;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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
        try
        {
            ClosedForms();
            Robustness();
            FieldMode();
            Component();
        }
        catch (Exception e)
        {
            Check(false, "no exception: " + e);
        }
        report.AppendLine();
        report.AppendLine(fails == 0 ? "all principal stress line checks passed" : $"{fails} principal stress line check(s) failed");
        return report.ToString();
    }

    // ---------------------------------------------------------------- closed forms

    /// <summary>
    /// A closed-form field: the tensor whose principal directions the lines must follow, and what a
    /// node is given - its translation, or for bending its rotation (rx = dw/dy, ry = -dw/dx, z up).
    /// </summary>
    abstract class Exact
    {
        public bool Bending;
        public double Nu = 0.3, E = 1000;
        public abstract void Tensor(double x, double y, out double sx, out double sy, out double txy);
        public abstract Vector3d Nodal(double x, double y);
        public abstract bool Inside(double x, double y);
        public abstract BoundingBox Box { get; }
        double? _largest;

        /// <summary>Half the principal difference, against the largest anywhere: where it is under 2% the direction is not unique.</summary>
        public bool Weak(double x, double y)
        {
            if (_largest == null)
            {
                double r = 0;
                for (int i = 0; i <= 200; i++)
                    for (int j = 0; j <= 200; j++)
                    {
                        double px = Box.Min.X + (Box.Max.X - Box.Min.X) * i / 200, py = Box.Min.Y + (Box.Max.Y - Box.Min.Y) * j / 200;
                        if (Inside(px, py)) r = Math.Max(r, Radius(px, py));
                    }
                _largest = r;
            }
            return Radius(x, y) < 0.02 * _largest.Value;
        }

        double Radius(double x, double y)
        {
            Tensor(x, y, out var sx, out var sy, out var t);
            return Math.Sqrt(0.25 * (sx - sy) * (sx - sy) + t * t);
        }

        public Vector3d Direction(double x, double y, int family)
        {
            Tensor(x, y, out var sx, out var sy, out var t);
            double angle = 0.5 * Math.Atan2(2 * t, sx - sy) + (family == 2 ? Math.PI / 2 : 0);
            return new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
        }
    }

    /// <summary>Timoshenko and Goodier's cantilever, 8 x 2: load P at x = 0, the other end at x = 8.</summary>
    sealed class Cantilever : Exact
    {
        public double L = 8, c = 1, P = 1;
        double I => 2 * c * c * c / 3;
        double G => E / (2 * (1 + Nu));
        public override void Tensor(double x, double y, out double sx, out double sy, out double txy)
        {
            sx = -P * x * y / I; sy = 0; txy = -P / (2 * I) * (c * c - y * y);
        }
        public override Vector3d Nodal(double x, double y) => new Vector3d(
            -P * x * x * y / (2 * E * I) - Nu * P * y * y * y / (6 * E * I) + P * y * y * y / (6 * I * G),
            Nu * P * x * y * y / (2 * E * I) + P * x * x * x / (6 * E * I) - P * c * c * x / (2 * I * G), 0);
        public override bool Inside(double x, double y) => x >= -1e-12 && x <= L + 1e-12 && Math.Abs(y) <= c + 1e-12;
        public override BoundingBox Box => new BoundingBox(0, -c, 0, L, c, 0);
    }

    /// <summary>Kirsch: a hole of radius 1 in an infinite plate pulled along x, seen through a 10 x 10 window.</summary>
    sealed class Kirsch : Exact
    {
        public override void Tensor(double x, double y, out double sx, out double sy, out double txy)
        {
            double r2 = x * x + y * y, th = Math.Atan2(y, x), a2 = 1 / r2, a4 = a2 * a2, c2 = Math.Cos(2 * th), s2 = Math.Sin(2 * th);
            double srr = 0.5 * (1 - a2) + 0.5 * (1 - 4 * a2 + 3 * a4) * c2;
            double stt = 0.5 * (1 + a2) - 0.5 * (1 + 3 * a4) * c2;
            double srt = -0.5 * (1 + 2 * a2 - 3 * a4) * s2;
            double c = Math.Cos(th), s = Math.Sin(th);
            sx = srr * c * c + stt * s * s - 2 * srt * s * c;
            sy = srr * s * s + stt * c * c + 2 * srt * s * c;
            txy = (srr - stt) * s * c + srt * (c * c - s * s);
        }
        public override Vector3d Nodal(double x, double y)
        {
            double mu = E / (2 * (1 + Nu)), kappa = (3 - Nu) / (1 + Nu);
            double r = Math.Sqrt(x * x + y * y), th = Math.Atan2(y, x), c2 = Math.Cos(2 * th), s2 = Math.Sin(2 * th), r3 = 1 / (r * r * r);
            double ur = 1 / (4 * mu) * (r * ((kappa - 1) / 2 + c2) + 1 / r * (1 + (1 + kappa) * c2) - r3 * c2);
            double ut = 1 / (4 * mu) * ((1 - kappa) / r - r - r3) * s2;
            return new Vector3d(ur * Math.Cos(th) - ut * Math.Sin(th), ur * Math.Sin(th) + ut * Math.Cos(th), 0);
        }
        public override bool Inside(double x, double y) => x * x + y * y >= 1 - 1e-9 && Math.Abs(x) <= 5 + 1e-12 && Math.Abs(y) <= 5 + 1e-12;
        public override BoundingBox Box => new BoundingBox(-5, -5, 0, 5, 5, 0);
    }

    /// <summary>
    /// Navier: a simply supported square plate, side A, under a uniform load downwards, z up. The
    /// tensor is the moment, positive where the face the normal (+z) points to is stretched.
    /// </summary>
    sealed class Navier : Exact
    {
        public double A = 10;
        public Navier() { Bending = true; Nu = 0.2; }
        void W(double x, double y, out double wx, out double wy, out double wxx, out double wyy, out double wxy)
        {
            wx = wy = wxx = wyy = wxy = 0;
            for (int m = 1; m <= 59; m += 2)
            {
                double al = m * Math.PI / A, sa = Math.Sin(al * x), ca = Math.Cos(al * x);
                for (int n = 1; n <= 59; n += 2)
                {
                    double be = n * Math.PI / A, sb = Math.Sin(be * y), cb = Math.Cos(be * y);
                    double C = 1.0 / (m * n * Math.Pow(m * m + n * n, 2));   // w (up) = -C sin sin, scale left out
                    wx -= C * al * ca * sb; wy -= C * be * sa * cb;
                    wxx += C * al * al * sa * sb; wyy += C * be * be * sa * sb; wxy -= C * al * be * ca * cb;
                }
            }
        }
        public override void Tensor(double x, double y, out double sx, out double sy, out double txy)
        {
            W(x, y, out _, out _, out var wxx, out var wyy, out var wxy);
            sx = -wxx - Nu * wyy; sy = -wyy - Nu * wxx; txy = -(1 - Nu) * wxy;
        }
        public override Vector3d Nodal(double x, double y)
        {
            W(x, y, out var wx, out var wy, out _, out _, out _);
            return new Vector3d(wy, -wx, 0);
        }
        public override bool Inside(double x, double y) => x >= -1e-12 && x <= A + 1e-12 && y >= -1e-12 && y <= A + 1e-12;
        public override BoundingBox Box => new BoundingBox(0, 0, 0, A, A, 0);
    }

    /// <summary>Nodes and faces of a structured grid; faces anticlockwise seen from +z unless turned.</summary>
    sealed class Grid
    {
        public List<Point3d> V = new List<Point3d>();
        public List<int[]> F = new List<int[]>();

        public Grid(int nx, int ny, bool periodic, Func<double, double, Point3d> map, bool triangles = false, Func<int, int, bool> turned = null)
        {
            int cols = periodic ? nx : nx + 1;
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i < cols; i++)
                    V.Add(map((double)i / nx, (double)j / ny));
            int Id(int i, int j) => j * cols + (periodic ? i % nx : i);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = Id(i, j), b = Id(i + 1, j), c = Id(i + 1, j + 1), d = Id(i, j + 1);
                    bool turn = turned != null && turned(i, j);
                    var faces = triangles
                        ? ((i + j) % 2 == 0 ? new[] { new[] { a, b, c }, new[] { a, c, d } } : new[] { new[] { a, b, d }, new[] { b, c, d } })
                        : new[] { new[] { a, b, c, d } };
                    foreach (var f in faces) F.Add(turn ? f.Reverse().ToArray() : f);
                }
        }
    }

    static Grid CantileverGrid(int nx, int ny, bool triangles = false) => new Grid(nx, ny, false, (u, v) => new Point3d(8 * u, -1 + 2 * v, 0), triangles);

    // Round the hole and out to the 10 x 10 square, finer towards the hole; clockwise in u so faces face +z.
    static Grid KirschGrid() => new Grid(64, 20, true, (u, v) =>
    {
        double th = -2 * Math.PI * u, c = Math.Cos(th), s = Math.Sin(th), t = 5 / Math.Max(Math.Abs(c), Math.Abs(s)), w = Math.Pow(v, 1.5);
        return new Point3d(c + (t * c - c) * w, s + (t * s - s) * w, 0);
    });

    static Grid PlateGrid(int n = 20, bool triangles = false, Func<int, int, bool> turned = null, double side = 10) =>
        new Grid(n, n, false, (u, v) => new Point3d(side * u, side * v, 0), triangles, turned);

    static PrincipalField FieldOf(Grid g, Exact f) => new PrincipalField(g.V, g.F, g.V.Select(p => f.Nodal(p.X, p.Y)).ToList(), f.Bending);

    // ---------------------------------------------------------------- measuring a line

    /// <summary>The exact trajectory through a seed, in fine steps, stopped at the boundary or where the direction is not unique.</summary>
    static Polyline ExactLine(Exact f, Point3d seed, int family, double h)
    {
        List<Point3d> Half(int sign)
        {
            var pts = new List<Point3d> { seed };
            var p = seed; var prev = f.Direction(p.X, p.Y, family) * sign;
            Vector3d At(Point3d q, Vector3d r) { var d = f.Direction(q.X, q.Y, family); return d * r < 0 ? -d : d; }
            for (int i = 0; i < 200000; i++)
            {
                var k1 = At(p, prev); var k2 = At(p + k1 * (h / 2), k1); var k3 = At(p + k2 * (h / 2), k1); var k4 = At(p + k3 * h, k1);
                var q = p + (k1 + 2 * k2 + 2 * k3 + k4) / 6.0 * h;
                if (!f.Inside(q.X, q.Y))
                {
                    double lo = 0, hi = 1;
                    for (int it = 0; it < 50; it++) { double mid = (lo + hi) / 2; var m = p + (q - p) * mid; if (f.Inside(m.X, m.Y)) lo = mid; else hi = mid; }
                    pts.Add(p + (q - p) * lo);
                    break;
                }
                prev = q - p; prev.Unitize(); pts.Add(q); p = q;
                if (f.Weak(p.X, p.Y)) break;
            }
            return pts;
        }
        var a = Half(1); var b = Half(-1);
        b.Reverse(); b.RemoveAt(b.Count - 1); b.AddRange(a);
        return new Polyline(b);
    }

    sealed class Measure { public List<double> Angles = new List<double>(); public double Distance; public bool TurnsBack; }

    /// <summary>
    /// Angles of the segments outside the weak regions against the exact direction, and the largest
    /// distance from the exact trajectory over the stretch round the seed that stays out of them.
    /// </summary>
    static Measure MeasureLine(Exact f, Polyline line, Point3d seed, int family, double step)
    {
        var m = new Measure();
        if (line.Count < 2) return m;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var seg = line[i + 1] - line[i]; double l = seg.Length; if (l < 1e-9) continue;
            if (i > 0) { var before = line[i] - line[i - 1]; if (before.Length > 1e-9 && Vector3d.VectorAngle(before, seg) > Math.PI / 2) m.TurnsBack = true; }
            var mid = line[i] + seg / 2;
            if (i == 0 || i == line.Count - 2 || !f.Inside(mid.X, mid.Y) || f.Weak(mid.X, mid.Y)) continue;
            m.Angles.Add(Math.Acos(Math.Min(1, Math.Abs(seg * f.Direction(mid.X, mid.Y, family)) / l)) * 180 / Math.PI);
        }
        int at = 0; for (int i = 1; i < line.Count; i++) if (line[i].DistanceTo(seed) < line[at].DistanceTo(seed)) at = i;
        int lo = at, hi = at;
        while (lo > 0 && !f.Weak(line[lo - 1].X, line[lo - 1].Y)) lo--;
        while (hi < line.Count - 1 && !f.Weak(line[hi + 1].X, line[hi + 1].Y)) hi++;
        var exact = ExactLine(f, seed, family, step / 20);
        for (int i = lo; i <= hi; i++) m.Distance = Math.Max(m.Distance, exact.ClosestPoint(line[i]).DistanceTo(line[i]));
        return m;
    }

    static double Mean(IEnumerable<double> v) => v.DefaultIfEmpty(double.NaN).Average();
    static double P95(List<double> v) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? double.NaN : s[Math.Min(s.Count - 1, (int)(0.95 * s.Count))]; }

    /// <summary>Single lines through each seed for both families, checked against the limits given.</summary>
    static void Lines(string name, Exact f, Func<int, List<Polyline>> trace, Point3d[] seeds, double step, double distance,
                      double mean = 1.0, double p95 = 3.0, int[] families = null)
    {
        families = families ?? new[] { 1, 2 };
        for (int output = 1; output <= 2; output++)
        {
            int family = families[output - 1];
            var lines = trace(output);
            var measures = seeds.Select((s, i) => MeasureLine(f, lines[i], s, family, step)).ToList();
            var angles = measures.SelectMany(m => m.Angles).ToList();
            double worst = measures.Max(m => m.Distance);
            Check(Mean(angles) < mean && P95(angles) < p95 && worst < distance && !measures.Any(m => m.TurnsBack),
                string.Format(Inv, "{0}, SL{1} along direction {2}: angle mean {3:F2}, p95 {4:F2} deg (limits {5}, {6}); off the exact line by at most {7:F4} (limit {8}); none turns back",
                    name, output, family, Mean(angles), P95(angles), mean, p95, worst, distance));
        }
    }

    static List<Polyline> Trace(PrincipalField field, int family, double step, Point3d[] seeds)
    {
        var tracer = new StressLineTracer(field, family, step);
        return seeds.Select(tracer.Trace).ToList();
    }

    static readonly Point3d[] CantSeeds = { new Point3d(2, 0, 0), new Point3d(5, 0, 0), new Point3d(3.1, 0.37, 0), new Point3d(5.3, -0.55, 0), new Point3d(1.2, 0.8, 0), new Point3d(7.0, -0.2, 0) };
    static readonly Point3d[] KirschSeeds = { new Point3d(0.05, 1.3, 0), new Point3d(1.5, 1.5, 0), new Point3d(3.0, 0.5, 0), new Point3d(-2.0, 3.0, 0), new Point3d(0.3, 4.0, 0), new Point3d(-1.2, -0.4, 0) };
    static readonly Point3d[] PlateSeeds = { new Point3d(2.5, 2.2, 0), new Point3d(1.0, 4.0, 0), new Point3d(3.0, 7.0, 0), new Point3d(7.5, 1.5, 0), new Point3d(4.0, 4.6, 0), new Point3d(6.3, 8.9, 0) };

    // A third of a 20 x 20 grid's faces, scattered: few enough that the rest decide which way is up.
    static bool Turned(int i, int j) => (i + j) % 3 == 0;
    static readonly int TurnedCount = Enumerable.Range(0, 400).Count(k => Turned(k % 20, k / 20));

    static void ClosedForms()
    {
        Section("Closed forms, fed their exact nodal values (single lines, 6 seeds each)");
        var cant = new Cantilever(); var kirsch = new Kirsch(); var navier = new Navier();

        // Off the exact line by no more than 5% of the beam's depth, the hole's diameter or the plate's tenth.
        var q = FieldOf(CantileverGrid(32, 8), cant);
        Lines("cantilever, 32 x 8 quads", cant, k => Trace(q, k, 0.1, CantSeeds), CantSeeds, 0.1, 0.1);
        var t = FieldOf(CantileverGrid(32, 8, true), cant);
        Lines("cantilever, 32 x 8 x 2 triangles", cant, k => Trace(t, k, 0.1, CantSeeds), CantSeeds, 0.1, 0.1);
        var k4 = FieldOf(KirschGrid(), kirsch);
        Lines("plate with a hole, 64 x 20 quads", kirsch, k => Trace(k4, k, 0.1, KirschSeeds), KirschSeeds, 0.1, 0.1, 0.5, 1.5);
        var n4 = FieldOf(PlateGrid(), navier);
        Lines("simply supported plate, bending, 20 x 20 quads", navier, k => Trace(n4, k, 0.2, PlateSeeds), PlateSeeds, 0.2, 0.5);
        var n3 = FieldOf(PlateGrid(triangles: true), navier);
        Lines("simply supported plate, bending, 20 x 20 x 2 triangles", navier, k => Trace(n3, k, 0.2, PlateSeeds), PlateSeeds, 0.2, 0.5);

        // Bending is read against the faces' normal; faces facing the other way from their
        // neighbours are turned to agree, and a mesh facing down throughout is read from below.
        var mixed = FieldOf(PlateGrid(turned: Turned), navier);
        Check(mixed.Flipped == TurnedCount, $"a third of the faces turned over: {mixed.Flipped} of 400 read the other way up (expected {TurnedCount})");
        Lines("the same, a third of the faces turned over", navier, k => Trace(mixed, k, 0.2, PlateSeeds), PlateSeeds, 0.2, 0.5);
        var down = FieldOf(PlateGrid(turned: (i, j) => true), navier);
        Check(down.Flipped == 0, $"every face turned over: none read the other way up ({down.Flipped})");
        Lines("the same, every face turned over (read from below)", navier, k => Trace(down, k, 0.2, PlateSeeds), PlateSeeds, 0.2, 0.5, families: new[] { 2, 1 });
    }

    // ---------------------------------------------------------------- robustness

    static void Robustness()
    {
        Section("What used to stop the component");

        // A closed box of shells has no edge at all.
        var box = new List<Point3d>(); var faces = new List<int[]>();
        var index = new Dictionary<(double, double, double), int>();
        int At(Point3d p) { var key = (Math.Round(p.X, 9), Math.Round(p.Y, 9), Math.Round(p.Z, 9)); if (!index.TryGetValue(key, out int i)) { i = box.Count; index[key] = i; box.Add(p); } return i; }
        var sides = new (Point3d o, Vector3d u, Vector3d v)[]
        {
            (new Point3d(-1, -1, -1), new Vector3d(0, 2, 0), new Vector3d(2, 0, 0)), (new Point3d(-1, -1, 1), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0)),
            (new Point3d(-1, -1, -1), new Vector3d(2, 0, 0), new Vector3d(0, 0, 2)), (new Point3d(-1, 1, -1), new Vector3d(0, 0, 2), new Vector3d(2, 0, 0)),
            (new Point3d(-1, -1, -1), new Vector3d(0, 0, 2), new Vector3d(0, 2, 0)), (new Point3d(1, -1, -1), new Vector3d(0, 2, 0), new Vector3d(0, 0, 2)),
        };
        foreach (var s in sides)
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    faces.Add(new[] { At(s.o + s.u * (i / 4.0) + s.v * (j / 4.0)), At(s.o + s.u * ((i + 1) / 4.0) + s.v * (j / 4.0)),
                                      At(s.o + s.u * ((i + 1) / 4.0) + s.v * ((j + 1) / 4.0)), At(s.o + s.u * (i / 4.0) + s.v * ((j + 1) / 4.0)) });
        var boxField = new PrincipalField(box, faces, box.Select(p => new Vector3d(p.X * p.Y, p.Y * p.Z + 0.3 * p.X, p.Z * p.X) * 1e-3).ToList(), false);
        var sw = Stopwatch.StartNew();
        var round = new StressLineTracer(boxField, 1, 0.05).Trace(new Point3d(0.3, 0.2, 1));
        Check(round.Count > 10 && sw.ElapsedMilliseconds < 2000, $"a closed box of shells: a line of {round.Count} points, {round.Length:F2} long, in {sw.ElapsedMilliseconds} ms");

        // Faces with no area are left out and counted; the rest still make lines.
        var cant = new Cantilever();
        var g = CantileverGrid(32, 8);
        g.F.Add(new[] { 0, 1, 1, 0 }); g.F.Add(new[] { 0, 1, 2 });
        var flat = FieldOf(g, cant);
        var through = new StressLineTracer(flat, 1, 0.1).Trace(new Point3d(3.1, 0.37, 0));
        Check(flat.Degenerate == 2 && through.Count > 10, $"two faces without area: {flat.Degenerate} left out, and a line of {through.Count} points drawn");

        // The same plate drawn in millimetres, run at the step the component picks by itself.
        var navier = new Navier { A = 10000 };
        var mm = FieldOf(PlateGrid(side: 10000), navier);
        double step = mm.EdgeLength / 3;
        sw.Restart();
        var line = new StressLineTracer(mm, 1, step).Trace(new Point3d(7500, 1500, 0));
        Check(Math.Abs(step - 500.0 / 3) < 1e-6 && line.Length > 10000 && sw.ElapsedMilliseconds < 1000,
            $"a plate in millimetres: a step of {step:F1} mm from the mesh, a line {line.Length:F0} mm long (it crosses the 10 m plate) in {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------------- field mode

    static void FieldMode()
    {
        Section("Field mode");
        var navier = new Navier();
        var field = FieldOf(PlateGrid(), navier);
        foreach (int family in new[] { 1, 2 })
        {
            var sw = Stopwatch.StartNew();
            var lines = new StressLineTracer(field, family, 0.2).TraceField(new[] { new Point3d(2.6, 3.1, 0) }, 0.8);
            long ms = sw.ElapsedMilliseconds;
            var angles = lines.SelectMany(l => MeasureAngles(navier, l, family)).ToList();
            bool back = lines.Any(TurnsBack);
            Check(lines.Count > 15 && Mean(angles) < 1 && !back && ms < 2000,
                string.Format(Inv, "simply supported plate, bending, SL{0}: {1} lines in {2} ms, angle mean {3:F2} deg, none turns back", family, lines.Count, ms, Mean(angles)));

            // With Test Dist., a line stops before it comes that close to one drawn earlier.
            var spaced = new StressLineTracer(field, family, 0.2, 0.4).TraceField(new[] { new Point3d(2.6, 3.1, 0) }, 0.8);
            double nearest = double.MaxValue;
            for (int j = 1; j < spaced.Count; j++)
                for (int k = 1; k + 1 < spaced[j].Count; k++)
                    for (int i = 0; i < j; i++)
                        nearest = Math.Min(nearest, spaced[i].ClosestPoint(spaced[j][k]).DistanceTo(spaced[j][k]));
            Check(nearest > 0.4 * 0.95, string.Format(Inv, "the same with Test Dist. 0.4: {0} lines, no line comes closer than {1:F3} to one before it", spaced.Count, nearest));
        }
    }

    static bool TurnsBack(Polyline line)
    {
        for (int i = 1; i + 1 < line.Count; i++)
        {
            var before = line[i] - line[i - 1]; var after = line[i + 1] - line[i];
            if (before.Length > 1e-9 && after.Length > 1e-9 && Vector3d.VectorAngle(before, after) > Math.PI / 2) return true;
        }
        return false;
    }

    static IEnumerable<double> MeasureAngles(Exact f, Polyline line, int family)
    {
        for (int i = 1; i + 2 < line.Count; i++)
        {
            var seg = line[i + 1] - line[i]; double l = seg.Length; if (l < 1e-9) continue;
            var mid = line[i] + seg / 2;
            if (!f.Inside(mid.X, mid.Y) || f.Weak(mid.X, mid.Y)) continue;
            yield return Math.Acos(Math.Min(1, Math.Abs(seg * f.Direction(mid.X, mid.Y, family)) / l)) * 180 / Math.PI;
        }
    }

    // ---------------------------------------------------------------- through the component

    static void Component()
    {
        Section("Principal Stress Lines on models OpenSees solved (ASD Shell Q4)");

        // The cantilever held only against rigid-body motion and loaded by the exact tractions at both
        // ends, so that its exact answer is the closed form.
        var cant = new Cantilever { E = 2.1e8, P = 100 };
        var model = CantileverModel(cant, CantileverGrid(32, 8));
        if (model != null)
        {
            var (s1, s2, ms, messages) = RunComponent(model, CantSeeds, 0.1, true, false);
            Check(messages.Count == 0, "no messages: " + string.Join("; ", messages));
            Lines("cantilever, membrane", cant, k => k == 1 ? s1 : s2, CantSeeds, 0.1, 0.1);
        }

        // A simply supported plate, w and the rotation along each edge held, one node lifted 5 cm so
        // that the four quads round it are warped.
        var navier = new Navier();
        var g = PlateGrid();
        int lifted = g.V.FindIndex(p => p.DistanceTo(new Point3d(5, 5, 0)) < 1e-9);
        g.V[lifted] = new Point3d(5, 5, 0.05);
        var plate = PlateModel(navier, g);
        if (plate != null)
        {
            // Lifted, it is a shallow shell rather than Navier's plate, so this asks only for lines:
            // a face warped past 0.01 used to blank the whole output.
            var (s1, s2, ms, messages) = RunComponent(plate, PlateSeeds, 0.2, true, true);
            Check(messages.Count == 0 && s1.Concat(s2).All(l => l.Count > 10),
                $"plate with warped quads: a line through every seed ({s1.Count(l => l.Count > 10)} + {s2.Count(l => l.Count > 10)} of {PlateSeeds.Length} + {PlateSeeds.Length}), no messages " + string.Join("; ", messages));

            var (f1, f2, fms, fmessages) = RunComponent(plate, new[] { new Point3d(2.6, 3.1, 0) }, 0.0, false, true);
            Check(f1.Count > 10 && f2.Count > 10 && fms < 3000,
                $"field mode with Step Tolerance and Separation left at 0: {f1.Count} + {f2.Count} lines in {fms:F0} ms");
        }

        // A third of the shells turned over: the component reads them the other way up and says so.
        var mixed = PlateModel(navier, PlateGrid(turned: Turned));
        if (mixed != null)
        {
            var (s1, s2, ms, messages) = RunComponent(mixed, PlateSeeds, 0.2, true, true);
            Check(messages.Count == 1 && messages[0].Contains($"{TurnedCount} shell element(s) face the other way"), "a third of the shells turned over: " + string.Join("; ", messages));
            Lines("a third of the shells turned over, bending", navier, k => k == 1 ? s1 : s2, PlateSeeds, 0.2, 0.5);
        }
    }

    static Model CantileverModel(Cantilever f, Grid g)
    {
        double t = 0.1;
        var section = new PlateFiberSection("plate", t, new ElasticIsotropicMaterial("steel", f.E, f.E / (2 * (1 + f.Nu)), f.Nu, 0.0));
        var forces = new Dictionary<int, Vector3d>();
        void Add(int id, Vector3d v) => forces[id] = forces.TryGetValue(id, out var o) ? o + v : v;
        void Edge(double x, Func<double, Vector3d> traction)
        {
            // Consistent nodal forces of the traction along an end; Simpson's rule is exact for these.
            var ids = Enumerable.Range(0, g.V.Count).Where(i => Math.Abs(g.V[i].X - x) < 1e-9).OrderBy(i => g.V[i].Y).ToList();
            for (int k = 0; k + 1 < ids.Count; k++)
            {
                double y0 = g.V[ids[k]].Y, y1 = g.V[ids[k + 1]].Y, l = y1 - y0;
                var t0 = traction(y0) * t; var tm = traction((y0 + y1) / 2) * t; var t1 = traction(y1) * t;
                Add(ids[k], l / 6 * (t0 + 2 * tm)); Add(ids[k + 1], l / 6 * (2 * tm + t1));
            }
        }
        double I = 2 * f.c * f.c * f.c / 3;
        Edge(0, y => new Vector3d(0, f.P / (2 * I) * (f.c * f.c - y * y), 0));
        Edge(f.L, y => new Vector3d(-f.P * f.L * y / I, -f.P / (2 * I) * (f.c * f.c - y * y), 0));
        var loads = forces.Select(kv => (ILoad)new PointLoad(g.V[kv.Key], kv.Value, Vector3d.Zero, new Alpaca4d.TimeSeries.Linear(1.0))).ToList();
        var supports = g.V.Select(p =>
        {
            bool pin = Math.Abs(p.X - f.L) < 1e-9 && Math.Abs(p.Y) < 1e-9, roller = Math.Abs(p.X - f.L) < 1e-9 && Math.Abs(p.Y - f.c) < 1e-9;
            return new Support(p, pin || roller, pin, true, true, true, false);
        }).ToList();
        return Solved(Shells(g, section), supports, loads, "cantilever");
    }

    static Model PlateModel(Navier f, Grid g)
    {
        var section = new PlateFiberSection("slab", 0.1, new ElasticIsotropicMaterial("concrete", 30e6, 30e6 / (2 * (1 + f.Nu)), f.Nu, 0.0));
        double h = f.A / 20;
        var supports = new List<Support>(); var loads = new List<ILoad>();
        foreach (var p in g.V)
        {
            bool ex = p.X < 1e-9 || p.X > f.A - 1e-9, ey = p.Y < 1e-9 || p.Y > f.A - 1e-9;
            supports.Add(new Support(p, true, true, ex || ey, ex, ey, true));
            if (!ex && !ey) loads.Add(new PointLoad(p, new Vector3d(0, 0, -10 * h * h), Vector3d.Zero, new Alpaca4d.TimeSeries.Linear(1.0)));
        }
        return Solved(Shells(g, section), supports, loads, "plate" + Guid.NewGuid().ToString("N").Substring(0, 6));
    }

    static List<IElement> Shells(Grid g, IMultiDimensionSection section) => g.F.Select(face =>
    {
        var mesh = new Mesh();
        foreach (var k in face) mesh.Vertices.Add(g.V[k]);
        if (face.Length == 4) mesh.Faces.AddFace(0, 1, 2, 3); else mesh.Faces.AddFace(0, 1, 2);
        return (IElement)new ASDShellQ4(mesh, section);
    }).ToList();

    static Model Solved(List<IElement> elements, List<Support> supports, List<ILoad> loads, string name)
    {
        var model = new Model(elements, supports, new List<LoadPattern> { new LoadPattern(PatternType.Plain, new Alpaca4d.TimeSeries.Linear(1.0), loads) },
                              new List<IConstraint>(), new List<IRecorder>()) { Tollerance = 1e-4 };
        model.Assemble();
        var recorder = Recorder.MpcoStatic(Path.Combine(folder, name + ".mpco"));
        model.Recorders = new List<IRecorder> { recorder };
        model.Tcl.Add(recorder.WriteTcl());
        model.Tcl.Add("system BandGeneral\nnumberer RCM\nconstraints Transformation\ntest NormDispIncr 1e-10 10\n" +
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
            bool ok = process.ExitCode == 0 && !(stdout.Result + stderr).Contains("ANALYSIS FAILED");
            Check(ok, $"OpenSees solves the {name} model");
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

    static (List<Polyline>, List<Polyline>, double, List<string>) RunComponent(Model model, Point3d[] seeds, double step, bool single, bool bending)
    {
        var doc = new GH_Document();
        var c = new Alpaca4d.Gh.PrincipalStressLines();
        c.CreateAttributes();
        c.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) })?.Invoke(c, new object[] { "PrincipalStressLines", false, false });
        doc.AddObject(c, false);
        var data = (GH_Structure<IGH_Goo>)In(c, "AlpacaModel").GetType().GetProperty("PersistentData").GetValue(In(c, "AlpacaModel"));
        data.Clear(); data.Append(new GH_ObjectWrapper(model));
        Set(c, "Seed", seeds.Select(s => new GH_Point(s)).ToArray());
        Set(c, "Step Tolerance", new GH_Number(step));
        Set(c, "Single Line", new GH_Boolean(single));
        Set(c, "Bending Stress Lines", new GH_Boolean(bending));
        doc.Enabled = true;
        var sw = Stopwatch.StartNew();
        doc.NewSolution(true, GH_SolutionMode.Silent);
        double ms = sw.Elapsed.TotalMilliseconds;
        List<Polyline> Read(int i) => c.Params.Output[i].VolatileData.AllData(false)
            .Select(goo => goo is GH_Curve gc && gc.Value != null && gc.Value.TryGetPolyline(out Polyline pl) ? pl : new Polyline()).ToList();
        var messages = c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(c.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
            .Concat(c.RuntimeMessages(GH_RuntimeMessageLevel.Remark)).ToList();
        return (Read(0), Read(1), ms, messages);
    }
}
