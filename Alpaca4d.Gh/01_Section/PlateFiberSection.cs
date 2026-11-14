using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using Alpaca4d.Generic;
using Alpaca4d;

namespace Alpaca4d.Gh
{
    public class PlateFiberSection : GH_Component
    {
        public PlateFiberSection()
          : base("Plate Fiber Section (Alpaca4d)", "PFS",
            "Construct an Plate Fiber Section",
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
            pManager.AddNumberParameter("Thickness", "Thickness", $"[{Units.Length}]", GH_ParamAccess.item, 0.15);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Material", "Material", "Material the plate is made of. Connect an nD material.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddParameter(ColourInput.Make(ColourInput.SectionDescription));
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Section", "Section", "The shell section. Feed it to the Section input of an ASD Shell.");

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
            double thickness = 0.15;

            IMultiDimensionMaterial material = new Alpaca4d.Material.ElasticIsotropicMaterial(null, 210000000, 90760000, 0.3, 7850);


            DA.GetData(0, ref secName);
            DA.GetData(1, ref thickness);
            DA.GetData(2, ref material);


            var section = new Alpaca4d.Section.PlateFiberSection(secName, thickness, material);

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

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Fiber_section__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{d54fadef-3e36-492c-9241-4e9b8f48ee26}");
    }
}