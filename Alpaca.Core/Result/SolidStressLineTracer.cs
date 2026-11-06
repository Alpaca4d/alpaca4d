using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

namespace Alpaca4d.Result
{
    /// <summary>
    /// Traces lines along one family of a <see cref="SolidPrincipalField"/>, inside the solid: one
    /// through a seed, or a field of them grown from seeds, as <see cref="StressLineTracer"/> does on
    /// shells.
    ///
    /// A principal direction is a line, not an arrow, so each step takes whichever way of it is
    /// closer to the step before (fourth-order Runge-Kutta). A line ends where it leaves the solid,
    /// cut at the face it crosses. It also ends where its principal stress comes level with another
    /// - in a solid that happens along whole lines, such as an axis of symmetry - or the direction
    /// turns more in one step than a line can: there the field no longer says which way to go.
    /// </summary>
    public class SolidStressLineTracer
    {
        /// <summary>A step that turns further than this means the field gave no direction to follow.</summary>
        public const double MaxTurn = 45.0;

        /// <summary>Where a principal stress comes within this share of the largest principal difference of another, there is no direction to follow.</summary>
        public const double Isotropic = 1e-3;

        /// <summary>A field stops once it has this many lines: past it, lines through a solid hide one another.</summary>
        public const int MaxLines = 5000;

        readonly SolidPrincipalField _field;
        readonly int _family;
        readonly double _step, _test, _maxLength;
        readonly int _maxSteps;
        PointHash _existing;

        /// <summary>Asked now and then; true stops the work with an <see cref="OperationCanceledException"/>.</summary>
        public Func<bool> Cancelled { get; set; }

        /// <param name="family">1 follows the largest principal stress, 2 the middle one, 3 the smallest.</param>
        /// <param name="step">The length of one step along a line.</param>
        /// <param name="test">A line ends when it comes closer than this to a line already finished; 0 lets lines run on.</param>
        public SolidStressLineTracer(SolidPrincipalField field, int family, double step, double test = 0.0)
        {
            if (family < 1 || family > 3)
                throw new ArgumentOutOfRangeException(nameof(family), "A solid has three principal families, 1 to 3.");
            _field = field;
            _family = family;
            _step = step;
            _test = test;
            _maxLength = 20 * field.Box.Diagonal.Length;
            _maxSteps = (int)Math.Min(int.MaxValue - 1, 2 * _maxLength / step) + 1000;
        }

        /// <summary>One line through <paramref name="seed"/>, both ways; empty when the seed is outside the solid.</summary>
        public Polyline Trace(Point3d seed)
        {
            int hint = -1;
            if (!_field.Contains(seed, ref hint))
                return new Polyline();

            var forward = Half(seed, hint, +1, out bool closed);
            if (closed) return new Polyline(forward);
            var back = Half(seed, hint, -1, out closed);
            if (closed) return new Polyline(back);

            back.Reverse();
            back.RemoveAt(back.Count - 1);
            back.AddRange(forward);
            return new Polyline(back);
        }

        /// <summary>
        /// A field of lines, <paramref name="separation"/> apart, grown from <paramref name="seeds"/>:
        /// each finished line offers seeds that far from it along the other two principal directions,
        /// and a seed closer than that to a finished line is passed over.
        /// </summary>
        public List<Polyline> TraceField(IEnumerable<Point3d> seeds, double separation)
        {
            var lines = new List<Polyline>();
            double cell = Math.Max(separation, _test);
            _existing = new PointHash(cell > 0 ? cell : _step);
            double spacing = separation * Math.Max(0.6, _test / separation * 1.1);
            var values = new double[3];
            var vectors = new Vector3d[3];

            var queue = new Queue<Point3d>(seeds);
            for (int tried = 0; queue.Count > 0 && tried < 1000000 && lines.Count < MaxLines; tried++)
            {
                if (tried % 200 == 0 && Cancelled != null && Cancelled())
                    throw new OperationCanceledException();

                var seed = queue.Dequeue();
                int hint = -1;
                if (!_field.Contains(seed, ref hint)) continue;
                if (_existing.AnyWithin(seed, spacing)) continue;

                var line = Trace(seed);
                if (line.Count < 3) continue;
                lines.Add(line);
                foreach (var p in line) _existing.Add(p);

                // New seeds about every half separation along the line, a separation away along the
                // other two principal directions there: the ones this family's lines lie across.
                double run = 0;
                int at = -1;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    run += line[i].DistanceTo(line[i + 1]);
                    if (i > 0 && run < separation / 2) continue;
                    run = 0;
                    if (!_field.Principal(line[i], ref at, values, vectors)) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        if (k == _family - 1) continue;
                        queue.Enqueue(line[i] + vectors[k] * separation);
                        queue.Enqueue(line[i] - vectors[k] * separation);
                    }
                }
            }
            _existing = null;
            return lines;
        }

        // ---------------------------------------------------------------- one way along a line

        List<Point3d> Half(Point3d start, int hint, int sign, out bool closed)
        {
            closed = false;
            var points = new List<Point3d> { start };
            if (!_field.Evaluate(start, _family, ref hint, out var first, out double strength) || Small(strength))
                return points;

            var previous = first * sign;
            var p = start;
            double length = 0;
            var own = new PointHash(_step);
            own.Add(start);

            for (int i = 0; i < _maxSteps; i++)
            {
                if (i % 200 == 0 && Cancelled != null && Cancelled())
                    throw new OperationCanceledException();
                if (length > _maxLength) break;

                if (!Direction(p, previous, ref hint, out var k1, out strength) || Small(strength)) break;
                if (Vector3d.VectorAngle(k1, previous) > MaxTurn * Math.PI / 180.0) break;

                // Near the boundary a half step can fall outside; the step then goes straight on. The
                // average is taken as it is: stretched to a full step, it would carry a curving line
                // outwards a little at every step.
                // A field that turns further than a line can within the step - where this family's
                // principal stress meets another and the family takes another direction - ends the
                // line here, rather than with a step that blends the two.
                int h = hint;
                Vector3d d = k1;
                if (Direction(p + k1 * (_step / 2), k1, ref h, out var k2, out _) &&
                    Direction(p + k2 * (_step / 2), k1, ref h, out var k3, out _) &&
                    Direction(p + k3 * _step, k1, ref h, out var k4, out _))
                {
                    if (Turns(k1, k2) || Turns(k1, k3) || Turns(k1, k4)) break;
                    d = (k1 + 2 * k2 + 2 * k3 + k4) / 6.0;
                }
                if (d.Length < 1e-9) break;

                var q = p + d * _step;
                int hq = hint;
                if (!_field.Contains(q, ref hq))
                {
                    var exit = Crossing(p, q, hint);
                    if (exit.DistanceTo(p) > 1e-9 * _step) points.Add(exit);
                    break;
                }
                if (_existing != null && _test > 0 && _existing.AnyWithin(q, _test)) break;

                var chord = q - p;
                length += chord.Length;
                if (!chord.Unitize()) break;
                points.Add(q);

                // Back at the start, heading the same way: a closed line.
                if (length > 3 * _step && q.DistanceTo(start) < _step && chord * (first * sign) > 0.95)
                {
                    points.Add(start);
                    closed = true;
                    break;
                }
                // Back on itself anywhere else: it would only go round again.
                if (length > 3 * _step && own.AnyWithin(q, _step / 2, skipLast: 3)) break;
                own.Add(q);

                previous = chord;
                p = q;
                hint = hq;
            }
            return points;
        }

        bool Small(double strength) => strength <= Isotropic * _field.Largest;

        static bool Turns(Vector3d a, Vector3d b) => Vector3d.VectorAngle(a, b) > MaxTurn * Math.PI / 180.0;

        bool Direction(Point3d at, Vector3d previous, ref int hint, out Vector3d direction, out double strength)
        {
            if (!_field.Evaluate(at, _family, ref hint, out direction, out strength)) return false;
            if (direction * previous < 0) direction = -direction;
            return true;
        }

        /// <summary>Where the step from <paramref name="inside"/> to <paramref name="outside"/> leaves the solid.</summary>
        Point3d Crossing(Point3d inside, Point3d outside, int hint)
        {
            double lo = 0, hi = 1;
            for (int i = 0; i < 40; i++)
            {
                double mid = 0.5 * (lo + hi);
                int h = hint;
                if (_field.Contains(inside + (outside - inside) * mid, ref h)) { lo = mid; hint = h; }
                else hi = mid;
            }
            return inside + (outside - inside) * lo;
        }
    }
}
