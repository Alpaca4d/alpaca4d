using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;


namespace Alpaca4d.Gh
{
    /// <summary>
    /// Superseded by the <see cref="Support"/> switcher, whose Rigid type has the same seven inputs
    /// in the same order. Kept so that definitions saved before that change still load and solve;
    /// "Upgrade Components" swaps it via <see cref="SupportUpgrader"/>.
    /// </summary>
    [Obsolete]
    public class Support_obsolete : GH_Component
    {
        public Support_obsolete()
          : base("Support (Alpaca4d)", "Support",
            "Construct a Support.\n" +
            "Replaced by Support, Rigid, which takes the same inputs. Upgrade Components swaps it.",
            "Alpaca4d", "02_Element")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("Position", "Position",
                $"Point or Plane to restrain [{Units.Length}]. A Point restrains the global axes. " +
                "A Plane restrains its own axes instead, so the support can be skewed - " +
                "an inclined roller, a support on a sloping face.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Tx", "Tx", "Translation along the support plane's X axis", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Ty", "Ty", "Translation along the support plane's Y axis", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Tz", "Tz", "Translation along the support plane's Z axis", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Rx", "Rx", "Rotation about the support plane's X axis", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Ry", "Ry", "Rotation about the support plane's Y axis", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Rz", "Rz", "Rotation about the support plane's Z axis", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Support", "Support", "Support");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // A Point arriving on this input is cast to a world-aligned Plane at that
            // point, which is the support every model had before planes were an option.
            Plane position = Plane.WorldXY;
            bool tx = true;
            bool ty = true;
            bool tz = true;
            bool rx = true;
            bool ry = true;
            bool rz = true;

            if (!DA.GetData(0, ref position)) return;
            if (!DA.GetData(1, ref tx)) return;
            if (!DA.GetData(2, ref ty)) return;
            if (!DA.GetData(3, ref tz)) return;
            if (!DA.GetData(4, ref rx)) return;
            if (!DA.GetData(5, ref ry)) return;
            if (!DA.GetData(6, ref rz)) return;

            var support = new Alpaca4d.Element.Support(position, tx, ty, tz, rx, ry, rz);

            DA.SetData(0, support);
        }

        /// <summary>
        /// Hidden so the component cannot be placed from the toolbar, while still
        /// being instantiable when an old definition is read.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Support__Alpaca4d_;

        /// <summary>
        /// Must keep the GUID the old component shipped with, otherwise old
        /// definitions cannot find it.
        /// </summary>
        public override Guid ComponentGuid => new Guid("08A676AD-7332-4D86-8206-DABD66A17357");
    }
}
