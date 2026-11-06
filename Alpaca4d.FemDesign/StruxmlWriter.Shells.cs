using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

using StruSoft.Interop.StruXml.Data;

namespace Alpaca4d.FemDesign
{
    /// <summary>
    /// Shells. Alpaca has one element per mesh face; a FEM-Design plate or wall is a planar region that
    /// FEM-Design meshes itself. So faces of one section that share edges and lie in one plane become one
    /// region, traced round its boundary, holes and all. Plates and walls meeting along an edge keep the
    /// points of that edge, so FEM-Design joins them there as Alpaca's mesh did.
    /// </summary>
    public sealed partial class StruxmlWriter
    {
        /// <summary>How far a corner may stand off a plane and still be in it [m].</summary>
        private const double Planarity = 1e-5;

        /// <summary>How near to level a normal must be for its region to be a wall rather than a plate.</summary>
        private const double Level = 1e-6;

        private sealed class Face
        {
            /// <summary>Welded point numbers, in the order of the mesh face.</summary>
            public int[] Points;
            public int Shell;
            public Vector3d Normal;
            public int Group = -1;
        }

        private sealed class FaceGroup
        {
            public readonly List<int> Faces = new List<int>();
            public int Section;
            public Vector3d LocalX;
            public Point3d Origin;
            /// <summary>The region's normal: up for a plate, the first face's for a wall.</summary>
            public Vector3d Normal;
            /// <summary>Whether a plate or wall was made of it, so that its loads have something to go on.</summary>
            public bool Written;
        }

        private PointWelder welder;
        private readonly List<Face> faces = new List<Face>();
        private readonly List<FaceGroup> groups = new List<FaceGroup>();
        /// <summary>The faces each shell of the export model became: none, one, or two for a warped quad.</summary>
        private List<int>[] shellFaces = new List<int>[0];
        /// <summary>Points an outline keeps even where it runs straight on, because something else uses them.</summary>
        private HashSet<int> pinned = new HashSet<int>();

        private int plateCount, wallCount;

        private void WriteShells()
        {
            shellFaces = model.Shells.Select(_ => new List<int>()).ToArray();
            if (model.Shells.Count == 0)
                return;

            welder = new PointWelder(model.Tolerance);
            BuildFaces();
            GroupFaces();
            pinned = PinnedPoints();

            foreach (var group in groups)
            {
                var section = model.ShellSections[group.Section];
                var shellNames = group.Faces.Select(f => model.Shells[faces[f].Shell]).Select(s => Describe(s.Id, s.Label)).Distinct().ToList();
                if (!(section.Thickness > 0))
                {
                    foreach (var name in shellNames)
                        report.Error("A shell element of a section without thickness is not exported.", name);
                    continue;
                }

                string sectionName = string.IsNullOrWhiteSpace(section.Name) ? $"{Mm(section.Thickness)} mm shell" : section.Name;
                if (section.Layers > 1)
                    report.Warning("FEM-Design's plates are of one material; a layered section is exported at its full thickness in the material of its thickest layer.", sectionName);

                var material = MaterialOf(section.Material, out _);
                bool isWall = Math.Abs(group.Normal.Z) <= Level;
                var localX = SquareTo(group.LocalX, group.Normal);
                if (localX.IsZero)
                    localX = isWall ? SquareTo(Vector3d.CrossProduct(Vector3d.ZAxis, group.Normal), group.Normal)
                                    : SquareTo(Vector3d.XAxis, group.Normal);
                if (localX.IsZero)
                    localX = SquareTo(Vector3d.YAxis, group.Normal);
                var localY = Vector3d.CrossProduct(group.Normal, localX);

                var regions = Outline(group.Faces, group.Normal);
                if (regions == null)
                {
                    foreach (var name in shellNames)
                        report.Warning("Coplanar shell elements that overlap, or whose outline touches itself at a corner - an opening meeting the edge or another opening there - " +
                                       "are exported one plate or wall per element, as FEM-Design's outlines may not touch themselves.", name);
                    regions = group.Faces.SelectMany(f => Outline(new[] { f }, group.Normal) ?? new List<Region_type>()).ToList();
                }

                foreach (var region in regions)
                {
                    try
                    {
                        var slab = isWall
                            ? Slab_type.CreateUniformWall(region, section.Thickness, V(localX), V(localY), material.Guid, Ver_align.Center, name: NextName("W"))
                            : Slab_type.CreateUniformPlate(region, section.Thickness, V(localX), V(localY), material.Guid, Ver_align.Center, name: NextName("P"));
                        Add(slab, slab.Guid);
                        group.Written = true;
                        if (isWall) wallCount++; else plateCount++;
                    }
                    catch (ArgumentException e)
                    {
                        foreach (var name in shellNames)
                            report.Error($"A plate or wall FEM-Design does not accept is not exported: {e.Message}", name);
                    }
                }
                if (regions.Count == 0)
                    foreach (var name in shellNames)
                        report.Error("Shell elements whose outline could not be traced are not exported.", name);
            }
        }

        private void BuildFaces()
        {
            for (int s = 0; s < model.Shells.Count; s++)
            {
                var shell = model.Shells[s];
                var points = new List<int>();
                foreach (var vertex in shell.Vertices ?? new Point3d[0])
                {
                    int point = welder.Weld(vertex);
                    if (points.Count == 0 || points[points.Count - 1] != point)
                        points.Add(point);
                }
                if (points.Count > 1 && points[0] == points[points.Count - 1])
                    points.RemoveAt(points.Count - 1);

                if (points.Count < 3 || Normal(points).IsZero)
                {
                    report.Error("A shell element with no area is not exported.", Describe(shell.Id, shell.Label));
                    continue;
                }

                if (points.Count > 3 && !IsPlanar(points))
                {
                    // FEM-Design's regions are planar; the two halves of a warped quad are.
                    report.Warning("A warped shell element is exported as two triangles.", Describe(shell.Id, shell.Label));
                    AddFace(s, new[] { points[0], points[1], points[2] });
                    AddFace(s, points.Skip(2).Concat(new[] { points[0] }).ToArray());
                }
                else
                    AddFace(s, points.ToArray());
            }
        }

        /// <summary>The face on each set of corners, so that a duplicate element goes in once.</summary>
        private readonly Dictionary<string, int> faceOn = new Dictionary<string, int>();

        private void AddFace(int shell, int[] points)
        {
            string corners = string.Join(",", points.OrderBy(p => p));
            if (faceOn.TryGetValue(corners, out int existing))
            {
                // Its loads still go onto the face it duplicates, as Alpaca applies both.
                report.Warning("A shell element on the same corners as another is exported once; Alpaca analyses both, so its model is twice as stiff and heavy there.",
                    Describe(model.Shells[shell].Id, model.Shells[shell].Label));
                shellFaces[shell].Add(existing);
                return;
            }

            faceOn[corners] = faces.Count;
            shellFaces[shell].Add(faces.Count);
            faces.Add(new Face { Points = points, Shell = shell, Normal = Normal(points) });
        }

        /// <summary>The unit normal of a polygon of welded points, by Newell's method; Zero when it has no area.</summary>
        private Vector3d Normal(IList<int> polygon)
        {
            var n = Vector3d.Zero;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = welder.Points[polygon[i]];
                var b = welder.Points[polygon[(i + 1) % polygon.Count]];
                n += new Vector3d((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            }
            return Unit(n);
        }

        private bool IsPlanar(IList<int> polygon)
        {
            var normal = Normal(polygon);
            var origin = welder.Points[polygon[0]];
            return polygon.All(p => Math.Abs((welder.Points[p] - origin) * normal) <= Planarity);
        }

        /// <summary>Faces of one section and one local x that share edges and lie in one plane go together.</summary>
        private void GroupFaces()
        {
            var edgeFaces = new Dictionary<(int, int), List<int>>();
            for (int f = 0; f < faces.Count; f++)
                foreach (var edge in Edges(faces[f].Points))
                {
                    if (!edgeFaces.TryGetValue(Key(edge), out var list))
                        edgeFaces[Key(edge)] = list = new List<int>();
                    list.Add(f);
                }

            for (int seed = 0; seed < faces.Count; seed++)
            {
                if (faces[seed].Group >= 0)
                    continue;

                var shell = model.Shells[faces[seed].Shell];
                var normal = faces[seed].Normal;
                if (Math.Abs(normal.Z) > Level && normal.Z < 0)
                    normal = -normal;
                var group = new FaceGroup
                {
                    Section = shell.Section,
                    LocalX = shell.LocalX,
                    Origin = welder.Points[faces[seed].Points[0]],
                    Normal = normal,
                };
                int index = groups.Count;
                groups.Add(group);

                var queue = new Queue<int>();
                faces[seed].Group = index;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int f = queue.Dequeue();
                    group.Faces.Add(f);
                    foreach (var edge in Edges(faces[f].Points))
                        foreach (int g in edgeFaces[Key(edge)])
                        {
                            if (faces[g].Group >= 0 || !Joins(group, faces[g]))
                                continue;
                            faces[g].Group = index;
                            queue.Enqueue(g);
                        }
                }
            }
        }

        private bool Joins(FaceGroup group, Face face)
        {
            var shell = model.Shells[face.Shell];
            return shell.Section == group.Section
                && (shell.LocalX - group.LocalX).Length <= 1e-9
                && face.Points.All(p => Math.Abs((welder.Points[p] - group.Origin) * group.Normal) <= Planarity);
        }

        private static IEnumerable<(int, int)> Edges(int[] polygon)
        {
            for (int i = 0; i < polygon.Length; i++)
                yield return (polygon[i], polygon[(i + 1) % polygon.Length]);
        }

        private static (int, int) Key((int A, int B) edge) => edge.A < edge.B ? (edge.A, edge.B) : (edge.B, edge.A);

        /// <summary>
        /// Points where something else meets a region's outline: other regions, bar ends, supports,
        /// connections, point loads and masses. An outline that runs straight past one keeps it, so
        /// FEM-Design finds a corner where the other element connects.
        /// </summary>
        private HashSet<int> PinnedPoints()
        {
            var result = new HashSet<int>();
            var groupOf = new Dictionary<int, int>();
            foreach (var face in faces)
                foreach (int p in face.Points)
                {
                    if (groupOf.TryGetValue(p, out int g) && g != face.Group)
                        result.Add(p);
                    groupOf[p] = face.Group;
                }

            void Pin(Point3d point)
            {
                int p = welder.Find(point);
                if (p >= 0)
                    result.Add(p);
            }

            foreach (var bar in bars.Where(b => b != null)) { Pin(bar.Start); Pin(bar.End); }
            foreach (var support in model.Supports) Pin(support.Position);
            foreach (var link in model.Links) { Pin(link.Start); Pin(link.End); }
            foreach (var link in model.RigidLinks) { Pin(link.RetainedNode); Pin(link.ConstrainedNode); }
            foreach (var tie in model.EqualDofs) { Pin(tie.MasterNode); Pin(tie.SlaveNode); }
            foreach (var mass in model.Masses) Pin(mass.Pos);
            foreach (var load in model.LoadCases.SelectMany(c => c.PointLoads)) Pin(load.Pos);
            return result;
        }

        // ---------------------------------------------------------------- outlines

        /// <summary>
        /// The regions a set of coplanar faces covers, each an outline wound anticlockwise about
        /// <paramref name="normal"/> with its holes wound clockwise, as FEM-Design wants them. Null when
        /// the faces make no such regions: when they overlap, or when an outline would touch itself or
        /// another at a corner - an opening meeting the edge there, say - which FEM-Design's may not.
        /// One face alone always makes one region.
        /// </summary>
        private List<Region_type> Outline(IEnumerable<int> faceIds, Vector3d normal)
        {
            // Every face wound anticlockwise about the normal. An edge two faces share then runs both
            // ways and cancels; what is left, each edge once, is the boundary, with the faces on its left.
            var directed = new List<(int A, int B)>();
            var uses = new Dictionary<(int, int), int>();
            foreach (int f in faceIds)
            {
                var points = faces[f].Points;
                if (faces[f].Normal * normal < 0)
                    points = Enumerable.Reverse(points).ToArray();
                foreach (var edge in Edges(points))
                {
                    directed.Add(edge);
                    uses.TryGetValue(Key(edge), out int n);
                    uses[Key(edge)] = n + 1;
                }
            }
            if (uses.Values.Any(n => n > 2))
                return null; // faces on top of one another

            // A clean boundary leaves each of its points once; a point it leaves twice is where it
            // touches itself.
            var next = new Dictionary<int, int>();
            foreach (var edge in directed.Where(e => uses[Key(e)] == 1))
            {
                if (next.ContainsKey(edge.A))
                    return null;
                next[edge.A] = edge.B;
            }

            // A plane to measure areas in, with u x v = normal.
            var u = SquareTo(Math.Abs(normal.X) < 0.9 ? Vector3d.XAxis : Vector3d.YAxis, normal);
            var v = Vector3d.CrossProduct(normal, u);
            (double, double) Flat(int p) => (new Vector3d(welder.Points[p]) * u, new Vector3d(welder.Points[p]) * v);

            var loops = new List<List<int>>();
            while (next.Count > 0)
            {
                int start = next.Keys.First();
                var loop = new List<int>();
                int current = start;
                do
                {
                    if (!next.TryGetValue(current, out int following))
                        return null; // a boundary that does not close
                    next.Remove(current);
                    loop.Add(current);
                    current = following;
                }
                while (current != start);

                if (loop.Count >= 3)
                    loops.Add(Simplify(loop, Flat));
            }

            double Area(List<int> loop)
            {
                double area = 0.0;
                for (int i = 0; i < loop.Count; i++)
                {
                    var (ax, ay) = Flat(loop[i]);
                    var (bx, by) = Flat(loop[(i + 1) % loop.Count]);
                    area += ax * by - bx * ay;
                }
                return area / 2.0;
            }

            var outers = loops.Where(l => Area(l) > 0).OrderBy(Area).ToList();
            var holes = loops.Where(l => Area(l) < 0).ToList();
            var holesOf = outers.ToDictionary(o => o, o => new List<List<int>>());
            foreach (var hole in holes)
            {
                // The smallest outline around a hole is the one it is cut out of.
                var (x, y) = Flat(hole[0]);
                var outer = outers.FirstOrDefault(o => Contains(o, x, y, Flat));
                if (outer == null)
                    return null;
                holesOf[outer].Add(hole);
            }

            List<Point_type_3d> Points(List<int> loop) => loop.Select(p => P(welder.Points[p])).ToList();
            return outers.Select(o => Region_type.FromPointLoops(Points(o), holesOf[o].Select(h => (IEnumerable<Point_type_3d>)Points(h)))).ToList();
        }

        /// <summary>Drops the points an outline runs straight through, unless something else meets it there.</summary>
        private List<int> Simplify(List<int> loop, Func<int, (double, double)> flat)
        {
            bool removed = true;
            while (removed && loop.Count > 3)
            {
                removed = false;
                for (int i = 0; i < loop.Count && loop.Count > 3; i++)
                {
                    int p = loop[i];
                    if (pinned.Contains(p))
                        continue;
                    var (px, py) = flat(loop[(i + loop.Count - 1) % loop.Count]);
                    var (cx, cy) = flat(p);
                    var (nx, ny) = flat(loop[(i + 1) % loop.Count]);
                    double ux = cx - px, uy = cy - py, wx = nx - cx, wy = ny - cy;
                    double cross = ux * wy - uy * wx;
                    double lengths = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(wx * wx + wy * wy);
                    if (Math.Abs(cross) <= 1e-9 * lengths && ux * wx + uy * wy > 0)
                    {
                        loop.RemoveAt(i);
                        removed = true;
                        i--;
                    }
                }
            }
            return loop;
        }

        private static bool Contains(List<int> polygon, double x, double y, Func<int, (double, double)> flat)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var (xi, yi) = flat(polygon[i]);
                var (xj, yj) = flat(polygon[j]);
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
            return inside;
        }
    }
}
