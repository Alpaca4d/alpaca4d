// The two spring elements and the equalDOF tie.
//
//   Link                twoNodeLink, a spring between two nodes that are apart.
//   ZeroLengthSpring    zeroLength, a spring between a node and the ground.
//   EqualDOF            a tie between chosen degrees of freedom of two nodes.
//
// Two halves. The first checks the tcl each object writes, on objects with their node tags set
// by hand the way Model.Assemble would set them - Assemble itself needs an RTree, whose native
// library only loads inside Rhino. The second writes complete decks around that same tcl and
// leaves them for OpenSees to solve, so what the solver is handed is the string these classes
// produce rather than a transcription of it. The decks check themselves against closed form.
//
// Worth knowing, and the reason the orientation is written the way it is:
//
//   - twoNodeLink with no -orient at all does not warn, it exits the process: "invalid
//     orientation vectors". So one is always written.
//   - Given six numbers it takes the first three as the local x and prints a warning for every
//     element saying it is using them instead of the nodes - even when the two agree exactly.
//     Given three it takes x from the nodes and the three as the y hint, and the frame comes out
//     identical with nothing printed. So the three-number form is used whenever the frame really
//     does run along the link, which is nearly always.
//   - The shear springs act at mid-length by default (-shearDist 0.5), so a shear force on a
//     link of finite length also turns the nodes. That is not a rounding error, it is what makes
//     two offset shells act compositely, and deck A measures it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.Constraints;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Material;

class LinkTest
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

    /// <summary>A spring of stiffness k, written the way Alpaca4d writes an elastic uniaxial material.</summary>
    static UniaxialMaterialElastic Spring(int id, double k)
    {
        var material = new UniaxialMaterialElastic(null, k, k, 0.0, k, 0.0, null);
        material.Id = id;
        return material;
    }

    static List<IUniaxialMaterial> Springs(params UniaxialMaterialElastic[] materials)
    {
        return materials.Cast<IUniaxialMaterial>().ToList();
    }

    static List<int> Dirs(params int[] directions)
    {
        return directions.ToList();
    }

    static string Failed(Action action)
    {
        try { action(); return null; }
        catch (Exception e) { return e.Message; }
    }

    static void Main(string[] args)
    {
        var outputDirectory = args.Length > 0 ? args[0] : ".";

        Section("Link - the element line");
        {
            var k = Spring(1, 1000.0);
            var link = new Alpaca4d.Element.Link(
                new Line(new Point3d(0, 0, 0), new Point3d(0, 0, 1)),
                Springs(k, k, k), Dirs(1, 2, 3));
            link.Id = 7;
            link.INode = 3;
            link.JNode = 4;

            var tcl = link.WriteTcl().Trim();
            Console.WriteLine($"        {tcl}");

            Check(tcl.StartsWith("element twoNodeLink 7 3 4 "), "tag and both nodes, in order");
            Check(tcl.Contains(" -mat 1 1 1 "), "one material tag per direction");
            Check(tcl.Contains(" -dir 1 2 3 "), "the directions, in the order given");
            Check(tcl.Contains(" -orient "), "an orientation is always written");
            Check(!tcl.Contains(" -mass "), "no -mass: a spring is a stiffness, weight is a Mass Point");

            // Local x runs along the link, so the y hint alone is enough and OpenSees stays quiet.
            var orient = tcl.Substring(tcl.IndexOf("-orient ") + 8).Trim().Split(' ');
            Check(orient.Length == 3, $"three numbers after -orient, not six (got {orient.Length})");
        }

        Section("Link - the local frame");
        {
            // Along global Z: local x is the link, and the two cross directions are whatever is
            // square to it. What matters is that they are square to it and to each other.
            var k = Spring(1, 1.0);
            var vertical = new Alpaca4d.Element.Link(
                new Line(new Point3d(0, 0, 0), new Point3d(0, 0, 1)),
                Springs(k), Dirs(1));

            var x = vertical.LocalX;
            var y = vertical.LocalY;
            var z = Vector3d.CrossProduct(x, y);

            Check(Math.Abs(x.Z - 1.0) < 1e-12, "local x runs along the link");
            Check(Math.Abs(x * y) < 1e-12, "local y is square to local x");
            Check(Math.Abs(y.Length - 1.0) < 1e-12, "local y is a unit vector");
            Check(Math.Abs(z.Length - 1.0) < 1e-12, "the frame is right handed and orthonormal");

            var horizontal = new Alpaca4d.Element.Link(
                new Line(new Point3d(0, 0, 0), new Point3d(2, 0, 0)),
                Springs(k), Dirs(1));
            Check(Math.Abs(horizontal.LocalX.X - 1.0) < 1e-12, "and along the link when the link runs on X");
            Check(Math.Abs(horizontal.LocalX * horizontal.LocalY) < 1e-12, "square there too");

            // A plane given on the input wins, and then the six-number form is needed because the
            // frame no longer agrees with the line.
            var across = new Alpaca4d.Element.Link(
                new Line(new Point3d(0, 0, 0), new Point3d(0, 0, 1)),
                Springs(k), Dirs(1), Plane.WorldXY);
            across.Id = 1; across.INode = 1; across.JNode = 2;

            Check(Math.Abs(across.LocalX.X - 1.0) < 1e-12, "a plane on the input sets the frame");

            var orient = across.WriteTcl().Trim();
            orient = orient.Substring(orient.IndexOf("-orient ") + 8).Trim();
            Check(orient.Split(' ').Length == 6, "six numbers when the frame is not along the link");
        }

        Section("Link - what it refuses");
        {
            var k = Spring(1, 1.0);
            Point3d origin = new Point3d(0, 0, 0);

            var mismatched = new Alpaca4d.Element.Link(new Line(origin, new Point3d(0, 0, 1)),
                Springs(k, k), Dirs(1));
            mismatched.Id = 1; mismatched.INode = 1; mismatched.JNode = 2;
            Check(Failed(() => mismatched.WriteTcl()) != null, "two materials against one direction");

            var outside = new Alpaca4d.Element.Link(new Line(origin, new Point3d(0, 0, 1)),
                Springs(k), Dirs(7));
            outside.Id = 1; outside.INode = 1; outside.JNode = 2;
            Check(Failed(() => outside.WriteTcl()) != null, "a direction outside 1-6");

            var repeated = new Alpaca4d.Element.Link(new Line(origin, new Point3d(0, 0, 1)),
                Springs(k, k), Dirs(2, 2));
            repeated.Id = 1; repeated.INode = 1; repeated.JNode = 2;
            Check(Failed(() => repeated.WriteTcl()) != null, "the same direction twice");

            var onSolids = new Alpaca4d.Element.Link(new Line(origin, new Point3d(0, 0, 1)),
                Springs(k), Dirs(4));
            onSolids.Id = 1; onSolids.INode = 1; onSolids.JNode = 2; onSolids.Ndf = 3;
            Check(Failed(() => onSolids.WriteTcl()) != null, "a rotational spring between solid nodes");

            var degenerate = new Alpaca4d.Element.Link(new Line(origin, origin), Springs(k), Dirs(1));
            degenerate.Id = 1; degenerate.INode = 1; degenerate.JNode = 2;
            var message = Failed(() => degenerate.WriteTcl());
            Check(message != null && message.Contains("Zero Length Spring"),
                  "both ends in one place, and it says what to use instead");
        }

        Section("Zero Length Spring - the element line");
        {
            var k = Spring(4, 5000.0);
            var spring = new ZeroLengthSpring(new Point3d(1, 2, 3), Springs(k, k), Dirs(1, 3));
            spring.Id = 9;
            spring.NodeId = 6;
            spring.GroundNodeId = 12;

            var lines = spring.WriteTcl().Trim().Split('\n');
            foreach (var line in lines) Console.WriteLine($"        {line}");

            Check(lines.Length == 3, $"a node, a fix and an element ({lines.Length} lines)");
            Check(lines[0] == "node 12 1 2 3", "the ground node stands where the model node stands");
            Check(lines[1] == "fix 12 1 1 1 1 1 1", "and is held in all six, one value per degree of freedom");
            Check(lines[2].StartsWith("element zeroLength 9 12 6 "), "ground first, model node second");
            Check(lines[2].Contains(" -mat 4 4 ") && lines[2].Contains(" -dir 1 3 "), "materials and directions");
            Check(lines[2].TrimEnd().EndsWith("-orient 1 0 0 0 1 0"), "world axes when no plane was given");

            // A spring on a solid node is held in three, and the builder is moved for it.
            var onSolid = new ZeroLengthSpring(new Point3d(0, 0, 0), Springs(k), Dirs(3));
            onSolid.Id = 2; onSolid.NodeId = 1; onSolid.GroundNodeId = 5; onSolid.Ndf = 3;
            var solid = onSolid.WriteTcl();
            Check(solid.Contains("fix 5 1 1 1\n"), "three values on a solid node");
            Check(solid.Contains("-ndf 3"), "and the builder is moved to match");

            var rotationOnSolid = new ZeroLengthSpring(new Point3d(0, 0, 0), Springs(k), Dirs(5));
            rotationOnSolid.Id = 2; rotationOnSolid.NodeId = 1; rotationOnSolid.GroundNodeId = 5; rotationOnSolid.Ndf = 3;
            Check(Failed(() => rotationOnSolid.WriteTcl()) != null, "a rotational spring on a solid node is refused");
        }

        Section("Zero Length Spring - a skewed frame");
        {
            var k = Spring(1, 1.0);
            var skewed = new ZeroLengthSpring(new Point3d(0, 0, 0), Springs(k), Dirs(1), Plane.WorldYZ);
            skewed.Id = 1; skewed.NodeId = 1; skewed.GroundNodeId = 2;

            Check(skewed.WriteTcl().Contains("-orient 0 1 0 0 0 1"), "the plane's own axes are written");
        }

        Section("Equal DOF");
        {
            var tie = new EqualDOF(new Point3d(0, 0, 0), new Point3d(0, 0, 0), true, true, true, false, false, false);
            tie.MasterNodeId = 3;
            tie.SlaveNodeId = 8;

            var tcl = tie.WriteTcl().Trim();
            Console.WriteLine($"        {tcl}");
            Check(tcl == "equalDOF 3 8 1 2 3", "retained node, constrained node, then the tied degrees of freedom");

            var all = new EqualDOF(Point3d.Origin, Point3d.Origin, true, true, true, true, true, true);
            all.MasterNodeId = 1; all.SlaveNodeId = 2;
            Check(all.WriteTcl().Trim() == "equalDOF 1 2 1 2 3 4 5 6", "all six when everything is tied");

            var rotationOnly = new EqualDOF(Point3d.Origin, Point3d.Origin, false, false, false, false, false, true);
            rotationOnly.MasterNodeId = 1; rotationOnly.SlaveNodeId = 2;
            Check(rotationOnly.WriteTcl().Trim() == "equalDOF 1 2 6", "and only what was asked for");
        }

        Section("Model - the node tag allocator");
        {
            // A ground node, and a skewed support's node, are nodes the geometry never asked for.
            // They all draw from one counter so that no two of them pick the same tag, and it
            // starts past the last node the point cloud handed out.
            var model = new Model();
            model.UniquePointsThreeNDF = Enumerable.Repeat(new Point3d(0, 0, 0), 4).ToList();
            model.UniquePointsSixNDF = Enumerable.Repeat(new Point3d(0, 0, 0), 6).ToList();

            Check(model.NextNodeTag() == 11, "the first spare tag is past every real node");
            Check(model.NextNodeTag() == 12, "and the next one is past that");
            Check(model.NextNodeTag() == 13, "and so on");

            var solidOnly = new Model();
            solidOnly.UniquePointsThreeNDF = Enumerable.Repeat(new Point3d(0, 0, 0), 3).ToList();
            Check(solidOnly.NextNodeTag() == 4, "counted from the three degree of freedom nodes too");
        }

        Section("Decks for OpenSees");
        {
            Deck.Directions(Path.Combine(outputDirectory, "link.tcl"));
            Deck.SpringSupport(Path.Combine(outputDirectory, "spring.tcl"));
            Deck.Tie(Path.Combine(outputDirectory, "equaldof.tcl"));
            Deck.Laminate(Path.Combine(outputDirectory, "laminate.tcl"));
            Deck.Joins(Path.Combine(outputDirectory, "joins.tcl"));
            Console.WriteLine("  [ .. ] link, spring, equaldof, laminate, joins written");
        }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all tcl checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
