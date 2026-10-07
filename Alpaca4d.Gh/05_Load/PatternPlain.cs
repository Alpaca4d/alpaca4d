using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alpaca4d.TimeSeries;
using Alpaca4d.Loads;
using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    internal class PatternPlain : SubComponent
    {
        public override string name() => "PlainPattern (Alpaca4d)";
        public override string display_name() => "PlainPattern";

        private EvaluationUnit _unit;

        public override void registerEvaluationUnits(EvaluationUnitManager mngr)
        {
            EvaluationUnit evaluationUnit = new EvaluationUnit(name(), display_name(), "Plain Load Pattern");
            evaluationUnit.Icon = Alpaca4d.Gh.Properties.Resources.Load_pattern__Alpaca4d_;
            mngr.RegisterUnit(evaluationUnit);
            _unit = evaluationUnit;

            // No default object in either input. An Alpaca object cannot be written into a file, so
            // a default held as persistent data came back from copy-paste or a reopened file as
            // text - "Invalid cast: Text » ITimeSeries", and the same for the loads. A missing time
            // series is filled in by SolveInstance instead, where nothing has to be saved.
            evaluationUnit.RegisterInputParam(new Param_GenericObject(), "TimeSeries", "TimeSeries", "Time series for the load pattern. Left empty, a constant series - the loads at full size throughout.", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            evaluationUnit.RegisterInputParam(new Param_GenericObject(), "Loads", "Loads", "List of loads to apply", GH_ParamAccess.list);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = false;

            evaluationUnit.RegisterInputParam(new Param_Number(), "Factor", "Factor", "Constant factor", GH_ParamAccess.item, new GH_Number(1));
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;
        }

        /// <summary>
        /// Copies and files made before the defaults were taken out still carry them, as text that
        /// cannot be cast. Nothing an Alpaca object can be survives in persistent data, so whatever
        /// is there is cleared; and the time series, required while it had a default, is optional
        /// again so an unwired one still solves.
        /// </summary>
        public override void OnComponentLoaded()
        {
            if (_unit == null) return;

            foreach (var plug in _unit.Inputs)
            {
                if (plug.Parameter is Param_GenericObject generic && !generic.PersistentData.IsEmpty)
                    generic.PersistentData.Clear();

                if (plug.Parameter.Name == "TimeSeries")
                    plug.Parameter.Optional = true;
            }
        }

        public override void SolveInstance(IGH_DataAccess DA, out string msg, out GH_RuntimeMessageLevel level)
        {
            msg = "";
            level = GH_RuntimeMessageLevel.Warning;

            Alpaca4d.Generic.ITimeSeries timeSeries = TimeSeries.Constant.Default();
            DA.GetData(0, ref timeSeries);

            List<ILoad> loads = new List<ILoad>();
            DA.GetDataList(1, loads);

            double factor = 1;
            DA.GetData(2, ref factor);

            var load = new Alpaca4d.Loads.LoadPattern(PatternType.Plain, timeSeries, loads, factor);

            DA.SetData(0, load);
        }
    }
}
