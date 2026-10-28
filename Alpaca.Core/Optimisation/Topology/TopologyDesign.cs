using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Loads;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// What a topology optimisation leaves, as a model of its own to analyse: the solids of the design at
    /// their full stiffness, the model's other elements, and the supports and loads that still stand on
    /// something.
    ///
    /// The model was assembled once already, so it is the one asked where everything is. The cells are
    /// the run's own, in the order of the model's solids, and their node tags those of that assembly.
    /// </summary>
    public static class TopologyDesign
    {
        /// <summary>The structure among the cells at or above <paramref name="threshold"/>: see <see cref="SolidPieces"/>.</summary>
        public static SolidPieces Pieces(Model model, IReadOnlyList<SolidCell> cells, IReadOnlyList<double> densities, double threshold)
        {
            var solid = Enumerable.Range(0, cells.Count).Select(i => cells[i].Passive || densities[i] >= threshold).ToArray();
            return SolidPieces.Find(cells, solid, tag => model.Nodes[tag - 1].Pos, Restraints(model), Attached(model, cells), densities);
        }

        /// <summary>Each direction a support holds a solid node in, in the support's own frame; a spring holds as a support does.</summary>
        public static List<(int node, Vector3d direction)> Restraints(Model model)
        {
            var restraints = new List<(int, Vector3d)>();
            foreach (var support in model.Supports)
            {
                int tag = SolidNode(model, support);
                if (tag == 0) continue;

                var plane = support.Plane.IsValid ? support.Plane : Plane.WorldXY;
                var axes = new[] { plane.XAxis, plane.YAxis, plane.ZAxis };
                if (support.Springs != null && support.Springs.Count > 0)
                {
                    foreach (var spring in support.Springs)
                        if (spring.Direction >= 0 && spring.Direction < 3)
                            restraints.Add((tag, axes[spring.Direction]));
                }
                else
                {
                    if (support.Tx) restraints.Add((tag, axes[0]));
                    if (support.Ty) restraints.Add((tag, axes[1]));
                    if (support.Tz) restraints.Add((tag, axes[2]));
                }
            }
            return restraints;
        }

        /// <summary>
        /// The solid nodes the model's other elements stand on: a beam's ends, a shell's vertices. A link
        /// says nothing of where it is, so with a link in the model every solid node counts, and no piece
        /// is left out.
        /// </summary>
        public static HashSet<int> Attached(Model model, IReadOnlyList<SolidCell> cells)
        {
            var attached = new HashSet<int>();
            void At(Point3d p)
            {
                int index = model.NodeIndexThreeNDF.Nearest(p);
                if (index >= 0) attached.Add(index + 1);
            }

            foreach (var element in model.Elements)
            {
                switch (element)
                {
                    case IBrick _:
                        break;
                    case IBeam beam:
                        At(beam.Curve.PointAtStart);
                        At(beam.Curve.PointAtEnd);
                        break;
                    case IShell shell:
                        foreach (var vertex in shell.Mesh.Vertices.ToPoint3dArray())
                            At(vertex);
                        break;
                    default:
                        attached.UnionWith(cells.SelectMany(cell => cell.Nodes));
                        return attached;
                }
            }
            return attached;
        }

        /// <summary>
        /// The design as a model, assembled: the solids in <paramref name="kept"/> - copies, so that
        /// assembling them does not renumber the optimised model's own - the model's other elements, and
        /// its supports, masses and point loads where something is left to stand on. Recorders are left
        /// behind: they belong to the analysis of the model optimised.
        /// </summary>
        /// <param name="supportsLeftOut">Supports that stood on solids left out, the void the design opens.</param>
        /// <param name="loadsLeftOut">Point loads and masses that did.</param>
        public static Model Build(Model model, IReadOnlyList<SolidCell> cells, IReadOnlyList<bool> kept, out int supportsLeftOut, out int loadsLeftOut)
        {
            var bricks = model.Bricks;
            if (cells.Count != bricks.Count || kept.Count != bricks.Count)
                throw new ArgumentException($"{cells.Count} cells and {kept.Count} flags for {bricks.Count} solids.");

            var keptBricks = new HashSet<IBrick>(Enumerable.Range(0, bricks.Count).Where(i => kept[i]).Select(i => bricks[i]));
            var solidNodes = new HashSet<int>(cells.SelectMany(cell => cell.Nodes));
            var keptNodes = new HashSet<int>(Enumerable.Range(0, cells.Count).Where(i => kept[i]).SelectMany(i => cells[i].Nodes));

            // A point stands on something when its node is one of a beam or shell, or of no solid at all,
            // or of a solid kept.
            bool Stands(Point3d p)
            {
                int tag = model.NodeTagNear(p, out int ndf);
                return tag > 0 && (ndf == 6 || !solidNodes.Contains(tag) || keptNodes.Contains(tag));
            }

            var elements = new List<IElement>();
            foreach (var element in model.Elements)
            {
                if (!(element is IBrick brick))
                    elements.Add(element);
                else if (keptBricks.Contains(brick))
                    elements.Add(Copy(brick));
            }

            supportsLeftOut = 0;
            var supports = new List<Support>();
            foreach (var support in model.Supports)
            {
                // One placed by node tag rather than by point would name a node of the other numbering.
                if (!support.Plane.IsValid || !Stands(support.Pos))
                {
                    supportsLeftOut++;
                    continue;
                }
                supports.Add(new Support(support.Plane, support.Tx, support.Ty, support.Tz, support.Rx, support.Ry, support.Rz)
                {
                    Springs = support.Springs?.ToList() ?? new List<SupportSpring>(),
                });
            }

            loadsLeftOut = 0;
            var patterns = new List<LoadPattern>();
            foreach (var pattern in model.LoadPatterns)
            {
                if (pattern.PatternType == PatternType.UniformExcitation || pattern.Load == null)
                {
                    patterns.Add(pattern);
                    continue;
                }
                var loads = new List<ILoad>();
                foreach (var load in pattern.Load)
                {
                    if (!(load is PointLoad point))
                        loads.Add(load);
                    else if (Stands(point.Pos))
                        loads.Add(new PointLoad(point.Pos, point.Force, point.Moment, point.TimeSeries));
                    else
                        loadsLeftOut++;
                }
                // Not the constructor, which would hand the pattern's time series a tag of the new pattern:
                // the series is the model's own, and the pattern line names it by the tag it already has.
                patterns.Add(new LoadPattern
                {
                    PatternType = pattern.PatternType,
                    TimeSeries = pattern.TimeSeries,
                    Load = loads,
                    Factor = pattern.Factor,
                    Name = pattern.Name,
                    Dof = pattern.Dof,
                    Velocity = pattern.Velocity,
                });
            }

            var masses = new List<MassLoad>();
            foreach (var mass in model.Mass)
            {
                if (Stands(mass.Pos)) masses.Add(mass);
                else loadsLeftOut++;
            }

            var design = new Model(elements, supports, patterns, model.Constraint, new List<IRecorder>())
            {
                Mass = masses,
                Tollerance = model.Tollerance,
            };
            design.Assemble();
            return design;
        }

        /// <summary>A solid like <paramref name="brick"/>, on the same mesh and material, not yet in any model.</summary>
        public static IBrick Copy(IBrick brick)
        {
            switch (brick)
            {
                case SSPbrick b:
                    return new SSPbrick(b.Mesh, b.Material) { ElementId = b.ElementId, BodyForce = b.BodyForce, Color = b.Color };
                case Tetrahedron t:
                    return new Tetrahedron(t.Mesh, t.Material) { ElementId = t.ElementId, BodyForce = t.BodyForce, Color = t.Color };
                case TenNodeTetrahedron t:
                    return new TenNodeTetrahedron(t.Mesh, t.Material) { ElementId = t.ElementId, BodyForce = t.BodyForce, Color = t.Color };
                default:
                    throw new NotSupportedException($"A {brick.GetType().Name} cannot be copied.");
            }
        }

        /// <summary>The tag of the solid node a support stands on, or 0 when it stands on a beam's or a shell's.</summary>
        private static int SolidNode(Model model, Support support)
        {
            if (!support.Plane.IsValid)
                return 0;
            int tag = model.NodeTagNear(support.Pos, out int ndf);
            return ndf == 3 ? tag : 0;
        }
    }
}
