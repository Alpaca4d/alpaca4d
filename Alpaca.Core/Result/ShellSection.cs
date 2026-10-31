using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;
using PureHDF;

namespace Alpaca4d.Result
{
    public partial class Read
    {
        /// <summary>
        /// The eight stress resultants at every Gauss point of every ASD shell, by element tag: pxx,
        /// pyy, pxy, mxx, myy, mxy, vxz, vyz, in the shell's own axes, as Shell Forces reads them.
        ///
        /// Keyed by the recorder's ID dataset rather than by row position, so it needs nothing of
        /// the model but the file - which is also what lets it be checked against a deck run by
        /// hand. Every group of the two classes is read, for the reason given at SolidStress.
        /// </summary>
        public static Dictionary<int, double[][]> ShellForcesByTag(Model alpacaModel, int step)
        {
            const string BASE = ON_ELEMENTS + "/section.force";
            const int COMPONENTS = 8;

            var output = new Dictionary<int, double[][]>();

            string recorderPath = System.IO.Path.GetFullPath(alpacaModel.Recorders.First().FileName);

            using var h5File = PureHDF.H5File.OpenRead(recorderPath);

            if (!h5File.LinkExists(BASE))
                return output;

            foreach (var group in h5File.Group(BASE).Children().OfType<PureHDF.IH5Group>())
            {
                if (!group.Name.Contains("ASDShellQ4") && !group.Name.Contains("ASDShellT3"))
                    continue;
                if (!group.LinkExists("DATA") || !group.LinkExists("ID"))
                    continue;

                var dataGroup = group.Group("DATA");
                if (!dataGroup.LinkExists($"{STEP_PREFIX}{step}"))
                    throw new Exception($"STEP_{step} not defined!");

                var idDataset = group.Dataset("ID");
                var dataDataset = dataGroup.Dataset($"{STEP_PREFIX}{step}");

                long rows = (long)dataDataset.Space.Dimensions[0];
                long cols = (long)dataDataset.Space.Dimensions[1];
                long idRows = (long)idDataset.Space.Dimensions[0];

                double[,] data = dataDataset.Read<double>().ToArray2D(rows, cols);
                int[,] ids = idDataset.Read<int>().ToArray2D(idRows, 1L);

                int points = (int)(cols / COMPONENTS);

                for (int r = 0; r < rows && r < idRows; r++)
                {
                    var gauss = new double[points][];
                    for (int g = 0; g < points; g++)
                    {
                        gauss[g] = new double[COMPONENTS];
                        for (int c = 0; c < COMPONENTS; c++)
                            gauss[g][c] = data[r, g * COMPONENTS + c];
                    }

                    output[ids[r, 0]] = gauss;
                }
            }

            return output;
        }
    }

    /// <summary>
    /// One shell as a section cut sees it: where its corners are, the axes its forces are in, and
    /// the forces at its Gauss points.
    /// </summary>
    public class SectionFace
    {
        /// <summary>Element tag.</summary>
        public int Tag;

        /// <summary>The corners, three or four, in the order the solver was given them.</summary>
        public Point3d[] Nodes;

        /// <summary>The corners' node tags, which is how two faces know they share a corner.</summary>
        public int[] NodeTags;

        /// <summary>The axes the forces are reported in - see Utils.ShellFrame.</summary>
        public Vector3d X, Y, Z;

        /// <summary>
        /// The eight stress resultants at each Gauss point, in the solver's order: pxx, pyy, pxy,
        /// mxx, myy, mxy, vxz, vyz. Four points on an ASDShellQ4, three on an ASDShellT3.
        /// </summary>
        public double[][] Gauss;

        /// <summary>
        /// A face with the frame OpenSees gives it. A triangle's transverse shears are scaled by
        /// <see cref="TriangleShearFactor"/> on the way in, for which it needs the
        /// <paramref name="thickness"/> of its section; without one they are taken as recorded.
        /// </summary>
        public static SectionFace Of(int tag, IList<Point3d> nodes, IList<int> nodeTags, Vector3d localX, double[][] gauss,
                                     double thickness = 0.0)
        {
            var frame = Utils.ShellFrame(nodes, localX);

            if (gauss != null && nodes.Count == 3 && thickness > 0.0)
            {
                double factor = TriangleShearFactor(nodes, thickness);
                gauss = gauss.Select(point =>
                {
                    var scaled = (double[])point.Clone();
                    scaled[6] *= factor;
                    scaled[7] *= factor;
                    return scaled;
                }).ToArray();
            }

            return new SectionFace
            {
                Tag = tag,
                Nodes = nodes.ToArray(),
                NodeTags = nodeTags.ToArray(),
                X = frame.X,
                Y = frame.Y,
                Z = frame.Z,
                Gauss = gauss,
            };
        }

        /// <summary>
        /// What an ASDShellT3's recorded transverse shears are multiplied by to be the shears it is
        /// in equilibrium with.
        ///
        /// The element damps its shear by Stenberg's factor, sqrt(h² / (h² + 0.1 L²)) with L its
        /// longest side, to keep thin triangles from locking. It scales the shear strain by it
        /// before handing it to the section and the section's shear by it again before adding it
        /// to the element's forces (ASDShellT3::calculateAll) - but the recorder reads the section,
        /// in between. On a triangle a little longer than its thickness the recorded shear is half
        /// as large again as the shear that carries the load. The thickness OpenSees uses is the
        /// one the section's stiffness implies, which for a section of one material is its own.
        /// </summary>
        public static double TriangleShearFactor(IList<Point3d> nodes, double thickness)
        {
            double longest = Math.Max(nodes[0].DistanceTo(nodes[1]),
                             Math.Max(nodes[1].DistanceTo(nodes[2]), nodes[2].DistanceTo(nodes[0])));
            double h2 = thickness * thickness;
            return Math.Sqrt(h2 / (h2 + 0.1 * longest * longest));
        }
    }

    /// <summary>
    /// The forces on the face of a cut at one point along it, per unit length of the cut.
    ///
    /// In the cut's own axes: x square to the cut in the shell's plane, y along the cut, z the
    /// shell's normal. They are the forces of Shell Forces on a face whose x is turned square to
    /// the cut, which is why they carry the same names: what acts on the face square to x is pxx,
    /// pxy, vxz, mxx and mxy, and pyy, myy and vyz act on the faces the cut does not open.
    /// </summary>
    public struct SectionStation
    {
        public Point3d Point;

        /// <summary>The tag of the shell the point is in.</summary>
        public int Element;

        /// <summary>Square to the cut, along it, and the shell's normal.</summary>
        public Vector3d X, Y, Z;

        public double Pxx, Pxy, Vxz, Mxx, Mxy;

        /// <summary>The components in the order of <see cref="ShellSection.ComponentNames"/>.</summary>
        public double Component(int index)
        {
            switch (index)
            {
                case 1: return Pxy;
                case 2: return Vxz;
                case 3: return Mxx;
                case 4: return Mxy;
                default: return Pxx;
            }
        }
    }

    /// <summary>One unbroken stretch of a cut: its line on the shells, and the forces along it.</summary>
    public class SectionRun
    {
        public Polyline Cut = new Polyline();
        public List<SectionStation> Stations = new List<SectionStation>();
    }

    /// <summary>
    /// What a curve cuts through the shells: every stretch of it, and the total force carried
    /// across it.
    /// </summary>
    public class SectionCut
    {
        public List<SectionRun> Runs = new List<SectionRun>();

        /// <summary>How long the cut is, all stretches together.</summary>
        public double Length;

        /// <summary>
        /// The axes the totals are in, at the centre of the cut: x square to it, y along it, z the
        /// shells' normal, each the mean over the cut.
        /// </summary>
        public Plane Axes = Plane.Unset;

        /// <summary>
        /// The total force and moment the side x points to puts on the other side, in the global
        /// axes, the moment about the centre of the cut.
        /// </summary>
        public Vector3d Force, Moment;

        /// <summary>The same, in <see cref="Axes"/>, read the way Beam Forces reads a beam whose axis is x.</summary>
        public double N, Vy, Vz, Mx, My, Mz;
    }

    /// <summary>
    /// The forces through a cut drawn across shells.
    ///
    /// The cut is the curve swept along a direction: each segment of the polyline, slid both ways
    /// along the direction, is a strip of a plane, and where that strip crosses a shell is where
    /// the shell is cut. A curve drawn on the shells and swept along their normal cuts them along
    /// itself; a line drawn in plan and swept vertically cuts every wall it crosses, top to bottom.
    ///
    /// Which way is positive comes from the cut, not from the elements. The shell's normal at the
    /// cut is turned so that x, square to the cut, always points to the same side of the strip:
    /// the left of the curve, looking along the direction. That is what makes the totals add up
    /// - every stretch reports the force on the same side of the same cut - and what keeps them
    /// the same on a mesh whose faces do not all face one way, which a mesh drawn in Rhino seldom
    /// does. Along a curve that runs square to the direction, it puts z on the side the direction
    /// points to.
    ///
    /// Within a shell the forces vary the way the element's own Gauss points say: bilinear through
    /// the four of a quad, linear through the three of a triangle. Smoothed, each corner instead
    /// takes the mean of every shell meeting there whose plane is within 30 degrees of its own.
    /// </summary>
    public class ShellSection
    {
        /// <summary>The per-length components, in the order <see cref="SectionStation.Component"/> takes them.</summary>
        public static readonly string[] ComponentNames = { "pxx", "pxy", "vxz", "mxx", "mxy" };

        /// <summary>How far apart, in degrees, two shells meeting at a corner may lie and still be smoothed together.</summary>
        public const double SmoothingAngle = 30.0;

        private readonly IList<SectionFace> _faces;
        private readonly bool _smooth;

        /// <summary>The faces meeting at each node, with which of their corners it is.</summary>
        private readonly Dictionary<int, List<(int Face, int Corner)>> _atNode = new Dictionary<int, List<(int, int)>>();

        /// <summary>The faces on each side, by the node tags at its ends, smaller first.</summary>
        private readonly Dictionary<(int, int), List<int>> _onEdge = new Dictionary<(int, int), List<int>>();

        private readonly Resultant[][] _corners;
        private readonly Resultant[][] _smoothed;

        /// <summary>The size of the model: what the tolerances are a share of.</summary>
        private readonly double _size;

        public ShellSection(IList<SectionFace> faces, bool smooth)
        {
            _faces = faces;
            _smooth = smooth;
            _corners = new Resultant[faces.Count][];
            _smoothed = new Resultant[faces.Count][];

            var low = new Point3d(double.MaxValue, double.MaxValue, double.MaxValue);
            var high = new Point3d(double.MinValue, double.MinValue, double.MinValue);

            for (int f = 0; f < faces.Count; f++)
            {
                var face = faces[f];
                int n = face.Nodes.Length;

                for (int c = 0; c < n; c++)
                {
                    var node = face.Nodes[c];
                    low = new Point3d(Math.Min(low.X, node.X), Math.Min(low.Y, node.Y), Math.Min(low.Z, node.Z));
                    high = new Point3d(Math.Max(high.X, node.X), Math.Max(high.Y, node.Y), Math.Max(high.Z, node.Z));

                    if (!_atNode.TryGetValue(face.NodeTags[c], out var list))
                        _atNode[face.NodeTags[c]] = list = new List<(int, int)>();
                    list.Add((f, c));

                    var key = EdgeKey(face.NodeTags[c], face.NodeTags[(c + 1) % n]);
                    if (!_onEdge.TryGetValue(key, out var sides))
                        _onEdge[key] = sides = new List<int>();
                    sides.Add(f);
                }
            }

            double size = faces.Count > 0 ? low.DistanceTo(high) : 0.0;
            _size = size > 0.0 ? size : 1.0;
        }

        private static (int, int) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);

        /// <summary>
        /// Scaled to length one, unless it has next to none. Written out, because RhinoCommon's own
        /// goes through native code, which a test run under mono does not have.
        /// </summary>
        private static bool Unitize(ref Vector3d v)
        {
            double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (length <= 1e-300 || double.IsNaN(length)) return false;
            v = new Vector3d(v.X / length, v.Y / length, v.Z / length);
            return true;
        }

        /// <summary>
        /// The normal of the shell nearest <paramref name="point"/>: the direction to sweep a curve
        /// along when none is given, which cuts a shell along a curve drawn on it.
        /// </summary>
        public Vector3d NormalNear(Point3d point)
        {
            double best = double.MaxValue;
            var normal = Vector3d.ZAxis;

            foreach (var face in _faces)
            {
                var centre = Point3d.Origin;
                foreach (var node in face.Nodes)
                    centre += node;
                centre /= face.Nodes.Length;

                double distance = centre.DistanceToSquared(point);
                if (distance < best)
                {
                    best = distance;
                    normal = face.Z;
                }
            }

            return normal;
        }

        #region The cut

        /// <summary>One segment of the curve, swept along the direction.</summary>
        private struct Strip
        {
            public int Index;
            public Point3d Start;
            /// <summary>Along the segment, square to the direction.</summary>
            public Vector3d U;
            /// <summary>The strip's normal, U × direction: the side x points to.</summary>
            public Vector3d M;
            public double Length;
        }

        /// <summary>A straight piece of the cut, inside one face.</summary>
        private struct Piece
        {
            public int Face;
            public Strip Strip;
            public Point3d A, B;
            public int IdA, IdB;
        }

        /// <summary>
        /// The forces through <paramref name="curve"/>, a polyline, swept along
        /// <paramref name="direction"/>.
        /// </summary>
        public SectionCut Cut(IList<Point3d> curve, Vector3d direction)
        {
            var d = direction;
            if (!d.IsValid || !Unitize(ref d))
                throw new ArgumentException("The direction to sweep the curve along has no length.");

            double tolerance = 1e-7 * _size;

            var strips = new List<Strip>();
            for (int k = 0; k + 1 < curve.Count; k++)
            {
                var segment = curve[k + 1] - curve[k];
                var across = segment - (segment * d) * d;
                double length = across.Length;
                if (length <= tolerance) continue;

                across /= length;
                strips.Add(new Strip
                {
                    Index = strips.Count,
                    Start = curve[k],
                    U = across,
                    M = Vector3d.CrossProduct(across, d),
                    Length = length,
                });
            }

            var pieces = new List<Piece>();
            foreach (var strip in strips)
            {
                bool last = strip.Index == strips.Count - 1;
                for (int f = 0; f < _faces.Count; f++)
                    Slice(f, strip, last, d, tolerance, pieces);
            }

            return Assemble(pieces, d);
        }

        /// <summary>Where one strip crosses one face, added to <paramref name="pieces"/>.</summary>
        private void Slice(int f, Strip strip, bool last, Vector3d d, double tolerance, List<Piece> pieces)
        {
            var face = _faces[f];
            int n = face.Nodes.Length;

            var delta = new double[n];
            int above = 0, below = 0;
            double uMin = double.MaxValue, uMax = double.MinValue;

            for (int i = 0; i < n; i++)
            {
                var offset = face.Nodes[i] - strip.Start;
                double value = offset * strip.M;
                if (Math.Abs(value) <= tolerance) value = 0.0;
                delta[i] = value;
                if (value > 0.0) above++;
                if (value < 0.0) below++;

                double u = offset * strip.U;
                uMin = Math.Min(uMin, u);
                uMax = Math.Max(uMax, u);
            }

            // Wholly to one side, lying in the strip's plane, or beyond either end of it.
            if (above == n || below == n || above + below == 0) return;
            if (uMax < -tolerance || uMin > strip.Length + tolerance) return;

            // Round the face, so a corner on the plane counts once.
            var points = new List<(Point3d Point, int Corner)>();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (delta[i] == 0.0)
                    points.Add((face.Nodes[i], i));
                else if (delta[i] * delta[j] < 0.0)
                    points.Add((face.Nodes[i] + (face.Nodes[j] - face.Nodes[i]) * (delta[i] / (delta[i] - delta[j])), -1));
            }

            if (points.Count < 2) return;

            Point3d a, b;
            if (points.Count == 2)
            {
                a = points[0].Point;
                b = points[1].Point;

                int ca = points[0].Corner, cb = points[1].Corner;
                if (ca >= 0 && cb >= 0 && ((ca + 1) % n == cb || (cb + 1) % n == ca)
                    && !OwnsEdge(f, face.NodeTags[ca], face.NodeTags[cb], above > 0, strip, tolerance))
                    return;
            }
            else
            {
                // A warped quad the plane crosses four times. The two furthest apart.
                a = points[0].Point;
                b = points[1].Point;
                for (int i = 0; i < points.Count; i++)
                    for (int j = i + 1; j < points.Count; j++)
                        if (points[i].Point.DistanceToSquared(points[j].Point) > a.DistanceToSquared(b))
                        {
                            a = points[i].Point;
                            b = points[j].Point;
                        }
            }

            // Clipped to the strip: from the segment's start to its end.
            double ua = (a - strip.Start) * strip.U;
            double ub = (b - strip.Start) * strip.U;

            if (Math.Abs(ua - ub) <= tolerance)
            {
                // Running along the direction, square to the strip. At a corner of the curve it is
                // on the end of one strip and the start of the next, and is counted at the start.
                if (ua < -tolerance) return;
                if (last ? ua > strip.Length + tolerance : ua >= strip.Length - tolerance) return;
            }
            else
            {
                double from = (0.0 - ua) / (ub - ua);
                double to = (strip.Length - ua) / (ub - ua);
                double lo = Math.Max(0.0, Math.Min(from, to));
                double hi = Math.Min(1.0, Math.Max(from, to));
                if (hi <= lo) return;

                var start = a + (b - a) * lo;
                var end = a + (b - a) * hi;
                a = start;
                b = end;
            }

            if (a.DistanceTo(b) <= tolerance) return;

            pieces.Add(new Piece { Face = f, Strip = strip, A = a, B = b });
        }

        /// <summary>
        /// Whether a face with one edge lying in the strip's plane takes that edge as its piece of
        /// the cut. The face on either side of it would, so only one does: the one on the side of
        /// the strip x points to, or the face on the other side when there is none there - at the
        /// shells' edge, or where they fold.
        /// </summary>
        private bool OwnsEdge(int f, int a, int b, bool above, Strip strip, double tolerance)
        {
            if (above) return true;

            if (!_onEdge.TryGetValue(EdgeKey(a, b), out var sides)) return true;

            foreach (int g in sides)
            {
                if (g == f) continue;

                foreach (var node in _faces[g].Nodes)
                    if ((node - strip.Start) * strip.M > tolerance)
                        return false;
            }

            // The other side holds nothing either: a fold with both faces below the plane. The
            // first of them takes it.
            return sides.Where(g => g != f && _faces[g].Nodes.All(node => (node - strip.Start) * strip.M <= tolerance))
                        .All(g => g > f);
        }

        /// <summary>
        /// The pieces joined into unbroken stretches, each running the way the curve does, with the
        /// forces read along them and summed across the whole cut.
        /// </summary>
        private SectionCut Assemble(List<Piece> pieces, Vector3d d)
        {
            var cut = new SectionCut();
            if (pieces.Count == 0) return cut;

            // Ends welded, so pieces from either side of an edge meet.
            var welder = new Welder(1e-6 * _size);
            var kept = new List<Piece>();
            var seen = new HashSet<(int, int)>();
            foreach (var piece in pieces)
            {
                var p = piece;
                p.IdA = welder.Id(p.A);
                p.IdB = welder.Id(p.B);
                if (p.IdA == p.IdB) continue;
                if (!seen.Add(EdgeKey(p.IdA, p.IdB))) continue;
                kept.Add(p);
            }

            var incident = new Dictionary<int, List<int>>();
            for (int i = 0; i < kept.Count; i++)
            {
                foreach (int id in new[] { kept[i].IdA, kept[i].IdB })
                {
                    if (!incident.TryGetValue(id, out var list))
                        incident[id] = list = new List<int>();
                    list.Add(i);
                }
            }

            var used = new bool[kept.Count];
            var chains = new List<List<(int Piece, bool Forward)>>();

            List<(int, bool)> Walk(int from, int first)
            {
                var chain = new List<(int, bool)>();
                int at = from, next = first;
                while (next >= 0 && !used[next])
                {
                    used[next] = true;
                    bool forward = kept[next].IdA == at;
                    chain.Add((next, forward));
                    at = forward ? kept[next].IdB : kept[next].IdA;

                    // On through a point two pieces share; a junction of three or more ends it.
                    var there = incident[at];
                    next = there.Count == 2 ? (there[0] == next ? there[1] : there[0]) : -1;
                }
                return chain;
            }

            foreach (var entry in incident.Where(x => x.Value.Count != 2))
                foreach (int piece in entry.Value)
                    if (!used[piece])
                        chains.Add(Walk(entry.Key, piece));

            // What is left closes on itself.
            for (int i = 0; i < kept.Count; i++)
                if (!used[i])
                    chains.Add(Walk(kept[i].IdA, i));

            var force = Vector3d.Zero;
            var moment = Vector3d.Zero;
            var centre = Vector3d.Zero;
            var normals = Vector3d.Zero;
            var tangents = Vector3d.Zero;
            double length = 0.0;

            foreach (var chain in chains.Where(x => x.Count > 0))
            {
                var ordered = chain.Select(x => Oriented(kept[x.Item1], x.Item2)).ToList();
                if (RunsBackwards(ordered, d))
                    ordered = ordered.AsEnumerable().Reverse().Select(x => Oriented(x, false)).ToList();

                var run = new SectionRun();
                run.Cut.Add(ordered[0].A);

                foreach (var piece in ordered)
                {
                    run.Cut.Add(piece.B);

                    var stations = Stations(piece);

                    // Smoothed, the start of a piece is the end of the one before.
                    bool skipFirst = _smooth && run.Stations.Count > 0;
                    run.Stations.AddRange(skipFirst ? stations.Skip(1) : stations);

                    // Simpson's rule over the three stations: exact for the force, quadratic along
                    // a straight line through a bilinear field, and for its moment, one order up.
                    double pieceLength = piece.A.DistanceTo(piece.B);
                    var weights = new[] { pieceLength / 6.0, pieceLength * 4.0 / 6.0, pieceLength / 6.0 };

                    for (int i = 0; i < 3; i++)
                    {
                        var s = stations[i];
                        var traction = s.Pxx * s.X + s.Pxy * s.Y + s.Vxz * s.Z;
                        var couple = s.Mxx * s.Y - s.Mxy * s.X;
                        var at = new Vector3d(s.Point);

                        force += weights[i] * traction;
                        moment += weights[i] * (Vector3d.CrossProduct(at, traction) + couple);
                        centre += weights[i] * at;
                    }

                    normals += pieceLength * stations[1].Z;
                    tangents += pieceLength * stations[1].Y;
                    length += pieceLength;
                }

                cut.Runs.Add(run);
            }

            if (length <= 0.0) return cut;

            centre /= length;
            var origin = new Point3d(centre);

            var z = normals;
            if (!Unitize(ref z)) z = cut.Runs[0].Stations[0].Z;

            var y = tangents - (tangents * z) * z;
            if (!Unitize(ref y))
            {
                // A closed cut has no mean direction along it.
                y = cut.Runs[0].Stations[0].Y - (cut.Runs[0].Stations[0].Y * z) * z;
                if (!Unitize(ref y))
                {
                    y = Vector3d.CrossProduct(z, Math.Abs(z.X) < 0.9 ? Vector3d.XAxis : Vector3d.YAxis);
                    Unitize(ref y);
                }
            }

            var x = Vector3d.CrossProduct(y, z);

            cut.Length = length;

            // Set axis by axis: the constructor goes through native code, which a test run under
            // mono does not have.
            var axes = default(Plane);
            axes.Origin = origin;
            axes.XAxis = x;
            axes.YAxis = y;
            axes.ZAxis = z;
            cut.Axes = axes;
            cut.Force = force;
            cut.Moment = moment - Vector3d.CrossProduct(centre, force);

            cut.N = cut.Force * x;
            cut.Vy = cut.Force * y;
            cut.Vz = cut.Force * z;
            cut.Mx = cut.Moment * x;
            cut.My = cut.Moment * y;
            cut.Mz = cut.Moment * z;

            return cut;
        }

        private static Piece Oriented(Piece piece, bool forward)
        {
            if (forward) return piece;
            return new Piece { Face = piece.Face, Strip = piece.Strip, A = piece.B, B = piece.A, IdA = piece.IdB, IdB = piece.IdA };
        }

        /// <summary>
        /// Whether a stretch runs against the curve: ends earlier along it than it starts. One that
        /// runs along the direction instead, down a wall cut from a line drawn in plan, runs the
        /// way the direction points; one that closes on itself turns right-handed about the strip.
        /// </summary>
        private static bool RunsBackwards(List<Piece> ordered, Vector3d d)
        {
            double Along(Point3d point, Strip strip) => strip.Index + (point - strip.Start) * strip.U / strip.Length;

            var first = ordered[0];
            var last = ordered[ordered.Count - 1];

            double change = Along(last.B, last.Strip) - Along(first.A, first.Strip);
            if (Math.Abs(change) > 1e-9) return change < 0.0;

            if (first.A.DistanceTo(last.B) > 1e-9 * Math.Max(1.0, new Vector3d(first.A).Length))
                return (last.B - first.A) * d < 0.0;

            var turn = Vector3d.Zero;
            foreach (var piece in ordered)
                turn += Vector3d.CrossProduct(new Vector3d(piece.A), new Vector3d(piece.B));

            double about = turn * first.Strip.M;
            if (Math.Abs(about) > 1e-12) return about < 0.0;
            return turn * d < 0.0;
        }

        /// <summary>The forces at the start, the middle and the end of a piece.</summary>
        private SectionStation[] Stations(Piece piece)
        {
            var face = _faces[piece.Face];

            var along = piece.B - piece.A;
            along -= (along * face.Z) * face.Z;
            Unitize(ref along);

            // The normal turned so x is on the side of the strip M points to.
            double side = Vector3d.CrossProduct(along, face.Z) * piece.Strip.M;
            double sign = side < 0.0 ? -1.0 : 1.0;
            var z = sign * face.Z;
            var x = Vector3d.CrossProduct(along, z);

            var stations = new SectionStation[3];
            for (int i = 0; i < 3; i++)
            {
                var point = piece.A + (piece.B - piece.A) * (i / 2.0);
                var r = At(piece.Face, point);
                if (sign < 0.0) r = r.Flipped();

                stations[i] = new SectionStation
                {
                    Point = point,
                    Element = face.Tag,
                    X = x,
                    Y = along,
                    Z = z,
                    Pxx = r.N.Along(x, x),
                    Pxy = r.N.Along(x, along),
                    Vxz = r.Q * x,
                    Mxx = r.M.Along(x, x),
                    Mxy = r.M.Along(x, along),
                };
            }

            return stations;
        }

        #endregion

        #region The field in a face

        /// <summary>The forces at a point of a face, against the face's own normal.</summary>
        private Resultant At(int f, Point3d point)
        {
            var corners = Corners(f);
            var weights = ShapeWeights(_faces[f], point);

            var total = default(Resultant);
            for (int c = 0; c < corners.Length; c++)
                total = total + corners[c] * weights[c];

            return total;
        }

        /// <summary>The forces at each corner of a face, its own or smoothed.</summary>
        private Resultant[] Corners(int f)
        {
            if (!_smooth) return Own(f);

            if (_smoothed[f] != null) return _smoothed[f];

            var face = _faces[f];
            double limit = Math.Cos(SmoothingAngle * Math.PI / 180.0);
            var corners = new Resultant[face.Nodes.Length];

            for (int c = 0; c < corners.Length; c++)
            {
                var total = default(Resultant);
                int count = 0;

                foreach (var (g, corner) in _atNode[face.NodeTags[c]])
                {
                    double cos = _faces[g].Z * face.Z;
                    if (Math.Abs(cos) < limit) continue;

                    var r = Own(g)[corner];
                    total = total + (cos < 0.0 ? r.Flipped() : r);
                    count++;
                }

                corners[c] = count > 0 ? total * (1.0 / count) : Own(f)[c];
            }

            return _smoothed[f] = corners;
        }

        /// <summary>
        /// The forces at each corner of a face, from its own Gauss points: the bilinear field
        /// through the four of an ASDShellQ4, at ±1/√3, or the linear one through the three of an
        /// ASDShellT3, at the middles of its sides.
        /// </summary>
        private Resultant[] Own(int f)
        {
            if (_corners[f] != null) return _corners[f];

            var face = _faces[f];
            int n = face.Nodes.Length;
            var gauss = face.Gauss;
            var local = new double[n][];

            if (n == 4 && gauss.Length == 4)
            {
                // ASDShellQ4: the points in the same order as the corners. A corner is √3 times as
                // far out as its point, so the field through the points, read at the corner.
                double r3 = Math.Sqrt(3.0);
                double[] s = { -1, 1, 1, -1 };
                double[] t = { -1, -1, 1, 1 };
                for (int c = 0; c < 4; c++)
                {
                    local[c] = new double[8];
                    for (int g = 0; g < 4; g++)
                    {
                        double w = 0.25 * (1.0 + r3 * s[c] * s[g]) * (1.0 + r3 * t[c] * t[g]);
                        for (int k = 0; k < 8; k++)
                            local[c][k] += w * gauss[g][k];
                    }
                }
            }
            else if (n == 3 && gauss.Length == 3)
            {
                // ASDShellT3: point 0 halfway along side 2-3, point 1 along 1-3, point 2 along 1-2.
                // A linear field is the mean of its corners at the middle of a side.
                for (int c = 0; c < 3; c++)
                {
                    local[c] = new double[8];
                    for (int k = 0; k < 8; k++)
                    {
                        double sum = gauss[0][k] + gauss[1][k] + gauss[2][k];
                        // Corner c faces point c: the other two points less this one.
                        local[c][k] = sum - 2.0 * gauss[c][k];
                    }
                }
            }
            else
            {
                // Any other layout: the mean, everywhere.
                for (int c = 0; c < n; c++)
                {
                    local[c] = new double[8];
                    foreach (var point in gauss)
                        for (int k = 0; k < 8; k++)
                            local[c][k] += point[k] / gauss.Length;
                }
            }

            var corners = new Resultant[n];
            for (int c = 0; c < n; c++)
                corners[c] = Resultant.FromLocal(local[c], face.X, face.Y, face.Z);

            return _corners[f] = corners;
        }

        /// <summary>
        /// How much each corner counts at a point of the face: the element's own shape functions,
        /// at the point's natural coordinates, found in the plane of the face.
        /// </summary>
        private static double[] ShapeWeights(SectionFace face, Point3d point)
        {
            int n = face.Nodes.Length;

            var centre = Point3d.Origin;
            foreach (var node in face.Nodes)
                centre += node;
            centre /= n;

            var xs = new double[n];
            var ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = (face.Nodes[i] - centre) * face.X;
                ys[i] = (face.Nodes[i] - centre) * face.Y;
            }

            double px = (point - centre) * face.X;
            double py = (point - centre) * face.Y;

            if (n == 3)
            {
                double ax = xs[1] - xs[0], ay = ys[1] - ys[0];
                double bx = xs[2] - xs[0], by = ys[2] - ys[0];
                double det = ax * by - ay * bx;
                double cx = px - xs[0], cy = py - ys[0];
                double xi = (cx * by - cy * bx) / det;
                double eta = (ax * cy - ay * cx) / det;

                var w = new[] { 1.0 - xi - eta, xi, eta };
                for (int i = 0; i < 3; i++) w[i] = Math.Max(0.0, w[i]);
                double sum = w.Sum();
                for (int i = 0; i < 3; i++) w[i] /= sum;
                return w;
            }

            // The bilinear map turned round by Newton's method, from the middle.
            double[] s = { -1, 1, 1, -1 };
            double[] t = { -1, -1, 1, 1 };
            double u = 0.0, v = 0.0;

            for (int iteration = 0; iteration < 30; iteration++)
            {
                double fx = -px, fy = -py, dxu = 0, dxv = 0, dyu = 0, dyv = 0;
                for (int i = 0; i < 4; i++)
                {
                    double weight = 0.25 * (1 + s[i] * u) * (1 + t[i] * v);
                    double du = 0.25 * s[i] * (1 + t[i] * v);
                    double dv = 0.25 * t[i] * (1 + s[i] * u);
                    fx += weight * xs[i];
                    fy += weight * ys[i];
                    dxu += du * xs[i];
                    dxv += dv * xs[i];
                    dyu += du * ys[i];
                    dyv += dv * ys[i];
                }

                double det = dxu * dyv - dxv * dyu;
                if (Math.Abs(det) < 1e-300) break;

                double stepU = (fx * dyv - fy * dxv) / det;
                double stepV = (dxu * fy - dyu * fx) / det;
                u -= stepU;
                v -= stepV;

                if (Math.Abs(stepU) + Math.Abs(stepV) < 1e-13) break;
            }

            u = Math.Max(-1.0, Math.Min(1.0, u));
            v = Math.Max(-1.0, Math.Min(1.0, v));

            var weights = new double[4];
            for (int i = 0; i < 4; i++)
                weights[i] = 0.25 * (1 + s[i] * u) * (1 + t[i] * v);
            return weights;
        }

        #endregion

        /// <summary>
        /// The forces of a shell at a point as global tensors, so that faces with axes of their own
        /// can be added up: membrane forces and moments as symmetric tensors in the shell's plane,
        /// the transverse shears as a vector in it. All three against the face's normal: the
        /// moments and the shears change sign with it, the membrane forces do not.
        /// </summary>
        private struct Resultant
        {
            public Symmetric N, M;
            public Vector3d Q;

            public static Resultant FromLocal(double[] local, Vector3d x, Vector3d y, Vector3d z)
            {
                return new Resultant
                {
                    N = Symmetric.FromLocal(local[0], local[1], local[2], x, y),
                    M = Symmetric.FromLocal(local[3], local[4], local[5], x, y),
                    Q = local[6] * x + local[7] * y,
                };
            }

            public Resultant Flipped() => new Resultant { N = N, M = M * -1.0, Q = -Q };

            public static Resultant operator +(Resultant a, Resultant b) =>
                new Resultant { N = a.N + b.N, M = a.M + b.M, Q = a.Q + b.Q };

            public static Resultant operator *(Resultant a, double k) =>
                new Resultant { N = a.N * k, M = a.M * k, Q = a.Q * k };
        }

        /// <summary>A symmetric 3 × 3 tensor in the global axes.</summary>
        private struct Symmetric
        {
            public double XX, YY, ZZ, XY, YZ, ZX;

            /// <summary>a11 x⊗x + a22 y⊗y + a12 (x⊗y + y⊗x).</summary>
            public static Symmetric FromLocal(double a11, double a22, double a12, Vector3d x, Vector3d y)
            {
                return new Symmetric
                {
                    XX = a11 * x.X * x.X + a22 * y.X * y.X + 2.0 * a12 * x.X * y.X,
                    YY = a11 * x.Y * x.Y + a22 * y.Y * y.Y + 2.0 * a12 * x.Y * y.Y,
                    ZZ = a11 * x.Z * x.Z + a22 * y.Z * y.Z + 2.0 * a12 * x.Z * y.Z,
                    XY = a11 * x.X * x.Y + a22 * y.X * y.Y + a12 * (x.X * y.Y + y.X * x.Y),
                    YZ = a11 * x.Y * x.Z + a22 * y.Y * y.Z + a12 * (x.Y * y.Z + y.Y * x.Z),
                    ZX = a11 * x.Z * x.X + a22 * y.Z * y.X + a12 * (x.Z * y.X + y.Z * x.X),
                };
            }

            /// <summary>a · T · b.</summary>
            public double Along(Vector3d a, Vector3d b)
            {
                return a.X * (XX * b.X + XY * b.Y + ZX * b.Z)
                     + a.Y * (XY * b.X + YY * b.Y + YZ * b.Z)
                     + a.Z * (ZX * b.X + YZ * b.Y + ZZ * b.Z);
            }

            public static Symmetric operator +(Symmetric a, Symmetric b) => new Symmetric
            {
                XX = a.XX + b.XX, YY = a.YY + b.YY, ZZ = a.ZZ + b.ZZ,
                XY = a.XY + b.XY, YZ = a.YZ + b.YZ, ZX = a.ZX + b.ZX,
            };

            public static Symmetric operator *(Symmetric a, double k) => new Symmetric
            {
                XX = a.XX * k, YY = a.YY * k, ZZ = a.ZZ * k,
                XY = a.XY * k, YZ = a.YZ * k, ZX = a.ZX * k,
            };
        }

        /// <summary>Points within a tolerance of one another given one id, through a grid of cells.</summary>
        private sealed class Welder
        {
            private readonly double _tolerance;
            private readonly Dictionary<(long, long, long), List<int>> _cells = new Dictionary<(long, long, long), List<int>>();
            private readonly List<Point3d> _points = new List<Point3d>();

            public Welder(double tolerance)
            {
                _tolerance = tolerance > 0.0 ? tolerance : 1e-9;
            }

            public int Id(Point3d point)
            {
                long cx = (long)Math.Floor(point.X / _tolerance);
                long cy = (long)Math.Floor(point.Y / _tolerance);
                long cz = (long)Math.Floor(point.Z / _tolerance);

                for (long i = cx - 1; i <= cx + 1; i++)
                    for (long j = cy - 1; j <= cy + 1; j++)
                        for (long k = cz - 1; k <= cz + 1; k++)
                            if (_cells.TryGetValue((i, j, k), out var ids))
                                foreach (int id in ids)
                                    if (_points[id].DistanceTo(point) <= _tolerance)
                                        return id;

                int created = _points.Count;
                _points.Add(point);
                if (!_cells.TryGetValue((cx, cy, cz), out var list))
                    _cells[(cx, cy, cz)] = list = new List<int>();
                list.Add(created);
                return created;
            }
        }
    }
}
