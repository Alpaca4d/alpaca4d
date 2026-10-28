using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// The pieces a topology optimisation's solid design falls into, and which of them make the structure.
    ///
    /// Solid elements make one piece when they share a face: a brick that touches the rest along an edge,
    /// or at a corner, hinges there, and analysed on its own it is a mechanism. A piece belongs to the
    /// structure when the supports on its nodes hold it still - every rigid motion, three translations
    /// and three rotations, stopped by a restraint - or when it touches an element that is not a solid,
    /// a beam or a shell, which may hold it instead. The rest is left out: bits the optimisation left
    /// floating, which carry nothing and would make the analysis of the design singular.
    ///
    /// On a coarse mesh a diagonal member one or two elements thick comes out as a staircase, its steps
    /// meeting along edges. The material the filter spread between them is there, only below the
    /// threshold: where two pieces meet only along an edge or at a corner, the densest cell joining them
    /// face to face is made solid too, until they hold together.
    /// </summary>
    public sealed class SolidPieces
    {
        /// <summary>Per cell: solid, and in a piece of the structure.</summary>
        public bool[] Kept { get; }

        /// <summary>How many solid cells are left out.</summary>
        public int LeftOut { get; }

        /// <summary>How many pieces those make.</summary>
        public int PiecesLeftOut { get; }

        /// <summary>How many cells below the threshold were made solid to join pieces meeting along an edge or at a corner.</summary>
        public int Bridged { get; }

        private SolidPieces(bool[] kept, int leftOut, int piecesLeftOut, int bridged)
        {
            Kept = kept;
            LeftOut = leftOut;
            PiecesLeftOut = piecesLeftOut;
            Bridged = bridged;
        }

        /// <summary>
        /// The structure among the cells marked <paramref name="solid"/>.
        /// </summary>
        /// <param name="cells">The cells, their node tags corners first.</param>
        /// <param name="solid">Per cell, whether it is solid.</param>
        /// <param name="node">The position of a node tag.</param>
        /// <param name="restraints">Each direction a support holds a node in: one entry per node and direction.</param>
        /// <param name="attached">Node tags shared with elements that are not solids.</param>
        /// <param name="densities">Per cell, its density: the densest cell is the one a joint is made with. Left out, the first.</param>
        public static SolidPieces Find(IReadOnlyList<SolidCell> cells, IReadOnlyList<bool> solid, Func<int, Point3d> node,
                                       IEnumerable<(int node, Vector3d direction)> restraints, ICollection<int> attached,
                                       IReadOnlyList<double> densities = null)
        {
            if (cells == null || solid == null || solid.Count != cells.Count)
                throw new ArgumentException($"{solid?.Count ?? 0} solid flags for {cells?.Count ?? 0} cells.");

            int n = cells.Count;
            var byCorner = new Dictionary<int, List<int>>();
            for (int c = 0; c < n; c++)
            {
                var cell = cells[c];
                for (int k = 0; k < SolidCell.CornerCount(cell.Shape); k++)
                {
                    if (!byCorner.TryGetValue(cell.Nodes[k], out var list))
                        byCorner[cell.Nodes[k]] = list = new List<int>();
                    list.Add(c);
                }
            }

            var isSolid = solid.ToArray();
            int bridged = Bridge(cells, isSolid, byCorner, densities);
            var piece = Join(cells, isSolid, byCorner);

            // Each piece's nodes, every node of its cells: a support can stand on a mid-side node.
            var nodesOf = new Dictionary<int, HashSet<int>>();
            for (int c = 0; c < n; c++)
            {
                if (!isSolid[c]) continue;
                int root = piece.Root(c);
                if (!nodesOf.TryGetValue(root, out var nodes))
                    nodesOf[root] = nodes = new HashSet<int>();
                nodes.UnionWith(cells[c].Nodes);
            }

            var restraintsAt = new Dictionary<int, List<Vector3d>>();
            // Plain arithmetic throughout, rather than Vector3d's own Unitize and IsTiny, which call into Rhino.
            foreach (var (tag, direction) in restraints ?? Enumerable.Empty<(int, Vector3d)>())
            {
                double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
                if (!(length > 1e-12) || double.IsInfinity(length)) continue;
                if (!restraintsAt.TryGetValue(tag, out var list))
                    restraintsAt[tag] = list = new List<Vector3d>();
                list.Add(new Vector3d(direction.X / length, direction.Y / length, direction.Z / length));
            }

            var held = new HashSet<int>();
            foreach (var pair in nodesOf)
            {
                if (attached != null && pair.Value.Any(attached.Contains))
                    held.Add(pair.Key);
                else if (HeldStill(pair.Value, node, restraintsAt))
                    held.Add(pair.Key);
            }

            var kept = new bool[n];
            int leftOut = 0;
            for (int c = 0; c < n; c++)
            {
                if (!isSolid[c]) continue;
                kept[c] = held.Contains(piece.Root(c));
                if (!kept[c]) leftOut++;
            }
            return new SolidPieces(kept, leftOut, nodesOf.Count - held.Count, bridged);
        }

        /// <summary>How many corners two cells share.</summary>
        private static int Shared(SolidCell a, SolidCell b)
        {
            int count = 0, ca = SolidCell.CornerCount(a.Shape), cb = SolidCell.CornerCount(b.Shape);
            for (int i = 0; i < ca; i++)
                for (int j = 0; j < cb; j++)
                    if (a.Nodes[i] == b.Nodes[j]) count++;
            return count;
        }

        /// <summary>Whether two cells share a face: three corners of a tetrahedron's, four of a hexahedron's.</summary>
        private static bool FaceToFace(SolidCell a, SolidCell b, int shared) =>
            shared >= (a.Shape == SolidShape.Hexahedron && b.Shape == SolidShape.Hexahedron ? 4 : 3);

        /// <summary>The solid cells joined face to face into pieces.</summary>
        private static UnionFind Join(IReadOnlyList<SolidCell> cells, bool[] solid, Dictionary<int, List<int>> byCorner)
        {
            var piece = new UnionFind(cells.Count);
            for (int a = 0; a < cells.Count; a++)
            {
                if (!solid[a]) continue;
                var cell = cells[a];
                for (int k = 0; k < SolidCell.CornerCount(cell.Shape); k++)
                    foreach (int b in byCorner[cell.Nodes[k]])
                        if (b > a && solid[b] && piece.Root(a) != piece.Root(b) && FaceToFace(cell, cells[b], Shared(cell, cells[b])))
                            piece.Join(a, b);
            }
            return piece;
        }

        /// <summary>
        /// Makes solid, pass after pass, a cell joining two pieces that meet only along an edge or at a
        /// corner: one that shares a face with a cell of one piece and more corners than it with a cell of
        /// the other. At a corner that takes two passes, the first leaving an edge. Returns how many.
        /// </summary>
        private static int Bridge(IReadOnlyList<SolidCell> cells, bool[] solid, Dictionary<int, List<int>> byCorner, IReadOnlyList<double> densities)
        {
            int added = 0;
            for (int pass = 0; pass < 100; pass++)
            {
                // Each pair of pieces that touch, by the two cells that share the most corners: an edge
                // takes one cell to close, a corner two.
                var piece = Join(cells, solid, byCorner);
                var closest = new Dictionary<(int, int), (int a, int b, int shared)>();
                foreach (var touching in byCorner.Values)
                {
                    for (int i = 0; i < touching.Count; i++)
                    {
                        int a = touching[i];
                        if (!solid[a]) continue;
                        for (int j = i + 1; j < touching.Count; j++)
                        {
                            int b = touching[j];
                            if (!solid[b]) continue;
                            int ra = piece.Root(a), rb = piece.Root(b);
                            if (ra == rb) continue;
                            var pair = (Math.Min(ra, rb), Math.Max(ra, rb));
                            int shared = Shared(cells[a], cells[b]);
                            if (!closest.TryGetValue(pair, out var best) || shared > best.shared)
                                closest[pair] = (a, b, shared);
                        }
                    }
                }

                var adds = new List<int>();
                foreach (var contact in closest.Values)
                {
                    int c = Between(cells, solid, byCorner, densities, contact.a, contact.b);
                    if (c < 0) c = Between(cells, solid, byCorner, densities, contact.b, contact.a);
                    if (c >= 0) adds.Add(c);
                }
                if (adds.Count == 0)
                    break;
                foreach (int c in adds)
                    if (!solid[c])
                    {
                        solid[c] = true;
                        added++;
                    }
            }
            return added;
        }

        /// <summary>The densest cell, not solid, sharing a face with <paramref name="a"/> and more corners with <paramref name="b"/> than a does; or -1.</summary>
        private static int Between(IReadOnlyList<SolidCell> cells, bool[] solid, Dictionary<int, List<int>> byCorner, IReadOnlyList<double> densities, int a, int b)
        {
            var cellA = cells[a];
            int already = Shared(cellA, cells[b]);
            int best = -1, bestShared = already;
            double bestDensity = double.NegativeInfinity;
            var seen = new HashSet<int>();
            for (int k = 0; k < SolidCell.CornerCount(cellA.Shape); k++)
            {
                foreach (int c in byCorner[cellA.Nodes[k]])
                {
                    if (solid[c] || !seen.Add(c) || !FaceToFace(cellA, cells[c], Shared(cellA, cells[c]))) continue;
                    int withB = Shared(cells[c], cells[b]);
                    double density = densities?[c] ?? 0.0;
                    if (withB > bestShared || (withB == bestShared && best >= 0 && density > bestDensity))
                    {
                        best = c;
                        bestShared = withB;
                        bestDensity = density;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Whether the restraints on <paramref name="nodes"/> stop every rigid motion of them. A motion
        /// u(x) = a + w x x moves node p along d by a.d + w.(p x d): the restraints stop it all when the
        /// rows (d, p x d) span the six dimensions of (a, w).
        /// </summary>
        private static bool HeldStill(HashSet<int> nodes, Func<int, Point3d> node, Dictionary<int, List<Vector3d>> restraintsAt)
        {
            var rows = new List<(Point3d at, Vector3d d)>();
            foreach (int tag in nodes)
                if (restraintsAt.TryGetValue(tag, out var directions))
                    foreach (var d in directions)
                        rows.Add((node(tag), d));
            if (rows.Count < 6)
                return false;

            // About the restraints' own centre and to their own size, so the two halves of a row weigh alike.
            double cx = rows.Average(r => r.at.X), cy = rows.Average(r => r.at.Y), cz = rows.Average(r => r.at.Z);
            double size = Math.Sqrt(rows.Max(r => (r.at.X - cx) * (r.at.X - cx) + (r.at.Y - cy) * (r.at.Y - cy) + (r.at.Z - cz) * (r.at.Z - cz)));
            if (!(size > 0.0)) size = 1.0;

            var m = new double[6, 6];
            foreach (var (at, d) in rows)
            {
                double px = (at.X - cx) / size, py = (at.Y - cy) / size, pz = (at.Z - cz) / size;
                var row = new[] { d.X, d.Y, d.Z, py * d.Z - pz * d.Y, pz * d.X - px * d.Z, px * d.Y - py * d.X };
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 6; j++)
                        m[i, j] += row[i] * row[j];
            }
            return FullRank(m);
        }

        /// <summary>Whether a symmetric positive semi-definite 6 x 6 matrix is regular: Cholesky, largest pivot first.</summary>
        private static bool FullRank(double[,] m)
        {
            const int size = 6;
            double scale = Enumerable.Range(0, size).Max(i => m[i, i]);
            if (!(scale > 0.0)) return false;
            var used = new bool[size];
            for (int step = 0; step < size; step++)
            {
                int p = -1;
                for (int i = 0; i < size; i++)
                    if (!used[i] && (p < 0 || m[i, i] > m[p, p]))
                        p = i;
                if (!(m[p, p] > 1e-9 * scale))
                    return false;
                used[p] = true;
                for (int i = 0; i < size; i++)
                {
                    if (used[i]) continue;
                    double f = m[i, p] / m[p, p];
                    for (int j = 0; j < size; j++)
                        m[i, j] -= f * m[p, j];
                }
            }
            return true;
        }

        private sealed class UnionFind
        {
            private readonly int[] _parent;

            public UnionFind(int n)
            {
                _parent = Enumerable.Range(0, n).ToArray();
            }

            public int Root(int i)
            {
                while (_parent[i] != i)
                {
                    _parent[i] = _parent[_parent[i]];
                    i = _parent[i];
                }
                return i;
            }

            public void Join(int a, int b)
            {
                int ra = Root(a), rb = Root(b);
                if (ra != rb) _parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
            }
        }
    }
}
