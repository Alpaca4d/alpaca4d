// The words a beam's GeomTransf input takes, checked without Rhino.
//
// GeomTransfCheck.exe checks GeomTransf.TryParseType and the geomTransf line each type writes, then
// writes a deck around that line for OpenSees: a cantilever bent by an end moment of 2 pi EI / L.
// Linear, the tip rises pi L and turns 2 pi, as small-displacement theory says. Corotational, the
// beam curls into a full circle and its tip comes back to its root.
//
// The transformations have no curve: RhinoCommon cannot make one outside Rhino, and the line a
// transformation writes does not need one.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Alpaca4d.Element;
using Rhino.Geometry;

public class GeomTransfCheck
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    static void Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        var folder = args.Length > 0 ? args[0] : ".";

        Words();
        Lines();

        Section("Deck for OpenSees");
        File.WriteAllText(Path.Combine(folder, "curl.tcl"), Curl());
        Console.WriteLine("  [ .. ] curl written");

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all write checks passed" : $"{fails} write check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static bool Is(string text, GeomTransfType expected) =>
        GeomTransf.TryParseType(text, out var type) && type == expected;

    static void Words()
    {
        Section("The words the input takes");
        Check(Is("Linear", GeomTransfType.Linear) && Is("PDelta", GeomTransfType.PDelta) && Is("Corotational", GeomTransfType.Corotational),
              "Linear, PDelta and Corotational, as the Value List gives them");
        Check(Is("linear", GeomTransfType.Linear) && Is("CORO TATIONAL", GeomTransfType.Corotational) && Is(" pdelta ", GeomTransfType.PDelta),
              "in any case, with spaces round or inside");
        Check(Is("P-Delta", GeomTransfType.PDelta) && Is("p_delta", GeomTransfType.PDelta) && Is("Corot", GeomTransfType.Corotational),
              "P-Delta written with a dash, and Corot for short");
        Check(!GeomTransf.TryParseType(null, out _) && !GeomTransf.TryParseType("", out _) && !GeomTransf.TryParseType("  ", out _),
              "nothing is not a word");
        Check(!GeomTransf.TryParseType("Elastic", out _) && !GeomTransf.TryParseType("1", out _) && !GeomTransf.TryParseType("Lin", out _),
              "nor is anything else");
    }

    static void Lines()
    {
        Section("The line each one writes");
        foreach (GeomTransfType type in Enum.GetValues(typeof(GeomTransfType)))
        {
            var line = new GeomTransf(type, null, Vector3d.ZAxis) { Id = 4 }.WriteTcl();
            Check(line == $"geomTransf {type} 4 0 0 1\n", $"{type}: {line.TrimEnd()}");
        }
    }

    const double L = 10.0, E = 2.1e8, A = 0.01, G = 8.1e7, J = 1e-4, I = 1e-4;
    const int Pieces = 20;

    /// <summary>
    /// The cantilever along X, in Pieces elastic beams, its root fixed, bent about Z by
    /// M = 2 pi EI / L at its tip: once with each transformation, the geomTransf line Core's.
    /// </summary>
    static string Curl()
    {
        double moment = 2.0 * Math.PI * E * I / L;
        var text = new StringBuilder();
        text.Append("proc check {what ok} { if {$ok} { puts \"  \\[PASS\\] $what\" } else { puts \"  \\[FAIL\\] $what\" } }\n");
        text.Append("proc near {a b tol} { expr {abs($a - $b) <= $tol * max(1.0, abs($b))} }\n");

        foreach (var (type, steps) in new[] { (GeomTransfType.Linear, 1), (GeomTransfType.Corotational, 40) })
        {
            text.Append(type == GeomTransfType.Linear
                ? "puts \"A. Linear: a cantilever bent by 2 pi EI / L at its tip, the small-displacement answer\"\n"
                : "puts \"B. Corotational: the same cantilever curls into a full circle, its tip back at its root\"\n");
            text.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
            for (int n = 0; n <= Pieces; n++)
                text.Append($"node {n + 1} {L * n / Pieces} 0 0\n");
            text.Append("fix 1 1 1 1 1 1 1\n");
            text.Append(new GeomTransf(type, null, Vector3d.ZAxis) { Id = 1 }.WriteTcl());
            for (int n = 1; n <= Pieces; n++)
                text.Append($"element elasticBeamColumn {n} {n} {n + 1} {A} {E} {G} {J} {I} {I} 1\n");
            int tip = Pieces + 1;
            text.Append($"timeSeries Linear 1\npattern Plain 1 1 {{\n\tload {tip} 0 0 0 0 0 {moment}\n}}\n");
            text.Append("system BandGeneral\nnumberer RCM\nconstraints Plain\ntest NormDispIncr 1e-10 50\nalgorithm Newton\n");
            text.Append($"integrator LoadControl {1.0 / steps}\nanalysis Static\n");
            text.Append($"check \"it solves, in {steps} step(s)\" [expr {{[analyze {steps}] == 0}}]\n");
            if (type == GeomTransfType.Linear)
            {
                text.Append($"check \"the tip rises ML^2/2EI = pi L ([nodeDisp {tip} 2])\" [near [nodeDisp {tip} 2] {Math.PI * L} 1e-9]\n");
                text.Append($"check \"and does not move along the beam ([nodeDisp {tip} 1])\" [near [nodeDisp {tip} 1] 0.0 1e-9]\n");
            }
            else
            {
                text.Append($"check \"the tip comes back along the beam by L ([nodeDisp {tip} 1])\" [near [nodeDisp {tip} 1] {-L} 1e-6]\n");
                text.Append($"check \"to its root's height ([nodeDisp {tip} 2])\" [near [nodeDisp {tip} 2] 0.0 1e-6]\n");
                // Straight pieces make a regular polygon, not the arc: its middle node is the polygon's
                // diameter, (L/n) / sin(pi/n), away - 3.196 for 20 pieces, against the arc's L/pi of 3.183.
                double diameter = L / Pieces / Math.Sin(Math.PI / Pieces);
                text.Append($"check \"its middle node is the {Pieces}-gon's diameter, {diameter:F4}, away ([nodeDisp {Pieces / 2 + 1} 1], [nodeDisp {Pieces / 2 + 1} 2])\" " +
                            $"[expr {{[near [nodeDisp {Pieces / 2 + 1} 1] {-L / 2} 1e-6] && [near [nodeDisp {Pieces / 2 + 1} 2] {diameter} 1e-6]}}]\n");
            }
            text.Append($"check \"the tip turns ML/EI = 2 pi ([nodeDisp {tip} 6])\" [near [nodeDisp {tip} 6] {2.0 * Math.PI} 1e-6]\n");
        }
        return text.ToString();
    }
}
