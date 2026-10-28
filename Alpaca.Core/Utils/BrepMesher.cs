using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Alpaca4d
{
    /// <summary>
    /// A quad mesh of Breps for shell elements, made by Rhino's QuadRemesh.
    ///
    /// QuadRemesh gives the smooth, mostly regular quads a shell model wants, but it is a modelling
    /// tool: it promises no node at a support, and nothing makes a wall and the slab it stands on
    /// share the nodes along the line where they meet. So it runs in its strict face mode, which
    /// keeps every face's boundary as a line of mesh edges, and the result is then tidied:
    ///  - where faces meet that QuadRemesh meshed apart, as at a wall under a slab, the lines of
    ///    nodes either side are given the same count and welded into one;
    ///  - the nodes along each Brep edge are put exactly on it, and the ones at its corners
    ///    exactly on them, so faces that share an edge share its nodes to the last digit;
    ///  - each inclusion point takes the nearest node and moves it onto itself. A point on an edge
    ///    takes a node on that edge, so the edge stays where it was;
    ///  - the nodes a few rings around each moved or added one relax, evening out the quads it
    ///    pulled out of shape. Edge nodes slide along their edge and the rest stay on the surface.
    /// </summary>
    public static class BrepMesher
    {
        /// <summary>
        /// Estimated quad count above which meshing is refused rather than tried. An edge length
        /// typed in metres on a model drawn in millimetres asks for billions of quads, and
        /// QuadRemesh would take Rhino down trying.
        /// </summary>
        public const int MaxQuads = 200_000;

        /// <summary>How many rings of nodes around a moved node relax.</summary>
        const int RelaxRings = 3;

        /// <summary>Relaxation sweeps. A three-ring patch settles well within this.</summary>
        const int RelaxSweeps = 30;

        /// <summary>
        /// Strips split to even out node counts before giving up on all-quad and splitting single
        /// quads instead. Each split can unbalance a line further along the strip, so a model whose
        /// lines keep pushing each other out of step has to be stopped somewhere.
        /// </summary>
        const int MaxStripSplits = 200;

        /// <param name="breps">Merged into one where they touch, so the mesh is joined there too.</param>
        /// <param name="tolerance">How far off the Brep an inclusion point may be and still count as on it.</param>
        /// <param name="warnings">Collects what was left out or only roughly kept.</param>
        /// <param name="remarks">Collects what was done differently from plain quads, which is not a fault.</param>
        public static Mesh Create(IList<Brep> breps, double edgeLength, IList<Point3d> points, double tolerance, List<string> warnings, List<string> remarks = null)
        {
            var given = (breps ?? new Brep[0]).Where(b => b != null).ToList();
            if (given.Count == 0)
                throw new ArgumentException("There is no Brep to mesh.");
            if (!(edgeLength > 0))
                throw new ArgumentException("Edge Length has to be greater than zero.");

            // A merge is a Brep where three faces can share an edge, which is what a wall standing
            // on a slab is. QuadRemesh then keeps that edge from every side.
            var brep = given.Count == 1 ? given[0] : Brep.MergeBreps(given, tolerance);
            if (brep == null)
                throw new ArgumentException("The Breps could not be merged into one, so their meshes could not be joined where they meet.");

            var area = AreaMassProperties.Compute(brep, true, false, false, false)?.Area ?? 0.0;
            var estimate = area / (edgeLength * edgeLength);
            if (estimate > MaxQuads)
                throw new ArgumentException($"An edge length of {edgeLength} would make about {estimate:N0} quads, more than the {MaxQuads:N0} allowed. Check the edge length against the model's units.");

            var parameters = new QuadRemeshParameters
            {
                TargetEdgeLength = edgeLength,
                DetectHardEdges = true,
                // Strict: every face's boundary survives as mesh edges, the small faces included.
                PreserveMeshArrayEdgesMode = 2,
            };
            Mesh remeshed;
            try
            {
                remeshed = Mesh.QuadRemeshBrep(brep, parameters);
            }
            catch (Exception)
            {
                // QuadRemesh starts from Rhino's own mesh of each face, and fails outright when a
                // face can't be meshed. The usual cause here is a wall whose top edge stops inside
                // a slab: merged, the slab gets a slit where the wall meets it.
                remeshed = null;
            }
            if (remeshed == null || remeshed.Faces.Count == 0)
                throw new InvalidOperationException(given.Count > 1
                    ? "Rhino's QuadRemesh could not mesh the merged Breps. If a wall meets a slab along a line that stops short of the slab's edges, split the slab along that line, carried through to its edges, so the line is an edge of its own."
                    : "Rhino's QuadRemesh made no mesh of this Brep.");

            var work = new Work(brep, remeshed);
            work.Conform(warnings, remarks);
            work.Snap(warnings);
            var moved = work.Include(points ?? new Point3d[0], tolerance, warnings);
            work.Relax(moved);
            return work.ToMesh();
        }

        sealed class Work
        {
            readonly Brep brep;
            readonly Curve[] edgeCurves;

            /// <summary>Mean length of the remeshed edges: the scale "near" is measured on.</summary>
            readonly double size;
            /// <summary>Each Brep face's plane, facing the way the face does, or Plane.Unset if it isn't flat.</summary>
            readonly Plane[] planes;
            /// <summary>Each Brep edge's line, or null if it isn't straight.</summary>
            readonly Line?[] lines;
            /// <summary>The Brep edges round each Brep face, and the faces along each edge, once each.</summary>
            readonly int[][] brepFaceEdges, brepEdgeFaces;
            /// <summary>Where along a Brep edge a node is, for ordering the nodes on it.</summary>
            readonly Dictionary<(int node, int edge), double> alongEdge = new Dictionary<(int, int), double>();

            readonly List<Point3d> p = new List<Point3d>();
            /// <summary>Welded nodes point at the node they became, a union-find.</summary>
            readonly List<int> alias = new List<int>();
            List<int[]> faces = new List<int[]>();
            /// <summary>The Brep face each mesh face lies on.</summary>
            List<int> faceOf = new List<int>();
            /// <summary>Mesh faces that turn the other way to the Brep face under them.</summary>
            List<bool> flip = new List<bool>();
            /// <summary>The Brep edge each mesh edge lying on one follows. Edges inside a face are not in it.</summary>
            Dictionary<(int, int), int> edgeOf = new Dictionary<(int, int), int>();

            /// <summary>Brep edges that some mesh edges follow.</summary>
            readonly bool[] kept;
            /// <summary>The node on each Brep vertex that is a corner, or -1.</summary>
            readonly int[] cornerNode;
            /// <summary>Nodes the conforming made or moved, which the quads around need to even out from.</summary>
            readonly HashSet<int> unsettled = new HashSet<int>();
            /// <summary>Brep edges whose lines of nodes could not be brought to one count.</summary>
            readonly HashSet<int> givenUp = new HashSet<int>();
            int triangles;

            // Rebuilt from the faces and edgeOf after every change to them.
            List<int>[] neighbours;
            List<int>[] nodeFaces;
            /// <summary>The Brep edges a node's mesh edges follow, or null for a node inside a face.</summary>
            List<int>[] nodeEdges;
            Dictionary<(int, int), List<int>> edgeFaces;
            List<(int, int)>[] meshEdgesOn;

            /// <summary>The Brep edge a node may slide along, or -1.</summary>
            int[] onEdge;
            /// <summary>Nodes that don't move again: the Brep's corners and the inclusion points.</summary>
            bool[] pinned;

            public Work(Brep brep, Mesh remeshed)
            {
                this.brep = brep;
                edgeCurves = brep.Edges.Select(e => e.DuplicateCurve()).ToArray();
                kept = new bool[brep.Edges.Count];
                cornerNode = Enumerable.Repeat(-1, brep.Vertices.Count).ToArray();

                // QuadRemesh meshes each Brep face as a block with vertices of its own. Where two
                // faces meet, their blocks' vertices coincide along some stretches and not along
                // others, so welding what coincides would leave lines of nodes that are partly one
                // and partly two. Instead each face keeps its own nodes, and the lines either side
                // of every edge are joined up one to one afterwards.
                var raw = new List<int[]>(remeshed.Faces.Count);
                for (int f = 0; f < remeshed.Faces.Count; f++)
                {
                    var face = remeshed.Faces[f];
                    var corners = face.IsQuad ? new[] { face.A, face.B, face.C, face.D } : new[] { face.A, face.B, face.C };
                    var ids = corners.Distinct().ToArray();
                    if (ids.Length >= 3)
                        raw.Add(ids);
                }
                var position = Enumerable.Range(0, remeshed.Vertices.Count).Select(remeshed.Vertices.Point3dAt).ToArray();
                Point3d CentreOf(int f)
                {
                    var centre = Point3d.Origin;
                    foreach (var v in raw[f])
                        centre += position[v];
                    return centre / raw[f].Length;
                }

                // Flat faces, which most are, are told apart and projected onto with their planes
                // rather than their surfaces, and straight edges with their lines.
                var diagonal = brep.GetBoundingBox(false).Diagonal.Length;
                planes = brep.Faces.Select(face =>
                {
                    if (!face.IsPlanar(1e-9 * diagonal))
                        return Plane.Unset;
                    var u = face.Domain(0).Mid;
                    var v = face.Domain(1).Mid;
                    return new Plane(face.PointAt(u, v), Normal(face, u, v));
                }).ToArray();
                lines = edgeCurves.Select(c => c.IsLinear(1e-9 * diagonal) ? new Line(c.PointAtStart, c.PointAtEnd) : (Line?)null).ToArray();
                brepFaceEdges = brep.Faces.Select(face => face.AdjacentEdges()).ToArray();
                brepEdgeFaces = brep.Edges.Select(edge => edge.AdjacentFaces().Distinct().ToArray()).ToArray();

                var region = new int[raw.Count];
                var normals = new Vector3d[raw.Count];
                var slack = 1e-5 * diagonal;
                var boxes = brep.Faces.Select(face => { var box = face.GetBoundingBox(false); box.Inflate(slack); return box; }).ToArray();
                for (int f = 0; f < raw.Count; f++)
                {
                    var centre = CentreOf(f);
                    region[f] = FaceOfCentre(centre, boxes, slack);
                    var face = brep.Faces[region[f]];
                    if (planes[region[f]].IsValid)
                        normals[f] = planes[region[f]].Normal;
                    else
                    {
                        face.ClosestPoint(centre, out var u, out var v);
                        normals[f] = Normal(face, u, v);
                    }
                }

                // A face meets itself along a closed surface's seam, so nodes in one place on one
                // face are one node. Keyed on the coordinates themselves: Point3d's own hash XORs
                // them, and on a regular grid nearly every node then lands in a handful of buckets.
                var nodeAt = new Dictionary<(int, double, double, double), int>();
                for (int f = 0; f < raw.Count; f++)
                {
                    var ids = raw[f].Select(v =>
                    {
                        var key = (region[f], position[v].X, position[v].Y, position[v].Z);
                        if (!nodeAt.TryGetValue(key, out var n))
                            nodeAt[key] = n = AddNode(position[v]);
                        return n;
                    }).Distinct().ToArray();
                    if (ids.Length < 3)
                        continue;
                    faces.Add(ids);
                    faceOf.Add(region[f]);
                    flip.Add(normals[f] * FaceNormal(faces.Count - 1) < 0);
                }

                Rebuild();
                size = edgeFaces.Keys.Average(k => p[k.Item1].DistanceTo(p[k.Item2]));

                MapEdges();
                IndexBoundary();
            }

            /// <summary>
            /// The Brep face the centre of a mesh face lies on. On flat faces the box around each
            /// face and its plane settle it without asking the Brep, which is most of the time;
            /// where they leave more than one face, as near a curved face, the Brep is asked.
            /// </summary>
            int FaceOfCentre(Point3d centre, BoundingBox[] boxes, double slack)
            {
                var found = -1;
                for (int f = 0; f < boxes.Length; f++)
                {
                    if (!boxes[f].Contains(centre))
                        continue;
                    if (planes[f].IsValid && Math.Abs(planes[f].DistanceTo(centre)) > slack)
                        continue;
                    if (found >= 0)
                        return BrepFaceAt(centre);
                    found = f;
                }
                return found >= 0 ? found : BrepFaceAt(centre);
            }

            /// <summary>
            /// Finds which mesh edges lie on the Brep's edges.
            ///
            /// A mesh edge is on a Brep edge when it is naked or when the faces either side of it
            /// lie on different Brep faces. Reading it off the mesh's connections rather than off
            /// distances means no tolerance decides it, so an interior node a little way in from
            /// an edge is never mistaken for one on it.
            /// </summary>
            void MapEdges()
            {
                foreach (var pair in edgeFaces)
                {
                    var regions = pair.Value.Select(f => faceOf[f]).Distinct().ToList();
                    if (pair.Value.Count == 2 && regions.Count == 1)
                        continue;

                    // The Brep edges every face it touches has in common: the one between them,
                    // or a naked one on a face's outline.
                    var candidates = regions
                        .Select(r => (IEnumerable<int>)brepFaceEdges[r])
                        .Aggregate((x, y) => x.Intersect(y))
                        .ToList();
                    if (candidates.Count == 0)
                        candidates = regions.SelectMany(r => brepFaceEdges[r]).Distinct().ToList();

                    var middle = 0.5 * (p[pair.Key.Item1] + p[pair.Key.Item2]);
                    var edge = candidates.OrderBy(e => DistanceTo(e, middle)).First();
                    edgeOf[pair.Key] = edge;
                    kept[edge] = true;
                }
            }

            /// <summary>
            /// Joins the faces up along the edges between them.
            ///
            /// Along most edges the faces either side already have their nodes in the same places,
            /// and joining them is only welding. Where three or more faces share an edge, as a wall
            /// under a slab does, QuadRemesh meshes each on its own, and their lines of nodes along
            /// the edge needn't even have the same count. The shorter lines get nodes until they
            /// match - by splitting the strip of quads that runs from the edge across the face,
            /// which keeps every face a quad, or failing that by splitting one quad into a quad and
            /// a triangle - and then each node is welded to its counterparts.
            /// </summary>
            public void Conform(List<string> warnings, List<string> remarks)
            {
                Corners(warnings);

                // A round adds a node to every line that is short of its edge's count, and then
                // the mesh's connections are rebuilt. A split that would touch a face another
                // split in the same round already changed waits for the next round.
                var strips = 0;
                for (int round = 0; round < 10_000; round++)
                {
                    var mismatches = FindMismatches();
                    if (mismatches.Count == 0)
                        break;

                    var touched = new HashSet<int>();
                    foreach (var (edge, chain) in mismatches)
                    {
                        var segment = chain.Pairs()
                            .Where(pair => edgeFaces.ContainsKey(Key(pair.Item1, pair.Item2)))
                            .OrderByDescending(pair => p[pair.Item1].DistanceTo(p[pair.Item2]))
                            .Select(pair => ((int, int)?)pair)
                            .FirstOrDefault();
                        if (segment == null)
                        {
                            givenUp.Add(edge);
                            continue;
                        }

                        var (a, b) = segment.Value;
                        var split = strips < MaxStripSplits ? StripSplit(a, b, touched) : Split.Failed;
                        if (split == Split.Done)
                            strips++;
                        else if (split == Split.Failed)
                            TriangleSplit(a, b, touched);
                    }
                    Rebuild();
                }

                WeldChains(warnings);

                if (triangles > 0)
                    remarks?.Add($"{triangles} quad{(triangles == 1 ? " was" : "s were")} split into a quad and a triangle, so that faces meshed apart share every node along the edge where they meet.");
            }

            /// <summary>
            /// Gives each corner of the Brep one node, sitting on it: every face meeting there
            /// brings its nearest node, and those become one. The lines of nodes along the edges
            /// from it then all start from the same node.
            /// </summary>
            void Corners(List<string> warnings)
            {
                for (int v = 0; v < brep.Vertices.Count; v++)
                {
                    var vertex = brep.Vertices[v];
                    if (!IsCorner(vertex))
                        continue;

                    // From each face, its nearest node on the edges that meet here, however far:
                    // QuadRemesh can mesh a small face much more coarsely than its neighbours.
                    var location = vertex.Location;
                    var nearest = vertex.EdgeIndices()
                        .Where(e => kept[e])
                        .SelectMany(e => meshEdgesOn[e])
                        .SelectMany(key => new[] { key.Item1, key.Item2 })
                        .Distinct()
                        .Where(n => nodeFaces[n].Count > 0)
                        .GroupBy(n => faceOf[nodeFaces[n][0]])
                        .Select(g => g.OrderBy(n => p[n].DistanceTo(location)).First())
                        .OrderBy(n => p[n].DistanceTo(location))
                        .ToList();
                    if (nearest.Count == 0)
                    {
                        warnings.Add($"The Brep's corner at {Format(location)} has no node near it, so the edges meeting there may not join up.");
                        continue;
                    }

                    var corner = nearest[0];
                    foreach (var n in nearest)
                    {
                        if (p[n].DistanceTo(location) > 1e-6 * size)
                            unsettled.Add(corner);
                        Union(corner, n);
                    }
                    p[corner] = location;
                    cornerNode[v] = corner;
                }
                ApplyAliases();
            }

            /// <summary>
            /// Whether a vertex needs a node of its own. A vertex where one edge runs smoothly on
            /// into the next along a face's outline, as at the seam of a hole's circle, doesn't:
            /// QuadRemesh puts no node there, and dragging the nearest one onto it would only pull
            /// the quads around out of shape. On an edge between faces it does even so, because
            /// the faces' lines of nodes along the edge have to start from the same node.
            /// </summary>
            bool IsCorner(BrepVertex vertex)
            {
                if (vertex.EdgeIndices().Any(e => kept[e] && brepEdgeFaces[e].Length > 1))
                    return true;

                // The directions the kept edges leave the vertex in.
                var ends = new List<Vector3d>();
                var close = 1e-3 * size;
                foreach (var e in vertex.EdgeIndices())
                {
                    if (!kept[e])
                        continue;
                    var curve = edgeCurves[e];
                    if (curve.PointAtStart.DistanceTo(vertex.Location) <= close)
                        ends.Add(curve.TangentAtStart);
                    if (curve.PointAtEnd.DistanceTo(vertex.Location) <= close)
                        ends.Add(-curve.TangentAtEnd);
                }
                return ends.Count > 0 && !(ends.Count == 2 && ends[0] * ends[1] < -0.9999);
            }

            /// <summary>The Brep edges with lines of nodes of different counts along them, each with the shortest.</summary>
            List<(int edge, Chain chain)> FindMismatches()
            {
                var found = new List<(int, Chain)>();
                for (int e = 0; e < kept.Length; e++)
                {
                    if (!kept[e] || brepEdgeFaces[e].Length < 2 || givenUp.Contains(e))
                        continue;
                    var chains = Chains(e);
                    if (chains.Count < 2)
                        continue;
                    var shortest = chains.OrderBy(c => c.Segments).First();
                    if (shortest.Segments < chains.Max(c => c.Segments))
                        found.Add((e, shortest));
                }
                return found;
            }

            /// <summary>
            /// The lines of nodes along Brep edge e, one per face along it, from the edge's start
            /// to its end. Faces whose lines are already one are given once, so a joined-up edge
            /// has a single line.
            /// </summary>
            List<Chain> Chains(int e)
            {
                var edge = brep.Edges[e];
                var start = cornerNode[edge.StartVertex.VertexIndex];
                var end = cornerNode[edge.EndVertex.VertexIndex];
                var curve = edgeCurves[e];

                var chains = new List<Chain>();
                foreach (var face in brepEdgeFaces[e])
                {
                    var nodes = new HashSet<int>();
                    foreach (var key in meshEdgesOn[e])
                        if (edgeFaces[key].Any(f => faceOf[f] == face))
                        {
                            nodes.Add(key.Item1);
                            nodes.Add(key.Item2);
                        }
                    if (nodes.Count == 0)
                        continue;

                    var line = new List<int>();
                    if (start >= 0 && nodes.Contains(start))
                        line.Add(start);
                    line.AddRange(nodes.Where(n => n != start && n != end).OrderBy(n => Along(n, e)));
                    if (end >= 0 && nodes.Contains(end))
                        line.Add(end);

                    // Round a closed edge with no corner on it, the last node joins back to the first.
                    var loop = curve.IsClosed && start < 0;
                    if (!chains.Any(c => c.Nodes.SequenceEqual(line)))
                        chains.Add(new Chain(line.ToArray(), loop));
                }
                return chains;
            }

            enum Split { Done, Failed, Clash }

            /// <summary>
            /// Adds a node to the line through a and b by splitting the strip of quads that runs
            /// from that edge across the mesh: each quad on the way becomes two, so every face
            /// stays a quad. The strip ends where it reaches a boundary, which gets a node too.
            ///
            /// Nothing changes if the strip runs into a triangle or a non-manifold edge, or crosses
            /// itself - Failed - or into a face another split this round has changed - Clash, to
            /// be tried again once the mesh's connections are rebuilt.
            /// </summary>
            Split StripSplit(int a, int b, HashSet<int> touched)
            {
                var nodeCount = p.Count;
                var faceCount = faces.Count;
                var replaced = new List<(int face, int[] was)>();
                var splitEdges = new List<(int a, int b, int middle, int edge)>();
                void SplitLogged(int x, int y, int middle)
                {
                    if (edgeOf.TryGetValue(Key(x, y), out var e))
                        splitEdges.Add((x, y, middle, e));
                    SplitEdgeKey(x, y, middle);
                }

                var made = new List<int>();
                var start = AddNode(0.5 * (p[a] + p[b]));
                made.Add(start);
                SplitLogged(a, b, start);

                var visited = new HashSet<int>();
                var result = Split.Done;
                foreach (var first in edgeFaces[Key(a, b)])
                {
                    int u = a, v = b, middle = start, f = first;
                    while (result == Split.Done)
                    {
                        if (touched.Contains(f))
                            result = Split.Clash;
                        else if (!visited.Add(f) || faces[f].Length != 4)
                            result = Split.Failed;
                        if (result != Split.Done)
                            break;

                        var face = faces[f];
                        var i = IndexOfEdge(face, u, v);
                        int A = face[i], B = face[(i + 1) % 4], C = face[(i + 2) % 4], D = face[(i + 3) % 4];

                        var across = AddNode(0.5 * (p[C] + p[D]));
                        made.Add(across);
                        replaced.Add((f, face));
                        faces[f] = new[] { A, middle, across, D };
                        AddFace(new[] { middle, B, C, across }, f);
                        SplitLogged(C, D, across);

                        var next = edgeFaces[Key(C, D)].Where(g => g != f).ToList();
                        if (next.Count == 0)
                            break;
                        if (next.Count > 1)
                            result = Split.Failed;
                        (u, v, middle, f) = (C, D, across, next[0]);
                    }
                    if (result != Split.Done)
                        break;
                }

                if (result != Split.Done)
                {
                    for (int k = replaced.Count - 1; k >= 0; k--)
                        faces[replaced[k].face] = replaced[k].was;
                    faces.RemoveRange(faceCount, faces.Count - faceCount);
                    faceOf.RemoveRange(faceCount, faceOf.Count - faceCount);
                    flip.RemoveRange(faceCount, flip.Count - faceCount);
                    p.RemoveRange(nodeCount, p.Count - nodeCount);
                    alias.RemoveRange(nodeCount, alias.Count - nodeCount);
                    for (int k = splitEdges.Count - 1; k >= 0; k--)
                    {
                        var (x, y, middle, e) = splitEdges[k];
                        edgeOf.Remove(Key(x, middle));
                        edgeOf.Remove(Key(middle, y));
                        edgeOf[Key(x, y)] = e;
                    }
                    return result;
                }

                touched.UnionWith(visited);
                unsettled.UnionWith(made);
                return Split.Done;
            }

            /// <summary>
            /// Adds a node to the line through a and b by splitting the face on each side of that
            /// edge into a quad and a triangle. It always works, at the cost of the triangles,
            /// unless another split this round has changed one of those faces; it then waits for
            /// the next round.
            /// </summary>
            void TriangleSplit(int a, int b, HashSet<int> touched)
            {
                var around = edgeFaces[Key(a, b)];
                if (around.Any(touched.Contains))
                    return;

                var middle = AddNode(0.5 * (p[a] + p[b]));
                SplitEdgeKey(a, b, middle);
                foreach (var f in around)
                {
                    var face = faces[f];
                    var i = IndexOfEdge(face, a, b);
                    int A = face[i], B = face[(i + 1) % face.Length], C = face[(i + 2) % face.Length];
                    faces[f] = face.Length == 4 ? new[] { A, middle, C, face[(i + 3) % 4] } : new[] { A, middle, C };
                    AddFace(new[] { middle, B, C }, f);
                    touched.Add(f);
                    triangles++;
                }
                unsettled.Add(middle);
            }

            /// <summary>
            /// Welds the lines of nodes along each edge, now of one count, node by node. The line
            /// most of the others already match keeps its nodes where they are, so along an edge
            /// QuadRemesh meshed from both sides alike nothing moves.
            /// </summary>
            void WeldChains(List<string> warnings)
            {
                var close = 1e-6 * size;
                for (int e = 0; e < kept.Length; e++)
                {
                    if (!kept[e] || brepEdgeFaces[e].Length < 2)
                        continue;
                    var chains = Chains(e);
                    if (chains.Count < 2)
                        continue;
                    var longest = chains.Max(c => c.Nodes.Length);
                    chains = chains.Where(c => c.Nodes.Length == longest).ToList();

                    var count = chains[0].Nodes.Length;
                    bool Same(Chain a, Chain b) => Enumerable.Range(0, count).All(i => p[a.Nodes[i]].DistanceTo(p[b.Nodes[i]]) <= close);
                    var master = chains.OrderByDescending(c => chains.Count(d => d != c && Same(c, d))).First();
                    foreach (var other in chains)
                    {
                        if (other == master)
                            continue;
                        var shift = other.Loop ? BestShift(master, other) : 0;
                        for (int i = 0; i < count; i++)
                        {
                            var n = other.Nodes[(i + shift) % count];
                            var m = master.Nodes[i];
                            if (p[n].DistanceTo(p[m]) > close)
                                unsettled.Add(m);
                            Union(m, n);
                        }
                    }
                }
                ApplyAliases();

                for (int e = 0; e < kept.Length; e++)
                {
                    if (kept[e] && brepEdgeFaces[e].Length > 1 && Chains(e).Count > 1)
                    {
                        var edge = brep.Edges[e];
                        warnings.Add($"The faces meeting at Brep edge {e}, from {Format(edge.PointAtStart)} to {Format(edge.PointAtEnd)}, don't share all their nodes there.");
                    }
                }
            }

            int BestShift(Chain master, Chain other)
            {
                var count = master.Nodes.Length;
                return Enumerable.Range(0, count)
                    .OrderBy(k => Enumerable.Range(0, count).Sum(i => p[master.Nodes[i]].DistanceTo(p[other.Nodes[(i + k) % count]])))
                    .First();
            }

            /// <summary>
            /// Puts the nodes on the Brep's edges exactly on them, and its corners' nodes exactly on
            /// the corners. QuadRemesh puts them close, a few thousandths off on a curved edge.
            /// </summary>
            public void Snap(List<string> warnings)
            {
                onEdge = Enumerable.Repeat(-1, p.Count).ToArray();
                pinned = new bool[p.Count];

                for (int v = 0; v < cornerNode.Length; v++)
                {
                    if (cornerNode[v] < 0)
                        continue;
                    var n = Find(cornerNode[v]);
                    p[n] = brep.Vertices[v].Location;
                    pinned[n] = true;
                }

                var drift = new double[brep.Edges.Count];
                for (int n = 0; n < p.Count; n++)
                {
                    if (nodeEdges[n] == null || pinned[n])
                        continue;

                    var edges = nodeEdges[n].Distinct().ToList();
                    var e = edges.OrderBy(x => DistanceTo(x, p[n])).First();
                    var on = OnEdge(e, p[n]);
                    drift[e] = Math.Max(drift[e], on.DistanceTo(p[n]));
                    p[n] = on;

                    // A node on two edges where the Brep has no corner, as where one curve runs
                    // smoothly into the next. Sliding along either would drag the other.
                    if (edges.Count == 1)
                        onEdge[n] = e;
                    else
                        pinned[n] = true;
                }

                for (int e = 0; e < brep.Edges.Count; e++)
                {
                    var edge = brep.Edges[e];
                    var between = edge.Valence == EdgeAdjacency.Naked || brepEdgeFaces[e].Length > 1;
                    if (between && !kept[e])
                        warnings.Add($"Brep edge {e}, from {Format(edge.PointAtStart)} to {Format(edge.PointAtEnd)}, has no mesh edges along it, so the faces either side of it don't share nodes there.");
                    else if (drift[e] > 0.25 * size)
                        warnings.Add($"Brep edge {e}, from {Format(edge.PointAtStart)} to {Format(edge.PointAtEnd)}, was only roughly followed: nodes had to move up to {drift[e]:G3} onto it, and the quads along it may be out of shape.");
                }
            }

            /// <summary>
            /// Moves a node onto each point, and returns the nodes that moved.
            ///
            /// A point on an edge takes the nearest node on that edge, and a point inside a face
            /// the nearest node inside that face, so no point can pull a node off an edge or
            /// across one. A point on a corner already has its node.
            /// </summary>
            public List<int> Include(IList<Point3d> points, double tolerance, List<string> warnings)
            {
                var moved = new List<int>();
                var held = Enumerable.Range(0, p.Count).Where(n => pinned[n]).ToList();
                var insideFace = Enumerable.Range(0, p.Count)
                    .Where(n => nodeEdges[n] == null && nodeFaces[n].Count > 0)
                    .ToLookup(n => faceOf[nodeFaces[n][0]]);
                var onEachEdge = Enumerable.Range(0, p.Count)
                    .Where(n => onEdge[n] >= 0)
                    .ToLookup(n => onEdge[n]);

                for (int i = 0; i < points.Count; i++)
                {
                    var point = points[i];
                    var around = FacesAt(point, tolerance);
                    if (around.Count == 0)
                    {
                        warnings.Add($"Point {i} at {Format(point)} is not on the Brep, so the mesh has no node there.");
                        continue;
                    }

                    // A corner, or a point given twice.
                    if (Nearest(point, held, out var distance) >= 0 && distance <= tolerance)
                        continue;

                    var edges = Enumerable.Range(0, edgeCurves.Length)
                        .Where(e => kept[e] && DistanceTo(e, point) <= tolerance)
                        .ToList();
                    var candidates = edges.Count > 0
                        ? edges.SelectMany(e => onEachEdge[e])
                        : around.SelectMany(f => insideFace[f]);

                    var node = Nearest(point, candidates.Where(n => !pinned[n]), out distance);
                    if (node < 0)
                    {
                        warnings.Add($"Point {i} at {Format(point)} found no node left to move onto it: the ones near it are already taken.");
                        continue;
                    }

                    p[node] = point;
                    pinned[node] = true;
                    held.Add(node);
                    moved.Add(node);
                }

                // Two nodes held much closer than a quad's side squeeze the quads between them
                // thin, and no relaxing can open them up again.
                var thin = 0.3 * size;
                for (int a = 0; a < moved.Count; a++)
                {
                    for (int b = a + 1; b < moved.Count; b++)
                        if (p[moved[a]].DistanceTo(p[moved[b]]) < thin)
                            warnings.Add($"The points at {Format(p[moved[a]])} and {Format(p[moved[b]])} are {p[moved[a]].DistanceTo(p[moved[b]]):G3} apart, much closer than the quads are wide, so the quads between them are thin.");

                    if (onEdge[moved[a]] >= 0)
                        continue;
                    foreach (var e in Enumerable.Range(0, edgeCurves.Length).Where(e => kept[e]))
                    {
                        var gap = DistanceTo(e, p[moved[a]]);
                        if (gap < thin)
                        {
                            warnings.Add($"The point at {Format(p[moved[a]])} is {gap:G3} from Brep edge {e}, much closer than the quads are wide, so the quads between them are thin.");
                            break;
                        }
                    }
                }
                return moved;
            }

            /// <summary>
            /// Relaxes the nodes a few rings around the moved and added ones, each towards the
            /// middle of its neighbours. Only these: the rest of the mesh is QuadRemesh's, and
            /// already as even as it will get.
            /// </summary>
            public void Relax(List<int> moved)
            {
                var seeds = moved
                    .Concat(unsettled.Select(Find))
                    .Where(n => nodeFaces[n].Count > 0)
                    .Distinct()
                    .ToList();
                if (seeds.Count == 0)
                    return;

                var patch = new HashSet<int>(seeds);
                var frontier = seeds;
                for (int ring = 0; ring < RelaxRings; ring++)
                    frontier = frontier.SelectMany(n => neighbours[n]).Where(patch.Add).ToList();

                // What each free node relaxes towards: a node on an edge only the two either side
                // of it along that edge, a node inside a face all its neighbours.
                var free = new List<(int node, int[] towards, int edge, int face)>();
                foreach (var n in patch)
                {
                    if (pinned[n])
                        continue;
                    if (onEdge[n] >= 0)
                    {
                        var e = onEdge[n];
                        var along = neighbours[n].Where(m => edgeOf.TryGetValue(Key(n, m), out var em) && em == e).ToArray();
                        if (along.Length == 2)
                            free.Add((n, along, e, -1));
                    }
                    else if (nodeEdges[n] == null)
                        free.Add((n, neighbours[n].ToArray(), -1, faceOf[nodeFaces[n][0]]));
                }

                for (int sweep = 0; sweep < RelaxSweeps; sweep++)
                {
                    foreach (var (n, towards, edge, face) in free)
                    {
                        var middle = Point3d.Origin;
                        foreach (var m in towards)
                            middle += p[m];
                        middle /= towards.Length;
                        p[n] = edge >= 0 ? OnEdge(edge, middle) : OnFace(face, middle);
                    }
                }
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh();
                mesh.Vertices.UseDoublePrecisionVertices = true;
                foreach (var point in p)
                    mesh.Vertices.Add(point);

                for (int f = 0; f < faces.Count; f++)
                {
                    var ids = flip[f] ? Enumerable.Reverse(faces[f]).ToArray() : faces[f];
                    if (ids.Length == 4)
                        mesh.Faces.AddFace(ids[0], ids[1], ids[2], ids[3]);
                    else
                        mesh.Faces.AddFace(ids[0], ids[1], ids[2]);
                }

                // Drops the nodes welded into others.
                mesh.Compact();
                mesh.Normals.ComputeNormals();
                return mesh;
            }

            void Rebuild()
            {
                neighbours = new List<int>[p.Count];
                nodeFaces = new List<int>[p.Count];
                for (int i = 0; i < p.Count; i++)
                {
                    neighbours[i] = new List<int>();
                    nodeFaces[i] = new List<int>();
                }

                edgeFaces = new Dictionary<(int, int), List<int>>();
                for (int f = 0; f < faces.Count; f++)
                {
                    var ids = faces[f];
                    for (int k = 0; k < ids.Length; k++)
                    {
                        int a = ids[k], b = ids[(k + 1) % ids.Length];
                        nodeFaces[a].Add(f);
                        var key = Key(a, b);
                        if (!edgeFaces.TryGetValue(key, out var list))
                        {
                            edgeFaces[key] = list = new List<int>(2);
                            neighbours[a].Add(b);
                            neighbours[b].Add(a);
                        }
                        list.Add(f);
                    }
                }

                IndexBoundary();
            }

            /// <summary>The mesh edges on each Brep edge, and the Brep edges each node is on, from edgeOf.</summary>
            void IndexBoundary()
            {
                nodeEdges = new List<int>[p.Count];
                meshEdgesOn = Enumerable.Range(0, brep.Edges.Count).Select(_ => new List<(int, int)>()).ToArray();
                foreach (var pair in edgeOf)
                {
                    if (!edgeFaces.ContainsKey(pair.Key))
                        continue;
                    meshEdgesOn[pair.Value].Add(pair.Key);
                    (nodeEdges[pair.Key.Item1] ??= new List<int>()).Add(pair.Value);
                    (nodeEdges[pair.Key.Item2] ??= new List<int>()).Add(pair.Value);
                }
            }

            int AddNode(Point3d point)
            {
                p.Add(point);
                alias.Add(alias.Count);
                return p.Count - 1;
            }

            void AddFace(int[] ids, int like)
            {
                faces.Add(ids);
                faceOf.Add(faceOf[like]);
                flip.Add(flip[like]);
            }

            /// <summary>Where a mesh edge on a Brep edge is split at m, both halves stay on it.</summary>
            void SplitEdgeKey(int a, int b, int m)
            {
                if (!edgeOf.TryGetValue(Key(a, b), out var e))
                    return;
                edgeOf.Remove(Key(a, b));
                edgeOf[Key(a, m)] = e;
                edgeOf[Key(m, b)] = e;
            }

            int Find(int n)
            {
                while (alias[n] != n)
                    n = alias[n] = alias[alias[n]];
                return n;
            }

            void Union(int keep, int drop)
            {
                keep = Find(keep);
                drop = Find(drop);
                if (keep != drop)
                    alias[drop] = keep;
            }

            /// <summary>Rewrites the faces and edges onto the nodes welded ones became.</summary>
            void ApplyAliases()
            {
                var newFaces = new List<int[]>(faces.Count);
                var newFaceOf = new List<int>(faces.Count);
                var newFlip = new List<bool>(faces.Count);
                for (int f = 0; f < faces.Count; f++)
                {
                    var ids = faces[f].Select(Find).Distinct().ToArray();
                    if (ids.Length < 3)
                        continue;
                    newFaces.Add(ids);
                    newFaceOf.Add(faceOf[f]);
                    newFlip.Add(flip[f]);
                }
                faces = newFaces;
                faceOf = newFaceOf;
                flip = newFlip;

                var newEdgeOf = new Dictionary<(int, int), int>(edgeOf.Count);
                foreach (var pair in edgeOf)
                {
                    int a = Find(pair.Key.Item1), b = Find(pair.Key.Item2);
                    if (a != b)
                        newEdgeOf[Key(a, b)] = pair.Value;
                }
                edgeOf = newEdgeOf;

                for (int v = 0; v < cornerNode.Length; v++)
                    if (cornerNode[v] >= 0)
                        cornerNode[v] = Find(cornerNode[v]);

                Rebuild();
            }

            /// <summary>The Brep face a point over it lies on.</summary>
            int BrepFaceAt(Point3d point)
            {
                if (brep.Faces.Count == 1)
                    return 0;

                IEnumerable<int> candidates = null;
                if (brep.ClosestPoint(point, out _, out var ci, out _, out _, 0.0, out _))
                {
                    if (ci.ComponentIndexType == ComponentIndexType.BrepFace)
                        return ci.Index;
                    if (ci.ComponentIndexType == ComponentIndexType.BrepEdge)
                        candidates = brep.Edges[ci.Index].AdjacentFaces();
                }
                candidates = candidates ?? Enumerable.Range(0, brep.Faces.Count);

                // Nearest to an edge: of the faces along it, the one the point is over, and of
                // those the nearest.
                return candidates
                    .Select(f =>
                    {
                        var face = brep.Faces[f];
                        face.ClosestPoint(point, out var u, out var v);
                        return (f, outside: face.IsPointOnFace(u, v) == PointFaceRelation.Exterior, distance: point.DistanceTo(face.PointAt(u, v)));
                    })
                    .OrderBy(c => c.outside)
                    .ThenBy(c => c.distance)
                    .First().f;
            }

            /// <summary>
            /// The Brep faces a point lies on, to within tolerance: none off the Brep, one inside a
            /// face, more on an edge between faces.
            /// </summary>
            List<int> FacesAt(Point3d point, double tolerance)
            {
                var found = new List<int>();
                for (int f = 0; f < brep.Faces.Count; f++)
                {
                    var face = brep.Faces[f];
                    var box = face.GetBoundingBox(false);
                    box.Inflate(tolerance);
                    if (!box.Contains(point))
                        continue;
                    face.ClosestPoint(point, out var u, out var v);
                    if (point.DistanceTo(face.PointAt(u, v)) <= tolerance && face.IsPointOnFace(u, v, tolerance) != PointFaceRelation.Exterior)
                        found.Add(f);
                }
                return found;
            }

            /// <summary>The point on Brep face f nearest a point near it.</summary>
            Point3d OnFace(int f, Point3d point)
            {
                if (planes[f].IsValid)
                    return planes[f].ClosestPoint(point);
                var face = brep.Faces[f];
                face.ClosestPoint(point, out var u, out var v);
                return face.PointAt(u, v);
            }

            static Vector3d Normal(BrepFace face, double u, double v)
            {
                var normal = face.NormalAt(u, v);
                if (face.OrientationIsReversed)
                    normal.Reverse();
                return normal;
            }

            Vector3d FaceNormal(int f)
            {
                var ids = faces[f];
                return ids.Length == 4
                    ? Vector3d.CrossProduct(p[ids[2]] - p[ids[0]], p[ids[3]] - p[ids[1]])
                    : Vector3d.CrossProduct(p[ids[1]] - p[ids[0]], p[ids[2]] - p[ids[0]]);
            }

            int Nearest(Point3d point, IEnumerable<int> nodes, out double distance)
            {
                var best = -1;
                distance = double.MaxValue;
                foreach (var n in nodes)
                {
                    var d = point.DistanceTo(p[n]);
                    if (d < distance)
                    {
                        distance = d;
                        best = n;
                    }
                }
                return best;
            }

            static int IndexOfEdge(int[] face, int a, int b)
            {
                for (int i = 0; i < face.Length; i++)
                {
                    int x = face[i], y = face[(i + 1) % face.Length];
                    if ((x == a && y == b) || (x == b && y == a))
                        return i;
                }
                throw new InvalidOperationException("The face does not have that edge.");
            }

            /// <summary>
            /// How far along Brep edge e node n is, as anything that grows from the edge's start
            /// to its end. Remembered: the nodes on an edge get put in order over and over while
            /// the lines are evened out, and a node doesn't move until they are.
            /// </summary>
            double Along(int n, int e)
            {
                if (!alongEdge.TryGetValue((n, e), out var t))
                {
                    if (lines[e] is Line line)
                        t = line.ClosestParameter(p[n]);
                    else
                        edgeCurves[e].ClosestPoint(p[n], out t);
                    alongEdge[(n, e)] = t;
                }
                return t;
            }

            /// <summary>The point on Brep edge e nearest a point.</summary>
            Point3d OnEdge(int e, Point3d point)
            {
                if (lines[e] is Line line)
                    return line.ClosestPoint(point, true);
                edgeCurves[e].ClosestPoint(point, out var t);
                return edgeCurves[e].PointAt(t);
            }

            double DistanceTo(int e, Point3d point) => point.DistanceTo(OnEdge(e, point));

            static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

            static string Format(Point3d point) => $"({point.X:G4}, {point.Y:G4}, {point.Z:G4})";
        }

        /// <summary>A line of nodes along a Brep edge, in order along it.</summary>
        sealed class Chain
        {
            public readonly int[] Nodes;
            /// <summary>Round a closed edge, so the last node joins back to the first.</summary>
            public readonly bool Loop;

            public Chain(int[] nodes, bool loop)
            {
                Nodes = nodes;
                Loop = loop;
            }

            public int Segments => Loop ? Nodes.Length : Nodes.Length - 1;

            public IEnumerable<(int, int)> Pairs()
            {
                for (int i = 0; i < Segments; i++)
                    yield return (Nodes[i], Nodes[(i + 1) % Nodes.Length]);
            }
        }
    }
}
