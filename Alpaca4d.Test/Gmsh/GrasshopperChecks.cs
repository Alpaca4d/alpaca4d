// Mesh Solid, Mesh Surface and Mesh Refinement through the Grasshopper components themselves, in a
// running Rhino 8 with gmsh installed. What the mono checks (GmshChecks.cs) cannot do is done here:
// Rhino's Breps and meshes written to STEP and STL and read back by gmsh at their own size, gmsh's
// faces matched to the Brep's, the elements turned the way the surfaces face, and the tetrahedra
// taken by Ten Node Tetrahedron. grasshopper.sh loads this build beside the installed plugin and
// calls Run by reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

public static class GrasshopperCheck
{
    static StringBuilder report;
    static int fails;
    static string folder;

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

    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol * Math.Max(1.0, Math.Abs(b));

    public static string Run(string work)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        // The user's settings stay as they are: gmsh is found where Homebrew put it.
        Alpaca4d.Application.UserFolder = work;
        try
        {
            BoxFaces();
            FootingAndColumn();
            Cylinder();
            ClosedMesh();
            IntoTenNodeTetrahedron();
            PlateWithHoles();
            FreeForm();
            ShellsOfAClosedPolysurface();
            Problems();
        }
        catch (Exception e)
        {
            Check(false, "no exception: " + e);
        }
        report.AppendLine();
        report.AppendLine(fails == 0 ? "all grasshopper checks passed" : $"{fails} grasshopper check(s) failed");
        return report.ToString();
    }

    // ---------------------------------------------------------------- building a document

    sealed class Doc
    {
        public readonly GH_Document Document = new GH_Document();
        readonly List<GH_Component> components = new List<GH_Component>();

        public T Add<T>(string unit = null) where T : GH_Component, new()
        {
            var component = new T();
            component.CreateAttributes();
            if (unit != null)
                component.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) }).Invoke(component, new object[] { unit, false, false });
            Document.AddObject(component, false);
            components.Add(component);
            return component;
        }

        public void Solve()
        {
            Document.Enabled = true;
            Document.NewSolution(true, GH_SolutionMode.Silent);
        }

        public IEnumerable<string> Messages(GH_RuntimeMessageLevel level) =>
            components.SelectMany(c => c.RuntimeMessages(level).Select(m => $"{c.NickName}: {m}"));
    }

    static IGH_Param In(GH_Component c, string name) =>
        c.Params.Input.FirstOrDefault(p => p.Name == name || p.NickName == name)
        ?? throw new Exception($"{c.Name} has no input {name}");

    static IGH_Param Out(GH_Component c, string name) =>
        c.Params.Output.FirstOrDefault(p => p.Name == name || p.NickName == name)
        ?? throw new Exception($"{c.Name} has no output {name}");

    static void Set<T>(GH_Component c, string input, params T[] values) where T : class, IGH_Goo
    {
        var param = (GH_PersistentParam<T>)In(c, input);
        param.PersistentData.Clear();
        foreach (var value in values)
            param.PersistentData.Append(value);
    }

    static void SetAny(GH_Component c, string input, params IGH_Goo[] values)
    {
        var param = In(c, input);
        var data = (GH_Structure<IGH_Goo>)param.GetType().GetProperty("PersistentData").GetValue(param);
        data.Clear();
        foreach (var value in values)
            data.Append(value);
    }

    static void Wire(GH_Component from, string output, GH_Component to, string input) =>
        In(to, input).AddSource(Out(from, output));

    static List<T> All<T>(IGH_Param p) => p.VolatileData.AllData(true).OfType<T>().ToList();

    static List<Mesh> Tetrahedra(GH_Component mesher, int branch) =>
        Out(mesher, "Tetrahedra").VolatileData.get_Branch(new GH_Path(branch)).Cast<GH_Mesh>().Select(m => m.Value).ToList();

    static List<Point3d> FaceNodes(GH_Component mesher, int solid, int face) =>
        Out(mesher, "Face Nodes").VolatileData.get_Branch(new GH_Path(solid, face)).Cast<GH_Point>().Select(p => p.Value).ToList();

    static double Volume(IEnumerable<Mesh> tets) => tets.Sum(m =>
    {
        var v = m.Vertices.ToPoint3dArray();
        return Math.Abs(Vector3d.CrossProduct(v[1] - v[0], v[2] - v[0]) * (v[3] - v[0])) / 6.0;
    });

    static void NoErrors(Doc doc, string what)
    {
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Count == 0, what + (errors.Count > 0 ? ": " + string.Join(" | ", errors) : ""));
    }

    static GH_Component MeshSolid(Doc doc, IEnumerable<IGH_Goo> solids, params double[] sizes)
    {
        var mesher = doc.Add<Alpaca4d.Gh.MeshSolid>();
        SetAny(mesher, "Solids", solids.ToArray());
        Set(mesher, "Size", sizes.Select(s => new GH_Number(s)).ToArray());
        return mesher;
    }

    // ---------------------------------------------------------------- the checks

    static void BoxFaces()
    {
        Section("A Brep box, off the origin: gmsh's faces matched to Rhino's");
        var box = new Box(new BoundingBox(10, 20, 3, 11, 21, 3.4)).ToBrep();
        var doc = new Doc();
        var mesher = MeshSolid(doc, new[] { new GH_Brep(box) }, 0.1);
        doc.Solve();
        NoErrors(doc, "Mesh Solid ran with no error");
        var tets = Tetrahedra(mesher, 0);
        Check(Near(Volume(tets), 0.4, 1e-9), $"its {tets.Count} tetrahedra fill the box: {Volume(tets):G10}, the numbers through STEP unchanged");
        var bounds = new BoundingBox(tets.SelectMany(t => t.Vertices.ToPoint3dArray()));
        Check(bounds.Min.DistanceTo(new Point3d(10, 20, 3)) < 1e-9 && bounds.Max.DistanceTo(new Point3d(11, 21, 3.4)) < 1e-9,
              "and they are where the box is, at its size: no unit conversion on the way");
        Check(Out(mesher, "Face Nodes").VolatileData.PathCount == box.Faces.Count, $"Face Nodes has a branch for each of its {box.Faces.Count} faces");
        bool onTheirFaces = true;
        for (int f = 0; f < box.Faces.Count; f++)
        {
            var face = box.Faces[f].DuplicateFace(false);
            var nodes = FaceNodes(mesher, 0, f);
            if (nodes.Count == 0 || nodes.Any(p => face.ClosestPoint(p).DistanceTo(p) > 1e-9))
                onTheirFaces = false;
        }
        Check(onTheirFaces, "branch {0;f} holds nodes on Rhino's face f, and only those");
        var quality = All<GH_Number>(Out(mesher, "Quality"));
        Check(quality.Count == tets.Count && quality.Min(q => q.Value) > 0.1, $"a Quality for each, the worst {quality.Min(q => q.Value):0.###}");
    }

    static void FootingAndColumn()
    {
        Section("A column stub on a footing, a point load on the column's top, a refinement on its foot");
        var footing = new Box(new BoundingBox(0, 0, 0, 2, 2, 0.5)).ToBrep();
        var column = new Box(new BoundingBox(0.8, 0.8, 0.5, 1.2, 1.2, 2.0)).ToBrep();
        var doc = new Doc();
        var mesher = MeshSolid(doc, new[] { new GH_Brep(footing), new GH_Brep(column) }, 0.25, 0.1);
        var load = new Point3d(1.05, 0.93, 2.0);
        Set(mesher, "Points", new GH_Point(load));
        var refinement = doc.Add<Alpaca4d.Gh.MeshRefinementComponent>();
        SetAny(refinement, "Geometry", new GH_Curve(new Rectangle3d(new Plane(new Point3d(0, 0, 0.5), Vector3d.ZAxis), new Interval(0.8, 1.2), new Interval(0.8, 1.2)).ToNurbsCurve()));
        Set(refinement, "Size", new GH_Number(0.04));
        Wire(refinement, "Refinement", mesher, "Refinements");
        doc.Solve();
        NoErrors(doc, "no errors");
        var a = Tetrahedra(mesher, 0);
        var b = Tetrahedra(mesher, 1);
        Check(Near(Volume(a), 2.0, 1e-9) && Near(Volume(b), 0.24, 1e-9), $"footing {Volume(a):G8} and column {Volume(b):G8} each filled");
        int foot = Enumerable.Range(0, column.Faces.Count).First(f => column.Faces[f].PointAt(column.Faces[f].Domain(0).Mid, column.Faces[f].Domain(1).Mid).Z < 0.5 + 1e-9);
        int top = Enumerable.Range(0, footing.Faces.Count).First(f => footing.Faces[f].PointAt(footing.Faces[f].Domain(0).Mid, footing.Faces[f].Domain(1).Mid).Z > 0.5 - 1e-9);
        var columnFoot = FaceNodes(mesher, 1, foot);
        var footingTop = new HashSet<Point3d>(FaceNodes(mesher, 0, top));
        var footingNodes = new HashSet<Point3d>(a.SelectMany(t => t.Vertices.ToPoint3dArray()));
        Check(columnFoot.Count > 0 && columnFoot.All(footingTop.Contains) &&
              b.SelectMany(t => t.Vertices.ToPoint3dArray()).Where(p => Math.Abs(p.Z - 0.5) < 1e-12).All(footingNodes.Contains),
              $"the {columnFoot.Count} Face Nodes of the column's foot are on the footing's top face, its corners the footing's nodes too");
        Check(b.Any(t => t.Vertices.ToPoint3dArray().Any(p => p.DistanceTo(load) < 1e-12)), "a node exactly at the point load");
        double Edge(Mesh t) { var p = t.Vertices.ToPoint3dArray(); return (p[0].DistanceTo(p[1]) + p[2].DistanceTo(p[3])) / 2; }
        var nearFoot = a.Where(t => Math.Abs(t.GetBoundingBox(false).Center.Z - 0.45) < 0.05 && Math.Abs(t.GetBoundingBox(false).Center.X - 0.8) < 0.05).ToList();
        var far = a.Where(t => t.GetBoundingBox(false).Center.X < 0.3 && t.GetBoundingBox(false).Center.Y < 0.3).ToList();
        Check(nearFoot.Count > 0 && far.Count > 0 && nearFoot.Average(Edge) < 0.5 * far.Average(Edge),
              $"refined along the column's foot ({(nearFoot.Count > 0 ? nearFoot.Average(Edge) : 0):0.###}) and coarse in the footing's corner ({(far.Count > 0 ? far.Average(Edge) : 0):0.###})");
    }

    static void Cylinder()
    {
        Section("A cylinder: curved faces through STEP");
        var cylinder = new Rhino.Geometry.Cylinder(new Circle(Plane.WorldXY, 0.5), 2.0).ToBrep(true, true);
        var doc = new Doc();
        var mesher = MeshSolid(doc, new[] { new GH_Brep(cylinder) }, 0.12);
        var inside = new Point3d(0.1234, -0.2345, 1.3456);
        Set(mesher, "Points", new GH_Point(inside));
        doc.Solve();
        NoErrors(doc, "no errors");
        var tets = Tetrahedra(mesher, 0);
        double exact = Math.PI * 0.25 * 2.0;
        Check(Volume(tets) < exact && Near(Volume(tets), exact, 0.02), $"they fill it, short of its {exact:0.####} by the facets on its round: {Volume(tets):0.####}");
        Check(tets.Any(t => t.Vertices.ToPoint3dArray().Any(p => p.DistanceTo(inside) < 1e-12)), "a node exactly at the point inside");
        int side = Enumerable.Range(0, cylinder.Faces.Count).First(f => !cylinder.Faces[f].IsPlanar());
        var nodes = FaceNodes(mesher, 0, side);
        var corners = new HashSet<Point3d>(tets.SelectMany(t => t.Vertices.ToPoint3dArray()));
        Check(nodes.Where(corners.Contains).All(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - 0.5) < 1e-6),
              "the round face's corner nodes are on the round, its middles inside it by the chord");
    }

    static void ClosedMesh()
    {
        Section("A closed Rhino mesh: a sphere, remeshed and filled");
        var sphere = Mesh.CreateFromSphere(new Sphere(new Point3d(5, 5, 5), 1.0), 40, 30);
        var doc = new Doc();
        var mesher = MeshSolid(doc, new[] { new GH_Mesh(sphere) }, 0.2);
        var inside = new Point3d(5.1, 4.9, 5.2);
        Set(mesher, "Points", new GH_Point(inside));
        doc.Solve();
        NoErrors(doc, "no errors");
        var tets = Tetrahedra(mesher, 0);
        Check(Near(Volume(tets), sphere.Volume(), 0.02), $"{tets.Count} tetrahedra fill it: {Volume(tets):0.####} against the mesh's {sphere.Volume():0.####}");
        Check(tets.Any(t => t.Vertices.ToPoint3dArray().Any(p => p.DistanceTo(inside) < 1e-6)), "a node at the point inside");
        Check(Out(mesher, "Face Nodes").VolatileData.PathCount >= 1, $"Face Nodes has its {Out(mesher, "Face Nodes").VolatileData.PathCount} patches");
    }

    static void IntoTenNodeTetrahedron()
    {
        Section("The tetrahedra into Ten Node Tetrahedron, Face Nodes among its nodes");
        var box = new Box(new BoundingBox(0, 0, 0, 1, 0.5, 0.5)).ToBrep();
        var doc = new Doc();
        var mesher = MeshSolid(doc, new[] { new GH_Brep(box) }, 0.2);
        var material = doc.Add<SimplexGh.nD>("ElasticIsotropic (Alpaca4d)");
        var element = doc.Add<Alpaca4d.Gh.TenNodeTetrahedron>();
        Wire(mesher, "Tetrahedra", element, "Mesh");
        Wire(material, "Material", element, "Material");
        doc.Solve();
        NoErrors(doc, "Ten Node Tetrahedron takes every one of them");
        int tets = Tetrahedra(mesher, 0).Count;
        Check(All<IGH_Goo>(Out(element, "Element")).Count == tets, $"an element for each of the {tets}");
        var nodes = new HashSet<Point3d>(All<GH_Point>(Out(element, "Points")).Select(p => p.Value));
        int x0 = Enumerable.Range(0, box.Faces.Count).First(f => box.Faces[f].PointAt(box.Faces[f].Domain(0).Mid, box.Faces[f].Domain(1).Mid).X < 1e-9);
        var face = FaceNodes(mesher, 0, x0);
        Check(face.Count > 0 && face.All(nodes.Contains), $"every one of the {face.Count} Face Nodes at x = 0 is exactly one of the elements' ten nodes: a support there holds them all");
    }

    static void PlateWithHoles()
    {
        Section("Mesh Surface: a plate with two holes, a point, a line and a refined hole");
        var outline = new Rectangle3d(Plane.WorldXY, 4, 2).ToNurbsCurve();
        var holeA = new Circle(new Point3d(1, 1, 0), 0.3).ToNurbsCurve();
        var holeB = new Circle(new Point3d(3, 1, 0), 0.2).ToNurbsCurve();
        var plate = Brep.CreatePlanarBreps(new Curve[] { outline, holeA, holeB }, 1e-6)[0];
        var doc = new Doc();
        var mesher = doc.Add<Alpaca4d.Gh.MeshSurface>();
        Set(mesher, "Breps", new GH_Brep(plate));
        Set(mesher, "Size", new GH_Number(0.15));
        var column = new Point3d(0.4321, 0.5678, 0);
        Set(mesher, "Points", new GH_Point(column));
        Set(mesher, "Curves", new GH_Curve(new LineCurve(new Point3d(2, 0.2, 0), new Point3d(2, 1.8, 0))));
        var refinement = doc.Add<Alpaca4d.Gh.MeshRefinementComponent>();
        SetAny(refinement, "Geometry", new GH_Curve(holeA));
        Set(refinement, "Size", new GH_Number(0.03));
        Wire(refinement, "Refinement", mesher, "Refinements");
        doc.Solve();
        NoErrors(doc, "no errors");
        var mesh = All<GH_Mesh>(Out(mesher, "Mesh")).Single().Value;
        int quads = mesh.Faces.Count(f => f.IsQuad);
        Check(quads > 0.9 * mesh.Faces.Count, $"{quads} of its {mesh.Faces.Count} faces are quads");
        double area = AreaMassProperties.Compute(mesh).Area;
        Check(Near(area, AreaMassProperties.Compute(plate).Area, 5e-3), $"it covers the plate: {area:0.####} against {AreaMassProperties.Compute(plate).Area:0.####}");
        var vertices = mesh.Vertices.ToPoint3dArray();
        Check(vertices.Any(p => p.DistanceTo(column) < 1e-12), "a node exactly at the column point");
        Check(vertices.Count(p => Math.Abs(p.X - 2) < 1e-9 && p.Y > 0.2 - 1e-9 && p.Y < 1.8 + 1e-9) >= 10, "a line of nodes along the curve");
        mesh.FaceNormals.ComputeFaceNormals();
        Check(Enumerable.Range(0, mesh.Faces.Count).All(i => mesh.FaceNormals[i].Z > 0), "every face faces +z, as the plate does");
        double Size(int i) { var f = mesh.Faces[i]; return mesh.Vertices[f.A].DistanceTo(mesh.Vertices[f.B]); }
        var near = Enumerable.Range(0, mesh.Faces.Count).Where(i => mesh.Faces.GetFaceCenter(i).DistanceTo(new Point3d(1, 1, 0)) < 0.36).ToList();
        Check(near.Count > 0 && near.Average(Size) < 0.05, $"refined at the larger hole: {(near.Count > 0 ? near.Average(Size) : 0):0.###}");
        Check(All<GH_Number>(Out(mesher, "Quality")).Count == mesh.Faces.Count, "a Quality for each face");

        Section("The same plate turned over, as triangles");
        var flipped = plate.DuplicateBrep();
        flipped.Flip();
        var doc2 = new Doc();
        var mesher2 = doc2.Add<Alpaca4d.Gh.MeshSurface>();
        Set(mesher2, "Breps", new GH_Brep(flipped));
        Set(mesher2, "Size", new GH_Number(0.15));
        typeof(Alpaca4d.Gh.MeshSurface).GetField("_elements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(mesher2, Alpaca4d.Meshing.SurfaceElements.Triangles);
        doc2.Solve();
        NoErrors(doc2, "no errors");
        var triangles = All<GH_Mesh>(Out(mesher2, "Mesh")).Single().Value;
        triangles.FaceNormals.ComputeFaceNormals();
        Check(triangles.Faces.All(f => f.IsTriangle), $"{triangles.Faces.Count} triangles, nothing else");
        // Rhino turns a face over by flagging it reversed; BrepFace.NormalAt already counts the flag.
        flipped.ClosestPoint(new Point3d(0.5, 0.5, 0), out _, out ComponentIndex ci, out double s, out double t, 0.0, out Vector3d n);
        var f0 = flipped.Faces[ci.Index];
        Check(f0.OrientationIsReversed && f0.NormalAt(s, t).Z < 0 && n.Z < 0, "Rhino's normal of the turned face is -z, its reversed flag counted");
        int up = Enumerable.Range(0, triangles.Faces.Count).Count(i => triangles.FaceNormals[i].Z > 0);
        Check(up == 0, $"every one faces -z, as the turned plate does ({up} do not)");
    }

    /// <summary>
    /// A doubly curved NURBS surface, 6 x 4, with a round hole and a rounded rectangular one projected onto it
    /// and cut out; two points on it, an isocurve across it and a circle projected onto it to follow, and the
    /// holes' edges refined.
    /// </summary>
    static void FreeForm()
    {
        Section("Mesh Surface: a free-form NURBS surface with two holes, points, curves on it and refined hole edges");
        var grid = new List<Point3d>();
        for (int i = 0; i < 7; i++)
            for (int j = 0; j < 5; j++)
            {
                double x = i, y = j;
                grid.Add(new Point3d(x, y, 0.8 * Math.Sin(Math.PI * x / 6.0) * Math.Cos(Math.PI * (y - 2.0) / 5.0) + 0.15 * x * y / 6.0));
            }
        var surface = NurbsSurface.CreateThroughPoints(grid, 7, 5, 3, 3, false, false);
        var whole = surface.ToBrep();
        var round = new Circle(new Point3d(2, 2, -5), 0.5).ToNurbsCurve();
        var slot = Curve.CreateFilletCornersCurve(new Rectangle3d(new Plane(new Point3d(4.6, 1.5, -5), Vector3d.ZAxis),
            new Interval(-0.6, 0.6), new Interval(-0.3, 0.3)).ToNurbsCurve(), 0.15, 1e-6, 1e-3);
        var cutters = new[] { round, slot }.SelectMany(c => Curve.ProjectToBrep(c, whole, Vector3d.ZAxis, 1e-3) ?? new Curve[0]).ToList();
        var pieces = whole.Split(cutters, 1e-3) ?? new Brep[0];
        report.AppendLine($"  ({cutters.Count} cutters projected, {pieces.Length} pieces)");
        if (pieces.Length == 0)
        {
            Check(false, "the holes could be cut");
            return;
        }
        var brep = pieces.OrderByDescending(p => AreaMassProperties.Compute(p).Area).First();
        Check(brep.Faces.Count == 1 && brep.Loops.Count(l => l.LoopType == BrepLoopType.Inner) == 2,
              "the surface has its two holes, cut along their projections");

        double U(Point3d near) { surface.ClosestPoint(near, out double u, out double v); return u; }
        double V(Point3d near) { surface.ClosestPoint(near, out double u, out double v); return v; }
        var columns = new[] { surface.PointAt(U(new Point3d(1, 3, 0)), V(new Point3d(1, 3, 0))),
                              surface.PointAt(U(new Point3d(5.2, 0.7, 0)), V(new Point3d(5.2, 0.7, 0))) };
        var ridge = surface.IsoCurve(0, V(new Point3d(3, 3.5, 0)));
        var ring = Curve.ProjectToBrep(new Circle(new Point3d(4.6, 3.1, -5), 0.4).ToNurbsCurve(), brep, Vector3d.ZAxis, 1e-3)[0];

        var doc = new Doc();
        var mesher = doc.Add<Alpaca4d.Gh.MeshSurface>();
        Set(mesher, "Breps", new GH_Brep(brep));
        Set(mesher, "Size", new GH_Number(0.2));
        Set(mesher, "Points", columns.Select(p => new GH_Point(p)).ToArray());
        Set(mesher, "Curves", new GH_Curve(ridge), new GH_Curve(ring));
        var refinement = doc.Add<Alpaca4d.Gh.MeshRefinementComponent>();
        SetAny(refinement, "Geometry", brep.Loops.Where(l => l.LoopType == BrepLoopType.Inner).Select(l => (IGH_Goo)new GH_Curve(l.To3dCurve())).ToArray());
        Set(refinement, "Size", new GH_Number(0.05));
        Wire(refinement, "Refinement", mesher, "Refinements");
        doc.Solve();
        NoErrors(doc, "no errors");
        var warnings = doc.Messages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Count == 0, "no warnings" + (warnings.Count > 0 ? ": " + string.Join(" | ", warnings) : ""));
        report.AppendLine("  (" + string.Join(" | ", doc.Messages(GH_RuntimeMessageLevel.Remark)) + ")");

        var mesh = All<GH_Mesh>(Out(mesher, "Mesh")).Single().Value;
        var vertices = mesh.Vertices.ToPoint3dArray();
        int quads = mesh.Faces.Count(f => f.IsQuad);
        Check(quads > 0.9 * mesh.Faces.Count, $"{quads} of its {mesh.Faces.Count} faces are quads");
        double off = vertices.Max(p => brep.ClosestPoint(p).DistanceTo(p));
        // Within the model's tolerance: a hole Rhino cuts at 1 mm has its edge that far off the surface at most,
        // and the nodes along it lie on the edge.
        Check(off < 1e-3, $"every node lies on the NURBS surface, the farthest {off:g2} off it, on a hole's edge");
        double area = AreaMassProperties.Compute(mesh).Area, exact = AreaMassProperties.Compute(brep).Area;
        Check(Near(area, exact, 5e-3), $"it covers the surface: {area:0.####} against {exact:0.####}");
        Check(columns.All(c => vertices.Any(v => v.DistanceTo(c) < 1e-9)), "a node exactly at each point");
        int onRidge = vertices.Count(v => ridge.ClosestPoint(v, out double t) && ridge.PointAt(t).DistanceTo(v) < 1e-4);
        Check(onRidge >= ridge.GetLength() / 0.2, $"{onRidge} nodes along the isocurve, {ridge.GetLength():0.##} long");
        int onRing = vertices.Count(v => ring.ClosestPoint(v, out double t) && ring.PointAt(t).DistanceTo(v) < 1e-4);
        Check(onRing >= 0.8 * ring.GetLength() / 0.2, $"{onRing} nodes along the projected circle, {ring.GetLength():0.##} long");
        var holes = brep.Loops.Where(l => l.LoopType == BrepLoopType.Inner).Select(l => l.To3dCurve()).ToList();
        int onHoles = vertices.Count(v => holes.Any(h => h.ClosestPoint(v, out double t) && h.PointAt(t).DistanceTo(v) < 1e-4));
        double perimeter = holes.Sum(h => h.GetLength());
        Check(onHoles >= perimeter / 0.07, $"{onHoles} nodes round the holes' edges, {perimeter:0.##} long: refined to about {perimeter / Math.Max(1, onHoles):0.###}");

        mesh.FaceNormals.ComputeFaceNormals();
        int against = 0;
        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            var centre = mesh.Faces.GetFaceCenter(i);
            brep.ClosestPoint(centre, out _, out _, out _, out _, 0.0, out Vector3d normal);
            if (normal * (Vector3d)mesh.FaceNormals[i] < 0)
                against++;
        }
        Check(against == 0, $"every face faces the way the surface does ({against} do not)");
        var quality = All<GH_Number>(Out(mesher, "Quality")).Select(q => q.Value).ToList();
        int worst = quality.IndexOf(quality.Min());
        var far = vertices.OrderByDescending(p => brep.ClosestPoint(p).DistanceTo(p)).First();
        string Near3(Point3d p) => (ridge.ClosestPoint(p, out double a) && ridge.PointAt(a).DistanceTo(p) < 1e-3 ? "on the isocurve" :
                                    ring.ClosestPoint(p, out double b) && ring.PointAt(b).DistanceTo(p) < 1e-3 ? "on the circle" : "elsewhere");
        report.AppendLine($"  (worst element at {mesh.Faces.GetFaceCenter(worst)}, {Near3(mesh.Faces.GetFaceCenter(worst))}; farthest node at {far}, {Near3(far)})");
        Check(quality.Min() > 0.1, $"the worst is shaped {quality.Min():0.###}");

        // A picture of it, when grasshopper.sh is asked for one.
        if (File.Exists(Path.Combine(folder, "pictures")))
            Picture(mesh, brep, columns, new[] { ridge, ring }, Path.Combine(folder, "freeform.png"));
    }

    static void Picture(Mesh mesh, Brep brep, IEnumerable<Point3d> points, IEnumerable<Curve> curves, string path)
    {
        var rdoc = Rhino.RhinoDoc.ActiveDoc;
        foreach (var o in rdoc.Objects.ToList())
            rdoc.Objects.Delete(o, true);
        Rhino.DocObjects.ObjectAttributes Colour(System.Drawing.Color c) => new Rhino.DocObjects.ObjectAttributes
        {
            ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject, ObjectColor = c,
            PlotWeightSource = Rhino.DocObjects.ObjectPlotWeightSource.PlotWeightFromObject,
        };
        var shown = mesh.DuplicateMesh();
        shown.VertexColors.CreateMonotoneMesh(System.Drawing.Color.FromArgb(185, 208, 246));
        rdoc.Objects.AddMesh(shown, Colour(System.Drawing.Color.FromArgb(185, 208, 246)));
        var edges = mesh.TopologyEdges;
        for (int i = 0; i < edges.Count; i++)
        {
            var line = edges.EdgeLine(i);
            line.Transform(Transform.Translation(0, 0, 0.002));
            rdoc.Objects.AddLine(line, Colour(System.Drawing.Color.FromArgb(66, 136, 247)));
        }
        foreach (var c in curves)
        {
            var lifted = c.DuplicateCurve();
            lifted.Transform(Transform.Translation(0, 0, 0.004));
            rdoc.Objects.AddCurve(lifted, Colour(System.Drawing.Color.FromArgb(235, 108, 63)));
        }
        foreach (var p in points)
            rdoc.Objects.AddSphere(new Sphere(p, 0.05), Colour(System.Drawing.Color.FromArgb(158, 53, 218)));

        var view = rdoc.Views.ActiveView;
        view.Maximized = true;
        var vp = view.ActiveViewport;
        vp.ChangeToPerspectiveProjection(true, 50.0);
        var box = brep.GetBoundingBox(true);
        vp.SetCameraLocations(box.Center, box.Center + new Vector3d(-0.6, -1.1, 0.9) * box.Diagonal.Length * 1.6);
        box.Inflate(box.Diagonal.Length * 0.03);
        vp.ZoomBoundingBox(box);
        var modes = Rhino.Display.DisplayModeDescription.GetDisplayModes();
        var mode = modes.First(m => m.EnglishName == "Shaded");
        vp.ConstructionGridVisible = false;
        vp.ConstructionAxesVisible = false;
        vp.WorldAxesVisible = false;
        vp.DisplayMode = mode;
        view.Redraw();
        int width = 2400, height = (int)Math.Round(width * vp.Size.Height / (double)vp.Size.Width);
        view.CaptureToBitmap(new System.Drawing.Size(width, height), mode).Dispose();
        view.Redraw();
        view.CaptureToBitmap(new System.Drawing.Size(width, height), mode).Save(path);
        report.AppendLine("  (drew " + path + ")");
    }

    static void ShellsOfAClosedPolysurface()
    {
        Section("Mesh Surface: a closed box as shells, under a slab");
        var core = new Box(new BoundingBox(0, 0, 0, 2, 2, 3)).ToBrep();
        var slab = Brep.CreatePlanarBreps(new Rectangle3d(new Plane(new Point3d(-2, -2, 3), Vector3d.ZAxis), 6, 6).ToNurbsCurve(), 1e-6)[0];
        var doc = new Doc();
        var mesher = doc.Add<Alpaca4d.Gh.MeshSurface>();
        Set(mesher, "Breps", new GH_Brep(core), new GH_Brep(slab));
        Set(mesher, "Size", new GH_Number(0.5));
        doc.Solve();
        NoErrors(doc, "no errors");
        var mesh = All<GH_Mesh>(Out(mesher, "Mesh")).Single().Value;
        double area = AreaMassProperties.Compute(mesh).Area;
        Check(Near(area, 6 * 4 + 2 * 4 + 36 - 4, 1e-9), $"the core's sides and the slab, the top shared: {area:0.###} against 64");
        var edges = mesh.TopologyEdges;
        int nonManifold = Enumerable.Range(0, edges.Count).Count(i => edges.GetConnectedFaces(i).Length == 3);
        Check(nonManifold > 0, $"the walls meet the slab at {nonManifold} edges with three faces each: joined, not just touching");
        mesh.FaceNormals.ComputeFaceNormals();
        var wallX0 = Enumerable.Range(0, mesh.Faces.Count).Where(i => Math.Abs(mesh.Faces.GetFaceCenter(i).X) < 1e-9).ToList();
        Check(wallX0.Count > 0 && wallX0.All(i => mesh.FaceNormals[i].X < 0), "the wall at x = 0 faces out of the box, as the closed Brep's face does");
    }

    static void Problems()
    {
        Section("What Mesh Solid refuses");
        var open = Brep.CreatePlanarBreps(new Rectangle3d(Plane.WorldXY, 1, 1).ToNurbsCurve(), 1e-6)[0];
        var doc = new Doc();
        var mesher = MeshSolid(doc, new[] { new GH_Brep(open) }, 0.2);
        Set(mesher, "Points", new GH_Point(new Point3d(5, 5, 5)));
        doc.Solve();
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Any(e => e.Contains("not closed")), "an open Brep is an error that says so: " + string.Join(" | ", errors));

        var box = new Box(new BoundingBox(0, 0, 0, 1, 1, 1)).ToBrep();
        var doc2 = new Doc();
        var mesher2 = MeshSolid(doc2, new[] { new GH_Brep(box) }, 0.3);
        Set(mesher2, "Points", new GH_Point(new Point3d(5, 5, 5)));
        doc2.Solve();
        var warnings = doc2.Messages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Any(w => w.Contains("Point 0") && w.Contains("left out")), "a point outside every solid is a warning: " + string.Join(" | ", warnings));
        Check(Tetrahedra(mesher2, 0).Count > 0, "and the box is meshed all the same");
    }
}
