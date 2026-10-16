using System;
using System.Collections.Generic;
using System.Drawing;

using Grasshopper.Kernel;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Which views Explorer Record takes a picture of for each design, and how big.
    ///
    /// Without it Explorer Record takes the active viewport at 800 × 600. The picture is the view
    /// as it is drawn, Grasshopper's preview included, without the grid and the axes: what is being
    /// compared is the design, not the construction plane.
    /// </summary>
    public class ExplorerImage : GH_Component
    {
        public ExplorerImage()
          : base("Explorer Image (Alpaca4d)", "Explorer Image",
            "Which views Explorer Record takes a picture of for each design, and at what size. " +
            "Without it, Explorer Record takes the active viewport at 800 × 600.",
            "Alpaca4d", "12_Optimisation")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Capture_Viewport__Alpaca4d_;
        public override Guid ComponentGuid => new Guid("{CFDFE3C5-7701-4BEC-972E-BF7B604224AF}");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Views", "Views",
                "Viewports (Perspective, Top, ...) or named views to take, one picture each. A named view is shown in the active viewport while it is taken, and the view is put back after.\n" +
                "Left empty, the active viewport.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Width", "Width", "Width of the picture, in pixels.", GH_ParamAccess.item, ExplorerImageSettings.DefaultWidth);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Height", "Height", "Height of the picture, in pixels.", GH_ParamAccess.item, ExplorerImageSettings.DefaultHeight);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Transparent", "Transparent", "True for a transparent background, so the explorer's own shows through.", GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Image", "Image", "The picture settings. Connect to the Image of Explorer Record.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var views = new List<string>();
            int width = ExplorerImageSettings.DefaultWidth;
            int height = ExplorerImageSettings.DefaultHeight;
            bool transparent = false;
            DA.GetDataList(0, views);
            DA.GetData(1, ref width);
            DA.GetData(2, ref height);
            DA.GetData(3, ref transparent);

            if (width < 50 || height < 50 || width > 8000 || height > 8000)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width and Height go from 50 to 8000 pixels.");
                return;
            }

            var settings = new ExplorerImageSettings(views, width, height, transparent);
            var doc = Rhino.RhinoDoc.ActiveDoc;
            if (doc != null)
            {
                foreach (var view in settings.Views)
                    if (ExplorerRecord.FindView(doc, view) == null && doc.NamedViews.FindByName(view) < 0)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"There is no viewport or named view called {view}.");
            }

            DA.SetData(0, settings);
        }
    }
}
