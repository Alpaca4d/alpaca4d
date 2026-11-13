using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using Alpaca4d;
using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    public class Damping : GH_Component
    {
        public Damping()
          : base("Damping (Alpaca4d)", "Damping",
            "Rayleigh damping for every element and node of the model, as a combination of the mass " +
                "and stiffness matrices:\n" +
                "D = alphaM*M + betaKcurr*Kcurrent + betaKinit*Kinit + betaKcomm*KlastCommit\n" +
                "For a damping ratio at a known circular frequency omega, taken from a Natural " +
                "Vibration Analysis, stiffness-proportional damping is betaKcomm = 2*ratio/omega - so " +
                "5% at omega gives 0.1/omega. Only a transient analysis uses damping; feeds the " +
                "Damping input of Analysis Settings.",
            "Alpaca4d", "07_Analysis")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }


        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("AlphaM", "AlphaM", "factor applied to elements or nodes mass matrix", GH_ParamAccess.item, 0.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("BetaKCurr", "BetaKCurr", "factor applied to elements current stiffness matrix.", GH_ParamAccess.item, 0.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("BetaKInit", "BetaKInit", "factor applied to elements initial stiffness matrix.", GH_ParamAccess.item, 0.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("BetaKComm", "BetaKComm", "factor applied to elements committed stiffness matrix. This is the usual place to put stiffness-proportional damping: 2*ratio/omega.", GH_ParamAccess.item, 0.0);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Damping", "Damping", "The Rayleigh damping. Feed it to the Damping input of Analysis Settings.");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            double alphaM = 0.0;
            DA.GetData(0, ref alphaM);

            double betaKCurr = 0.00;
            DA.GetData(1, ref betaKCurr);

            double betaKInit = 0.00;
            DA.GetData(2, ref betaKInit);

            double betaKComm = 0.00;
            DA.GetData(3, ref betaKComm);


            var damping = new Alpaca4d.Damping(alphaM, betaKCurr, betaKInit, betaKComm);

            DA.SetData(0, damping);
        }



        public override GH_Exposure Exposure => GH_Exposure.quinary;


        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Rayleigh_Damping__Alpaca4d_;


        public override Guid ComponentGuid => new Guid("{EB8C2885-7DEF-4B12-B978-FD81A1A5763E}");
    }
}