using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    // Model View's elements, and the result painted on them: what SolveInstance builds for the
    // viewport and the Geometry output.
    public partial class ModelView
    {
        // A section's outline, flattened once per solve rather than once per beam that uses it.
        private readonly Dictionary<IUniaxialSection, List<Polyline>> _outlines = new Dictionary<IUniaxialSection, List<Polyline>>();

        // And cut into points to work a stress out at, the same way; see Strips.
        private readonly Dictionary<IUniaxialSection, List<Strip>> _strips = new Dictionary<IUniaxialSection, List<Strip>>();

        // The beams whose stress is painted point by point over the section this solve, by tag.
        private readonly Dictionary<int, Tuple<BeamSectionField, List<Strip>>> _sections = new Dictionary<int, Tuple<BeamSectionField, List<Strip>>>();

        #region Reading

        /// <summary>
        /// What a mode shape is multiplied by on top of the deformation scale, so that at 1 its
        /// largest movement stands off the model as far as the diagrams do (<see cref="Reach"/>).
        /// A vibration or buckling mode has no size of its own - it is read with its largest
        /// translation 1 m - and a metre is a shudder on a bridge and a fold on a bracket. A
        /// displacement of any other analysis is drawn at true size, times the scale.
        /// </summary>
        private double ModeFit()
        {
            if (!_model.IsModal && !_model.IsBuckling)
                return 1.0;

            double largest = _cachedDisplacement.Values.Select(v => v.Length).DefaultIfEmpty(0.0).Max();
            return largest > 0.0 ? Reach() / largest : 1.0;
        }

        /// <summary>The deformation scale's range for a model of each kind, and how many decimals it shows.</summary>
        private static readonly (double Min, double Max, int Decimals) StaticScale = (0.0, 200.0, 1);
        private static readonly (double Min, double Max, int Decimals) ModeShapeScale = (-2.0, 2.0, 2);

        /// <summary>
        /// The deformation scale's range set to suit the model: -2 to 2 for a natural vibration or
        /// buckling model, 0 to 200 for any other. A mode is already fitted to the model at 1
        /// (<see cref="ModeFit"/>) and goes either way - a vibration swings through its rest, and a
        /// buckle, like the imperfection it stands for, is as likely one way as the other - so
        /// Animate sweeps it through zero and out the other side. A displacement has one way to go
        /// and a size of its own.
        ///
        /// Only a slider still on the other kind's range is switched. One given a range of its own -
        /// double-clicked, or saved so in a file - keeps it whatever is wired in.
        /// </summary>
        private void FitScaleRange()
        {
            if (_slDeformScale == null) return;

            bool mode = _model.IsModal || _model.IsBuckling;
            var from = mode ? StaticScale : ModeShapeScale;
            var to = mode ? ModeShapeScale : StaticScale;
            if (_slDeformScale.MinValue != from.Min || _slDeformScale.MaxValue != from.Max)
                return;

            double value = _slDeformScale.Value;
            _slDeformScale.MinValue = to.Min;
            _slDeformScale.MaxValue = to.Max;
            _slDeformScale.NumDecimals = to.Decimals;
            _slDeformScale.Value = value >= to.Min && value <= to.Max ? value : 1.0;

            Attributes?.ExpireLayout();
        }

        /// <summary>Displacement at every node, at true size. Scaled when it is used, not here.</summary>
        private Dictionary<int, Vector3d> NodalOffsets(int step)
        {
            var offsets = new Dictionary<int, Vector3d>();

            foreach (var entry in _model.NodalDisplacements(step))
            {
                if (entry.Key.HasValue)
                    offsets[entry.Key.Value] = entry.Value;
            }

            return offsets;
        }

        /// <summary>
        /// The values a result shows, which is what its range fits: on the elements it colours, or
        /// with <paramref name="everything"/>, on every element it has a value for - nothing is
        /// filtered out and no other result colours any of them. A beam painted over its section
        /// gives every point of it at every integration section: drawn in between, the stress is a
        /// blend of two sections' forces, and none of the values drawn lies beyond the two it is
        /// blended from.
        /// </summary>
        private IEnumerable<double> ShownValues(ShownResult shown, bool everything)
        {
            if (_field.Reactions != null || (everything && _field.ByElement == null))
                return _field.Values;

            if (_field.ByNode != null)
            {
                // A node has no identifier of its own, so it is shown when an element that reaches
                // it is shown. Anything else would filter the displacement field by nothing.
                var nodes = new HashSet<int>();
                foreach (var element in shown.Elements)
                    foreach (var tag in NodesOf(element))
                        nodes.Add(tag);

                return _field.ByNode.Where(entry => nodes.Contains(entry.Key)).Select(entry => entry.Value);
            }

            if (_field.ByElement == null)
                return Enumerable.Empty<double>();

            IEnumerable<KeyValuePair<int, List<double>>> entries = _field.ByElement;
            if (!everything)
            {
                var kept = new HashSet<int>(shown.Elements.Where(x => x.Id.HasValue).Select(x => x.Id.Value));
                entries = entries.Where(entry => kept.Contains(entry.Key));
            }

            return entries.SelectMany(entry => _field.BySection != null && _sections.TryGetValue(entry.Key, out var section)
                ? section.Item1.Forces.SelectMany(forces => section.Item2.SelectMany(strip =>
                      strip.Points.Select(point => section.Item1.At(point, forces, _field.Component))))
                : entry.Value);
        }

        private static IEnumerable<int> NodesOf(IElement element)
        {
            var beam = element as IBeam;
            if (beam != null)
            {
                if (beam.INode.HasValue) yield return beam.INode.Value;
                if (beam.JNode.HasValue) yield return beam.JNode.Value;
                yield break;
            }

            var shell = element as IShell;
            if (shell != null && shell.IndexNodes != null)
            {
                foreach (var tag in shell.IndexNodes.Where(x => x.HasValue)) yield return tag.Value;
                yield break;
            }

            var brick = element as IBrick;
            if (brick != null && brick.IndexNodes != null)
            {
                foreach (var tag in brick.IndexNodes.Where(x => x.HasValue)) yield return tag.Value;
            }
        }

        private Point3d Move(Point3d point, int? node, Dictionary<int, Vector3d> displacement)
        {
            Vector3d offset;
            if (displacement != null && node.HasValue && displacement.TryGetValue(node.Value, out offset))
                return point + offset;

            return point;
        }

        #endregion

        #region Building what gets drawn

        /// <summary>
        /// No result: every element that passed the filter in the colour Colour by gives it, and
        /// the rest ghosted, as a result ghosts them. A shell is drawn as thick as its section with
        /// Extruded on, as Model View always drew it, where a result is painted on its mid-surface.
        /// </summary>
        private void BuildModel(ElementFilter filter, Dictionary<int, Vector3d> displacement)
        {
            foreach (var beam in _model.Beams)
            {
                var start = Move(beam.Curve.PointAtStart, beam.INode, displacement);
                var end = Move(beam.Curve.PointAtEnd, beam.JNode, displacement);

                if (!filter.Matches(beam))
                {
                    Ghost(beam, start, end);
                    continue;
                }

                var colour = _colours.Of(beam);
                Plain(beam, start, end, colour, colour);
            }

            foreach (var shell in _model.Shells)
                BuildModelFace(shell.Mesh, shell.IndexNodes, filter.Matches(shell), displacement, _colours.Of(shell),
                               shell.Section?.Thickness ?? 0.0);

            foreach (var brick in _model.Bricks)
                BuildModelFace(brick.Mesh, brick.IndexNodes, filter.Matches(brick), displacement, _colours.Of(brick), 0.0,
                               Corners(brick));
        }

        /// <summary>
        /// A shell or a solid in one colour. A shell with a thickness, and Extruded on, as the two
        /// halves of its thickness either side of its mid-surface; drawn flat where Rhino cannot
        /// offset it, rather than not at all.
        /// </summary>
        private void BuildModelFace(Mesh source, List<int?> nodes, bool kept, Dictionary<int, Vector3d> displacement,
                                    Color colour, double thickness, int solidCorners = 0)
        {
            if (source == null) return;

            var mesh = Moved(source, nodes, displacement);
            if (!kept)
            {
                _ghostMeshes.Add(mesh);
                return;
            }

            if (thickness > 0.0 && (_ckExtruded?.Active ?? false))
            {
                var solid = new Mesh();
                var top = mesh.Offset(thickness / 2, true);
                var bottom = mesh.Offset(-thickness / 2, true);
                if (top != null) solid.Append(top);
                if (bottom != null) solid.Append(bottom);
                if (solid.Vertices.Count > 0) mesh = solid;
            }

            mesh.VertexColors.CreateMonotoneMesh(colour);
            _shaded.Add(mesh);
            Edges(mesh, nodes, solidCorners);
        }

        /// <summary>
        /// How many of a solid element's nodes are its corners, which come first: a ten node
        /// tetrahedron's six others are on its edges.
        /// </summary>
        private static int Corners(IBrick brick) =>
            brick is Alpaca4d.Element.TenNodeTetrahedron ? 4 : brick.Mesh?.Vertices.Count ?? 0;

        /// <summary>
        /// An element's edges, to draw: a shell's every one, from its mesh; a solid's - one with
        /// <paramref name="corners"/> - only those on the outside of the solid.
        /// </summary>
        private void Edges(Mesh mesh, List<int?> nodes, int corners)
        {
            if (corners > 0 && _solidEdges.Add(mesh, nodes, corners)) return;
            _faceEdges.Add(mesh);
        }

        /// <summary>A copy of an element's mesh, each corner moved by its node's displacement.</summary>
        private Mesh Moved(Mesh source, List<int?> nodes, Dictionary<int, Vector3d> displacement)
        {
            var mesh = source.DuplicateMesh();

            if (displacement != null && nodes != null)
            {
                for (int i = 0; i < mesh.Vertices.Count && i < nodes.Count; i++)
                    mesh.Vertices.SetVertex(i, Move(new Point3d(mesh.Vertices[i]), nodes[i], displacement));
            }

            return mesh;
        }

        /// <summary>
        /// A beam the filter left out: its section see-through grey, or a faint line when it has
        /// no section to draw.
        /// </summary>
        private void Ghost(IBeam beam, Point3d start, Point3d end)
        {
            var ghosts = Rings(beam, start, end, new[] { 0.0, 1.0 });
            if (ghosts == null)
            {
                _ghostCurves.Add(new LineCurve(start, end), Color.FromArgb(90, GhostColour));
                return;
            }

            // Ghosts are drawn shaded rather than in false colour, and shading needs normals.
            foreach (var stack in ghosts)
            {
                var ghost = Alpaca4d.Utils.CreateLoft(stack);
                ghost.Normals.ComputeNormals();
                _ghostMeshes.Add(ghost);
            }
        }

        /// <summary>
        /// Sorts every element into one of three piles: drawn in colour because it passed the
        /// filter and a result ticked colours it (see <see cref="SortElements"/>), drawn plain
        /// because it passed but no result ticked says anything about it, or ghosted. Then the beam
        /// force diagrams over the beams, and the reactions at the supports.
        ///
        /// Ghosting is not optional. The whole point of filtering here rather than upstream is that
        /// the part being read stays in the model around it, and a filter that simply hid the rest
        /// would be the same as wiring a smaller model in.
        /// </summary>
        private void Build(ElementFilter filter, Dictionary<int, Vector3d> displacement)
        {
            // A diagram for each beam force ticked, each in the colours of its force.
            var forces = _shown.Where(x => x.Family == ResultFamily.BeamForce).ToList();

            foreach (var beam in _model.Beams)
            {
                var start = Move(beam.Curve.PointAtStart, beam.INode, displacement);
                var end = Move(beam.Curve.PointAtEnd, beam.JNode, displacement);

                if (!filter.Matches(beam))
                {
                    Ghost(beam, start, end);
                    continue;
                }

                BuildBeam(beam, start, end, Painter(beam));

                foreach (var force in forces)
                {
                    Use(force);
                    BuildDiagram(beam, start, end, force.Component);
                }
            }

            foreach (var shell in _model.Shells)
                BuildFace(shell.Mesh, shell.Id, shell.IndexNodes, filter.Matches(shell), Painter(shell), displacement,
                          midEdge: shell.ElementClass == Alpaca4d.Element.ElementClass.ASDShellT3);

            foreach (var brick in _model.Bricks)
                BuildFace(brick.Mesh, brick.Id, brick.IndexNodes, filter.Matches(brick), Painter(brick), displacement,
                          solidCorners: Corners(brick));

            foreach (var reactions in _shown.Where(x => x.Family == ResultFamily.Reaction))
            {
                Use(reactions);
                BuildReactions(reactions.Component);
            }
        }

        /// <summary>The result an element is coloured by, or null when none is.</summary>
        private ShownResult Painter(IElement element)
        {
            ShownResult painter;
            return _painter.TryGetValue(element, out painter) ? painter : null;
        }

        /// <summary>A beam shown: coloured by the result that colours it, or plain.</summary>
        private void BuildBeam(IBeam beam, Point3d start, Point3d end, ShownResult painter)
        {
            if (painter == null)
            {
                // A beam under a shell or solid result has no value of its own. Drawn plain rather
                // than coloured, because colouring it would mean picking a number it does not have.
                Plain(beam, start, end);
                return;
            }

            Use(painter);
            var family = painter.Family;
            double lift = Lift(beam);

            if (family == ResultFamily.Displacement && _field.ByNode != null)
            {
                // Two nodes and a colour at each, so the beam is drawn as a handful of segments
                // with the colour walked between them. Anything less and a beam reads as one flat
                // colour while the shells beside it are graded.
                double from = ValueAtNode(beam.INode);
                double to = ValueAtNode(beam.JNode);
                const int steps = 8;

                var samples = Enumerable.Range(0, steps + 1)
                                        .Select(i => Tuple.Create(i / (double)steps, from + (to - from) * i / steps))
                                        .ToList();
                Paint(beam, start, end, samples);

                Label((start + end) * 0.5, Math.Max(from, to), lift);
                return;
            }

            if (family == ResultFamily.BeamStress)
            {
                Tuple<BeamSectionField, List<Strip>> section;
                if (beam.Id.HasValue && _sections.TryGetValue(beam.Id.Value, out section)
                    && BuildBeamSection(beam, start, end, section.Item1, section.Item2, lift))
                    return;

                List<double> stresses;
                if (beam.Id.HasValue && _field.ByElement != null && _field.ByElement.TryGetValue(beam.Id.Value, out stresses)
                    && stresses != null && stresses.Count > 0)
                {
                    BuildBeamField(beam, start, end, stresses, lift);
                    return;
                }
            }

            Plain(beam, start, end);
        }

        /// <summary>A beam's force diagram, with the largest force written at its tip.</summary>
        private void BuildDiagram(IBeam beam, Point3d start, Point3d end, int component)
        {
            List<double> forces;
            if (!beam.Id.HasValue || _field.ByElement == null || !_field.ByElement.TryGetValue(beam.Id.Value, out forces))
                return;

            var diagram = Diagram(beam, start, end, forces, component, _diagramUnit);
            if (diagram != null) _diagrams.Add(diagram);

            // One label per beam, at whichever station carries the most. Every station
            // labelled is unreadable on anything but a single member.
            if (forces != null && forces.Count > 0)
            {
                int worst = 0;
                for (int i = 1; i < forces.Count; i++)
                    if (Math.Abs(forces[i]) > Math.Abs(forces[worst])) worst = i;

                // At the tip of the diagram, where the value is drawn, not on the beam.
                var at = start + (end - start) * Stations(beam, forces.Count)[worst];
                Label(at + DiagramDirection(beam, start, end, component) * forces[worst] * _diagramUnit,
                      forces[worst]);
            }
        }

        /// <summary>A beam with no value to show: a grey line, or the same light grey as a face with none.</summary>
        private void Plain(IBeam beam, Point3d start, Point3d end)
        {
            Plain(beam, start, end, Color.FromArgb(210, 210, 210), Color.DimGray);
        }

        /// <summary>A beam in one colour: its section in <paramref name="face"/>, or a line in <paramref name="line"/> when it stays a line.</summary>
        private void Plain(IBeam beam, Point3d start, Point3d end, Color face, Color line)
        {
            if (!Extrude(beam, start, end, new[] { 0.0, 1.0 }, new[] { face, face }))
                _curves.Add(new LineCurve(start, end), line);
        }

        /// <summary>
        /// A value painted along a beam, given at points from 0 at its start to 1 at its end. Drawn
        /// as short lines each in the colour of its middle, or with Extruded beams on, as the beam's
        /// section carried along it with a ring at every point in the colour there.
        ///
        /// With iso-surfaces, as one stretch per band instead, cut where the value crosses into the
        /// next - extruded, two rings at each cut, one in either colour.
        /// </summary>
        private void Paint(IBeam beam, Point3d start, Point3d end, List<Tuple<double, double>> samples)
        {
            var axis = end - start;

            if (_banded)
            {
                var stretches = IsoBands.Along(samples, _min, _max, _palette.Count);
                if (Extrude(beam, start, end, stretches.SelectMany(x => new[] { x.Item1, x.Item2 }).ToList(),
                            stretches.SelectMany(x => new[] { _palette[x.Item3], _palette[x.Item3] }).ToList()))
                    return;

                foreach (var stretch in stretches.Where(x => x.Item2 > x.Item1))
                    _curves.Add(new LineCurve(start + axis * stretch.Item1, start + axis * stretch.Item2),
                                _palette[stretch.Item3]);
                return;
            }

            if (Extrude(beam, start, end, samples.Select(x => x.Item1).ToList(),
                        samples.Select(x => Colour(x.Item2)).ToList()))
                return;

            for (int i = 0; i < samples.Count - 1; i++)
            {
                var segment = new LineCurve(start + axis * samples[i].Item1, start + axis * samples[i + 1].Item1);
                _curves.Add(segment, Colour((samples[i].Item2 + samples[i + 1].Item2) * 0.5));
            }
        }

        /// <summary>
        /// The beam's section carried from start to end with a ring at each of `at`, coloured with
        /// the colour that goes with it. False, and nothing added, when the beam stays a line.
        /// </summary>
        private bool Extrude(IBeam beam, Point3d start, Point3d end, IList<double> at, IList<Color> colours)
        {
            var rings = Rings(beam, start, end, at);
            if (rings == null) return false;

            foreach (var stack in rings)
            {
                var solid = Alpaca4d.Utils.CreateLoft(stack);

                // The loft lays its vertices down a ring at a time, so a vertex's ring is its index
                // over the ring size.
                int perRing = solid.Vertices.Count / stack.Count;
                if (perRing == 0) continue;

                var vertexColours = new Color[solid.Vertices.Count];
                for (int v = 0; v < vertexColours.Length; v++)
                    vertexColours[v] = colours[Math.Min(v / perRing, colours.Count - 1)];

                solid.VertexColors.SetColors(vertexColours);
                _beamSolids.Add(solid);

                _beamEdges.Add(Alpaca4d.Utils.CreateLoft(new List<Polyline> { stack[0], stack[stack.Count - 1] }));
            }

            return true;
        }

        /// <summary>
        /// Every outline of the beam's section stood up at each of `at` along it - one list of
        /// rings per outline, because a hollow section has two and a double angle two apart.
        ///
        /// Square to the line from start to end, by <see cref="LocalAxes"/>, so a section on the
        /// deformed shape stays a section and does not shear with the deformation.
        ///
        /// Null when the beam is to stay a line: Extruded beams is off, or there is no section
        /// outline or local z to stand one up with.
        /// </summary>
        private List<List<Polyline>> Rings(IBeam beam, Point3d start, Point3d end, IList<double> at)
        {
            if (!(_ckExtruded?.Active ?? false)) return null;

            var outlines = Outlines(beam);
            if (outlines == null) return null;

            Vector3d y, z;
            if (!LocalAxes(beam, start, end, out y, out z)) return null;

            // The same frame ExtrudedBeam stands a section in: its x along local z, its y along
            // local y.
            var axis = end - start;

            var rings = new List<List<Polyline>>();
            foreach (var outline in outlines)
            {
                var stack = new List<Polyline>();
                foreach (var t in at)
                {
                    var ring = new Polyline(outline);
                    ring.Transform(Transform.PlaneToPlane(Plane.WorldXY, new Plane(start + axis * t, z, y)));
                    stack.Add(ring);
                }
                rings.Add(stack);
            }

            return rings;
        }

        /// <summary>
        /// The beam's local y and z, the axes its forces are reported in, square to the line from
        /// start to end rather than to the beam as it was - on the deformed shape, a diagram or a
        /// section stays square to the beam that is drawn.
        ///
        /// z is the ZAxis the beam was given, projected off the beam, because OpenSees only asks
        /// that vector to lie in the x-z plane; y is z cross x, as OpenSees makes it. False when
        /// there is no z to start from, or it runs along the beam.
        /// </summary>
        private static bool LocalAxes(IBeam beam, Point3d start, Point3d end, out Vector3d y, out Vector3d z)
        {
            y = Vector3d.Unset;
            z = Vector3d.Unset;
            if (beam.GeomTransf == null) return false;

            var x = end - start;
            if (!x.Unitize()) return false;

            z = beam.GeomTransf.LocalZ;
            z -= x * (z * x);
            if (!z.Unitize()) return false;

            y = Vector3d.CrossProduct(z, x);
            return true;
        }

        /// <summary>
        /// Where each of a beam's sections sits, from 0 at its start to 1 at its end, by the beam's
        /// own integration; evenly spaced when that does not say, or names a different count.
        /// </summary>
        private static IReadOnlyList<double> Stations(IBeam beam, int count)
        {
            IReadOnlyList<double> stations = null;
            if (beam.BeamIntegration != null && beam.Curve != null)
                stations = beam.BeamIntegration.SectionLocations(beam.Curve.PointAtStart.DistanceTo(beam.Curve.PointAtEnd));

            if (stations == null || stations.Count != count)
                stations = Enumerable.Range(0, count)
                                     .Select(i => count > 1 ? i / (double)(count - 1) : 0.5)
                                     .ToList();

            return stations;
        }

        /// <summary>The section's outlines as polylines in its own plane, or null when it has none.</summary>
        private List<Polyline> Outlines(IBeam beam)
        {
            if (beam.Section == null) return null;

            List<Polyline> outlines;
            if (_outlines.TryGetValue(beam.Section, out outlines)) return outlines;

            outlines = (beam.Section.Curves ?? new List<Curve>())
                .Where(x => x != null)
                .Select(x => x.ToPolyline(0, 0, 0, 0)?.ToPolyline())
                .Where(x => x != null && x.Count > 1)
                .ToList();

            if (outlines.Count == 0) outlines = null;
            _outlines[beam.Section] = outlines;
            return outlines;
        }

        /// <summary>How far a label on this beam stands off its axis: clear of the section when it is drawn solid.</summary>
        private double Lift(IBeam beam)
        {
            if (!(_ckExtruded?.Active ?? false)) return 0.0;

            var outlines = Outlines(beam);
            if (outlines == null) return 0.0;

            return outlines.SelectMany(x => x).Select(x => x.DistanceTo(Point3d.Origin)).DefaultIfEmpty(0.0).Max() * 1.2;
        }

        /// <summary>
        /// A value at every integration section, painted along the beam: each section's colour sits
        /// where the section does, and the colour is walked between neighbouring ones.
        ///
        /// Placed by the beam's own integration rather than at equal steps. Newton-Cotes spaces its
        /// sections evenly, but HingeRadau puts one a short way in from each end and two over the
        /// middle, and at equal steps the section just inside a hinge would be drawn a fifth of the
        /// way along the beam rather than where it is.
        /// </summary>
        private void BuildBeamField(IBeam beam, Point3d start, Point3d end, List<double> values, double lift)
        {
            var stations = Stations(beam, values.Count);
            var axis = end - start;

            var samples = Samples(beam, values.Count)
                .Select(x => Tuple.Create(x.At, values[x.From] + (values[x.To] - values[x.From]) * x.Share))
                .ToList();

            Paint(beam, start, end, samples);

            int worst = 0;
            for (int i = 1; i < values.Count; i++)
                if (Math.Abs(values[i]) > Math.Abs(values[worst])) worst = i;

            Label(start + axis * stations[worst], values[worst], lift);
        }

        /// <summary>
        /// Where along a beam to sample something given at its integration sections, from 0 at its
        /// start to 1 at its end, and what to blend there: section From with section To, the Share
        /// of the way from one to the other. Each section where it sits, three more between each
        /// pair so a colour walked between them has stops to walk through, and out to both ends,
        /// which the first and last section need not sit on.
        /// </summary>
        private static List<(double At, int From, int To, double Share)> Samples(IBeam beam, int count)
        {
            var samples = new List<(double, int, int, double)>();

            if (count == 1)
            {
                samples.Add((0.0, 0, 0, 0.0));
                samples.Add((1.0, 0, 0, 0.0));
                return samples;
            }

            var stations = Stations(beam, count);
            var at = new List<double> { 0.0 };
            var section = new List<int> { 0 };
            for (int i = 0; i < count; i++)
            {
                at.Add(stations[i]);
                section.Add(i);
            }
            at.Add(1.0);
            section.Add(count - 1);

            const int steps = 4;
            for (int i = 0; i < at.Count - 1; i++)
            {
                if (at[i + 1] - at[i] <= 0.0) continue;

                for (int k = 0; k < steps; k++)
                {
                    double a = k / (double)steps;
                    samples.Add((at[i] + (at[i + 1] - at[i]) * a, section[i], section[i + 1], a));
                }
            }
            samples.Add((1.0, count - 1, count - 1, 0.0));

            return samples;
        }

        /// <summary>
        /// One edge of a section's outline, cut into short pieces: where each point is in the
        /// section's own drawing - x along local z, y along local y - and what each section force
        /// does there.
        /// </summary>
        private class Strip
        {
            public Point2d[] At;
            public SurfacePoint[] Points;
        }

        /// <summary>
        /// The section's outline as points to work a stress out at, edge by edge, or null when it has
        /// none. Each edge is cut into pieces no longer than a twelfth of the section, so a stress
        /// running across a face has colours to walk through rather than going from one corner's
        /// straight to the other's - the display blends between vertices without passing the
        /// gradient's stops in between.
        ///
        /// An edge apiece rather than a ring round the outline, so a corner has a point on each face
        /// meeting there: the shear running along one face need not be the one running along the
        /// other.
        /// </summary>
        private List<Strip> Strips(IBeam beam, BeamSectionStress shape)
        {
            List<Strip> strips;
            if (_strips.TryGetValue(beam.Section, out strips)) return strips;

            var outlines = Outlines(beam);
            if (outlines != null)
            {
                var box = new BoundingBox(outlines.SelectMany(x => x));
                double longest = box.Diagonal.Length / 12.0;

                strips = new List<Strip>();
                foreach (var outline in outlines)
                {
                    for (int k = 0; k < outline.Count - 1; k++)
                    {
                        var a = outline[k];
                        var b = outline[k + 1];
                        double length = a.DistanceTo(b);
                        if (length <= 0.0) continue;

                        int pieces = longest > 0.0 ? Math.Max(1, Math.Min(24, (int)Math.Ceiling(length / longest))) : 1;
                        var strip = new Strip { At = new Point2d[pieces + 1], Points = new SurfacePoint[pieces + 1] };

                        // The outline is drawn with x along local z and y along local y, and the
                        // stresses are measured from the centroid rather than from the drawing's origin.
                        double ty = (b.Y - a.Y) / length, tz = (b.X - a.X) / length;
                        for (int j = 0; j <= pieces; j++)
                        {
                            var p = a + (b - a) * (j / (double)pieces);
                            strip.At[j] = new Point2d(p.X, p.Y);
                            strip.Points[j] = shape.SurfaceAt(p.Y - shape.CentroidY, p.X - shape.CentroidZ, ty, tz);
                        }

                        strips.Add(strip);
                    }
                }

                if (strips.Count == 0) strips = null;
            }

            _strips[beam.Section] = strips;
            return strips;
        }

        /// <summary>
        /// A beam stress painted point by point over the section: the section carried along the
        /// beam, every point of it coloured with the stress the forces there put at that point.
        /// Bending in two directions and an axial force make one corner of a section the worst and
        /// the opposite one the least; shear peaks on the neutral axis and torsion on the faces.
        ///
        /// Along the beam it is sampled where <see cref="BuildBeamField"/> samples, the forces
        /// blended between neighbouring sections - stress is linear in them, so that is the stress
        /// blended, Von Mises included at each point.
        ///
        /// False, with nothing added, when the beam has no local axes to stand the section up in.
        /// </summary>
        private bool BuildBeamSection(IBeam beam, Point3d start, Point3d end, BeamSectionField section,
                                      List<Strip> strips, double lift)
        {
            Vector3d y, z;
            if (!LocalAxes(beam, start, end, out y, out z)) return false;

            var forces = section.Forces;
            var rings = Samples(beam, forces.Count)
                .Select(x => Tuple.Create(x.At, forces[x.From].Zip(forces[x.To], (f, t) => f + (t - f) * x.Share).ToArray()))
                .ToList();

            var axis = end - start;
            var mesh = new Mesh();
            var values = new List<double>();
            int worst = -1;
            double worstAt = 0.0;

            foreach (var strip in strips)
            {
                int first = mesh.Vertices.Count;
                int count = strip.At.Length;

                foreach (var ring in rings)
                {
                    // The same frame ExtrudedBeam stands a section in: its x along local z, its y along local y.
                    var origin = start + axis * ring.Item1;
                    for (int j = 0; j < count; j++)
                    {
                        mesh.Vertices.Add(origin + z * strip.At[j].X + y * strip.At[j].Y);
                        double value = section.At(strip.Points[j], ring.Item2, _field.Component);
                        if (worst < 0 || Math.Abs(value) > Math.Abs(values[worst]))
                        {
                            worst = values.Count;
                            worstAt = ring.Item1;
                        }
                        values.Add(value);
                    }
                }

                for (int r = 0; r < rings.Count - 1; r++)
                {
                    for (int j = 0; j < count - 1; j++)
                    {
                        int a = first + r * count + j;
                        mesh.Faces.AddFace(a, a + 1, a + 1 + count, a + count);
                    }
                }
            }

            if (_banded)
            {
                _beamSolids.Add(IsoBands.Paint(mesh, values, _palette, _min, _max));
            }
            else
            {
                mesh.VertexColors.SetColors(values.Select(Colour).ToArray());
                _beamSolids.Add(mesh);
            }

            // The outline at each end and the lines along the corners, as any solid beam has.
            var ends = Rings(beam, start, end, new[] { 0.0, 1.0 });
            if (ends != null)
                foreach (var stack in ends)
                    _beamEdges.Add(Alpaca4d.Utils.CreateLoft(stack));

            if (worst >= 0)
                Label(start + axis * worstAt, values[worst], lift);

            return true;
        }

        /// <summary>A shell or a solid: ghosted, coloured by the result that colours it, or plain.</summary>
        private void BuildFace(Mesh source, int? tag, List<int?> nodes, bool kept, ShownResult painter,
                               Dictionary<int, Vector3d> displacement, bool midEdge = false, int solidCorners = 0)
        {
            if (source == null) return;

            var mesh = Moved(source, nodes, displacement);

            if (!kept)
            {
                _ghostMeshes.Add(mesh);
                return;
            }

            mesh.VertexColors.Clear();
            Edges(mesh, nodes, solidCorners);

            double label = 0.0;
            double[] corners = null;

            var family = painter?.Family;
            if (painter != null)
                Use(painter);

            if (family == ResultFamily.Displacement && _field.ByNode != null && nodes != null)
            {
                corners = new double[mesh.Vertices.Count];
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    corners[i] = ValueAtNode(i < nodes.Count ? nodes[i] : null);
                    if (Math.Abs(corners[i]) > Math.Abs(label)) label = corners[i];
                }
            }
            else if (family != null && family != ResultFamily.Displacement && tag.HasValue && _field.ByElement != null && _field.ByElement.ContainsKey(tag.Value))
            {
                // One value per integration point, each painted on the corner it sits by - a
                // quad's point i is next to its node i. A face with more corners than it has
                // stations reuses the last.
                //
                // An ASDShellT3's three points are not by its corners but at the middles of its
                // edges, point i on the edge opposite node i, so a corner takes the mean of the two
                // on the edges that meet at it. Painted point i on corner i, every value sat at the
                // far side of the triangle from where it was read.
                var values = _field.ByElement[tag.Value];
                bool edges = midEdge && values.Count == 3 && mesh.Vertices.Count == 3;
                corners = new double[mesh.Vertices.Count];
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    corners[i] = edges
                        ? (values[0] + values[1] + values[2] - values[i]) * 0.5
                        : values[Math.Min(i, values.Count - 1)];
                }

                foreach (var value in values)
                    if (Math.Abs(value) > Math.Abs(label)) label = value;
            }

            if (corners == null)
            {
                // Passed the filter, but no result ticked has anything to say about it.
                mesh.VertexColors.CreateMonotoneMesh(Color.FromArgb(210, 210, 210));
                _shaded.Add(mesh);
                return;
            }

            if (_banded)
            {
                _shaded.Add(IsoBands.Paint(mesh, corners, _palette, _min, _max));
            }
            else
            {
                foreach (var value in corners)
                    mesh.VertexColors.Add(Colour(value));
                _shaded.Add(mesh);
            }

            Label(mesh.GetBoundingBox(false).Center, label);
        }

        /// <summary>
        /// The reaction at every support, drawn the way the Reactions dropdown asks for.
        ///
        /// The first cut always drew the resultant, whichever component was chosen. On a model with
        /// any horizontal reaction that means picking Fz and getting an arrow pointing off at an
        /// angle - right magnitude in the Values output, wrong picture. An arrow that says Fz now
        /// runs along z and nowhere else, and carries the sign, so a downward reaction points down.
        /// </summary>
        private void BuildReactions(int component)
        {
            if (_field.Reactions == null || _field.Reactions.Count == 0) return;

            int style = _ddReaction?.Value ?? 0;

            double largest = _field.Reactions.Select(x => Math.Abs(x.Item3)).DefaultIfEmpty(0.0).Max();
            if (largest <= 0.0) return;

            double scale = Reach() / largest * (_slArrowScale?.Value ?? 1.0);

            // A moment is not a force, and pointing an arrow along one is already a convention
            // rather than a picture. It is at least drawn from the same axes.
            bool moment = component >= 4;

            foreach (var reaction in _field.Reactions)
            {
                var plane = reaction.Item1;
                var local = reaction.Item2;
                var axes = new[] { plane.XAxis, plane.YAxis, plane.ZAxis };
                var parts = new[] { local.X, local.Y, local.Z };

                if (style == 2)
                {
                    // One arrow per axis, so a support carrying load three ways shows all three.
                    for (int i = 0; i < 3; i++)
                        Arrow(plane.Origin, axes[i] * parts[i] * scale, parts[i], moment);
                }
                else if (style == 1 || component % 4 == 0)
                {
                    // The resultant, which is also what "Force" and "Moment" mean.
                    var world = axes[0] * local.X + axes[1] * local.Y + axes[2] * local.Z;
                    Arrow(plane.Origin, world * scale, world.Length, moment);
                }
                else
                {
                    // The one component the dropdown names, along its own axis and with its sign.
                    int axis = component % 4 - 1;
                    Arrow(plane.Origin, axes[axis] * parts[axis] * scale, parts[axis], moment);
                }
            }
        }

        /// <summary>
        /// One arrow from a support. Drawn outwards along the reaction, which OpenSees reports as
        /// the support's action on the structure - a downward load on a cantilever gives an upward
        /// reaction, measured, so an upward arrow is the picture an engineer expects.
        /// </summary>
        private void Arrow(Point3d at, Vector3d offset, double value, bool moment)
        {
            if (offset.Length <= 0.0) return;

            var line = new Line(at, at + offset);
            _arrows.Add(Tuple.Create(line, Colour(Math.Abs(value))));

            // A double head marks a moment, the usual way of telling one from a force.
            if (moment)
                _arrows.Add(Tuple.Create(new Line(at + offset * 0.75, at + offset * 0.95), Colour(Math.Abs(value))));

            Label(line.To, value);
        }

        /// <summary>The way a positive value of a beam force is drawn; see <see cref="Diagram"/>.</summary>
        private static Vector3d DiagramDirection(IBeam beam, Point3d start, Point3d end, int component)
        {
            Vector3d y, z;
            if (!LocalAxes(beam, start, end, out y, out z)) return Vector3d.ZAxis;

            return component == 2 || component == 4 ? z : component == 5 ? -y : y;
        }

        /// <summary>
        /// The length one unit of beam force is drawn at: the largest force on the beams that passed
        /// the filter stands off the model as far as the largest reaction arrow does.
        ///
        /// One scale for the whole model. Each beam used to be fitted to its own largest force, so
        /// every element of a beam split into ten drew the same height - the end ones carrying the
        /// full shear and the middle ones a tenth of it - and the diagram climbed in a sawtooth
        /// the numbers beside it did not.
        /// </summary>
        private double DiagramUnit(ElementFilter filter)
        {
            if (_field.ByElement == null) return 0.0;

            double largest = _model.Beams
                .Where(beam => beam.Id.HasValue && filter.Matches(beam) && _field.ByElement.ContainsKey(beam.Id.Value))
                .SelectMany(beam => _field.ByElement[beam.Id.Value])
                .Select(Math.Abs)
                .DefaultIfEmpty(0.0)
                .Max();

            return largest > 0.0 ? Reach() / largest * (_slDiagramScale?.Value ?? 1.0) : 0.0;
        }

        /// <summary>
        /// How far the largest diagram or reaction arrow stands off the model, at a scale of 1: a fixed
        /// share of the model's size, which keeps it readable whether the model is a bracket or a
        /// bridge, and the two the same length when both are drawn.
        /// </summary>
        private double Reach()
        {
            var box = _model.UniquePoints != null && _model.UniquePoints.Count > 0
                ? new BoundingBox(_model.UniquePoints)
                : BoundingBox.Unset;

            return box.IsValid && box.Diagonal.Length > 0.0 ? box.Diagonal.Length * 0.08 : 1.0;
        }

        /// <summary>A value to write on the model, when Show values is ticked; see <see cref="_labels"/>.</summary>
        private void Label(Point3d at, double value, double lift = 0.0)
        {
            _labels.Add(Tuple.Create(at, value, lift));
        }

        private double ValueAtNode(int? node)
        {
            double value;
            if (node.HasValue && _field.ByNode != null && _field.ByNode.TryGetValue(node.Value, out value))
                return value;

            return _min;
        }

        private Color Colour(double value)
        {
            if (_banded && _palette != null && _palette.Count > 0)
                return _palette[ColourScale.Band(value, _min, _max, _palette.Count)];

            return _gradient != null && _gradient.Count > 0
                ? Alpaca4d.Colors.GetColor(value, _gradient)
                : Color.Gray;
        }

        /// <summary>
        /// A force diagram: the values stood off the beam in the plane the force acts in, closed
        /// back onto it at both ends so the area reads as the diagram it is.
        ///
        /// Vy and Mz along local y, Vz and My along local z - Mz bends the beam in its x-y plane
        /// and My in its x-z one. N and Torsion act in no plane of their own and go in the x-y
        /// one, the plane of the strong-axis diagrams. Drawing all six along z, as the first cut
        /// did, put Vy and Mz in the plane at right angles to the one they belong to.
        ///
        /// Moments on the side they put in tension. A positive My stretches the +z side, so it
        /// is drawn towards +z; a positive Mz compresses the +y side, so it is drawn towards -y -
        /// on a beam with local y up, sagging hangs below it. The others are drawn towards the
        /// positive axis.
        ///
        /// <paramref name="unit"/> is the length one unit of force is drawn at, the same for every
        /// beam - see <see cref="DiagramUnit"/>.
        /// </summary>
        private static Mesh Diagram(IBeam beam, Point3d start, Point3d end, List<double> forces,
                                    int component, double unit)
        {
            if (forces == null || forces.Count < 2 || unit <= 0.0) return null;
            if (forces.All(x => x == 0.0)) return null;

            var offset = DiagramDirection(beam, start, end, component);

            var stations = Stations(beam, forces.Count);
            var mesh = new Mesh();
            var colours = new List<Color>();
            var axis = end - start;

            int Vertex(Point3d at, Color colour)
            {
                mesh.Vertices.Add(at);
                colours.Add(colour);
                return mesh.Vertices.Count - 1;
            }

            for (int i = 0; i < forces.Count - 1; i++)
            {
                var a = start + axis * stations[i];
                var b = start + axis * stations[i + 1];
                double fa = forces[i];
                double fb = forces[i + 1];
                var ca = ResultField.BeamForceColour(component, fa);
                var cb = ResultField.BeamForceColour(component, fb);

                if (fa * fb < 0.0)
                {
                    // Changing sign between the two: closed onto the beam where it crosses zero, as
                    // a triangle either side. One quad would fold over itself across the beam.
                    var zero = a + (b - a) * (fa / (fa - fb));
                    mesh.Faces.AddFace(Vertex(a, ca), Vertex(zero, ca), Vertex(a + offset * fa * unit, ca));
                    mesh.Faces.AddFace(Vertex(zero, cb), Vertex(b, cb), Vertex(b + offset * fb * unit, cb));
                }
                else
                {
                    mesh.Faces.AddFace(Vertex(a, ca), Vertex(b, cb),
                                       Vertex(b + offset * fb * unit, cb), Vertex(a + offset * fa * unit, ca));
                }
            }

            mesh.VertexColors.SetColors(colours.ToArray());
            mesh.Normals.ComputeNormals();

            return mesh;
        }

        #endregion

    }
}
