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
    /// Checks every beam against the design code of its material. The component is <see cref="Utilisation"/>;
    /// this is everything it shares with <see cref="Utilisation_obsolete"/>, the one saved definitions
    /// from before lateral-torsional buckling and 6.3.3 still hold.
    ///
    /// A switcher with a single unit, for the sake of its menus: what is set once per project and then
    /// left alone - fabrication, partial factors, whether to write a report - and what says more about
    /// a member than how loaded it is - its type, class, governing check and report - sit in a Design
    /// menu that stays folded away. The body shows the utilisations and nothing else.
    ///
    /// Every parameter belongs to the unit, outputs included: only a unit's parameters can be plugs
    /// of a menu, and the component's own would come first and fix the order.
    /// </summary>
    public abstract class UtilisationBase : GH_SwitcherComponent
    {
        private const string Help =
            "Checks every beam against the design code of its material and reports how much of each " +
            "resistance it uses: 1 is exactly at the limit.\n" +
            "Steel, to EN 1993-1-1: cross-section class, then axial force, shear, torsion, bending, and " +
            "bending with axial force and shear (6.2); flexural buckling about both axes (6.3.1); " +
            "lateral-torsional buckling (6.3.2), with M_cr worked out on each beam's own moment diagram; and " +
            "bending with compression as a member (6.3.3, by Annex B). Not yet: torsional buckling and Class 4 sections.\n" +
            "The steel grade comes from the beam's material: make it with Material Library Elastic, or " +
            "name it after its grade (\"S355\").\n" +
            "One item per beam, in the order of the Element output. A check that could not be made is " +
            "empty, never zero, and leaves Max empty too.\n" +
            "Fabrication, the partial factors and the report - and the Type, Class and Governing outputs - " +
            "are in the Design menu below the component.";

        protected UtilisationBase()
          : base("Utilisation (Alpaca4d)", "Utilisation", Help, "Alpaca4d", "08_NumericalOutput")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
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

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit("Utilisation", "Utilisation", "Check beams against the design code of their material.");
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
                    $"Buckling length L_cr about local y - the minor axis of an I [{Units.Length}]. " + PerBeam +
                    " Left empty, each beam's own length, node to node. A member drawn as several beams needs " +
                    "its whole length here: each beam's own is only a piece of it.",
                    GH_ParamAccess.list);
            else
                unit.RegisterInputParam(new Param_Number(), "Length", "Length",
                    $"Buckling length L_cr, the same about both axes [{Units.Length}]. One value for every beam, or " +
                    "one per beam checked, in the order of the Element output. Left empty, each beam's own " +
                    "length, node to node.",
                    GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "Fabrication", "Fabrication",
                "How the sections were made, which sets the buckling curves (Tables 6.2, 6.4, 6.5) and an I's shear area.\n" +
                "Rolled: rolled I-sections and hot-finished hollow sections.\n" +
                "Welded: welded I-sections and cold-formed hollow sections.",
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
                    $"Buckling length L_cr about local z - the major axis of an I [{Units.Length}]. " + PerBeam +
                    " Left empty, each beam's own length, node to node.",
                    GH_ParamAccess.list);
                unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

                unit.RegisterInputParam(new Param_Number(), "LengthLT", "LengthLT",
                    $"Distance between lateral restraints, for lateral-torsional buckling [{Units.Length}]. Each beam " +
                    "is taken as held sideways and against twist at both ends and at equal steps no longer than " +
                    "this in between, and free to rotate and warp at each - fork supports - and every piece is " +
                    "checked on its own part of the moment diagram. Longer than the beam, it stands for an " +
                    "unrestrained length the beam is only part of, or a cantilever's effective length, and the " +
                    "beam's peak moment is taken as uniform over all of it. " + PerBeam +
                    " Left empty, each beam's own length: not safe for a cantilever, whose tip is not held.",
                    GH_ParamAccess.list);
                unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

                unit.RegisterInputParam(new Param_String(), "Load", "Load",
                    "Where the load along each beam acts on its section, for lateral-torsional buckling.\n" +
                    "ShearCentre: neither helps nor hinders. The default.\n" +
                    "Destabilising (or Top): on the face the load comes from - gravity on the top flange, which " +
                    "the beam carries sideways as it twists.\n" +
                    "Stabilising (or Bottom): hung from the face it goes towards.\n" + PerBeam,
                    GH_ParamAccess.list);
                unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

                unit.RegisterInputParam(new Param_Boolean(), "Sway", "Sway",
                    "The beam belongs to a frame that buckles by swaying: Cm = 0.9 about both axes, as Table B.3 " +
                    "has it, in place of the factor its moment diagram gives. Off by default. " + PerBeam,
                    GH_ParamAccess.list);
                unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
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

        private const string PerBeam = "One value for every beam, or one per beam checked, in the order of the Element output.";

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // All outputs belong to the evaluation unit, see RegisterEvaluationUnits.
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            var alpacaModel = new Alpaca4d.Model();
            int step = 0;
            var lengthsY = new List<double>();
            var lengthsZ = new List<double>();
            var lengthsLT = new List<double>();
            var loadTexts = new List<string>();
            var sways = new List<bool>();
            bool report = false;
            string fabricationText = "Rolled";
            double gammaM0 = 1.0, gammaM1 = 1.0;

            if (!DA.GetData(ModelInput, ref alpacaModel)) return;
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

            // Node to node, the length OpenSees integrates over. A beam curve is a straight line between
            // its nodes, so this is its length too.
            var members = beams.Select(b => new SteelMember(b.Curve != null ? b.Curve.PointAtStart.DistanceTo(b.Curve.PointAtEnd) : 0.0)).ToList();
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
                                      new DesignSettings { Fabrication = fabrication, GammaM0 = gammaM0, GammaM1 = gammaM1, Report = report });
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var results = kept.Select(i => all[i]).ToList();

            Set(DA, "Axial", Numbers(results, u => u.Axial));
            Set(DA, "ShearY", Numbers(results, u => u.ShearY));
            Set(DA, "ShearZ", Numbers(results, u => u.ShearZ));
            Set(DA, "Torsion", Numbers(results, u => u.Torsion));
            Set(DA, "BendingY", Numbers(results, u => u.BendingY));
            Set(DA, "BendingZ", Numbers(results, u => u.BendingZ));
            Set(DA, "Combined", Numbers(results, u => u.Combined));
            Set(DA, "BucklingY", Numbers(results, u => u.BucklingY));
            Set(DA, "BucklingZ", Numbers(results, u => u.BucklingZ));
            if (this.MemberInputs)
            {
                Set(DA, "LTB", Numbers(results, u => u.LateralTorsional));
                Set(DA, "InteractionY", Numbers(results, u => u.InteractionY));
                Set(DA, "InteractionZ", Numbers(results, u => u.InteractionZ));
            }
            Set(DA, "Max", Numbers(results, u => u.Max));
            Set(DA, "Type", results.Select(u => u.Type));
            Set(DA, "Class", results.Select(u => u.Class.HasValue ? new GH_Integer(u.Class.Value) : null));
            Set(DA, "Governing", results.Select(u => u.Governing));
            if (report)
                Set(DA, "Report", results.Select(u => u.Report));
            Set(DA, "Element", kept.Select(i => beams[i]));

            bool defaultLengths = lengthsY.Count == 0 || lengthsZ.Count == 0 || (this.MemberInputs && lengthsLT.Count == 0);
            Explain(results, kept, beams, defaultLengths);
        }

        /// <summary>An output, by name: the outputs after BucklingZ move when LTB and the interactions are there.</summary>
        private void Set(IGH_DataAccess DA, string name, System.Collections.IEnumerable data)
        {
            DA.SetDataList(this.Params.IndexOfOutputParam(name), data);
        }

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

            var shapeless = Enumerable.Range(0, results.Count).Where(j => results[j].Type != "Unknown" && !results[j].Checked && results[j].Class == null).ToList();
            if (shapeless.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{shapeless.Count} beam(s) use a section with no shape - an Elastic Section - and are not checked (element {Tags(shapeless)}).");

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
