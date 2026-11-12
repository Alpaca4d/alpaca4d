using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

using Alpaca4d.Report;
using static Alpaca4d.Gh.ModelViewSettings;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Draws a report's figures with Model View, without a Model View on the canvas.
    ///
    /// A Model View is made for each figure, in memory only: its menus are set from the figure's
    /// settings in the template, as clicks would set them, it is given the model and solved, and
    /// what it built is drawn by a display conduit of this class's own while the view is captured.
    /// So a figure is drawn by the very code that draws the canvas's Model View, and looks the way
    /// that one would with the same settings.
    ///
    /// For as long as it is open, the active Rhino view is borrowed: its camera is moved, its grid
    /// and axes are hidden, Grasshopper's own previews are switched off and Rhino's objects are left
    /// out of the drawing. All of it is put back on Dispose.
    /// </summary>
    internal sealed class ReportFigures : IFigureMaker, IDisposable
    {
        /// <summary>The pictures written, in order.</summary>
        public readonly List<string> Pictures = new List<string>();

        /// <summary>What Model View said while drawing them: warnings, as "Figure n: ...".</summary>
        public readonly List<string> Remarks = new List<string>();

        /// <summary>The settings a figure can have, for telling a misspelt one from one that is not there.</summary>
        public static readonly string[] Settings = ModelViewSettings.Names.Concat(new[]
        {
            "model", "mode", "caption", "width", "aspect", "pixels", "trim",
            "view", "projection", "eye", "zoom", "camera", "legend", "legendTitle", "footfall", "quantity", "table",
        }).ToArray();

        private readonly string _folder;
        private readonly RhinoView _view;
        private readonly Conduit _conduit = new Conduit();

        private readonly bool _grid, _gridAxes, _worldAxes;
        private readonly GH_Document _canvas;
        private readonly GH_PreviewMode _preview;

        private bool _primed;
        private int _count;

        public ReportFigures(string folder)
        {
            _folder = folder;
            Directory.CreateDirectory(folder);

            var doc = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("Rhino has no document open to draw the figures in.");
            _view = doc.Views.ActiveView ?? doc.Views.FirstOrDefault()
                    ?? throw new InvalidOperationException("Rhino has no view to draw the figures in.");

            var vp = _view.ActiveViewport;
            vp.PushViewProjection();
            _grid = vp.ConstructionGridVisible;
            _gridAxes = vp.ConstructionAxesVisible;
            _worldAxes = vp.WorldAxesVisible;
            vp.ConstructionGridVisible = false;
            vp.ConstructionAxesVisible = false;
            vp.WorldAxesVisible = false;

            // Everything else Grasshopper draws stays out of the pictures.
            _canvas = Grasshopper.Instances.ActiveCanvas?.Document;
            if (_canvas != null)
            {
                _preview = _canvas.PreviewMode;
                _canvas.PreviewMode = GH_PreviewMode.Disabled;
            }

            _conduit.Enabled = true;
        }

        public void Dispose()
        {
            _conduit.View = null;
            _conduit.Enabled = false;

            var vp = _view.ActiveViewport;
            vp.PopViewProjection();
            vp.ConstructionGridVisible = _grid;
            vp.ConstructionAxesVisible = _gridAxes;
            vp.WorldAxesVisible = _worldAxes;

            if (_canvas != null)
                _canvas.PreviewMode = _preview;

            _view.Redraw();
        }

        public Size? ImageSize(string path)
        {
            try
            {
                using (var image = Image.FromFile(path))
                    return image.Size;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The values of each result of the figure that asks for a table, read as Model View reads them
        /// for the picture: the same components - all three forces when the picture draws the reaction
        /// force - in the same units, at the same step, through the same layer and in the same axes.
        /// </summary>
        public IList<ResultValues> Values(Alpaca4d.Model model, JObject figure, out string problem)
        {
            problem = null;
            var made = new List<ResultValues>();
            if (!model.IsAnalysed) { problem = "the model is not analysed"; return null; }

            int step = (int?)figure["step"] ?? 0;
            int layer = Math.Max(0, Array.FindIndex(Alpaca4d.Result.Read.LayerNames, n => Same(n, (string)figure["layer"] ?? "")));
            bool local = Same((string)figure["axes"] ?? "", "Local");

            var entries = figure["results"] is JArray list
                ? list.OfType<JObject>().ToList()
                : new List<JObject> { figure };
            foreach (var entry in entries.Where(e => (bool?)e["table"] == true))
            {
                string name = (string)entry["result"] ?? "Displacement";
                int place = Array.FindIndex(ResultField.MenuOrder, x => Same(ResultField.FamilyNames[(int)x], name) || Same(x.ToString(), name));
                if (place < 0) { problem = $"\"{name}\" is not a result of Model View"; return null; }
                var family = ResultField.MenuOrder[place];
                var names = ResultField.ComponentNames(family);

                // The components the picture draws, a column or a row of the table each.
                var components = new List<int>();
                if (entry["forces"] is JArray forces)
                    foreach (var force in forces.Select(x => (string)x))
                    {
                        int i = Array.FindIndex(ResultField.BeamForceShortNames, n => Same(n, force));
                        if (i < 0) i = Array.FindIndex(names, n => Same(n, force));
                        if (i >= 0) components.Add(i);
                    }
                else
                {
                    var token = entry["component"];
                    int c = token == null ? 0 : token.Type == JTokenType.Integer ? (int)token : Array.FindIndex(names, n => Same(n, (string)token));
                    if (c < 0) { problem = $"\"{token}\" is not a component of {name}"; return null; }
                    // The reaction force or moment drawn as an arrow: its three components.
                    if (family == ResultFamily.Reaction && (c == 0 || c == 4)) components.AddRange(new[] { c + 1, c + 2, c + 3 });
                    else components.Add(c);
                }
                if (components.Count == 0) continue;

                // The unit of each quantity, as the picture has it.
                int UnitIndex(Quantity quantity)
                {
                    string unit = entry["units"] is JObject units ? (string)units[quantity.ToString()] : null;
                    if (unit == null && ResultField.QuantityOf(family, components[0]) == quantity) unit = (string)entry["unit"];
                    var offered = ResultUnits.Of(quantity);
                    int at = unit == null ? -1 : Array.FindIndex(offered, u => Same(u.Symbol, unit) || Same(u.Caption, unit));
                    return at >= 0 ? at : ResultUnits.Default(quantity);
                }

                var fields = family == ResultFamily.BeamForce
                    ? ResultField.BeamForces(model, components, step)
                    : components.Distinct().ToDictionary(c => c, c => ResultField.Read(model, family, c, layer, step, local));

                string note = family == ResultFamily.ShellStress ? $"Through the {Alpaca4d.Result.Read.LayerNames[layer].ToLowerInvariant()} layer."
                    : family == ResultFamily.BrickStress && local ? "In each element's own axes."
                    : family == ResultFamily.Reaction ? "In each support's own axes."
                    : "";

                foreach (int c in components)
                {
                    if (!fields.TryGetValue(c, out var field) || field == null) continue;
                    var quantity = ResultField.QuantityOf(family, c);
                    int unitIndex = UnitIndex(quantity);
                    double factor = ResultUnits.Factor(quantity, unitIndex);
                    var values = new ResultValues
                    {
                        Result = ResultField.FamilyNames[(int)family],
                        Component = names[c],
                        Unit = ResultUnits.At(quantity, unitIndex).Symbol,
                        Note = note
                    };
                    if (field.Reactions != null)
                    {
                        values.At = ResultValues.AtSupport;
                        values.Summed = quantity == Quantity.Force;
                        for (int i = 0; i < field.Reactions.Count; i++)
                        {
                            string id = i < model.Supports.Count ? model.Supports[i].Id?.ToString(CultureInfo.InvariantCulture) ?? (i + 1).ToString(CultureInfo.InvariantCulture) : "";
                            values.Values.Add(new KeyValuePair<string, double>(id, field.Reactions[i].Item3 * factor));
                        }
                    }
                    else if (field.ByNode != null)
                    {
                        values.At = ResultValues.AtNode;
                        foreach (var v in field.ByNode.OrderBy(x => x.Key))
                            values.Values.Add(new KeyValuePair<string, double>(v.Key.ToString(CultureInfo.InvariantCulture), v.Value * factor));
                    }
                    else if (field.ByElement != null)
                    {
                        values.At = ResultValues.AtElement;
                        foreach (var e in field.ByElement.OrderBy(x => x.Key))
                            foreach (var v in e.Value ?? new List<double>())
                                values.Values.Add(new KeyValuePair<string, double>(e.Key.ToString(CultureInfo.InvariantCulture), v * factor));
                    }
                    made.Add(values);
                }
            }
            return made;
        }

        public FigureBlock Draw(Alpaca4d.Model model, JObject figure, out string problem)
        {
            _count++;
            foreach (var property in figure.Properties())
            {
                if (!Settings.Any(s => string.Equals(s, property.Name, StringComparison.OrdinalIgnoreCase)))
                    Remarks.Add($"Figure {_count}: \"{property.Name}\" is not a figure setting, and was left out. The settings are: {string.Join(", ", Settings)}.");
            }

            var view = Solve(model, figure, out problem);
            if (view == null) return null;

            foreach (var message in view.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
                Remarks.Add($"Figure {_count}: {message}");

            var errors = view.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            if (errors.Count > 0) { problem = string.Join(" ", errors); return null; }

            var box = view.ClippingBox;
            if (!box.IsValid) { problem = "Model View drew nothing"; return null; }

            int width = Math.Max(400, Math.Min(4000, Int(figure, "pixels") ?? 2000));
            double aspect = Math.Max(0.2, Math.Min(2.0, Number(figure, "aspect") ?? 0.62));
            int height = (int)Math.Round(width * aspect);

            string path = Path.Combine(_folder, $"figure-{_count:00}.png");
            Size saved;
            try
            {
                _conduit.View = view;
                Aim(box, figure, width, height);
                saved = Capture(path, width, height, Bool(figure, "trim") ?? true);
            }
            finally
            {
                _conduit.View = null;
            }
            Pictures.Add(path);

            return new FigureBlock
            {
                ImagePath = path,
                PixelWidth = saved.Width,
                PixelHeight = saved.Height,
                Legend = (Bool(figure, "legend") ?? true) ? Legend(view) : null
            };
        }

        // ── Model View ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A Model View in memory, its menus set as the figure says and solved on the model: null,
        /// and why, when a setting names something Model View does not have.
        /// </summary>
        private static ModelView Solve(Alpaca4d.Model model, JObject f, out string problem)
        {
            var view = new ModelView();
            view.CreateAttributes();
            problem = ModelViewSettings.Apply(view, model, f);
            if (problem != null) return null;

            // A footfall's response, drawn as the displacement would be: R, or what "quantity" names.
            if (Bool(f, "footfall") == true)
            {
                var footfall = model.Footfall;
                if (footfall == null) { problem = "the model holds no footfall response: give the Report the model Footfall Analysis hands on"; return null; }
                string quantity = Alpaca4d.FootfallResponse.Quantity(Text(f, "quantity"));
                if (quantity == null) { problem = $"footfall has no \"{Text(f, "quantity")}\" to draw: it has {string.Join(", ", Alpaca4d.FootfallResponse.Quantities)}"; return null; }
                var values = footfall.Of(quantity);
                if (values.Length == 0)
                {
                    problem = Alpaca4d.FootfallResponse.IsTransient(quantity) && !footfall.TransientChecked
                        ? $"the transient response was not checked - not for a stair, nor for a floor above the cut-off - so there is no {quantity.ToLowerInvariant()} to draw"
                        : $"the model holds no {quantity.ToLowerInvariant()}: run Footfall Analysis again for it";
                    return null;
                }
                view.NodeValues = new Dictionary<int, double>();
                for (int i = 0; i < footfall.Nodes.Length && i < values.Length; i++)
                    view.NodeValues[footfall.Nodes[i]] = values[i];
                string unit = Alpaca4d.FootfallResponse.UnitOf(quantity);
                view.NodeValuesLabel = unit.Length > 0 ? $"{quantity} [{unit} rms]" : quantity;
            }

            view.ExpireSolution(false);
            view.CollectData();
            view.ComputeData();
            return view;
        }

        /// <summary>The legend of the first result drawn with a gradient, as Model View's Values and Colors give it; null for none.</summary>
        private static FigureLegend Legend(ModelView view)
        {
            var values = view.Params.Output[3].VolatileData;
            var colours = view.Params.Output[4].VolatileData;
            if (values.PathCount == 0 || colours.PathCount == 0) return null;

            var legend = new FigureLegend
            {
                Values = values.get_Branch(0).OfType<GH_Number>().Select(x => x.Value).ToList(),
                Colours = colours.get_Branch(0).OfType<GH_Colour>().Select(x => x.Value).ToList(),
            };
            if (legend.Colours.Count == 0) return null;
            legend.Banded = legend.Values.Count == legend.Colours.Count + 1;

            // "Displacement - Magnitude [mm], step 0, min 0, max 12.3": the part before the step.
            var info = view.Params.Output[2].VolatileData.AllData(true).OfType<GH_String>().Select(x => x.Value).FirstOrDefault() ?? "";
            int cut = info.IndexOf(", step", StringComparison.Ordinal);
            legend.Title = cut > 0 ? info.Substring(0, cut) : info;
            if (view.NodeValues != null) legend.Title = view.NodeValuesLabel;
            return legend;
        }

        // ── The camera ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The camera on the box from the side the figure's "view" names - iso (the south-east
        /// one), southwest, northeast, northwest, top, bottom, front, back, left, right, a direction
        /// as "eye": [x, y, z], or a view saved in Rhino as "named:Name" - with all of the box in
        /// the picture.
        /// </summary>
        private void Aim(BoundingBox box, JObject f, int width, int height)
        {
            var vp = _view.ActiveViewport;
            string name = (Text(f, "view") ?? "iso").Trim();

            if (name.StartsWith("named:", StringComparison.OrdinalIgnoreCase))
            {
                var doc = RhinoDoc.ActiveDoc;
                int index = doc.NamedViews.FindByName(name.Substring(6).Trim());
                if (index >= 0)
                {
                    doc.NamedViews.Restore(index, _view.ActiveViewport);
                    return;
                }
                Remarks.Add($"Figure {_count}: Rhino has no view saved as \"{name.Substring(6).Trim()}\": the iso view it is.");
                name = "iso";
            }

            // A camera given whole - where it is, what it looks at, which way is up, the lens - is
            // used as it is: the view the user set up in Rhino.
            if (Get(f, "camera") is JObject camera && camera["location"] is JArray at && camera["target"] is JArray to)
            {
                Point3d P(JArray xyz) => new Point3d((double)xyz[0], (double)xyz[1], (double)xyz[2]);
                if ((bool?)camera["parallel"] ?? false) vp.ChangeToParallelProjection(true);
                else vp.ChangeToPerspectiveProjection(true, (double?)camera["lens"] ?? 50.0);
                vp.SetCameraLocations(P(to), P(at));
                if (camera["up"] is JArray upward) vp.CameraUp = new Vector3d((double)upward[0], (double)upward[1], (double)upward[2]);
                FitInside(vp, box, width, height);
                if (Number(f, "zoom") is double magnify && magnify > 0.0) vp.Magnify(magnify, false);
                return;
            }

            Vector3d eye, up = Vector3d.ZAxis;
            bool flat = true;
            switch (name.ToLowerInvariant())
            {
                case "top": case "plan": eye = Vector3d.ZAxis; up = Vector3d.YAxis; break;
                case "bottom": eye = -Vector3d.ZAxis; up = Vector3d.YAxis; break;
                case "front": case "south": eye = -Vector3d.YAxis; break;
                case "back": case "north": eye = Vector3d.YAxis; break;
                case "left": case "west": eye = -Vector3d.XAxis; break;
                case "right": case "east": eye = Vector3d.XAxis; break;
                case "southwest": eye = new Vector3d(-1, -1, 0.8); flat = false; break;
                case "northeast": eye = new Vector3d(1, 1, 0.8); flat = false; break;
                case "northwest": eye = new Vector3d(-1, 1, 0.8); flat = false; break;
                case "iso": case "southeast": eye = new Vector3d(1, -1, 0.8); flat = false; break;
                default:
                    Remarks.Add($"Figure {_count}: \"{name}\" is not a view: the iso view it is.");
                    eye = new Vector3d(1, -1, 0.8); flat = false;
                    break;
            }

            if (f["eye"] is JArray e && e.Count == 3)
            {
                eye = new Vector3d((double)e[0], (double)e[1], (double)e[2]);
                flat = false;
            }

            string projection = Text(f, "projection")?.ToLowerInvariant();
            bool parallel = projection != null ? projection == "parallel" : flat;
            if (parallel) vp.ChangeToParallelProjection(true);
            else vp.ChangeToPerspectiveProjection(true, 50.0);

            eye.Unitize();
            // Nearly straight down a view's own up, the up would be lost: Y instead.
            if (Math.Abs(eye * up) > 0.99) up = Vector3d.YAxis;

            var centre = box.Center;
            vp.SetCameraLocations(centre, centre + eye * box.Diagonal.Length * 3.0);
            vp.CameraUp = up;
            vp.ZoomBoundingBox(box);
            FitInside(vp, box, width, height);

            if (Number(f, "zoom") is double zoom && zoom > 0.0)
                vp.Magnify(zoom, false);
        }

        /// <summary>
        /// What a capture of width by height shows of the viewport, in its screen coordinates: the
        /// viewport widened, or made taller, to the capture's shape about its middle.
        /// </summary>
        private static RectangleF Captured(RhinoViewport vp, int width, int height)
        {
            double w = vp.Size.Width, h = vp.Size.Height, shape = (double)width / height;
            double cw = w / h < shape ? h * shape : w, ch = w / h < shape ? h : w / shape;
            return new RectangleF((float)((w - cw) / 2), (float)((h - ch) / 2), (float)cw, (float)ch);
        }

        /// <summary>Zoomed out until every corner of the box is in the picture, with a margin all round.</summary>
        private static void FitInside(RhinoViewport vp, BoundingBox box, int width, int height)
        {
            var field = Captured(vp, width, height);
            for (int i = 0; i < 40; i++)
            {
                var toScreen = vp.GetTransform(Rhino.DocObjects.CoordinateSystem.World, Rhino.DocObjects.CoordinateSystem.Screen);
                bool inside = box.GetCorners().All(c =>
                {
                    var p = c;
                    p.Transform(toScreen);
                    return p.X >= field.Left + 0.05 * field.Width && p.X <= field.Right - 0.05 * field.Width &&
                           p.Y >= field.Top + 0.05 * field.Height && p.Y <= field.Bottom - 0.05 * field.Height;
                });
                if (inside) return;
                vp.Magnify(0.94, false);
            }
        }

        /// <summary>
        /// The view captured on a transparent background and laid on white, as a page is, and with
        /// <paramref name="trim"/> cut down to what was drawn and a margin round it, so that the
        /// structure fills its figure whatever shape it is. The very first capture of a view can come
        /// before Rhino has caught up with it, so it is made twice. Returns the size saved.
        /// </summary>
        private Size Capture(string path, int width, int height, bool trim)
        {
            var capture = new ViewCapture
            {
                Width = width,
                Height = height,
                ScaleScreenItems = true,
                DrawAxes = false,
                DrawGrid = false,
                DrawGridAxes = false,
                TransparentBackground = true,
            };

            if (!_primed)
            {
                capture.CaptureToBitmap(_view)?.Dispose();
                _primed = true;
            }

            using (var captured = capture.CaptureToBitmap(_view))
            {
                if (captured == null) throw new InvalidOperationException("Rhino could not capture the view");
                using (var white = new Bitmap(captured.Width, captured.Height, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(white))
                    {
                        g.Clear(Color.White);
                        g.DrawImage(captured, 0, 0, captured.Width, captured.Height);
                    }

                    var keep = trim ? Drawn(white) : new Rectangle(System.Drawing.Point.Empty, white.Size);
                    if (keep.Size == white.Size)
                    {
                        white.Save(path, ImageFormat.Png);
                        return white.Size;
                    }

                    using (var cut = white.Clone(keep, PixelFormat.Format24bppRgb))
                        cut.Save(path, ImageFormat.Png);
                    return keep.Size;
                }
            }
        }

        /// <summary>
        /// The part of a picture on white that anything was drawn in, with a margin of a few percent
        /// round it; the whole picture if nothing was, or if its pixels cannot be read.
        /// </summary>
        private static Rectangle Drawn(Bitmap bitmap)
        {
            var whole = new Rectangle(System.Drawing.Point.Empty, bitmap.Size);
            try
            {
                var data = bitmap.LockBits(whole, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                int left = whole.Width, right = -1, top = whole.Height, bottom = -1;
                try
                {
                    var bytes = new byte[data.Stride * whole.Height];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                    for (int y = 0; y < whole.Height; y++)
                    {
                        int row = y * data.Stride;
                        for (int x = 0; x < whole.Width; x++)
                        {
                            int i = row + 3 * x;
                            if (bytes[i] > 244 && bytes[i + 1] > 244 && bytes[i + 2] > 244) continue;
                            if (x < left) left = x;
                            if (x > right) right = x;
                            if (y < top) top = y;
                            if (y > bottom) bottom = y;
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                if (right < 0) return whole;
                int pad = 8 + (int)(0.04 * Math.Max(right - left, bottom - top));
                return Rectangle.FromLTRB(Math.Max(0, left - pad), Math.Max(0, top - pad),
                                          Math.Min(whole.Width, right + pad + 1), Math.Min(whole.Height, bottom + pad + 1));
            }
            catch
            {
                return whole;
            }
        }

        // ── Drawing ────────────────────────────────────────────────────────────────────────────

        /// <summary>Draws the Model View it is given, and nothing of Rhino's own, while it has one.</summary>
        private sealed class Conduit : DisplayConduit
        {
            public ModelView View;

            protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
            {
                if (View != null) e.IncludeBoundingBox(View.ClippingBox);
            }

            protected override void ObjectCulling(CullObjectEventArgs e)
            {
                if (View != null) e.CullObject = true;
            }

            protected override void PostDrawObjects(DrawEventArgs e)
            {
                if (View == null) return;
                var args = new PreviewArgs(e.Display, e.Viewport);
                View.DrawViewportMeshes(args);
                View.DrawViewportWires(args);
            }
        }

        /// <summary>What Grasshopper hands a component to draw with, made up for a component it does not know of.</summary>
        private sealed class PreviewArgs : IGH_PreviewArgs
        {
            public PreviewArgs(DisplayPipeline display, RhinoViewport viewport)
            {
                Display = display;
                Viewport = viewport;
            }

            public GH_Document Document => null;
            public DisplayPipeline Display { get; }
            public RhinoViewport Viewport { get; }
            public int DefaultCurveThickness => 1;
            public Color WireColour => Color.FromArgb(150, 0, 0);
            public Color WireColour_Selected => Color.FromArgb(0, 150, 0);
            public DisplayMaterial ShadeMaterial { get; } = new DisplayMaterial(Color.FromArgb(150, 0, 0), 0.5);
            public DisplayMaterial ShadeMaterial_Selected { get; } = new DisplayMaterial(Color.FromArgb(0, 150, 0), 0.5);
            public MeshingParameters MeshingParameters => MeshingParameters.Default;
        }
    }
}
