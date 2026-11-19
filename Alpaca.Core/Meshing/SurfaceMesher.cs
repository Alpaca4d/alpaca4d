using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino.Geometry;

namespace Alpaca4d.Meshing
{
    /// <summary>The elements a surface mesh is made of.</summary>
    public enum SurfaceElements
    {
        /// <summary>Quadrangles, with a few triangles where the mesh grades and no pairing is left.</summary>
        Quads,
        Triangles,
    }

    /// <summary>
    /// What <see cref="SurfaceMesher"/> meshes: surfaces and polysurfaces, already written to STEP
    /// files, the points and curves the mesh must follow, and where it must be finer.
    /// </summary>
    public sealed class SurfaceMeshJob
    {
        /// <summary>The Breps: each one's STEP file and its elements' size.</summary>
        public List<(string StepFile, double Size)> Breps { get; } = new List<(string, double)>();

        /// <summary>Curves the mesh must have a line of edges along.</summary>
        public List<InclusionCurve> Curves { get; } = new List<InclusionCurve>();

        /// <summary>Points on the surfaces the mesh must have a node at.</summary>
        public List<Point3d> Points { get; } = new List<Point3d>();

        public List<MeshRefinement> Refinements { get; } = new List<MeshRefinement>();

        public SurfaceElements Elements { get; set; } = SurfaceElements.Quads;

        public double Tolerance { get; set; } = 1e-3;

        /// <summary>
        /// The normal of the surfaces at the point nearest a point on them, so the elements face the
        /// way their surface does; Vector3d.Zero when that cannot be told.
        /// </summary>
        public Func<Point3d, Vector3d> NormalAt { get; set; }
    }

    /// <summary>A surface mesh: its nodes, its faces of three or four nodes, and how well shaped they are.</summary>
    public sealed class SurfaceMesh
    {
        public List<Point3d> Nodes { get; } = new List<Point3d>();
        public List<int[]> Faces { get; } = new List<int[]>();
        public List<double> Quality { get; } = new List<double>();
        public List<string> Warnings { get; } = new List<string>();
        public string Failure { get; set; }
        public bool Stopped { get; set; }
    }

    /// <summary>
    /// Meshes surfaces and polysurfaces for shell elements, by gmsh: quadrangles by default, paired
    /// from its triangles, or triangles.
    ///
    /// All the Breps are meshed in one go: gmsh's OpenCASCADE kernel cuts them into fragments where
    /// they touch, so faces that meet share the nodes along the line where they do. The inclusion
    /// points and curves are cut in with them, which puts a node at each point and a line of element
    /// edges along each curve.
    /// </summary>
    public static class SurfaceMesher
    {
        public static SurfaceMesh Create(SurfaceMeshJob job, string gmsh, string workFolder, Func<bool> stop = null)
        {
            var result = new SurfaceMesh();
            var run = Gmsh.Run(gmsh, Path.Combine(workFolder, "surfaces"), Script(job), 2, stop);
            foreach (var warning in run.Log.Warnings.Distinct())
                result.Warnings.Add("gmsh: " + warning);

            if (!run.Succeeded || run.Log.Errors.Count > 0)
            {
                result.Stopped = run.Stopped;
                result.Failure = run.Succeeded
                    ? "gmsh had errors: " + string.Join(" | ", run.Log.Errors.Distinct()) + $" Its script is {run.ScriptFile}."
                    : run.Failure(gmsh);
                return result;
            }

            Take(MshFile.Read(run.MeshFile), job, result);
            SolidMesher.Tidy(run);
            return result;
        }

        internal static string Script(SurfaceMeshJob job)
        {
            var geo = new GeoScript();
            geo.Line("// Alpaca4d: surfaces meshed for shells.");
            geo.Line("SetFactory(\"OpenCASCADE\");");
            geo.Line("Geometry.OCCTargetUnit = \"MM\";");
            geo.Line($"Geometry.ToleranceBoolean = {GeoScript.F(job.Tolerance)};");

            // A closed polysurface comes out of STEP as a solid. Its faces are taken - the surfaces
            // that were not there before it - and the solid itself dropped, leaving them.
            var surfaces = new List<string>();
            for (int i = 0; i < job.Breps.Count; i++)
            {
                string name = $"brep{i}";
                surfaces.Add(name + "()");
                geo.Line("before() = Surface{:};");
                geo.Line($"imported() = ShapeFromFile(\"{GeoScript.Path(Path.GetFullPath(job.Breps[i].StepFile))}\");");
                geo.Line($"{name}() = Surface{{:}};");
                geo.Line($"{name}() -= before();");
                geo.Line($"MeshSize{{ PointsOf{{ Surface{{{name}()}}; }} }} = {GeoScript.F(job.Breps[i].Size)};");
            }
            geo.Line("solids() = Volume{:};");
            geo.Line("If (#solids() > 0)");
            geo.Line("  Delete{ Volume{solids()}; }");
            geo.Line("EndIf");

            var tools = new List<string>();
            if (job.Points.Count > 0)
                tools.Add($"Point{{{geo.Points(job.Points, GeoScript.FirstInclusionPoint)}}};");
            var curves = geo.Curves(job.Curves);
            if (curves.Count > 0)
                tools.Add($"Curve{{{string.Join(", ", curves)}}};");

            if (tools.Count > 0 || job.Breps.Count > 1)
                geo.Line($"BooleanFragments{{ Surface{{{string.Join(", ", surfaces)}}}; Delete; }}{{ {string.Join(" ", tools)} Delete; }}");

            double coarsest = job.Breps.Max(b => b.Size);
            geo.Refinements(job.Refinements, coarsest);

            geo.Line($"Mesh.MeshSizeMax = {GeoScript.F(coarsest)};");
            geo.Line("Mesh.MeshSizeExtendFromBoundary = 1;");
            geo.Line("Mesh.MeshSizeFromPoints = 1;");
            if (job.Elements == SurfaceElements.Quads)
            {
                // Frontal-Delaunay for quads lays its triangles out in right-angled pairs for the
                // Blossom recombination to join.
                geo.Line("Mesh.Algorithm = 8;");
                geo.Line("Mesh.RecombineAll = 1;");
                geo.Line("Mesh.RecombinationAlgorithm = 1;");
                // Two triangles become a quad only if it is fairly shaped: gmsh's default, 0.01, let a
                // quad with a corner of almost 180 degrees through, beside a curve on a free-form surface.
                geo.Line("Mesh.RecombineMinimumQuality = 0.2;");
            }
            else
            {
                geo.Line("Mesh.Algorithm = 6;");
            }
            geo.Line("Mesh.Optimize = 1;");
            geo.Line("Mesh.Smoothing = 5;");
            geo.Line("Mesh.ElementOrder = 1;");
            return geo.ToString();
        }

        private static void Take(MshFile msh, SurfaceMeshJob job, SurfaceMesh result)
        {
            var index = new Dictionary<long, int>();
            int Index(long tag)
            {
                if (!index.TryGetValue(tag, out int i))
                {
                    i = result.Nodes.Count;
                    index[tag] = i;
                    result.Nodes.Add(msh.Nodes[tag]);
                }
                return i;
            }

            foreach (var entity in msh.Elements.Where(e => e.Type == MshFile.Triangle || e.Type == MshFile.Quadrangle)
                                               .GroupBy(e => e.Entity))
            {
                // gmsh orients a surface's elements by its own idea of the surface, which the trip
                // through STEP can turn over. Shells take their local axes from the way the face
                // turns, so each surface's elements are made to face the way the Brep does.
                bool flip = false;
                if (job.NormalAt != null)
                {
                    var largest = entity.OrderByDescending(e => Normal(msh, e).Length).First();
                    var normal = Normal(msh, largest);
                    var centre = Centre(msh, largest);
                    var expected = job.NormalAt(centre);
                    flip = expected.IsValid && !expected.IsZero && normal * expected < 0;
                }

                foreach (var element in entity)
                {
                    var face = element.Nodes.Select(Index).ToArray();
                    if (flip)
                        Array.Reverse(face);
                    result.Faces.Add(face);
                    var p = face.Select(i => result.Nodes[i]).ToArray();
                    result.Quality.Add(p.Length == 3
                        ? MeshQuality.Triangle(p[0], p[1], p[2])
                        : MeshQuality.Quadrangle(p[0], p[1], p[2], p[3]));
                }
            }
        }

        private static Vector3d Normal(MshFile msh, MshElement element)
        {
            var p = element.Nodes.Select(n => msh.Nodes[n]).ToArray();
            return p.Length == 3
                ? Vector3d.CrossProduct(p[1] - p[0], p[2] - p[0])
                : Vector3d.CrossProduct(p[2] - p[0], p[3] - p[1]);
        }

        private static Point3d Centre(MshFile msh, MshElement element)
        {
            var sum = Point3d.Origin;
            foreach (var n in element.Nodes)
                sum += msh.Nodes[n];
            return sum / element.Nodes.Length;
        }
    }
}
