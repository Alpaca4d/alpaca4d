using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Drawing;

namespace Alpaca4d.Gh
{
    internal class ForceBeamColumnSubComponent : SubComponent
    {
        public override string name() => "ForceBeamColumn (Alpaca4d)";
        public override string display_name() => "ForceBeamColumn";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(), "Construct a ForceBeamColumn element.");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.Force_Beam_Column__Alpaca4d_;
            mngr.RegisterUnit(evaluationUnit);

            evaluationUnit.RegisterInputParam(new Param_Curve(), "Line", "Line", $"[{Units.Length}]", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = false;

            evaluationUnit.RegisterInputParam(new Param_GenericObject(), "Section", "Section", "Cross-section.", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = false;

            evaluationUnit.RegisterInputParam(new Param_GenericObject(), "GeometricTransformation", "GeomTransf", "Optional geometric transformation. If omitted, Linear is used.", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Vector(), "ZAxis", "ZAxis", "Local Z-axis direction (optional).", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Colour(), "Colour", "Colour", "", GH_ParamAccess.item, new GH_Colour(Alpaca4d.Colors.DefaultBeam));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_String(), "ElementId", "ElementId", ElementIdentity.ElementIdInput, GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;
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

            Color color = Alpaca4d.Colors.DefaultBeam;
            DA.GetData(4, ref color);

            string elementId = null;
            DA.GetData(5, ref elementId);

            var element = new Alpaca4d.Element.ForceBeamColumn(line, section, geomTransf);
            element.Color = color;
            element.ElementId = elementId;

            DA.SetData(0, element);
        }
    }
}
