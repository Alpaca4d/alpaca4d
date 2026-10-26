using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The kinds of result View Results can draw.
    ///
    /// Files saved before the menu had an order of its own hold the choice as a position in this
    /// list, so a family is only ever added at the end: one slipped in beside its neighbours would
    /// reopen those files one family along. The order the menu shows is
    /// <see cref="ResultField.MenuOrder"/>.
    /// </summary>
    internal enum ResultFamily
    {
        Displacement,
        BeamForce,
        ShellForce,
        ShellStress,
        BrickStress,
        Reaction,
        BeamStress
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
            "Displacement", "Beam forces", "Shell forces", "Shell stresses", "Brick stresses", "Reactions",
            "Beam stresses"
        };

        private static readonly string[] DisplacementNames = { "Magnitude", "Ux", "Uy", "Uz" };
        private static readonly string[] BeamForceNames = { "N", "Vy", "Vz", "Torsion", "My", "Mz" };
        private static readonly string[] ShellForceNames = { "fxx", "fyy", "fxy", "mxx", "myy", "mxy", "vxz", "vyz" };
        private static readonly string[] ShellStressNames = { "σ11", "σ22", "σ12", "σ23", "σ31", "VonMises" };
        // Index notation, because the frame is the Axes dropdown's to choose: the solver reports in
        // the global axes, and Axes can turn the six components into the element's own, taken from
        // its node numbering. Naming them sigma-xx would be right for only one of the two settings.
        private static readonly string[] BrickStressNames = { "σ11", "σ22", "σ33", "σ12", "σ23", "σ13", "VonMises" };
        private static readonly string[] ReactionNames = { "Force", "Fx", "Fy", "Fz", "Moment", "Mx", "My", "Mz" };
        // The outputs of Beam Stresses, in the same order, with σx - N, My and Mz at each fibre -
        // after its three parts. Files saved before σx are moved one along on load (ViewResults).
        private static readonly string[] BeamStressNames = { "σN", "σMy", "σMz", "σx", "σmax", "σmin", "τV", "τT", "VonMises" };

        /// <summary>Where σx sits in <see cref="BeamStressNames"/>: a saved Component at or past it predates it.</summary>
        public const int SigmaX = 3;

        /// <summary>
        /// The order the Result dropdown offers the families in: element by element, beams before
        /// shells before solids, forces before the stresses they cause, reactions last. Free to
        /// change - the dropdown's position is turned into a family through this list, and View
        /// Results marks the files it saves so an older one is read in the order it was saved in.
        /// </summary>
        public static readonly ResultFamily[] MenuOrder =
        {
            ResultFamily.Displacement,
            ResultFamily.BeamForce,
            ResultFamily.BeamStress,
            ResultFamily.ShellForce,
            ResultFamily.ShellStress,
            ResultFamily.BrickStress,
            ResultFamily.Reaction
        };

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
                case ResultFamily.BeamStress: return BeamStressNames;
                default: return ReactionNames;
            }
        }

        /// <summary>
        /// What kind of quantity a component of a family is, which is what decides the units it is
        /// offered in. Beam forces and reactions are forces for the first half of their list and
        /// moments for the rest; shell forces are a force per width, but for the three bending
        /// moments in the middle.
        /// </summary>
        public static Quantity QuantityOf(ResultFamily family, int component)
        {
            switch (family)
            {
                case ResultFamily.Displacement: return Quantity.Length;
                case ResultFamily.BeamForce: return component >= 3 ? Quantity.Moment : Quantity.Force;
                case ResultFamily.ShellForce: return component >= 3 && component <= 5 ? Quantity.MomentPerLength : Quantity.ForcePerLength;
                case ResultFamily.Reaction: return component >= 4 ? Quantity.Moment : Quantity.Force;
                default: return Quantity.Stress;
            }
        }

        /// <summary>
        /// Whether a component differs from one point of a beam's section to the next, and so is
        /// painted point by point when beams are drawn solid. σN is the same all over the section by
        /// what it is, and σmax and σmin are each the worst anywhere on it: one value per section.
        /// </summary>
        public static bool VariesOverSection(ResultFamily family, int component)
        {
            if (family != ResultFamily.BeamStress) return false;

            switch (component)
            {
                case 0:
                case 4:
                case 5:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// One component at one point of a beam's surface. The shears in size, as the section's own
        /// τV and τT are, since the way a shear runs along the surface has no sign of its own.
        /// </summary>
        public static double AtPoint(Alpaca4d.Result.PointStress stress, int component)
        {
            switch (component)
            {
                case 1: return stress.SigmaMy;
                case 2: return stress.SigmaMz;
                case 6: return Math.Abs(stress.TauV);
                case 7: return Math.Abs(stress.TauT);
                case 8: return stress.VonMises;
                case 0: return stress.Sigma - stress.SigmaMy - stress.SigmaMz;
                default: return stress.Sigma;
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

        /// <summary>
        /// Whether the family can be read in a frame other than the global one. Only the solids:
        /// a beam force is already in the beam's axes and a shell stress in the shell's, and a
        /// displacement and a reaction are vectors with nowhere else to go.
        /// </summary>
        public static bool HasAxes(ResultFamily family)
        {
            return family == ResultFamily.BrickStress;
        }

        /// <summary>The frames a solid stress can be read in, in the order the dropdown offers them.</summary>
        public static readonly string[] StressAxes = { "Global", "Local" };

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

        /// <summary>
        /// Beam stresses only: what each beam needs for its stress to be worked out at any point of
        /// its section rather than once per section, by element tag. Beams whose section has no shape
        /// are left out. Null for every other family.
        /// </summary>
        public Dictionary<int, BeamSectionField> BySection;

        /// <summary>The component read, which <see cref="BySection"/> is turned into a value with.</summary>
        public int Component;

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

        /// <summary>
        /// Every value multiplied by <paramref name="factor"/>, to show the field in another unit
        /// than the solver's. Into new lists, so nothing a reader handed back is changed under it.
        /// </summary>
        public void Scale(double factor)
        {
            if (factor == 1.0) return;

            if (this.ByNode != null)
                this.ByNode = this.ByNode.ToDictionary(x => x.Key, x => x.Value * factor);

            if (this.ByElement != null)
                this.ByElement = this.ByElement.ToDictionary(x => x.Key, x => x.Value?.Select(v => v * factor).ToList());

            // The vector too, which the arrows are drawn and labelled from.
            if (this.Reactions != null)
                this.Reactions = this.Reactions.Select(x => Tuple.Create(x.Item1, x.Item2 * factor, x.Item3 * factor)).ToList();

            if (this.BySection != null)
                this.BySection = this.BySection.ToDictionary(x => x.Key, x => x.Value.Scaled(factor));
        }

        public static ResultField Read(Alpaca4d.Model model, ResultFamily family, int component, int layer, int step,
                                       bool localAxes = false)
        {
            switch (family)
            {
                case ResultFamily.Displacement: return Displacement(model, component, step);
                case ResultFamily.BeamForce: return BeamForce(model, component, step);
                case ResultFamily.ShellForce: return ShellForce(model, component, step);
                case ResultFamily.ShellStress: return ShellStress(model, component, layer, step);
                case ResultFamily.BrickStress: return BrickStress(model, component, step, localAxes);
                case ResultFamily.BeamStress: return BeamStress(model, component, step);
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

        private static ResultField BeamStress(Alpaca4d.Model model, int component, int step)
        {
            var field = new ResultField
            {
                ByElement = new Dictionary<int, List<double>>(),
                BySection = new Dictionary<int, BeamSectionField>(),
                Component = component,
                Label = BeamStressNames[component]
            };

            if (model.Beams.Count == 0)
                return field;

            var read = Alpaca4d.Result.Read.BeamSectionForces(model, step);

            for (int i = 0; i < model.Beams.Count && i < read.Count; i++)
            {
                var (shape, forces) = read[i];
                if (!model.Beams[i].Id.HasValue || forces.Count == 0)
                    continue;

                // An Elastic Section has no shape, so N/A is all it has. Left out of everything else,
                // its beams draw plain rather than in a colour standing for a number they do not have.
                if (!shape.HasShape && component != 0)
                    continue;

                // In the solver's kN/m², as every other family is: View Results turns them into the
                // unit picked, which starts at MPa - the unit Beam Stresses gives them in.
                int tag = model.Beams[i].Id.Value;
                field.ByElement[tag] = forces.Select(f => Of(shape.At(f[0], f[1], f[2], f[3], f[4], f[5]), component)).ToList();

                if (shape.HasShape)
                    field.BySection[tag] = new BeamSectionField(shape, forces);
            }

            return field;
        }

        /// <summary>
        /// One component of a section's stresses. Drawn as a line, a beam has no section to paint,
        /// so σx is the worst of it: σmax or σmin, whichever is larger in size, with its sign.
        /// </summary>
        private static double Of(Alpaca4d.Result.BeamStress stress, int component)
        {
            switch (component)
            {
                case 1: return stress.SigmaMy;
                case 2: return stress.SigmaMz;
                case 3: return Math.Abs(stress.SigmaMax) >= Math.Abs(stress.SigmaMin) ? stress.SigmaMax : stress.SigmaMin;
                case 4: return stress.SigmaMax;
                case 5: return stress.SigmaMin;
                case 6: return stress.TauV;
                case 7: return stress.TauT;
                case 8: return stress.VonMises;
                default: return stress.SigmaN;
            }
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

        private static ResultField BrickStress(Alpaca4d.Model model, int component, int step, bool localAxes)
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
                var read = Alpaca4d.Result.Read.TetrahedronStress(model, step, local: localAxes);
                FillFlat(field.ByElement, tetrahedra, component,
                    read.Item1, read.Item2, read.Item3, read.Item4, read.Item5, read.Item6);
            }

            if (model.HasSSpBrick && bricks.Count > 0)
            {
                var read = Alpaca4d.Result.Read.SSPBrickStress(model, step, local: localAxes);
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

    /// <summary>
    /// A beam's stress anywhere on its section: the section's shape and the section forces at each
    /// of the beam's integration sections. See <see cref="ResultField.BySection"/>.
    /// </summary>
    internal class BeamSectionField
    {
        public readonly Alpaca4d.Result.BeamSectionStress Shape;

        /// <summary>N, Vy, Vz, T, My, Mz at each integration section, from the I end.</summary>
        public readonly List<double[]> Forces;

        /// <summary>What every value is multiplied by on the way out, to show it in the unit picked.</summary>
        public readonly double Factor;

        public BeamSectionField(Alpaca4d.Result.BeamSectionStress shape, List<double[]> forces, double factor = 1.0)
        {
            this.Shape = shape;
            this.Forces = forces;
            this.Factor = factor;
        }

        public BeamSectionField Scaled(double factor) => new BeamSectionField(this.Shape, this.Forces, this.Factor * factor);

        /// <summary>One component at a point of the surface, under one set of forces.</summary>
        public double At(Alpaca4d.Result.SurfacePoint point, double[] forces, int component)
        {
            var stress = point.Under(forces[0], forces[1], forces[2], forces[3], forces[4], forces[5]);
            return this.Factor * ResultField.AtPoint(stress, component);
        }
    }
}
