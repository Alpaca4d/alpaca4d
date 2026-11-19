using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino.Geometry;

namespace Alpaca4d.Meshing
{
    /// <summary>
    /// What <see cref="SolidMesher"/> meshes: closed Breps, already written to STEP files, and closed
    /// meshes, already written to STL files, with what the mesh must have and where it must be finer.
    ///
    /// The questions only Rhino can answer about the geometry - which Brep a point is in, which of
    /// its faces a triangle lies on - are asked through the functions, so the mesher itself needs no
    /// more of Rhino than its points and vectors.
    /// </summary>
    public sealed class SolidMeshJob
    {
        /// <summary>The Breps, in their solids' order: the solid each is, its STEP file and its elements' size.</summary>
        public List<(int Solid, string StepFile, double Size)> Breps { get; } = new List<(int, string, double)>();

        /// <summary>The closed meshes: the solid each is, its STL file and its elements' size. Each is meshed on its own.</summary>
        public List<(int Solid, string StlFile, double Size)> Meshes { get; } = new List<(int, string, double)>();

        /// <summary>How many solids there are, Breps and meshes together.</summary>
        public int SolidCount { get; set; }

        /// <summary>Points inside or on the Breps that the mesh must have a node at.</summary>
        public List<Point3d> BrepPoints { get; } = new List<Point3d>();

        /// <summary>For each closed mesh, by its solid: the points inside it the mesh must have a node at.</summary>
        public Dictionary<int, List<Point3d>> PointsInside { get; } = new Dictionary<int, List<Point3d>>();

        /// <summary>
        /// For each closed mesh, by its solid: points on its surface. Its surface is remeshed, so no
        /// point on it can be held; the nearest node is moved onto each instead, as Mesh Brep does.
        /// </summary>
        public Dictionary<int, List<Point3d>> PointsOnSurface { get; } = new Dictionary<int, List<Point3d>>();

        public List<MeshRefinement> Refinements { get; } = new List<MeshRefinement>();

        /// <summary>The model's tolerance: within it, points are on a surface and solids touch.</summary>
        public double Tolerance { get; set; } = 1e-3;

        /// <summary>A closed mesh's edges that fold by more than this many degrees stay sharp.</summary>
        public double FeatureAngle { get; set; } = 40.0;

        /// <summary>Which Brep solid a point inside the Breps is in: the first that holds it.</summary>
        public Func<Point3d, int> SolidOf { get; set; }

        /// <summary>
        /// Which face of Brep solid <c>solid</c> the triangle lies on, by the face's index in the
        /// Brep, or -1 when it lies on none of them - a surface where two Breps overlap.
        /// </summary>
        public Func<int, Point3d[], int> FaceOf { get; set; }

        /// <summary>How many faces Brep solid <c>solid</c> has.</summary>
        public Func<int, int> FaceCount { get; set; }
    }

    /// <summary>The tetrahedra of each solid, the nodes on each of its faces and how well shaped they are.</summary>
    public sealed class SolidMesh
    {
        public List<Point3d> Nodes { get; } = new List<Point3d>();

        /// <summary>For each solid, its tetrahedra, as four indices into <see cref="Nodes"/>.</summary>
        public List<List<int[]>> Tetrahedra { get; } = new List<List<int[]>>();

        /// <summary>For each solid, its tetrahedra's <see cref="MeshQuality.Tetrahedron"/>.</summary>
        public List<List<double>> Quality { get; } = new List<List<double>>();

        /// <summary>
        /// For each solid and each of its faces - a Brep's faces in their order, a mesh's patches in
        /// gmsh's - the nodes on it: the tetrahedra's corners, then the middles of their edges, where
        /// a ten node tetrahedron puts its other six nodes.
        /// </summary>
        public List<List<List<Point3d>>> FaceNodes { get; } = new List<List<List<Point3d>>>();

        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Why a solid has no mesh, by solid; nothing for those that have one.</summary>
        public Dictionary<int, string> Failures { get; } = new Dictionary<int, string>();
    }

    /// <summary>
    /// Fills closed Breps and closed meshes with four node tetrahedra, by gmsh.
    ///
    /// The Breps are meshed in one go. gmsh's OpenCASCADE kernel cuts them into fragments where they
    /// touch or overlap, so the mesh is one, with its nodes shared across every interface; the
    /// inclusion points are cut in with them, which puts a node at each. A fragment inside two Breps
    /// goes to the first.
    ///
    /// Each closed mesh is meshed on its own: gmsh splits its surface into patches at its sharp
    /// edges, remeshes each, and fills the whole. Two meshes that touch do not share nodes.
    ///
    /// gmsh's Netgen optimiser is left off: with a point held inside a volume, it ends gmsh with a bus
    /// error (gmsh 4.15). HXT, the 3D algorithm, shapes the tetrahedra well without it.
    /// </summary>
    public static class SolidMesher
    {
        public static SolidMesh Create(SolidMeshJob job, string gmsh, string workFolder, Func<bool> stop = null)
        {
            var result = new SolidMesh();
            for (int i = 0; i < job.SolidCount; i++)
            {
                result.Tetrahedra.Add(new List<int[]>());
                result.Quality.Add(new List<double>());
                result.FaceNodes.Add(new List<List<Point3d>>());
            }

            if (job.Breps.Count > 0)
            {
                var run = Gmsh.Run(gmsh, Path.Combine(workFolder, "breps"), BrepScript(job), 3, stop);
                if (Collect(run, gmsh, result, job.Breps.Select(b => b.Solid).ToList()))
                    TakeBreps(MshFile.Read(run.MeshFile), job, result);
                Tidy(run);
                if (run.Stopped)
                    return result;
            }

            foreach (var mesh in job.Meshes)
            {
                var run = Gmsh.Run(gmsh, Path.Combine(workFolder, $"mesh{mesh.Solid}"), MeshScript(job, mesh.Solid, mesh.StlFile, mesh.Size), 3, stop);
                if (Collect(run, gmsh, result, new List<int> { mesh.Solid }))
                    TakeMesh(MshFile.Read(run.MeshFile), job, mesh.Solid, result);
                Tidy(run);
                if (run.Stopped)
                    return result;
            }

            return result;
        }

        /// <summary>Records gmsh's warnings, and its failure against each of <paramref name="solids"/>; whether it made a mesh.</summary>
        internal static bool Collect(GmshRun run, string gmsh, SolidMesh result, List<int> solids)
        {
            foreach (var warning in run.Log.Warnings.Distinct())
                result.Warnings.Add("gmsh: " + warning);
            if (run.Succeeded && run.Log.Errors.Count == 0)
                return true;
            string failure = run.Succeeded
                ? "gmsh had errors: " + string.Join(" | ", run.Log.Errors.Distinct()) + $" Its script is {run.ScriptFile}."
                : run.Failure(gmsh);
            foreach (var solid in solids)
                result.Failures[solid] = failure;
            return false;
        }

        /// <summary>Removes a run's files once it made its mesh. A failed run's stay, for the user to open in gmsh.</summary>
        internal static void Tidy(GmshRun run)
        {
            if (!run.Succeeded || run.Log.Errors.Count > 0)
                return;
            try { Directory.Delete(run.Folder, true); }
            catch (Exception) { }
        }

        internal static string BrepScript(SolidMeshJob job)
        {
            var geo = new GeoScript();
            geo.Line("// Alpaca4d: closed Breps filled with tetrahedra.");
            geo.Line("SetFactory(\"OpenCASCADE\");");
            // The STEP files are written in millimetres whatever the model's unit, and read as
            // millimetres, so that the numbers come through as they are.
            geo.Line("Geometry.OCCTargetUnit = \"MM\";");
            geo.Line($"Geometry.ToleranceBoolean = {GeoScript.F(job.Tolerance)};");

            // Faces that come in sewn, but not as a solid, are closed into one.
            geo.Line("Geometry.OCCSewFaces = 1;");

            var names = new List<string>();
            foreach (var brep in job.Breps)
            {
                string name = $"solid{brep.Solid}";
                names.Add(name + "()");
                geo.Line("before() = Volume{:};");
                geo.Line("beforeFaces() = Surface{:};");
                geo.Line($"imported() = ShapeFromFile(\"{GeoScript.Path(Path.GetFullPath(brep.StepFile))}\");");
                geo.Line($"{name}() = Volume{{:}};");
                geo.Line($"{name}() -= before();");
                geo.Line($"If (#{name}() == 0)");
                geo.Line("  faces() = Surface{:};");
                geo.Line("  faces() -= beforeFaces();");
                geo.Line("  loop = newsl;");
                geo.Line("  Surface Loop(loop) = {faces()};");
                geo.Line("  volume = newv;");
                geo.Line("  Volume(volume) = {loop};");
                geo.Line($"  {name}() = {{volume}};");
                geo.Line("EndIf");
                geo.Line($"MeshSize{{ PointsOf{{ Volume{{{name}()}}; }} }} = {GeoScript.F(brep.Size)};");
            }

            string volumes = string.Join(", ", names);
            if (job.BrepPoints.Count > 0)
            {
                string points = geo.Points(job.BrepPoints, GeoScript.FirstInclusionPoint);
                geo.Line($"BooleanFragments{{ Volume{{{volumes}}}; Delete; }}{{ Point{{{points}}}; Delete; }}");
            }
            else if (job.Breps.Count > 1)
            {
                geo.Line($"BooleanFragments{{ Volume{{{volumes}}}; Delete; }}{{ }}");
            }

            double coarsest = job.Breps.Max(b => b.Size);
            geo.Refinements(job.Refinements, coarsest);
            MeshOptions(geo, coarsest);
            return geo.ToString();
        }

        internal static string MeshScript(SolidMeshJob job, int solid, string stlFile, double size)
        {
            var geo = new GeoScript();
            geo.Line("// Alpaca4d: a closed mesh, its surface remeshed, filled with tetrahedra.");
            geo.Line($"Merge \"{GeoScript.Path(Path.GetFullPath(stlFile))}\";");
            geo.Line($"ClassifySurfaces{{{GeoScript.F(job.FeatureAngle)} * Pi / 180, 1, 1, Pi}};");
            geo.Line("CreateGeometry;");
            geo.Line("Surface Loop(1) = Surface{:};");
            geo.Line("Volume(1) = {1};");
            if (job.PointsInside.TryGetValue(solid, out var inside) && inside.Count > 0)
            {
                string points = geo.Points(inside, GeoScript.FirstInclusionPoint);
                geo.Line($"Point{{{points}}} In Volume{{1}};");
            }
            geo.Refinements(job.Refinements, size);
            MeshOptions(geo, size);
            return geo.ToString();
        }

        private static void MeshOptions(GeoScript geo, double meshSize)
        {
            geo.Line($"Mesh.MeshSizeMax = {GeoScript.F(meshSize)};");
            geo.Line("Mesh.MeshSizeExtendFromBoundary = 1;");
            geo.Line("Mesh.MeshSizeFromPoints = 1;");
            geo.Line("Mesh.Algorithm = 6;");
            // HXT, gmsh's parallel Delaunay: its worst tetrahedra are far better shaped than the plain
            // Delaunay's (0.38 against 0.06 on the femur example), and it keeps the held points.
            geo.Line("Mesh.Algorithm3D = 10;");
            geo.Line("Mesh.Optimize = 1;");
            geo.Line("Mesh.OptimizeNetgen = 0;");
            geo.Line("Mesh.ElementOrder = 1;");
        }

        /// <summary>The tetrahedra of the Breps' run, given to the solids their volumes are in.</summary>
        private static void TakeBreps(MshFile msh, SolidMeshJob job, SolidMesh result)
        {
            var nodes = new NodeMap(msh, result);
            var volumeSolid = new Dictionary<int, int>();

            foreach (var tet in msh.Elements.Where(e => e.Type == MshFile.Tetrahedron))
            {
                if (!volumeSolid.TryGetValue(tet.Entity, out int solid))
                {
                    var corners = tet.Nodes.Select(n => msh.Nodes[n]).ToArray();
                    var centre = (corners[0] + corners[1] + corners[2] + corners[3]) / 4.0;
                    solid = job.SolidOf?.Invoke(centre) ?? job.Breps[0].Solid;
                    if (solid < 0)
                        solid = job.Breps[0].Solid;
                    volumeSolid[tet.Entity] = solid;
                }
                AddTetrahedron(tet, solid, nodes, result);
            }

            foreach (var brep in job.Breps)
            {
                int count = job.FaceCount?.Invoke(brep.Solid) ?? 0;
                for (int f = 0; f < count; f++)
                    result.FaceNodes[brep.Solid].Add(new List<Point3d>());
            }

            var triangles = Triangles(msh);
            foreach (var volume in volumeSolid)
            {
                int solid = volume.Value;
                if (!msh.VolumeSurfaces.TryGetValue(volume.Key, out var surfaces))
                    continue;
                foreach (var surface in surfaces)
                {
                    if (!triangles.TryGetValue(surface, out var onIt) || onIt.Count == 0)
                        continue;
                    var largest = onIt.OrderByDescending(t => Area(msh, t)).First();
                    int face = job.FaceOf?.Invoke(solid, largest.Nodes.Select(n => msh.Nodes[n]).ToArray()) ?? -1;
                    if (face < 0 || face >= result.FaceNodes[solid].Count)
                        continue;
                    AddFaceNodes(onIt, nodes, result.FaceNodes[solid][face]);
                }
            }

            foreach (var face in result.FaceNodes.SelectMany(f => f))
                Distinct(face);
        }

        /// <summary>The tetrahedra of a closed mesh's run, and the nodes on each patch of its surface.</summary>
        private static void TakeMesh(MshFile msh, SolidMeshJob job, int solid, SolidMesh result)
        {
            // The points on its surface: the nearest surface node is moved onto each.
            if (job.PointsOnSurface.TryGetValue(solid, out var onSurface) && onSurface.Count > 0)
            {
                var surfaceNodes = msh.Elements.Where(e => e.Type == MshFile.Triangle).SelectMany(e => e.Nodes).Distinct().ToList();
                foreach (var point in onSurface)
                {
                    long nearest = surfaceNodes.OrderBy(n => msh.Nodes[n].DistanceToSquared(point)).First();
                    msh.Nodes[nearest] = point;
                }
            }

            var nodes = new NodeMap(msh, result);
            foreach (var tet in msh.Elements.Where(e => e.Type == MshFile.Tetrahedron))
                AddTetrahedron(tet, solid, nodes, result);

            var triangles = Triangles(msh);
            var patches = msh.VolumeSurfaces.Values.SelectMany(s => s).Distinct().OrderBy(s => s).ToList();
            foreach (var patch in patches)
            {
                var face = new List<Point3d>();
                if (triangles.TryGetValue(patch, out var onIt))
                    AddFaceNodes(onIt, nodes, face);
                Distinct(face);
                result.FaceNodes[solid].Add(face);
            }
        }

        private static void AddTetrahedron(MshElement tet, int solid, NodeMap nodes, SolidMesh result)
        {
            var indices = tet.Nodes.Select(nodes.Index).ToArray();
            result.Tetrahedra[solid].Add(indices);
            var p = indices.Select(i => result.Nodes[i]).ToArray();
            result.Quality[solid].Add(MeshQuality.Tetrahedron(p[0], p[1], p[2], p[3]));
        }

        /// <summary>The corners of <paramref name="triangles"/>, then the middles of their edges.</summary>
        private static void AddFaceNodes(List<MshElement> triangles, NodeMap nodes, List<Point3d> face)
        {
            var corners = new HashSet<int>();
            var edges = new HashSet<(int, int)>();
            foreach (var triangle in triangles)
            {
                var t = triangle.Nodes.Select(nodes.Index).ToArray();
                for (int i = 0; i < 3; i++)
                {
                    corners.Add(t[i]);
                    int a = t[i], b = t[(i + 1) % 3];
                    edges.Add(a < b ? (a, b) : (b, a));
                }
            }
            foreach (var corner in corners.OrderBy(c => c))
                face.Add(nodes.Point(corner));
            foreach (var edge in edges.OrderBy(e => e.Item1).ThenBy(e => e.Item2))
                face.Add(0.5 * (nodes.Point(edge.Item1) + nodes.Point(edge.Item2)));
        }

        /// <summary>Drops repeats, which a face split in two by a fragment can give along the split.</summary>
        private static void Distinct(List<Point3d> points)
        {
            var seen = new HashSet<Point3d>();
            points.RemoveAll(p => !seen.Add(p));
        }

        private static Dictionary<int, List<MshElement>> Triangles(MshFile msh) =>
            msh.Elements.Where(e => e.Type == MshFile.Triangle)
                        .GroupBy(e => e.Entity)
                        .ToDictionary(g => g.Key, g => g.ToList());

        private static double Area(MshFile msh, MshElement triangle)
        {
            var a = msh.Nodes[triangle.Nodes[0]];
            var b = msh.Nodes[triangle.Nodes[1]];
            var c = msh.Nodes[triangle.Nodes[2]];
            return Vector3d.CrossProduct(b - a, c - a).Length;
        }

        /// <summary>gmsh's node tags numbered into the result's nodes, as they are first used.</summary>
        private sealed class NodeMap
        {
            private readonly MshFile msh;
            private readonly SolidMesh result;
            private readonly Dictionary<long, int> index = new Dictionary<long, int>();

            public NodeMap(MshFile msh, SolidMesh result)
            {
                this.msh = msh;
                this.result = result;
            }

            public int Index(long tag)
            {
                if (!index.TryGetValue(tag, out int i))
                {
                    i = result.Nodes.Count;
                    index[tag] = i;
                    result.Nodes.Add(msh.Nodes[tag]);
                }
                return i;
            }

            public Point3d Point(int i) => result.Nodes[i];
        }
    }
}
