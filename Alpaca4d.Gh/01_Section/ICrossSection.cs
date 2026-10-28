using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using Alpaca4d.Generic;
using Alpaca4d;

namespace Alpaca4d.Gh
{
    public class ISection : GH_Component
    {
        public ISection()
          : base("I Section (Alpaca4d)", "I Section",
            "Construct an I Cross Section",
            "Alpaca4d", "01_Section")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("SectionName", "SecName", "A label for the section, carried on the object and readable through Deconstruct. Nothing in the analysis reads it.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Height", "Height", $"[{Units.Length}]", GH_ParamAccess.item, 0.3);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("TopFlangeWidth", "TopFlangeWidth", $"[{Units.Length}]", GH_ParamAccess.item, 0.15);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("TopFlangeThickness", "TopFlangeThickness", $"[{Units.Length}]", GH_ParamAccess.item, 0.02);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("BottomFlangeWidth", "BottomFlangeWidth", $"[{Units.Length}]", GH_ParamAccess.item, 0.20);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("BottomFlangeThickness", "BottomFlangeThickness", $"[{Units.Length}]", GH_ParamAccess.item, 0.02);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Web", "Web", $"[{Units.Length}]", GH_ParamAccess.item, 0.02);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Material", "Material", "Material the section is made of. Connect an elastic uniaxial material.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddParameter(ColourInput.Make(ColourInput.SectionDescription));
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Section", "Section", "Section");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Input
            string secName = "";
            double topWidth = 0.15;
            double bottomWidth = 0.20;
            double height = 0.30;
            double web = 0.02;
            double topFlangeThickness = 0.02;
            double bottomFlangeThickness = 0.02;
            IUniaxialMaterial material = Alpaca4d.Material.UniaxialMaterialElastic.Steel;

            DA.GetData(0, ref secName);
            DA.GetData(1, ref height);
            DA.GetData(2, ref topWidth);
            DA.GetData(3, ref topFlangeThickness);
            DA.GetData(4, ref bottomWidth);
            DA.GetData(5, ref bottomFlangeThickness);
            DA.GetData(6, ref web);
            DA.GetData(7, ref material);

            var section = new Alpaca4d.Section.ISection(secName, height, topWidth, topFlangeThickness, bottomWidth, bottomFlangeThickness, web, material);

            section.Color = ColourInput.Read(this, DA);
            DA.SetData(0, section);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            // A file saved before the Colour input has one input fewer; see StoredInputs.
            return ColourInput.ReadComponent(this, reader, base.Read, ColourInput.SectionDescription);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.I_Section__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{BB9A94AE-BA76-4E04-9F48-72D99E47EB8E}");
    }
}