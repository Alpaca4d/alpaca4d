using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.FileIO;
using Rhino.Geometry;

namespace Alpaca4d.Meshing
{
    /// <summary>
    /// The Rhino side of meshing with gmsh: the geometry written to files gmsh reads, the inclusion
    /// points sorted by where they are, and the answers about the geometry the meshers ask for.
    /// Uses Rhino's native code, so it runs inside Rhino only.
    /// </summary>
    public static class RhinoGmsh
    {
        /// <summary>A folder of its own for one meshing, under the system's temporary folder.</summary>
        public static string NewWorkFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Alpaca4d-gmsh", Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>Deletes a work folder, when nothing in it is worth keeping.</summary>
        public static void Remove(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (Exception) { }
        }

        /// <summary>
        /// Writes <paramref name="geometry"/> to a STEP file through a document of its own. The
        /// document is in millimetres whatever the model's unit, and gmsh reads the file in
        /// millimetres, so the numbers come through as they are.
        /// </summary>
        public static void WriteStep(string path, IEnumerable<GeometryBase> geometry, double tolerance)
        {
            using (var doc = RhinoDoc.CreateHeadless(null))
            {
                doc.ModelUnitSystem = UnitSystem.Millimeters;
                doc.ModelAbsoluteTolerance = tolerance;
                foreach (var item in geometry)
                {
                    if (doc.Objects.Add(item) == Guid.Empty)
                        throw new Exception("Rhino would not take this geometry into the file for gmsh.");
                }
                if (!FileStp.Write(path, doc, new FileStpWriteOptions()) || !File.Exists(path))
                    throw new Exception("Rhino could not write the STEP file gmsh reads the geometry from.");
            }
        }

        /// <summary>Writes <paramref name="mesh"/>'s faces to a binary STL file, quads as two triangles.</summary>
        public static void WriteStl(string path, Mesh mesh)
        {
            var triangles = new List<(Point3d, Point3d, Point3d)>();
            var v = mesh.Vertices;
            foreach (var face in mesh.Faces)
            {
                triangles.Add((v[face.A], v[face.B], v[face.C]));
                if (face.IsQuad)
                    triangles.Add((v[face.A], v[face.C], v[face.D]));
            }

            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new byte[80]);
                writer.Write((uint)triangles.Count);
                foreach (var (a, b, c) in triangles)
                {
                    var normal = Vector3d.CrossProduct(b - a, c - a);
                    normal.Unitize();
                    foreach (var value in new[] { normal.X, normal.Y, normal.Z, a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z })
                        writer.Write((float)value);
                    writer.Write((ushort)0);
                }
            }
        }

        /// <summary>
        /// STL holds single precision only, so a closed mesh's numbers are rounded on the way to gmsh.
        /// Far from the origin that rounding is coarser than the model's tolerance; this says by how much.
        /// </summary>
        public static double StlRounding(Mesh mesh)
        {
            var box = mesh.GetBoundingBox(false);
            double largest = new[] { box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z }.Max(Math.Abs);
            return largest * 6e-8;
        }

        /// <summary>
        /// The job for <see cref="SolidMesher"/>: each solid written to its file, the points sorted
        /// into the solids they are in or on, and the questions about the Breps answered from them.
        /// A solid that is not closed is reported in <paramref name="errors"/>, by its index, and left out.
        /// </summary>
        public static SolidMeshJob SolidJob(IList<GeometryBase> solids, IList<double> sizes, IList<Point3d> points,
            IList<MeshRefinement> refinements, double tolerance, double featureAngle, string folder,
            List<string> warnings, Dictionary<int, string> errors)
        {
            var job = new SolidMeshJob
            {
                SolidCount = solids.Count,
                Tolerance = tolerance,
                FeatureAngle = featureAngle,
            };
            job.Refinements.AddRange(refinements.Where(r => r != null));

            var breps = new Dictionary<int, Brep>();
            var meshes = new Dictionary<int, Mesh>();
            for (int i = 0; i < solids.Count; i++)
            {
                double size = sizes[Math.Min(i, sizes.Count - 1)];
                switch (solids[i])
                {
                    case Brep brep when brep.IsSolid:
                        var solid = brep.DuplicateBrep();
                        if (solid.SolidOrientation == BrepSolidOrientation.Inward)
                            solid.Flip();
                        breps[i] = solid;
                        string step = Path.Combine(folder, $"solid{i}.step");
                        WriteStep(step, new[] { solid }, tolerance);
                        job.Breps.Add((i, step, size));
                        break;
                    case Brep _:
                        errors[i] = $"Solid {i} is a Brep that is not closed. Mesh Solid fills closed ones; mesh a surface for shells with Mesh Surface.";
                        break;
                    case Mesh mesh when mesh.IsClosed:
                        meshes[i] = mesh;
                        string stl = Path.Combine(folder, $"solid{i}.stl");
                        WriteStl(stl, mesh);
                        job.Meshes.Add((i, stl, size));
                        if (StlRounding(mesh) > tolerance)
                            warnings.Add($"Solid {i} is far from the origin: gmsh reads meshes in single precision, " +
                                         $"so its points move by up to {StlRounding(mesh):g3}, more than the model's tolerance.");
                        break;
                    case Mesh _:
                        errors[i] = $"Solid {i} is a mesh with holes in it. Mesh Solid fills closed meshes; close it first (Rhino's FillMeshHoles).";
                        break;
                    default:
                        errors[i] = $"Solid {i} is neither a closed Brep nor a closed mesh.";
                        break;
                }
            }

            for (int p = 0; p < points.Count; p++)
            {
                var point = points[p];
                if (breps.Values.Any(b => b.IsPointInside(point, tolerance, false)))
                {
                    job.BrepPoints.Add(point);
                    continue;
                }

                bool placed = false;
                foreach (var entry in meshes)
                {
                    var mesh = entry.Value;
                    var onSurface = mesh.ClosestPoint(point);
                    if (onSurface.DistanceTo(point) <= tolerance)
                    {
                        Add(job.PointsOnSurface, entry.Key, point);
                        placed = true;
                        break;
                    }
                    if (mesh.IsPointInside(point, tolerance, true))
                    {
                        Add(job.PointsInside, entry.Key, point);
                        placed = true;
                        break;
                    }
                }
                if (!placed)
                    warnings.Add($"Point {p} is in no solid, nor on one: it is left out.");
            }

            var order = breps.Keys.OrderBy(k => k).ToList();
            job.SolidOf = centre =>
            {
                foreach (var i in order)
                    if (breps[i].IsPointInside(centre, tolerance, false))
                        return i;
                return order.OrderBy(i => breps[i].ClosestPoint(centre).DistanceTo(centre)).First();
            };
            job.FaceCount = i => breps.TryGetValue(i, out var b) ? b.Faces.Count : 0;
            job.FaceOf = (i, triangle) => breps.TryGetValue(i, out var b) ? FaceOf(b, triangle, tolerance) : -1;
            return job;
        }

        /// <summary>
        /// The face of <paramref name="brep"/> the triangle lies on: its corners all on the Brep, and
        /// the face the one nearest its middle. -1 when it is not on the Brep.
        /// </summary>
        private static int FaceOf(Brep brep, Point3d[] triangle, double tolerance)
        {
            double near = 10.0 * tolerance;
            foreach (var corner in triangle)
                if (brep.ClosestPoint(corner).DistanceTo(corner) > near)
                    return -1;

            var centre = (triangle[0] + triangle[1] + triangle[2]) / 3.0;
            if (brep.ClosestPoint(centre, out _, out ComponentIndex component, out _, out _, 0.0, out _) &&
                component.ComponentIndexType == ComponentIndexType.BrepFace)
                return component.Index;

            // On an edge, the middle is as near one face as the other; the face the corners share settles it.
            return -1;
        }

        /// <summary>The job for <see cref="SurfaceMesher"/>: each Brep and the curves written to their files.</summary>
        public static SurfaceMeshJob SurfaceJob(IList<Brep> breps, IList<double> sizes, IList<Point3d> points, IList<Curve> curves,
            IList<MeshRefinement> refinements, SurfaceElements elements, double tolerance, string folder, List<string> warnings)
        {
            var job = new SurfaceMeshJob { Elements = elements, Tolerance = tolerance };
            job.Refinements.AddRange(refinements.Where(r => r != null));

            var kept = new List<Brep>();
            for (int i = 0; i < breps.Count; i++)
            {
                if (breps[i] == null)
                    continue;
                var brep = breps[i].DuplicateBrep();
                kept.Add(brep);
                string step = Path.Combine(folder, $"brep{i}.step");
                WriteStep(step, new[] { brep }, tolerance);
                job.Breps.Add((step, sizes[Math.Min(i, sizes.Count - 1)]));
            }

            for (int p = 0; p < points.Count; p++)
            {
                if (kept.Any(b => b.ClosestPoint(points[p]).DistanceTo(points[p]) <= tolerance))
                    job.Points.Add(points[p]);
                else
                    warnings.Add($"Point {p} is on none of the surfaces: it is left out.");
            }

            double finest = sizes.Min();
            for (int c = 0; c < curves.Count; c++)
            {
                var curve = curves[c];
                if (curve == null)
                    continue;
                var samples = curve.DivideByCount(16, true)?.Select(curve.PointAt) ?? new[] { curve.PointAtStart, curve.PointAtEnd };
                if (samples.All(s => kept.Any(b => b.ClosestPoint(s).DistanceTo(s) <= tolerance)))
                    job.Curves.AddRange(InclusionCurves(curve, finest));
                else
                    warnings.Add($"Curve {c} does not lie on the surfaces: it is left out.");
            }

            job.NormalAt = point =>
            {
                Brep nearest = null;
                double distance = double.MaxValue;
                foreach (var brep in kept)
                {
                    double d = brep.ClosestPoint(point).DistanceTo(point);
                    if (d < distance) { distance = d; nearest = brep; }
                }
                // The normal Rhino gives is the face's, already turned over where the face is
                // reversed in its Brep (checked in Alpaca4d.Test/Gmsh/grasshopper.sh).
                if (nearest == null ||
                    !nearest.ClosestPoint(point, out _, out ComponentIndex component, out _, out _, 0.0, out Vector3d normal) ||
                    component.ComponentIndexType != ComponentIndexType.BrepFace)
                    return Vector3d.Zero;
                return normal;
            };
            return job;
        }

        /// <summary>
        /// A Rhino curve as the curves gmsh is given: a polyline as its straight segments, kinks and
        /// all; anything else segment by segment, a straight one as a line, a bent one as points
        /// along it a quarter of an element apart, which gmsh joins with a smooth spline.
        /// </summary>
        public static IEnumerable<InclusionCurve> InclusionCurves(Curve curve, double size)
        {
            if (curve.TryGetPolyline(out var polyline))
            {
                yield return new InclusionCurve(polyline, true);
                yield break;
            }

            var segments = curve.DuplicateSegments();
            if (segments == null || segments.Length == 0)
                segments = new[] { curve };
            foreach (var segment in segments)
            {
                if (segment.IsLinear(1e-9))
                {
                    yield return new InclusionCurve(new[] { segment.PointAtStart, segment.PointAtEnd }, true);
                    continue;
                }
                int count = (int)Math.Max(12, Math.Min(2000, Math.Ceiling(segment.GetLength() / (size / 4.0))));
                var points = segment.DivideByCount(count, true).Select(segment.PointAt).ToList();
                // Divided closed, a curve gives no end point; its start closes it.
                if (segment.IsClosed)
                    points.Add(points[0]);
                yield return new InclusionCurve(points, false);
            }
        }

        /// <summary>A four node tetrahedron as a closed mesh of four triangles, each facing out.</summary>
        public static Mesh Tetrahedron(Point3d a, Point3d b, Point3d c, Point3d d)
        {
            var corners = new[] { a, b, c, d };
            if (Vector3d.CrossProduct(b - a, c - a) * (d - a) < 0)
                corners = new[] { a, c, b, d };
            // Double precision: in single, a node far from the origin moves by more than the tolerance,
            // and the tetrahedra's corners no longer meet the Face Nodes to the last digit.
            var mesh = new Mesh();
            mesh.Vertices.UseDoublePrecisionVertices = true;
            mesh.Vertices.AddVertices(corners);
            mesh.Faces.AddFace(0, 2, 1);
            mesh.Faces.AddFace(0, 3, 2);
            mesh.Faces.AddFace(0, 1, 3);
            mesh.Faces.AddFace(1, 2, 3);
            mesh.Normals.ComputeNormals();
            return mesh;
        }

        /// <summary>A surface mesh as one Rhino mesh.</summary>
        public static Mesh ToRhino(SurfaceMesh surface)
        {
            var mesh = new Mesh();
            mesh.Vertices.UseDoublePrecisionVertices = true;
            mesh.Vertices.AddVertices(surface.Nodes);
            foreach (var face in surface.Faces)
            {
                if (face.Length == 4)
                    mesh.Faces.AddFace(face[0], face[1], face[2], face[3]);
                else
                    mesh.Faces.AddFace(face[0], face[1], face[2]);
            }
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        private static void Add(Dictionary<int, List<Point3d>> map, int key, Point3d point)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<Point3d>();
            list.Add(point);
        }
    }
}
