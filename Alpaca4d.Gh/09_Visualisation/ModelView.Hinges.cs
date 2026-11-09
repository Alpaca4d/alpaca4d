using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    // The plastic hinges of beams on fibre sections, ticked Plastic hinges in the Results menu: a ball at
    // every station whose fibres are past their yield strain - orange - or past the strain they fail
    // at - purple - at the step drawn, riding on the deformed shape. What a pushover shows as it goes on:
    // where the frame has yielded, and in what order. See Alpaca4d.Result.PlasticHinges.
    public partial class ModelView
    {
        private List<HingeStation> _hinges;

        /// <summary>The recorder file, when it was written, and the step the hinges were read for: animating re-solves twenty times a second.</summary>
        private string _hingeKey;

        /// <summary>The balls, for the Hinge size and the shape they were made on; null to make them again.</summary>
        private List<(Mesh Ball, DisplayMaterial Material)> _hingeBalls;
        private double _hingeBallsSize = double.NaN;

        // Made when first drawn, not with the class: a DisplayMaterial is Rhino's own, and Model View is
        // also made where Rhino is not running - by Tools/Examples/make.sh, and the docs' pictures.
        private static DisplayMaterial _yielded, _failed;
        private static DisplayMaterial YieldedMaterial => _yielded ?? (_yielded = Glowing(Alpaca4d.Colors.AlpacaOrange));
        private static DisplayMaterial FailedMaterial => _failed ?? (_failed = Glowing(Alpaca4d.Colors.AlpacaPurple));

        /// <summary>A third of its colour given off as well as reflected: the side of a ball away from the light still reads orange, or purple.</summary>
        private static DisplayMaterial Glowing(Color colour) =>
            new DisplayMaterial(colour) { Emission = Color.FromArgb(colour.R / 3, colour.G / 3, colour.B / 3), Shine = 0.3 };

        /// <summary>The hinges at <paramref name="step"/>, when Plastic hinges is ticked and the model has them to show.</summary>
        private void SolveHinges(int step)
        {
            _hingeBalls = null;
            if (!(_ckHinges?.Active ?? false) || _model == null)
            {
                _hinges = null;
                _hingeKey = null;
                return;
            }

            if (!PlasticHinges.HasFibreBeams(_model))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Plastic hinges are drawn on beams on fibre sections, and this model has none: an elastic section does not yield.");
                _hinges = null;
                return;
            }
            if (!_model.IsAnalysed || _model.Recorders == null || _model.Recorders.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Plastic hinges need an analysed model: from Pushover Analysis, or Run Analysis.");
                _hinges = null;
                return;
            }

            string file = System.IO.Path.GetFullPath(_model.Recorders.First().FileName);
            string key = $"{file}|{(System.IO.File.Exists(file) ? System.IO.File.GetLastWriteTimeUtc(file).Ticks : 0)}|{step}";
            if (key == _hingeKey)
                return;

            try
            {
                _hinges = PlasticHinges.At(_model, step);
                _hingeKey = key;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Plastic hinges: " + ex.Message);
                _hinges = null;
                _hingeKey = null;
            }
        }

        /// <summary>
        /// The hinges as balls, over the model: drawn last of the shaded meshes, and not hidden by them.
        /// Drawn in the wire pass, after the release circles, they came out hidden by the members they
        /// stood in, and a column's in the member's own grey.
        /// </summary>
        private void DrawHinges(IGH_PreviewArgs args)
        {
            if (_hinges == null) return;

            double size = _slHingeSize?.Value ?? 1.0;
            if (_hingeBalls == null || _hingeBallsSize != size)
            {
                _hingeBalls = HingeBalls(size);
                _hingeBallsSize = size;
            }

            args.Display.PushDepthTesting(false);
            foreach (var (ball, material) in _hingeBalls)
                args.Display.DrawMeshShaded(ball, material);
            args.Display.PopDepthTesting();
        }

        /// <summary>
        /// A ball for each station past its yield strain, on the shape drawn. One at an end of a beam is
        /// moved in along it by a ball and a half, so that the hinges at the two ends of a joint - a column's
        /// top and a beam's end - stand apart rather than in one another.
        /// </summary>
        private List<(Mesh, DisplayMaterial)> HingeBalls(double size)
        {
            var balls = new List<(Mesh, DisplayMaterial)>();
            foreach (var hinge in _hinges.Where(h => h.State != HingeState.Elastic))
            {
                var beam = _model.Beams[hinge.Beam];
                if (!Shown(beam)) continue;

                var start = beam.Curve.PointAtStart;
                var end = beam.Curve.PointAtEnd;
                double length = start.DistanceTo(end);
                double radius = size * HingeRadius(beam, length);

                double t = hinge.Along;
                if (length > 0.0)
                {
                    double inset = Math.Min(0.25, 1.6 * radius / length);
                    t = Math.Max(inset, Math.Min(1.0 - inset, t));
                }

                var at = start + (end - start) * t + Moved(beam, t);
                var ball = Mesh.CreateFromSphere(new Sphere(at, radius), 20, 14);
                balls.Add((ball, hinge.State == HingeState.Ultimate ? FailedMaterial : YieldedMaterial));
            }
            return balls;
        }

        /// <summary>A little over half the section's larger side, so that a ball stands out of its member; a thirtieth of the beam with no outline to measure.</summary>
        private static double HingeRadius(IBeam beam, double length)
        {
            var curves = beam.Section?.Curves;
            if (curves != null && curves.Count > 0)
            {
                var box = BoundingBox.Empty;
                foreach (var curve in curves)
                    if (curve != null) box.Union(curve.GetBoundingBox(false));
                if (box.IsValid)
                {
                    double side = Math.Max(box.Max.X - box.Min.X, box.Max.Y - box.Min.Y);
                    if (side > 0.0) return 0.55 * side;
                }
            }
            return length > 0.0 ? length / 30.0 : 0.1;
        }

        /// <summary>How far the point <paramref name="t"/> along a beam moved on the shape drawn: the beam is drawn straight between its ends.</summary>
        private Vector3d Moved(IBeam beam, double t)
        {
            if (_drawnDisplacement == null) return Vector3d.Zero;
            Vector3d Node(int? tag) => tag.HasValue && _drawnDisplacement.TryGetValue(tag.Value, out var offset) ? offset : Vector3d.Zero;
            return (1.0 - t) * Node(beam.INode) + t * Node(beam.JNode);
        }
    }
}
