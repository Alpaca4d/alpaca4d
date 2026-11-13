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
    public class IntegratorBase : GH_SwitcherComponent
    {
        private List<SubComponent> _subcomponents = new List<SubComponent>();
        public override string UnitMenuName => "Integrator (Alpaca4d)";
        protected override string DefaultEvaluationUnit => _subcomponents.Count > 0 ? _subcomponents[0].name() : "LoadControl";
        public override Guid ComponentGuid => new Guid("{A5F2B8C3-D1E4-4F5A-B6C7-8D9E0F1A2B3C}");
        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override Bitmap Icon => null;

        public IntegratorBase()
            : base("Integrator (Alpaca4d)", "Integrator",
              "How the analysis advances from one step to the next.\n" +
              "Load Control scales the loads by a load factor and is the static integrator; Newmark " +
              "and Central Difference step forward in time and are the transient ones. Feeds the " +
              "Integrator input of Analysis Settings.",
              "Alpaca4d", "07_Analysis")
        {
            ((GH_Component)this).Hidden = false;
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.RegisterParam(new Param_GenericObject(), "Integrator", "Integrator", "The stepping scheme. Feed it to the Integrator input of Analysis Settings.");
        }

        protected override void RegisterEvaluationUnits(EvaluationUnitManager mngr)
        {
            _subcomponents.Add(new IntegratorLoadControl());
            _subcomponents.Add(new IntegratorNewmark());
            _subcomponents.Add(new IntegratorCentralDifference());

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
