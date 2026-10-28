using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.TimeSeries;

namespace Alpaca4d.Gh
{

    public class FiberSection : GH_Component
    {

        private List<Alpaca4d.Section.PointFiber> _pointFiber = new List<Section.PointFiber>();
        private List<Alpaca4d.Section.Layer> _layer = new List<Section.Layer>();
        private List<Alpaca4d.Section.Patch> _patch = new List<Section.Patch>();

        private Alpaca4d.Section.FiberSection _fiberSection;

        private readonly FiberPreview _preview = new FiberPreview();

        public FiberSection()
          : base("Fiber Section (Alpaca4d)", "FiberSection",
            "Collects fibre points, layers and patches into one section.\nThe green y and blue z arrows are the section's local axes: y along world X, z along world Y, the way OpenSees writes a fibre. " +
            "They are drawn at the area centroid of the fibres, the point moments are taken about. My and Mz - in Moment Curvature, Interaction Domain and its views - " +
            "are moments about y and z by the right-hand rule, the beam's axis x coming out of the drawing (towards you in Top view): positive My stretches the +z side, " +
            "positive Mz the -y side.",
            "Alpaca4d", "MomentCurvature_βeta")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("PointFiber", "PointFiber", "Single fibres, typically bars.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Layer", "Layer", "Rows of fibres.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Patch", "Patch", "Filled areas of fibres.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;

            // Left as it was. Torsion in a fibre section is elastic and uncoupled from
            // the bending response, so this figure does not touch a moment-curvature
            // result - but it is the torsional stiffness of every model that uses the
            // section, and changing it under existing files would.
            pManager.AddNumberParameter("GJ", "GJ",
                "Torsional stiffness, in kN m2. Elastic and uncoupled from the bending response.",
                GH_ParamAccess.item, 1e8);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("FiberSection", "FiberSection", "The fibre section. Feed it to Moment Curvature, or to a beam element that takes a fibre section.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _fiberSection = null;

            DA.GetDataList(0, _pointFiber);
            DA.GetDataList(1, _layer);
            DA.GetDataList(2, _patch);

            if (_pointFiber.Count == 0 && _layer.Count == 0 && _patch.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "At least one fiber should be provided");
                return;
            }

            double GJ = 1000;
            DA.GetData(3, ref GJ);

            _fiberSection = new Alpaca4d.Section.FiberSection(_pointFiber, _layer, _patch, GJ);

            // Finally assign the spiral to the output parameter.
            DA.SetData(0, _fiberSection);
        }


        protected override void BeforeSolveInstance()
        {
            _pointFiber.Clear();
            _layer.Clear();
            _patch.Clear();
            _preview.Clear();
        }

        protected override void AfterSolveInstance()
        {
            // Once per solve, not once per redraw. Layer.Fibers and Patch.Fibers are
            // computed properties - reading a patch's fibres explodes its mesh and takes
            // the centroid of every face - and the preview used to read them on every
            // frame, on top of the child components doing the same.
            _preview.AddPatches(_patch);
            _preview.AddLayers(_layer);
            _preview.AddPointFibers(_pointFiber);
        }

        /// <summary>
        /// Without this the box comes from the output parameter, which holds a section and
        /// not geometry - so it is empty, Zoom Extents ignores the preview, and Rhino is
        /// free to cull the drawing. Inflated to leave room for the local axes.
        /// </summary>
        public override BoundingBox ClippingBox
        {
            get { return _preview.ClippingBoxWithAxes(); }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (this.Hidden || this.Locked || _preview.IsEmpty)
                return;

            _preview.Draw(args);

            // Only once the section itself exists - a solve that returned early for want
            // of fibres leaves nothing to put axes on.
            if (_fiberSection != null)
                _preview.DrawLocalAxes(args);
        }


        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Fiber_Aggregated_Alpaca4d_;
        public override Guid ComponentGuid => new Guid("{18cd5969-e0e4-4acf-9790-0af2c22cfeb9}");
    }
}
