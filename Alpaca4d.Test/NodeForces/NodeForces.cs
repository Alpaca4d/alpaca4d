// Node Forces: the forces every beam brings into a node, from the section forces at its ends.
//
// Three kinds of check.
//
//   1. The graph. Beams filed under their end nodes, the ends at a node in the order of the model,
//      a node found from a point within tolerance and not beyond it, other elements counted.
//
//   2. The signs, on cases worked by hand: a cantilever's tip, a bar in tension, each end of a beam.
//
//   3. A joint OpenSees solves, recorded the way Alpaca4d records it (MPCO, section.force and
//      axialForce) and read back through Read.ForceBeamColumn. Five members of every kind Alpaca4d
//      writes meet at one node: Newton-Cotes beams with distributed loads, one with three sections,
//      a HingeRadau beam with a released end and a point load, and a truss. What NodeForces makes of
//      each end has to be the opposite of OpenSees' own globalForce, recorded beside it, and the
//      members have to balance the load on the joint and the reaction at every support.
//
// Without OpenSees (on PATH, or OPENSEES=...) the third is skipped.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Result;

class NodeForcesTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void CloseVector(Vector3d got, Vector3d want, double tol, string what)
    {
        bool ok = (got - want).Length <= tol * Math.Max(1.0, want.Length);
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got ({got.X:G6}, {got.Y:G6}, {got.Z:G6}), want ({want.X:G6}, {want.Y:G6}, {want.Z:G6})");
    }

    static int Main(string[] args)
    {
        string openSees = args.Length > 0 ? args[0] : null;

        Graph();
        Signs();
        if (!string.IsNullOrEmpty(openSees))
        {
            Solved(openSees);
            Grillage(openSees);
        }
        else
            Console.WriteLine("\nnote: OpenSees not found, the solved joint was skipped");

        Console.WriteLine(fails == 0 ? "\nall checks passed" : $"\n{fails} checks failed");
        return fails == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- a model by hand

    static ForceBeamColumn Beam(int id, int i, int j, Vector3d vecxz) =>
        new ForceBeamColumn(null, null, new GeomTransf(GeomTransfType.Linear, null, vecxz)) { Id = id, INode = i, JNode = j };

    static Model ModelOf(Dictionary<int, Point3d> nodes, List<IBeam> beams)
    {
        var model = new Model { Tollerance = 0.01 };
        foreach (var node in nodes.OrderBy(pair => pair.Key))
            model.Nodes.Add(new Node(node.Key, node.Value.X, node.Value.Y, node.Value.Z) { Ndf = 6 });
        model.Beams.AddRange(beams);
        model.Elements.AddRange(beams);
        return model;
    }

    // ---------------------------------------------------------------- 1. the graph

    static void Graph()
    {
        Console.WriteLine("\nThe graph");

        // A Warren truss girder: bottom chord 1-2-3-4, top chord 5-6-7, diagonals between. Beams
        // listed out of tag order, so the order at a node is the model's and not the tags'.
        var nodes = new Dictionary<int, Point3d>
        {
            [1] = new Point3d(0, 0, 0), [2] = new Point3d(2, 0, 0), [3] = new Point3d(4, 0, 0), [4] = new Point3d(6, 0, 0),
            [5] = new Point3d(1, 0, 1), [6] = new Point3d(3, 0, 1), [7] = new Point3d(5, 0, 1),
            [8] = new Point3d(9, 9, 9),   // a node no beam meets
        };
        var z = Vector3d.YAxis;
        var beams = new List<IBeam>
        {
            Beam(1, 1, 2, z), Beam(2, 2, 3, z), Beam(3, 3, 4, z),
            Beam(4, 5, 6, z), Beam(5, 6, 7, z),
            Beam(6, 1, 5, z), Beam(7, 5, 2, z), Beam(8, 2, 6, z), Beam(9, 6, 3, z), Beam(10, 3, 7, z), Beam(11, 7, 4, z),
        };
        var model = ModelOf(nodes, beams);

        // A link from node 2 to node 8: an element other than a beam reaching both.
        model.Elements.Add(new Link(new Line(nodes[2], nodes[8]), new List<IUniaxialMaterial>(), new List<int>()) { Id = 12, INode = 2, JNode = 8 });

        var graph = NodeGraph.Of(model);

        Check(graph.Degree(1) == 2 && graph.Degree(2) == 4 && graph.Degree(5) == 3 && graph.Degree(6) == 4 && graph.Degree(8) == 0,
              "degrees: 2 at the support corner, 4 at a bottom node, 3 at the top end, 4 mid-top, 0 at the bare node");

        var at2 = graph.EndsAt(2).ToList();
        Check(at2.Select(end => end.Beam).SequenceEqual(new[] { 0, 1, 6, 7 }),
              $"node 2's beams in the model's order: {string.Join(", ", at2.Select(end => end.Beam))}, want 0, 1, 6, 7");
        Check(at2.Select(end => end.AtJ).SequenceEqual(new[] { true, false, true, false }),
              "node 2 is the J end of beams 1 and 7 and the I end of beams 2 and 8");

        Check(graph.BeamNodes.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7 }), "every node a beam meets, smallest first, and not the bare one");
        Check(graph.Has(8) && !graph.Has(9) && !graph.Has(0) && !graph.Has(-1), "Has: the bare node is there, tags past the last or below one are not");
        Check(graph.Degree(9) == 0 && graph.Degree(-3) == 0 && !graph.EndsAt(42).Any(), "a tag out of range has no beams, rather than throwing");

        Check(graph.OthersAt(2) == 1 && graph.OthersAt(8) == 1 && graph.OthersAt(3) == 0, "the link counts at both its nodes and nowhere else");

        Check(graph.NodeAt(new Point3d(3.004, 0.003, 1.0), 0.01) == 6, "a point within tolerance finds its node");
        Check(graph.NodeAt(new Point3d(3.02, 0, 1.0), 0.01) == 0, "a point beyond tolerance finds none");
        Check(graph.NodeAt(new Point3d(9, 9, 9), 0.01) == 0, "a point on a node no beam meets finds none");
        Check(graph.NodeAt(new Point3d(3.02, 0, 1.0), 0.05) == 6, "a looser tolerance asked for afterwards is the one used");
        Check(graph.NodeAt(new Point3d(4, 0, 0), 0.0) == 3, "a tolerance of zero falls back on 0.01 rather than exact equality");
        Check(graph.NodeAt(Point3d.Unset, 0.01) == 0, "an unset point finds none");

        // Time: a grid of 300 x 300 bays in the plane, every node meeting four beams.
        int n = 300;
        var bigNodes = new Dictionary<int, Point3d>();
        var bigBeams = new List<IBeam>();
        Func<int, int, int> tag = (i, j) => j * (n + 1) + i + 1;
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
                bigNodes[tag(i, j)] = new Point3d(i, j, 0);
        int id = 1;
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                if (i < n) bigBeams.Add(Beam(id++, tag(i, j), tag(i + 1, j), Vector3d.ZAxis));
                if (j < n) bigBeams.Add(Beam(id++, tag(i, j), tag(i, j + 1), Vector3d.ZAxis));
            }
        var big = ModelOf(bigNodes, bigBeams);

        var watch = Stopwatch.StartNew();
        var bigGraph = NodeGraph.Of(big);
        long build = watch.ElapsedMilliseconds;
        watch.Restart();
        long ends = 0;
        foreach (int node in bigGraph.BeamNodes)
            ends += bigGraph.EndsAt(node).Count();
        long walk = watch.ElapsedMilliseconds;
        watch.Restart();
        int found = 0;
        for (int i = 0; i < 1000; i++)
            if (bigGraph.NodeAt(new Point3d(i % n, i / 7 % n, 0), big.Tollerance) > 0) found++;
        long points = watch.ElapsedMilliseconds;

        Check(ends == 2L * bigBeams.Count, $"{bigBeams.Count} beams and {bigNodes.Count} nodes: every beam's two ends found once");
        Check(found == 1000, "a thousand points each find their node");
        Console.WriteLine($"         built in {build} ms, every node's beams walked in {walk} ms, 1000 points found in {points} ms");
    }

    // ---------------------------------------------------------------- 2. the signs

    static void Signs()
    {
        Console.WriteLine("\nThe signs, by hand");

        // A cantilever along x, fixed at I, a load P down -y at J. Its section forces at I (Beam
        // Forces' convention): Vy = +P, Mz = -P L. At the fixed node the beam pulls down with P and
        // twists with the moment -P L about z, which the support resists.
        double P = 10, L = 3;
        NodeForces.OnNode(new Vector3d(L, 0, 0), Vector3d.ZAxis, false, 0, P, 0, 0, 0, -P * L, out var force, out var moment);
        CloseVector(force, new Vector3d(0, -P, 0), 1e-12, "cantilever, fixed end: the beam pulls the support down with P");
        CloseVector(moment, new Vector3d(0, 0, -P * L), 1e-12, "cantilever, fixed end: and turns it with -P L about z");

        // At the tip, J: section Vy = +P, Mz = 0. The beam holds the loaded node up with P.
        NodeForces.OnNode(new Vector3d(L, 0, 0), Vector3d.ZAxis, true, 0, P, 0, 0, 0, 0, out force, out moment);
        CloseVector(force, new Vector3d(0, P, 0), 1e-12, "cantilever, tip: the beam holds the node up against the load");

        // A bar in tension, N = 5, from (0,0,0) to (3,4,0): it pulls each end node towards the other.
        var axis = new Vector3d(3, 4, 0);
        NodeForces.OnNode(axis, Vector3d.ZAxis, false, 5, 0, 0, 0, 0, 0, out force, out _);
        CloseVector(force, new Vector3d(3, 4, 0), 1e-12, "tension pulls the I node along the bar, towards J");
        NodeForces.OnNode(axis, Vector3d.ZAxis, true, 5, 0, 0, 0, 0, 0, out force, out _);
        CloseVector(force, new Vector3d(-3, -4, 0), 1e-12, "and the J node back towards I");

        // A beam along y with local z up: local y is z × x = -X. Vz is dMy/dx and its force on the I
        // node is along +z; Vy is dMz/dx and its force on the I node is along -y local, so +X.
        NodeForces.OnNode(new Vector3d(0, 2, 0), new Vector3d(0, 0.1, 1), false, 0, 1, 2, 0, 0, 0, out force, out _);
        CloseVector(force, new Vector3d(1, 0, 2), 1e-12, "a beam along y: local z squared off the axis, local y = z × x");

        // Torsion, positive at J about +x: the beam puts -T about its axis on its J node.
        NodeForces.OnNode(new Vector3d(0, 0, 4), Vector3d.XAxis, true, 0, 0, 0, 7, 0, 0, out _, out moment);
        CloseVector(moment, new Vector3d(0, 0, -7), 1e-12, "torsion at the J end acts back on the node");
    }

    // ---------------------------------------------------------------- 3. a joint, solved

    static readonly Dictionary<int, Point3d> JointNodes = new Dictionary<int, Point3d>
    {
        [1] = new Point3d(0, 0, 0),
        [2] = new Point3d(-3, 0, 0),
        [3] = new Point3d(2, 1, 0.5),
        [4] = new Point3d(0.3, -0.2, 3),
        [5] = new Point3d(1, -2, -1),
        [6] = new Point3d(0, 2.5, -0.5),
    };

    static readonly Vector3d NodalLoad = new Vector3d(10, -20, 15), NodalMoment = new Vector3d(3, -4, 5);

    const string Deck = @"wipe
model basic -ndm 3 -ndf 6
node 1 0 0 0
node 2 -3 0 0
node 3 2 1 0.5
node 4 0.3 -0.2 3
node 5 1 -2 -1
node 6 0 2.5 -0.5
foreach n {2 3 4 5 6} { fix $n 1 1 1 1 1 1 }
section Elastic 1 210e6 0.01 2e-4 1e-4 80e6 5e-5 0.83 0.83
section Elastic 2 210e6 0.01 2e-10 1e-10 80e6 5e-5 0.83 0.83
uniaxialMaterial Elastic 10 210e6
geomTransf Linear 1 0 0 1
geomTransf Linear 2 0 0 1
geomTransf Linear 3 1 0 0
geomTransf Linear 4 0 0 1
element forceBeamColumn 1 2 1 1 NewtonCotes 1 5
element forceBeamColumn 2 1 3 2 NewtonCotes 1 5
element forceBeamColumn 3 1 4 3 NewtonCotes 1 3
element forceBeamColumn 4 5 1 4 HingeRadau 1 0.1 2 0.1 1
element corotTruss 5 1 6 0.002 10
recorder mpco joint.mpco -N displacement reactionForce reactionMoment -E section.force axialForce
foreach e {1 2 3 4 5} { recorder Element -file glob_$e.out -ele $e globalForce }
foreach e {1 2 3 4} { recorder Element -file loc_$e.out -ele $e localForce }
foreach n {2 3 4 5 6} { recorder Node -file react_$n.out -node $n -dof 1 2 3 4 5 6 reaction }
timeSeries Linear 1
pattern Plain 1 1 {
  load 1 10 -20 15 3 -4 5
  eleLoad -ele 2 -type -beamUniform -7 4 2
  eleLoad -ele 1 -type -beamUniform 3 -5 1
  eleLoad -ele 4 -type -beamPoint 2 -1 0.4 1.5
}
system UmfPack
constraints Plain
numberer RCM
test NormDispIncr 1e-12 20
algorithm Newton
integrator LoadControl 0.5
analysis Static
analyze 2
wipe
";

    /// <summary>
    /// A grillage of 20 x 20 bays, solved twice into the same file: once with its beams numbered
    /// from 1 along x, and once in reverse from 1001. Reading the four beams at its middle row by row
    /// has to give what reading them all gives, and the second run has to be read as itself: with the
    /// element tags kept from the first, none of its beams would be found.
    /// </summary>
    static void Grillage(string openSees)
    {
        Console.WriteLine("\nA grillage solved twice into one file");
        const int n = 20;
        Func<int, int, int> tag = (i, j) => j * (n + 1) + i + 1;
        int middle = tag(n / 2, n / 2);

        foreach (bool reversed in new[] { false, true })
        {
            var ends = new List<(int i, int j)>();
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    if (i < n) ends.Add((tag(i, j), tag(i + 1, j)));
                    if (j < n) ends.Add((tag(i, j), tag(i, j + 1)));
                }
            if (reversed) ends.Reverse();

            var deck = new System.Text.StringBuilder("wipe\nmodel basic -ndm 3 -ndf 6\n");
            var nodes = new Dictionary<int, Point3d>();
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    nodes[tag(i, j)] = new Point3d(i, j, 0);
                    deck.Append($"node {tag(i, j)} {i} {j} 0\n");
                }
            foreach (int corner in new[] { tag(0, 0), tag(n, 0), tag(0, n), tag(n, n) })
                deck.Append($"fix {corner} 1 1 1 1 1 1\n");
            deck.Append("section Elastic 1 3e7 0.08 1e-3 3e-4 1.2e7 1e-3 0.83 0.83\ngeomTransf Linear 1 0 0 1\n");
            var beams = new List<IBeam>();
            int first = reversed ? 1001 : 1;
            for (int k = 0; k < ends.Count; k++)
            {
                deck.Append($"element forceBeamColumn {k + first} {ends[k].i} {ends[k].j} 1 NewtonCotes 1 5\n");
                beams.Add(Beam(k + first, ends[k].i, ends[k].j, Vector3d.ZAxis));
            }
            deck.Append($"recorder mpco grid.mpco -N displacement -E section.force\ntimeSeries Linear 1\n");
            deck.Append($"pattern Plain 1 1 {{ load {middle} 0 0 -100 0 0 0 }}\n");
            deck.Append("system UmfPack\nconstraints Plain\nnumberer RCM\ntest NormDispIncr 1e-10 10\nalgorithm Newton\nintegrator LoadControl 1.0\nanalysis Static\nanalyze 1\nwipe\n");
            File.WriteAllText("grid.tcl", deck.ToString());

            var run = Process.Start(new ProcessStartInfo(openSees, "grid.tcl")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            run.StandardOutput.ReadToEnd(); run.StandardError.ReadToEnd(); run.WaitForExit();
            Check(run.ExitCode == 0, $"{(reversed ? "numbered in reverse from 1001" : "numbered from 1 along x")}: {beams.Count} beams solve");

            var model = ModelOf(nodes, beams);
            model.Recorders.Add(new Recorder { FileName = Path.GetFullPath("grid.mpco") });
            var graph = NodeGraph.Of(model);

            var full = Read.ForceBeamColumn(model, 0);
            var some = Read.ForceBeamColumn(model, 0, null, graph.BeamsAt(new[] { middle }));
            var fromFull = NodeForces.At(graph, middle, beams, full, null, out _);
            var fromSome = NodeForces.At(graph, middle, beams, some, null, out int unread);
            bool same = unread == 0 && fromSome.Count == 4 && fromSome.Zip(fromFull, (a, b) => a.Force == b.Force && a.Moment == b.Moment && a.N == b.N && a.Mz == b.Mz).All(x => x);
            Check(same, "the four beams at the middle, read row by row, are what reading every beam gives, bit for bit");
            var sum = fromSome.Aggregate(Vector3d.Zero, (total, end) => total + end.Force);
            CloseVector(sum, new Vector3d(0, 0, 100), 1e-8, "and they hold up the 100 kN on it");
        }
    }

    static double[][] Rows(string file) =>
        File.ReadAllLines(file).Where(line => line.Trim().Length > 0)
            .Select(line => line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray())
            .ToArray();

    static void Solved(string openSees)
    {
        Console.WriteLine("\nA joint solved by OpenSees");

        string folder = Directory.GetCurrentDirectory();
        File.WriteAllText(Path.Combine(folder, "joint.tcl"), Deck);
        var run = Process.Start(new ProcessStartInfo(openSees, "joint.tcl")
        {
            WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        });
        run.StandardOutput.ReadToEnd();
        string error = run.StandardError.ReadToEnd();
        run.WaitForExit();
        Check(run.ExitCode == 0 && File.Exists(Path.Combine(folder, "joint.mpco")), "joint.tcl solves" + (run.ExitCode == 0 ? "" : ": " + error));
        if (run.ExitCode != 0) return;

        // The model as Assemble would leave it: tags in the deck's order, end nodes and axes.
        var beams = new List<IBeam>
        {
            Beam(1, 2, 1, new Vector3d(0, 0, 1)),
            Beam(2, 1, 3, new Vector3d(0, 0, 1)),
            Beam(3, 1, 4, new Vector3d(1, 0, 0)),
            Beam(4, 5, 1, new Vector3d(0, 0, 1)),
            new Truss(null, null, new Vector3d(0, 0, 1)) { Id = 5, INode = 1, JNode = 6 },
        };
        var model = ModelOf(JointNodes, beams);
        model.Recorders.Add(new Recorder { FileName = Path.Combine(folder, "joint.mpco") });
        var graph = NodeGraph.Of(model);

        Check(Read.StepCount(model) == 2, "two steps recorded");
        Check(graph.Degree(1) == 5, "five members meet at the joint");

        for (int step = 0; step < 2; step++)
        {
            double factor = 0.5 * (step + 1);
            Console.WriteLine($"\n  step {step}, load factor {factor}");
            var forces = Read.ForceBeamColumn(model, step);
            var displacement = NodeForces.Displacements(model, step);
            Check(displacement != null && displacement.Length == 7 && displacement[1].Length > 0,
                  "the model has a truss, so the displacements are read, by tag");

            // Every member end against OpenSees' globalForce, the force the node puts on the
            // element: NodeForces gives what the element puts on the node, the opposite.
            double worst = 0;
            foreach (int node in graph.BeamNodes)
            {
                foreach (var end in NodeForces.At(graph, node, beams, forces, displacement, out int unread))
                {
                    var g = Rows($"glob_{end.Beam + 1}.out")[step];
                    int k = end.AtJ ? g.Length / 2 : 0;
                    var wantForce = -new Vector3d(g[k], g[k + 1], g[k + 2]);
                    var wantMoment = g.Length == 12 ? -new Vector3d(g[k + 3], g[k + 4], g[k + 5]) : Vector3d.Zero;
                    worst = Math.Max(worst, Math.Max((end.Force - wantForce).Length, (end.Moment - wantMoment).Length));
                }
            }
            Check(worst < 1e-4, $"every member end matches OpenSees' globalForce, to {worst:G2} (text recorders print six figures)");

            // N to Mz against OpenSees' localForce, the end forces on the element in its own axes,
            // (N, Vy, Vz, T, My, Mz) at I then at J. On the element at I they are (-N, Vy, -Vz, -Mx,
            // -My, -Mz) in Beam Forces' signs, and at J (N, -Vy, Vz, Mx, My, Mz): the opposite of what
            // the element puts on its node, (N, -Vy, Vz; Mx, My, Mz) at I and minus that at J. The
            // four beams lie four different ways, one with its local z set along global x.
            double worstLocal = 0;
            foreach (int node in graph.BeamNodes)
            {
                foreach (var end in NodeForces.At(graph, node, beams, forces, displacement, out _))
                {
                    if (end.Beam == 4) continue;   // the truss answers localForce with N alone
                    var l = Rows($"loc_{end.Beam + 1}.out")[step];
                    double[] want = end.AtJ
                        ? new[] { l[6], -l[7], l[8], l[9], l[10], l[11] }
                        : new[] { -l[0], l[1], -l[2], -l[3], -l[4], -l[5] };
                    double[] got = { end.N, end.Vy, end.Vz, end.Mx, end.My, end.Mz };
                    worstLocal = Math.Max(worstLocal, got.Zip(want, (a, b) => Math.Abs(a - b)).Max());
                }
            }
            Check(worstLocal < 1e-4, $"N, Vy, Vz, Mx, My, Mz at every member end match OpenSees' localForce in the member's own axes, to {worstLocal:G2}");

            var atJoint = NodeForces.At(graph, 1, beams, forces, displacement, out int missing);
            Check(missing == 0 && atJoint.Count == 5, "all five members read at the joint");

            var sumForce = atJoint.Aggregate(Vector3d.Zero, (sum, end) => sum + end.Force);
            var sumMoment = atJoint.Aggregate(Vector3d.Zero, (sum, end) => sum + end.Moment);
            CloseVector(sumForce + factor * NodalLoad, Vector3d.Zero, 1e-8, "the members and the load on the joint balance, forces");
            CloseVector(sumMoment + factor * NodalMoment, Vector3d.Zero, 1e-8, "and moments");

            // Taken along the bar where it was drawn, the truss's force misses by its turn through
            // the displacement: small here, but a joint that only nearly balances.
            var undeformed = NodeForces.At(graph, 1, beams, forces, null, out _);
            var undeformedSum = undeformed.Aggregate(Vector3d.Zero, (sum, end) => sum + end.Force) + factor * NodalLoad;
            Check(undeformedSum.Length > 1e-7, $"along the undeformed bar the joint misses by {undeformedSum.Length:G2}, so the displaced one is what balances it");

            var truss = atJoint.Single(end => end.Beam == 4);
            Check(truss.Vy == 0 && truss.Mz == 0 && truss.Moment.Length == 0, "the truss brings an axial force and nothing else");
            var trussAxis = (JointNodes[6] + displacement[6]) - (JointNodes[1] + displacement[1]);
            trussAxis /= trussAxis.Length;
            CloseVector(truss.Force, truss.N * trussAxis, 1e-12, "its force on the joint is N along the displaced bar, towards its far end in tension");

            var hinged = NodeForces.At(graph, 1, beams, forces, displacement, out _).Single(end => end.Beam == 3);
            Check(hinged.AtJ && Math.Abs(hinged.My) < 1e-3 && Math.Abs(hinged.Mz) < 1e-3,
                  $"the HingeRadau beam's released end, at the joint, carries no bending: My {hinged.My:G2}, Mz {hinged.Mz:G2}");

            // At every support only one member arrives, so it and the reaction balance.
            foreach (int support in new[] { 2, 3, 4, 5, 6 })
            {
                var ends = NodeForces.At(graph, support, beams, forces, displacement, out _);
                var r = Rows($"react_{support}.out")[step];
                var f = ends.Aggregate(Vector3d.Zero, (sum, end) => sum + end.Force) + new Vector3d(r[0], r[1], r[2]);
                var m = ends.Aggregate(Vector3d.Zero, (sum, end) => sum + end.Moment) + new Vector3d(r[3], r[4], r[5]);
                Check(f.Length < 1e-4 && m.Length < 1e-4, $"node {support}: the member and the reaction balance, to {Math.Max(f.Length, m.Length):G2}");
            }
        }

        // A beam the file holds nothing for is counted, not read as zeros.
        var extra = new List<IBeam>(beams) { Beam(99, 1, 2, new Vector3d(0, 0, 1)) };
        var withExtra = ModelOf(JointNodes, extra);
        withExtra.Recorders.Add(model.Recorders[0]);
        var extraGraph = NodeGraph.Of(withExtra);
        var read = NodeForces.At(extraGraph, 1, extra, Read.ForceBeamColumn(withExtra, 1), null, out int notRead);
        Check(read.Count == 5 && notRead == 1, "a member with no recorded forces is left out and counted");

        // Reading only the members at a node gives them exactly, and the rest nothing.
        var full = Read.ForceBeamColumn(model, 1);
        var at4 = graph.BeamsAt(new[] { 4 });
        var some = Read.ForceBeamColumn(model, 1, null, at4);
        bool asked = at4.All(b => some.n[b].SequenceEqual(full.n[b]) && some.mz[b].SequenceEqual(full.mz[b]) && some.vy[b].SequenceEqual(full.vy[b])
                                  && some.my[b].SequenceEqual(full.my[b]) && some.vz[b].SequenceEqual(full.vz[b]) && some.t[b].SequenceEqual(full.t[b]));
        bool others = Enumerable.Range(0, beams.Count).Where(b => !at4.Contains(b)).All(b => some.n[b].Count == 0 && some.mz[b].Count == 0);
        Check(at4.SetEquals(new[] { 2 }) && asked && others, "reading only the beams at node 4 gives that one beam bit for bit, and the others empty");
        var trussOnly = Read.ForceBeamColumn(model, 1, null, new[] { 4 });
        Check(trussOnly.n[4].SequenceEqual(full.n[4]) && trussOnly.n[0].Count == 0, "and the same for a truss");
    }
}
