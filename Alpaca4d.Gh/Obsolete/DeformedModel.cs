using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using Alpaca4d.TimeSeries;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Retired. The deformed shape is a toggle inside View Results rather than a component of
    /// its own, so a result can be read on the shape it belongs to instead of being drawn twice.
    /// Kept hidden so definitions that already place it still open.
    /// </summary>
    [Obsolete]
    public class DeformedModel : GH_Component
    {
        public DeformedModel()
          : base("Deformed Model View (Alpaca4d)", "Deformed Model View",
            "Deformed Model View",
            "Alpaca4d", "09_Visualisation")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to draw, or which mode after a natural vibration analysis.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Scale", "Scale", "How far to exaggerate the displacements. 1 draws them at true size, which on a stiff structure is usually too small to see.", GH_ParamAccess.item, 1.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colors", "Colors", "Gradient to colour the displacement with, from the low end to the high. Connect the Colors component for a ready-made one.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntervalParameter("Range", "Range", "Displacement range the gradient is stretched over. Left empty it fits the model, which is what makes two models with different ranges impossible to compare - set it to compare them.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Beam", "Beam", "The deformed beam elements, as coloured lines.");
            pManager.Register_GenericParam("Shell", "Shell", "The deformed shell elements, as coloured meshes.");
            pManager.Register_GenericParam("Brick", "Brick", "The deformed brick and tetrahedron elements, as coloured meshes.");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Model model = new Model();
            DA.GetData(0, ref model);
            int step = 0;
            DA.GetData(1, ref step);

            double scale = 1.0;
            DA.GetData(2, ref scale);

            // Compute displacements once and reuse for performance
            var dispDictionary = model.NodalDisplacements(step);

            double min;
            double max;
            Rhino.Geometry.Interval domain = new Rhino.Geometry.Interval();
            if(!DA.GetData(4, ref domain))
            {
                var value = dispDictionary.Values;
                min = value.Select(x => x.Length).Min();
                max = value.Select(x => x.Length).Max();
            }
            else
            {
                min = domain.Min;
                max = domain.Max;
            }



            //value.
            //min = dispDictionary.Values.Min().Length;


            List<System.Drawing.Color> colors = new List<System.Drawing.Color>();
            List<Mesh> mesh = null;
            List<Mesh> bricks = null;
            List<Mesh> lines = null;

            if (DA.GetDataList(3, colors))
			{
                mesh = model.DeformedShell(dispDictionary, scale, colors, min, max);
                lines = model.DeformedBeam(dispDictionary, scale, colors, min, max);
                bricks = model.DeformedBrick(dispDictionary, scale, colors, min, max);
            }
			else
			{
                colors = Alpaca4d.Colors.Gradient(0);
                mesh = model.DeformedShell(dispDictionary, scale, colors, min, max);
                lines = model.DeformedBeam(dispDictionary, scale, colors, min, max);
                bricks = model.DeformedBrick(dispDictionary, scale, colors, min, max);
            }

            // Finally assign the spiral to the output parameter.
            DA.SetDataList(0, lines);
            DA.SetDataList(1, mesh);
            DA.SetDataList(2, bricks);
        }


        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Deformed_Model__Alpaca4d_;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("{C03F4DEC-0B0E-4403-A031-EA1A51923252}");
    }
}