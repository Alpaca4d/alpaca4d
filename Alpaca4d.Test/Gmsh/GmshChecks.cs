// Checks of the gmsh meshers in Alpaca.Core/Meshing, run under mono with the gmsh on this machine.
// The geometry is made by gmsh itself - boxes and plates written to STEP and STL - so nothing here
// needs Rhino's native code; the questions the meshers ask Rhino are answered from the boxes' planes.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Alpaca4d.Meshing;
using Rhino.Geometry;

class GmshChecks
{
    static int fails = 0;
    static string gmsh;
    static string work;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Close(double got, double want, string what, double tolerance = 1e-9)
    {
        bool ok = Math.Abs(got - want) <= tolerance * Math.Max(1.0, Math.Abs(want));
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {got:G10}, want {want:G10}");
    }

    /// <summary>A STEP (or STL) file of the geometry an OpenCASCADE .geo script makes.</summary>
    static string Make(string name, string script, string format = "step", string extra = "")
    {
        string geo = Path.Combine(work, name + ".geo");
        string file = Path.Combine(work, name + "." + format);
        File.WriteAllText(geo, "SetFactory(\"OpenCASCADE\");\n" + script);
        var info = new ProcessStartInfo(gmsh, $"\"{geo}\" {extra} -o \"{file}\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using (var p = Process.Start(info))
        {
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
        }
        if (!File.Exists(file))
            throw new Exception("gmsh made no " + file);
        return file;
    }

    static double Volume(Point3d a, Point3d b, Point3d c, Point3d d) =>
        Math.Abs(Vector3d.CrossProduct(b - a, c - a) * (d - a)) / 6.0;

    static double SolidVolume(SolidMesh mesh, int solid) =>
        mesh.Tetrahedra[solid].Sum(t => Volume(mesh.Nodes[t[0]], mesh.Nodes[t[1]], mesh.Nodes[t[2]], mesh.Nodes[t[3]]));

    static double MeanEdge(SolidMesh mesh, int[] t)
    {
        var p = t.Select(i => mesh.Nodes[i]).ToArray();
        return (p[0].DistanceTo(p[1]) + p[0].DistanceTo(p[2]) + p[0].DistanceTo(p[3]) +
                p[1].DistanceTo(p[2]) + p[1].DistanceTo(p[3]) + p[2].DistanceTo(p[3])) / 6.0;
    }

    static bool HasNode(IEnumerable<Point3d> nodes, Point3d point, double tolerance = 1e-9) =>
        nodes.Any(n => n.DistanceTo(point) <= tolerance);

    /// <summary>
    /// The faces of an axis-aligned box, in the order x min, x max, y min, y max, z min, z max: the
    /// face a triangle lies on, or -1 when it lies on none.
    /// </summary>
    static int BoxFace(BoundingBox box, Point3d[] triangle)
    {
        double[] min = { box.Min.X, box.Min.Y, box.Min.Z };
        double[] max = { box.Max.X, box.Max.Y, box.Max.Z };
        for (int axis = 0; axis < 3; axis++)
        {
            if (triangle.All(p => Math.Abs(p[axis] - min[axis]) < 1e-9)) return 2 * axis;
            if (triangle.All(p => Math.Abs(p[axis] - max[axis]) < 1e-9)) return 2 * axis + 1;
        }
        return -1;
    }

    static bool Inside(BoundingBox box, Point3d p) =>
        p.X > box.Min.X - 1e-9 && p.X < box.Max.X + 1e-9 &&
        p.Y > box.Min.Y - 1e-9 && p.Y < box.Max.Y + 1e-9 &&
        p.Z > box.Min.Z - 1e-9 && p.Z < box.Max.Z + 1e-9;

    static SolidMeshJob BoxJob(params (BoundingBox Box, double Size)[] boxes)
    {
        var job = new SolidMeshJob { SolidCount = boxes.Length, Tolerance = 1e-6 };
        for (int i = 0; i < boxes.Length; i++)
        {
            var b = boxes[i].Box;
            string step = Make($"box{i}_{Guid.NewGuid():N}",
                $"Box(1) = {{{b.Min.X}, {b.Min.Y}, {b.Min.Z}, {b.Max.X - b.Min.X}, {b.Max.Y - b.Min.Y}, {b.Max.Z - b.Min.Z}}};\n", "step", "-0");
            job.Breps.Add((i, step, boxes[i].Size));
        }
        job.SolidOf = c =>
        {
            for (int i = 0; i < boxes.Length; i++)
                if (Inside(boxes[i].Box, c)) return i;
            return 0;
        };
        job.FaceOf = (i, triangle) => BoxFace(boxes[i].Box, triangle);
        job.FaceCount = i => 6;
        return job;
    }

    static SolidMesh Solve(SolidMeshJob job, string name) =>
        SolidMesher.Create(job, gmsh, Path.Combine(work, name));

    static void Finder()
    {
        Console.WriteLine("\nFinding gmsh");
        var version = Gmsh.VersionOf(gmsh);
        Check(version != null && version >= Gmsh.MinimumVersion, $"gmsh at {gmsh} says it is {version}, at least {Gmsh.MinimumVersion}");
        Check(Gmsh.Find(gmsh, out _) == gmsh, "a path that is set is the one used");
        string missing = Gmsh.Find("/nowhere/gmsh", out string problem);
        Check(missing == null && problem.Contains("/nowhere/gmsh"), "a path set to nothing is reported, not searched past");
        Check(Gmsh.Candidates().Any(), "there are places to look for gmsh when no path is set");
    }

    static void OneBox()
    {
        Console.WriteLine("\nOne box, 1 x 1 x 0.4, elements of 0.1");
        var box = new BoundingBox(0, 0, 0, 1, 1, 0.4);
        var mesh = Solve(BoxJob((box, 0.1)), "onebox");
        Check(mesh.Failures.Count == 0, "gmsh meshed it" + (mesh.Failures.Count > 0 ? ": " + mesh.Failures.First().Value : ""));
        Check(mesh.Tetrahedra[0].Count > 300, $"{mesh.Tetrahedra[0].Count} tetrahedra");
        Close(SolidVolume(mesh, 0), 0.4, "their volumes add up to the box's", 1e-9);
        Check(mesh.Quality[0].Min() > 0.1, $"the worst is shaped {mesh.Quality[0].Min():0.###} (above {MeshQuality.Poor})");
        double mean = mesh.Tetrahedra[0].Average(t => MeanEdge(mesh, t));
        Check(mean > 0.05 && mean < 0.15, $"their mean edge is {mean:0.###}, near the size 0.1");

        Check(mesh.FaceNodes[0].Count == 6, "Face Nodes has a branch for each of the box's six faces");
        var bottom = mesh.FaceNodes[0][4];
        Check(bottom.Count > 0 && bottom.All(p => Math.Abs(p.Z) < 1e-12), $"the bottom face's {bottom.Count} nodes are all at z = 0");
        var corners = new HashSet<Point3d>(mesh.Nodes.Where(n => Math.Abs(n.Z) < 1e-12));
        var middles = bottom.Where(p => !corners.Contains(p)).ToList();
        Check(middles.Count > corners.Count, $"and besides its {corners.Count} corner nodes, the {middles.Count} edge middles of a ten node tetrahedron");
        Check(bottom.Distinct().Count() == bottom.Count, "each only once");
        Check(new[] { new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0), new Point3d(0, 1, 0) }.All(p => HasNode(bottom, p, 0)),
              "the face's four corners are among them");
        var top = mesh.FaceNodes[0][5];
        Check(top.Count > 0 && top.All(p => Math.Abs(p.Z - 0.4) < 1e-12), "the top face's nodes are all at z = 0.4");
        Check(mesh.FaceNodes[0].Sum(f => f.Count) > 0 && mesh.FaceNodes[0].All(f => f.Count > 0), "every face has its nodes");
    }

    static void Footing()
    {
        Console.WriteLine("\nA column stub on a footing: two Breps that touch, meshed as one");
        var footing = new BoundingBox(0, 0, 0, 1, 1, 0.4);
        var column = new BoundingBox(0.3, 0.3, 0.4, 0.7, 0.7, 1.4);
        var mesh = Solve(BoxJob((footing, 0.15), (column, 0.05)), "footing");
        Check(mesh.Failures.Count == 0, "gmsh meshed them");
        Close(SolidVolume(mesh, 0), 0.4, "the footing's tetrahedra fill the footing", 1e-9);
        Close(SolidVolume(mesh, 1), 0.16, "the column's fill the column", 1e-9);
        double footingEdge = mesh.Tetrahedra[0].Average(t => MeanEdge(mesh, t));
        double columnEdge = mesh.Tetrahedra[1].Average(t => MeanEdge(mesh, t));
        Check(columnEdge < footingEdge, $"each keeps its own size: column {columnEdge:0.###}, footing {footingEdge:0.###}");

        // The interface: the column's bottom face is part of the footing's top face, node for node.
        var columnBottom = mesh.FaceNodes[1][4];
        var footingTop = new HashSet<Point3d>(mesh.FaceNodes[0][5]);
        Check(columnBottom.Count > 0 && columnBottom.All(footingTop.Contains),
              $"all {columnBottom.Count} nodes of the column's bottom face are on the footing's top face: they share them");
        var footingNodes = new HashSet<int>(mesh.Tetrahedra[0].SelectMany(t => t));
        var columnNodes = new HashSet<int>(mesh.Tetrahedra[1].SelectMany(t => t));
        int shared = footingNodes.Intersect(columnNodes).Count();
        Check(shared > 0 && mesh.Nodes.Where((n, i) => footingNodes.Contains(i) && columnNodes.Contains(i)).All(n => Math.Abs(n.Z - 0.4) < 1e-12),
              $"the two solids' tetrahedra share {shared} nodes, all on the interface");
    }

    static void Overlap()
    {
        Console.WriteLine("\nTwo Breps that overlap: the overlap goes to the first");
        var a = new BoundingBox(0, 0, 0, 1, 1, 1);
        var b = new BoundingBox(0.5, 0, 0, 1.5, 1, 1);
        var mesh = Solve(BoxJob((a, 0.2), (b, 0.2)), "overlap");
        Check(mesh.Failures.Count == 0, "gmsh meshed them");
        Close(SolidVolume(mesh, 0), 1.0, "the first keeps all of its volume", 1e-9);
        Close(SolidVolume(mesh, 1), 0.5, "the second, what the first does not hold", 1e-9);
        // The second's face at x = 0.5 is inside the first: it is on no face of the second.
        Check(mesh.FaceNodes[1][0].Count == 0 || mesh.FaceNodes[1][0].All(p => Math.Abs(p.X - 0.5) < 1e-12),
              "the surface between them inside the first is not taken for a face of the second");
    }

    static void Points()
    {
        Console.WriteLine("\nInclusion points: one inside, one on a face, one on an edge");
        var box = new BoundingBox(0, 0, 0, 1, 1, 0.4);
        var job = BoxJob((box, 0.15));
        var points = new[] { new Point3d(0.4321, 0.5678, 0.2345), new Point3d(0.3141, 0, 0.1592), new Point3d(0.2718, 0, 0) };
        job.BrepPoints.AddRange(points);
        var mesh = Solve(job, "points");
        Check(mesh.Failures.Count == 0, "gmsh meshed it (Netgen's optimiser, which crashes on a held point, is off)" +
              (mesh.Failures.Count > 0 ? ": " + mesh.Failures.First().Value : ""));
        var used = mesh.Tetrahedra[0].SelectMany(t => t).Distinct().Select(i => mesh.Nodes[i]).ToList();
        Check(HasNode(used, points[0], 0), "a tetrahedra node is exactly at the point inside");
        Check(HasNode(used, points[1], 0), "and exactly at the point on the face");
        Check(HasNode(used, points[2], 0), "and exactly at the point on the edge");
        Check(HasNode(mesh.FaceNodes[0][2], points[1], 0), "the point on the face is among that face's Face Nodes");
        Close(SolidVolume(mesh, 0), 0.4, "the volume is still the box's", 1e-9);
    }

    static void Refinement()
    {
        Console.WriteLine("\nRefinement near a point at a corner, to 0.02 from 0.15");
        var box = new BoundingBox(0, 0, 0, 1, 1, 0.4);
        var corner = new Point3d(1, 1, 0.4);
        var job = BoxJob((box, 0.15));
        job.Refinements.Add(new MeshRefinement(new[] { corner }, 0.02, 0.4, "point"));
        var mesh = Solve(job, "refine");
        Check(mesh.Failures.Count == 0, "gmsh meshed it");
        Func<int[], Point3d> centre = t => new Point3d(t.Select(i => mesh.Nodes[i]).Aggregate(Point3d.Origin, (s, p) => s + p) / 4.0);
        var near = mesh.Tetrahedra[0].Where(t => centre(t).DistanceTo(corner) < 0.05).ToList();
        var far = mesh.Tetrahedra[0].Where(t => centre(t).DistanceTo(corner) > 0.6).ToList();
        double nearEdge = near.Average(t => MeanEdge(mesh, t));
        double farEdge = far.Average(t => MeanEdge(mesh, t));
        Check(nearEdge < 0.04, $"near the point the edges are {nearEdge:0.####} long, near the 0.02 asked for");
        Check(farEdge > 0.08, $"further than its reach they are {farEdge:0.###}, back near 0.15");
        Close(SolidVolume(mesh, 0), 0.4, "the volume is still the box's", 1e-9);

        var auto = new MeshRefinement(new[] { corner }, 0.02, 0, "point");
        Close(auto.ReachFor(0.15), (0.15 - 0.02) / (MeshRefinement.Growth - 1), "a reach left to the mesher grows the elements by 1.3 a ring");
    }

    static void ShellStep()
    {
        Console.WriteLine("\nA Brep that comes out of STEP as faces, not a solid");
        string step = Make("faces", "Box(1) = {0, 0, 0, 1, 1, 1};\nDelete{ Volume{1}; }\n", "step", "-0");
        var job = new SolidMeshJob { SolidCount = 1, Tolerance = 1e-6 };
        job.Breps.Add((0, step, 0.2));
        var box = new BoundingBox(0, 0, 0, 1, 1, 1);
        job.SolidOf = c => 0;
        job.FaceOf = (i, t) => BoxFace(box, t);
        job.FaceCount = i => 6;
        var mesh = Solve(job, "faces");
        Check(mesh.Failures.Count == 0, "its faces are closed into a volume and meshed" + (mesh.Failures.Count > 0 ? ": " + mesh.Failures.First().Value : ""));
        Close(mesh.Tetrahedra[0].Count > 0 ? SolidVolume(mesh, 0) : 0, 1.0, "which fills the box", 1e-9);
    }

    static void ClosedMesh()
    {
        Console.WriteLine("\nA closed mesh: a box's surface in STL, remeshed and filled");
        string stl = Make("stlbox", "Box(1) = {0, 0, 0, 2, 1, 1};\nMesh.MeshSizeMax = 0.25;\n", "stl", "-2");
        var job = new SolidMeshJob { SolidCount = 1, Tolerance = 1e-6, FeatureAngle = 40 };
        job.Meshes.Add((0, stl, 0.1));
        var inside = new Point3d(1.2345, 0.4321, 0.5555);
        var onSurface = new Point3d(0.7777, 0.3333, 1.0);
        job.PointsInside[0] = new List<Point3d> { inside };
        job.PointsOnSurface[0] = new List<Point3d> { onSurface };
        var mesh = Solve(job, "stlbox");
        Check(mesh.Failures.Count == 0, "gmsh meshed it" + (mesh.Failures.Count > 0 ? ": " + mesh.Failures.First().Value : ""));
        // STL is single precision: the box's corners come through to 1e-7 or so.
        Close(SolidVolume(mesh, 0), 2.0, "the tetrahedra fill the box", 1e-5);
        double mean = mesh.Tetrahedra[0].Average(t => MeanEdge(mesh, t));
        Check(mean < 0.16, $"remeshed at the size asked, 0.1, finer than the STL's 0.25: mean edge {mean:0.###}");
        // gmsh may cut a side in two to reparametrise it, but never folds a patch over an edge.
        var box = new BoundingBox(0, 0, 0, 2, 1, 1);
        Func<List<Point3d>, bool> flat = patch => Enumerable.Range(0, 3).Any(axis =>
            patch.All(p => Math.Abs(p[axis] - box.Min[axis]) < 1e-5) || patch.All(p => Math.Abs(p[axis] - box.Max[axis]) < 1e-5));
        Check(mesh.FaceNodes[0].Count >= 6 && mesh.FaceNodes[0].All(flat),
              $"its surface is cut at the sharp edges into {mesh.FaceNodes[0].Count} patches, each on one side of the box");
        var used = mesh.Tetrahedra[0].SelectMany(t => t).Distinct().Select(i => mesh.Nodes[i]).ToList();
        Check(HasNode(used, inside, 0), "a node is exactly at the point inside");
        Check(HasNode(used, onSurface, 0), "the node nearest the point on its surface was moved onto it");
    }

    static string Plate()
    {
        return Make("plate",
            "Rectangle(1) = {0, 0, 0, 4, 2};\nDisk(2) = {1, 1, 0, 0.3};\nDisk(3) = {3, 1, 0, 0.2};\n" +
            "BooleanDifference{ Surface{1}; Delete; }{ Surface{2, 3}; Delete; }\n", "step", "-0");
    }

    static double Area(SurfaceMesh mesh) => mesh.Faces.Sum(f =>
    {
        var p = f.Select(i => mesh.Nodes[i]).ToArray();
        return p.Length == 3
            ? Vector3d.CrossProduct(p[1] - p[0], p[2] - p[0]).Length / 2
            : (Vector3d.CrossProduct(p[1] - p[0], p[2] - p[0]).Length + Vector3d.CrossProduct(p[2] - p[0], p[3] - p[0]).Length) / 2;
    });

    static void Surfaces()
    {
        Console.WriteLine("\nA plate with two holes and a plate beside it, quads");
        string plate = Plate();
        string beside = Make("beside", "Rectangle(1) = {4, 0, 0, 2, 2};\n", "step", "-0");
        var job = new SurfaceMeshJob { Tolerance = 1e-6, NormalAt = p => Vector3d.ZAxis };
        job.Breps.Add((plate, 0.15));
        job.Breps.Add((beside, 0.15));
        // A straight line, and a circle as a closed smooth curve through 64 points on it.
        job.Curves.Add(new InclusionCurve(new[] { new Point3d(2, 0.2, 0), new Point3d(2, 1.8, 0) }, true));
        var circle = Enumerable.Range(0, 65).Select(i => new Point3d(5 + 0.5 * Math.Cos(i * Math.PI / 32), 1 + 0.5 * Math.Sin(i * Math.PI / 32), 0)).ToList();
        circle[64] = circle[0];
        job.Curves.Add(new InclusionCurve(circle, false));
        var points = new[] { new Point3d(0.4321, 0.5678, 0), new Point3d(5.5, 1.5, 0) };
        job.Points.AddRange(points);
        var mesh = SurfaceMesher.Create(job, gmsh, Path.Combine(work, "plate"));
        Check(mesh.Failure == null, "gmsh meshed them" + (mesh.Failure != null ? ": " + mesh.Failure : ""));
        int quads = mesh.Faces.Count(f => f.Length == 4);
        Check(quads > 0.9 * mesh.Faces.Count, $"{quads} of its {mesh.Faces.Count} elements are quads");
        double area = 4 * 2 - Math.PI * (0.09 + 0.04) + 2 * 2;
        Close(Area(mesh), area, "they cover the plates, the holes left out", 5e-3);
        Check(points.All(p => HasNode(mesh.Nodes, p, 0)), "a node is exactly at each inclusion point");
        var onLine = mesh.Nodes.Where(n => Math.Abs(n.X - 2) < 1e-12 && n.Y > 0.2 - 1e-12 && n.Y < 1.8 + 1e-12).ToList();
        Check(HasNode(onLine, new Point3d(2, 0.2, 0), 0) && HasNode(onLine, new Point3d(2, 1.8, 0), 0) && onLine.Count >= 10,
              $"a line of {onLine.Count} nodes along the inclusion line, from end to end");
        var onCircle = mesh.Nodes.Where(n => Math.Abs(n.DistanceTo(new Point3d(5, 1, 0)) - 0.5) < 1e-4).ToList();
        Check(onCircle.Count >= 12, $"and {onCircle.Count} on the inclusion circle");
        Check(mesh.Faces.All(f => Normal(mesh, f).Z > 0), "every element faces +z, as the surfaces do");
        Check(mesh.Quality.Min() > 0.0, $"none is inside out (worst {mesh.Quality.Min():0.###})");

        // Joined where they touch: the line x = 4 has nodes both plates use.
        var left = new HashSet<int>(mesh.Faces.Where(f => f.All(i => mesh.Nodes[i].X <= 4 + 1e-12)).SelectMany(f => f));
        var right = new HashSet<int>(mesh.Faces.Where(f => f.All(i => mesh.Nodes[i].X >= 4 - 1e-12)).SelectMany(f => f));
        Check(left.Intersect(right).Count() >= 10, $"the two plates share {left.Intersect(right).Count()} nodes along the edge where they meet");

        Console.WriteLine("\nThe same plate, triangles, facing down");
        var down = new SurfaceMeshJob { Tolerance = 1e-6, Elements = SurfaceElements.Triangles, NormalAt = p => -Vector3d.ZAxis };
        down.Breps.Add((plate, 0.15));
        var triangles = SurfaceMesher.Create(down, gmsh, Path.Combine(work, "triangles"));
        Check(triangles.Failure == null && triangles.Faces.All(f => f.Length == 3), $"{triangles.Faces.Count} triangles, and nothing else");
        Check(triangles.Faces.All(f => Normal(triangles, f).Z < 0), "each turned to face -z, as the surface does");
        Close(Area(triangles), 4 * 2 - Math.PI * (0.09 + 0.04), "they cover the plate, its holes polygons a little smaller than the circles", 5e-3);
    }

    static void Refined()
    {
        Console.WriteLine("\nThe plate refined along its larger hole's edge");
        var job = new SurfaceMeshJob { Tolerance = 1e-6, NormalAt = p => Vector3d.ZAxis };
        job.Breps.Add((Plate(), 0.2));
        var circle = Enumerable.Range(0, 64).Select(i => new Point3d(1 + 0.3 * Math.Cos(i * Math.PI / 32), 1 + 0.3 * Math.Sin(i * Math.PI / 32), 0));
        job.Refinements.Add(new MeshRefinement(circle, 0.03, 0.5, "curve"));
        var mesh = SurfaceMesher.Create(job, gmsh, Path.Combine(work, "refined"));
        Check(mesh.Failure == null, "gmsh meshed it");
        Func<int[], double> size = f => f.Select((i, k) => mesh.Nodes[i].DistanceTo(mesh.Nodes[f[(k + 1) % f.Length]])).Average();
        Func<int[], Point3d> centre = f => new Point3d(f.Select(i => mesh.Nodes[i]).Aggregate(Point3d.Origin, (s, p) => s + p) / f.Length);
        var near = mesh.Faces.Where(f => centre(f).DistanceTo(new Point3d(1, 1, 0)) < 0.36).ToList();
        var far = mesh.Faces.Where(f => centre(f).X > 2.5 && centre(f).DistanceTo(new Point3d(3, 1, 0)) > 0.4).ToList();
        Check(near.Average(size) < 0.05, $"at the hole the elements are {near.Average(size):0.###}, near the 0.03 asked for");
        Check(far.Average(size) > 0.12, $"away from it they are {far.Average(size):0.###}, near the plate's 0.2");
    }

    static void ClosedPolysurface()
    {
        Console.WriteLine("\nA closed polysurface as shells: a core under a slab");
        string core = Make("core", "Box(1) = {0, 0, 0, 2, 2, 3};\n", "step", "-0");
        string slab = Make("slab", "Rectangle(1) = {-2, -2, 3, 6, 6};\n", "step", "-0");
        var job = new SurfaceMeshJob { Tolerance = 1e-6 };
        job.Breps.Add((core, 0.5));
        job.Breps.Add((slab, 0.5));
        var mesh = SurfaceMesher.Create(job, gmsh, Path.Combine(work, "core"));
        Check(mesh.Failure == null, "gmsh meshed them" + (mesh.Failure != null ? ": " + mesh.Failure : ""));
        Close(Area(mesh), 6 * 4 + 2 * 4 + 36 - 4, "the core's six sides and the slab, the core's top shared with it", 1e-9);
        var walls = new HashSet<int>(mesh.Faces.Where(f => f.All(i => mesh.Nodes[i].Z <= 3 - 1e-12 || Math.Abs(mesh.Nodes[i].X) < 1e-12)).SelectMany(f => f));
        Check(mesh.Faces.Count(f => f.All(i => Math.Abs(mesh.Nodes[i].Z - 3) < 1e-12)) > 0, "the slab is meshed");
        var topEdge = mesh.Nodes.Where(n => Math.Abs(n.Z - 3) < 1e-12 && Math.Abs(n.X) < 1e-12 && n.Y >= 0 && n.Y <= 2).ToList();
        Check(topEdge.Count >= 3, $"the wall at x = 0 and the slab share the {topEdge.Count} nodes along their meeting line");
        _ = walls;
    }

    static Vector3d Normal(SurfaceMesh mesh, int[] f)
    {
        var p = f.Select(i => mesh.Nodes[i]).ToArray();
        return p.Length == 3 ? Vector3d.CrossProduct(p[1] - p[0], p[2] - p[0]) : Vector3d.CrossProduct(p[2] - p[0], p[3] - p[1]);
    }

    static void Stop()
    {
        Console.WriteLine("\nEscape stops gmsh");
        var job = BoxJob((new BoundingBox(0, 0, 0, 1, 1, 1), 0.01));
        var watch = Stopwatch.StartNew();
        var mesh = SolidMesher.Create(job, gmsh, Path.Combine(work, "stop"), () => watch.ElapsedMilliseconds > 300);
        Check(mesh.Failures.Count == 1 && mesh.Failures[0].StartsWith("Stopped"), "a run halted is reported as stopped");
        Check(watch.ElapsedMilliseconds < 5000, $"and ends at once ({watch.ElapsedMilliseconds} ms), not after the million tetrahedra it was making");
    }

    static void Reader()
    {
        Console.WriteLine("\nThe .msh reader");
        string text = "$MeshFormat\n4.1 0 8\n$EndMeshFormat\n$Entities\n0 0 0 1\n7 0 0 0 1 1 1 0 2 3 -4\n$EndEntities\n" +
                      "$Nodes\n1 4 1 4\n3 7 0 4\n1\n2\n3\n4\n0 0 0\n1 0 0\n0 1 0\n0 0 1\n$EndNodes\n" +
                      "$Elements\n1 1 1 1\n3 7 4 1\n9 1 2 3 4\n$EndElements\n";
        var msh = MshFile.Read(new StringReader(text));
        Check(msh.Nodes.Count == 4 && msh.Nodes[4] == new Point3d(0, 0, 1), "nodes by tag");
        Check(msh.Elements.Count == 1 && msh.Elements[0].Type == MshFile.Tetrahedron && msh.Elements[0].Entity == 7, "a tetrahedron on volume 7");
        Check(msh.VolumeSurfaces[7].SequenceEqual(new[] { 3, 4 }), "volume 7 bounded by surfaces 3 and 4, signs dropped");
        bool binary = false;
        try { MshFile.Read(new StringReader("$MeshFormat\n4.1 1 8\n")); }
        catch (FormatException) { binary = true; }
        Check(binary, "a binary file is refused, not misread");
    }

    static int Main(string[] args)
    {
        gmsh = args.Length > 0 ? args[0] : "gmsh";
        work = Path.Combine(Path.GetTempPath(), "alpaca-gmsh-checks-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(work);
        try
        {
            Reader();
            Finder();
            OneBox();
            Footing();
            Overlap();
            Points();
            Refinement();
            ShellStep();
            ClosedMesh();
            Surfaces();
            Refined();
            ClosedPolysurface();
            Stop();
        }
        finally
        {
            if (fails == 0)
                Directory.Delete(work, true);
            else
                Console.WriteLine($"\nThe files are kept in {work}");
        }
        Console.WriteLine(fails == 0 ? "\nAll checks passed." : $"\n{fails} check(s) FAILED.");
        return fails == 0 ? 0 : 1;
    }
}
