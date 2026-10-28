using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using Alpaca4d.TimeSeries;
using static Alpaca4d.Gh.ComponentMessage;

namespace Alpaca4d.Gh
{
    public class LinearLoad : GH_Component
    {
        public LinearLoad()
          : base("Linear Load (Alpaca4d)", "LinearLoad",
            "Construct a LinearLoad",
            "Alpaca4d", "05_Load")
        {
            // Draw a Description Underneath the component
            this.Message = MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(Target, Target, LoadTarget.Description("beams"), GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddVectorParameter("Force", "Force", $"[{Units.Force}/{Units.Length}]", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Load", "Load", "The load. Collect it into a Load Pattern, then feed that to Assemble Model.");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!LoadTarget.Read(this, DA, 0, "beams", out Generic.IBeam element, out string filter))
                return;

            var force = Vector3d.Zero;
            DA.GetData(1, ref force);

            bool local = false;
            var linearLoad = new Alpaca4d.Loads.LineLoad(element, force, null, local) { Filter = filter };

            // Finally assign the spiral to the output parameter.
            DA.SetData(0, linearLoad);
        }

        private const string Target = "Beam|Id";

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (!base.Read(reader))
                return false;

            LoadTarget.Rename(Params.Input[0], "BeamElement", Target, LoadTarget.Description("beams"));
            return true;
        }


        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Line_load;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("{8593AC69-D975-4566-97FC-27D7D4A4D415}");
    }
}