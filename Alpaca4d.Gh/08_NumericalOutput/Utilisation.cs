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
    /// A switcher with a single unit, for the sake of its menus: what is set once per project and then
    /// left alone - fabrication, partial factors, whether to write a report - and what says more about
    /// a member than how loaded it is - its type, class, governing check and report - sit in a Design
    /// menu that stays folded away. The body shows the utilisations and nothing else.
    ///
    /// Every parameter belongs to the unit, outputs included: only a unit's parameters can be plugs
    /// of a menu, and the component's own would come first and fix the order.
    /// </summary>
    public class Utilisation : GH_SwitcherComponent
    {
        public Utilisation()
          : base("Utilisation (Alpaca4d)", "Utilisation",
            "Checks every beam against the design code of its material and reports how much of each " +
            "resistance it uses: 1 is exactly at the limit.\n" +
            "Steel, to EN 1993-1-1: cross-section class, then axial force, shear, torsion, bending, and " +
            "bending with axial force and shear (6.2), and flexural buckling about both axes (6.3.1). " +
            "Not yet: lateral-torsional buckling (6.3.2), bending with compression as a member (6.3.3), " +
            "torsional buckling and Class 4 sections - a member in compression and bending is not " +
            "verified by this alone.\n" +
            "The steel grade comes from the beam's material: make it with Material Library Elastic, or " +
            "name it after its grade (\"S355\").\n" +
            "One item per beam, in the order of the Element output. A check that could not be made is " +
            "empty, never zero, and leaves Max empty too.\n" +
            "Fabrication, the partial factors and the report - and the Type, Class and Governing outputs - " +
            "are in the Design menu below the component.",
            "Alpaca4d", "08_NumericalOutput")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // All inputs belong to the evaluation unit, see RegisterEvaluationUnits.
        }

        // Where each parameter sits. The inputs from Fabrication on, and the outputs from Type on, are
        // plugs of the Design menu, in the order they are drawn there: Report beside Report.
        private const int ModelInput = 0, StepInput = 1, FilterInput = 2, LengthInput = 3;
        private const int FabricationInput = 4, GammaM0Input = 5, GammaM1Input = 6, ReportInput = 7;

        private const int AxialOutput = 0, ShearYOutput = 1, ShearZOutput = 2, TorsionOutput = 3, BendingYOutput = 4,
                          BendingZOutput = 5, CombinedOutput = 6, BucklingYOutput = 7, BucklingZOutput = 8, MaxOutput = 9,
                          ElementOutput = 10;
        private const int TypeOutput = 11, ClassOutput = 12, GoverningOutput = 13, ReportOutput = 14;

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

            unit.RegisterInputParam(new Param_Number(), "Length", "Length",
                $"Buckling length L_cr, the same about both axes [{Units.Length}]. One value for every beam, or " +
                "one per beam checked, in the order of the Element output. Left empty, each beam's own " +
                "length, node to node.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "Fabrication", "Fabrication",
                "How the sections were made, which sets the buckling curves (Table 6.2) and an I's shear area.\n" +
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

            unit.RegisterOutputParam(new Param_Number(), "Axial", "Axial", "N against the plastic resistance of the section, in tension or compression (6.2.3, 6.2.4).");
            unit.RegisterOutputParam(new Param_Number(), "ShearY", "ShearY", "Vy against the plastic shear resistance, reduced for torsion (6.2.6, 6.2.7). Along the web of an I.");
            unit.RegisterOutputParam(new Param_Number(), "ShearZ", "ShearZ", "Vz against the plastic shear resistance, reduced for torsion (6.2.6, 6.2.7). Across the flanges of an I.");
            unit.RegisterOutputParam(new Param_Number(), "Torsion", "Torsion", "St. Venant shear stress against fy/√3 (6.2.7). Warping torsion is not part of the analysis.");
            unit.RegisterOutputParam(new Param_Number(), "BendingY", "BendingY", "My, bending about local y - the minor axis of an I - against the plastic or elastic moment, reduced for shear (6.2.5, 6.2.8).");
            unit.RegisterOutputParam(new Param_Number(), "BendingZ", "BendingZ", "Mz, bending about local z - the major axis of an I - against the plastic or elastic moment, reduced for shear (6.2.5, 6.2.8).");
            unit.RegisterOutputParam(new Param_Number(), "Combined", "Combined", "Bending about both axes with axial force and shear (6.2.9, 6.2.10).");
            unit.RegisterOutputParam(new Param_Number(), "BucklingY", "BucklingY", "Flexural buckling about local y - the minor axis of an I (6.3.1). Zero when the beam is not in compression.");
            unit.RegisterOutputParam(new Param_Number(), "BucklingZ", "BucklingZ", "Flexural buckling about local z - the major axis of an I (6.3.1). Zero when the beam is not in compression.");
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
            design.RegisterOutputPlug(unit.Outputs[TypeOutput]);
            design.RegisterOutputPlug(unit.Outputs[ClassOutput]);
            design.RegisterOutputPlug(unit.Outputs[GoverningOutput]);
            design.RegisterOutputPlug(unit.Outputs[ReportOutput]);
            unit.AddMenu(design);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // All outputs belong to the evaluation unit, see RegisterEvaluationUnits.
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            var alpacaModel = new Alpaca4d.Model();
            int step = 0;
            var lengths = new List<double>();
            bool report = false;
            string fabricationText = "Rolled";
            double gammaM0 = 1.0, gammaM1 = 1.0;

            if (!DA.GetData(ModelInput, ref alpacaModel)) return;
            DA.GetData(StepInput, ref step);
            var filter = ElementFilterInput.Read(DA, FilterInput, this);
            DA.GetDataList(LengthInput, lengths);
            DA.GetData(ReportInput, ref report);
            DA.GetData(FabricationInput, ref fabricationText);
            DA.GetData(GammaM0Input, ref gammaM0);
            DA.GetData(GammaM1Input, ref gammaM1);

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

            var beams = alpacaModel.Beams;
            var kept = ElementFilterInput.Select(filter, beams, alpacaModel, "beams", this);

            if (lengths.Count > 1 && lengths.Count != kept.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Length has {lengths.Count} values for {kept.Count} beams. Give one for all of them, or one each.");
                return;
            }

            if (lengths.Any(x => x <= 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A buckling length has to be positive.");
                return;
            }

            // Node to node, the length OpenSees integrates over. A beam curve is a straight line between
            // its nodes, so this is its length too.
            var elementLengths = beams.Select(b => b.Curve != null ? b.Curve.PointAtStart.DistanceTo(b.Curve.PointAtEnd) : 0.0).ToList();

            var bucklingLengths = new List<double>(elementLengths);
            for (int j = 0; j < kept.Count; j++)
            {
                if (lengths.Count == 1) bucklingLengths[kept[j]] = lengths[0];
                else if (lengths.Count > 1) bucklingLengths[kept[j]] = lengths[j];
            }

            List<MemberUtilisation> all;
            try
            {
                all = Utilisations.Of(alpacaModel, step, elementLengths, bucklingLengths,
                                      new DesignSettings { Fabrication = fabrication, GammaM0 = gammaM0, GammaM1 = gammaM1, Report = report });
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var results = kept.Select(i => all[i]).ToList();

            DA.SetDataList(AxialOutput, Numbers(results, u => u.Axial));
            DA.SetDataList(ShearYOutput, Numbers(results, u => u.ShearY));
            DA.SetDataList(ShearZOutput, Numbers(results, u => u.ShearZ));
            DA.SetDataList(TorsionOutput, Numbers(results, u => u.Torsion));
            DA.SetDataList(BendingYOutput, Numbers(results, u => u.BendingY));
            DA.SetDataList(BendingZOutput, Numbers(results, u => u.BendingZ));
            DA.SetDataList(CombinedOutput, Numbers(results, u => u.Combined));
            DA.SetDataList(BucklingYOutput, Numbers(results, u => u.BucklingY));
            DA.SetDataList(BucklingZOutput, Numbers(results, u => u.BucklingZ));
            DA.SetDataList(MaxOutput, Numbers(results, u => u.Max));
            DA.SetDataList(TypeOutput, results.Select(u => u.Type));
            DA.SetDataList(ClassOutput, results.Select(u => u.Class.HasValue ? new GH_Integer(u.Class.Value) : null));
            DA.SetDataList(GoverningOutput, results.Select(u => u.Governing));
            if (report)
                DA.SetDataList(ReportOutput, results.Select(u => u.Report));
            DA.SetDataList(ElementOutput, kept.Select(i => beams[i]));

            Explain(results, kept, beams);
        }

        /// <summary>Says, once per solve, which beams were left out and why.</summary>
        private void Explain(List<MemberUtilisation> results, List<int> kept, IReadOnlyList<IBeam> beams)
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

            bool BucklingMissing(MemberUtilisation u) => u.Missing.Contains("BucklingY") || u.Missing.Contains("BucklingZ");

            var bucklingFour = Enumerable.Range(0, results.Count)
                .Where(j => results[j].Checked && BucklingMissing(results[j]) && results[j].BucklingClass == 4).ToList();
            if (bucklingFour.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{bucklingFour.Count} beam(s) are Class 4 in compression, so their flexural buckling is not checked and Max is empty " +
                    $"(element {Tags(bucklingFour)}). Their cross-section checks are made; buckling needs A_eff from EN 1993-1-5.");

            var incomplete = Enumerable.Range(0, results.Count)
                .Where(j => results[j].Checked && results[j].Missing.Count > 0 && !bucklingFour.Contains(j)).ToList();
            if (incomplete.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{incomplete.Count} beam(s) have checks that could not be made, so no Max (element {Tags(incomplete)}). See their Report.");

            var relaxed = Enumerable.Range(0, results.Count).Where(j => results[j].Checked && results[j].LowStressClass3).ToList();
            if (relaxed.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{relaxed.Count} beam(s) have a part that is Class 4 by Table 5.2, taken as Class 3 under 5.5.2(9) at the low stress " +
                    $"it carries, and checked elastically there (element {Tags(relaxed)}).");

            if (results.Any(u => u.Type == "Steel" && u.Checked))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Lateral-torsional buckling (6.3.2), bending with compression as a member (6.3.3) and torsional buckling are not checked yet.");
        }

        private static IEnumerable<GH_Number> Numbers(IEnumerable<MemberUtilisation> results, Func<MemberUtilisation, double?> pick)
        {
            return results.Select(u =>
            {
                var value = pick(u);
                return value.HasValue && !double.IsNaN(value.Value) ? new GH_Number(value.Value) : null;
            });
        }

        private static bool TryFabrication(string text, out Fabrication fabrication)
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

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Utilisation__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{6E2B9C41-8F0A-4B3D-A6C7-2D51E9F48B07}");
    }
}
