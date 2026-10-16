using System;
using System.Collections.Generic;
using System.Drawing;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

using Alpaca4d.Explorer;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// A design's geometry as the .glb the explorer shows in 3D.
    ///
    /// An AlpacaModel is drawn as Model View draws it - beams as their sections carried along them,
    /// shells and solids as their meshes, each in its element's colour - so plugging in the model is
    /// all it takes. Anything else is drawn as what it is: a mesh with its vertex colours, which is
    /// how a coloured result arrives, a Brep meshed, a curve as a line, a point as a point. Text dots
    /// and text become labels, which the explorer draws over the model facing the camera, as Rhino
    /// draws a text dot; turned into geometry they would turn away and shrink with the model.
    ///
    /// Everything goes into one set of triangles and one set of lines, coloured per vertex: a viewer
    /// draws two big batches far faster than thousands of small ones, and a colour per element is
    /// all a design study needs to tell its members apart.
    /// </summary>
    internal static class ExplorerModel
    {
        private static readonly Color Neutral = Color.FromArgb(190, 193, 198);
        private static readonly Color Ink = Alpaca4d.Colors.DarkTech;

        /// <summary>The .glb of the items, or null when none of them can be drawn.</summary>
        public static byte[] Build(IList<IGH_Goo> items, IList<Color> colours, RhinoDoc doc, List<string> warnings)
        {
            double toMetres = doc == null ? 1.0 : RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters);
            double tolerance = doc?.ModelAbsoluteTolerance ?? 0.01;
            var batch = new Batch(toMetres, tolerance);

            int skipped = 0;
            for (int i = 0; i < items.Count; i++)
            {
                Color? colour = null;
                if (colours.Count > 0) colour = colours[Math.Min(i, colours.Count - 1)];
                if (!batch.Add(items[i], colour)) skipped++;
            }
            if (skipped > 0)
                warnings.Add($"{skipped} of the Model items are not drawn in 3D: only AlpacaModels, meshes, Breps, SubDs, curves, points, text dots and text are.");

            var writer = new GlbWriter { Generator = "Alpaca4d Explorer Record" };
            writer.AddTriangles(batch.Positions, batch.Normals, batch.Colours, batch.Triangles);
            writer.AddLines(batch.LinePositions, batch.LineColours, batch.Lines);
            writer.AddPoints(batch.PointPositions, batch.PointColours);
            foreach (var label in batch.Labels)
                writer.AddLabel(label.Text, label.At.X, label.At.Y, label.At.Z, toMetres, label.Colour.R, label.Colour.G, label.Colour.B, label.Dot);
            return writer.IsEmpty ? null : writer.ToArray();
        }

        private sealed class Batch
        {
            private readonly double _toMetres;
            private readonly double _tolerance;

            public readonly List<float> Positions = new List<float>();
            public readonly List<float> Normals = new List<float>();
            public readonly List<float> Colours = new List<float>();
            public readonly List<int> Triangles = new List<int>();
            public readonly List<float> LinePositions = new List<float>();
            public readonly List<float> LineColours = new List<float>();
            public readonly List<int> Lines = new List<int>();
            public readonly List<float> PointPositions = new List<float>();
            public readonly List<float> PointColours = new List<float>();
            public readonly List<Label> Labels = new List<Label>();

            public Batch(double toMetres, double tolerance)
            {
                _toMetres = toMetres;
                _tolerance = tolerance;
            }

            public bool Add(IGH_Goo goo, Color? colour)
            {
                if (goo == null) return false;

                var model = ExplorerGoo.As<Alpaca4d.Model>(goo);
                if (model != null)
                {
                    AddModel(model, colour);
                    return true;
                }
                // Something picked from the Rhino document keeps the colour Rhino draws it in - its
                // own, or its layer's - as a text dot does in Rhino. A colour given in Colours comes
                // first, a mesh's own vertex colours next, then this one.
                Color? own = ObjectColour(goo);

                if (goo is GH_Point point)
                {
                    AddPoint(point.Value, colour ?? own ?? Ink);
                    return true;
                }

                // Text dots and text arrive wrapped in whatever the plugin that made them uses.
                var geometry = GH_Convert.ToGeometryBase(goo) ?? ExplorerGoo.As<GeometryBase>(goo);
                switch (geometry)
                {
                    case TextDot dot:
                        Labels.Add(new Label(dot.Text, dot.Point, colour ?? own ?? Ink, true));
                        return !string.IsNullOrWhiteSpace(dot.Text);
                    case TextEntity text:
                        Labels.Add(new Label(text.PlainText, text.GetBoundingBox(true).Center, colour ?? own ?? Ink, false));
                        return !string.IsNullOrWhiteSpace(text.PlainText);
                    case Rhino.Geometry.Point rhinoPoint:
                        AddPoint(rhinoPoint.Location, colour ?? own ?? Ink);
                        return true;
                    case PointCloud cloud:
                        bool cloudColours = colour == null && cloud.ContainsColors;
                        foreach (var item in cloud)
                            AddPoint(item.Location, cloudColours ? item.Color : colour ?? own ?? Ink);
                        return cloud.Count > 0;
                    case Mesh mesh:
                        AddMesh(mesh, colour, own);
                        return true;
                    case Brep brep:
                        return AddBrep(brep, colour, own);
                    case Extrusion extrusion:
                        return AddBrep(extrusion.ToBrep(true), colour, own);
                    case Surface surface:
                        return AddBrep(surface.ToBrep(), colour, own);
                    case SubD subd:
                        var subdMesh = Mesh.CreateFromSubD(subd, 3);
                        if (subdMesh == null) return false;
                        AddMesh(subdMesh, colour, own);
                        return true;
                    case Curve curve:
                        return AddCurve(curve, colour ?? own ?? Neutral);
                }
                return false;
            }

            private void AddModel(Alpaca4d.Model model, Color? colour)
            {
                foreach (var beam in model.Beams)
                {
                    if (beam?.Curve == null) continue;
                    var mesh = ModelView.ExtrudedBeam(beam);
                    if (mesh != null) AddMesh(mesh, colour ?? beam.Color);
                    else AddCurve(beam.Curve, colour ?? beam.Color);
                }
                foreach (var shell in model.Shells)
                    if (shell?.Mesh != null) AddMesh(shell.Mesh, colour ?? shell.Color);
                foreach (var brick in model.Bricks)
                    if (brick?.Mesh != null) AddMesh(brick.Mesh, colour ?? brick.Color);
            }

            /// <summary>The colour Rhino draws a referenced object in, or null for geometry made in Grasshopper.</summary>
            private static Color? ObjectColour(IGH_Goo goo)
            {
                if (!(goo is IGH_GeometricGoo geometric) || !geometric.IsReferencedGeometry) return null;
                var doc = RhinoDoc.ActiveDoc;
                var rhinoObject = doc?.Objects.FindId(geometric.ReferenceID);
                return rhinoObject?.Attributes.DrawColor(doc);
            }

            private bool AddBrep(Brep brep, Color? colour, Color? fallback)
            {
                if (brep == null) return false;
                var meshes = Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh);
                if (meshes == null || meshes.Length == 0) return false;
                foreach (var mesh in meshes) AddMesh(mesh, colour, fallback);
                return true;
            }

            /// <summary>
            /// A mesh, in its own vertex colours when it has them and no colour is given, otherwise
            /// in <paramref name="fallback"/>. Creases sharper than 30° are unwelded first, so a
            /// beam's flat sides are shaded flat rather than rounded off across its corners.
            /// </summary>
            private void AddMesh(Mesh source, Color? colour, Color? fallback = null)
            {
                var mesh = source.DuplicateMesh();
                mesh.Faces.ConvertQuadsToTriangles();
                mesh.Unweld(Math.PI / 6, true);
                mesh.Normals.ComputeNormals();

                bool ownColours = colour == null && mesh.VertexColors.Count == mesh.Vertices.Count && mesh.VertexColors.Count > 0;
                Color fill = colour ?? fallback ?? Neutral;
                int first = Positions.Count / 3;

                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    var point = mesh.Vertices[i];
                    var normal = i < mesh.Normals.Count ? mesh.Normals[i] : Vector3f.ZAxis;
                    var c = ownColours ? mesh.VertexColors[i] : fill;
                    GlbWriter.Point(point.X, point.Y, point.Z, _toMetres, Positions);
                    GlbWriter.Normal(normal.X, normal.Y, normal.Z, Normals);
                    GlbWriter.Colour(c.R, c.G, c.B, Colours);
                }
                for (int i = 0; i < mesh.Faces.Count; i++)
                {
                    var face = mesh.Faces[i];
                    Triangles.Add(first + face.A);
                    Triangles.Add(first + face.B);
                    Triangles.Add(first + face.C);
                }
            }

            private void AddPoint(Point3d point, Color colour)
            {
                GlbWriter.Point(point.X, point.Y, point.Z, _toMetres, PointPositions);
                GlbWriter.Colour(colour.R, colour.G, colour.B, PointColours);
            }

            private bool AddCurve(Curve curve, Color colour)
            {
                Polyline polyline;
                if (!curve.TryGetPolyline(out polyline))
                {
                    var approximation = curve.ToPolyline(_tolerance, RhinoMath.ToRadians(5), 0, 0);
                    if (approximation == null || !approximation.TryGetPolyline(out polyline)) return false;
                }
                if (polyline.Count < 2) return false;

                int first = LinePositions.Count / 3;
                foreach (var point in polyline)
                {
                    GlbWriter.Point(point.X, point.Y, point.Z, _toMetres, LinePositions);
                    GlbWriter.Colour(colour.R, colour.G, colour.B, LineColours);
                }
                for (int i = 0; i < polyline.Count - 1; i++)
                {
                    Lines.Add(first + i);
                    Lines.Add(first + i + 1);
                }
                return true;
            }
        }

        /// <summary>A text dot or a text, as the explorer labels it.</summary>
        private struct Label
        {
            public readonly string Text;
            public readonly Point3d At;
            public readonly Color Colour;
            public readonly bool Dot;

            public Label(string text, Point3d at, Color colour, bool dot)
            {
                Text = text;
                At = at;
                Colour = colour;
                Dot = dot;
            }
        }
    }
}
