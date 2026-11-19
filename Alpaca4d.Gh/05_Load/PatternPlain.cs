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

        /// <summary>The Duration menu's drop-down: "Not set", then EN 1995-1-1's classes, permanent first.</summary>
        private MenuDropDown _duration;
        private const string NotSet = "Not set";

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

            // Last, as new inputs go: a file saved before it still lines up with the ones before.
            evaluationUnit.RegisterInputParam(new Param_String(), "Name", "Name",
                "The load case's name, as Load Combination and Mass from Loads call the pattern. Empty: no name.", GH_ParamAccess.item);
            evaluationUnit.Inputs[evaluationUnit.Inputs.Count - 1].Parameter.Optional = true;

            // The load-duration class, for timber's k_mod: a drop-down in a menu folded away, since
            // only the timber check reads it. Saved with the unit, as its menus are; a file from
            // before it opens with Not set.
            var menu = new GH_ExtendableMenu(0, "PlainPattern_Duration")
            {
                Name = "Duration",
                Header = "Load duration, for timber's k_mod (EN 1995-1-1 2.3.1.2)"
            };
            var panel = new MenuPanel(0, "PlainPattern_DurationPanel") { Header = "Duration" };
            _duration = new MenuDropDown(0, "Duration", "Duration") { VisibleItemCount = LoadDurations.Names.Length + 1 };
            _duration.AddItem(NotSet, NotSet);
            foreach (var duration in LoadDurations.Names)
                _duration.AddItem(duration, duration);
            _duration.ValueChanged += (sender, e) => _unit?.Component?.ExpireSolution(true);
            panel.AddControl(_duration);
            menu.AddControl(panel);
            evaluationUnit.AddMenu(menu);
        }

        /// <summary>The class the Duration menu shows, or null for Not set.</summary>
        private LoadDuration? Duration =>
            _duration == null || _duration.Value <= 0 || _duration.Value > LoadDurations.Names.Length
                ? (LoadDuration?)null
                : (LoadDuration)(_duration.Value - 1);

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

            string name = null;
            DA.GetData(3, ref name);

            var load = new Alpaca4d.Loads.LoadPattern(PatternType.Plain, timeSeries, loads, factor)
            {
                Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                Duration = this.Duration,
            };

            DA.SetData(0, load);
        }
    }
}
