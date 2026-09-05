using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Drawing;

namespace Alpaca4d.Gh
{
    internal class WithHingesSubComponent : SubComponent
    {
        public override string name() => "WithHinges (Alpaca4d)";
        public override string display_name() => "WithHinges";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(), "Construct a BeamWithHinges element using HingeRadau integration.");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.Force_Beam_Column__Alpaca4d_;
            mngr.RegisterUnit(evaluationUnit);

            evaluationUnit.RegisterInputParam(new Param_Curve(), "Line", "Line", $"[{Units.Length}]", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = false;

            evaluationUnit.RegisterInputParam(new Param_GenericObject(), "Section", "Section", "Interior cross-section.", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = false;

            evaluationUnit.RegisterInputParam(new Param_GenericObject(), "GeometricTransformation", "GeomTransf", "Optional geometric transformation. If omitted, Linear is used.", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Vector(), "ZAxis", "ZAxis", "Local Z-axis direction (optional).", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_GenericObject(), "ReleaseI", "ReleaseI", "Release condition at the I end.", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "LpI", "LpI", LpDescription("I"), GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_GenericObject(), "ReleaseJ", "ReleaseJ", "Release condition at the J end.", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "LpJ", "LpJ", LpDescription("J"), GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Colour(), "Colour", "Colour", "", GH_ParamAccess.item, new GH_Colour(Alpaca4d.Colors.DefaultBeamWithHinges));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_String(), "ElementId", "ElementId", ElementIdentity.ElementIdInput, GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;
        }

        /// <summary>
        /// The hinge length is asked for as a fraction of L, not as a length: the released
        /// flexibility of a 1e-6 hinge section scales with lp/L, so a ratio means the same thing
        /// whether the model is drawn in metres or millimetres, and on beams of any span.
        /// </summary>
        private static string LpDescription(string end)
        {
            return $"Plastic hinge length at the {end} end, as a fraction of the element length L "
                 + $"(so {Alpaca4d.Element.BeamWithHinges.DefaultLpRatio} means "
                 + $"{Alpaca4d.Element.BeamWithHinges.DefaultLpRatio:P0} of L). "
                 + $"Leave empty for {Alpaca4d.Element.BeamWithHinges.DefaultLpRatio}; other values are clamped to "
                 + $"[{Alpaca4d.Element.BeamWithHinges.MinLpRatio}, {Alpaca4d.Element.BeamWithHinges.MaxLpRatio}].";
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            Curve line = null;
            if (!DA.GetData(0, ref line)) return;

            Alpaca4d.Generic.IUniaxialSection section = null;
            if (!DA.GetData(1, ref section)) return;

            Vector3d zAxis = Vector3d.Zero;
            if (DA.GetData(3, ref zAxis))
            {
                Plane perpFrame = Alpaca4d.Utils.PerpendicularFrame(line);
                zAxis = Alpaca4d.Utils.AlignPlane(perpFrame, zAxis).XAxis;
            }
            else
            {
                Plane perpFrame = Alpaca4d.Utils.PerpendicularFrame(line);
                zAxis = perpFrame.XAxis;
            }

            Alpaca4d.Element.GeomTransf geomTransf = null;
            if (!DA.GetData(2, ref geomTransf))
            {
                geomTransf = new Alpaca4d.Element.GeomTransf(Alpaca4d.Element.GeomTransfType.Linear, line, zAxis);
            }

            Alpaca4d.Element.Release releaseI = null;
            DA.GetData(4, ref releaseI);
            if (releaseI == null) releaseI = Alpaca4d.Element.Release.FullFixed;

            double? lpRatioI = OptionalRatio(DA, 5);

            Alpaca4d.Element.Release releaseJ = null;
            DA.GetData(6, ref releaseJ);
            if (releaseJ == null) releaseJ = Alpaca4d.Element.Release.FullFixed;

            double? lpRatioJ = OptionalRatio(DA, 7);

            ValidateHingeRatios(lpRatioI, lpRatioJ, out msg, out level);

            Color color = Alpaca4d.Colors.DefaultBeamWithHinges;
            DA.GetData(8, ref color);

            string elementId = null;
            DA.GetData(9, ref elementId);

            var element = new Alpaca4d.Element.BeamWithHinges(line, section, geomTransf, releaseI, lpRatioI, releaseJ, lpRatioJ);
            element.Color = color;
            element.ElementId = elementId;

            DA.SetData(0, element);
        }

        /// <summary>An empty input is left as null, which is what asks for the default ratio.</summary>
        private static double? OptionalRatio(IGH_DataAccess DA, int index)
        {
            double value = 0.0;
            return DA.GetData(index, ref value) ? (double?)value : null;
        }

        /// <summary>
        /// Reports how the raw LpI/LpJ ratios will be treated by
        /// <see cref="Alpaca4d.Element.BeamWithHinges.ResolveLpRatio"/>. The 1/4 check runs on the
        /// raw values, before clamping, so the user still hears about a request that HingeRadau
        /// could not have integrated: its interior weights are 0.5 - 2*(lpI+lpJ)/L.
        /// </summary>
        private static void ValidateHingeRatios(double? lpRatioI, double? lpRatioJ, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Remark;

            // A blank input is not a request for zero, it asks for the default share of L.
            double requestedI = lpRatioI.HasValue && lpRatioI.Value > 0.0
                ? lpRatioI.Value : Alpaca4d.Element.BeamWithHinges.DefaultLpRatio;
            double requestedJ = lpRatioJ.HasValue && lpRatioJ.Value > 0.0
                ? lpRatioJ.Value : Alpaca4d.Element.BeamWithHinges.DefaultLpRatio;

            if (requestedI + requestedJ >= 0.25)
            {
                msg = $"LpI + LpJ ({requestedI + requestedJ:G4}) must stay below 0.25 or the HingeRadau "
                    + $"interior weights 0.5-2*(LpI+LpJ)/L turn negative. Clamped to {Alpaca4d.Element.BeamWithHinges.MaxLpRatio} each.";
                level = GH_RuntimeMessageLevel.Warning;
                return;
            }

            var clamped = new System.Collections.Generic.List<string>();
            if (WasClamped(lpRatioI)) clamped.Add("LpI");
            if (WasClamped(lpRatioJ)) clamped.Add("LpJ");

            if (clamped.Count > 0)
            {
                msg = $"{string.Join(" and ", clamped)} clamped to the "
                    + $"[{Alpaca4d.Element.BeamWithHinges.MinLpRatio}, {Alpaca4d.Element.BeamWithHinges.MaxLpRatio}] "
                    + "range of lp/L.";
            }
        }

        private static bool WasClamped(double? ratio)
        {
            return ratio.HasValue
                && ratio.Value > 0.0
                && Alpaca4d.Element.BeamWithHinges.ResolveLpRatio(ratio) != ratio.Value;
        }
    }
}
