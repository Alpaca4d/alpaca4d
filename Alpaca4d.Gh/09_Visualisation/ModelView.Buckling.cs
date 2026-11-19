using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

using Alpaca4d.Design;
using Alpaca4d.Generic;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    // The Buckling menu: the buckling lengths the steel check works out from the model where the user
    // gives none (Alpaca4d.Design.BucklingLengths), drawn on it. Each beam coloured by its length about
    // the axis picked, a mark where each member is held that way, and each piece between marks labelled
    // with the length it gives. With a result ticked the result colours the model, and the marks and
    // labels are drawn over it.
    public partial class ModelView
    {
        internal MenuCheckBox _ckBuckling;
        internal MenuDropDown _ddBuckling;
        private MenuStaticText _txBuckling;

        /// <summary>The lengths of the model drawn, while the Buckling menu has them on; null otherwise.</summary>
        private BucklingLengths _buckling;

        /// <summary>Each beam's colour by its length, in the order of Model.Beams, while the model is drawn coloured by them.</summary>
        private Color[] _bucklingColours;

        private readonly List<Tuple<string, Point3d>> _bucklingLabels = new List<Tuple<string, Point3d>>();
        private readonly List<Tuple<Line, Color>> _bucklingMarks = new List<Tuple<Line, Color>>();
        private readonly List<Tuple<Point3d, Color>> _bucklingPoints = new List<Tuple<Point3d, Color>>();

        /// <summary>The colour of a mark the structure makes - a support, bracing - and of one a member carrying the beam makes.</summary>
        private static readonly Color HeldColour = Alpaca4d.Colors.AlpacaOrange;
        private static readonly Color CarriedColour = Alpaca4d.Colors.AlpacaPurple;

        private static readonly string[] BucklingAxisNames = { "About y", "About z", "Lateral-torsional" };

        private void BucklingMenu(GH_ExtendableComponentAttributes attr)
        {
            var menu = new GH_ExtendableMenu(4, "Buckling") { Name = "Buckling", Header = "Buckling lengths worked out from the model" };
            var panel = new MenuPanel(4, "buckling_panel");

            _ckBuckling = new MenuCheckBox(0, "ShowBuckling", "Buckling lengths");
            // In the order of BucklingAxis, which the saved index is read back against.
            _ddBuckling = new MenuDropDown(0, "BucklingAxis", "BucklingAxis") { VisibleItemCount = 3 };
            foreach (var name in BucklingAxisNames)
                _ddBuckling.AddItem(name, name);
            _txBuckling = Caption("Length");

            panel.AddControl(_ckBuckling);
            panel.AddControl(_txBuckling);
            panel.AddControl(_ddBuckling);
            menu.AddControl(panel);
            attr.AddMenu(menu);
        }

        private bool BucklingShown => _ckBuckling?.Active ?? false;

        private BucklingAxis BucklingAxisPicked =>
            (BucklingAxis)Math.Max(0, Math.Min(2, _ddBuckling?.Value ?? 0));

        private string BucklingName
        {
            get
            {
                switch (BucklingAxisPicked)
                {
                    case BucklingAxis.Y: return "Buckling length about y";
                    case BucklingAxis.Z: return "Buckling length about z";
                    default: return "Lateral-torsional buckling length";
                }
            }
        }

        /// <summary>For scripts that build definitions: the lengths drawn about <paramref name="axis"/>, 0 to 2, or none for -1.</summary>
        internal void ShowBuckling(int axis)
        {
            if (_ckBuckling == null) return;
            _ckBuckling.Active = axis >= 0;
            if (axis >= 0) _ddBuckling.Value = Math.Min(2, axis);
            UpdateVisibility();
        }

        private void ClearBuckling()
        {
            _buckling = null;
            _bucklingColours = null;
            _bucklingLabels.Clear();
            _bucklingMarks.Clear();
            _bucklingPoints.Clear();
        }

        /// <summary>
        /// The lengths of the model, and what is drawn of them: the marks and labels always, and with no
        /// result to colour the model, the beams coloured by their lengths and the scale for Legend.
        /// </summary>
        private void SolveBuckling(IGH_DataAccess DA, bool colour)
        {
            ClearBuckling();
            if (!BucklingShown || _model == null || _model.Beams.Count == 0) return;

            try
            {
                _buckling = BucklingLengths.Of(_model);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The buckling lengths could not be worked out: {ex.Message}");
                return;
            }

            if (_buckling.Approximate)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "The model is too large to find its bracing in: each member is drawn with its own length.");
            if (_model.Shells.Count > 0 || _model.Bricks.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "No slab or wall counts as a restraint in these lengths: give Utilisation those they shorten.");

            var axis = BucklingAxisPicked;
            Marks(axis);

            if (!colour) return;

            var shown = Enumerable.Range(0, _model.Beams.Count)
                                  .Where(i => Shown(_model.Beams[i]) && _buckling.Of(i, axis) > 0.0)
                                  .ToList();

            var palette = new List<Color>();
            if (!DA.GetDataList(3, palette) || palette.Count < 2)
                palette = Alpaca4d.Colors.Gradient(0);

            var ranges = new List<Interval>();
            DA.GetDataList(4, ranges);
            double min, max;
            bool ranged = ranges.Count > 0 && ranges[0].IsValid;
            if (ranged)
            {
                min = ranges[0].Min;
                max = ranges[0].Max;
            }
            else
            {
                min = shown.Count > 0 ? shown.Min(i => _buckling.Of(i, axis)) : 0.0;
                max = shown.Count > 0 ? shown.Max(i => _buckling.Of(i, axis)) : 0.0;
            }

            var gradient = ColourScale.Stops(palette, min, max);
            _bucklingColours = new Color[_model.Beams.Count];
            for (int i = 0; i < _model.Beams.Count; i++)
            {
                double length = _buckling.Of(i, axis);
                _bucklingColours[i] = length > 0.0 ? Alpaca4d.Colors.GetColor(length, gradient) : Color.FromArgb(210, 210, 210);
            }

            DA.SetData(2, $"{BucklingName} [{Units.Length}], min {min:G4}, max {max:G4} - worked out from the model");

            var scales = new GH_Structure<GH_Number>();
            var colours = new GH_Structure<GH_Colour>();
            var path = DA.ParameterTargetPath(3).AppendElement(0);
            scales.EnsurePath(path);
            if (ranged || shown.Count > 0)
                scales.AppendRange(ColourScale.LabelValues(min, max, palette.Count).Select(x => new GH_Number(x)), path);
            colours.AppendRange(palette.Select(x => new GH_Colour(x)), path);
            DA.SetDataTree(3, scales);
            DA.SetDataTree(4, colours);
        }

        /// <summary>
        /// A mark across each member where it is held along the axis picked - a short line along the
        /// direction it is held in, orange where the structure holds it, purple where a member it sits on
        /// carries it - and a label at the middle of each piece between marks.
        /// </summary>
        private void Marks(BucklingAxis axis)
        {
            var lengths = _buckling.Members.Select(m => m.Length).Where(x => x > 0.0).OrderBy(x => x).ToList();
            if (lengths.Count == 0) return;
            double reach = lengths[lengths.Count / 2] * 0.04;

            foreach (var member in _buckling.Members)
            {
                if (!member.Beams.Any(i => Shown(_model.Beams[i]))) continue;

                var held = member.Held(axis);
                var directions = member.Direction(axis);
                for (int k = 0; k < member.Points.Count; k++)
                {
                    if (held[k] == HeldBy.None) continue;
                    var colour = held[k] == HeldBy.Member ? CarriedColour : HeldColour;
                    var d = directions[k] * reach;
                    _bucklingMarks.Add(Tuple.Create(new Line(member.Points[k] - d, member.Points[k] + d), colour));
                    _bucklingPoints.Add(Tuple.Create(member.Points[k], colour));
                }

                foreach (var segment in member.Segments(axis))
                {
                    // The length of the beams on it, read back per beam: a beam turned about its axis
                    // takes the other set of restraints, and its label should say what it was given.
                    int beam = member.Beams[Math.Min(segment.From, member.Beams.Count - 1)];
                    if (!Shown(_model.Beams[beam])) continue;
                    double value = _buckling.Of(beam, axis);
                    double piece = member.Stations[segment.To] - member.Stations[segment.From];

                    string text = Length(value);
                    if (segment.Kind == SegmentKind.Free || segment.Kind == SegmentKind.Sway)
                        text += $" = 2 × {Length(piece)}";

                    _bucklingLabels.Add(Tuple.Create(text, Along(member, (member.Stations[segment.From] + member.Stations[segment.To]) / 2)));
                }
            }
        }

        private static string Length(double value) => value.ToString(value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##", CultureInfo.InvariantCulture);

        /// <summary>The point <paramref name="station"/> along a member.</summary>
        private static Point3d Along(BucklingLengths.Member member, double station)
        {
            var s = member.Stations;
            for (int k = 0; k + 1 < s.Count; k++)
            {
                if (station > s[k + 1] && k + 2 < s.Count) continue;
                double t = s[k + 1] > s[k] ? (station - s[k]) / (s[k + 1] - s[k]) : 0.0;
                return member.Points[k] + (member.Points[k + 1] - member.Points[k]) * Math.Max(0.0, Math.Min(1.0, t));
            }
            return member.Points[0];
        }

        private void DrawBuckling(Grasshopper.Kernel.IGH_PreviewArgs args, double textSize)
        {
            if (_buckling == null) return;

            args.Display.PushDepthTesting(false);
            foreach (var mark in _bucklingMarks)
                args.Display.DrawLine(mark.Item1, mark.Item2, 4);
            foreach (var point in _bucklingPoints)
                args.Display.DrawPoint(point.Item1, Rhino.Display.PointStyle.RoundSimple, 6, point.Item2);
            args.Display.PopDepthTesting();

            DrawLabels(args, _bucklingLabels, Alpaca4d.Colors.DarkTech, textSize);
        }
    }
}
