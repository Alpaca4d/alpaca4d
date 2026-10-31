using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

namespace Alpaca4d.Result
{
    /// <summary>
    /// Traces lines along one family of a <see cref="PrincipalField"/>: one through a seed, or a
    /// field of them grown from seeds by placing new seeds beside each finished line (neighbour
    /// seeding, after Jobard and Lefer, 1997, as LilyPad does it).
    ///
    /// A principal direction is a line, not an arrow, so each step takes whichever way of it is
    /// closer to the step before (fourth-order Runge-Kutta). A line ends where it leaves the mesh, cut
    /// at the edge it crosses. It also ends where the field stops saying which way to go: where the
    /// two principal values are equal (an isotropic point), or the direction turns more in one step
    /// than a line can. A line that went on through such a point would carry on along whichever
    /// neighbouring line it met, and draw it again.
    /// </summary>
    public class StressLineTracer
    {
        /// <summary>A step that turns further than this means the field gave no direction to follow.</summary>
        public const double MaxTurn = 45.0;

        /// <summary>Where the half principal difference falls below this share of the largest, there is no direction to follow.</summary>
        public const double Isotropic = 1e-3;

        readonly PrincipalField _field;
        readonly int _family;
        readonly double _step, _test, _onEdge, _maxLength;
        readonly int _maxSteps;
        readonly RTree _edgeTree = new RTree();
        readonly List<Line> _edges = new List<Line>();
        PointHash _existing;

        /// <summary>Asked now and then; true stops the work with an <see cref="OperationCanceledException"/>.</summary>
        public Func<bool> Cancelled { get; set; }

        /// <param name="family">1 follows the larger principal value, 2 the smaller.</param>
        /// <param name="step">The length of one step along a line.</param>
        /// <param name="test">A line ends when it comes closer than this to a line already finished; 0 lets lines run on.</param>
        public StressLineTracer(PrincipalField field, int family, double step, double test = 0.0)
        {
            _field = field;
            _family = family;
            _step = step;
            _test = test;

            var box = field.Mesh.GetBoundingBox(false);
            _maxLength = 20 * box.Diagonal.Length;
            _maxSteps = (int)Math.Min(int.MaxValue - 1, 2 * _maxLength / step) + 1000;
            // A point the mesh puts on its edge lies on it only as closely as single precision
            // allows, which goes with the size of the coordinates, not of the mesh.
            double extent = Math.Max(Math.Max(Math.Abs(box.Min.X), Math.Abs(box.Max.X)),
                            Math.Max(Math.Max(Math.Abs(box.Min.Y), Math.Abs(box.Max.Y)), Math.Max(Math.Abs(box.Min.Z), Math.Abs(box.Max.Z))));
            _onEdge = Math.Max(1e-4 * step, 1e-6 * extent);

            var topology = field.Mesh.TopologyEdges;
            for (int e = 0; e < topology.Count; e++)
            {
                if (topology.GetConnectedFaces(e).Length != 1) continue;
                var line = topology.EdgeLine(e);
                var bb = line.BoundingBox; bb.Inflate(_onEdge);
                _edgeTree.Insert(bb, _edges.Count);
                _edges.Add(line);
            }
        }

        /// <summary>One line through <paramref name="seed"/>, both ways.</summary>
        public Polyline Trace(Point3d seed)
        {
            if (!_field.Evaluate(seed, _family, out var start, out _, out _, out _))
                return new Polyline();

            var forward = Half(start, +1, out bool closed);
            if (closed) return new Polyline(forward);
            var back = Half(start, -1, out closed);
            if (closed) return new Polyline(back);

            back.Reverse();
            back.RemoveAt(back.Count - 1);
            back.AddRange(forward);
            return new Polyline(back);
        }

        /// <summary>
        /// A field of lines, <paramref name="separation"/> apart, grown from <paramref name="seeds"/>:
        /// each finished line offers seeds that far to either side of it, and a seed closer than that
        /// to a finished line is passed over.
        /// </summary>
        public List<Polyline> TraceField(IEnumerable<Point3d> seeds, double separation)
        {
            var lines = new List<Polyline>();
            double cell = Math.Max(separation, _test);
            _existing = new PointHash(cell > 0 ? cell : _step);

            // How near a seed may come to a finished line: LilyPad's rule, which keeps the gaps
            // between lines from showing when Test Dist. is small.
            double spacing = separation * Math.Max(0.6, _test / separation * 1.1);

            var queue = new Queue<Point3d>(seeds);
            for (int tried = 0; queue.Count > 0 && tried < 1000000; tried++)
            {
                // Onto the mesh first: a seed off its edge would come back to a point already used.
                if (!_field.Evaluate(queue.Dequeue(), _family, out var seed, out _, out _, out _)) continue;
                if (_existing.AnyWithin(seed, spacing)) continue;

                var line = Trace(seed);
                if (line.Count < 3) continue;
                lines.Add(line);
                foreach (var p in line) _existing.Add(p);

                // New seeds either side of the line, about every half separation along it.
                double run = 0;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    run += line[i].DistanceTo(line[i + 1]);
                    if (i > 0 && run < separation / 2) continue;
                    run = 0;
                    if (!_field.Evaluate(line[i], _family, out var at, out _, out var normal, out _)) continue;
                    var side = Vector3d.CrossProduct(normal, line[i + 1] - line[i]);
                    if (!side.Unitize()) continue;
                    queue.Enqueue(at + side * separation);
                    queue.Enqueue(at - side * separation);
                }
            }
            _existing = null;
            return lines;
        }

        // ---------------------------------------------------------------- one way along a line

        List<Point3d> Half(Point3d start, int sign, out bool closed)
        {
            closed = false;
            var points = new List<Point3d> { start };
            if (!_field.Evaluate(start, _family, out _, out var first, out _, out double strength) || Small(strength))
                return points;

            var previous = first * sign;
            var p = start;
            double length = 0;
            var own = new PointHash(_step);
            own.Add(start);
            int stuck = 0;

            for (int i = 0; i < _maxSteps; i++)
            {
                if (i % 200 == 0 && Cancelled != null && Cancelled())
                    throw new OperationCanceledException();
                if (length > _maxLength) break;

                if (!Direction(p, previous, out var k1, out strength) || Small(strength)) break;
                if (Vector3d.VectorAngle(k1, previous) > MaxTurn * Math.PI / 180.0) break;
                if (!Direction(p + k1 * (_step / 2), k1, out var k2, out _)) break;
                if (!Direction(p + k2 * (_step / 2), k1, out var k3, out _)) break;
                if (!Direction(p + k3 * _step, k1, out var k4, out _)) break;
                // A field that turns further than a line can within the step ends the line here,
                // rather than with a step that blends the two directions.
                if (Turns(k1, k2) || Turns(k1, k3) || Turns(k1, k4)) break;
                // The average as it is: stretched to a full step, it would carry a curving line
                // outwards a little at every step.
                var d = (k1 + 2 * k2 + 2 * k3 + k4) / 6.0;
                if (d.Length < 1e-9) break;

                var target = p + d * _step;
                if (!_field.Evaluate(target, _family, out var q, out _, out _, out _)) break;
                if (OnEdge(q))
                {
                    points.Add(Crossing(p, target, q));
                    break;
                }
                if (_existing != null && _test > 0 && _existing.AnyWithin(q, _test)) break;

                // A step over a fold can come out short once; one that keeps doing so is held
                // against the mesh and going nowhere.
                stuck = p.DistanceTo(q) < 0.25 * _step ? stuck + 1 : 0;
                if (stuck == 3) break;
                var chord = q - p;
                if (!chord.Unitize()) break;
                length += p.DistanceTo(q);
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
            }
            return points;
        }

        bool Small(double strength) => strength <= Isotropic * _field.Largest;

        static bool Turns(Vector3d a, Vector3d b) => Vector3d.VectorAngle(a, b) > MaxTurn * Math.PI / 180.0;

        bool Direction(Point3d at, Vector3d previous, out Vector3d direction, out double strength)
        {
            if (!_field.Evaluate(at, _family, out _, out direction, out _, out strength)) return false;
            if (direction * previous < 0) direction = -direction;
            return true;
        }

        bool OnEdge(Point3d point)
        {
            bool hit = false;
            _edgeTree.Search(new Sphere(point, _onEdge), (s, e) =>
            {
                if (_edges[e.Id].DistanceTo(point, true) <= _onEdge) { hit = true; e.Cancel = true; }
            });
            return hit;
        }

        /// <summary>Where the step from <paramref name="inside"/> to <paramref name="outside"/> crosses the edge of the mesh.</summary>
        Point3d Crossing(Point3d inside, Point3d outside, Point3d onEdge)
        {
            double lo = 0, hi = 1;
            Point3d last = onEdge;
            for (int i = 0; i < 30; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (!_field.Evaluate(inside + (outside - inside) * mid, _family, out var q, out _, out _, out _)) break;
                if (OnEdge(q)) { hi = mid; last = q; } else lo = mid;
            }
            return last;
        }
    }
}
