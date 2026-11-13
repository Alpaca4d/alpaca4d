using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// What the three law switchers - Concrete, Steel and Other Laws - share: units that each build one
    /// OpenSees uniaxial material for the fibres of a section, and the helpers that register their inputs.
    /// Stresses in kN/m2, strains dimensionless.
    /// </summary>
    public abstract class FibreLaws : GH_SwitcherComponent
    {
        protected FibreLaws(string name, string nickname, string description)
          : base(name, nickname, description, "Alpaca4d", "MomentCurvature_βeta")
        {
            UpdateMessage();
        }

        public override string UnitMenuName => "Law";
        public override string UnitMenuHeader => "Select the law";

        private void UpdateMessage()
        {
            string name = ActiveUnit?.DisplayName ?? this.NickName;
            this.Message = $"{name}\n{this.Category}";
        }

        protected override void SwitchUnit(EvaluationUnit unit, bool recompute = true, bool recordEvent = true)
        {
            base.SwitchUnit(unit, recompute, recordEvent);
            UpdateMessage();
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager) { }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

        protected static EvaluationUnit NewUnit(EvaluationUnitManager mngr, string name, string display, string description, Bitmap icon)
        {
            var unit = new EvaluationUnit(name, display, description) { Icon = icon };
            mngr.RegisterUnit(unit);
            return unit;
        }

        protected static void NameInput(EvaluationUnit unit, string preset)
        {
            unit.RegisterInputParam(new Param_String(), "Material Name", "MatName", "Material name.", GH_ParamAccess.item, new GH_String(preset));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
        }

        /// <summary>A number input with a default, or optional and empty when <paramref name="preset"/> is null.</summary>
        protected static void Num(EvaluationUnit unit, string name, string description, double? preset)
        {
            if (preset.HasValue)
                unit.RegisterInputParam(new Param_Number(), name, name, description, GH_ParamAccess.item, new GH_Number(preset.Value));
            else
                unit.RegisterInputParam(new Param_Number(), name, name, description, GH_ParamAccess.item);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
        }

        protected static void Flag(EvaluationUnit unit, string name, string description)
        {
            unit.RegisterInputParam(new Param_Boolean(), name, name, description, GH_ParamAccess.item, new GH_Boolean(false));
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
        }

        protected static void Output(EvaluationUnit unit)
        {
            unit.RegisterOutputParam(new Param_GenericObject(), "Material", "Material",
                "The material, for the Fibers and Layers of a Fiber Section.");
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
    }

    /// <summary>Concrete for fibres: Concrete01, 02 or 04 from the Law menu.</summary>
    public class ConcreteLaws : FibreLaws
    {
        public const string Concrete01 = "Concrete01", Concrete02 = "Concrete02", Concrete04 = "Concrete04";

        public ConcreteLaws()
          : base("Concrete (Alpaca4d)", "Concrete",
            "A concrete law for the fibres of a section, from the Law menu. Compression is negative, stresses in kN/m2, strains dimensionless.\n" +
            "Concrete01 - a parabola to the peak, a straight fall to the crushing strain, no tension (the default);\n" +
            "Concrete02 - the same with a tensile strength and a linear tension softening;\n" +
            "Concrete04 - Popovics' curve, and an exponential tension branch when you give fct and et.")
        {
        }

        protected override string DefaultEvaluationUnit => Concrete01;

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            var c1 = Alpaca4d.Material.Concrete01.C2530;
            var u = NewUnit(mngr, Concrete01, "Concrete01", "Concrete with zero tensile strength.", Alpaca4d.Gh.Properties.Resources.concreate01);
            NameInput(u, c1.MatName);
            Num(u, "fco", "Compressive strength, in kN/m2. Negative, as every stress and strain in Concrete01 is.", c1.FpCo);
            Num(u, "fcu", "Crushing strength, in kN/m2 - what is left at EpsilonCu, past the peak. Negative, and no larger in magnitude than fco.", c1.FpCu);
            Num(u, "EpsilonCo", "Strain at fco. Negative, dimensionless.", c1.EpsilonCo);
            Num(u, "EpsilonCu", "Strain at fcu. Negative, dimensionless.", c1.EpsilonCu);
            Flag(u, "MinMax", "Wrap the material so a fibre fails once its strain leaves the range between EpsilonCu and zero.");
            Output(u);

            var c2 = Alpaca4d.Material.Concrete02.C2530;
            u = NewUnit(mngr, Concrete02, "Concrete02", "Concrete with a tensile strength and linear tension softening.", Alpaca4d.Gh.Properties.Resources.concreate02);
            NameInput(u, c2.MatName);
            Num(u, "fpc", "Compressive strength, in kN/m2. Negative.", c2.Fpc);
            Num(u, "EpsC0", "Strain at fpc. Negative, dimensionless.", c2.EpsC0);
            Num(u, "fpcu", "Crushing strength, in kN/m2. Negative.", c2.Fpcu);
            Num(u, "EpsU", "Strain at the crushing strength. Negative, dimensionless.", c2.EpsU);
            Num(u, "lambda", "Ratio between the unloading slope at EpsU and the initial slope. Default 0.2: with 0.1 the analysis can stall as the concrete reaches EpsU.", c2.Lambda);
            Num(u, "ft", "Tensile strength, in kN/m2. Positive.", c2.Ft);
            Num(u, "Ets", "Tension softening stiffness, in kN/m2: the slope of the falling branch. Positive.", c2.Ets);
            Flag(u, "MinMax", "Wrap the material so a fibre fails once its strain leaves the range between EpsU and zero.");
            Output(u);

            var c4 = Alpaca4d.Material.Concrete04.C2530;
            u = NewUnit(mngr, Concrete04, "Concrete04", "Concrete after Popovics, with an optional tension branch.", Alpaca4d.Gh.Properties.Resources.concreate02);
            NameInput(u, c4.MatName);
            Num(u, "fc", "Compressive strength, in kN/m2. Negative.", c4.Fc);
            Num(u, "EpsC", "Strain at fc. Negative, dimensionless.", c4.EpsC);
            Num(u, "EpsCu", "Crushing strain. Negative, dimensionless.", c4.EpsCu);
            Num(u, "Ec", "Initial elastic modulus, in kN/m2.", c4.Ec);
            Num(u, "fct", "Tensile strength, in kN/m2. Leave empty for no tension. Give it together with et.", null);
            Num(u, "et", "Strain at the tensile strength, dimensionless. Give it together with fct.", null);
            Num(u, "beta", "Exponential decay of the tension branch. Needs fct and et. Empty takes OpenSees' own.", null);
            Flag(u, "MinMax", "Wrap the material so a fibre fails once its strain leaves the range between EpsCu and zero.");
            Output(u);
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            string name = "";
            DA.GetData(0, ref name);
            bool minMax = false;
            switch (unit?.Name)
            {
                case Concrete02:
                {
                    var p = Alpaca4d.Material.Concrete02.C2530;
                    double fpc = p.Fpc, e0 = p.EpsC0, fpcu = p.Fpcu, eu = p.EpsU, lambda = p.Lambda, ft = p.Ft, ets = p.Ets;
                    DA.GetData(1, ref fpc); DA.GetData(2, ref e0); DA.GetData(3, ref fpcu); DA.GetData(4, ref eu);
                    DA.GetData(5, ref lambda); DA.GetData(6, ref ft); DA.GetData(7, ref ets); DA.GetData(8, ref minMax);
                    if (!(fpc < 0.0) || !(fpcu < 0.0) || !(e0 < 0.0) || !(eu < 0.0))
                    { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Concrete02 takes fpc, fpcu, EpsC0 and EpsU negative: compression is negative."); return; }
                    if (!(ft >= 0.0) || !(ets > 0.0))
                    { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "ft is zero or more, and Ets above zero."); return; }
                    DA.SetData(0, new Alpaca4d.Material.Concrete02(name, fpc, e0, fpcu, eu, lambda, ft, ets, minMax));
                    return;
                }
                case Concrete04:
                {
                    var p = Alpaca4d.Material.Concrete04.C2530;
                    double fc = p.Fc, ec0 = p.EpsC, ecu = p.EpsCu, modulus = p.Ec;
                    double? fct = null, et = null, beta = null;
                    DA.GetData(1, ref fc); DA.GetData(2, ref ec0); DA.GetData(3, ref ecu); DA.GetData(4, ref modulus);
                    DA.GetData(5, ref fct); DA.GetData(6, ref et); DA.GetData(7, ref beta); DA.GetData(8, ref minMax);
                    if (!(fc < 0.0) || !(ec0 < 0.0) || !(ecu < 0.0))
                    { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Concrete04 takes fc, EpsC and EpsCu negative: compression is negative."); return; }
                    if (!(modulus > 0.0))
                    { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Ec has to be positive."); return; }
                    if (fct.HasValue != et.HasValue || (beta.HasValue && !fct.HasValue))
                    { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "fct and et go together, and beta needs them."); return; }
                    DA.SetData(0, new Alpaca4d.Material.Concrete04(name, fc, ec0, ecu, modulus, fct, et, beta, minMax));
                    return;
                }
                default:
                {
                    var p = Alpaca4d.Material.Concrete01.C2530;
                    double fpco = p.FpCo, fpcu = p.FpCu, e0 = p.EpsilonCo, eu = p.EpsilonCu;
                    DA.GetData(1, ref fpco); DA.GetData(2, ref fpcu); DA.GetData(3, ref e0); DA.GetData(4, ref eu); DA.GetData(5, ref minMax);
                    DA.SetData(0, new Alpaca4d.Material.Concrete01(name, fpco, fpcu, e0, eu, minMax));
                    return;
                }
            }
        }

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.concreate01;

        /// <summary>Concrete01's GUID: files saved with it open on the Concrete01 law, with the inputs they had.</summary>
        public override Guid ComponentGuid => new Guid("{57BF0FDB-B333-491C-B9EB-50783029B8C2}");
    }

    /// <summary>Steel for fibres: Steel01, Steel02 or ReinforcingSteel from the Law menu.</summary>
    public class SteelLaws : FibreLaws
    {
        public const string Steel01 = "Steel01", Steel02 = "Steel02", ReinforcingSteel = "ReinforcingSteel";

        public SteelLaws()
          : base("Steel (Alpaca4d)", "Steel",
            "A steel law for the fibres of a section, from the Law menu. Stresses in kN/m2, strains dimensionless.\n" +
            "Steel01 - bilinear with kinematic hardening (the default);\n" +
            "Steel02 - Giuffre-Menegotto-Pinto: the same, with a smooth elastic to plastic transition;\n" +
            "ReinforcingSteel - a reinforcing bar, with a strain-hardening branch to its ultimate strength.")
        {
        }

        protected override string DefaultEvaluationUnit => Steel01;

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            var s1 = Alpaca4d.Material.Steel01.S355;
            var u = NewUnit(mngr, Steel01, "Steel01", "Bilinear steel with kinematic hardening.", Alpaca4d.Gh.Properties.Resources.Uniaxial_Material_steal01);
            NameInput(u, s1.MatName);
            Num(u, "fy", "Yield strength, in kN/m2.", s1.Fy);
            Num(u, "E0", "Initial elastic modulus, in kN/m2.", s1.E0);
            Num(u, "b", "Strain-hardening ratio, dimensionless: the post-yield tangent over E0.", s1.b);
            // The four isotropic hardening parameters are one optional group, and OpenSees takes them together
            // or not at all - so they are left empty rather than defaulted, and Steel01 rejects a half-filled set.
            Num(u, "a1", "Isotropic hardening in compression, dimensionless. Give a1-a4 together or leave all four empty.", null);
            Num(u, "a2", "Compression hardening limit, dimensionless. Give a1-a4 together or leave all four empty.", null);
            Num(u, "a3", "Isotropic hardening in tension, dimensionless. Give a1-a4 together or leave all four empty.", null);
            Num(u, "a4", "Tension hardening limit, dimensionless. Give a1-a4 together or leave all four empty.", null);
            Output(u);

            var s2 = Alpaca4d.Material.Steel02.S355;
            u = NewUnit(mngr, Steel02, "Steel02", "Giuffre-Menegotto-Pinto steel.", Alpaca4d.Gh.Properties.Resources.Uniaxial_Material_steal02);
            NameInput(u, s2.MatName);
            Num(u, "fy", "Yield strength, in kN/m2.", s2.Fy);
            Num(u, "E0", "Initial elastic modulus, in kN/m2.", s2.E0);
            Num(u, "b", "Strain-hardening ratio, dimensionless: the post-yield tangent over E0.", s2.B);
            Num(u, "R0", "Shape of the elastic to plastic transition, 10 to 20. Default 18.", s2.R0);
            Num(u, "cR1", "Shape of the transition after a reversal. Default 0.925.", s2.CR1);
            Num(u, "cR2", "Shape of the transition after a reversal. Default 0.15.", s2.CR2);
            Num(u, "a1", "Isotropic hardening in compression, dimensionless. Give a1-a4 together or leave all four empty.", null);
            Num(u, "a2", "Compression hardening limit, dimensionless. Give a1-a4 together or leave all four empty.", null);
            Num(u, "a3", "Isotropic hardening in tension, dimensionless. Give a1-a4 together or leave all four empty.", null);
            Num(u, "a4", "Tension hardening limit, dimensionless. Give a1-a4 together or leave all four empty.", null);
            Num(u, "sigInit", "Initial stress, in kN/m2. Empty for none.", null);
            Output(u);

            var r = Alpaca4d.Material.ReinforcingSteel.B450C;
            u = NewUnit(mngr, ReinforcingSteel, "ReinforcingSteel", "A reinforcing bar with strain hardening.", Alpaca4d.Gh.Properties.Resources.reinforcing_steel_material);
            NameInput(u, r.MatName);
            Num(u, "fy", "Yield strength, in kN/m2.", r.Fy);
            Num(u, "fu", "Ultimate strength, in kN/m2.", r.Fu);
            Num(u, "Es", "Initial elastic modulus, in kN/m2.", r.Es);
            Num(u, "Esh", "Tangent modulus at the onset of strain hardening, in kN/m2. Greater than zero.", r.Esh);
            Num(u, "esh", "Strain at the onset of strain hardening, dimensionless. Past the yield strain fy/Es.", r.EpsilonSh);
            Num(u, "eult", "Strain at fu, dimensionless.", r.EpsilonUlt);
            Flag(u, "MinMax", "Wrap the material so a bar fails once its strain passes eult in either direction.");
            Output(u);
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            string name = "";
            DA.GetData(0, ref name);
            switch (unit?.Name)
            {
                case Steel02:
                {
                    var p = Alpaca4d.Material.Steel02.S355;
                    double fy = p.Fy, e0 = p.E0, b = p.B, r0 = p.R0, cr1 = p.CR1, cr2 = p.CR2;
                    double? a1 = null, a2 = null, a3 = null, a4 = null, sigInit = null;
                    DA.GetData(1, ref fy); DA.GetData(2, ref e0); DA.GetData(3, ref b); DA.GetData(4, ref r0); DA.GetData(5, ref cr1); DA.GetData(6, ref cr2);
                    DA.GetData(7, ref a1); DA.GetData(8, ref a2); DA.GetData(9, ref a3); DA.GetData(10, ref a4); DA.GetData(11, ref sigInit);
                    if (!(fy > 0.0) || !(e0 > 0.0))
                    { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "fy and E0 have to be positive."); return; }
                    var steel = new Alpaca4d.Material.Steel02(name, fy, e0, b, r0, cr1, cr2) { A1 = a1, A2 = a2, A3 = a3, A4 = a4, SigInit = sigInit };
                    DA.SetData(0, steel);
                    return;
                }
                case ReinforcingSteel:
                {
                    var p = Alpaca4d.Material.ReinforcingSteel.B450C;
                    double fy = p.Fy, fu = p.Fu, es = p.Es, esh = p.Esh, eSh = p.EpsilonSh, eUlt = p.EpsilonUlt;
                    bool minMax = false;
                    DA.GetData(1, ref fy); DA.GetData(2, ref fu); DA.GetData(3, ref es); DA.GetData(4, ref esh);
                    DA.GetData(5, ref eSh); DA.GetData(6, ref eUlt); DA.GetData(7, ref minMax);
                    DA.SetData(0, new Alpaca4d.Material.ReinforcingSteel(name, fy, fu, es, esh, eSh, eUlt, minMax));
                    return;
                }
                default:
                {
                    var p = Alpaca4d.Material.Steel01.S355;
                    double fy = p.Fy, e0 = p.E0, b = p.b;
                    double? a1 = null, a2 = null, a3 = null, a4 = null;
                    DA.GetData(1, ref fy); DA.GetData(2, ref e0); DA.GetData(3, ref b);
                    DA.GetData(4, ref a1); DA.GetData(5, ref a2); DA.GetData(6, ref a3); DA.GetData(7, ref a4);
                    DA.SetData(0, new Alpaca4d.Material.Steel01(name, fy, e0, b, a1, a2, a3, a4));
                    return;
                }
            }
        }

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Uniaxial_Material_steal01;

        /// <summary>Steel01's GUID: files saved with it open on the Steel01 law, with the inputs they had.</summary>
        public override Guid ComponentGuid => new Guid("{DEB86811-4429-426F-8A7B-02DF89C5A074}");
    }

    /// <summary>Other laws for fibres: Elastic-Perfectly Plastic, or Hysteretic.</summary>
    public class OtherLaws : FibreLaws
    {
        public const string ElasticPP = "ElasticPP", Hysteretic = "Hysteretic", Elastic = "Elastic";

        public OtherLaws()
          : base("Other Laws (Alpaca4d)", "OtherLaws",
            "A law that is neither concrete nor steel, for the fibres of a section, from the Law menu. Stresses in kN/m2, strains dimensionless.\n" +
            "ElasticPP - elastic, then perfectly plastic (the default);\n" +
            "Hysteretic - a bilinear or trilinear backbone, each side of its own, with pinching and damage;\n" +
            "Elastic - linear, to a rupture strain if you give one: a carbon or glass fibre laminate, a tendon.")
        {
        }

        protected override string DefaultEvaluationUnit => ElasticPP;

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            var icon = Alpaca4d.Gh.Properties.Resources.uniaxial_Elastic_Material__Alpaca4d_;
            var u = NewUnit(mngr, ElasticPP, "ElasticPP", "Elastic, then perfectly plastic.", icon);
            NameInput(u, "ElasticPP");
            Num(u, "E", "Elastic modulus, in kN/m2.", 210000000.0);
            Num(u, "epsyP", "Yield strain in tension, dimensionless. Positive. Default 0.0017, S355.", 0.0017);
            Num(u, "epsyN", "Yield strain in compression, dimensionless. Negative. Empty: the mirror of epsyP.", null);
            Num(u, "eps0", "Strain at zero stress to start from, dimensionless. Empty for none.", null);
            Output(u);

            u = NewUnit(mngr, Elastic, "Elastic", "Linear, to a rupture strain: a fibre laminate or a tendon.", icon);
            NameInput(u, "Elastic");
            Num(u, "E", "Elastic modulus, in kN/m2. Carbon fibre laminate 160 GPa: 160000000.", 160000000.0);
            Num(u, "Eneg", "Elastic modulus in compression, in kN/m2. Empty: the same as E.", null);
            Num(u, "RuptureTension", "Strain at rupture in tension, dimensionless. Positive. Empty for none. Carbon fibre laminate: 0.017.", 0.017);
            Num(u, "RuptureCompression", "Strain at rupture in compression, dimensionless. Positive. Empty for none.", null);
            Output(u);

            u = NewUnit(mngr, Hysteretic, "Hysteretic", "A tri-linear backbone with pinching and damage.", icon);
            NameInput(u, "Hysteretic");
            Num(u, "e1p", "Strain at the first point of the backbone in tension, dimensionless. Positive.", 0.00169);
            Num(u, "s1p", "Stress at the first point in tension, in kN/m2. Positive.", 355000.0);
            Num(u, "e2p", "Strain at the second point in tension, dimensionless.", 0.05);
            Num(u, "s2p", "Stress at the second point in tension, in kN/m2.", 450000.0);
            Num(u, "e3p", "Strain at a third point in tension, dimensionless. Empty for a bilinear backbone. Give it with s3p.", null);
            Num(u, "s3p", "Stress at a third point in tension, in kN/m2. Give it with e3p.", null);
            Num(u, "e1n", "Strain at the first point in compression. Negative. Empty: the mirror of the tension side, all of it.", null);
            Num(u, "s1n", "Stress at the first point in compression, in kN/m2. Negative.", null);
            Num(u, "e2n", "Strain at the second point in compression. Negative.", null);
            Num(u, "s2n", "Stress at the second point in compression, in kN/m2. Negative.", null);
            Num(u, "e3n", "Strain at a third point in compression. Negative. Give it with s3n, and only if there is one in tension.", null);
            Num(u, "s3n", "Stress at a third point in compression, in kN/m2. Negative.", null);
            Num(u, "pinchX", "Pinching of strain during reloading, 0 to 1. Default 1: none.", 1.0);
            Num(u, "pinchY", "Pinching of stress during reloading, 0 to 1. Default 1: none.", 1.0);
            Num(u, "damage1", "Damage from ductility. Default 0.", 0.0);
            Num(u, "damage2", "Damage from energy. Default 0.", 0.0);
            Num(u, "beta", "Power of the degrading unloading stiffness. Empty for none.", null);
            Output(u);
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            string name = "";
            DA.GetData(0, ref name);
            if (unit?.Name == Elastic)
            {
                double modulus = 160000000.0;
                double? eneg = null, ruptureT = 0.017, ruptureC = null;
                DA.GetData(1, ref modulus); DA.GetData(2, ref eneg); DA.GetData(3, ref ruptureT); DA.GetData(4, ref ruptureC);
                if (!(modulus > 0.0) || (eneg.HasValue && !(eneg > 0.0)))
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "E, and Eneg when given, have to be positive."); return; }
                if ((ruptureT.HasValue && !(ruptureT > 0.0)) || (ruptureC.HasValue && !(ruptureC > 0.0)))
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Rupture strains are positive, for both sides."); return; }
                DA.SetData(0, new Alpaca4d.Material.ElasticFibre(name, modulus, eneg, ruptureT, ruptureC));
                return;
            }
            if (unit?.Name == Hysteretic)
            {
                double e1p = 0.0, s1p = 0.0, e2p = 0.0, s2p = 0.0;
                double? e3p = null, s3p = null, e1n = null, s1n = null, e2n = null, s2n = null, e3n = null, s3n = null, beta = null;
                double pinchX = 1.0, pinchY = 1.0, damage1 = 0.0, damage2 = 0.0;
                DA.GetData(1, ref e1p); DA.GetData(2, ref s1p); DA.GetData(3, ref e2p); DA.GetData(4, ref s2p); DA.GetData(5, ref e3p); DA.GetData(6, ref s3p);
                DA.GetData(7, ref e1n); DA.GetData(8, ref s1n); DA.GetData(9, ref e2n); DA.GetData(10, ref s2n); DA.GetData(11, ref e3n); DA.GetData(12, ref s3n);
                DA.GetData(13, ref pinchX); DA.GetData(14, ref pinchY); DA.GetData(15, ref damage1); DA.GetData(16, ref damage2); DA.GetData(17, ref beta);

                if (e3p.HasValue != s3p.HasValue || e3n.HasValue != s3n.HasValue)
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A third point needs its strain and its stress."); return; }
                var positive = new List<(double, double)> { (e1p, s1p), (e2p, s2p) };
                if (e3p.HasValue) positive.Add((e3p.Value, s3p.Value));
                if (!(e1p > 0.0) || !(s1p > 0.0) || !(e2p > e1p) || !(s2p > s1p) || (e3p.HasValue && !(e3p > e2p && s3p > s2p)))
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The tension backbone rises: strains and stresses positive and growing from point to point."); return; }

                List<(double, double)> negative = null;
                if (e1n.HasValue || s1n.HasValue || e2n.HasValue || s2n.HasValue || e3n.HasValue)
                {
                    if (!(e1n.HasValue && s1n.HasValue && e2n.HasValue && s2n.HasValue) || e3n.HasValue != e3p.HasValue)
                    { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The compression side has as many points as the tension side, each with a strain and a stress - or none, to mirror."); return; }
                    negative = new List<(double, double)> { (e1n.Value, s1n.Value), (e2n.Value, s2n.Value) };
                    if (e3n.HasValue) negative.Add((e3n.Value, s3n.Value));
                    if (!(e1n < 0.0) || !(s1n < 0.0) || !(e2n < e1n) || !(s2n < s1n) || (e3n.HasValue && !(e3n < e2n && s3n < s2n)))
                    { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The compression backbone falls: strains and stresses negative and growing in size from point to point."); return; }
                }
                if (!(pinchX >= 0.0 && pinchX <= 1.0) || !(pinchY >= 0.0 && pinchY <= 1.0))
                { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "pinchX and pinchY are between 0 and 1."); return; }

                var law = new Alpaca4d.Material.Hysteretic(name, positive.Select(p => p.Item1).ToArray(), positive.Select(p => p.Item2).ToArray())
                {
                    PinchX = pinchX, PinchY = pinchY, Damage1 = damage1, Damage2 = damage2, Beta = beta,
                };
                if (negative != null)
                    law.SetNegative(negative.Select(p => p.Item1).ToArray(), negative.Select(p => p.Item2).ToArray());
                DA.SetData(0, law);
                return;
            }

            double e = 210000000.0, epsyP = 0.0017;
            double? epsyN = null, eps0 = null;
            DA.GetData(1, ref e); DA.GetData(2, ref epsyP); DA.GetData(3, ref epsyN); DA.GetData(4, ref eps0);
            if (!(e > 0.0) || !(epsyP > 0.0))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "E and epsyP have to be positive."); return; }
            if (epsyN.HasValue && !(epsyN < 0.0))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "epsyN is negative."); return; }
            DA.SetData(0, new Alpaca4d.Material.ElasticPP(name, e, epsyP, epsyN, eps0));
        }

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.uniaxial_Elastic_Material__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{B3F7D2A5-61C8-4E94-8A0D-5E2C9F174B38}");
    }
}
