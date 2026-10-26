using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// What Model View and View Results draw, put together for the viewport.
    ///
    /// Rhino draws a mesh, or a batch of lines, in one call however big it is, and on a small mesh the
    /// call costs more than the drawing. Ten thousand bricks drawn one by one were ten thousand calls
    /// for the faces and ten thousand for the edges, every time the view moved; joined, they are one
    /// of each. Nothing is welded: every element keeps its own vertices, so its colours and its edges
    /// look as they did when it was drawn on its own.
    /// </summary>
    internal static class Joined
    {
        /// <summary>The meshes as one, or null when there are none.</summary>
        public static Mesh Of(IEnumerable<Mesh> meshes)
        {
            var joined = new Mesh();
            joined.Append(meshes);
            if (joined.Vertices.Count == 0) return null;

            // Append keeps the normals only if every mesh had them, and a shaded mesh needs them
            // to be lit.
            if (joined.Normals.Count != joined.Vertices.Count)
                joined.Normals.ComputeNormals();
            return joined;
        }
    }

    /// <summary>
    /// Beams drawn as lines: the straight ones in a batch per colour, drawn in one call each, and any
    /// other curve on its own.
    /// </summary>
    internal sealed class JoinedLines
    {
        private readonly Dictionary<Color, List<Line>> _straight = new Dictionary<Color, List<Line>>();
        private readonly List<(Curve Curve, Color Colour)> _curved = new List<(Curve, Color)>();

        public void Clear()
        {
            _straight.Clear();
            _curved.Clear();
        }

        public void Add(Curve curve, Color colour)
        {
            if (curve == null) return;
            if (curve.IsLinear())
            {
                if (!_straight.TryGetValue(colour, out var lines))
                    _straight[colour] = lines = new List<Line>();
                lines.Add(new Line(curve.PointAtStart, curve.PointAtEnd));
            }
            else
                _curved.Add((curve, colour));
        }

        public void Draw(DisplayPipeline display)
        {
            foreach (var batch in _straight)
                display.DrawLines(batch.Value, batch.Key);
            foreach (var item in _curved)
                display.DrawCurve(item.Curve, item.Colour);
        }
    }
}
