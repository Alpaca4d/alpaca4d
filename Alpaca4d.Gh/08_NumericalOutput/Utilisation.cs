using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Linq;
using System.Collections.Generic;

using Alpaca4d.Design;
using Alpaca4d.Generic;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Checks beams against the design code of their material: a Steel type, to EN 1993-1-1, and a
    /// Timber type, to EN 1995-1-1 (UtilisationTimber.cs), each with the inputs and outputs of its
    /// code, and each checking its own material alone. The component is <see cref="Utilisation"/>;
    /// this is everything it shares with <see cref="Utilisation_obsolete"/>, the one saved definitions
    /// from before lateral-torsional buckling and 6.3.3 still hold, which has the Steel type alone.
    ///
    /// A switcher with a single unit, for the sake of its menus: what is set once per project and then
    /// left alone - fabrication, partial factors, whether to write a report - and what says more about
    /// a member than how loaded it is - its type, class, governing check and report - sit in a Design
    /// menu that stays folded away. The body shows the utilisations and nothing else.
    ///
    /// Every parameter belongs to the unit, outputs included: only a unit's parameters can be plugs
    /// of a menu, and the component's own would come first and fix the order.
    /// </summary>
    public abstract partial class UtilisationBase : GH_SwitcherComponent
    {
        private const string Help =
            "Checks beams against the design code of their material and reports how much of each " +
            "resistance it uses: 1 is exactly at the limit. Pick the material below the component: Steel or Timber.\n" +
            "Steel, to EN 1993-1-1: cross-section class, then axial force, shear, torsion, bending, and " +
            "bending with axial force and shear (6.2); flexural buckling about both axes (6.3.1); " +
            "lateral-torsional buckling (6.3.2), with M_cr worked out on each beam's own moment diagram; and " +
            "bending with compression as a member (6.3.3, by Annex B). Not yet: torsional buckling and Class 4 sections.\n" +
            "Timber, to EN 1995-1-1: tension, compression, bending, shear and torsion (6.1), with axial force (6.2), " +
            "column buckling (6.3.2) and lateral-torsional buckling (6.3.3), with k_mod from the service class and the " +
            "duration of the loads, set in each Load Pattern's Duration menu.\n" +
            "Buckling lengths left empty are worked out from the model: beams in line make a member, held where " +
            "supports, bracing or the beams it sits on hold it. Model View's Buckling menu draws them.\n" +
            "The grade comes from the beam's material: make it with Material Library Elastic, or " +
            "name it after its grade (\"S355\", \"C24\", \"GL24h\").\n" +
            "One item per beam, in the order of the Element output. A check that could not be made is " +
            "empty, never zero, and leaves Max empty too.\n" +
            "Fabrication, the partial factors and the report - and the Type, Class and Governing outputs - " +
            "are in the Design menu below the component.";

        protected UtilisationBase()
          : base("Utilisation (Alpaca4d)", "Utilisation", Help, "Alpaca4d", "08_NumericalOutput")
        {
            // Below the component: the material it checks, as SwitchUnit writes it.
            this.Message = $"{ActiveUnit?.DisplayName ?? this.NickName}\n{this.Category}";
        }

        /// <summary>
        /// The inputs of 6.3.2 and 6.3.3 - a buckling length about each axis, L_LT, the load position
        /// and sway - and the LTB and interaction outputs. Only <see cref="Utilisation_obsolete"/> lacks
        /// them, and checks with each beam's own length for all of it.
        /// </summary>
        protected abstract bool MemberInputs { get; }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // All inputs belong to the evaluation unit, see RegisterEvaluationUnits.
        }

        // Where each input sits. Those from Fabrication to Report are plugs of the Design menu; the four
        // after them came later, and are last so that the inputs before them keep their places.
        private const int ModelInput = 0, StepInput = 1, FilterInput = 2, LengthInput = 3;
        private const int FabricationInput = 4, GammaM0Input = 5, GammaM1Input = 6, ReportInput = 7;
        private const int LengthZInput = 8, LengthLTInput = 9, LoadInput = 10, SwayInput = 11;
        // Only Utilisation has it, last of all: Utilisation_obsolete has none of the inputs from LengthZ on.
        private const int CombinationInput = 12;

        /// <summary>The units, by name. Steel keeps the name the one unit had, so saved definitions open on it.</summary>
        private const string SteelUnit = "Utilisation", TimberUnit = "UtilisationTimber";

        public override string UnitMenuName => "Material";
        public override string UnitMenuHeader => "Select the material";
        protected override string DefaultEvaluationUnit => SteelUnit;

        protected override void SwitchUnit(EvaluationUnit unit, bool recompute = true, bool recordEvent = true)
        {
            base.SwitchUnit(unit, recompute, recordEvent);
            this.Message = $"{ActiveUnit?.DisplayName ?? this.NickName}\n{this.Category}";
        }

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            RegisterSteel(mngr);
            if (this.MemberInputs)
                RegisterTimber(mngr);
        }

        private void RegisterSteel(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(SteelUnit, "Steel", "Check steel beams to EN 1993-1-1.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Utilisation__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.",
                GH_ParamAccess.item);

            unit.RegisterInputParam(new Param_Integer(), "Step", "Step", "Which recorded step to check.",
                GH_ParamAccess.item, new GH_Integer(0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "ElementId", "ElementId", ElementIdentity.Filter, GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            if (this.MemberInputs)
                unit.RegisterInputParam(new Param_Number(), "LengthY", "LengthY",
                    $"Buckling length about local y, an I's minor axis [{Units.Length}]: one, or one per beam. Empty: worked out from the model.",
                    GH_ParamAccess.list);
            else
                unit.RegisterInputParam(new Param_Number(), "Length", "Length",
                    $"Buckling length about both axes [{Units.Length}]: one, or one per beam in Element order. Empty: each beam's length.",
                    GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "Fabrication", "Fabrication",
                "How the sections were made: Rolled (rolled I, hot-finished hollow) or Welded (welded I, cold-formed hollow).",
                GH_ParamAccess.item, new GH_String("Rolled"));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "γM0", "γM0",
                "Partial factor for the resistance of cross-sections. 1.0 is the recommended value; a National Annex may set another.",
                GH_ParamAccess.item, new GH_Number(1.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "γM1", "γM1",
                "Partial factor for the resistance of members to instability. 1.0 is the recommended value; a National Annex may set another.",
                GH_ParamAccess.item, new GH_Number(1.0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Boolean(), "Report", "Report",
                "Write the Report output: every number worked out on the way, for checking by hand. Off by " +
                "default - on a large model the text is most of the work.",
                GH_ParamAccess.item, new GH_Boolean(false));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            if (this.MemberInputs)
            {
                unit.RegisterInputParam(new Param_Number(), "LengthZ", "LengthZ",
                    $"Buckling length about local z, an I's major axis [{Units.Length}]: one, or one per beam. Empty: worked out from the model.",
                    GH_ParamAccess.list);
                unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

                unit.RegisterInputParam(new Param_Number(), "LengthLT", "LengthLT",
                    $"Spacing of lateral restraints, for lateral-torsional buckling [{Units.Length}]: one, or one per beam. Empty: worked out from the model.",
                    GH_ParamAccess.list);
                unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

                unit.RegisterInputParam(new Param_String(), "Load", "Load",
                    "Load height, for lateral-torsional buckling: ShearCentre (default), Destabilising (or Top) or Stabilising (or Bottom).",
                    GH_ParamAccess.list);
                unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

                unit.RegisterInputParam(new Param_Boolean(), "Sway", "Sway",
                    "The beam is in a frame that buckles by swaying: Cm = 0.9 about both axes (Table B.3). One, or one per beam.",
                    GH_ParamAccess.list);
                unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

                ResultCombinations.Register(unit);
            }

            unit.RegisterOutputParam(new Param_Number(), "Axial", "Axial", "N against the plastic resistance of the section, in tension or compression (6.2.3, 6.2.4).");
            unit.RegisterOutputParam(new Param_Number(), "ShearY", "ShearY", "Vy against the plastic shear resistance, reduced for torsion (6.2.6, 6.2.7). Along the web of an I.");
            unit.RegisterOutputParam(new Param_Number(), "ShearZ", "ShearZ", "Vz against the plastic shear resistance, reduced for torsion (6.2.6, 6.2.7). Across the flanges of an I.");
            unit.RegisterOutputParam(new Param_Number(), "Torsion", "Torsion", "St. Venant shear stress against fy/√3 (6.2.7). Warping torsion is not part of the analysis.");
            unit.RegisterOutputParam(new Param_Number(), "BendingY", "BendingY", "My, bending about local y - the minor axis of an I - against the plastic or elastic moment, reduced for shear (6.2.5, 6.2.8).");
            unit.RegisterOutputParam(new Param_Number(), "BendingZ", "BendingZ", "Mz, bending about local z - the major axis of an I - against the plastic or elastic moment, reduced for shear (6.2.5, 6.2.8).");
            unit.RegisterOutputParam(new Param_Number(), "Combined", "Combined", "Bending about both axes with axial force and shear (6.2.9, 6.2.10).");
            unit.RegisterOutputParam(new Param_Number(), "BucklingY", "BucklingY", "Flexural buckling about local y - the minor axis of an I (6.3.1). Zero when the beam is not in compression.");
            unit.RegisterOutputParam(new Param_Number(), "BucklingZ", "BucklingZ", "Flexural buckling about local z - the major axis of an I (6.3.1). Zero when the beam is not in compression.");
            if (this.MemberInputs)
            {
                unit.RegisterOutputParam(new Param_Number(), "LTB", "LTB",
                    "Lateral-torsional buckling (6.3.2): the major axis moment against χLT W fy/γM1, the worst piece between " +
                    "lateral restraints, with M_cr worked out on the beam's own moment diagram. Zero with no major axis " +
                    "bending, and for a tube or a round bar, which cannot buckle sideways.");
                unit.RegisterOutputParam(new Param_Number(), "InteractionY", "InteractionY",
                    "Bending and axial compression as a member (6.3.3, Annex B): the one of (6.61) and (6.62) whose χ is about " +
                    "local y - (6.62) for an I. Zero with no bending, or with bending about one axis and no compression. " +
                    "Empty for a section that is not doubly symmetric, which 6.3.3 does not cover.");
                unit.RegisterOutputParam(new Param_Number(), "InteractionZ", "InteractionZ",
                    "As InteractionY, the one whose χ is about local z - (6.61) for an I.");
            }
            unit.RegisterOutputParam(new Param_Number(), "Max", "Max", "The largest of the above. Empty when a check that applies could not be made.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Element", "Element", ElementIdentity.ElementOutput);
            unit.RegisterOutputParam(new Param_String(), "Type", "Type", "What each beam is made of, from its material's design grade: Steel, or Unknown when the material has none.");
            unit.RegisterOutputParam(new Param_Integer(), "Class", "Class", "EN 1993-1-1 cross-section class, the worst along the beam.");
            unit.RegisterOutputParam(new Param_String(), "Governing", "Governing", "Which check gives Max, or which checks could not be made.");
            unit.RegisterOutputParam(new Param_String(), "Report", "Report", "Everything worked out on the way, for checking by hand: grade, class, properties, resistances, where each check governs, and what is not covered. Empty unless the Report input is on.");

            // Folded away until it is opened.
            var design = new GH_ExtendableMenu(0, "Utilisation_Design")
            {
                Name = "Design",
                Header = "Fabrication, partial factors and the report; type, class and governing check"
            };
            design.RegisterInputPlug(unit.Inputs[FabricationInput]);
            design.RegisterInputPlug(unit.Inputs[GammaM0Input]);
            design.RegisterInputPlug(unit.Inputs[GammaM1Input]);
            design.RegisterInputPlug(unit.Inputs[ReportInput]);
            foreach (var name in new[] { "Type", "Class", "Governing", "Report" })
                design.RegisterOutputPlug(unit.Outputs.First(plug => plug.Parameter.Name == name));
            unit.AddMenu(design);
        }


        /// <summary>
        /// What the user should know about the buckling lengths worked out from the model, when some are:
        /// that a model too large to look for its bracing in got each member's own length, and that a
        /// slab or a wall holds nothing in them.
        /// </summary>
        internal static void SayHowLengths(GH_Component component, Alpaca4d.Model model, BucklingLengths worked, bool someWorkedOut)
        {
            if (!someWorkedOut || model.Beams.Count == 0) return;
            if (worked.Approximate)
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "The model is too large to find its bracing in: each member was given its own length. Give the lengths by hand.");
            if (model.Shells.Count > 0 || model.Bricks.Count > 0)
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Lengths worked out from the model count no slab or wall as a restraint: give those they shorten by hand.");
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // All outputs belong to the evaluation unit, see RegisterEvaluationUnits.
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            var alpacaModel = new Alpaca4d.Model();
            if (!DA.GetData(ModelInput, ref alpacaModel)) return;
            if (!AnalysedModel.Check(this, alpacaModel)) return;
            if (unit?.Name == TimberUnit)
                ResultCombinations.Read(this, DA, Input("Combination"), alpacaModel, (model, outputs) => ReadTimber(DA, model, outputs));
            else
                ResultCombinations.Read(this, DA, CombinationInput, alpacaModel, (model, outputs) => Read(DA, model, outputs));
        }

        /// <summary>The check of one analysed model - the model itself, or one of its load combinations.</summary>
        private void Read(IGH_DataAccess DA, Alpaca4d.Model alpacaModel, ResultCombinations outputs)
        {
            int step = 0;
            var lengthsY = new List<double>();
            var lengthsZ = new List<double>();
            var lengthsLT = new List<double>();
            var loadTexts = new List<string>();
            var sways = new List<bool>();
            bool report = false;
            string fabricationText = "Rolled";
            double gammaM0 = 1.0, gammaM1 = 1.0;

            DA.GetData(StepInput, ref step);
            var filter = ElementFilterInput.Read(DA, FilterInput, this);
            DA.GetDataList(LengthInput, lengthsY);
            DA.GetData(ReportInput, ref report);
            DA.GetData(FabricationInput, ref fabricationText);
            DA.GetData(GammaM0Input, ref gammaM0);
            DA.GetData(GammaM1Input, ref gammaM1);
            if (this.MemberInputs)
            {
                DA.GetDataList(LengthZInput, lengthsZ);
                DA.GetDataList(LengthLTInput, lengthsLT);
                DA.GetDataList(LoadInput, loadTexts);
                DA.GetDataList(SwayInput, sways);
            }
            else
            {
                // The one length of the old component, about both axes.
                lengthsZ.AddRange(lengthsY);
            }

            Fabrication fabrication;
            if (!TryFabrication(fabricationText, out fabrication))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Fabrication \"{fabricationText}\" is not one of Rolled or Welded.");
                return;
            }

            if (gammaM0 <= 0.0 || gammaM1 <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "γM0 and γM1 have to be positive.");
                return;
            }

            var loads = new List<LoadPosition>();
            foreach (var text in loadTexts)
            {
                if (!TryLoad(text, out var load))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Load \"{text}\" is not one of ShearCentre, Destabilising or Stabilising.");
                    return;
                }
                loads.Add(load);
            }

            var beams = alpacaModel.Beams;
            var kept = ElementFilterInput.Select(filter, beams, alpacaModel, "beams", this);

            string lengthY = this.MemberInputs ? "LengthY" : "Length";
            if (!Spread(this, lengthsY, kept.Count, lengthY, out var eachY) ||
                !Spread(this, lengthsZ, kept.Count, "LengthZ", out var eachZ) ||
                !Spread(this, lengthsLT, kept.Count, "LengthLT", out var eachLT) ||
                !Spread(this, loads, kept.Count, "Load", out var eachLoad) ||
                !Spread(this, sways, kept.Count, "Sway", out var eachSway))
                return;

            if (lengthsY.Concat(lengthsZ).Concat(lengthsLT).Any(x => x <= 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A buckling length has to be positive.");
                return;
            }

            // Node to node, the length OpenSees integrates over - a beam curve is a straight line between its
            // nodes, so this is its length too - and the buckling lengths worked out from the model, which a
            // length given here goes over. The old component, which never had them, keeps each beam's own.
            List<SteelMember> members;
            if (this.MemberInputs)
            {
                var worked = BucklingLengths.Of(alpacaModel);
                members = worked.SteelMembers(alpacaModel);
                SayHowLengths(this, alpacaModel, worked, lengthsY.Count == 0 || lengthsZ.Count == 0 || lengthsLT.Count == 0);
            }
            else
            {
                members = beams.Select(b => new SteelMember(b.Curve != null ? b.Curve.PointAtStart.DistanceTo(b.Curve.PointAtEnd) : 0.0)).ToList();
            }
            for (int j = 0; j < kept.Count; j++)
            {
                var member = members[kept[j]];
                if (eachY[j].HasValue) member.BucklingLengthY = eachY[j].Value;
                if (eachZ[j].HasValue) member.BucklingLengthZ = eachZ[j].Value;
                if (eachLT[j].HasValue) member.LateralTorsionalLength = eachLT[j].Value;
                if (eachLoad[j].HasValue) member.Load = eachLoad[j].Value;
                if (eachSway[j].HasValue) member.Sway = eachSway[j].Value;
            }

            List<MemberUtilisation> all;
            try
            {
                all = Utilisations.Of(alpacaModel, step, members,
                                      new DesignSettings { Fabrication = fabrication, GammaM0 = gammaM0, GammaM1 = gammaM1, Report = report, Family = "Steel" });
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var results = kept.Select(i => all[i]).ToList();

            Set(outputs, "Axial", Numbers(results, u => u.Axial));
            Set(outputs, "ShearY", Numbers(results, u => u.ShearY));
            Set(outputs, "ShearZ", Numbers(results, u => u.ShearZ));
            Set(outputs, "Torsion", Numbers(results, u => u.Torsion));
            Set(outputs, "BendingY", Numbers(results, u => u.BendingY));
            Set(outputs, "BendingZ", Numbers(results, u => u.BendingZ));
            Set(outputs, "Combined", Numbers(results, u => u.Combined));
            Set(outputs, "BucklingY", Numbers(results, u => u.BucklingY));
            Set(outputs, "BucklingZ", Numbers(results, u => u.BucklingZ));
            if (this.MemberInputs)
            {
                Set(outputs, "LTB", Numbers(results, u => u.LateralTorsional));
                Set(outputs, "InteractionY", Numbers(results, u => u.InteractionY));
                Set(outputs, "InteractionZ", Numbers(results, u => u.InteractionZ));
            }
            Set(outputs, "Max", Numbers(results, u => u.Max));
            outputs.Same(Output("Type"), results.Select(u => u.Type));
            Set(outputs, "Class", results.Select(u => u.Class.HasValue ? new GH_Integer(u.Class.Value) : null));
            Set(outputs, "Governing", results.Select(u => u.Governing));
            if (report)
                Set(outputs, "Report", results.Select(u => u.Report));
            outputs.Same(Output("Element"), kept.Select(i => beams[i]));

            // Only the old component still takes each beam's own length; the new one works them out.
            bool defaultLengths = !this.MemberInputs && lengthsY.Count == 0;
            Explain(results, kept, beams, defaultLengths);
        }

        /// <summary>An output, by name: the outputs after BucklingZ move when LTB and the interactions are there.</summary>
        private void Set(ResultCombinations outputs, string name, System.Collections.IEnumerable data)
        {
            outputs.List(Output(name), data);
        }

        private int Output(string name) => this.Params.IndexOfOutputParam(name);

        /// <summary>
        /// One value per kept beam from a list of none (no value for any), one (the same for all) or
        /// one each. False, with an error, for any other count.
        /// </summary>
        internal static bool Spread<T>(GH_Component component, List<T> values, int count, string name, out List<T?> each) where T : struct
        {
            each = Enumerable.Range(0, count)
                             .Select(j => values.Count == 0 ? (T?)null : values.Count == 1 ? values[0] : j < values.Count ? values[j] : (T?)null)
                             .ToList();

            if (values.Count > 1 && values.Count != count)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"{name} has {values.Count} values for {count} beams. Give one for all of them, or one each.");
                return false;
            }

            return true;
        }

        /// <summary>Says, once per solve, which beams were left out and why.</summary>
        private void Explain(List<MemberUtilisation> results, List<int> kept, IReadOnlyList<IBeam> beams, bool defaultLengths)
        {
            string Tags(IEnumerable<int> positions)
            {
                var tags = positions.Select(j => beams[kept[j]].Id?.ToString() ?? "?").ToList();
                return string.Join(", ", tags.Take(10)) + (tags.Count > 10 ? ", ..." : "");
            }

            var ungraded = Enumerable.Range(0, results.Count).Where(j => results[j].Type == "Unknown").ToList();
            if (ungraded.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{ungraded.Count} beam(s) have a material with no design grade and are not checked (element {Tags(ungraded)}). " +
                    "Make the material with Material Library Elastic, or name it after its grade (\"S355\").");

            var classFour = Enumerable.Range(0, results.Count).Where(j => !results[j].Checked && results[j].Class == 4).ToList();
            if (classFour.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{classFour.Count} beam(s) are Class 4 and are not checked at all: Class 4 needs effective section properties " +
                    $"(EN 1993-1-5), which are not covered yet (element {Tags(classFour)}). See their Report for the part that sets it.");

            var shapeless = Enumerable.Range(0, results.Count).Where(j => results[j].Type == "Steel" && !results[j].Checked && results[j].Class == null).ToList();
            if (shapeless.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{shapeless.Count} beam(s) use a section with no shape - an Elastic Section - and are not checked (element {Tags(shapeless)}).");

            OtherMaterial(results, "Steel", Tags);

            bool MemberMissing(MemberUtilisation u) => u.Missing.Any(name => name.StartsWith("Buckling") || name == "LTB" || name.StartsWith("Interaction"));

            var bucklingFour = Enumerable.Range(0, results.Count)
                .Where(j => results[j].Checked && MemberMissing(results[j]) && results[j].BucklingClass == 4).ToList();
            if (bucklingFour.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{bucklingFour.Count} beam(s) are Class 4 by Table 5.2 alone, so their member checks are not made and Max is empty " +
                    $"(element {Tags(bucklingFour)}). Their cross-section checks are made; the member checks need A_eff and W_eff from EN 1993-1-5.");

            // Everything else in place, the interactions alone missing: a section 6.3.3 does not cover.
            var monosymmetric = Enumerable.Range(0, results.Count)
                .Where(j => results[j].Checked && !bucklingFour.Contains(j)
                            && results[j].Missing.All(name => name.StartsWith("Interaction")) && results[j].Missing.Count > 0)
                .ToList();
            if (monosymmetric.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{monosymmetric.Count} beam(s) are not doubly symmetric - an I with unequal flanges, a box with unequal top and " +
                    "bottom walls, a pair of angles - and are in " +
                    "compression or bending about both axes. 6.3.3 covers doubly symmetric members only, so their interaction is not " +
                    $"checked and Max is empty (element {Tags(monosymmetric)}).");

            var incomplete = Enumerable.Range(0, results.Count)
                .Where(j => results[j].Checked && results[j].Missing.Count > 0 && !bucklingFour.Contains(j) && !monosymmetric.Contains(j)).ToList();
            if (incomplete.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{incomplete.Count} beam(s) have checks that could not be made, so no Max (element {Tags(incomplete)}). See their Report.");

            var relaxed = Enumerable.Range(0, results.Count).Where(j => results[j].Checked && results[j].LowStressClass3).ToList();
            if (relaxed.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{relaxed.Count} beam(s) have a part that is Class 4 by Table 5.2, taken as Class 3 under 5.5.2(9) at the low stress " +
                    $"it carries, and checked elastically there (element {Tags(relaxed)}).");

            if (results.Any(u => u.Type == "Steel" && u.Checked))
            {
                if (defaultLengths)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "Buckling lengths left empty are each beam's own length, node to node, held at both ends: a member drawn " +
                        "as several beams, a sway column or a cantilever needs its lengths given.");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Torsional and torsional-flexural buckling (6.3.1.4) are not checked yet.");
            }
        }

        private static IEnumerable<GH_Number> Numbers(IEnumerable<MemberUtilisation> results, Func<MemberUtilisation, double?> pick)
        {
            return results.Select(u =>
            {
                var value = pick(u);
                return value.HasValue && !double.IsNaN(value.Value) ? new GH_Number(value.Value) : null;
            });
        }

        internal static bool TryFabrication(string text, out Fabrication fabrication)
        {
            var key = (text ?? "").Replace(" ", "").Replace("-", "").ToLowerInvariant();
            switch (key)
            {
                case "":
                case "rolled":
                case "hotrolled":
                case "hotfinished":
                    fabrication = Fabrication.Rolled;
                    return true;
                case "welded":
                case "coldformed":
                    fabrication = Fabrication.Welded;
                    return true;
                default:
                    fabrication = Fabrication.Rolled;
                    return false;
            }
        }

        internal static bool TryLoad(string text, out LoadPosition load)
        {
            var key = (text ?? "").Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
            switch (key)
            {
                case "":
                case "shearcentre":
                case "shearcenter":
                case "centre":
                case "center":
                    load = LoadPosition.ShearCentre;
                    return true;
                case "destabilising":
                case "destabilizing":
                case "top":
                    load = LoadPosition.Destabilising;
                    return true;
                case "stabilising":
                case "stabilizing":
                case "bottom":
                    load = LoadPosition.Stabilising;
                    return true;
                default:
                    load = LoadPosition.ShearCentre;
                    return false;
            }
        }

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Utilisation__Alpaca4d_;
    }

    /// <summary>
    /// Utilisation: every beam against the design code of its material, with a buckling length about
    /// each axis, the distance between lateral restraints, where the load acts and whether the frame
    /// sways.
    /// </summary>
    public class Utilisation : UtilisationBase
    {
        protected override bool MemberInputs => true;

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        public override Guid ComponentGuid => new Guid("{4F8A2D6B-91C3-4E57-B0A9-3C6E8D1F2A74}");
    }
}
