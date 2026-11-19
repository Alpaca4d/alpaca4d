using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

using Alpaca4d.Design;
using Alpaca4d.Generic;
using Alpaca4d.Loads;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Utilisation's Timber type: every timber beam against EN 1995-1-1 - the cross-section (6.1,
    /// 6.2), column buckling (6.3.2) and lateral-torsional buckling (6.3.3) - with k_mod from its
    /// service class and the duration of the loads of each combination, which each Load Pattern's
    /// Duration menu sets. Steel beams are left to the Steel type.
    ///
    /// Its inputs are found by name, not place: the type is new, and has no saved files to keep in step with.
    /// </summary>
    public abstract partial class UtilisationBase
    {
        private int Input(string name) => this.Params.IndexOfInputParam(name);

        private void RegisterTimber(EvaluationUnitManager mngr)
        {
            var unit = new EvaluationUnit(TimberUnit, "Timber", "Check timber beams to EN 1995-1-1.");
            unit.Icon = Alpaca4d.Gh.Properties.Resources.Utilisation_timber__Alpaca4d_;
            mngr.RegisterUnit(unit);

            unit.RegisterInputParam(new Param_GenericObject(), "AlpacaModel", "AlpacaModel",
                "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.",
                GH_ParamAccess.item);

            unit.RegisterInputParam(new Param_Integer(), "Step", "Step", "Which recorded step to check.",
                GH_ParamAccess.item, new GH_Integer(0));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "ElementId", "ElementId", ElementIdentity.Filter, GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "LengthY", "LengthY",
                $"Buckling length about local y, across a rectangle's Width [{Units.Length}]: one, or one per beam. Empty: each beam's.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "LengthZ", "LengthZ",
                $"Buckling length about local z, across a rectangle's Height [{Units.Length}]: one, or one per beam. Empty: each beam's.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "LengthLT", "LengthLT",
                $"Spacing of lateral restraints, for lateral-torsional buckling [{Units.Length}]. " +
                "Empty: each beam's length, unsafe for a cantilever.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_String(), "Load", "Load",
                "Load height, for lateral-torsional buckling: ShearCentre (default), Destabilising (or Top) or Stabilising (or Bottom).",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            ResultCombinations.Register(unit);

            unit.RegisterInputParam(new Param_Integer(), "ServiceClass", "ServiceClass",
                "Service class 1, 2 or 3, for k_mod (EN 1995-1-1 2.3.1.3). One, or one per beam. Empty: 1.",
                GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Number(), "γM", "γM",
                "Material partial factor. Empty: Table 2.3, 1.3 solid timber, 1.25 glulam, 1.0 in an accidental combination.",
                GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterInputParam(new Param_Boolean(), "Report", "Report",
                "Write the Report: every number worked out, to check by hand. Off by default: on a large model it is most of the work.",
                GH_ParamAccess.item, new GH_Boolean(false));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;

            unit.RegisterOutputParam(new Param_Number(), "Axial", "Axial", "N against f_t,0,d with k_h in tension (6.1.2), or f_c,0,d in compression (6.1.4).");
            unit.RegisterOutputParam(new Param_Number(), "ShearY", "ShearY", "Vy: 1.5 V / (k_cr A), 4V/3 for a circle, against f_v,d (6.1.7).");
            unit.RegisterOutputParam(new Param_Number(), "ShearZ", "ShearZ", "Vz: 1.5 V / (k_cr A), 4V/3 for a circle, against f_v,d (6.1.7).");
            unit.RegisterOutputParam(new Param_Number(), "Torsion", "Torsion", "St. Venant shear stress against k_shape f_v,d (6.1.8).");
            unit.RegisterOutputParam(new Param_Number(), "BendingY", "BendingY", "My, across a rectangle's Width: σ_m,d against f_m,d with k_h (6.1.6).");
            unit.RegisterOutputParam(new Param_Number(), "BendingZ", "BendingZ", "Mz, across a rectangle's Height: σ_m,d against f_m,d with k_h (6.1.6).");
            unit.RegisterOutputParam(new Param_Number(), "Combined", "Combined",
                "Bending about both axes with axial force, k_m on the other axis: (6.11), (6.12); with tension (6.17), (6.18); with compression (6.19), (6.20).");
            unit.RegisterOutputParam(new Param_Number(), "BucklingY", "BucklingY", "σ_c,0,d against k_c f_c,0,d, buckling about local y (6.3.2). Zero when not in compression.");
            unit.RegisterOutputParam(new Param_Number(), "BucklingZ", "BucklingZ", "σ_c,0,d against k_c f_c,0,d, buckling about local z (6.3.2). Zero when not in compression.");
            unit.RegisterOutputParam(new Param_Number(), "LTB", "LTB",
                "Lateral-torsional buckling, the worst piece between lateral restraints: σ_m,d against k_crit f_m,d (6.33), and (6.35) " +
                "with compression. M_cr on the beam's own moment diagram. Zero with no major axis bending, and for a circle.");
            unit.RegisterOutputParam(new Param_Number(), "InteractionY", "InteractionY",
                "Compression with bending, buckling about local y (6.23). Zero with no compression, or λ_rel ≤ 0.3 about both axes.");
            unit.RegisterOutputParam(new Param_Number(), "InteractionZ", "InteractionZ", "As InteractionY, buckling about local z (6.24).");
            unit.RegisterOutputParam(new Param_Number(), "Max", "Max", "The largest of the above. Empty when a check that applies could not be made.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Element", "Element", ElementIdentity.ElementOutput);
            unit.RegisterOutputParam(new Param_String(), "Type", "Type", "What each beam is made of, from its material's design grade: Timber, Steel, or Unknown.");
            unit.RegisterOutputParam(new Param_Number(), "kmod", "kmod", "k_mod each beam was checked with: Table 3.1, from its service class and the shortest load duration of the combination.");
            unit.RegisterOutputParam(new Param_String(), "Governing", "Governing", "Which check gives Max, or which checks could not be made.");
            unit.RegisterOutputParam(new Param_GenericObject(), "Report", "Report",
                "Everything worked out, for checking by hand: a Panel shows it as text, Deconstruct Anything splits it into its values - " +
                "k_mod, γM, k_h, design strengths, k_c, k_crit - and its Checks, one each. Empty unless the Report input is on.");

            // Folded away until it is opened.
            var design = new GH_ExtendableMenu(0, "UtilisationTimber_Design")
            {
                Name = "Design",
                Header = "Service class, partial factor and the report; type, k_mod and governing check"
            };
            foreach (var name in new[] { "ServiceClass", "γM", "Report" })
                design.RegisterInputPlug(unit.Inputs.First(plug => plug.Parameter.Name == name));
            foreach (var name in new[] { "Type", "kmod", "Governing", "Report" })
                design.RegisterOutputPlug(unit.Outputs.First(plug => plug.Parameter.Name == name));
            unit.AddMenu(design);
        }

        /// <summary>The timber check of one analysed model - the model itself, or one of its load combinations.</summary>
        private void ReadTimber(IGH_DataAccess DA, Alpaca4d.Model alpacaModel, ResultCombinations outputs)
        {
            int step = 0;
            var lengthsY = new List<double>();
            var lengthsZ = new List<double>();
            var lengthsLT = new List<double>();
            var loadTexts = new List<string>();
            var serviceClasses = new List<int>();
            bool report = false;
            double gammaM = 0.0;

            DA.GetData(Input("Step"), ref step);
            var filter = ElementFilterInput.Read(DA, Input("ElementId"), this);
            DA.GetDataList(Input("LengthY"), lengthsY);
            DA.GetDataList(Input("LengthZ"), lengthsZ);
            DA.GetDataList(Input("LengthLT"), lengthsLT);
            DA.GetDataList(Input("Load"), loadTexts);
            DA.GetDataList(Input("ServiceClass"), serviceClasses);
            bool gammaMGiven = DA.GetData(Input("γM"), ref gammaM);
            DA.GetData(Input("Report"), ref report);

            if (gammaMGiven && gammaM <= 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "γM has to be positive.");
                return;
            }

            if (serviceClasses.Any(sc => sc < 1 || sc > 3))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A service class is 1, 2 or 3.");
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

            if (!Spread(this, lengthsY, kept.Count, "LengthY", out var eachY) ||
                !Spread(this, lengthsZ, kept.Count, "LengthZ", out var eachZ) ||
                !Spread(this, lengthsLT, kept.Count, "LengthLT", out var eachLT) ||
                !Spread(this, loads, kept.Count, "Load", out var eachLoad) ||
                !Spread(this, serviceClasses, kept.Count, "ServiceClass", out var eachClass))
                return;

            if (lengthsY.Concat(lengthsZ).Concat(lengthsLT).Any(x => x <= 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A buckling length has to be positive.");
                return;
            }

            // Node to node, the length OpenSees integrates over.
            var members = beams.Select(b => new SteelMember(b.Curve != null ? b.Curve.PointAtStart.DistanceTo(b.Curve.PointAtEnd) : 0.0)).ToList();
            for (int j = 0; j < kept.Count; j++)
            {
                var member = members[kept[j]];
                if (eachY[j].HasValue) member.BucklingLengthY = eachY[j].Value;
                if (eachZ[j].HasValue) member.BucklingLengthZ = eachZ[j].Value;
                if (eachLT[j].HasValue) member.LateralTorsionalLength = eachLT[j].Value;
                if (eachLoad[j].HasValue) member.Load = eachLoad[j].Value;
                if (eachClass[j].HasValue) member.ServiceClass = eachClass[j].Value;
            }

            List<MemberUtilisation> all;
            try
            {
                all = Utilisations.Of(alpacaModel, step, members,
                                      new DesignSettings { Report = report, GammaM = gammaMGiven ? gammaM : (double?)null, Family = "Timber" });
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            var results = kept.Select(i => all[i]).ToList();

            foreach (var (name, pick) in new (string, Func<MemberUtilisation, double?>)[]
            {
                ("Axial", u => u.Axial), ("ShearY", u => u.ShearY), ("ShearZ", u => u.ShearZ), ("Torsion", u => u.Torsion),
                ("BendingY", u => u.BendingY), ("BendingZ", u => u.BendingZ), ("Combined", u => u.Combined),
                ("BucklingY", u => u.BucklingY), ("BucklingZ", u => u.BucklingZ), ("LTB", u => u.LateralTorsional),
                ("InteractionY", u => u.InteractionY), ("InteractionZ", u => u.InteractionZ), ("Max", u => u.Max), ("kmod", u => u.Kmod),
            })
                Set(outputs, name, Numbers(results, pick));
            outputs.Same(Output("Type"), results.Select(u => u.Type));
            Set(outputs, "Governing", results.Select(u => u.Governing));
            // One kind of object for every beam, as Deconstruct Anything takes a list of one kind: a
            // beam the check passed over gets its text alone.
            if (report)
                Set(outputs, "Report", results.Select(u => u.Details ?? new TimberReport(u.Report) { Max = u.Max, Governing = u.Governing }));
            outputs.Same(Output("Element"), kept.Select(i => beams[i]));

            bool defaultLengths = lengthsY.Count == 0 || lengthsZ.Count == 0 || lengthsLT.Count == 0;
            ExplainTimber(results, kept, beams, defaultLengths, alpacaModel, serviceClasses.Count == 0);
        }

        /// <summary>Says, once per solve, which beams were left out and why, and what k_mod was worked out from.</summary>
        private void ExplainTimber(List<MemberUtilisation> results, List<int> kept, IReadOnlyList<IBeam> beams, bool defaultLengths,
                                   Alpaca4d.Model model, bool defaultServiceClass)
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
                    "Make the material with Material Library Elastic, or name it after its class (\"C24\", \"GL24h\").");

            var left = Enumerable.Range(0, results.Count).Where(j => results[j].Type == "Timber" && !results[j].Checked).ToList();
            if (left.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{left.Count} timber beam(s) are not checked (element {Tags(left)}): " +
                    string.Join("; ", left.Select(j => results[j].NotCheckedWhy).Where(why => why != null).Distinct().Take(3)) + ".");

            OtherMaterial(results, "Timber", Tags);

            var incomplete = Enumerable.Range(0, results.Count).Where(j => results[j].Checked && results[j].Missing.Count > 0).ToList();
            if (incomplete.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{incomplete.Count} beam(s) have checks that could not be made, so no Max (element {Tags(incomplete)}). See their Report.");

            if (!results.Any(u => u.Checked))
                return;

            if (defaultLengths)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Buckling lengths left empty are each beam's own length, node to node, held at both ends: a member drawn " +
                    "as several beams, a sway column or a cantilever needs its lengths given.");

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "k_mod from the shortest load duration of each combination" +
                (defaultServiceClass ? ", in service class 1 - give ServiceClass in the Design menu for another" : "") +
                ". Not checked yet: bearing across the grain (6.1.5), notches, curved and tapered members, connections.");

            // Every pattern of the model, whichever combination this is, so the message is the same for each.
            LoadDurations.Governing(model?.Combination?.Source ?? model, out var unset);
            if (unset.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Load pattern(s) {string.Join(", ", unset.Take(8))}{(unset.Count > 8 ? ", ..." : "")} have no Duration " +
                    "and count as permanent, the lowest k_mod. Set it in each Load Pattern's Duration menu.");
        }

        /// <summary>Names the beams of a material the type does not check: each material is its own type's.</summary>
        private void OtherMaterial(List<MemberUtilisation> results, string family, Func<IEnumerable<int>, string> tags)
        {
            foreach (var other in results.Select(u => u.Type).Distinct().Where(type => type != "Unknown" && type != family).ToList())
            {
                var them = Enumerable.Range(0, results.Count).Where(j => results[j].Type == other && !results[j].Checked).ToList();
                if (them.Count > 0)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"{them.Count} beam(s) are {other.ToLowerInvariant()}, left to the {other} type (element {tags(them)}).");
            }
        }
    }
}
