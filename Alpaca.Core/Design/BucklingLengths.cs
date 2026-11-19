using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Generic;

namespace Alpaca4d.Design
{
    /// <summary>Which buckling length: about local y, about local z, or the lateral-torsional one.</summary>
    public enum BucklingAxis
    {
        /// <summary>L_cr about local y: the beam bends about y and moves along z, so restraints along z shorten it.</summary>
        Y,

        /// <summary>L_cr about local z: the beam moves along y.</summary>
        Z,

        /// <summary>L_LT: the beam moves sideways, along the axis it is weak about - y's length when its major axis is z.</summary>
        LateralTorsional
    }

    /// <summary>What holds a node of a member in a direction across it.</summary>
    public enum HeldBy
    {
        None,

        /// <summary>The structure: supports, bracing and the members joined there leave it no way to move.</summary>
        Structure,

        /// <summary>A member it sits on, which runs across that direction and is held itself either side: a secondary beam on a main beam.</summary>
        Member
    }

    /// <summary>How a piece of a member between restraints buckles.</summary>
    public enum SegmentKind
    {
        /// <summary>Between two held points: its own length.</summary>
        Held,

        /// <summary>From a held point to a free end: twice its length, as a cantilever.</summary>
        Free,

        /// <summary>Nothing holds the member anywhere in this direction: the whole member.</summary>
        Unheld,

        /// <summary>
        /// Part of the way from a held point to a free end, between joints other members frame into:
        /// twice its length, as a storey of a sway frame whose beams hold its ends against turning.
        /// </summary>
        Sway
    }

    /// <summary>
    /// The buckling lengths of every beam, worked out from the geometry of the model: L_cr about local
    /// y and z, and L_LT, for EN 1993-1-1 6.3. They are what the steel check uses where the user gives
    /// none.
    ///
    /// A beam is often only a piece of a member - a 6 m column meshed at 0.5 m is twelve beams - so its
    /// own length is not the length that buckles. Three steps, all of them geometry:
    ///
    /// 1. Members. Beams that run on in line, kinked by less than 10° at the node they share, are one
    ///    member; where more than two could, the two most nearly in line pair up. Nothing else splits a
    ///    member - not a change of section, which the Section Optimiser makes all the time.
    ///
    /// 2. Where the structure holds each node. The model taken as pin-jointed - each member a chain of
    ///    bars that only push and pull, a straight run between two joints one bar; supports, constraints
    ///    and links as ties - has a mechanism wherever nothing stops a node moving. A direction a node
    ///    moves in, in any mechanism, is free there. Every mechanism comes out of one factorisation: see
    ///    <see cref="FreeDirections"/>. Shells and solids are left out: a slab or a wall holds nothing
    ///    here, and a length it shortens has to be given by hand.
    ///
    /// 3. Held points and lengths. Along each member, for each of its local axes across it, a joint is
    ///    held when the structure holds it within 45° of that axis, or when it sits on another member
    ///    that runs across that axis and is held so on both sides of the joint, which then carries it by
    ///    bending: a secondary beam on a main beam. A beam's buckling length is then the distance between
    ///    the held points either side of it, along the member; twice the distance to a held point from a
    ///    free end; and the whole member where nothing holds it.
    ///
    /// Safe on the whole, a pin-jointed frame being looser than a real one, but not everywhere: a sway
    /// column on a pinned base under a flexible beam can buckle over more than twice its height. The
    /// user's own lengths always win.
    /// </summary>
    public sealed class BucklingLengths
    {
        /// <summary>A beam as the lengths see it: a straight bar and its local axes.</summary>
        public struct Bar
        {
            public Point3d Start, End;

            /// <summary>The beam's local z; local y is z × x, as <see cref="Element.GeomTransf"/> has it. Unset for a default.</summary>
            public Vector3d LocalZ;

            /// <summary>The section is stiffer about local y than about z, so it buckles sideways along y.</summary>
            public bool MajorIsY;

            public Bar(Point3d start, Point3d end, Vector3d localZ, bool majorIsY = false)
            {
                Start = start;
                End = end;
                LocalZ = localZ;
                MajorIsY = majorIsY;
            }
        }

        /// <summary>A support holding a node against moving along <see cref="Direction"/>.</summary>
        public struct Restraint
        {
            public Point3d At;
            public Vector3d Direction;

            public Restraint(Point3d at, Vector3d direction)
            {
                At = at;
                Direction = direction;
            }
        }

        /// <summary>Two nodes held together along <see cref="Direction"/>: a constraint or a link.</summary>
        public struct Tie
        {
            public Point3d A, B;
            public Vector3d Direction;

            public Tie(Point3d a, Point3d b, Vector3d direction)
            {
                A = a;
                B = b;
                Direction = direction;
            }
        }

        /// <summary>A piece of a member between restraints, by the places of its end nodes along the member.</summary>
        public sealed class Segment
        {
            public int From { get; internal set; }
            public int To { get; internal set; }

            /// <summary>The buckling length it gives the beams on it.</summary>
            public double Length { get; internal set; }

            public SegmentKind Kind { get; internal set; }
        }

        /// <summary>Beams in line, and where they are held.</summary>
        public sealed class Member
        {
            /// <summary>Its beams, by their place in the list the lengths were worked out for, in order along it.</summary>
            public IReadOnlyList<int> Beams { get; internal set; }

            /// <summary>Its nodes in order, one more than its beams.</summary>
            public IReadOnlyList<Point3d> Points { get; internal set; }

            /// <summary>How far along the member each node is.</summary>
            public IReadOnlyList<double> Stations { get; internal set; }

            public double Length => Stations[Stations.Count - 1];

            /// <summary>
            /// At each node, the two directions across the member restraints are looked for in: the local
            /// y and z of its first beam, carried along it.
            /// </summary>
            public IReadOnlyList<Vector3d> AxisY { get; internal set; }
            public IReadOnlyList<Vector3d> AxisZ { get; internal set; }

            /// <summary>What holds each node along <see cref="AxisY"/>, and along <see cref="AxisZ"/>.</summary>
            public IReadOnlyList<HeldBy> HeldY { get; internal set; }
            public IReadOnlyList<HeldBy> HeldZ { get; internal set; }

            /// <summary>The pieces between restraints along <see cref="AxisY"/> - what buckling about local z sees - and along <see cref="AxisZ"/>.</summary>
            public IReadOnlyList<Segment> SegmentsY { get; internal set; }
            public IReadOnlyList<Segment> SegmentsZ { get; internal set; }

            /// <summary>Its first beam's section is stiffer about y: it buckles sideways along y.</summary>
            public bool MajorIsY { get; internal set; }

            /// <summary>Whether <paramref name="axis"/>'s restraints are those along <see cref="AxisY"/>.</summary>
            public bool AlongY(BucklingAxis axis) =>
                axis == BucklingAxis.Z || (axis == BucklingAxis.LateralTorsional && MajorIsY);

            /// <summary>The pieces that give its beams their length about <paramref name="axis"/>.</summary>
            public IReadOnlyList<Segment> Segments(BucklingAxis axis) => AlongY(axis) ? SegmentsY : SegmentsZ;

            /// <summary>What holds each node for <paramref name="axis"/>.</summary>
            public IReadOnlyList<HeldBy> Held(BucklingAxis axis) => AlongY(axis) ? HeldY : HeldZ;

            /// <summary>The direction <paramref name="axis"/>'s restraints act in, at each node.</summary>
            public IReadOnlyList<Vector3d> Direction(BucklingAxis axis) => AlongY(axis) ? AxisY : AxisZ;
        }

        /// <summary>L_cr about local y of each beam, in the order given; 0 for one with no length to work with.</summary>
        public IReadOnlyList<double> AboutY { get; private set; }

        /// <summary>L_cr about local z of each beam.</summary>
        public IReadOnlyList<double> AboutZ { get; private set; }

        /// <summary>L_LT of each beam: the length about the axis its section is weak about.</summary>
        public IReadOnlyList<double> LateralTorsional { get; private set; }

        /// <summary>The member each beam belongs to, by its place in <see cref="Members"/>; -1 for none.</summary>
        public IReadOnlyList<int> MemberOf { get; private set; }

        public IReadOnlyList<Member> Members { get; private set; }

        /// <summary>
        /// The pin-jointed model was too large to look for its mechanisms in, and each member was taken
        /// as held at its ends and at its supports: its own length, as before there were these lengths.
        /// </summary>
        public bool Approximate { get; private set; }

        /// <summary>The length of <paramref name="beam"/> about <paramref name="axis"/>; 0 when there is none.</summary>
        public double Of(int beam, BucklingAxis axis)
        {
            switch (axis)
            {
                case BucklingAxis.Y: return AboutY[beam];
                case BucklingAxis.Z: return AboutZ[beam];
                default: return LateralTorsional[beam];
            }
        }

        // Beams kinked by less than this at a node are one member.
        private static readonly double KinkCos = Math.Cos(10.0 * Math.PI / 180.0);

        // A run whose every beam is within this of the line between its ends is one bar.
        private static readonly double StraightCos = Math.Cos(2.0 * Math.PI / 180.0);

        // Held within 45° of a direction counts as held along it: |P d|² ≥ cos² 45°.
        private const double HeldShare = 0.5;

        // A member runs across a direction, and can carry a node along it by bending, when |t · d| is below this (60°).
        private const double Across = 0.5;

        // A pivot this small against its diagonal is a mechanism.
        private const double PivotTolerance = 1e-8;

        // Beyond these the mechanisms are not looked for: unknowns, and solves times the factor's size.
        private const int LargestSystem = 300000;
        private const double LargestWork = 4e9;

        /// <summary>
        /// A steel member for each beam of <paramref name="model"/>, in the order of Model.Beams, its
        /// length node to node and its buckling lengths these: what the steel check takes when the user
        /// gives none. A length the user does give goes over the one set here.
        /// </summary>
        public List<SteelMember> SteelMembers(Model model)
        {
            var members = new List<SteelMember>(model.Beams.Count);
            for (int i = 0; i < model.Beams.Count; i++)
            {
                var beam = model.Beams[i];
                var member = new SteelMember(beam?.Curve != null ? beam.Curve.PointAtStart.DistanceTo(beam.Curve.PointAtEnd) : 0.0);
                if (i < AboutY.Count && AboutY[i] > 0.0 && AboutZ[i] > 0.0)
                {
                    member.BucklingLengthY = AboutY[i];
                    member.BucklingLengthZ = AboutZ[i];
                    member.LateralTorsionalLength = LateralTorsional[i];
                }
                members.Add(member);
            }
            return members;
        }

        // What each model came to, kept as long as the model is: Utilisation, Model View, the Report and
        // the Section Optimiser all ask for the same model's lengths, and the Report once per combination.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Model, BucklingLengths> Worked =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Model, BucklingLengths>();

        // The model's beams and supports when its lengths were worked out: a model changed since has them again.
        private int _beams = -1, _supports = -1, _constraints = -1;

        /// <summary>
        /// The lengths of every beam of <paramref name="model"/>, in the order of Model.Beams. Its
        /// supports, constraints and links hold its nodes; its shells and solids do not. Worked out once
        /// per model, which only has its beams and supports counted to see that it is still the one.
        /// </summary>
        public static BucklingLengths Of(Model model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            lock (Worked)
            {
                if (Worked.TryGetValue(model, out var known) && known._beams == model.Beams.Count &&
                    known._supports == model.Supports.Count && known._constraints == model.Constraint.Count)
                    return known;
            }

            var lengths = WorkOut(model);
            lengths._beams = model.Beams.Count;
            lengths._supports = model.Supports.Count;
            lengths._constraints = model.Constraint.Count;
            lock (Worked)
            {
                Worked.Remove(model);
                Worked.Add(model, lengths);
            }
            return lengths;
        }

        private static BucklingLengths WorkOut(Model model)
        {

            var majorIsY = new Dictionary<IUniaxialSection, bool>();
            var bars = new List<Bar>(model.Beams.Count);
            foreach (var beam in model.Beams)
            {
                if (beam?.Curve == null)
                {
                    bars.Add(new Bar(Point3d.Unset, Point3d.Unset, Vector3d.Unset));
                    continue;
                }

                bool major = false;
                if (beam.Section != null && !majorIsY.TryGetValue(beam.Section, out major))
                {
                    try { major = beam.Section.Iyy > beam.Section.Izz; }
                    catch (Exception) { major = false; }
                    majorIsY[beam.Section] = major;
                }

                bars.Add(new Bar(beam.Curve.PointAtStart, beam.Curve.PointAtEnd,
                                 beam.GeomTransf?.LocalZ ?? Vector3d.Unset, major));
            }

            var restraints = new List<Restraint>();
            foreach (var support in model.Supports)
            {
                if (support == null || !support.Pos.IsValid) continue;
                var plane = support.Plane;
                var axes = new[] { plane.XAxis, plane.YAxis, plane.ZAxis };
                foreach (int direction in support.RestrainedDirections)
                    if (direction >= 1 && direction <= 3)
                        restraints.Add(new Restraint(support.Pos, axes[direction - 1]));
            }

            var ties = new List<Tie>();
            foreach (var constraint in model.Constraint)
                AddTies(constraint, ties, model.Tollerance);

            foreach (var link in model.Links.OfType<Alpaca4d.Element.Link>())
            {
                // The frame the link itself counts its directions along.
                var line = link.Line;
                Alpaca4d.Element.LinkFrame.Axes(link.Orient, line.To - line.From, out var x, out var y);
                var axes = new[] { x, y, Vector3d.CrossProduct(x, y) };
                foreach (int direction in link.Directions ?? new List<int>())
                    if (direction >= 1 && direction <= 3)
                        ties.Add(new Tie(line.From, line.To, axes[direction - 1]));
            }

            return Of(bars, restraints, ties, Math.Max(model.Tollerance, 1e-9));
        }

        private static void AddTies(IConstraint constraint, List<Tie> ties, double tolerance)
        {
            switch (constraint)
            {
                case Alpaca4d.Constraints.EqualDOF equal:
                    if (equal.Dof_x) ties.Add(new Tie(equal.MasterNode, equal.SlaveNode, Vector3d.XAxis));
                    if (equal.Dof_y) ties.Add(new Tie(equal.MasterNode, equal.SlaveNode, Vector3d.YAxis));
                    if (equal.Dof_z) ties.Add(new Tie(equal.MasterNode, equal.SlaveNode, Vector3d.ZAxis));
                    break;

                case Alpaca4d.Constraints.RigidLink link:
                {
                    // A bar between the two; at one point, the two nodes as one. A rigid beam link also
                    // moves the far node across as the near one turns, but a pin-jointed node does not turn.
                    var along = link.ConstrainedNode - link.RetainedNode;
                    if (along.Length > Math.Max(tolerance, 1e-9))
                        ties.Add(new Tie(link.RetainedNode, link.ConstrainedNode, along));
                    else
                        foreach (var axis in new[] { Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis })
                            ties.Add(new Tie(link.RetainedNode, link.ConstrainedNode, axis));
                    break;
                }

                case Alpaca4d.Constraints.RigidDiaphragm diaphragm:
                {
                    // Rigid in its plane: three nodes far apart tied into a triangle, every other node
                    // tied to two of them, each tie squared into the plane.
                    var normal = diaphragm.Dir == 1 ? Vector3d.XAxis : diaphragm.Dir == 2 ? Vector3d.YAxis : Vector3d.ZAxis;
                    var nodes = new List<Point3d>();
                    if (diaphragm.MasterNode.HasValue) nodes.Add(diaphragm.MasterNode.Value);
                    if (diaphragm.SlaveNodes != null) nodes.AddRange(diaphragm.SlaveNodes);
                    if (nodes.Count < 2) break;

                    Vector3d InPlane(Point3d a, Point3d b)
                    {
                        var v = b - a;
                        return v - normal * (v * normal);
                    }

                    var p0 = nodes[0];
                    var p1 = nodes.OrderByDescending(p => InPlane(p0, p).Length).First();
                    var chord = InPlane(p0, p1);
                    if (!Normalise(ref chord)) break;
                    var p2 = nodes.OrderByDescending(p => Vector3d.CrossProduct(chord, InPlane(p0, p)).Length).First();
                    var anchors = new[] { p0, p1, p2 };
                    bool flat = Vector3d.CrossProduct(chord, InPlane(p0, p2)).Length <= 1e-9 * Math.Max(1.0, InPlane(p0, p1).Length);

                    void Add(Point3d a, Point3d b)
                    {
                        var d = InPlane(a, b);
                        if (d.Length > Math.Max(tolerance, 1e-9)) ties.Add(new Tie(a, b, d));
                    }

                    Add(p0, p1);
                    if (!flat)
                    {
                        Add(p0, p2);
                        Add(p1, p2);
                    }
                    foreach (var p in nodes)
                    {
                        if (anchors.Any(a => a.DistanceTo(p) <= tolerance)) continue;
                        // The two anchors it sees at the widest angle.
                        var best = anchors.SelectMany((a, i) => anchors.Skip(i + 1).Select(b => (a, b)))
                                          .OrderByDescending(ab =>
                                          {
                                              var u = InPlane(p, ab.a);
                                              var v = InPlane(p, ab.b);
                                              return Normalise(ref u) && Normalise(ref v) ? Vector3d.CrossProduct(u, v).Length : -1.0;
                                          })
                                          .First();
                        Add(p, best.a);
                        Add(p, best.b);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// The lengths of <paramref name="bars"/>, held by <paramref name="restraints"/> and
        /// <paramref name="ties"/>. Points within <paramref name="tolerance"/> of each other are one node.
        /// </summary>
        public static BucklingLengths Of(IReadOnlyList<Bar> bars, IReadOnlyList<Restraint> restraints,
                                         IReadOnlyList<Tie> ties, double tolerance)
        {
            var work = new Work(bars, restraints ?? new Restraint[0], ties ?? new Tie[0], tolerance);
            return work.Run();
        }

        /// <summary>One working out of the lengths: the nodes, the members, the pin-jointed model.</summary>
        private sealed class Work
        {
            private readonly IReadOnlyList<Bar> _bars;
            private readonly NodeIndex _nodes;

            // Each bar's two nodes, or -1 for a bar with no length.
            private readonly int[] _start, _end;

            // The restraints and ties at each node, by node.
            private readonly List<(int Node, Vector3d Direction)> _restraints = new List<(int, Vector3d)>();
            private readonly List<(int A, int B, Vector3d Direction)> _ties = new List<(int, int, Vector3d)>();

            private readonly List<Chain> _chains = new List<Chain>();

            // Per node: the members through it (member, place along it), whether it is a joint, and the
            // directions it is free in - null when it is not in the pin-jointed model, and so free in all.
            private List<(int Member, int At)>[] _on;
            private bool[] _joint;
            private List<Vector3d>[] _free;
            private bool _approximate;

            private sealed class Chain
            {
                public List<int> Bars = new List<int>();
                public List<int> Nodes = new List<int>();
                public double[] Stations;
                public Vector3d[] Tangent;       // at each node
                public Vector3d[] AxisY, AxisZ;  // at each node
                public Vector3d[] BarZ;          // the member's z carried to each bar
                public HeldBy[] HeldY, HeldZ;
                public List<Segment> SegmentsY, SegmentsZ;
            }

            public Work(IReadOnlyList<Bar> bars, IReadOnlyList<Restraint> restraints, IReadOnlyList<Tie> ties, double tolerance)
            {
                _bars = bars;
                _nodes = new NodeIndex(tolerance);
                _start = new int[bars.Count];
                _end = new int[bars.Count];

                for (int i = 0; i < bars.Count; i++)
                {
                    _start[i] = _end[i] = -1;
                    var bar = bars[i];
                    if (!bar.Start.IsValid || !bar.End.IsValid || bar.Start.DistanceTo(bar.End) <= tolerance)
                        continue;
                    int a = _nodes.Weld(bar.Start), b = _nodes.Weld(bar.End);
                    if (a == b) continue;
                    _start[i] = a;
                    _end[i] = b;
                }

                foreach (var restraint in restraints)
                {
                    var d = restraint.Direction;
                    if (!restraint.At.IsValid || !d.IsValid || !Normalise(ref d)) continue;
                    _restraints.Add((_nodes.Weld(restraint.At), d));
                }

                foreach (var tie in ties)
                {
                    var d = tie.Direction;
                    if (!tie.A.IsValid || !tie.B.IsValid || !d.IsValid || !Normalise(ref d)) continue;
                    int a = _nodes.Weld(tie.A), b = _nodes.Weld(tie.B);
                    if (a != b) _ties.Add((a, b, d));
                }
            }

            private Point3d P(int node) => _nodes.Points[node];

            public BucklingLengths Run()
            {
                Chains();
                Joints();
                FreeDirections();
                for (int m = 0; m < _chains.Count; m++)
                    Hold(m);
                for (int m = 0; m < _chains.Count; m++)
                {
                    var chain = _chains[m];
                    var framed = Framed(m);
                    chain.SegmentsY = Segments(chain, chain.HeldY, framed);
                    chain.SegmentsZ = Segments(chain, chain.HeldZ, framed);
                }
                return Result();
            }

            #region Members

            /// <summary>Beams in line, joined two by two at each node, most nearly in line first.</summary>
            private void Chains()
            {
                int count = _bars.Count;
                var incident = new Dictionary<int, List<int>>();
                for (int i = 0; i < count; i++)
                {
                    if (_start[i] < 0) continue;
                    foreach (int node in new[] { _start[i], _end[i] })
                    {
                        if (!incident.TryGetValue(node, out var list))
                            incident[node] = list = new List<int>();
                        list.Add(i);
                    }
                }

                int Other(int bar, int node) => _start[bar] == node ? _end[bar] : _start[bar];

                var partner = new Dictionary<(int Bar, int Node), int>();
                foreach (var pair in incident)
                {
                    int node = pair.Key;
                    var list = pair.Value;
                    if (list.Count < 2) continue;

                    var away = list.Select(bar => Unit(P(Other(bar, node)) - P(node))).ToList();
                    var candidates = new List<(double Cos, int A, int B)>();
                    for (int a = 0; a < list.Count; a++)
                        for (int b = a + 1; b < list.Count; b++)
                        {
                            double cos = away[a] * away[b];
                            if (cos <= -KinkCos) candidates.Add((cos, list[a], list[b]));
                        }

                    var used = new HashSet<int>();
                    foreach (var c in candidates.OrderBy(c => c.Cos))
                    {
                        if (used.Contains(c.A) || used.Contains(c.B)) continue;
                        partner[(c.A, node)] = c.B;
                        partner[(c.B, node)] = c.A;
                        used.Add(c.A);
                        used.Add(c.B);
                    }
                }

                var seen = new bool[count];
                for (int first = 0; first < count; first++)
                {
                    if (seen[first] || _start[first] < 0) continue;
                    seen[first] = true;

                    var chainBars = new LinkedList<int>();
                    var chainNodes = new LinkedList<int>();
                    chainBars.AddLast(first);
                    chainNodes.AddLast(_start[first]);
                    chainNodes.AddLast(_end[first]);

                    foreach (bool forward in new[] { true, false })
                    {
                        int bar = first, node = forward ? _end[first] : _start[first];
                        while (partner.TryGetValue((bar, node), out int next) && !seen[next])
                        {
                            seen[next] = true;
                            int far = Other(next, node);
                            if (forward)
                            {
                                chainBars.AddLast(next);
                                chainNodes.AddLast(far);
                            }
                            else
                            {
                                chainBars.AddFirst(next);
                                chainNodes.AddFirst(far);
                            }
                            bar = next;
                            node = far;
                        }
                    }

                    var chain = new Chain { Bars = chainBars.ToList(), Nodes = chainNodes.ToList() };
                    Frame(chain);
                    _chains.Add(chain);
                }
            }

            /// <summary>
            /// Where each node is along the chain, which way it runs there, and its two axes across: the
            /// first beam's local y and z, carried from beam to beam by squaring z off each in turn.
            /// </summary>
            private void Frame(Chain chain)
            {
                int n = chain.Nodes.Count;
                int bars = chain.Bars.Count;
                var along = new Vector3d[bars];
                chain.Stations = new double[n];
                for (int k = 0; k < bars; k++)
                {
                    var v = P(chain.Nodes[k + 1]) - P(chain.Nodes[k]);
                    chain.Stations[k + 1] = chain.Stations[k] + v.Length;
                    along[k] = Unit(v);
                }

                chain.BarZ = new Vector3d[bars];
                for (int k = 0; k < bars; k++)
                {
                    var z = k == 0 ? OwnZ(chain.Bars[0], along[0]) : chain.BarZ[k - 1];
                    z -= along[k] * (z * along[k]);
                    if (!Normalise(ref z)) z = OwnZ(chain.Bars[k], along[k]);
                    chain.BarZ[k] = z;
                }

                chain.Tangent = new Vector3d[n];
                chain.AxisY = new Vector3d[n];
                chain.AxisZ = new Vector3d[n];
                for (int k = 0; k < n; k++)
                {
                    Vector3d t, z;
                    if (k == 0) { t = along[0]; z = chain.BarZ[0]; }
                    else if (k == n - 1) { t = along[bars - 1]; z = chain.BarZ[bars - 1]; }
                    else
                    {
                        t = Unit(along[k - 1] + along[k]);
                        z = chain.BarZ[k - 1] + chain.BarZ[k];
                        z -= t * (z * t);
                        if (!Normalise(ref z)) z = chain.BarZ[k];
                    }
                    chain.Tangent[k] = t;
                    chain.AxisZ[k] = z;
                    chain.AxisY[k] = Unit(Vector3d.CrossProduct(z, t));
                }
            }

            /// <summary>
            /// A bar's own local z, squared off <paramref name="x"/>; for one with none, the beam
            /// component's default (Utils.DefaultLocalZ): horizontal, so that local y points up, or
            /// along global X for an upright bar.
            /// </summary>
            private Vector3d OwnZ(int bar, Vector3d x)
            {
                var z = _bars[bar].LocalZ;
                if (z.IsValid)
                {
                    z -= x * (z * x);
                    if (Normalise(ref z)) return z;
                }
                var up = Vector3d.CrossProduct(x, Vector3d.ZAxis).Length < 1e-3 ? Vector3d.XAxis : Vector3d.ZAxis;
                return Unit(Vector3d.CrossProduct(x, up));
            }

            #endregion

            #region The pin-jointed model

            /// <summary>Joints: the ends of members, nodes several members share, and nodes held or tied.</summary>
            private void Joints()
            {
                int nodes = _nodes.Count;
                _on = new List<(int, int)>[nodes];
                _joint = new bool[nodes];

                for (int m = 0; m < _chains.Count; m++)
                {
                    var chain = _chains[m];
                    for (int k = 0; k < chain.Nodes.Count; k++)
                    {
                        int node = chain.Nodes[k];
                        (_on[node] ?? (_on[node] = new List<(int, int)>())).Add((m, k));
                        if (k == 0 || k == chain.Nodes.Count - 1) _joint[node] = true;
                    }
                }

                for (int node = 0; node < nodes; node++)
                    if (_on[node] != null && _on[node].Select(x => x.Member).Distinct().Count() > 1)
                        _joint[node] = true;

                foreach (var r in _restraints) _joint[r.Node] = true;
                foreach (var t in _ties) _joint[t.A] = _joint[t.B] = true;
            }

            /// <summary>
            /// The directions each node of the pin-jointed model moves in, in some mechanism.
            ///
            /// Bars between joints, every bar of a run that is not straight, and the restraints and ties,
            /// each of unit stiffness: only whether a shape stretches something matters, not by how much.
            /// A direction no bar, restraint or tie at a node has any part in is free there by itself,
            /// and is taken out first; that leaves few mechanisms, each found as a zero pivot of one
            /// factorisation and solved for (<see cref="EnvelopeCholesky.FactorSemiDefinite"/>).
            /// </summary>
            private void FreeDirections()
            {
                int nodes = _nodes.Count;
                var inModel = new bool[nodes];
                var links = new List<(int A, int B, Vector3d E)>();

                foreach (var chain in _chains)
                {
                    var joints = Enumerable.Range(0, chain.Nodes.Count).Where(k => _joint[chain.Nodes[k]]).ToList();
                    for (int j = 0; j + 1 < joints.Count; j++)
                    {
                        int a = joints[j], b = joints[j + 1];
                        var chord = P(chain.Nodes[b]) - P(chain.Nodes[a]);
                        bool straight = Normalise(ref chord);
                        for (int k = a; straight && k < b; k++)
                            straight = Unit(P(chain.Nodes[k + 1]) - P(chain.Nodes[k])) * chord >= StraightCos;

                        if (straight)
                            links.Add((chain.Nodes[a], chain.Nodes[b], chord));
                        else
                            for (int k = a; k < b; k++)
                                links.Add((chain.Nodes[k], chain.Nodes[k + 1], Unit(P(chain.Nodes[k + 1]) - P(chain.Nodes[k]))));
                    }
                }
                foreach (var t in _ties) links.Add((t.A, t.B, t.Direction));

                foreach (var l in links) inModel[l.A] = inModel[l.B] = true;
                foreach (var r in _restraints) inModel[r.Node] = true;

                // Each node's own stiffness, and the directions it has some in.
                var block = new double[nodes][,];
                void AddBlock(int node, Vector3d e)
                {
                    var b = block[node] ?? (block[node] = new double[3, 3]);
                    var v = new[] { e.X, e.Y, e.Z };
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++)
                            b[i, j] += v[i] * v[j];
                }
                foreach (var l in links) { AddBlock(l.A, l.E); AddBlock(l.B, l.E); }
                foreach (var r in _restraints) AddBlock(r.Node, r.Direction);

                _free = new List<Vector3d>[nodes];
                var basis = new Vector3d[nodes][];
                var start = new int[nodes];
                int size = 0;
                for (int node = 0; node < nodes; node++)
                {
                    if (!inModel[node]) continue;
                    _free[node] = new List<Vector3d>();
                    BucklingSolver.Jacobi(block[node], 3, out var values, out var vectors);
                    double largest = values.Max();
                    var kept = new List<Vector3d>();
                    for (int i = 0; i < 3; i++)
                    {
                        var v = new Vector3d(vectors[0, i], vectors[1, i], vectors[2, i]);
                        if (values[i] > 1e-9 * largest) kept.Add(v);
                        else _free[node].Add(v);
                    }
                    basis[node] = kept.ToArray();
                    start[node] = size;
                    size += kept.Count;
                }

                if (size == 0) return;
                if (size > LargestSystem)
                {
                    Approximately();
                    return;
                }

                // K, both triangles, in the directions each node has stiffness in.
                var rows = new List<int>();
                var cols = new List<int>();
                var vals = new List<double>();
                double[] Local(int node, Vector3d e) => basis[node].Select(b => b * e).ToArray();
                void Couple(int a, double[] ua, int b, double[] ub, double sign)
                {
                    for (int i = 0; i < ua.Length; i++)
                        for (int j = 0; j < ub.Length; j++)
                        {
                            rows.Add(start[a] + i);
                            cols.Add(start[b] + j);
                            vals.Add(sign * ua[i] * ub[j]);
                        }
                }
                foreach (var l in links)
                {
                    var ua = Local(l.A, l.E);
                    var ub = Local(l.B, l.E);
                    Couple(l.A, ua, l.A, ua, 1.0);
                    Couple(l.B, ub, l.B, ub, 1.0);
                    Couple(l.A, ua, l.B, ub, -1.0);
                    Couple(l.B, ub, l.A, ua, -1.0);
                }
                foreach (var r in _restraints)
                {
                    var u = Local(r.Node, r.Direction);
                    Couple(r.Node, u, r.Node, u, 1.0);
                }

                var stiffness = SparseMatrix.FromEntries(size, rows.ToArray(), cols.ToArray(), vals.ToArray(), rows.Count);
                var zeros = new List<int>();
                EnvelopeCholesky factor;
                try
                {
                    factor = EnvelopeCholesky.FactorSemiDefinite(stiffness, PivotTolerance, zeros);
                }
                catch (InvalidOperationException)
                {
                    Approximately();
                    return;
                }
                if (zeros.Count * (double)factor.EnvelopeSize > LargestWork)
                {
                    Approximately();
                    return;
                }

                var removed = new bool[size];
                foreach (int s in zeros) removed[s] = true;

                // Each mechanism, scaled to move its furthest unknown by one; what it moves each node by,
                // gathered as Σ g gᵀ, whose range is every way the node moves.
                var gram = new double[nodes][,];
                var rhs = new double[size];
                var x = new double[size];
                var scratch = new double[size];
                var owner = new int[size];
                for (int node = 0; node < nodes; node++)
                    if (basis[node] != null)
                        for (int i = 0; i < basis[node].Length; i++)
                            owner[start[node] + i] = node;

                foreach (int s in zeros)
                {
                    Array.Clear(rhs, 0, size);
                    for (int at = stiffness.RowStart[s]; at < stiffness.RowStart[s + 1]; at++)
                    {
                        int c = stiffness.Columns[at];
                        if (!removed[c]) rhs[c] = -stiffness.Values[at];
                    }
                    factor.Solve(rhs, x, scratch);
                    x[s] = 1.0;

                    double largest = 0.0;
                    for (int i = 0; i < size; i++) largest = Math.Max(largest, Math.Abs(x[i]));
                    if (!(largest > 0.0)) continue;

                    int previous = -1;
                    for (int i = 0; i < size; i++)
                    {
                        int node = owner[i];
                        if (node == previous) continue;
                        previous = node;
                        var g = Vector3d.Zero;
                        for (int j = 0; j < basis[node].Length; j++)
                            g += basis[node][j] * (x[start[node] + j] / largest);
                        if (g.SquareLength < 1e-14) continue;
                        var m = gram[node] ?? (gram[node] = new double[3, 3]);
                        var v = new[] { g.X, g.Y, g.Z };
                        for (int p = 0; p < 3; p++)
                            for (int q = 0; q < 3; q++)
                                m[p, q] += v[p] * v[q];
                    }
                }

                for (int node = 0; node < nodes; node++)
                {
                    if (gram[node] == null) continue;
                    BucklingSolver.Jacobi(gram[node], 3, out var values, out var vectors);
                    for (int i = 0; i < 3; i++)
                        if (values[i] > 1e-12)
                            _free[node].Add(new Vector3d(vectors[0, i], vectors[1, i], vectors[2, i]));
                }
            }

            /// <summary>
            /// Too large to look for mechanisms in: each node free but for its restraints, and the ends of
            /// members held, which gives each member its own length.
            /// </summary>
            private void Approximately()
            {
                _approximate = true;
                int nodes = _nodes.Count;
                var held = new List<Vector3d>[nodes];
                foreach (var r in _restraints)
                    (held[r.Node] ?? (held[r.Node] = new List<Vector3d>())).Add(r.Direction);

                _free = new List<Vector3d>[nodes];
                for (int node = 0; node < nodes; node++)
                {
                    bool end = _on[node] != null && _on[node].Any(x => x.At == 0 || x.At == _chains[x.Member].Nodes.Count - 1);
                    if (end)
                    {
                        _free[node] = new List<Vector3d>();
                        continue;
                    }

                    // The directions none of its restraints act in.
                    var block = new double[3, 3];
                    foreach (var d in held[node] ?? new List<Vector3d>())
                    {
                        var v = new[] { d.X, d.Y, d.Z };
                        for (int i = 0; i < 3; i++)
                            for (int j = 0; j < 3; j++)
                                block[i, j] += v[i] * v[j];
                    }
                    BucklingSolver.Jacobi(block, 3, out var values, out var vectors);
                    double largest = values.Max();
                    _free[node] = new List<Vector3d>();
                    for (int i = 0; i < 3; i++)
                        if (!(values[i] > 1e-9 * largest) || largest <= 0.0)
                            _free[node].Add(new Vector3d(vectors[0, i], vectors[1, i], vectors[2, i]));
                }
            }

            /// <summary>How much of <paramref name="d"/> the structure holds at a node, as |P d|²; 0 for one it does not reach.</summary>
            private double HeldShareAt(int node, Vector3d d)
            {
                if (!_joint[node] || _free[node] == null) return 0.0;
                double free = 0.0;
                foreach (var f in _free[node])
                {
                    double c = f * d;
                    free += c * c;
                }
                return 1.0 - free;
            }

            private bool HeldAlong(int node, Vector3d d) => HeldShareAt(node, d) >= HeldShare - 1e-9;

            #endregion

            #region Held points and segments

            private void Hold(int self)
            {
                var chain = _chains[self];
                int n = chain.Nodes.Count;
                chain.HeldY = new HeldBy[n];
                chain.HeldZ = new HeldBy[n];
                for (int k = 0; k < n; k++)
                {
                    int node = chain.Nodes[k];
                    if (!_joint[node]) continue;
                    chain.HeldY[k] = HeldAt(self, node, chain.AxisY[k]);
                    chain.HeldZ[k] = HeldAt(self, node, chain.AxisZ[k]);
                }
            }

            /// <summary>What holds <paramref name="node"/> of member <paramref name="self"/> along <paramref name="d"/>.</summary>
            private HeldBy HeldAt(int self, int node, Vector3d d)
            {
                if (HeldAlong(node, d)) return Design.HeldBy.Structure;

                // On another member, inside it, that runs across d, bends that way about its strong
                // axis, and is held along d on both sides: a secondary beam on a main beam, a purlin on
                // a rafter. Not about its weak axis - a floor beam bent sideways over its whole length
                // holds the columns along it no better than nothing.
                foreach (var (m, at) in _on[node])
                {
                    if (m == self) continue;
                    var other = _chains[m];
                    if (at == 0 || at == other.Nodes.Count - 1) continue;
                    var t = other.Tangent[at];
                    if (Math.Abs(t * d) >= Across) continue;
                    var across = d - t * (t * d);
                    if (!Normalise(ref across)) continue;
                    var strong = _bars[other.Bars[0]].MajorIsY ? other.AxisZ[at] : other.AxisY[at];
                    if ((across * strong) * (across * strong) < HeldShare) continue;

                    bool before = false, after = false;
                    for (int j = 0; j < at && !before; j++) before = HeldAlong(other.Nodes[j], across);
                    for (int j = at + 1; j < other.Nodes.Count && !after; j++) after = HeldAlong(other.Nodes[j], across);
                    if (before && after) return Design.HeldBy.Member;
                }

                return Design.HeldBy.None;
            }

            /// <summary>Whether another member meets the chain at each of its nodes.</summary>
            private bool[] Framed(int self)
            {
                var chain = _chains[self];
                return chain.Nodes.Select(node => _on[node].Any(x => x.Member != self)).ToArray();
            }

            /// <summary>
            /// The pieces between held points. From the last held point to a free end the member is a
            /// cantilever, twice as long - but where other members frame into it on the way, as the
            /// floors of an unbraced frame do into its columns, each piece between them is a storey that
            /// sways with its ends held against turning, twice its own length, rather than all of it
            /// one cantilever from the ground up.
            /// </summary>
            private static List<Segment> Segments(Chain chain, HeldBy[] held, bool[] framed)
            {
                int last = chain.Nodes.Count - 1;
                var s = chain.Stations;
                var at = Enumerable.Range(0, last + 1).Where(k => held[k] != Design.HeldBy.None).ToList();
                var segments = new List<Segment>();
                if (at.Count == 0)
                {
                    segments.Add(new Segment { From = 0, To = last, Length = s[last], Kind = SegmentKind.Unheld });
                    return segments;
                }

                void Overhang(int from, int to)
                {
                    var cuts = new List<int> { from };
                    for (int k = from + 1; k < to; k++)
                        if (framed[k]) cuts.Add(k);
                    cuts.Add(to);
                    var kind = cuts.Count == 2 ? SegmentKind.Free : SegmentKind.Sway;
                    for (int j = 0; j + 1 < cuts.Count; j++)
                        segments.Add(new Segment { From = cuts[j], To = cuts[j + 1], Length = 2.0 * (s[cuts[j + 1]] - s[cuts[j]]), Kind = kind });
                }

                if (at[0] > 0)
                    Overhang(0, at[0]);
                for (int j = 0; j + 1 < at.Count; j++)
                    segments.Add(new Segment { From = at[j], To = at[j + 1], Length = s[at[j + 1]] - s[at[j]], Kind = SegmentKind.Held });
                if (at[at.Count - 1] < last)
                    Overhang(at[at.Count - 1], last);
                return segments;
            }

            private static Segment On(List<Segment> segments, int bar) =>
                segments.FirstOrDefault(x => x.From <= bar && bar < x.To) ?? segments[segments.Count - 1];

            #endregion

            private BucklingLengths Result()
            {
                int count = _bars.Count;
                var aboutY = new double[count];
                var aboutZ = new double[count];
                var lateral = new double[count];
                var memberOf = Enumerable.Repeat(-1, count).ToArray();
                var members = new List<Member>();

                for (int m = 0; m < _chains.Count; m++)
                {
                    var chain = _chains[m];
                    for (int p = 0; p < chain.Bars.Count; p++)
                    {
                        int bar = chain.Bars[p];
                        memberOf[bar] = m;

                        // The member's axes are its first beam's, carried along; a beam turned another
                        // way about its axis takes the restraints across its own y and z.
                        var x = Unit(P(chain.Nodes[p + 1]) - P(chain.Nodes[p]));
                        var ownZ = OwnZ(bar, x);
                        var memberZ = chain.BarZ[p];
                        var memberY = Vector3d.CrossProduct(memberZ, x);
                        bool turned = Math.Abs(ownZ * memberY) > Math.Abs(ownZ * memberZ);

                        double alongY = On(chain.SegmentsY, p).Length;
                        double alongZ = On(chain.SegmentsZ, p).Length;
                        aboutY[bar] = turned ? alongY : alongZ;
                        aboutZ[bar] = turned ? alongZ : alongY;
                        lateral[bar] = _bars[bar].MajorIsY ? aboutZ[bar] : aboutY[bar];
                    }

                    members.Add(new Member
                    {
                        Beams = chain.Bars.AsReadOnly(),
                        Points = chain.Nodes.Select(P).ToList(),
                        Stations = chain.Stations,
                        AxisY = chain.AxisY,
                        AxisZ = chain.AxisZ,
                        HeldY = chain.HeldY,
                        HeldZ = chain.HeldZ,
                        SegmentsY = chain.SegmentsY,
                        SegmentsZ = chain.SegmentsZ,
                        // Its axes are its first beam's own.
                        MajorIsY = _bars[chain.Bars[0]].MajorIsY,
                    });
                }

                return new BucklingLengths
                {
                    AboutY = aboutY,
                    AboutZ = aboutZ,
                    LateralTorsional = lateral,
                    MemberOf = memberOf,
                    Members = members,
                    Approximate = _approximate,
                };
            }
        }

        private static Vector3d Unit(Vector3d v)
        {
            Normalise(ref v);
            return v;
        }

        /// <summary>
        /// <paramref name="v"/> made one long; false, and left as it was, when it has no length. In C#
        /// rather than Vector3d.Unitize, which goes through Rhino's native library: these lengths are
        /// worked out without Rhino too, for the tests.
        /// </summary>
        private static bool Normalise(ref Vector3d v)
        {
            double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (!(length > 1e-300) || double.IsInfinity(length)) return false;
            v = new Vector3d(v.X / length, v.Y / length, v.Z / length);
            return true;
        }
    }
}
