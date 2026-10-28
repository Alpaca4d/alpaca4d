using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// What a domain view draws, worked out once per solve: the domain's own curves and mesh, the
    /// axes with their ticks and labels, and the design forces of a Domain Check with the points
    /// they meet the boundary at.
    ///
    /// The geometry goes out of the component as well and bakes; the labels are drawn in pixels, so
    /// they read the same at any zoom and in any unit, and do not.
    /// </summary>
    internal sealed class DomainPlot
    {
        public static readonly Color DomainColor = Color.FromArgb(47, 111, 219);
        public static readonly Color MeshColor = Color.FromArgb(160, 190, 240);
        public static readonly Color AxisColor = Color.FromArgb(110, 119, 129);
        public static readonly Color TextColor = Color.FromArgb(31, 35, 40);
        public static readonly Color DemandColor = Color.Black;
        public static readonly Color CapacityColor = Color.FromArgb(209, 36, 47);

        private const int LabelHeight = 11;
        private const int TitleHeight = 12;

        public readonly List<Polyline> Curves = new List<Polyline>();
        public readonly List<Polyline> FaintCurves = new List<Polyline>();
        public readonly List<Line> Axes = new List<Line>();
        public readonly List<Line> Ticks = new List<Line>();
        public readonly List<(string Text, Point3d At, bool Title)> Labels = new List<(string, Point3d, bool)>();
        public readonly List<Point3d> Demands = new List<Point3d>();
        public readonly List<Point3d> Capacities = new List<Point3d>();
        public readonly List<Line> CheckLines = new List<Line>();
        public readonly List<(Point3d At, Color Colour)> Marks = new List<(Point3d, Color)>();
        public Mesh Mesh;

        public BoundingBox Box
        {
            get
            {
                var box = BoundingBox.Empty;
                foreach (var curve in Curves.Concat(FaintCurves))
                    box.Union(curve.BoundingBox);
                foreach (var line in Axes.Concat(Ticks).Concat(CheckLines))
                    box.Union(line.BoundingBox);
                foreach (var label in Labels)
                    box.Union(label.At);
                foreach (var point in Demands.Concat(Capacities).Concat(Marks.Select(m => m.At)))
                    box.Union(point);
                if (Mesh != null)
                    box.Union(Mesh.GetBoundingBox(false));
                return box;
            }
        }

        public bool IsEmpty => Curves.Count == 0 && Mesh == null;

        public void Clear()
        {
            Curves.Clear();
            FaintCurves.Clear();
            Axes.Clear();
            Ticks.Clear();
            Labels.Clear();
            Demands.Clear();
            Capacities.Clear();
            CheckLines.Clear();
            Marks.Clear();
            Mesh = null;
        }

        // ---------------------------------------------------------------- building

        /// <summary>
        /// An axis from <paramref name="from"/> to <paramref name="to"/> in plot values, through
        /// <paramref name="at"/>, ticked at round values with their labels on the
        /// <paramref name="side"/> of it, and its title past the far end.
        /// </summary>
        /// <param name="at">Where a value of the axis is drawn.</param>
        /// <param name="side">The way ticks and labels stand off the axis, a unit vector.</param>
        /// <param name="tick">How long a tick is, in model units.</param>
        /// <param name="skipZero">Leave the label at zero to another axis that crosses it there.</param>
        /// <param name="shown">The number a label shows for a value of the axis: N, for an axis drawn as -N.</param>
        public void Axis(double from, double to, Func<double, Point3d> at, Vector3d side, double tick, string title,
                         bool skipZero = false, Func<double, double> shown = null)
        {
            if (!(to > from))
                return;

            Axes.Add(new Line(at(from), at(to)));

            double step = NiceStep(to - from);
            for (double value = Math.Ceiling(from / step) * step; value <= to + 1e-9 * step; value += step)
            {
                var p = at(value);
                Ticks.Add(new Line(p, p + side * tick));
                if (skipZero && Math.Abs(value) < 1e-9 * step)
                    continue;
                Labels.Add((Number(shown == null ? value : shown(value), step), p + side * (3.0 * tick), false));
            }

            var end = at(to);
            var direction = at(to) - at(from);
            direction.Unitize();
            Labels.Add((title, end + direction * (4.0 * tick), true));
        }

        /// <summary>A round step that splits <paramref name="range"/> into about five: 1, 2 or 5 times a power of ten.</summary>
        public static double NiceStep(double range)
        {
            if (!(range > 0.0))
                return 1.0;
            double raw = range / 5.0;
            double magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(raw)));
            double norm = raw / magnitude;
            double nice = norm < 1.5 ? 1.0 : norm < 3.0 ? 2.0 : norm < 7.0 ? 5.0 : 10.0;
            return nice * magnitude;
        }

        /// <summary>A tick label, with as many decimals as the step needs.</summary>
        public static string Number(double value, double step)
        {
            int decimals = Math.Max(0, -(int)Math.Floor(Math.Log10(step) + 1e-9));
            if (Math.Abs(value) < 0.5 * Math.Pow(10.0, -decimals))
                value = 0.0;
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        /// <summary>A design force and where it meets the boundary, with the line between them.</summary>
        public void Check(Point3d demand, Point3d capacity, bool hasCapacity)
        {
            Demands.Add(demand);
            if (!hasCapacity)
                return;
            Capacities.Add(capacity);
            CheckLines.Add(new Line(demand, capacity));
        }

        // ---------------------------------------------------------------- drawing

        public void DrawMeshes(IGH_PreviewArgs args, bool selected)
        {
            if (Mesh == null)
                return;
            var material = new DisplayMaterial(selected ? args.ShadeMaterial_Selected.Diffuse : MeshColor, 0.55);
            args.Display.DrawMeshShaded(Mesh, material);
        }

        public void DrawWires(IGH_PreviewArgs args, bool selected)
        {
            var domain = selected ? args.WireColour_Selected : DomainColor;

            foreach (var curve in FaintCurves)
                args.Display.DrawPolyline(curve, Color.FromArgb(120, domain), 1);
            foreach (var curve in Curves)
                args.Display.DrawPolyline(curve, domain, 2);

            foreach (var line in Axes)
                args.Display.DrawLine(line, AxisColor, 1);
            foreach (var line in Ticks)
                args.Display.DrawLine(line, AxisColor, 1);

            foreach (var label in Labels)
                args.Display.Draw2dText(label.Text, TextColor, label.At, true, label.Title ? TitleHeight : LabelHeight);

            foreach (var line in CheckLines)
                args.Display.DrawLine(line, CapacityColor, 1);
            foreach (var point in Demands)
                args.Display.DrawPoint(point, PointStyle.RoundSimple, 5, DemandColor);
            foreach (var point in Capacities)
                args.Display.DrawPoint(point, PointStyle.RoundSimple, 5, CapacityColor);
            foreach (var mark in Marks)
                args.Display.DrawPoint(mark.At, PointStyle.RoundSimple, 5, mark.Colour);
        }

        /// <summary>The domain checks wired to a view, read off a list of anything.</summary>
        public static List<Alpaca4d.Section.DomainCheck> ChecksFrom(IEnumerable<object> items)
        {
            var checks = new List<Alpaca4d.Section.DomainCheck>();
            foreach (var item in items)
            {
                object value = item is Grasshopper.Kernel.Types.IGH_Goo goo ? goo.ScriptVariable() : item;
                if (value is Alpaca4d.Section.DomainCheck check)
                    checks.Add(check);
            }
            return checks;
        }
    }
}
