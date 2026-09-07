using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Drawing;

using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Superseded by the <see cref="BeamBase"/> switcher component, which offers
    /// ForceBeamColumn and WithHinges as evaluation units under a single component.
    /// Kept so that definitions saved before that change still load; opening one and
    /// running Grasshopper's "Upgrade Components" swaps it via
    /// <see cref="ForceBeamColumnUpgrader"/>.
    /// </summary>
    [Obsolete]
    public class ForceBeamColumn_obsolete : GH_Component
    {
        public ForceBeamColumn_obsolete()
          : base("Force Beam Column (Alpaca4d)", "Force Beam Column",
            "Construct a ForceBeamColumn",
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
            pManager.AddCurveParameter("Line", "Line", $"[{Units.Length}]", GH_ParamAccess.item);
            pManager.AddGenericParameter("Section", "Section", "", GH_ParamAccess.item);
            pManager.AddGenericParameter("GeometricTransformation", "GeomTransf", "", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddVectorParameter("ZAxis", "ZAxis", "", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddColourParameter("Colour", "Colour", "", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Element", "Element", "Element");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve line = null;
            DA.GetData(0, ref line);

            IUniaxialSection section = null;
            DA.GetData(1, ref section);

            Vector3d zAxis = Vector3d.Zero;
            if (DA.GetData(3, ref zAxis))
            {
                Plane perpFrame = Alpaca4d.Utils.PerpendicularFrame(line);
                zAxis = Alpaca4d.Utils.AlignPlane(perpFrame, zAxis).XAxis;
            }
            else
            {
                Plane perpFrame = Alpaca4d.Utils.PerpendicularFrame(line);
                zAxis = perpFrame.XAxis;
            }

            Alpaca4d.Element.GeomTransf geomTransf = null;
            if (!DA.GetData(2, ref geomTransf))
            {
                geomTransf = new Element.GeomTransf(Alpaca4d.Element.GeomTransfType.Linear, line, zAxis);
            }

            Color color = System.Drawing.Color.FromArgb(255, 13, 13, 13);
            DA.GetData(4, ref color);

            var element = new Alpaca4d.Element.ForceBeamColumn(line, section, geomTransf);
            element.Color = color;

            DA.SetData(0, element);
        }

        /// <summary>
        /// Hidden so the component cannot be placed from the toolbar, while still
        /// being instantiable when an old definition is read.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Force_Beam_Column__Alpaca4d_;

        /// <summary>
        /// Must keep the GUID the deleted component shipped with, otherwise old
        /// definitions cannot find it.
        /// </summary>
        public override Guid ComponentGuid => new Guid("BA137E72-9B35-44FD-9601-5859C0E97AD4");
    }
}
