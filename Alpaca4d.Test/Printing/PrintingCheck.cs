// 3D printing of fresh concrete: PrintedLayers cuts the printed layers into bricks and ages them,
// FreshConcrete stiffens them with age, SurfaceConnection makes the joint band's material - Printing.cs,
// the C# the printed cylinder example's GhPython components were written from - and Alpaca4d.Buckling
// finds the load factor of the structure printed so far.
// The decks are written the way Assemble writes them: nodes welded, one material per brick, the base
// held, self-weight shared equally among a brick's corners. See README.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Alpaca4d;
using Alpaca4d.Material;
using Alpaca4d.Printing;
using Rhino.Geometry;

class PrintingCheck
{
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail == "" ? "" : "  " + detail)}");
        if (!ok) fails++;
    }

    static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string F(double v, string f = "0.0000") => v.ToString(f, CultureInfo.InvariantCulture);
    static string Pct(double got, double want) => (100 * (got / want - 1)).ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + "%";

    static string Folder;

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

    /// <summary>The volume of a brick of eight corners in OpenSees' order: its trilinear Jacobian, at 2 x 2 x 2 points.</summary>
    static double Volume(Point3d[] c)
    {
        double[] xi = { -1, 1, 1, -1, -1, 1, 1, -1 }, eta = { -1, -1, 1, 1, -1, -1, 1, 1 }, zeta = { -1, -1, -1, -1, 1, 1, 1, 1 };
        double g = 1 / Math.Sqrt(3), v = 0;
        foreach (var r in new[] { -g, g })
            foreach (var q in new[] { -g, g })
                foreach (var p in new[] { -g, g })
                {
                    var j = new double[3, 3];
                    for (int a = 0; a < 8; a++)
                    {
                        var d = new[] { 0.125 * xi[a] * (1 + eta[a] * q) * (1 + zeta[a] * r), 0.125 * eta[a] * (1 + xi[a] * p) * (1 + zeta[a] * r), 0.125 * zeta[a] * (1 + xi[a] * p) * (1 + eta[a] * q) };
                        var x = new[] { c[a].X, c[a].Y, c[a].Z };
                        for (int s = 0; s < 3; s++)
                            for (int t = 0; t < 3; t++)
                                j[s, t] += d[s] * x[t];
                    }
                    v += j[0, 0] * (j[1, 1] * j[2, 2] - j[1, 2] * j[2, 1]) - j[0, 1] * (j[1, 0] * j[2, 2] - j[1, 2] * j[2, 0]) + j[0, 2] * (j[1, 0] * j[2, 1] - j[1, 1] * j[2, 0]);
                }
        return v;
    }

    /// <summary>
    /// The bricks as Assemble writes them: three-dof nodes welded, an nD material per brick, the base
    /// held in all three directions, self-weight as an eighth of each brick's weight at each corner.
    /// </summary>
    static (List<string> tcl, int patternStart, int nodes) Deck(PrintedLayers.Result printed, FreshConcrete concrete,
                                                                 double jointShear = double.NaN, double jointThickness = 0.0, double top = 0.0)
    {
        var at = new Dictionary<(long, long, long), int>();
        var nodes = new List<string>();
        Func<Point3d, int> node = p =>
        {
            var key = ((long)Math.Round(p.X * 1e7), (long)Math.Round(p.Y * 1e7), (long)Math.Round(p.Z * 1e7));
            if (!at.TryGetValue(key, out int tag))
            {
                at[key] = tag = at.Count + 1;
                nodes.Add($"node {tag} {N(p.X)} {N(p.Y)} {N(p.Z)}\n");
            }
            return tag;
        };
        var materials = new List<string>();
        var elements = new List<string>();
        var loads = new Dictionary<int, double>();
        int element = 0;
        foreach (var brick in printed.Bricks)
        {
            var bulk = concrete.At(brick.Age);
            bulk.Id = ++element;
            string material = bulk.WriteTcl();
            if (brick.Joint)
            {
                var joint = SurfaceConnection.Joint(bulk, jointThickness, null, double.IsNaN(jointShear) ? (double?)null : jointShear, out _, out _);
                joint.Id = element;
                material = joint.WriteTcl();
            }
            materials.Add(material);
            var tags = brick.Corners.Select(node).ToArray();
            elements.Add($"element SSPbrick {element} {string.Join(" ", tags)} {element}\n");
            double weight = concrete.Density * 9.81 / 1000.0 * Volume(brick.Corners);
            foreach (var t in tags)
                loads[t] = (loads.TryGetValue(t, out var w) ? w : 0.0) - weight / 8;
        }
        if (top != 0.0)
        {
            // P spread over the top face, each brick's top a share by its area, a quarter at each corner.
            double highest = printed.Bricks.Max(b => b.Corners.Max(p => p.Z));
            var tops = printed.Bricks.Where(b => Math.Abs(b.Corners[4].Z - highest) < 1e-9).ToList();
            double area = tops.Sum(b => b.Corners[4].DistanceTo(b.Corners[5]) * b.Corners[5].DistanceTo(b.Corners[6]));
            foreach (var b in tops)
            {
                double share = top * b.Corners[4].DistanceTo(b.Corners[5]) * b.Corners[5].DistanceTo(b.Corners[6]) / area / 4;
                foreach (var t in b.Corners.Skip(4).Select(node))
                    loads[t] = (loads.TryGetValue(t, out var w) ? w : 0.0) - share;
            }
        }
        var fixes = printed.Base.Select(p => $"fix {node(p)} 1 1 1\n").ToList();

        var tcl = new List<string> { "model BasicBuilder -ndm 3 -ndf 3\n" };
        tcl.AddRange(nodes); tcl.AddRange(fixes); tcl.AddRange(materials); tcl.AddRange(elements);
        int start = tcl.Count;
        tcl.Add("timeSeries Linear 1 -factor 1.0\npattern Plain 1 1 -fact 1 {\n" +
                string.Concat(loads.Select(l => $"\tload {l.Key} 0 0 {N(l.Value)}\n")) + "}\n");
        return (tcl, start, at.Count);
    }

    static Buckling.Outcome Buckle(string os, PrintedLayers.Result printed, FreshConcrete concrete, int modes = 1,
                                   double jointShear = double.NaN, double jointThickness = 0.0, double top = 0.0)
    {
        var (tcl, start, nodes) = Deck(printed, concrete, jointShear, jointThickness, top);
        var outcome = Buckling.Analyse(tcl, start, 1, modes, nodes, Folder, "printing", null, true, Runner(os));
        if (outcome.Failure != null)
            Console.WriteLine("        (" + outcome.Failure + ")");
        return outcome;
    }

    // ------------------------------------------------------------------ the material

    static void Concrete()
    {
        Console.WriteLine("Fresh concrete: Wolfs, Bos and Salet 2018, and levelling off as Suiker 2018 has it");
        var wolfs = new FreshConcrete();
        Check($"E(0) = {F(wolfs.Modulus(0))} MPa, the 0.078 of their fit", Math.Abs(wolfs.Modulus(0) - 0.078) < 1e-12);
        Check($"E(90 min) = {F(wolfs.Modulus(90))} MPa, the 0.186 they measured", Math.Abs(wolfs.Modulus(90) - 0.186) < 1e-12);
        var m = wolfs.At(30);
        Check($"at 30 min an isotropic material of E = {F(m.E, "0.0")} kN/m², G = E / 2(1 + ν), ρ = 2070",
              Math.Abs(m.E - 114.0) < 1e-9 && Math.Abs(m.G - m.E / 2.6) < 1e-9 && m.Rho == 2070.0);

        var levels = new FreshConcrete(0.078, 0.0012, 0.5);
        double slope = (levels.Modulus(1e-4) - levels.Modulus(0)) / 1e-4;
        Check($"levelling off: starts at the same rate, {F(slope, "0.000000")} MPa/min", Math.Abs(slope / 0.0012 - 1) < 1e-3);
        Check($"levelling off: E(50 h) = {F(levels.Modulus(3000))} MPa, near its 0.5", Math.Abs(levels.Modulus(3000) - 0.5) < 0.001 && levels.Modulus(3000) < 0.5);
        bool threw = false;
        try { wolfs.Modulus(-1); } catch (ArgumentException) { threw = true; }
        Check("an age below zero is refused", threw);

        var joint = SurfaceConnection.Joint(m, 0.001, null, null, out double kn, out double ks);
        Check("a joint given no stiffness is the concrete", Math.Abs(joint.Ez - m.E) < 1e-9 && Math.Abs(joint.Gzx - m.G) < 1e-9 && joint.NuZx == m.Nu
              && Math.Abs(kn - m.E / 0.001) < 1e-6 && Math.Abs(ks - m.G / 0.001) < 1e-6);
        joint = SurfaceConnection.Joint(m, 0.001, null, 4000.0, out kn, out ks);
        Check($"k_s = 4000 kN/m³ over 1 mm: G across = {F(joint.Gyz, "0.0")} kN/m², in plane the concrete's",
              Math.Abs(joint.Gyz - 4.0) < 1e-12 && Math.Abs(joint.Gzx - 4.0) < 1e-12 && joint.Gxy == m.G && joint.Ex == m.E);
        joint = SurfaceConnection.Joint(m, 0.001, 10000.0, null, out kn, out ks);
        Check("a softer joint across loses Poisson's coupling across, and stays positive definite", joint.NuYz == 0.0 && joint.NuZx == 0.0 && Math.Abs(joint.Ez - 10.0) < 1e-12);
    }

    // ------------------------------------------------------------------ the bricks

    static List<PrintedLayers.Path> Ring(double radius, int segments, int layers, double height)
    {
        var paths = new List<PrintedLayers.Path>();
        for (int l = 0; l < layers; l++)
            paths.Add(new PrintedLayers.Path(Enumerable.Range(0, segments).Select(k =>
                new Point3d(radius * Math.Cos(2 * Math.PI * k / segments), radius * Math.Sin(2 * Math.PI * k / segments), l * height)), true));
        return paths;
    }

    static void Bricks()
    {
        Console.WriteLine("Printed layers: the bricks of a filament 40 x 10 mm along a straight path");
        const double W = 0.04, H = 0.01;
        var line = Enumerable.Range(0, 3).Select(l => new PrintedLayers.Path(new[] { new Point3d(0, 0, l * H), new Point3d(1, 0, l * H) }, false)).ToList();

        var square = new PrintedLayers(W, H, 4, 2, 0.0, 0.0, 5.0);
        var all = square.Print(line, 100);
        Check($"square sides: 3 layers x 4 x 2 bricks, volume {F(all.Bricks.Sum(b => Volume(b.Corners)) * 1e6, "0.000")} cm³ = 3 x 40 x 10 mm x 1 m",
              all.Bricks.Count == 24 && Math.Abs(all.Bricks.Sum(b => Volume(b.Corners)) - 3 * W * H) < 1e-12);
        Check("every brick's Jacobian positive, as OpenSees wants it", all.Bricks.All(b => Volume(b.Corners) > 0));
        Check($"the base is the bottom of the first layer: {all.Base.Count} points = 5 across x 2 ends", all.Base.Count == 10 && all.Base.All(p => p.Z == 0));
        Check($"three layers printed in 0.6 min at 5 m/min, {F(all.Duration)}", Math.Abs(all.Duration - 0.6) < 1e-12 && all.Layers == 3);

        var round = new PrintedLayers(W, H, 4, 16, 1.0, 0.0, 5.0);
        double area = round.Print(line.Take(1).ToList(), 100).Bricks.Sum(b => Volume(b.Corners));
        double exact = W * H - 2 * (H / 2) * H * (1 - Math.PI / 4);
        Check($"round sides, 16 up: section {F(area * 1e6, "0.00")} mm² ~ {F(exact * 1e6, "0.00")} mm², a rectangle with half circles", Math.Abs(area / exact - 1) < 0.005, Pct(area, exact));
        Check($"the layers touch over the flat, {F(2 * round.HalfWidth(0) * 1000, "0.0")} mm of 40", Math.Abs(2 * round.HalfWidth(0) - (W - H)) < 1e-12);

        var growing = new PrintedLayers(W, H, 2, 1, 1.0, 0.0, 5.0);
        var ring = Ring(0.25, 48, 3, H);
        double lap = 2 * Math.PI * 0.25 * Math.Sin(Math.PI / 48) / (Math.PI / 48) / 5.0;   // one lap of the 48-gon
        var half = growing.Print(ring, 1.5 * lap);
        Check($"half way round the second lap: {half.Bricks.Count / 2} segments printed of 72, {F(half.Layers, "0.000")} layers",
              half.Bricks.Count == 2 * 72 && Math.Abs(half.Layers - 1.5) < 1e-9);
        var first = half.Bricks.Where(b => b.Layer == 0 && b.Segment == 0).Max(b => b.Age);
        var newest = half.Bricks.Where(b => b.Layer == 1).Min(b => b.Age);
        Check($"the oldest brick is {F(first, "0.000")} min old, the newest {F(newest, "0.000")}: half a segment's time",
              Math.Abs(first - (1.5 * lap - lap / 96)) < 1e-9 && Math.Abs(newest - lap / 96) < 1e-9);
        Check("the nozzle is half way round, on top of the second layer",
              Math.Abs(half.Nozzle.X + 0.25) < 1e-9 && Math.Abs(half.Nozzle.Y) < 1e-9 && Math.Abs(half.Nozzle.Z - 2 * H) < 1e-12);

        var jointed = new PrintedLayers(W, H, 2, 2, 1.0, 0.001, 5.0).Print(line, 100);
        Check($"a 1 mm joint band at the bottom of layers 2 and 3: {jointed.Bricks.Count(b => b.Joint)} joint bricks of {jointed.Bricks.Count}",
              jointed.Bricks.Count(b => b.Joint) == 4 && jointed.Bricks.Count == 16 && jointed.Bricks.Where(b => b.Joint).All(b => b.Layer > 0
              && Math.Abs(b.Corners.Max(c => c.Z) - b.Corners.Min(c => c.Z) - 0.001) < 1e-12));
    }

    // ------------------------------------------------------------------ buckling while printing

    /// <summary>
    /// The load factor of a free wall l tall from the equation Suiker (2018) derives for it, his Eq. (25)
    /// for a free wall: (D(x) w'')'' − (ρgh (x − l) w')' = 0, foot clamped, top free, D(x) = D0 (1 + ξ_E age)
    /// with age = (l − x) / l̇ - as a column of n Hermite beam elements with the geometric stiffness of
    /// their self-weight, its lowest factor found by inverse iteration.
    /// </summary>
    static double WallFactor(double l, double d0, double q, double xiE, double grow, int n = 400)
    {
        int size = 2 * n;
        var k = new double[size, size];
        var g = new double[size, size];
        double L = l / n;
        for (int e = 0; e < n; e++)
        {
            double middle = (e + 0.5) * L, d = d0 * (1 + xiE * (l - middle) / grow), p = q * (l - middle);
            double[,] ke = { { 12, 6 * L, -12, 6 * L }, { 6 * L, 4 * L * L, -6 * L, 2 * L * L }, { -12, -6 * L, 12, -6 * L }, { 6 * L, 2 * L * L, -6 * L, 4 * L * L } };
            double[,] ge = { { 36, 3 * L, -36, 3 * L }, { 3 * L, 4 * L * L, -3 * L, -L * L }, { -36, -3 * L, 36, -3 * L }, { 3 * L, -L * L, -3 * L, 4 * L * L } };
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 4; b++)
                {
                    int i = 2 * e + a - 2, j = 2 * e + b - 2;   // the foot's two dofs held
                    if (i < 0 || j < 0) continue;
                    k[i, j] += d / (L * L * L) * ke[a, b];
                    g[i, j] += p / (30 * L) * ge[a, b];
                }
        }
        // Cholesky of K, then z = K⁻¹ G x until it settles.
        var c = new double[size, size];
        for (int i = 0; i < size; i++)
            for (int j = Math.Max(0, i - 3); j <= i; j++)
            {
                double sum = k[i, j];
                for (int m = Math.Max(0, i - 3); m < j; m++) sum -= c[i, m] * c[j, m];
                c[i, j] = i == j ? Math.Sqrt(sum) : sum / c[j, j];
            }
        var x = Enumerable.Range(0, size).Select(i => (double)(i + 1)).ToArray();
        double lambda = 0;
        for (int it = 0; it < 200; it++)
        {
            var y = new double[size];
            for (int i = 0; i < size; i++)
                for (int j = Math.Max(0, i - 3); j < Math.Min(size, i + 4); j++)
                    y[i] += g[i, j] * x[j];
            for (int i = 0; i < size; i++) { for (int m = Math.Max(0, i - 3); m < i; m++) y[i] -= c[i, m] * y[m]; y[i] /= c[i, i]; }
            for (int i = size - 1; i >= 0; i--) { for (int m = i + 1; m < Math.Min(size, i + 4); m++) y[i] -= c[m, i] * y[m]; y[i] /= c[i, i]; }
            double kk = 0, gg = 0;
            for (int i = 0; i < size; i++)
                for (int j = Math.Max(0, i - 3); j < Math.Min(size, i + 4); j++)
                {
                    kk += y[i] * k[i, j] * y[j];
                    gg += y[i] * g[i, j] * y[j];
                }
            double next = kk / gg, norm = Math.Sqrt(y.Sum(v => v * v));
            x = y.Select(v => v / norm).ToArray();
            if (Math.Abs(next / lambda - 1) < 1e-12) { lambda = next; break; }
            lambda = next;
        }
        return lambda;
    }

    /// <summary>
    /// A free wall printed under linear curing (Suiker 2018): the height at which it buckles, the
    /// bricks against his equation solved by <see cref="WallFactor"/>. His closed form,
    /// l_cr = (1.98635 + 0.996 ξ^0.793) (D0 / ρgh)^(1/3), ξ = (D0 / ρgh)^(1/3) ξ_E / l̇ - Eqs. (75),
    /// (111) and (112) - is exact without curing and a least-squares fit with it, 3 % high at
    /// ξ = 0.53, so it is shown alongside. D0 is the plate stiffness at age 0, h the wall's thickness,
    /// ξ_E the rate of curing as a share of E0 per minute and l̇ how fast the wall grows. ν = 0, so
    /// that D0 = E0 h³ / 12 whether the wall bends as a plate or as a beam.
    ///
    /// A mortar ten times as stiff as Wolfs', curing at the same rate for its stiffness: its wall is
    /// slender enough for plate theory. Wolfs' own stands only four times its thickness at the
    /// moment it buckles, and shear deformation, which a plate leaves out, takes 2 % off that.
    /// </summary>
    static void FreeWall(string os)
    {
        Console.WriteLine("A free wall printed under linear curing (Suiker 2018)");
        const double h = 0.04, H = 0.005, E0 = 0.78, rho = 2070, length = 0.1;
        foreach (double minutes in new[] { 0.0, 1.0 })
        {
            // A rate of zero is a wall that never stiffens: Greenhill's column. A minute a layer gives it time to.
            double rate = minutes == 0.0 ? 0.0 : 0.012;
            var concrete = new FreshConcrete(E0, rate, double.NaN, 0.0, rho);
            double speed = minutes == 0.0 ? 5.0 : length / minutes;
            double grow = H / (length / speed);   // m per minute
            double d0 = ModelStress.FromMPa(E0) * h * h * h / 12, q = rho * 9.81 / 1000 * h, xiE = rate / E0;
            double scale = Math.Pow(d0 / q, 1.0 / 3);
            double xi = scale * xiE / grow;
            double fit = (1.98635 + 0.996 * Math.Pow(xi, 0.793)) * scale;
            double lo = 0.5 * fit, hi = 1.5 * fit;
            for (int it = 0; it < 50; it++)
            {
                double mid = (lo + hi) / 2;
                if (WallFactor(mid, d0, q, xiE, grow) > 1.0) lo = mid; else hi = mid;
            }
            double exact = lo;

            var printer = new PrintedLayers(h, H, 2, 1, 0.0, 0.0, speed);
            var paths = Enumerable.Range(0, (int)(1.3 * exact / H)).Select(l => new PrintedLayers.Path(new[] { new Point3d(0, 0, l * H), new Point3d(length / 2, 0, l * H), new Point3d(length, 0, l * H) }, false)).ToList();
            // λ layer by layer from a little below until it drops under 1, the crossing found on log λ
            // against log l.
            var heights = new List<double>();
            var factors = new List<double>();
            for (int n = (int)(0.95 * exact / H); n < paths.Count && (factors.Count == 0 || factors.Last() >= 1.0); n++)
            {
                var outcome = Buckle(os, printer.Print(paths, n * length / speed + 1e-9), concrete);
                heights.Add(n * H);
                factors.Add(outcome.Factors.Length > 0 ? outcome.Factors[0] : double.NaN);
            }
            int k = factors.FindIndex(f => f < 1.0);
            double found = double.NaN;
            if (k > 0)
            {
                double a = Math.Log(factors[k - 1]), b = Math.Log(factors[k]);
                found = Math.Exp(Math.Log(heights[k - 1]) + a / (a - b) * (Math.Log(heights[k]) - Math.Log(heights[k - 1])));
            }
            string what = minutes == 0.0 ? "no curing (ξ = 0)" : $"{minutes} min a layer, ξ = {F(xi, "0.000")}";
            Check($"{what}: buckles at {F(found * 1000, "0.0")} mm = {F(exact * 1000, "0.0")} mm (his closed form {F(fit * 1000, "0.0")})",
                  Math.Abs(found / exact - 1) < 0.01, Pct(found, exact) + "  λ " + string.Join(" ", factors.Select(f => F(f, "0.000"))));
        }
    }

    /// <summary>
    /// A joint band between the layers of a free wall (Round 0, so that the band's row of nodes
    /// changes nothing else): as stiff as the concrete it is the bonded wall; a hundred times softer in
    /// shear it lowers the factor through the slip of its joints, as a stack of cards. A band has the
    /// stress stiffness of a joint, not of a band: with its own, a band t thick under σ would buckle in
    /// shear at σ = k_s t - here half the pressure at the foot - whatever the wall above it.
    /// </summary>
    static void Joint(string os)
    {
        Console.WriteLine("A free wall with a joint band between its layers");
        const double h = 0.04, H = 0.005, t = 0.001, length = 0.1;
        var concrete = new FreshConcrete(0.78, 0.0, double.NaN, 0.0, 2070);
        var paths = Enumerable.Range(0, 68).Select(l => new PrintedLayers.Path(new[] { new Point3d(0, 0, l * H), new Point3d(length / 2, 0, l * H), new Point3d(length, 0, l * H) }, false)).ToList();
        double done = 68 * length / 5.0 + 1e-9;
        var bonded = Buckle(os, new PrintedLayers(h, H, 2, 1, 0.0, 0.0, 5.0).Print(paths, done), concrete);
        var banded = new PrintedLayers(h, H, 2, 1, 0.0, t, 5.0).Print(paths, done);
        var same = Buckle(os, banded, concrete, 1, double.NaN, t);
        double g = concrete.At(0).G, soft = g / t / 100;
        var slipping = Buckle(os, banded, concrete, 1, soft, t);
        double foot = 2070 * 9.81 / 1000 * 68 * H;
        double l0 = bonded.Factors.FirstOrDefault(), l1 = same.Factors.FirstOrDefault(), l2 = slipping.Factors.FirstOrDefault();
        Check($"the 67 bands' {same.Deck.JointSolids} bricks marked, of {same.Deck.Solids.Count}",
              same.Deck.JointSolids == 67 * 2 * 2 && same.Deck.Solids.Count == 68 * 2 * 2 + 67 * 2 * 2 && same.Deck.JointMaterials.Count == 67 * 2 * 2
              && same.Deck.Solids.Count(x => x.Joint) == 67 * 2 * 2);
        Check($"a band as stiff as the concrete: {F(l1)} = {F(l0)} bonded", Math.Abs(l1 / l0 - 1) < 0.01, Pct(l1, l0));
        Check($"a hundred times softer in shear: {F(l2)}, lower, and far above the {F(soft * t / foot)} of a band buckling in shear",
              l2 < 0.98 * l0 && l2 > 1.3 * soft * t / foot);

        // Haringx: a weightless wall, P on its top, buckles at P = (√(S² + 4 P_E S) − S) / 2, P_E Euler's
        // 2 L, S its shear stiffness: the concrete's, (5/6) G over each layer less its joint, in
        // series with the joint's, k_s over the area, as a stack of plates on rubber is reckoned.
        var weightless = new FreshConcrete(0.78, 0.0, double.NaN, 0.0, 0.0);
        double L = 68 * H, area = h * length, ei = ModelStress.FromMPa(0.78) * length * h * h * h / 12, P = 0.001;
        double pe = Math.PI * Math.PI * ei / (4 * L * L);
        var euler = Buckle(os, new PrintedLayers(h, H, 2, 1, 0.0, 0.0, 5.0).Print(paths, done), weightless, 1, double.NaN, 0.0, P);
        Check($"no joints, P on top: {F(euler.Factors.FirstOrDefault())} = {F(pe / P)}, Euler", Math.Abs(euler.Factors.FirstOrDefault() * P / pe - 1) < 0.01, Pct(euler.Factors.FirstOrDefault() * P, pe));
        double gc = weightless.At(0).G;
        double shear = area * H / ((H - t) / (5.0 / 6 * gc) + 1 / soft);
        double haringx = (Math.Sqrt(shear * shear + 4 * pe * shear) - shear) / 2;
        var stack = Buckle(os, banded, weightless, 1, soft, t, P);
        Check($"joints a hundred times softer, P on top: {F(stack.Factors.FirstOrDefault())} = {F(haringx / P)}, Haringx's; {F(pe / P)} without the slip",
              Math.Abs(stack.Factors.FirstOrDefault() * P / haringx - 1) < 0.02, Pct(stack.Factors.FirstOrDefault() * P, haringx));
    }

    /// <summary>
    /// The cylinder Wolfs, Bos and Salet 2018 printed: a heart line of radius 250 mm, layers 40 x 10 mm,
    /// 5 m/min. Their five prints fell at 25 to 31 layers, 29 on average; their Abaqus model, plastic and
    /// geometrically non-linear, at 40 to 53. Not a check - a table of the load factor and of what it costs.
    /// </summary>
    static void Cylinder(string os, int across, int up, int segments, double round, int[] layers)
    {
        var concrete = new FreshConcrete();
        var printer = new PrintedLayers(0.04, 0.01, across, up, round, 0.0, 5.0);
        var paths = Ring(0.25, segments, layers.Max(), 0.01);
        double lap = paths[0].Length / 5.0;
        Console.WriteLine($"Cylinder, {across} x {up} bricks a section, {segments} round, round sides {round}:");
        foreach (int n in layers)
        {
            var clock = Stopwatch.StartNew();
            var printed = printer.Print(paths, n * lap + 1e-9);
            var outcome = Buckle(os, printed, concrete, 2);
            Console.WriteLine($"  {n,3} layers  {printed.Bricks.Count,6} bricks  {outcome.Equations,6} equations  λ = " +
                              $"{string.Join(", ", outcome.Factors.Select(f => F(f, "0.000")))}  {clock.Elapsed.TotalSeconds:0.0} s");
        }
    }

    static void Main(string[] args)
    {
        Folder = Path.Combine(Path.GetTempPath(), "alpaca4d_printing_" + Process.GetCurrentProcess().Id);
        Directory.CreateDirectory(Folder);
        try
        {
            string os = args.FirstOrDefault(File.Exists);
            if (args.Contains("--cylinder"))
            {
                // --cylinder across up segments round layers...
                var n = args.SkipWhile(a => a != "--cylinder").Skip(1).ToArray();
                Cylinder(os, int.Parse(n[0]), int.Parse(n[1]), int.Parse(n[2]), double.Parse(n[3], CultureInfo.InvariantCulture),
                         n.Skip(4).Select(int.Parse).ToArray());
                return;
            }
            Concrete();
            Bricks();
            if (os == null)
                Console.WriteLine("  [skip] no OpenSees given: the buckling of a printed wall");
            else
            {
                FreeWall(os);
                Joint(os);
            }
        }
        finally
        {
            try { Directory.Delete(Folder, true); } catch (IOException) { }
        }

        Console.WriteLine(fails == 0 ? "\nAll checks passed." : $"\n{fails} check(s) failed.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
