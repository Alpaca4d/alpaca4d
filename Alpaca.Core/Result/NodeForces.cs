using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Element;
using Alpaca4d.Generic;

namespace Alpaca4d.Result
{
    /// <summary>
    /// Which beams meet at which node: the model as a graph, its nodes the vertices and every beam
    /// an edge between its two end nodes.
    ///
    /// Held as compressed adjacency lists. One pass counts the beam ends at each node, a running
    /// sum turns the counts into where each node's ends begin, and a second pass files every end in
    /// its node's run of one flat array. Building it costs a step per beam whatever the model, and
    /// asking for a node's beams costs only as many steps as there are beams there. The obvious way
    /// round - every beam tested against every node asked for - grows with the product of the two,
    /// which is what makes it crawl over all the nodes of a large model.
    ///
    /// Indexed by node tag rather than hashed. Assemble hands tags out from one with no gaps, so
    /// an array as long as the largest tag is as small as a dictionary would be, and faster to
    /// read.
    /// </summary>
    public sealed class NodeGraph
    {
        /// <summary>By node tag: where its beam ends begin in <see cref="_beam"/>. The next tag's entry is where they stop.</summary>
        private readonly int[] _start;

        /// <summary>Each beam end, grouped by node: the beam's place in Model.Beams.</summary>
        private readonly int[] _beam;

        /// <summary>Alongside <see cref="_beam"/>: whether that end is the beam's J end.</summary>
        private readonly bool[] _atJ;

        /// <summary>By node tag: how many elements other than beams reach it - shells, solids, links.</summary>
        private readonly int[] _others;

        /// <summary>By node tag: where it stands, and whether the model has a node with that tag at all.</summary>
        private readonly Point3d[] _position;
        private readonly bool[] _known;

        /// <summary>The nodes a beam meets, by where they stand, for a point asked for. Built when first needed.</summary>
        private NodeIndex _index;
        private double _indexTolerance = double.NaN;

        /// <summary>The tags of the nodes at least one beam meets, smallest first.</summary>
        public IReadOnlyList<int> BeamNodes { get; }

        private NodeGraph(int[] start, int[] beam, bool[] atJ, int[] others, Point3d[] position, bool[] known)
        {
            _start = start;
            _beam = beam;
            _atJ = atJ;
            _others = others;
            _position = position;
            _known = known;

            var beamNodes = new List<int>();
            for (int tag = 1; tag + 1 < start.Length; tag++)
                if (start[tag + 1] > start[tag])
                    beamNodes.Add(tag);
            BeamNodes = beamNodes;
        }

        /// <summary>The graph of an assembled model: the end nodes Assemble gave every beam, and the nodes it made.</summary>
        public static NodeGraph Of(Model model)
        {
            var beams = model.Beams ?? new List<IBeam>();
            var nodes = model.Nodes ?? new List<Node>();
            var elements = model.Elements ?? new List<IElement>();

            int largest = 0;
            foreach (var node in nodes)
                largest = Math.Max(largest, node.Id ?? 0);
            foreach (var beam in beams)
                largest = Math.Max(largest, Math.Max(beam.INode ?? 0, beam.JNode ?? 0));
            foreach (var element in elements)
                foreach (int tag in OtherNodes(element))
                    largest = Math.Max(largest, tag);

            // One past the largest tag, and one more so that the last tag's run has an end.
            var start = new int[largest + 2];
            foreach (var beam in beams)
            {
                if (beam.INode > 0) start[beam.INode.Value + 1]++;
                if (beam.JNode > 0) start[beam.JNode.Value + 1]++;
            }
            for (int tag = 1; tag < start.Length; tag++)
                start[tag] += start[tag - 1];

            var beamOf = new int[start[start.Length - 1]];
            var atJ = new bool[beamOf.Length];
            var next = (int[])start.Clone();
            for (int i = 0; i < beams.Count; i++)
            {
                if (beams[i].INode > 0)
                {
                    int slot = next[beams[i].INode.Value]++;
                    beamOf[slot] = i;
                }
                if (beams[i].JNode > 0)
                {
                    int slot = next[beams[i].JNode.Value]++;
                    beamOf[slot] = i;
                    atJ[slot] = true;
                }
            }

            var others = new int[largest + 2];
            foreach (var element in elements)
                foreach (int tag in OtherNodes(element))
                    others[tag]++;

            var position = new Point3d[largest + 2];
            var known = new bool[largest + 2];
            foreach (var node in nodes)
            {
                if (!(node.Id > 0)) continue;
                position[node.Id.Value] = node.Pos;
                known[node.Id.Value] = true;
            }

            return new NodeGraph(start, beamOf, atJ, others, position, known);
        }

        /// <summary>
        /// The nodes an element other than a beam reaches. Beams are counted in the graph itself, and
        /// anything else meeting a node carries force into it that no beam's ends account for.
        /// </summary>
        private static IEnumerable<int> OtherNodes(IElement element)
        {
            switch (element)
            {
                case IBeam _:
                    break;
                case IShell shell when shell.IndexNodes != null:
                    foreach (var tag in shell.IndexNodes.Where(tag => tag > 0).Distinct())
                        yield return tag.Value;
                    break;
                case IBrick brick when brick.IndexNodes != null:
                    foreach (var tag in brick.IndexNodes.Where(tag => tag > 0).Distinct())
                        yield return tag.Value;
                    break;
                case Link link:
                    if (link.INode > 0) yield return link.INode.Value;
                    if (link.JNode > 0) yield return link.JNode.Value;
                    break;
                case ZeroLengthSection zeroLength:
                    if (zeroLength.INode > 0) yield return zeroLength.INode.Value;
                    if (zeroLength.JNode > 0) yield return zeroLength.JNode.Value;
                    break;
            }
        }

        /// <summary>Whether the model has a node with this tag.</summary>
        public bool Has(int tag) => tag > 0 && tag < _known.Length && _known[tag];

        /// <summary>Where the node with this tag stands. Only meaningful when <see cref="Has"/> says it is there.</summary>
        public Point3d PositionOf(int tag) => Has(tag) ? _position[tag] : Point3d.Unset;

        /// <summary>How many beam ends meet at the node.</summary>
        public int Degree(int tag) => tag > 0 && tag + 1 < _start.Length ? _start[tag + 1] - _start[tag] : 0;

        /// <summary>How many elements other than beams reach the node.</summary>
        public int OthersAt(int tag) => tag > 0 && tag < _others.Length ? _others[tag] : 0;

        /// <summary>The beam ends at the node, in the order of Model.Beams: each beam's place there, and whether it is its J end.</summary>
        public IEnumerable<(int Beam, bool AtJ)> EndsAt(int tag)
        {
            if (Degree(tag) == 0)
                yield break;

            for (int slot = _start[tag]; slot < _start[tag + 1]; slot++)
                yield return (_beam[slot], _atJ[slot]);
        }

        /// <summary>
        /// Every beam that meets one of these nodes, by its place in Model.Beams: what has to be read
        /// from the recorder file to report them, and on a large model a small part of it.
        /// </summary>
        public HashSet<int> BeamsAt(IEnumerable<int> tags)
        {
            var beams = new HashSet<int>();
            foreach (int tag in tags)
                foreach (var (beam, _) in EndsAt(tag))
                    beams.Add(beam);
            return beams;
        }

        /// <summary>
        /// The tag of the node a beam meets nearest <paramref name="point"/>, within
        /// <paramref name="tolerance"/>; 0 when there is none.
        ///
        /// Only nodes a beam meets are searched, so a point picked on a beam end finds that node
        /// even where a solid's node stands at the same place. The tolerance should be the
        /// model's own, the distance at which Assemble made two points one node.
        /// </summary>
        public int NodeAt(Point3d point, double tolerance)
        {
            if (!point.IsValid)
                return 0;

            // A tolerance of zero would make this an exact-equality test on floating point, which
            // no point picked off the screen would pass: the default Assemble offers instead.
            if (!(tolerance > 0.0))
                tolerance = 0.01;

            if (_index == null || _indexTolerance != tolerance)
            {
                _index = new NodeIndex(tolerance);
                foreach (int tag in BeamNodes)
                    _index.Add(_position[tag]);
                _indexTolerance = tolerance;
            }

            int found = _index.Nearest(point);
            return found >= 0 ? BeamNodes[found] : 0;
        }
    }

    /// <summary>One beam's end at a node, and what it carries there.</summary>
    public struct MemberEnd
    {
        /// <summary>The beam's place in Model.Beams.</summary>
        public int Beam;

        /// <summary>Whether the node is at the beam's J end. Its local x then points into the node rather than away.</summary>
        public bool AtJ;

        /// <summary>The section forces at that end, signed and in the axes Beam Forces reports them in.</summary>
        public double N, Vy, Vz, Mx, My, Mz;

        /// <summary>The force and the moment the beam puts on the node, in global axes.</summary>
        public Vector3d Force, Moment;
    }

    /// <summary>
    /// The forces every beam brings into a node, for designing the joint there.
    ///
    /// Taken from the section forces at the beams' end stations, which is where both integrations
    /// Alpaca4d writes put a section: Newton-Cotes samples the ends, and so does HingeRadau. A
    /// force-based element's section forces come out of equilibrium with its end forces and its
    /// element loads, so those at an end are the end forces exactly, distributed loads and all.
    /// Read this way the numbers are the first or last ones Beam Forces gives for the same beam, and
    /// no result has to be recorded that is not recorded already.
    /// </summary>
    public static class NodeForces
    {
        /// <summary>
        /// What a beam puts on its end node, in global axes, from the section forces at that end.
        ///
        /// On the face of a cut whose outward normal is +x the section forces act as a force
        /// (N, -Vy, Vz) and a moment (Mx, My, Mz) in local axes. The shear along y changes sign
        /// because OpenSees takes Vy as dMz/dx (see Beam Forces). The beam's I end is a face looking
        /// along -x, so the node holds the beam there with minus that vector and the beam puts the
        /// vector itself on the node. The J end looks along +x: the node holds the beam with the
        /// vector and the beam puts minus it on the node. Checked against OpenSees' own globalForce,
        /// element loads included.
        ///
        /// <paramref name="localX"/> runs from the I end to the J end. <paramref name="localZ"/> is
        /// the beam's local z, squared off <paramref name="localX"/> here in case it is not
        /// already, and local y is z × x, as GeomTransf has it.
        /// </summary>
        public static void OnNode(Vector3d localX, Vector3d localZ, bool atJ,
                                  double n, double vy, double vz, double mx, double my, double mz,
                                  out Vector3d force, out Vector3d moment)
        {
            var x = Unit(localX);
            var z = Unit(localZ - x * (localZ * x));
            var y = Vector3d.CrossProduct(z, x);

            double sign = atJ ? -1.0 : 1.0;
            force = sign * (n * x - vy * y + vz * z);
            moment = sign * (mx * x + my * y + mz * z);
        }

        /// <summary>
        /// The vector at length one, or zero when it has none. By hand rather than Vector3d.Unitize,
        /// which goes through Rhino's native library and so cannot run outside Rhino.
        /// </summary>
        private static Vector3d Unit(Vector3d vector)
        {
            double length = vector.Length;
            return length > 0.0 ? vector / length : Vector3d.Zero;
        }

        /// <summary>
        /// The beam ends at node <paramref name="tag"/> and what each carries, in the order of
        /// Model.Beams. <paramref name="forces"/> is Read.ForceBeamColumn at one step, a list per beam
        /// in the same order.
        ///
        /// <paramref name="displacement"/>, by node tag, is where the nodes have moved to at the same
        /// step, from <see cref="Displacements"/>. A truss is always corotational, so its force runs
        /// along the bar where it has moved to, and a cable that sags under its load pulls along the
        /// sag rather than along the line it was drawn on. Beams are written with a Linear
        /// transformation, which keeps the axes they started with, so their forces are turned with
        /// those whatever the displacements. Null takes trusses along their undeformed axes too.
        ///
        /// A beam the recorder file holds no forces for is left out and counted in
        /// <paramref name="unread"/>, so a caller can say so rather than show a joint that does not
        /// balance.
        /// </summary>
        public static List<MemberEnd> At(NodeGraph graph, int tag, IReadOnlyList<IBeam> beams,
            (List<List<double>> n, List<List<double>> mz, List<List<double>> vy, List<List<double>> my, List<List<double>> vz, List<List<double>> t) forces,
            IReadOnlyList<Vector3d> displacement, out int unread)
        {
            var ends = new List<MemberEnd>(graph.Degree(tag));
            unread = 0;

            foreach (var (b, atJ) in graph.EndsAt(tag))
            {
                var n = forces.n[b];
                if (n.Count == 0)
                {
                    unread++;
                    continue;
                }

                int k = atJ ? n.Count - 1 : 0;
                var end = new MemberEnd
                {
                    Beam = b,
                    AtJ = atJ,
                    N = n[k],
                    Vy = forces.vy[b][k],
                    Vz = forces.vz[b][k],
                    Mx = forces.t[b][k],
                    My = forces.my[b][k],
                    Mz = forces.mz[b][k],
                };

                var beam = beams[b];
                int i = beam.INode ?? 0, j = beam.JNode ?? 0;
                var localX = graph.PositionOf(j) - graph.PositionOf(i);
                if (beam is Truss && displacement != null && i < displacement.Count && j < displacement.Count)
                    localX += displacement[j] - displacement[i];
                var localZ = beam.GeomTransf?.LocalZ ?? Vector3d.Zero;

                OnNode(localX, localZ, atJ, end.N, end.Vy, end.Vz, end.Mx, end.My, end.Mz,
                       out end.Force, out end.Moment);
                ends.Add(end);
            }

            return ends;
        }

        /// <summary>
        /// Where every node has moved to at <paramref name="step"/>, by tag, for the trusses
        /// <see cref="At"/> turns along their displaced axes. Null when the model has no trusses,
        /// which need nothing of it, or when the recorder file holds no displacements, in which case
        /// trusses are taken along the line they were drawn on.
        /// </summary>
        public static Vector3d[] Displacements(Model model, int step)
        {
            if (!model.Beams.Any(beam => beam is Truss) || !Read.Holds(model, ResultType.DISPLACEMENT))
                return null;

            // Read.NodalOutput gives a row per node in tag order, from one.
            var rows = Read.NodalOutput(model, step, ResultType.DISPLACEMENT).ToList();
            var byTag = new Vector3d[rows.Count + 1];
            for (int k = 0; k < rows.Count; k++)
                byTag[k + 1] = rows[k];
            return byTag;
        }
    }
}
