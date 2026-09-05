using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d.Loads;
using Alpaca4d.Generic;
using Alpaca4d;

namespace Alpaca4d.Gh
{
    internal class PatternUniformExcitation : SubComponent
    {
        public override string name() => "UniformExcitation (Alpaca4d)";
        public override string display_name() => "UniformExcitation";

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(), "Uniform Excitation Pattern");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.Uniform_excitation;
            mngr.RegisterUnit(evaluationUnit);

            evaluationUnit.RegisterInputParam(new Param_String(), "Dof", "Dof", "Direction the ground motion acts in. Connect a 'ValueList'.\n" +
                "x, y and z translate along the global axes; xx, yy and zz rotate about them.", GH_ParamAccess.item, new GH_String("X"));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = false;
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].EnumInput = Enum.GetNames(typeof(Alpaca4d.Loads.Direction)).ToList();;

            evaluationUnit.RegisterInputParam(new Param_GenericObject(), "TimeSeries", "TimeSeries", "Time series for the excitation", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = false;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Velocity", "Velocity", $"The initial velocity [{Units.Length}/s]", GH_ParamAccess.item, new GH_Number(0));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Factor", "Factor", "Constant factor", GH_ParamAccess.item, new GH_Number(1));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            string _direction = "X";
            DA.GetData(0, ref _direction);

            Alpaca4d.Generic.ITimeSeries timeSeries = null;
            DA.GetData(1, ref timeSeries);

            double velocity = 0;
            DA.GetData(2, ref velocity);

            double factor = 1;
            DA.GetData(3, ref factor);

            var direction = (Alpaca4d.Loads.Direction)Enum.Parse(typeof(Alpaca4d.Loads.Direction), _direction, true);

            var load = Alpaca4d.Loads.LoadPattern.CreateUniformExcitation(direction, timeSeries, velocity, factor);

            DA.SetData(0, load);
        }
    }
}
