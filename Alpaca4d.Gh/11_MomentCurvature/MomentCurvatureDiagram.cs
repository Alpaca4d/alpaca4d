using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    public class MomentCurvatureDiagram : GH_Component
    {
        private static readonly Color YieldColor = Color.FromArgb(235, 108, 63);

        private readonly DomainPlot _plot = new DomainPlot();

        public MomentCurvatureDiagram()
          : base("Moment Curvature Diagram (Alpaca4d)", "M-κ",
            "Draws moment-curvature curves: κ along the plane's X, M along its Y, scaled to fit Size, with each curve's first yield " +
            "in orange and its ultimate state in red. Wire several Moment Curvature results to compare them - at different axial " +
            "forces, say.",
            "Alpaca4d", "MomentCurvature_βeta")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Results", "Results", "The Result output of one or more Moment Curvature components.", GH_ParamAccess.list);
            // No default: a default plane is a GH_Plane, whose type initializer needs Rhino and
            // stops the plugin loading anywhere else - the docs' component images, for one.
            pManager.AddPlaneParameter("Plane", "Plane", "Where the origin - no curvature, no moment - is drawn, and which way the axes go. Left empty, the world XY plane.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Size", "Size", "How large the diagram is drawn, in model units: the largest curvature and the largest moment both fit in it.", GH_ParamAccess.item, 10.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.Register_CurveParam("Curves", "Curves", "Each result's curve.");
            pManager.Register_LineParam("Axes", "Axes", "The κ and M axes.");
            pManager.Register_DoubleParam("CurvatureScale", "CurvatureScale", "Model units per rad/m.");
            pManager.Register_DoubleParam("MomentScale", "MomentScale", "Model units per kN m.");
        }

        protected override void BeforeSolveInstance()
        {
            _plot.Clear();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var items = new List<object>();
            if (!DA.GetDataList(0, items))
                return;

            var results = items
                .Select(item => item is Grasshopper.Kernel.Types.IGH_Goo goo ? goo.ScriptVariable() : item)
                .OfType<Alpaca4d.Section.MomentCurvatureResult>()
                .Where(r => r.Curvature.Count > 0)
                .ToList();
            if (results.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Results needs the Result output of Moment Curvature.");
                return;
            }

            if (!LicenseGate.Allows(this, "Moment Curvature Diagram"))
                return;

            var plane = Plane.WorldXY;
            DA.GetData(1, ref plane);
            double size = 10.0;
            DA.GetData(2, ref size);
            if (!(size > 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Size has to be positive.");
                return;
            }

            // Zero is always on both axes, so the curves start where the reader expects them to.
            double kMin = Math.Min(0.0, results.Min(r => r.Curvature.Min()));
            double kMax = Math.Max(0.0, results.Max(r => r.Curvature.Max()));
            double mMin = Math.Min(0.0, results.Min(r => r.Moment.Min()));
            double mMax = Math.Max(0.0, results.Max(r => r.Moment.Max()));
            double kSpan = Math.Max(kMax - kMin, 1e-12);
            double mSpan = Math.Max(mMax - mMin, 1e-12);
            double sK = size / kSpan;
            double sM = size / mSpan;
            Point3d At(double k, double m) => plane.PointAt(k * sK, m * sM);

            var curves = new List<Curve>();
            double tick = 0.012 * size;
            foreach (var r in results)
            {
                // From the origin: the curve's first point is the first increment, not the unloaded section.
                var line = new Polyline { At(0.0, 0.0) };
                line.AddRange(r.Curvature.Select((k, i) => At(k, r.Moment[i])));
                _plot.Curves.Add(line);
                curves.Add(line.ToNurbsCurve());

                if (!double.IsNaN(r.YieldCurvature))
                    _plot.Marks.Add((At(r.YieldCurvature, r.YieldMoment), YieldColor));
                if (!double.IsNaN(r.UltimateCurvature))
                    _plot.Marks.Add((At(r.UltimateCurvature, r.UltimateMoment), DomainPlot.CapacityColor));

                if (results.Count > 1)
                    _plot.Labels.Add(($"N = {r.HeldN:0.#}, {r.DirectionName}", line[line.Count - 1] + plane.XAxis * (8.0 * tick), false));
            }

            _plot.Axis(kMin, kMax + 0.08 * kSpan, v => At(v, 0.0), -plane.YAxis, tick, "κ [rad/m]", skipZero: true);
            _plot.Axis(mMin, mMax + 0.08 * mSpan, v => At(0.0, v), -plane.XAxis, tick, "M [kNm]");

            DA.SetDataList(0, curves);
            DA.SetDataList(1, _plot.Axes);
            DA.SetData(2, sK);
            DA.SetData(3, sM);
        }

        public override BoundingBox ClippingBox => _plot.Box;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (Hidden || Locked)
                return;
            _plot.DrawWires(args, Attributes.Selected);
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Moment_Curvature_Diagram__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{B7297C55-C18D-43C1-A082-979958576A8F}");
    }
}
