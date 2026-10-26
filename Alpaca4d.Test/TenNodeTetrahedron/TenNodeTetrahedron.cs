// The ten node tetrahedron: what Alpaca4d writes for it, what it reads back from it, and which
// OpenSees it refuses.
//
//   1. The numbers Alpaca4d keeps about the element - the order of its edge nodes, how its own
//      weight is shared among its nodes, where its four integration points sit - are checked
//      against what OpenSees does with it, on decks next to this file that pin each one down:
//      pure bending for the integration points, a body force for the shares, a patch test for
//      the stresses and reactions.
//   2. The cantilever shows what the element is for: a mesh that leaves the four node tetrahedron
//      at a quarter of the deflection gets within 0.2% of beam theory.
//   3. OpenSees 3.8.0's TenNodeTetrahedron is six times too soft. Alpaca4d writes
//      TenNodeTetrahedronStd, which no OpenSees without the fixes knows, and Run Analysis explains
//      the error such an OpenSees stops with.
//
// The decks need an OpenSees that knows TenNodeTetrahedronStd (OPENSEES_TET10, or
// /Applications/OpenSeesLatest/bin/OpenSees); without one only the checks that need no solver
// run. Point3d and Vector3d are managed, so no RhinoCommon native library is needed either.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Result;

/// <summary>A solid that is nothing but a tag and a class: all the reader asks an element for.</summary>
class TaggedSolid : IBrick
{
    public int? Id { get; set; }
    public string ElementId { get; set; }
    public Mesh Mesh { get; set; }
    public IMultiDimensionMaterial Material { get; set; }
    public ElementType Type => ElementType.Brick;
    public ElementClass ElementClass { get; set; }
    public List<int?> IndexNodes { get; set; }
    public Color Color { get; set; }
    public int Ndf => 3;
    public Plane LocalPlane => Plane.Unset;
    public void SetTags() { }
    public void SetTopologyRTree(Model model) { }
    public string WriteTcl() => "";
}

class TenNodeTetrahedronTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Close(double got, double want, string what, double tolerance = 1e-6)
    {
        bool ok = Math.Abs(got - want) <= tolerance * Math.Max(1.0, Math.Abs(want));
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {got:G8}, want {want:G8}");
    }

    // The element of element.tcl: corners tilted off every axis, edge nodes at the midpoints.
    static readonly Point3d[] Corners =
    {
        new Point3d(0.0, 0.0, 0.0), new Point3d(2.0, 0.2, 0.1),
        new Point3d(0.3, 1.5, -0.2), new Point3d(0.4, 0.3, 1.8),
    };

    static List<Point3d> TenNodes(IList<Point3d> corners)
    {
        var nodes = corners.ToList();
        foreach (var edge in Utils.TetrahedronEdges)
            nodes.Add(0.5 * (corners[edge[0]] + corners[edge[1]]));
        return nodes;
    }

    static double Volume(IList<Point3d> c)
        => (c[1] - c[0]) * Vector3d.CrossProduct(c[2] - c[0], c[3] - c[0]) / 6.0;

    static Model ModelOf(string file, params int[] tags)
    {
        return new Model
        {
            Recorders = new List<IRecorder> { new Recorder { FileName = file } },
            Bricks = tags.Select(t => (IBrick)new TaggedSolid { Id = t, ElementClass = ElementClass.TenNodeTetrahedron }).ToList(),
        };
    }

    /// <summary>
    /// The lines a deck printed, by their first word - and the node number after it, for a line
    /// about one node - each with the numbers that follow.
    /// </summary>
    static Dictionary<string, double[]> Lines(string file)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var lines = new Dictionary<string, double[]>();
        foreach (var line in File.ReadAllLines(file))
        {
            var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 2) continue;
            bool perNode = t.Length > 2 && int.TryParse(t[1], out _);
            lines[perNode ? t[0] + " " + t[1] : t[0]] = t.Skip(perNode ? 2 : 1)
                .Select(v => double.TryParse(v, System.Globalization.NumberStyles.Float, culture, out double d) ? d : double.NaN)
                .Where(d => !double.IsNaN(d)).ToArray();
        }
        return lines;
    }

    // ---------------------------------------------------------------- what needs no solver

    static void Constants()
    {
        Console.WriteLine("\nThe numbers kept about the element\n");

        var standard = new[] { "0-1", "1-2", "2-0", "0-3", "1-3", "2-3" };
        Check(Utils.TetrahedronEdges.Select(e => $"{e[0]}-{e[1]}").SequenceEqual(standard),
              "edge nodes in TenNodeTetrahedronStd order: 1-2, 2-3, 3-1, 1-4, 2-4, 3-4");

        var shares = TenNodeTetrahedron.BodyForceShares;
        Close(shares.Sum(), 1.0, "the body force shares add up to the whole weight", 1e-14);
        Check(shares.Take(4).All(s => s == -1.0 / 20.0) && shares.Skip(4).All(s => s == 1.0 / 5.0),
              "-1/20 at each corner, 1/5 at each edge node");

        Console.WriteLine("\nFour integration points to ten nodes, on a field it can carry\n");

        // f = sum c_i L_i sampled where TenNodeTetrahedron.cpp puts its points: point k at alpha
        // along corner k and beta along the others.
        double alpha = (5.0 + 3.0 * Math.Sqrt(5.0)) / 20.0, beta = (5.0 - Math.Sqrt(5.0)) / 20.0;
        var c = new[] { 3.0, -1.0, 4.0, 1.5 };
        var atPoints = Enumerable.Range(0, 4)
            .Select(k => Enumerable.Range(0, 4).Sum(i => c[i] * (i == k ? alpha : beta))).ToList();
        var atNodes = TenNodeTetrahedron.AtNodes(atPoints);
        for (int i = 0; i < 4; i++)
            Close(atNodes[i], c[i], $"corner {i + 1}", 1e-12);
        for (int e = 0; e < 6; e++)
        {
            var edge = Utils.TetrahedronEdges[e];
            Close(atNodes[4 + e], 0.5 * (c[edge[0]] + c[edge[1]]), $"edge node {5 + e}", 1e-12);
        }

        Console.WriteLine("\nThe element's axes are those of its corners\n");

        var ten = Utils.SolidFrame(TenNodes(Corners));
        var four = Utils.SolidFrame(Corners);
        Check((ten.X - four.X).Length < 1e-15 && (ten.Y - four.Y).Length < 1e-15 && (ten.Z - four.Z).Length < 1e-15,
              "SolidFrame of the ten nodes is SolidFrame of the four corners");
        Check(Math.Abs(Utils.SolidAxes(TenNodes(Corners)).Origin.DistanceTo(Utils.SolidAxes(Corners).Origin)) < 1e-15,
              "and its plane sits at the same centre");
    }

    static void TooOld(string log)
    {
        Console.WriteLine("\nAn OpenSees without the fixes\n");

        Check(TenNodeTetrahedron.SolverTooOld("WARNING something else\nanalyze failed") == null,
              "an ordinary failure gets no word about the element");
        Check(TenNodeTetrahedron.SolverTooOld(null) == null, "nor does no log at all");

        if (log == null)
        {
            Console.WriteLine("  note: no OpenSees that lacks the element was found, so its own log is not checked");
            return;
        }

        var said = TenNodeTetrahedron.SolverTooOld(log);
        Check(said != null && said.Contains("six times too soft"),
              $"the log of an OpenSees that does not know {TenNodeTetrahedron.Command} is explained: {said?.Split('.')[0]}");
    }

    // ------------------------------------------------------------------ what the solver says

    static void Patch(string dir)
    {
        Console.WriteLine("\nThe patch test: a uniform strain, read back\n");

        var model = ModelOf(Path.Combine(dir, "patch.mpco"), 1);
        var mean = Read.TenNodeTetrahedronStress(model, 0);
        var want = new[] { 1.2, 0.4, 0.4, 0.0, 0.0, 0.0 };
        var got = new[] { mean.Item1[0], mean.Item2[0], mean.Item3[0], mean.Item4[0], mean.Item5[0], mean.Item6[0] };
        var names = new[] { "sigma11", "sigma22", "sigma33", "sigma12", "sigma23", "sigma13" };
        for (int i = 0; i < 6; i++)
            Close(got[i], want[i], $"Brick Stresses' {names[i]}", 1e-9);

        var nodes = Read.TenNodeTetrahedronStressAtNodes(model, 0)[0];
        Check(nodes.Length == 10 && nodes.All(n => Enumerable.Range(0, 6).All(i => Math.Abs(n[i] - want[i]) < 1e-9)),
              "the same at all ten nodes");

        Close(Lines(Path.Combine(dir, "patch.txt"))["REACTION_X0"][0], -0.4,
              "the nodes at x = 0 take -0.4: the whole volume is integrated, not a sixth of it", 1e-9);
    }

    static void Bending(string dir)
    {
        Console.WriteLine("\nPure bending: where the four integration points sit\n");

        const double E = 1000.0, kappa = 0.01;
        var nodes = TenNodes(Corners);
        var model = ModelOf(Path.Combine(dir, "bending.mpco"), 1);

        var centre = nodes.Take(4).Aggregate(Point3d.Origin, (s, p) => s + p) / 4.0;
        Close(Read.TenNodeTetrahedronStress(model, 0).Item1[0], -E * kappa * centre.Z,
              "the mean of the four points is the stress at the centre", 1e-9);

        var atNodes = Read.TenNodeTetrahedronStressAtNodes(model, 0)[0];
        for (int n = 0; n < 10; n++)
            Close(atNodes[n][0], -E * kappa * nodes[n].Z, $"sigma11 at node {n + 1}, z = {nodes[n].Z:F2}", 1e-9);
        Check(atNodes.All(s => s.Skip(1).All(v => Math.Abs(v) < 1e-9)), "and nothing else anywhere");
    }

    static void BodyForce(string dir)
    {
        Console.WriteLine("\nA body force: how the element shares out its weight\n");

        var reactions = Lines(Path.Combine(dir, "bodyforce.txt"));
        double weight = 10.0 * Volume(Corners);
        for (int n = 1; n <= 10; n++)
            Close(reactions[$"REACTION {n}"][2], weight * TenNodeTetrahedron.BodyForceShares[n - 1],
                  $"node {n} holds {(n <= 4 ? "-1/20" : "1/5")} of the weight", 1e-9);
    }

    static void Cantilever(string dir)
    {
        Console.WriteLine("\nA cantilever of 60 elements, against beam theory's 0.4024\n");

        var tet10 = Lines(Path.Combine(dir, "cantilever10.txt"))["TIP_UZ"][0];
        var tet4 = Lines(Path.Combine(dir, "cantilever4.txt"))["TIP_UZ"][0];

        Close(-tet10, 0.4024, "ten node tetrahedra", 2e-3);
        Check(-tet4 < 0.3 * 0.4024, $"four node tetrahedra on the same cells reach {-tet4 / 0.4024:P0} of it");
    }

    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        bool solved = File.Exists(Path.Combine(dir, "patch.mpco"));

        Constants();
        TooOld(File.Exists(Path.Combine(dir, "old.log")) ? File.ReadAllText(Path.Combine(dir, "old.log")) : null);

        if (solved)
        {
            Patch(dir);
            Bending(dir);
            BodyForce(dir);
            Cantilever(dir);
        }
        else
        {
            Console.WriteLine("\nnote: no OpenSees that knows TenNodeTetrahedronStd, so the decks were not solved");
        }

        Console.WriteLine(fails == 0 ? "\nAll checks passed." : $"\n{fails} check(s) FAILED.");
        return fails == 0 ? 0 : 1;
    }
}
