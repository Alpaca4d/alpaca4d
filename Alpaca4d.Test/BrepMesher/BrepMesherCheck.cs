// #! csharp
// Mesh Brep's mesher, BrepMesher, checked inside a running Rhino 8: QuadRemesh is Rhino's, so
// nothing outside Rhino can run it. run.sh fills in __CORE__ and __OUT__ and starts this with
// rhinocode; every line it writes is a PASS or a FAIL. See README.md.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using Rhino.Geometry;

var core = "__CORE__";
var outPath = "__OUT__";
var report = new StringBuilder();
const double tol = 0.001;

void Check(string name, bool ok, string detail) => report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");

Brep Rect(Point3d a, Point3d b, Point3d c, Point3d d) => Brep.CreateFromCornerPoints(a, b, c, d, tol);
Brep Wall(double x0, double y0, double x1, double y1, double z0, double z1) =>
    Rect(new Point3d(x0, y0, z0), new Point3d(x1, y1, z0), new Point3d(x1, y1, z1), new Point3d(x0, y0, z1));

try
{
    var create = Assembly.LoadFile(core).GetType("Alpaca4d.BrepMesher").GetMethod("Create");
    var cases = new List<(string name, Brep[] breps, double length, Point3d[] points, string[] warns, string throws)>();

    var halves = Brep.JoinBreps(new[] {
        Rect(new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(4, 5, 0), new Point3d(0, 5, 0)),
        Rect(new Point3d(4, 0, 0), new Point3d(10, 0, 0), new Point3d(10, 5, 0), new Point3d(4, 5, 0)) }, tol)[0];
    cases.Add(("two faces joined, with points", new[] { halves }, 0.5,
        new[] { new Point3d(2.13, 1.37, 0), new Point3d(7.71, 3.29, 0), new Point3d(4, 2.21, 0), new Point3d(6.33, 0, 0), new Point3d(10, 5, 0), new Point3d(5, 5, 0.5), new Point3d(2.2, 1.4, 0) },
        new[] { "Point 5 at (5, 5, 0.5) is not on the Brep", "0.0762 apart" }, null));

    cases.Add(("box", new[] { new Box(Plane.WorldXY, new Interval(0, 6), new Interval(0, 4), new Interval(0, 3)).ToBrep() }, 0.5,
        new[] { new Point3d(3.1, 2.2, 3), new Point3d(6, 1.7, 1.3) }, new string[0], null));

    var plate = Brep.CreatePlanarBreps(new Curve[] { new Rectangle3d(Plane.WorldXY, 10, 6).ToNurbsCurve(), new Circle(new Point3d(4, 3, 0), 1.2).ToNurbsCurve() }, tol)[0];
    cases.Add(("plate with a hole, points on its edge", new[] { plate }, 0.5,
        new[] { new Point3d(4, 4.2, 0), new Point3d(7.4, 3.1, 0), new Point3d(4 + 1.2 * Math.Cos(2.0), 3 + 1.2 * Math.Sin(2.0), 0) }, new string[0], null));

    var arc = new Arc(new Point3d(0, 0, 0), new Point3d(2, 0, 2), new Point3d(4, 0, 0));
    var vault = Surface.CreateExtrusion(Curve.JoinCurves(new Curve[] { new ArcCurve(arc), new LineCurve(new Point3d(4, 0, 0), new Point3d(8, 0, 0)) })[0], new Vector3d(0, 6, 0)).ToBrep();
    vault.Faces.SplitKinkyFaces(0.01, true);
    var onArc = arc.ClosestPoint(new Point3d(1, 0, 2));
    cases.Add(("vault running into a flat", new[] { vault }, 0.4, new[] { new Point3d(onArc.X, 2.5, onArc.Z), new Point3d(6, 3.3, 0) }, new string[0], null));

    var slab = Rect(new Point3d(0, 0, 3), new Point3d(9, 0, 3), new Point3d(9, 6, 3), new Point3d(0, 6, 3));
    cases.Add(("wall under a slab", new[] { slab, Wall(4, 0, 4, 6, 0, 3) }, 0.5,
        new[] { new Point3d(4, 2.7, 0), new Point3d(4, 4.1, 3), new Point3d(7.3, 1.9, 3) }, new string[0], null));
    cases.Add(("wall under a slab, L = 0.37", new[] { slab, Wall(4, 0, 4, 6, 0, 3) }, 0.37, new Point3d[0], new string[0], null));
    cases.Add(("box of walls with one across, under a slab",
        new[] { Wall(0, 0, 9, 0, 0, 3), Wall(9, 0, 9, 6, 0, 3), Wall(9, 6, 0, 6, 0, 3), Wall(0, 6, 0, 0, 0, 3), Wall(4, 0, 4, 6, 0, 3), slab }, 0.5,
        new[] { new Point3d(4, 3, 1.5) }, new string[0], null));
    cases.Add(("round wall under a slab", new[] { slab, new Cylinder(new Circle(new Plane(new Point3d(4.5, 3, 0), Vector3d.ZAxis), 1.5), 3).ToBrep(false, false) }, 0.5,
        new Point3d[0], new string[0], null));
    cases.Add(("wall stopping inside a slab", new[] { slab, Wall(6.5, 1, 6.5, 5, 0, 3) }, 0.5, new Point3d[0], new string[0], "could not mesh the merged Breps"));
    cases.Add(("edge length in the wrong units", new[] { slab }, 0.001, new Point3d[0], new string[0], "more than the 200,000 allowed"));

    foreach (var (name, breps, length, points, warns, throws) in cases)
    {
        var warnings = new List<string>();
        var remarks = new List<string>();
        Mesh mesh;
        try
        {
            mesh = (Mesh)create.Invoke(null, new object[] { breps, length, points, tol, warnings, remarks });
        }
        catch (TargetInvocationException ex)
        {
            Check(name, throws != null && ex.InnerException.Message.Contains(throws), "throws: " + ex.InnerException.Message);
            continue;
        }
        if (throws != null)
        {
            Check(name, false, "should have thrown \"" + throws + "\"");
            continue;
        }

        var brep = breps.Length == 1 ? breps[0] : Brep.MergeBreps(breps, tol);
        var te = mesh.TopologyEdges;
        var tv = mesh.TopologyVertices;
        var P = Enumerable.Range(0, mesh.Vertices.Count).Select(mesh.Vertices.Point3dAt).ToArray();

        Check(name, mesh.IsValid && mesh.DisjointMeshCount == 1, $"valid {mesh.IsValid}, {mesh.DisjointMeshCount} piece(s), {mesh.Faces.QuadCount} quads, {mesh.Faces.TriangleCount} triangles");

        // Every edge of the Brep is a line of mesh edges, end to end, lying on it, and each mesh
        // edge along it has every face of the Brep along it: that is what joined means.
        var broken = new List<string>();
        foreach (var edge in brep.Edges)
        {
            var faces = edge.AdjacentFaces().Distinct().Count();
            if (edge.Valence != EdgeAdjacency.Naked && faces < 2)
                continue;
            var curve = edge.DuplicateCurve();
            var on = new List<(double t, int v)>();
            for (int i = 0; i < P.Length; i++)
            {
                curve.ClosestPoint(P[i], out var t);
                if (P[i].DistanceTo(curve.PointAt(t)) < 1e-7)
                    on.Add((t, tv.TopologyVertexIndex(i)));
            }
            on.Sort((x, y) => x.t.CompareTo(y.t));
            var segments = Enumerable.Range(0, Math.Max(0, on.Count - 1)).Select(k => te.GetEdgeIndex(on[k].v, on[k + 1].v)).ToList();
            if (curve.IsClosed && on.Count > 2)
                segments.Add(te.GetEdgeIndex(on[0].v, on[on.Count - 1].v));
            var ends = curve.IsClosed || (P.Any(q => q.DistanceTo(curve.PointAtStart) < 1e-9) && P.Any(q => q.DistanceTo(curve.PointAtEnd) < 1e-9));
            var whole = on.Count >= 2 && ends && segments.All(s => s >= 0 && te.GetConnectedFaces(s).Length == edge.AdjacentFaces().Length);
            if (!whole)
                broken.Add($"edge {edge.EdgeIndex}");
        }
        Check(name, broken.Count == 0, broken.Count == 0 ? $"all {brep.Edges.Count} Brep edges kept and joined" : "not kept or not joined: " + string.Join(", ", broken));

        var onBrep = points.Where(pt => brep.ClosestPoint(pt).DistanceTo(pt) <= tol).ToList();
        var missed = onBrep.Where(pt => P.Min(q => q.DistanceTo(pt)) > 1e-9).ToList();
        if (points.Length > 0)
            Check(name, missed.Count == 0, $"{onBrep.Count - missed.Count} of the {onBrep.Count} points on the Brep have a node on them");

        double least = 180;
        var against = 0;
        foreach (var f in mesh.Faces)
        {
            var ids = f.IsQuad ? new[] { f.A, f.B, f.C, f.D } : new[] { f.A, f.B, f.C };
            if (f.IsQuad)
                for (int k = 0; k < 4; k++)
                    least = Math.Min(least, Vector3d.VectorAngle(P[ids[(k + 1) % 4]] - P[ids[k]], P[ids[(k + 3) % 4]] - P[ids[k]]) * 180 / Math.PI);
            var centre = new Point3d(ids.Average(k => P[k].X), ids.Average(k => P[k].Y), ids.Average(k => P[k].Z));
            brep.ClosestPoint(centre, out _, out var ci, out var s, out var t, 0, out _);
            if (ci.ComponentIndexType != ComponentIndexType.BrepFace)
                continue;
            var face = brep.Faces[ci.Index];
            var normal = face.NormalAt(s, t);
            if (face.OrientationIsReversed)
                normal.Reverse();
            var facing = ids.Length == 4 ? Vector3d.CrossProduct(P[ids[2]] - P[ids[0]], P[ids[3]] - P[ids[1]]) : Vector3d.CrossProduct(P[ids[1]] - P[ids[0]], P[ids[2]] - P[ids[0]]);
            if (facing * normal < 0)
                against++;
        }
        Check(name, against == 0, $"{against} faces turned against the Brep");
        Check(name, least >= 40, $"smallest quad corner {least:F1} degrees");

        foreach (var expected in warns)
            Check(name, warnings.Any(w => w.Contains(expected)), $"warns \"{expected}\"");
        var unexpected = warnings.Where(w => !warns.Any(w.Contains)).ToList();
        Check(name, unexpected.Count == 0, unexpected.Count == 0 ? "no other warnings" : "other warnings: " + string.Join(" | ", unexpected));
    }
}
catch (Exception ex)
{
    report.AppendLine("FAIL  " + ex);
}
File.WriteAllText(outPath, report.ToString());
