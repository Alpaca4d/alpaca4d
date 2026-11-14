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
    // every station that has yielded by the step drawn, riding on the deformed shape, coloured by the
    // ductility it has reached - yellow from first yield, orange from twice the yield strain, red from four
    // times, dark red from eight - or purple once it has failed. A ball stays once it has come: a fibre that
    // yielded keeps the strain it took. It is drawn solid while the station is past its yield strain at the
    // step drawn, and faded once it has unloaded. What a pushover shows as it goes on: where the frame has
    // yielded, in what order, and how far. See Alpaca4d.Result.PlasticHinges.
    public partial class ModelView
    {
        private List<HingeStation> _hinges;

        /// <summary>Every step of the analysis, read once a recorder file: stepping through a push - and animating, twenty times a second - reads nothing again.</summary>
        private HingeHistory _hingeHistory;

        /// <summary>The recorder file and when it was written, which the history was read from.</summary>
        private string _hingeKey;

        /// <summary>The balls, for the Hinge size and the shape they were made on; null to make them again.</summary>
        private List<(Mesh Ball, DisplayMaterial Material)> _hingeBalls;
        private double _hingeBallsSize = double.NaN;

        // Made when first drawn, not with the class: a DisplayMaterial is Rhino's own, and Model View is
        // also made where Rhino is not running - by Tools/Examples/make.sh, and the docs' pictures.
        private static readonly Dictionary<(int Band, bool Faded), DisplayMaterial> _hingeMaterials = new Dictionary<(int, bool), DisplayMaterial>();

        /// <summary>A ductility band's ball, 0 to 3, or -1 for failed; faded for a station that has unloaded.</summary>
        private static DisplayMaterial HingeMaterial(int band, bool faded)
        {
            if (!_hingeMaterials.TryGetValue((band, faded), out var material))
            {
                var colour = band < 0 ? PlasticHinges.FailedColour : PlasticHinges.BandColours[band];
                material = Glowing(colour);
                if (faded) material.Transparency = 0.65;
                _hingeMaterials[(band, faded)] = material;
            }
            return material;
        }

        /// <summary>A third of its colour given off as well as reflected: the side of a ball away from the light still reads in its colour.</summary>
        private static DisplayMaterial Glowing(Color colour) =>
            new DisplayMaterial(colour) { Emission = Color.FromArgb(colour.R / 3, colour.G / 3, colour.B / 3), Shine = 0.3 };

        /// <summary>The hinges at <paramref name="step"/>, when Plastic hinges is ticked and the model has them to show.</summary>
        private void SolveHinges(int step)
        {
            _hingeBalls = null;
            if (!(_ckHinges?.Active ?? false) || _model == null)
            {
                _hinges = null;
                _hingeHistory = null;
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
            string key = $"{file}|{(System.IO.File.Exists(file) ? System.IO.File.GetLastWriteTimeUtc(file).Ticks : 0)}";
            try
            {
                if (key != _hingeKey || _hingeHistory == null)
                {
                    _hingeHistory = PlasticHinges.Track(_model);
                    _hingeKey = key;
                }
                _hinges = _hingeHistory.At(step);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Plastic hinges: " + ex.Message);
                _hinges = null;
                _hingeHistory = null;
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
        /// A ball for each station that has yielded by the step drawn, on the shape drawn, in the colour of the
        /// ductility it has reached - faded once it has unloaded - or purple once it has failed. One at an end of a beam is
        /// moved in along it by a ball and a half, so that the hinges at the two ends of a joint - a column's
        /// top and a beam's end - stand apart rather than in one another.
        /// </summary>
        private List<(Mesh, DisplayMaterial)> HingeBalls(double size)
        {
            var balls = new List<(Mesh, DisplayMaterial)>();
            foreach (var hinge in _hinges.Where(h => h.Reached != HingeState.Elastic))
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
                balls.Add((ball, hinge.Reached == HingeState.Ultimate
                    ? HingeMaterial(-1, false)
                    : HingeMaterial(PlasticHinges.Band(hinge.Ductility), hinge.Unloaded)));
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
