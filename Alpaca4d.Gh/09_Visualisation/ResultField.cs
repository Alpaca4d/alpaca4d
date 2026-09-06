using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    /// <summary>The kinds of result View Results can draw.</summary>
    internal enum ResultFamily
    {
        Displacement,
        BeamForce,
        ShellForce,
        ShellStress,
        BrickStress,
        Reaction
    }

    /// <summary>
    /// One result, read out of a model and reduced to numbers the viewport can be coloured by.
    ///
    /// This is the half of View Results that knows about recorder files, and it exists so that the
    /// component itself does not. Every family is read to the same shape - a value per node, or a
    /// list of values per element tag - so the drawing code has one case to handle rather than six,
    /// and adding a seventh family means adding a branch here and a name to a list.
    ///
    /// Keyed by tag rather than by position throughout. The readers hand their results back in the
    /// model's own order, and quads, triangles, tetrahedra and bricks each come from a dataset of
    /// their own; resolving all of that to tags here is what lets everything downstream ask a
    /// single question - what is the value on this element - without knowing where it came from.
    /// </summary>
    internal class ResultField
    {
        /// <summary>The names in the Result dropdown, in the order of <see cref="ResultFamily"/>.</summary>
        public static readonly string[] FamilyNames =
        {
            "Displacement", "Beam forces", "Shell forces", "Shell stresses", "Brick stresses", "Reactions"
        };

        private static readonly string[] DisplacementNames = { "Magnitude", "Ux", "Uy", "Uz" };
        private static readonly string[] BeamForceNames = { "N", "Vy", "Vz", "Torsion", "My", "Mz" };
        private static readonly string[] ShellForceNames = { "fxx", "fyy", "fxy", "mxx", "myy", "mxy", "vxz", "vyz" };
        private static readonly string[] ShellStressNames = { "σ11", "σ22", "σ12", "σ23", "σ31", "VonMises" };
        // Index notation and the element's own axes, the way the solid reader reports them and the
        // way Brick Stresses names them - not σxx, which would read as the global axes.
        private static readonly string[] BrickStressNames = { "σ11", "σ22", "σ33", "σ12", "σ23", "σ13", "VonMises" };
        private static readonly string[] ReactionNames = { "Force", "Fx", "Fy", "Fz", "Moment", "Mx", "My", "Mz" };

        /// <summary>What the Component dropdown offers for a given family.</summary>
        public static string[] ComponentNames(ResultFamily family)
        {
            switch (family)
            {
                case ResultFamily.Displacement: return DisplacementNames;
                case ResultFamily.BeamForce: return BeamForceNames;
                case ResultFamily.ShellForce: return ShellForceNames;
                case ResultFamily.ShellStress: return ShellStressNames;
                case ResultFamily.BrickStress: return BrickStressNames;
                default: return ReactionNames;
            }
        }

        /// <summary>The ways a reaction can be drawn at a support.</summary>
        public static readonly string[] ReactionStyles = { "Selected component", "Resultant", "All three" };

        /// <summary>
        /// The pair of colours that belongs to a beam force, positive and negative.
        ///
        /// A force diagram is read by its sign before it is read by its size, and every other tool
        /// draws N one colour and Vy another so that two diagrams on one screen can be told apart.
        /// The gradient is for fields painted on geometry; a diagram is not one.
        ///
        /// The palette pairs a force with its moment - N with Torsion, Vy with My, Vz with Mz -
        /// which is fine, because those two never share a diagram.
        /// </summary>
        public static System.Drawing.Color BeamForceColour(int component, double value)
        {
            switch (component)
            {
                case 1: return value >= 0.0 ? Alpaca4d.UI.Palette.Vy_Positive : Alpaca4d.UI.Palette.Vy_Negative;
                case 2: return value >= 0.0 ? Alpaca4d.UI.Palette.Vz_Positive : Alpaca4d.UI.Palette.Vz_Negative;
                case 3: return value >= 0.0 ? Alpaca4d.UI.Palette.Torsion_Positive : Alpaca4d.UI.Palette.Torsion_Negative;
                case 4: return value >= 0.0 ? Alpaca4d.UI.Palette.My_Positive : Alpaca4d.UI.Palette.My_Negative;
                case 5: return value >= 0.0 ? Alpaca4d.UI.Palette.Mz_Positive : Alpaca4d.UI.Palette.Mz_Negative;
                default: return value >= 0.0 ? Alpaca4d.UI.Palette.N_Positive : Alpaca4d.UI.Palette.N_Negative;
            }
        }

        /// <summary>Whether the family needs the Layer dropdown - only stress through a thickness does.</summary>
        public static bool HasLayers(ResultFamily family)
        {
            return family == ResultFamily.ShellStress;
        }

        /// <summary>
        /// Whether the family draws something standing off the model that has to be sized by hand.
        ///
        /// A field painted on an element is as big as the element. A force diagram and a reaction
        /// arrow are not: they stick out into space, and how far is a choice. Only those two get
        /// the scale, and the control for it is put away for the rest.
        /// </summary>
        public static bool HasScale(ResultFamily family)
        {
            return family == ResultFamily.BeamForce || family == ResultFamily.Reaction;
        }

        /// <summary>Whether the family draws reactions, which have a style of their own to pick.</summary>
        public static bool HasReactionStyle(ResultFamily family)
        {
            return family == ResultFamily.Reaction;
        }

        /// <summary>Displacement at every node, by node tag. Null for the other families.</summary>
        public Dictionary<int, double> ByNode;

        /// <summary>
        /// The result at every element that has one, by element tag. One entry per integration
        /// point, in the element's own order, so a mesh can be shaded across its face rather than
        /// flooded with a single colour.
        /// </summary>
        public Dictionary<int, List<double>> ByElement;

        /// <summary>Reaction at each support, in the support's own axes, paired with where it acts.</summary>
        public List<Tuple<Plane, Vector3d, double>> Reactions;

        /// <summary>What is being shown, for the Info output and the viewport caption.</summary>
        public string Label = "";

        /// <summary>Every value in the field, which is what the colour range is fitted to.</summary>
        public IEnumerable<double> Values
        {
            get
            {
                if (this.ByNode != null)
                    return this.ByNode.Values;

                if (this.ByElement != null)
                    return this.ByElement.Values.SelectMany(x => x);

                if (this.Reactions != null)
                    return this.Reactions.Select(x => x.Item3);

                return Enumerable.Empty<double>();
            }
        }

        public static ResultField Read(Alpaca4d.Model model, ResultFamily family, int component, int layer, int step)
        {
            switch (family)
            {
                case ResultFamily.Displacement: return Displacement(model, component, step);
                case ResultFamily.BeamForce: return BeamForce(model, component, step);
                case ResultFamily.ShellForce: return ShellForce(model, component, step);
                case ResultFamily.ShellStress: return ShellStress(model, component, layer, step);
                case ResultFamily.BrickStress: return BrickStress(model, component, step);
                default: return Reaction(model, component, step);
            }
        }

        private static ResultField Displacement(Alpaca4d.Model model, int component, int step)
        {
            var displacement = model.NodalDisplacements(step);
            var field = new ResultField
            {
                ByNode = new Dictionary<int, double>(),
                Label = DisplacementNames[component]
            };

            foreach (var entry in displacement)
            {
                if (entry.Key.HasValue)
                    field.ByNode[entry.Key.Value] = Of(entry.Value, component);
            }

            return field;
        }

        /// <summary>Magnitude, or one global component of it.</summary>
        private static double Of(Vector3d vector, int component)
        {
            switch (component)
            {
                case 1: return vector.X;
                case 2: return vector.Y;
                case 3: return vector.Z;
                default: return vector.Length;
            }
        }

        private static ResultField BeamForce(Alpaca4d.Model model, int component, int step)
        {
            var field = new ResultField
            {
                ByElement = new Dictionary<int, List<double>>(),
                Label = BeamForceNames[component]
            };

            if (model.Beams.Count == 0)
                return field;

            var read = Alpaca4d.Result.Read.ForceBeamColumn(model, step);

            // Read.ForceBeamColumn hands them back in the order of Model.Beams, and in the order
            // n, mz, vy, my, vz, t - which is not the order they are offered in.
            List<List<double>> chosen;
            switch (component)
            {
                case 1: chosen = read.vy; break;
                case 2: chosen = read.vz; break;
                case 3: chosen = read.t; break;
                case 4: chosen = read.my; break;
                case 5: chosen = read.mz; break;
                default: chosen = read.n; break;
            }

            Fill(field.ByElement, model.Beams, chosen);

            return field;
        }

        private static ResultField ShellForce(Alpaca4d.Model model, int component, int step)
        {
            var field = new ResultField
            {
                ByElement = new Dictionary<int, List<double>>(),
                Label = ShellForceNames[component]
            };

            // Quads and triangles are recorded separately, each in the order of its own sub-list.
            var quads = model.Shells.Where(IsQuad).ToList();
            var triangles = model.Shells.Where(shell => !IsQuad(shell)).ToList();

            if (model.HasQuadShell && quads.Count > 0)
            {
                var read = Alpaca4d.Result.Read.ASDQ4Forces(model, step);
                Fill(field.ByElement, quads, Pick(component,
                    read.Item1, read.Item2, read.Item3, read.Item4, read.Item5, read.Item6, read.Item7, read.Item8));
            }

            if (model.HasTriShell && triangles.Count > 0)
            {
                var read = Alpaca4d.Result.Read.ASDT3Forces(model, step);
                Fill(field.ByElement, triangles, Pick(component,
                    read.Item1, read.Item2, read.Item3, read.Item4, read.Item5, read.Item6, read.Item7, read.Item8));
            }

            return field;
        }

        private static bool IsQuad(IShell shell)
        {
            return shell.ElementClass == Alpaca4d.Element.ElementClass.ASDShellQ4;
        }

        /// <summary>One of the eight stress resultants the shell readers return, in the offered order.</summary>
        private static List<List<double>> Pick(int component, params List<List<double>>[] read)
        {
            return component >= 0 && component < read.Length ? read[component] : read[0];
        }

        private static ResultField ShellStress(Alpaca4d.Model model, int component, int layer, int step)
        {
            var field = new ResultField
            {
                ByElement = new Dictionary<int, List<double>>(),
                Label = ShellStressNames[component] + " at " + Alpaca4d.Result.Read.LayerNames[layer]
            };

            if (model.Shells.Count == 0)
                return field;

            var all = Alpaca4d.Result.Read.ShellFibreStresses(model, step);

            foreach (var group in all.GroupBy(x => x.ElementId))
            {
                var entries = group.ToList();
                int fibres = entries.Select(x => x.Fibre).Distinct().Count();
                if (fibres == 0)
                    continue;

                // The reader gives one station per fibre per integration point, bottom to top,
                // however many fibres the section has. LayerFibres picks the three that Top,
                // Middle and Bottom mean for that count.
                int wanted = Alpaca4d.Result.Read.LayerFibres(fibres)[layer];

                field.ByElement[group.Key] = entries.Where(x => x.Fibre == wanted)
                                                    .OrderBy(x => x.GaussPoint)
                                                    .Select(x => Of(x, component))
                                                    .ToList();
            }

            return field;
        }

        private static double Of(Alpaca4d.Result.Read.ShellFibreStress stress, int component)
        {
            switch (component)
            {
                case 1: return stress.S22;
                case 2: return stress.S12;
                case 3: return stress.S23;
                case 4: return stress.S31;
                case 5: return stress.VonMises;
                default: return stress.S11;
            }
        }

        private static ResultField BrickStress(Alpaca4d.Model model, int component, int step)
        {
            var field = new ResultField
            {
                ByElement = new Dictionary<int, List<double>>(),
                Label = BrickStressNames[component]
            };

            // Tetrahedra first, then the bricks, which is the order they were recorded in.
            var tetrahedra = model.Bricks.Where(x => x.ElementClass == Alpaca4d.Element.ElementClass.FourNodeTetrahedron).ToList();
            var bricks = model.Bricks.Where(x => x.ElementClass == Alpaca4d.Element.ElementClass.SSPBrick).ToList();

            if (model.HasTetrahedron && tetrahedra.Count > 0)
            {
                var read = Alpaca4d.Result.Read.TetrahedronStress(model, step);
                FillFlat(field.ByElement, tetrahedra, component,
                    read.Item1, read.Item2, read.Item3, read.Item4, read.Item5, read.Item6);
            }

            if (model.HasSSpBrick && bricks.Count > 0)
            {
                var read = Alpaca4d.Result.Read.SSPBrickStress(model, step);
                FillFlat(field.ByElement, bricks, component,
                    read.Item1, read.Item2, read.Item3, read.Item4, read.Item5, read.Item6);
            }

            return field;
        }

        /// <summary>
        /// The solids report one value per element rather than one per integration point, so their
        /// six components arrive as flat lists.
        /// </summary>
        private static void FillFlat<T>(Dictionary<int, List<double>> into, IReadOnlyList<T> elements,
            int component, params List<double>[] components) where T : IElement
        {
            for (int i = 0; i < elements.Count; i++)
            {
                if (!elements[i].Id.HasValue)
                    continue;

                // Von Mises is not recorded; it is the six components put back together.
                double value = component < 6
                    ? At(components[component], i)
                    : VonMises(At(components[0], i), At(components[1], i), At(components[2], i),
                               At(components[3], i), At(components[4], i), At(components[5], i));

                into[elements[i].Id.Value] = new List<double> { value };
            }
        }

        private static double At(List<double> values, int index)
        {
            return values != null && index < values.Count ? values[index] : 0.0;
        }

        private static double VonMises(double xx, double yy, double zz, double xy, double yz, double zx)
        {
            return Math.Sqrt(0.5 * ((xx - yy) * (xx - yy) + (yy - zz) * (yy - zz) + (zz - xx) * (zz - xx))
                             + 3.0 * (xy * xy + yz * yz + zx * zx));
        }

        private static void Fill<T>(Dictionary<int, List<double>> into, IReadOnlyList<T> elements,
                                    List<List<double>> read) where T : IElement
        {
            if (read == null)
                return;

            for (int i = 0; i < elements.Count && i < read.Count; i++)
            {
                if (elements[i].Id.HasValue)
                    into[elements[i].Id.Value] = read[i];
            }
        }

        private static ResultField Reaction(Alpaca4d.Model model, int component, int step)
        {
            var field = new ResultField
            {
                Reactions = new List<Tuple<Plane, Vector3d, double>>(),
                Label = ReactionNames[component]
            };

            if (model.Supports.Count == 0)
                return field;

            // A skewed support carries its fix on a coincident node of its own, so that is where
            // OpenSees puts the reaction; the support node itself reads zero.
            var nodes = model.Supports.Select(x => x.AuxiliaryNodeId ?? x.Id).ToList();
            var type = component < 4 ? ResultType.REACTION_FORCE : ResultType.REACTION_MOMENT;
            var global = Alpaca4d.Result.Read.NodalOutput(model, step, type, nodes).ToList();

            for (int i = 0; i < model.Supports.Count && i < global.Count; i++)
            {
                var plane = model.Supports[i].Plane;

                // Reactions come out in global components whatever the support is turned to, which
                // for a skewed one spreads a reaction running along one local axis across all
                // three global ones. Resolved onto the support's own axes, a released direction
                // reads as the zero it is.
                var local = new Vector3d(global[i] * plane.XAxis, global[i] * plane.YAxis, global[i] * plane.ZAxis);

                field.Reactions.Add(Tuple.Create(plane, local, Of(local, component % 4)));
            }

            return field;
        }
    }
}
