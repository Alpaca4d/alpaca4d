using Grasshopper.Kernel;
using System;
using static Alpaca4d.Gh.ComponentMessage;

namespace Alpaca4d.Gh
{
    public class HingeRelease : GH_Component
    {
        public HingeRelease()
          : base("Hinge Release (Alpaca4d)", "Hinge Release",
            "Define a release condition for a beam hinge. Set a DOF to False to release it (low stiffness).",
            "Alpaca4d", "02_Element")
        {
            this.Message = MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Tx", "Tx", "Axial translation along X. False = released.", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Ty", "Ty", "Translation along Y. False = released.", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Tz", "Tz", "Translation along Z. False = released.", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Rx", "Rx", "Torsional rotation about X. False = released.", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("My", "My", "Bending about Y. False = released.", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Mz", "Mz", "Bending about Z. False = released.", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Release", "Release", "Release condition for a beam hinge end.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool tx = true, ty = true, tz = true, rx = true, my = true, mz = true;

            DA.GetData(0, ref tx);
            DA.GetData(1, ref ty);
            DA.GetData(2, ref tz);
            DA.GetData(3, ref rx);
            DA.GetData(4, ref my);
            DA.GetData(5, ref mz);

            var release = new Alpaca4d.Element.Release(tx, ty, tz, rx, my, mz);
            DA.SetData(0, release);
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Hinge_Release__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("3F8A1C2D-5B4E-4F9A-8D7C-1E2F3A4B5C6D");
    }
}
