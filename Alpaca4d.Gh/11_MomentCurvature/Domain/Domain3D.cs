using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Alpaca4d.Section;

namespace Alpaca4d.Gh
{
    public class Domain3D : GH_Component
    {
        private readonly DomainPlot _plot = new DomainPlot();

        public Domain3D()
          : base("Domain 3D (Alpaca4d)", "Domain 3D",
            "Draws an interaction domain in three dimensions: My along the plane's X, Mz along its Y, and N along its normal " +
            "with compression up, scaled to fit Size.\nWire a Domain Check's Checks to see each combination (black) and where it " +
            "meets the boundary (red).",
            "Alpaca4d", "MomentCurvature_βeta")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Domain", "Domain", "The domain, from Interaction Domain.", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Plane", "Plane", "Where the origin - no force at all - is drawn, and which way the axes go Left empty, the world XY plane.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Size", "Size", "How large the domain is drawn, in model units: its widest moment and its whole range of N both fit in it.", GH_ParamAccess.item, 10.0);
            pManager.AddGenericParameter("Checks", "Checks", "Optional: the Checks of a Domain Check, drawn as points.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.Register_MeshParam("Mesh", "Mesh", "The domain's surface.");
            pManager.Register_CurveParam("Levels", "Levels", "The domain at each level of N.");
            pManager.Register_CurveParam("Meridians", "Meridians", "Lines over the surface from pure tension to pure compression.");
            pManager.Register_LineParam("Axes", "Axes", "The My, Mz and N axes.");
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

            if (!LicenseGate.Allows(this, "Domain 3D"))
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
            var items = new List<object>();
            DA.GetDataList(3, items);

            double extent = MomentExtent(domain);
            double sM = size / (2.0 * extent);
            double sN = size / (domain.Nt - domain.Nc);
            Point3d At(double n, double my, double mz) => plane.PointAt(my * sM, mz * sM, -n * sN);
            Point3d Of(DomainPoint p) => At(p.N, p.My, p.Mz);

            var mesh = Surface(domain, Of);
            _plot.Mesh = mesh;

            var levels = domain.Grid.Select(row => Closed(row.Select(Of))).ToList();
            _plot.Curves.AddRange(levels);

            int columns = domain.Thetas.Length;
            int every = Math.Max(1, columns / 24);
            var meridians = new List<Polyline>();
            for (int j = 0; j < columns; j += every)
            {
                var line = new Polyline { Of(domain.TensionPole) };
                line.AddRange(domain.Grid.Select(row => Of(row[j])));
                line.Add(Of(domain.CompressionPole));
                meridians.Add(line);
            }
            _plot.FaintCurves.AddRange(meridians);

            double tick = 0.012 * size;
            double reach = 1.15 * extent;
            _plot.Axis(-reach, reach, v => At(0, v, 0), -plane.YAxis, tick, "My [kNm]", skipZero: true);
            _plot.Axis(-reach, reach, v => At(0, 0, v), -plane.XAxis, tick, "Mz [kNm]", skipZero: true);
            double pad = 0.08 * (domain.Nt - domain.Nc);
            // Drawn as -N, so that compression is up; labelled in N, tension positive, as the forces are.
            _plot.Axis(-(domain.Nt + pad), -(domain.Nc - pad), v => At(-v, 0, 0), -plane.XAxis, tick, "N [kN], compression up",
                       shown: v => -v);

            foreach (var check in DomainPlot.ChecksFrom(items))
            {
                bool has = !double.IsNaN(check.CapacityN);
                _plot.Check(At(check.N, check.My, check.Mz),
                            has ? At(check.CapacityN, check.CapacityMy, check.CapacityMz) : Point3d.Unset, has);
            }

            DA.SetData(0, mesh);
            DA.SetDataList(1, levels.Select(p => p.ToNurbsCurve()));
            DA.SetDataList(2, meridians.Select(p => p.ToNurbsCurve()));
            DA.SetDataList(3, _plot.Axes);
            DA.SetData(4, sM);
            DA.SetData(5, sN);
        }

        /// <summary>The largest moment either way, for the scale.</summary>
        internal static double MomentExtent(Alpaca4d.Section.InteractionDomain domain)
        {
            double extent = new[] { domain.MaxMy, -domain.MinMy, domain.MaxMz, -domain.MinMz }.Max();
            return extent > 0.0 ? extent : 1.0;
        }

        private static Polyline Closed(IEnumerable<Point3d> points)
        {
            var line = new Polyline(points);
            if (line.Count > 1)
                line.Add(line[0]);
            return line;
        }

        /// <summary>The levels as rows of a grid, closed round, with a fan to each pole.</summary>
        private static Mesh Surface(Alpaca4d.Section.InteractionDomain domain, Func<DomainPoint, Point3d> of)
        {
            var mesh = new Mesh();
            int rows = domain.Grid.Length, columns = domain.Thetas.Length;

            int tension = mesh.Vertices.Add(of(domain.TensionPole));
            for (int l = 0; l < rows; l++)
                for (int j = 0; j < columns; j++)
                    mesh.Vertices.Add(of(domain.Grid[l][j]));
            int compression = mesh.Vertices.Add(of(domain.CompressionPole));

            int V(int l, int j) => 1 + l * columns + (j % columns);

            for (int j = 0; j < columns; j++)
                mesh.Faces.AddFace(tension, V(0, j + 1), V(0, j));
            for (int l = 0; l + 1 < rows; l++)
                for (int j = 0; j < columns; j++)
                    mesh.Faces.AddFace(V(l, j), V(l, j + 1), V(l + 1, j + 1), V(l + 1, j));
            for (int j = 0; j < columns; j++)
                mesh.Faces.AddFace(V(rows - 1, j), V(rows - 1, j + 1), compression);

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        public override BoundingBox ClippingBox => _plot.Box;

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            if (Hidden || Locked)
                return;
            _plot.DrawMeshes(args, Attributes.Selected);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (Hidden || Locked)
                return;
            _plot.DrawWires(args, Attributes.Selected);
        }

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Domain_3D__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{084A5E4D-98ED-4D79-A15C-BEE423F4ACF4}");
    }
}
