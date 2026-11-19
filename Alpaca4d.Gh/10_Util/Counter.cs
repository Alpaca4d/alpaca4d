using Grasshopper;
using Grasshopper.Kernel;
using System;

namespace Alpaca4d.Gh
{
    public class Counter : GH_Component
    {
        private int _counter = 0;

        public Counter()
          : base("Counter (Alpaca4d)", "Counter",
            "Timer-based counter",
            "Alpaca4d", "10_Utility")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Run", "Run", "Start/continue counting.", GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Reset", "Reset", "Reset the counter to zero.", GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Interval", "Interval", "Update interval in milliseconds.", GH_ParamAccess.item, 1);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_IntegerParam("Counter", "Counter", "Current counter value.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool run = false;
            bool reset = false;
            int intervalMs = 1;

            DA.GetData(0, ref run);
            DA.GetData(1, ref reset);
            DA.GetData(2, ref intervalMs);

            if (intervalMs < 1) intervalMs = 1;

            if (reset)
            {
                _counter = 0;
            }

            if (run && !reset)
            {
                _counter++;

                var doc = OnPingDocument();
                if (doc != null)
                {
                    doc.ScheduleSolution(intervalMs, d => ExpireSolution(false));
                }
            }

            DA.SetData(0, _counter);
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Counter__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{3A6E4C8F-5BC0-4D1B-BD45-5F8E0E2C0F8A}");
    }
}



