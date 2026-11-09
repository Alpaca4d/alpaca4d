// The bricks, the fresh concrete and the joint band of a 3D print, for PrintingCheck.cs: the C# the
// printed cylinder example's three GhPython components (Tools/Examples/PrintedCylinderLayers.py,
// PrintingConcrete.py, PrintingJoint.py) were written from, kept here to drive the
// buckling checks. They are not part of the plugin.
using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;
using Alpaca4d;
using Alpaca4d.Material;

namespace Alpaca4d.Printing
{
    /// <summary>
    /// A 3D printed object as bricks, printed so far at a given time: each layer a filament swept
    /// along its print path, its cross-section an oval - flat where it rests on the layer below and
    /// carries the one above, round at the sides where the nozzle let it bulge - cut into bricks
    /// across, up and along the path.
    ///
    /// The nozzle runs every path in turn at a constant speed, so a brick is printed once the nozzle
    /// has passed its far end, and its age is the time since the nozzle passed its middle. That is
    /// what a growing structure of fresh concrete is checked with: the concrete stiffens with age
    /// (<see cref="Material.FreshConcrete"/>), so the bottom of a wall is older and stiffer than its
    /// top, and moving the time on prints more of it.
    ///
    /// Between two layers there can be a joint band, a thin row of bricks at the bottom of every
    /// layer above the first, given a material of its own by <see cref="Material.SurfaceConnection"/>
    /// to model a joint that slips more than the concrete shears.
    ///
    /// Plain point arithmetic, so that it runs without Rhino: the paths come in already divided.
    /// </summary>
    public sealed class PrintedLayers
    {
        /// <summary>One layer's print path, divided into the segments its bricks run along, in the order it is printed.</summary>
        public sealed class Path
        {
            public List<Point3d> Points { get; }
            public bool Closed { get; }

            public Path(IEnumerable<Point3d> points, bool closed)
            {
                this.Points = points.ToList();
                this.Closed = closed;
                if (this.Points.Count < (closed ? 3 : 2))
                    throw new ArgumentException(closed ? "A closed print path needs at least three points." : "A print path needs at least two points.");
            }

            public int Segments => this.Closed ? this.Points.Count : this.Points.Count - 1;

            public Point3d Start(int segment) => this.Points[segment];

            public Point3d End(int segment) => this.Points[(segment + 1) % this.Points.Count];

            public double Length => Enumerable.Range(0, this.Segments).Sum(s => this.Start(s).DistanceTo(this.End(s)));
        }

        public sealed class Brick
        {
            /// <summary>
            /// Its eight corners in OpenSees' order: the four at its bottom, then the four at its top, each
            /// going start inner, end inner, end outer, start outer - "outer" being to the left of the
            /// direction of print, so that they turn counter-clockwise seen from above.
            /// </summary>
            public Point3d[] Corners;

            /// <summary>From 0, the first layer printed.</summary>
            public int Layer;

            /// <summary>From 0, along the layer's path.</summary>
            public int Segment;

            /// <summary>Minutes since the nozzle passed its middle.</summary>
            public double Age;

            /// <summary>Whether it belongs to the joint band at the bottom of its layer.</summary>
            public bool Joint;
        }

        public sealed class Result
        {
            public List<Brick> Bricks { get; } = new List<Brick>();

            /// <summary>The corners of the first layer that rest on the print bed, each once.</summary>
            public List<Point3d> Base { get; } = new List<Point3d>();

            /// <summary>Where the nozzle is, at the top of the layer it is printing.</summary>
            public Point3d Nozzle;

            /// <summary>Layers printed, the one being printed counted by how far along it is.</summary>
            public double Layers;

            /// <summary>Minutes the whole print takes.</summary>
            public double Duration;
        }

        /// <summary>The width of the filament [length].</summary>
        public double Width { get; }

        /// <summary>The height of a layer [length].</summary>
        public double Height { get; }

        /// <summary>Bricks across the filament.</summary>
        public int Across { get; }

        /// <summary>Bricks up a layer, the joint band not counted.</summary>
        public int Up { get; }

        /// <summary>
        /// How far the round sides reach in, as a share of half the layer height: 0 for square sides,
        /// 1 for a half circle. The flat top and bottom, where the layers touch, are Width less twice that.
        /// </summary>
        public double Round { get; }

        /// <summary>The thickness of the joint band at the bottom of every layer but the first; 0 for none [length].</summary>
        public double JointThickness { get; }

        /// <summary>The speed of the nozzle [length per minute].</summary>
        public double Speed { get; }

        public PrintedLayers(double width, double height, int across, int up, double round, double jointThickness, double speed)
        {
            if (!(width > 0.0) || !(height > 0.0))
                throw new ArgumentException("The filament needs a width and a layer height greater than zero.");
            if (across < 1 || up < 1)
                throw new ArgumentException("There has to be at least one brick across the filament and one up the layer.");
            if (!(round >= 0.0 && round <= 1.0))
                throw new ArgumentException("Round is a share of half the layer height, from 0 (square sides) to 1 (a half circle).");
            if (round * height / 2 >= width / 2)
                throw new ArgumentException("The round sides meet in the middle: make the filament wider than the layer is high, or Round smaller.");
            if (!(jointThickness >= 0.0) || jointThickness >= height)
                throw new ArgumentException("The joint band has to be thinner than the layer, or 0 for none.");
            if (!(speed > 0.0))
                throw new ArgumentException("The print speed must be greater than zero.");

            this.Width = width;
            this.Height = height;
            this.Across = across;
            this.Up = up;
            this.Round = round;
            this.JointThickness = jointThickness;
            this.Speed = speed;
        }

        /// <summary>Half the width of the filament at <paramref name="v"/> above the bottom of its layer.</summary>
        public double HalfWidth(double v)
        {
            double c = this.Round * this.Height / 2, h = this.Height / 2;
            double s = Math.Max(0.0, 1.0 - Math.Pow((v - h) / h, 2));
            return this.Width / 2 - c + c * Math.Sqrt(s);
        }

        /// <summary>The heights of the rows of nodes in a layer, from its bottom; the joint band, when it has one, first.</summary>
        public List<double> Rows(bool joint)
        {
            var rows = new List<double> { 0.0 };
            double start = joint ? this.JointThickness : 0.0;
            if (joint)
                rows.Add(start);
            for (int j = 1; j <= this.Up; j++)
                rows.Add(start + (this.Height - start) * j / this.Up);
            return rows;
        }

        /// <summary>
        /// The object as printed after <paramref name="time"/> minutes, the paths printed one after the
        /// other in the order given, each from its first point.
        /// </summary>
        public Result Print(IList<Path> paths, double time)
        {
            var result = new Result();
            var lengths = paths.Select(p => p.Length).ToList();
            double total = lengths.Sum();
            result.Duration = total / this.Speed;
            if (paths.Count == 0)
                return result;

            var bed = new HashSet<(long, long, long)>();
            double started = 0.0;   // minutes at which the current path began
            result.Nozzle = paths[0].Start(0) + new Vector3d(0, 0, this.Height);
            for (int l = 0; l < paths.Count; l++)
            {
                var path = paths[l];
                double duration = lengths[l] / this.Speed;
                if (time <= started)
                    break;

                bool joint = l > 0 && this.JointThickness > 0.0;
                var rows = this.Rows(joint);
                var frames = Frames(path);
                double along = 0.0;   // length printed along this path before the segment
                for (int s = 0; s < path.Segments; s++)
                {
                    double length = path.Start(s).DistanceTo(path.End(s));
                    double end = started + (along + length) / this.Speed;
                    if (end > time + 1e-12)
                    {
                        // The nozzle is in this segment.
                        double share = length > 0 ? Math.Max(0.0, (time - started) * this.Speed - along) / length : 0.0;
                        result.Nozzle = path.Start(s) + share * (path.End(s) - path.Start(s)) + new Vector3d(0, 0, this.Height);
                        result.Layers = l + (along + share * length) / lengths[l];
                        return result;
                    }
                    double age = time - (started + (along + length / 2) / this.Speed);
                    this.AddSegment(result, bed, frames, path, s, l, rows, joint, age);
                    along += length;
                }
                started += duration;
                result.Nozzle = path.End(path.Segments - 1) + new Vector3d(0, 0, this.Height);
                result.Layers = l + 1;
            }
            return result;
        }

        /// <summary>The bricks of one segment of a layer: Across × the rows of its section.</summary>
        private void AddSegment(Result result, HashSet<(long, long, long)> bed, Frame[] frames, Path path, int s, int layer,
                                List<double> rows, bool joint, double age)
        {
            var a = frames[s];
            var b = frames[(s + 1) % frames.Length];
            Func<Frame, Point3d, int, int, Point3d> At = (f, o, i, j) =>
            {
                double v = rows[j];
                double u = this.HalfWidth(v) * (-1.0 + 2.0 * i / this.Across) * f.Scale;
                return new Point3d(o.X + u * f.Out.X, o.Y + u * f.Out.Y, o.Z + v);
            };

            Point3d start = path.Start(s), end = path.End(s);
            for (int j = 0; j < rows.Count - 1; j++)
                for (int i = 0; i < this.Across; i++)
                {
                    var corners = new[]
                    {
                        At(a, start, i, j), At(a, start, i + 1, j), At(a, start, i + 1, j + 1), At(a, start, i, j + 1),
                        At(b, end, i, j), At(b, end, i + 1, j), At(b, end, i + 1, j + 1), At(b, end, i, j + 1),
                    };
                    // OpenSees wants the first four corners, read in order, to turn about the line to the
                    // other four: from the start of the segment they go inner, outer, up - which turns
                    // about the direction of print because "outer" lies to its left.
                    var order = new[] { corners[0], corners[4], corners[5], corners[1], corners[3], corners[7], corners[6], corners[2] };
                    result.Bricks.Add(new Brick { Corners = order, Layer = layer, Segment = s, Age = age, Joint = joint && j == 0 });

                    if (layer == 0 && j == 0)
                        foreach (var p in new[] { corners[0], corners[1], corners[4], corners[5] })
                            if (bed.Add(Key(p)))
                                result.Base.Add(p);
                }
        }

        private static (long, long, long) Key(Point3d p) =>
            ((long)Math.Round(p.X * 1e7), (long)Math.Round(p.Y * 1e7), (long)Math.Round(p.Z * 1e7));

        /// <summary>The direction across the filament at a point of the path, horizontal, and how much a mitred corner widens it.</summary>
        private struct Frame
        {
            public Vector3d Out;
            public double Scale;
        }

        /// <summary>
        /// At each point of the path, the horizontal direction to its left across the filament: square
        /// to the path at its ends, along the bisector at a corner, where the section is widened by
        /// 1 / cos of half the turn so that the wall keeps its width on either side - up to three
        /// times, at the sharpest corners.
        /// </summary>
        private static Frame[] Frames(Path path)
        {
            int n = path.Points.Count;
            var frames = new Frame[n];
            Func<Point3d, Point3d, Vector3d> Left = (from, to) =>
            {
                double x = to.X - from.X, y = to.Y - from.Y, length = Math.Sqrt(x * x + y * y);
                if (!(length > 1e-12))
                    throw new ArgumentException("A print path runs straight up, or two of its points are in one place: a layer is printed flat.");
                return new Vector3d(-y / length, x / length, 0);
            };
            for (int k = 0; k < n; k++)
            {
                bool first = k == 0 && !path.Closed, last = k == n - 1 && !path.Closed;
                Vector3d before = first ? Vector3d.Unset : Left(path.Points[(k - 1 + n) % n], path.Points[k]);
                Vector3d after = last ? Vector3d.Unset : Left(path.Points[k], path.Points[(k + 1) % n]);
                if (first) { frames[k] = new Frame { Out = after, Scale = 1 }; continue; }
                if (last) { frames[k] = new Frame { Out = before, Scale = 1 }; continue; }
                var bisector = before + after;
                double size = Math.Sqrt(bisector.X * bisector.X + bisector.Y * bisector.Y);
                bisector = size > 1e-9 ? new Vector3d(bisector.X / size, bisector.Y / size, 0) : after;   // or the path doubles back on itself
                double cos = bisector.X * after.X + bisector.Y * after.Y;
                frames[k] = new Frame { Out = bisector, Scale = Math.Min(3.0, 1.0 / Math.Max(cos, 1.0 / 3.0)) };
            }
            return frames;
        }
    }
}

namespace Alpaca4d.Material
{
    /// <summary>
    /// Concrete in the minutes after it leaves the nozzle of a printer, whose stiffness grows with
    /// its age. Wolfs, Bos and Salet ("Early age mechanical behaviour of 3D printed concrete",
    /// Cement and Concrete Research 106, 2018) measured a printable mortar over its first 90
    /// minutes: its Young's modulus grows in a straight line, E(t) = 0.078 + 0.0012 t MPa with t in
    /// minutes, its Poisson's ratio stays at 0.3, and it weighs 2070 kg/m³. Those are the defaults.
    ///
    /// A final stiffness turns the straight line into the curve Suiker ("Mechanical performance of
    /// wall structures in 3D printing processes", Int. J. Mech. Sci. 137, 2018) calls exponentially
    /// decaying curing - the shape an accelerator gives: E(t) = E∞ − (E∞ − E0) e^(−ξt), with ξ chosen
    /// so that it starts at the same rate, ξ = rate / (E∞ − E0).
    ///
    /// Stresses here are in MPa and ages in minutes, as the papers give them; the material it makes
    /// is in the model's units.
    /// </summary>
    public sealed class FreshConcrete
    {
        /// <summary>Wolfs et al. 2018: the stiffness at age 0 [MPa].</summary>
        public const double WolfsE0 = 0.078;

        /// <summary>Wolfs et al. 2018: how fast it stiffens [MPa/min].</summary>
        public const double WolfsRate = 0.0012;

        /// <summary>Wolfs et al. 2018: constant over the first 90 minutes.</summary>
        public const double WolfsPoisson = 0.3;

        /// <summary>Wolfs et al. 2018: the average of their compression and shear tests [kg/m³].</summary>
        public const double WolfsDensity = 2070.0;

        /// <summary>The stiffness as it leaves the nozzle [MPa].</summary>
        public double E0 { get; }

        /// <summary>How fast it stiffens at first [MPa/min].</summary>
        public double Rate { get; }

        /// <summary>The stiffness it levels off at [MPa]; NaN for a straight line that never does.</summary>
        public double EFinal { get; }

        public double Poisson { get; }

        /// <summary>[kg/m³]</summary>
        public double Density { get; }

        public FreshConcrete(double e0 = WolfsE0, double rate = WolfsRate, double eFinal = double.NaN,
                             double poisson = WolfsPoisson, double density = WolfsDensity)
        {
            if (!(e0 > 0.0))
                throw new ArgumentException("The stiffness at age 0, E0, must be greater than zero.");
            if (!(rate >= 0.0))
                throw new ArgumentException("The rate the concrete stiffens at cannot be negative.");
            if (!double.IsNaN(eFinal) && !(eFinal > e0))
                throw new ArgumentException("The final stiffness has to be greater than E0: it is what the concrete stiffens towards.");
            if (!(poisson > -1.0 && poisson < 0.5))
                throw new ArgumentException("Poisson's ratio has to lie between -1 and 0.5.");
            if (!(density >= 0.0))
                throw new ArgumentException("The density cannot be negative.");

            this.E0 = e0;
            this.Rate = rate;
            this.EFinal = eFinal;
            this.Poisson = poisson;
            this.Density = density;
        }

        /// <summary>Whether the stiffness levels off, rather than growing in a straight line.</summary>
        public bool Levels => !double.IsNaN(this.EFinal);

        /// <summary>Young's modulus at <paramref name="age"/> minutes [MPa].</summary>
        public double Modulus(double age)
        {
            if (!(age >= 0.0))
                throw new ArgumentException($"An age of {age} minutes: concrete not yet printed has no stiffness to give.");
            if (!this.Levels)
                return this.E0 + this.Rate * age;
            double span = this.EFinal - this.E0;
            return this.EFinal - span * Math.Exp(-this.Rate / span * age);
        }

        /// <summary>The concrete at <paramref name="age"/> minutes as an elastic material, in the model's units.</summary>
        public ElasticIsotropicMaterial At(double age, string name = null)
        {
            double e = ModelStress.FromMPa(this.Modulus(age));
            return new ElasticIsotropicMaterial(name ?? "Fresh concrete, " + age.ToString("0.##") + " min", e, e / (2.0 * (1.0 + this.Poisson)), this.Poisson, this.Density);
        }
    }

    /// <summary>
    /// The joint between two printed layers as a thin band of solids: a surface connection whose
    /// stiffness is given per area, normal to the joint and along it, and which the band carries as
    /// an orthotropic material - Ez = k_n t across it, Gxz = Gyz = k_s t along it, t its thickness.
    /// A slip δ along the joint under a shear stress τ is then τ / k_s, as with springs spread over
    /// the joint. In the band's own plane it is the concrete it joins.
    ///
    /// The band is marked (<see cref="ElasticOrthotropicMaterial.IsSurfaceConnection"/>), for a
    /// buckling analysis to give it the stress stiffness of a joint rather than of a band: a band t
    /// thick under a pressure σ would also soften against slip by σ / t, which springs of no thickness
    /// never do, and buckle in shear at σ = k_s t, a pressure that vanishes as the band gets thinner.
    /// </summary>
    public static class SurfaceConnection
    {
        /// <summary>
        /// The band's material. A stiffness left null is the concrete's own across the thickness -
        /// E / t normal to the joint, G / t along it - so a band given neither is the concrete.
        /// </summary>
        /// <param name="normal">k_n [model force / length³].</param>
        /// <param name="shear">k_s [model force / length³].</param>
        public static ElasticOrthotropicMaterial Joint(ElasticIsotropicMaterial concrete, double thickness, double? normal, double? shear,
                                                       out double kn, out double ks)
        {
            if (concrete == null)
                throw new ArgumentException("Connect the concrete the joint is in: an elastic isotropic material.");
            if (!(thickness > 0.0))
                throw new ArgumentException("The joint band needs a thickness greater than zero.");
            if (normal.HasValue && !(normal.Value > 0.0))
                throw new ArgumentException("The normal stiffness of the joint must be greater than zero.");
            if (shear.HasValue && !(shear.Value > 0.0))
                throw new ArgumentException("The shear stiffness of the joint must be greater than zero.");

            kn = normal ?? concrete.E / thickness;
            ks = shear ?? concrete.G / thickness;
            double ez = kn * thickness, gz = ks * thickness;

            // Poisson's effect across the joint is kept only while the band is as stiff across it as
            // the concrete is: a softer band with it would be a material that is not positive definite,
            // its ν_xz = ν_zx Ex / Ez far above one.
            double across = Math.Abs(ez - concrete.E) <= 1e-9 * concrete.E ? concrete.Nu : 0.0;
            var name = $"Surface connection, kn {kn:g4} ks {ks:g4}";
            return new ElasticOrthotropicMaterial(name, concrete.E, concrete.E, ez, concrete.G, gz, gz,
                                                  concrete.Nu, across, across, concrete.Rho) { IsSurfaceConnection = true };
        }
    }
}
