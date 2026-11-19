// Buckling lengths worked out from the model: Alpaca4d.Design.BucklingLengths.
//
// The structures the method was sketched on before it was written, each meshed at 0.5 m, with the
// lengths worked out by hand: a floor grid, a two-storey frame braced in its plane, a portal unbraced
// and braced, a roof of purlins with and without a bracing bay, a beam with an overhang, an arch.
// Then the local axes: the same frame with its columns turned, a column turned halfway up, and L_LT
// taking the length about the weak axis. Last, how long a large frame takes.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Design;

class BucklingLengthsTest
{
    static int fails = 0;
    static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static readonly Vector3d X = Vector3d.XAxis, Y = Vector3d.YAxis, Z = Vector3d.ZAxis;

    /// <summary>A model of bars, each line cut into pieces of about 0.5 m, tagged so the checks can find them.</summary>
    sealed class Frame
    {
        public readonly List<BucklingLengths.Bar> Bars = new List<BucklingLengths.Bar>();
        public readonly List<BucklingLengths.Restraint> Restraints = new List<BucklingLengths.Restraint>();
        public readonly List<string> Tags = new List<string>();
        public BucklingLengths Lengths;

        public void Line(Point3d a, Point3d b, string tag, Vector3d localZ = default, bool majorIsY = false, double step = 0.5)
        {
            int n = Math.Max(1, (int)Math.Round(a.DistanceTo(b) / step));
            var points = Enumerable.Range(0, n + 1).Select(k => a + (b - a) * ((double)k / n)).ToList();
            Polyline(points, tag, localZ, majorIsY);
        }

        public void Polyline(IList<Point3d> points, string tag, Vector3d localZ = default, bool majorIsY = false)
        {
            var z = localZ.IsZero ? Vector3d.Unset : localZ;
            for (int k = 0; k + 1 < points.Count; k++)
            {
                Bars.Add(new BucklingLengths.Bar(points[k], points[k + 1], z, majorIsY));
                Tags.Add(tag);
            }
        }

        public void Support(Point3d at, params Vector3d[] directions)
        {
            foreach (var d in directions)
                Restraints.Add(new BucklingLengths.Restraint(at, d));
        }

        public Frame Solve()
        {
            Lengths = BucklingLengths.Of(Bars, Restraints, null, 1e-6);
            return this;
        }

        /// <summary>
        /// Every bar tagged <paramref name="tag"/> - the ones <paramref name="where"/> picks by their
        /// middle - has these lengths about y and about z.
        /// </summary>
        public void Expect(string tag, double aboutY, double aboutZ, string what, Func<Point3d, bool> where = null)
        {
            var bars = Enumerable.Range(0, Bars.Count)
                                 .Where(i => Tags[i] == tag && (where == null || where(Mid(i))))
                                 .ToList();
            if (bars.Count == 0)
            {
                Check(false, $"{what}: no bar tagged {tag}");
                return;
            }

            var wrong = bars.Where(i => Math.Abs(Lengths.AboutY[i] - aboutY) > 0.005 || Math.Abs(Lengths.AboutZ[i] - aboutZ) > 0.005).ToList();
            string got = wrong.Count == 0
                ? ""
                : $" - bar at {Mid(wrong[0])} has y {Lengths.AboutY[wrong[0]].ToString("0.###", Invariant)}, z {Lengths.AboutZ[wrong[0]].ToString("0.###", Invariant)}";
            Check(wrong.Count == 0,
                  $"{what}: {bars.Count} bars, about y {aboutY.ToString("0.###", Invariant)}, about z {aboutZ.ToString("0.###", Invariant)}{got}");
        }

        public Point3d Mid(int bar) => (Bars[bar].Start + Bars[bar].End) / 2;
    }

    static Point3d P(double x, double y, double z) => new Point3d(x, y, z);

    // With the beam component's default axes a beam's local y points up and its local z across,
    // and a column's local y runs along global X and its z along Y. So for a beam, about z is up and
    // down and about y sideways; for a column, about z is along X and about y along Y.

    static void FloorGrid()
    {
        Console.WriteLine("Floor grid: main beams 8 m, secondary beams 6 m on them every 2 m");
        var f = new Frame();
        foreach (double y in new[] { 0.0, 6.0 }) f.Line(P(0, y, 0), P(8, y, 0), "M");
        foreach (double x in new[] { 0.0, 8.0 }) f.Line(P(x, 0, 0), P(x, 6, 0), "E");
        foreach (double x in new[] { 2.0, 4.0, 6.0 }) f.Line(P(x, 0, 0), P(x, 6, 0), "S");
        foreach (var p in new[] { P(0, 0, 0), P(8, 0, 0), P(0, 6, 0), P(8, 6, 0) }) f.Support(p, X, Y, Z);
        f.Solve();

        f.Expect("M", 8.0, 8.0, "main beam: held only at its supports, either way");
        f.Expect("E", 6.0, 6.0, "edge beam between supports");
        f.Expect("S", 6.0, 6.0, "secondary beam: carried up and down by the main beams it sits on, its whole span sideways");
        var m = f.Lengths.Members[f.Lengths.MemberOf[f.Tags.IndexOf("M")]];
        Check(Enumerable.Range(1, m.HeldZ.Count - 2).All(k => m.HeldZ[k] == HeldBy.None),
              "a main beam is not held sideways by the secondary beams: they would have to bend about their weak axis");
        var s = f.Lengths.Members[f.Lengths.MemberOf[f.Tags.IndexOf("S")]];
        Check(s.HeldY[0] == HeldBy.Member && s.HeldY[s.HeldY.Count - 1] == HeldBy.Member,
              "and it says the main beams hold it: HeldBy.Member at both ends");
        Check(f.Lengths.Members.Count == 7, $"seven members out of {f.Bars.Count} bars: {f.Lengths.Members.Count}");
    }

    static void BracedFrame()
    {
        Console.WriteLine("Two storeys, braced in their plane, held out of it at the top");
        var f = new Frame();
        foreach (double x in new[] { 0.0, 6.0 }) f.Line(P(x, 0, 0), P(x, 0, 6), "C");
        foreach (double z in new[] { 3.0, 6.0 }) f.Line(P(0, 0, z), P(6, 0, z), "B");
        f.Line(P(0, 0, 0), P(6, 0, 3), "D");
        f.Line(P(0, 0, 3), P(6, 0, 6), "D");
        foreach (double x in new[] { 0.0, 6.0 })
        {
            f.Support(P(x, 0, 0), X, Y, Z);
            f.Support(P(x, 0, 6), Y);
        }
        f.Solve();

        f.Expect("C", 6.0, 3.0, "column: a storey along X, the frame braced; its whole height along Y");
        f.Expect("B", 6.0, 6.0, "beam");
        // Out of the plane each diagonal is held at one end only: its other end is on a column at
        // mid-height, which only its weak axis would hold. A cantilever, twice its length.
        f.Expect("D", 2 * Math.Sqrt(45), Math.Sqrt(45), "diagonal: end to end in the plane, twice that out of it");
    }

    static void Portal(bool braced)
    {
        Console.WriteLine(braced ? "Portal 8 x 4 m, braced" : "Portal 8 x 4 m, unbraced: it sways");
        var f = new Frame();
        foreach (double x in new[] { 0.0, 8.0 }) f.Line(P(x, 0, 0), P(x, 0, 4), "C");
        f.Line(P(0, 0, 4), P(8, 0, 4), "B");
        if (braced) f.Line(P(0, 0, 0), P(8, 0, 4), "D");
        foreach (double x in new[] { 0.0, 8.0 })
        {
            f.Support(P(x, 0, 0), X, Y, Z);
            f.Support(P(x, 0, 4), Y);
        }
        f.Solve();

        if (braced)
        {
            f.Expect("C", 4.0, 4.0, "column, held at both ends both ways");
            f.Expect("D", Math.Sqrt(80), Math.Sqrt(80), "diagonal");
        }
        else
        {
            f.Expect("C", 4.0, 8.0, "column: twice its height along X, as its top sways; its height along Y");
        }
        f.Expect("B", 8.0, 8.0, "beam");
    }

    static void Roof(bool braced)
    {
        Console.WriteLine(braced ? "Roof of purlins with a bracing bay" : "Roof of purlins, no bracing");
        var f = new Frame();
        foreach (double y in new[] { 0.0, 4.0, 8.0 }) f.Line(P(0, y, 0), P(8, y, 0), "R");
        foreach (double x in new[] { 2.0, 4.0, 6.0 }) f.Line(P(x, 0, 0), P(x, 8, 0), "P");
        if (braced)
            foreach (var ab in new[] { (P(0, 0, 0), P(2, 4, 0)), (P(2, 4, 0), P(4, 0, 0)), (P(4, 0, 0), P(6, 4, 0)), (P(6, 4, 0), P(8, 0, 0)) })
                f.Line(ab.Item1, ab.Item2, "D");
        foreach (double y in new[] { 0.0, 4.0, 8.0 })
            foreach (double x in new[] { 0.0, 8.0 })
                f.Support(P(x, y, 0), X, Y, Z);
        f.Solve();

        if (braced)
        {
            f.Expect("R", 2.0, 8.0, "rafter: held sideways at every purlin, which the bracing holds");
            f.Expect("D", Math.Sqrt(20), Math.Sqrt(20), "brace");
        }
        else
        {
            f.Expect("R", 8.0, 8.0, "rafter: the purlins hold nothing sideways, the roof can shear");
        }
        f.Expect("P", 4.0, 4.0, "purlin: carried on the rafters, held along them by the rafters' supports");
    }

    static void Overhang()
    {
        Console.WriteLine("Beam 8 m on supports at 0 and 6 m");
        var f = new Frame();
        f.Line(P(0, 0, 0), P(8, 0, 0), "B");
        f.Support(P(0, 0, 0), X, Y, Z);
        f.Support(P(6, 0, 0), X, Y, Z);
        f.Solve();

        f.Expect("B", 6.0, 6.0, "the span", p => p.X < 6.0);
        f.Expect("B", 4.0, 4.0, "the overhang: twice its 2 m", p => p.X > 6.0);
        var b = f.Lengths.Members[0];
        Check(b.SegmentsY.Count == 2 && b.SegmentsY[1].Kind == SegmentKind.Free, "two pieces, the second free at its end");
    }

    static void Arch()
    {
        Console.WriteLine("Parabolic arch, 12 m span, 3 m rise, pinned at both springings");
        var f = new Frame();
        var points = Enumerable.Range(0, 25).Select(k => k * 0.5).Select(x => P(x, 0, 4 * 3 * x * (12 - x) / 144)).ToList();
        f.Polyline(points, "A", Y);
        f.Support(points[0], X, Y, Z);
        f.Support(points[points.Count - 1], X, Y, Z);
        f.Solve();

        double length = 0.0;
        for (int k = 0; k + 1 < points.Count; k++) length += points[k].DistanceTo(points[k + 1]);
        f.Expect("A", length, length, $"its whole length, {length.ToString("0.00", Invariant)} m, either way: one member for all its bends");
    }

    static void TurnedAxes()
    {
        Console.WriteLine("Local axes: the braced frame with its columns' local z along X, then turned halfway up");
        var f = new Frame();
        foreach (double x in new[] { 0.0, 6.0 }) f.Line(P(x, 0, 0), P(x, 0, 6), "C", X);
        foreach (double z in new[] { 3.0, 6.0 }) f.Line(P(0, 0, z), P(6, 0, z), "B");
        f.Line(P(0, 0, 0), P(6, 0, 3), "D");
        f.Line(P(0, 0, 3), P(6, 0, 6), "D");
        foreach (double x in new[] { 0.0, 6.0 })
        {
            f.Support(P(x, 0, 0), X, Y, Z);
            f.Support(P(x, 0, 6), Y);
        }
        f.Solve();
        f.Expect("C", 3.0, 6.0, "z along X: the short length is now about y, the long one about z");

        // The same column in two halves, its local z along Y below 3 m and along X above.
        var g = new Frame();
        g.Line(P(0, 0, 0), P(0, 0, 3), "Low", Y);
        g.Line(P(0, 0, 3), P(0, 0, 6), "High", X);
        g.Line(P(6, 0, 0), P(6, 0, 6), "C");
        foreach (double z in new[] { 3.0, 6.0 }) g.Line(P(0, 0, z), P(6, 0, z), "B");
        g.Line(P(0, 0, 0), P(6, 0, 3), "D");
        g.Line(P(0, 0, 3), P(6, 0, 6), "D");
        foreach (double x in new[] { 0.0, 6.0 })
        {
            g.Support(P(x, 0, 0), X, Y, Z);
            g.Support(P(x, 0, 6), Y);
        }
        g.Solve();
        Check(g.Lengths.MemberOf[g.Tags.IndexOf("Low")] == g.Lengths.MemberOf[g.Tags.IndexOf("High")], "the two halves are one member");
        g.Expect("Low", 6.0, 3.0, "lower half, z along Y: 6 m about y, a storey about z");
        g.Expect("High", 3.0, 6.0, "upper half, z along X: each beam takes the restraints across its own axes");
    }

    static void LateralTorsional()
    {
        Console.WriteLine("L_LT: the length about the axis the section is weak about");
        var f = new Frame();
        f.Line(P(0, 0, 0), P(8, 0, 0), "Major z");
        f.Line(P(0, 4, 0), P(8, 4, 0), "Major y", default, true);
        f.Line(P(4, 0, 0), P(4, 4, 0), "Tie");
        foreach (var p in new[] { P(0, 0, 0), P(8, 0, 0), P(0, 4, 0), P(8, 4, 0) }) f.Support(p, X, Y, Z);
        // Held sideways at its middle by a tie to the other beam: not up and down.
        f.Support(P(4, 0, 0), Y);
        f.Support(P(4, 4, 0), Y);
        f.Solve();

        int a = f.Tags.IndexOf("Major z"), b = f.Tags.IndexOf("Major y");
        Check(Math.Abs(f.Lengths.AboutY[a] - 4.0) < 1e-6 && Math.Abs(f.Lengths.AboutZ[a] - 8.0) < 1e-6,
              $"held sideways at mid-span: 4 m about y, 8 m about z: {f.Lengths.AboutY[a]}, {f.Lengths.AboutZ[a]}");
        Check(Math.Abs(f.Lengths.LateralTorsional[a] - 4.0) < 1e-6, $"major axis z: L_LT is the length about y, 4 m: {f.Lengths.LateralTorsional[a]}");
        Check(Math.Abs(f.Lengths.LateralTorsional[b] - 8.0) < 1e-6, $"major axis y: L_LT is the length about z, 8 m: {f.Lengths.LateralTorsional[b]}");
    }

    static void Large(bool braced)
    {
        // 10 storeys of 3.5 m, 6 x 6 bays of 6 m, a secondary beam every 2 m, all at 0.5 m.
        var f = new Frame();
        const int bays = 6, storeys = 10;
        for (int i = 0; i <= bays; i++)
            for (int j = 0; j <= bays; j++)
            {
                f.Line(P(6 * i, 6 * j, 0), P(6 * i, 6 * j, 3.5 * storeys), "C");
                f.Support(P(6 * i, 6 * j, 0), X, Y, Z);
            }
        for (int s = 1; s <= storeys; s++)
        {
            double z = 3.5 * s;
            for (int i = 0; i <= bays; i++)
            {
                f.Line(P(6 * i, 0, z), P(6 * i, 6 * bays, z), "M");
                f.Line(P(0, 6 * i, z), P(6 * bays, 6 * i, z), "M");
            }
            for (int i = 0; i < bays; i++)
                foreach (double dx in new[] { 2.0, 4.0 })
                    f.Line(P(6 * i + dx, 0, z), P(6 * i + dx, 6 * bays, z), "S");
            if (braced)
            {
                // A bay braced on each face, and the floor braced in its plane: every cell along one
                // edge and one end, which is enough to make a grid rigid.
                foreach (var ab in new[] { (P(0, 0, z - 3.5), P(6, 0, z)), (P(0, 6 * bays, z - 3.5), P(6, 6 * bays, z)),
                                           (P(0, 0, z - 3.5), P(0, 6, z)), (P(6 * bays, 0, z - 3.5), P(6 * bays, 6, z)) })
                    f.Line(ab.Item1, ab.Item2, "D");
                for (double x = 0; x < 6 * bays; x += 2) f.Line(P(x, 0, z), P(x + 2, 6, z), "F");
                for (double y = 6; y < 6 * bays; y += 6) f.Line(P(0, y, z), P(2, y + 6, z), "F");
            }
        }

        var clock = Stopwatch.StartNew();
        f.Solve();
        clock.Stop();
        Console.WriteLine($"  {(braced ? "braced" : "unbraced")}: {f.Bars.Count} beams, {f.Lengths.Members.Count} members, {clock.ElapsedMilliseconds} ms" +
                          (f.Lengths.Approximate ? " (approximate)" : ""));
        Check(!f.Lengths.Approximate, "the mechanisms were looked for, not approximated");
        Check(clock.ElapsedMilliseconds < 5000, "within 5 s");
        if (braced)
            f.Expect("C", 3.5, 3.5, "braced: every column a storey long", p => p.Z < 35.0);
        else
            f.Expect("C", 7.0, 7.0, "unbraced: each storey of a column sways, twice 3.5 m", p => p.Z < 35.0);
    }

    static void Main(string[] args)
    {
        FloorGrid();
        BracedFrame();
        Portal(false);
        Portal(true);
        Roof(false);
        Roof(true);
        Overhang();
        Arch();
        TurnedAxes();
        LateralTorsional();
        Console.WriteLine("Speed: a large frame");
        Large(true);
        Large(false);

        Console.WriteLine(fails == 0 ? "All passed." : $"{fails} FAILED.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
