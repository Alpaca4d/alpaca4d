using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using Alpaca4d.Generic;
using Alpaca4d;

namespace Alpaca4d.Gh
{
    public class CircleCS : GH_Component
    {
        public CircleCS()
          : base("CircleCS (Alpaca4d)", "CircleCS",
            "Construct an Circle Cross Section",
            "Alpaca4d", "01_Section")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("SectionName", "SecName", "A label for the section, carried on the object and readable through Deconstruct. Nothing in the analysis reads it.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Diameter", "Diameter", $"[{Units.Length}]", GH_ParamAccess.item, 0.15);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Thickness", "Thickness", $"[{Units.Length}]", GH_ParamAccess.item, 0.01);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Material", "Material", "Material the section is made of. Connect an elastic uniaxial material.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddParameter(ColourInput.Make(ColourInput.SectionDescription));
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Section", "Section", "Section");

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Input
            string secName = "";
            double d = 0.15;
            double t = 0.01;
            IUniaxialMaterial material = Alpaca4d.Material.UniaxialMaterialElastic.Steel;


            DA.GetData(0, ref secName);
            DA.GetData(1, ref d);
            DA.GetData(2, ref t);
            DA.GetData(3, ref material);


            var section = new Alpaca4d.Section.CircleCS(secName, d, t, material);

            section.Color = ColourInput.Read(this, DA);
            DA.SetData(0, section);
        }


        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            // A file saved before the Colour input has one input fewer; see StoredInputs.
            return ColourInput.ReadComponent(this, reader, base.Read, ColourInput.SectionDescription);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Circular_cross_section;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("AFCC30B2-7CB9-453F-B5A1-C73554BD3DC7");
    }
}