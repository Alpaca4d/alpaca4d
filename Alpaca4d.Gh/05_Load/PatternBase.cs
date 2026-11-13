using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Alpaca4d.Gh;

namespace Alpaca4d.Gh
{
    public class PatternBase : GH_SwitcherComponent
    {
        private List<SubComponent> _subcomponents = new List<SubComponent>();
        public override string UnitMenuName => "Load Pattern";
        protected override string DefaultEvaluationUnit => _subcomponents.Count > 0 ? _subcomponents[0].name() : "PlainPattern";
        public override Guid ComponentGuid => new Guid("{C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B}");
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => null;

        public PatternBase()
            : base("Load Pattern (Alpaca4d)", "Load Pattern",
              "Groups loads into a load case for Assemble Model: a time series that scales them through the analysis, a " +
              "factor, and a name. Mass from Loads names a pattern by it, to make its loads' weight mass.",
              "Alpaca4d", "05_Load")
        {
            ((GH_Component)this).Hidden = false;
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.RegisterParam(new Param_GenericObject(), "LoadPattern", "LoadPattern", "Load Pattern");
        }

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            _subcomponents.Add(new PatternPlain());
            _subcomponents.Add(new PatternUniformExcitation());

            foreach (SubComponent item in _subcomponents)
            {
                item.registerEvaluationUnits(mngr);
            }
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            foreach (SubComponent item in _subcomponents)
            {
                item.OnComponentLoaded();
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA, EvaluationUnit unit)
        {
            if (unit == null)
            {
                return;
            }
            foreach (SubComponent item in _subcomponents)
            {
                if (unit.Name.Equals(item.name()))
                {
                    item.SolveInstance(DA, out var msg, out var level);
                    if (msg != "")
                    {
                        ((GH_ActiveObject)this).AddRuntimeMessage(level, msg);
                    }
                    return;
                }
            }
            throw new Exception("Invalid sub-component");
        }
        // Part of the code that allows to extend the menu with additional items
        // Right click on the component to see the options
        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
        }
        private void Menu_ActivateUnit(object sender, EventArgs e)
        {
            try
            {
                EvaluationUnit evaluationUnit = (EvaluationUnit)((ToolStripMenuItem)sender).Tag;
                if (evaluationUnit != null)
                {
                    SwitchUnit(evaluationUnit);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
