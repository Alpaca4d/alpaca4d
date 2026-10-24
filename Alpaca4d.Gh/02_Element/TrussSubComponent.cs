using Alpaca4d.Element;
using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The Truss type of the beam component: a bar that carries axial force only, in tension and
    /// compression or in one of the two. See <see cref="Alpaca4d.Element.Truss"/>.
    /// </summary>
    internal class TrussSubComponent : SubComponent
    {
        public override string name() => "Truss (Alpaca4d)";
        public override string display_name() => "Truss";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(),
                "A bar that carries axial force only: in tension and compression, or in one of the two - a cable or a bracing " +
                "rod that goes slack when pushed, a strut that lets go when pulled. Always corotational, so it holds for large " +
                "displacements too. A line load on it goes to its two end nodes, half each. A tension-only or compression-only " +
                "bar needs a non-linear analysis to let go: Newton, in several steps.");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.Force_Beam_Column__Alpaca4d_;
            mngr.RegisterUnit(evaluationUnit);

            evaluationUnit.RegisterInputParam(new Param_Curve(), "Line", "Line", $"One bar per line [{Units.Length}]. Its two ends become nodes.", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = false;

            evaluationUnit.RegisterInputParam(new Param_GenericObject(), "Section", "Section",
                "The bar's area and material: any beam section, a tube from Section Library included. Its shape only draws the bar.",
                GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = false;

            evaluationUnit.RegisterInputParam(new Param_String(), "WorksIn", "WorksIn",
                "Both, Tension or Compression: which way the bar carries load. A tension-only bar goes slack when pushed, " +
                "a compression-only one lets go when pulled. Connect a Value List to pick from them.",
                GH_ParamAccess.item, new GH_String(AxialBehaviour.Both.ToString()));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].EnumInput = Enum.GetNames(typeof(AxialBehaviour)).ToList();

            evaluationUnit.RegisterInputParam(new Param_Number(), "Prestress", "Prestress",
                $"The axial force the bar starts with [{Units.Force}], tension positive. A cable needs it: straight and " +
                "slack, it has no stiffness across its length. A tension-only bar takes zero or more, a compression-only " +
                "one zero or less.",
                GH_ParamAccess.item, new GH_Number(0.0));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Vector(), "ZAxis", "ZAxis",
                "Local z axis, squared off the bar. It only turns the section, for drawing and for the steel check; left " +
                "empty it is horizontal, as on a beam.",
                GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Colour(), "Colour", "Colour", "Colour to draw the bar in. Display only; it changes nothing in the analysis.",
                GH_ParamAccess.item, new GH_Colour(Alpaca4d.Colors.DefaultBeam));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_String(), "ElementId", "ElementId", ElementIdentity.ElementIdInput, GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Error;

            Curve curve = null;
            if (!DA.GetData(0, ref curve)) return;
            if (!curve.IsLinear() || curve.PointAtStart.DistanceTo(curve.PointAtEnd) <= 0.0)
            {
                msg = "A truss is a straight bar between two different points: give it a line.";
                return;
            }
            var line = new LineCurve(curve.PointAtStart, curve.PointAtEnd);

            Alpaca4d.Generic.IUniaxialSection section = null;
            if (!DA.GetData(1, ref section)) return;

            string worksIn = AxialBehaviour.Both.ToString();
            DA.GetData(2, ref worksIn);
            if (!Enum.TryParse((worksIn ?? "").Trim(), true, out AxialBehaviour behaviour) || !Enum.IsDefined(typeof(AxialBehaviour), behaviour))
            {
                msg = $"WorksIn is \"{worksIn}\". Give Both, Tension or Compression.";
                return;
            }

            double prestress = 0.0;
            DA.GetData(3, ref prestress);

            Vector3d zAxis = Vector3d.Zero;
            if (DA.GetData(4, ref zAxis))
                zAxis = Alpaca4d.Utils.AlignPlane(Alpaca4d.Utils.PerpendicularFrame(line), zAxis).XAxis;
            else
                zAxis = Alpaca4d.Utils.DefaultLocalZ(line);

            var colour = Alpaca4d.Colors.DefaultBeam;
            DA.GetData(5, ref colour);

            string elementId = null;
            DA.GetData(6, ref elementId);

            var truss = new Alpaca4d.Element.Truss(line, section, zAxis, behaviour, prestress)
            {
                Color = colour,
                ElementId = elementId,
            };

            var problem = truss.Problem();
            if (problem != null)
            {
                msg = problem;
                return;
            }

            DA.SetData(0, truss);
        }
    }
}
