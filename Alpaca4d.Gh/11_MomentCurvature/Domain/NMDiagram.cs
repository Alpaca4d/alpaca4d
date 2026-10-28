using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Alpaca4d.Section;

namespace Alpaca4d.Gh
{
    public class NMDiagram : GH_Component
    {
        private readonly DomainPlot _plot = new DomainPlot();

        public NMDiagram()
          : base("N-M Diagram (Alpaca4d)", "N-M",
            "Draws the N-M interaction diagram of a domain: its cut by the plane that holds the N axis and the moment direction " +
            "Angle - 0° for N-My, 90° for N-Mz. M along the plane's X, N along its Y with compression up.\n" +
            "The Checks of a Domain Check are drawn projected onto the cut: exactly on it only when their moment points along Angle.",
            "Alpaca4d", "MomentCurvature_βeta")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Domain", "Domain", "The domain, from Interaction Domain.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Angle", "Angle", "The moment direction to cut along, in degrees from the y axis: 0 for My, 90 for Mz.", GH_ParamAccess.item, 0.0);
            pManager.AddPlaneParameter("Plane", "Plane", "Where the origin - no force at all - is drawn, and which way the axes go Left empty, the world XY plane.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Size", "Size", "How large the diagram is drawn, in model units: its widest moment either way and its whole range of N both fit in it.", GH_ParamAccess.item, 10.0);
            pManager.AddGenericParameter("Checks", "Checks", "Optional: the Checks of a Domain Check, drawn as points.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.Register_CurveParam("Curve", "Curve", "The diagram, closed.");
            pManager.Register_LineParam("Axes", "Axes", "The M and N axes.");
            pManager.Register_DoubleParam("M", "M", "Moment along Angle at each point of the diagram, in kN m: My cos Angle + Mz sin Angle.");
            pManager.Register_DoubleParam("N", "N", "Axial force at each point of the diagram, in kN, tension positive.");
            pManager.Register_DoubleParam("MomentScale", "MomentScale", "Model units per kN m.");
            pManager.Register_DoubleParam("AxialScale", "AxialScale", "Model units per kN.");
        }

        protected override void BeforeSolveInstance()
        {
            _plot.Clear();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object input = null;
            if (!DA.GetData(0, ref input))
                return;
            var domain = DomainCheck.Unwrap(input);
            if (domain == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Domain needs the Domain output of Interaction Domain.");
                return;
            }

            if (!LicenseGate.Allows(this, "N-M Diagram"))
                return;

            double degrees = 0.0;
            DA.GetData(1, ref degrees);
            var plane = Plane.WorldXY;
            DA.GetData(2, ref plane);
            double size = 10.0;
            DA.GetData(3, ref size);
            if (!(size > 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Size has to be positive.");
                return;
            }
            var items = new List<object>();
            DA.GetDataList(4, items);

            double alpha = degrees * Math.PI / 180.0;
            double ux = Math.Cos(alpha), uy = Math.Sin(alpha);
            double Along(double my, double mz) => ux * my + uy * mz;

            var cut = domain.Cut(alpha);
            if (cut.Count < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The plane at this angle barely cuts the domain.");
                return;
            }

            var m = cut.Select(p => Along(p.My, p.Mz)).ToList();
            var n = cut.Select(p => p.N).ToList();

            double extent = Math.Max(m.Max(), -m.Min());
            if (!(extent > 0.0))
                extent = Domain3D.MomentExtent(domain);
            double sM = size / (2.0 * extent);
            double sN = size / (domain.Nt - domain.Nc);
            Point3d At(double force, double moment) => plane.PointAt(moment * sM, -force * sN);

            var curve = new Polyline(cut.Select((p, i) => At(n[i], m[i])));
            curve.Add(curve[0]);
            _plot.Curves.Add(curve);

            double tick = 0.012 * size;
            double reach = 1.15 * extent;
            _plot.Axis(-reach, reach, v => At(0, v), -plane.YAxis, tick, Title(degrees), skipZero: true);
            double pad = 0.08 * (domain.Nt - domain.Nc);
            _plot.Axis(-(domain.Nt + pad), -(domain.Nc - pad), v => At(-v, 0), -plane.XAxis, tick, "N [kN], compression up",
                       shown: v => -v);

            foreach (var check in DomainPlot.ChecksFrom(items))
            {
                bool has = !double.IsNaN(check.CapacityN);
                _plot.Check(At(check.N, Along(check.My, check.Mz)),
                            has ? At(check.CapacityN, Along(check.CapacityMy, check.CapacityMz)) : Point3d.Unset, has);
            }

            DA.SetData(0, curve.ToNurbsCurve());
            DA.SetDataList(1, _plot.Axes);
            DA.SetDataList(2, m);
            DA.SetDataList(3, n);
            DA.SetData(4, sM);
            DA.SetData(5, sN);
        }

        private static string Title(double degrees)
        {
            double a = ((degrees % 360.0) + 360.0) % 360.0;
            if (Math.Abs(a) < 1e-9)
                return "My [kNm]";
            if (Math.Abs(a - 90.0) < 1e-9)
                return "Mz [kNm]";
            return $"M at {degrees:0.##}° [kNm]";
        }

        public override BoundingBox ClippingBox => _plot.Box;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (Hidden || Locked)
                return;
            _plot.DrawWires(args, Attributes.Selected);
        }

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.N_M_Diagram__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{F0A7A4EC-435E-493E-89C2-7231CB0C2D28}");
    }
}
