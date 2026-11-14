using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Alpaca4d.Section;

namespace Alpaca4d.Gh
{
    public class MyMzDiagram : GH_Component
    {
        private readonly DomainPlot _plot = new DomainPlot();

        public MyMzDiagram()
          : base("My-Mz Diagram (Alpaca4d)", "My-Mz",
            "Draws the My-Mz interaction diagram of a domain at each axial force N: My along the plane's X, Mz along its Y, " +
            "to one scale for both.\nThe Checks of a Domain Check are drawn by their moments, whatever their N.",
            "Alpaca4d", "MomentCurvature_βeta")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Domain", "Domain", "The domain, from Interaction Domain.", GH_ParamAccess.item);
            pManager.AddNumberParameter("N", "N", "The axial forces to draw the diagram at, in kN: tension positive, compression negative.", GH_ParamAccess.list, 0.0);
            pManager.AddPlaneParameter("Plane", "Plane", "Where the origin - no moment - is drawn, and which way the axes go Left empty, the world XY plane.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Size", "Size", "How large the diagram is drawn, in model units: the domain's widest moment either way fits in it.", GH_ParamAccess.item, 10.0);
            pManager.AddGenericParameter("Checks", "Checks", "Optional: the Checks of a Domain Check, drawn as points.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.Register_CurveParam("Curves", "Curves", "The diagram at each N, closed.");
            pManager.Register_LineParam("Axes", "Axes", "The My and Mz axes.");
            pManager.Register_DoubleParam("My", "My", "Moment about y round each diagram, in kN m: one branch for each N.");
            pManager.Register_DoubleParam("Mz", "Mz", "Moment about z round each diagram, in kN m: one branch for each N.");
            pManager.Register_DoubleParam("Scale", "Scale", "Model units per kN m.");
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

            if (!LicenseGate.Allows(this, "My-Mz Diagram"))
                return;

            var forces = new List<double>();
            DA.GetDataList(1, forces);
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

            double extent = Domain3D.MomentExtent(domain);
            double s = size / (2.0 * extent);
            Point3d At(double my, double mz) => plane.PointAt(my * s, mz * s);

            var curves = new List<Curve>();
            var myTree = new DataTree<double>();
            var mzTree = new DataTree<double>();
            var beyond = new List<double>();
            double tick = 0.012 * size;

            for (int i = 0; i < forces.Count; i++)
            {
                var path = new GH_Path(i);
                double n = forces[i];
                var level = domain.Contour(n);
                if (level.Count < 3)
                {
                    beyond.Add(n);
                    myTree.EnsurePath(path);
                    mzTree.EnsurePath(path);
                    continue;
                }

                var line = new Polyline(level.Select(p => At(p.My, p.Mz)));
                line.Add(line[0]);
                _plot.Curves.Add(line);
                curves.Add(line.ToNurbsCurve());
                myTree.AddRange(level.Select(p => p.My), path);
                mzTree.AddRange(level.Select(p => p.Mz), path);

                // Each diagram named at its right-most point.
                var right = level.OrderByDescending(p => p.My).First();
                _plot.Labels.Add(($"N = {n:0.#}", At(right.My, right.Mz) + plane.XAxis * (6.0 * tick), false));
            }

            double reach = 1.15 * extent;
            _plot.Axis(-reach, reach, v => At(v, 0), -plane.YAxis, tick, "My [kNm]", skipZero: true);
            _plot.Axis(-reach, reach, v => At(0, v), -plane.XAxis, tick, "Mz [kNm]", skipZero: true);

            foreach (var check in DomainPlot.ChecksFrom(items))
            {
                bool has = !double.IsNaN(check.CapacityN);
                _plot.Check(At(check.My, check.Mz), has ? At(check.CapacityMy, check.CapacityMz) : Point3d.Unset, has);
            }

            if (beyond.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"N = {string.Join(", ", beyond.Select(x => x.ToString("0.#")))} kN is beyond the axial resistance, " +
                    $"{domain.Nc:0.#} to {domain.Nt:0.#} kN, so there is no diagram there.");

            DA.SetDataList(0, curves);
            DA.SetDataList(1, _plot.Axes);
            DA.SetDataTree(2, myTree);
            DA.SetDataTree(3, mzTree);
            DA.SetData(4, s);
        }

        public override BoundingBox ClippingBox => _plot.Box;

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (Hidden || Locked)
                return;
            _plot.DrawWires(args, Attributes.Selected);
        }

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.My_Mz_Diagram__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{41525B7A-8024-44B4-ABFC-4680277DF95B}");
    }
}
