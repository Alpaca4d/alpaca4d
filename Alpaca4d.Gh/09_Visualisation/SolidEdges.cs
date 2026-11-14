using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The edges Model View draws on solids: the elements' own edges, corner to corner, and only
    /// those on the outside of the solid.
    ///
    /// A solid element's mesh is its whole closed surface, and its wires were every face of every
    /// element, deep inside the solid too. Rhino draws wires a little towards the camera so that
    /// they sit on the shaded surface, which let the inner edges near the surface show through it,
    /// a star at every node. A ten node tetrahedron's faces, each cut into four triangles at the
    /// nodes on its edges, also drew lines joining those nodes, which are no edges of the element.
    ///
    /// A face is known by the nodes at its corners: one that two elements share is inside the
    /// solid, one that a single element has is on its outside, or against an element the filter
    /// left out.
    /// </summary>
    internal sealed class SolidEdges
    {
        // Every face seen so far that has an edge of an element, by its nodes sorted: how many
        // elements have it, and its edges.
        private readonly Dictionary<(int, int, int, int), Face> _faces = new Dictionary<(int, int, int, int), Face>();
        private List<Line> _lines = new List<Line>();

        private sealed class Face
        {
            public int Count;
            public List<(int A, int B, Line Line)> Edges;
        }

        public void Clear()
        {
            _faces.Clear();
            _lines = new List<Line>();
        }

        /// <summary>
        /// A solid element, its mesh already moved to where it is drawn, its nodes in the order of
        /// the mesh's vertices, and its corners the first <paramref name="corners"/> of them - four
        /// on a ten node tetrahedron, whose six edge nodes follow. False, with nothing taken, when a
        /// vertex has no node: the caller draws the mesh's own wires instead.
        /// </summary>
        public bool Add(Mesh mesh, IList<int?> nodes, int corners)
        {
            if (mesh == null || nodes == null || nodes.Count < mesh.Vertices.Count) return false;
            for (int i = 0; i < mesh.Vertices.Count; i++)
                if (!nodes[i].HasValue) return false;

            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                var face = mesh.Faces[f];
                int count = face.IsQuad ? 4 : 3;
                var vertices = new[] { face.A, face.B, face.C, face.D };

                // A line joining two edge nodes crosses a face; only a corner's lines run along
                // the element's edges. A face with none of them has nothing to draw, inside or out.
                List<(int, int, Line)> edges = null;
                for (int k = 0; k < count; k++)
                {
                    int a = vertices[k], b = vertices[(k + 1) % count];
                    if (a >= corners && b >= corners) continue;
                    if (edges == null) edges = new List<(int, int, Line)>(count);
                    edges.Add((nodes[a].Value, nodes[b].Value, new Line(mesh.Vertices[a], mesh.Vertices[b])));
                }
                if (edges == null) continue;

                var key = Key(nodes, vertices, count);
                if (_faces.TryGetValue(key, out var seen))
                    seen.Count++;
                else
                    _faces[key] = new Face { Count = 1, Edges = edges };
            }

            return true;
        }

        /// <summary>
        /// The edges of the faces only one element has, each once, ready to draw; the faces are let
        /// go, which on a large solid are many.
        /// </summary>
        public void Join()
        {
            var drawn = new HashSet<(int, int)>();
            var lines = new List<Line>();
            foreach (var face in _faces.Values)
            {
                if (face.Count != 1) continue;
                foreach (var (a, b, line) in face.Edges)
                    if (drawn.Add(a < b ? (a, b) : (b, a)))
                        lines.Add(line);
            }

            _faces.Clear();
            _lines = lines;
        }

        public void Draw(DisplayPipeline display, Color colour, int thickness)
        {
            if (_lines.Count > 0)
                display.DrawLines(_lines, colour, thickness);
        }

        private static (int, int, int, int) Key(IList<int?> nodes, int[] vertices, int count)
        {
            var sorted = new int[4];
            for (int k = 0; k < 4; k++)
                sorted[k] = k < count ? nodes[vertices[k]].Value : int.MaxValue;
            System.Array.Sort(sorted);
            return (sorted[0], sorted[1], sorted[2], sorted[3]);
        }
    }
}
