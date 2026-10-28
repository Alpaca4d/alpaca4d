using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Grasshopper.Kernel;


namespace Alpaca4d.Gh
{
    public class MeshBrep : GH_Component
    {
        public MeshBrep()
          : base("Mesh Brep (Alpaca4d)", "Mesh Brep",
            "Quad mesh of surfaces and polysurfaces for shell elements, made by Rhino's QuadRemesh. Faces that meet share the nodes along the edge between them, and the mesh has a node on every inclusion point.",
            "Alpaca4d", "10_Utility")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }


        /// <inheritdoc />
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Breps", "Breps", "Surfaces or polysurfaces to mesh, as one mesh. They are merged where they touch, so a wall standing on a slab shares the nodes along the line where it meets it, and so do the faces of a polysurface along the edges between them.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Edge Length", "EdgeLength", $"Length the quads' sides aim for [{Units.Length}]. The Brep's edges are all kept, so any shorter than this make smaller quads around them.", GH_ParamAccess.item, 0.5);
            pManager.AddPointParameter("Points", "Points", "Points the mesh must have a node at, such as supports, columns and point loads. Each one pulls the nearest node onto itself and the quads around it even out again. A point on an edge pulls a node along that edge, and one further than the document tolerance from the Brep is left out with a warning.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }


        /// <inheritdoc />
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "Mesh", "Quad mesh, joined into one. Feed it to ASD Shell.", GH_ParamAccess.item);
        }


        /// <inheritdoc />
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var breps = new List<Brep>();
            if (!DA.GetDataList(0, breps)) return;

            double edgeLength = 0.5;
            DA.GetData(1, ref edgeLength);

            var points = new List<Point3d>();
            DA.GetDataList(2, points);

            var warnings = new List<string>();
            var remarks = new List<string>();
            var mesh = Alpaca4d.BrepMesher.Create(breps, edgeLength, points, DocumentTolerance(), warnings, remarks);
            foreach (var warning in warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            foreach (var remark in remarks)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, remark);

            DA.SetData(0, mesh);
        }


        protected override System.Drawing.Bitmap Icon => Properties.Resources.Mesh_Brep__Alpaca4d_;

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        public override Guid ComponentGuid => new Guid("{E0121E43-9C8D-4FC5-A273-50FE088F5E43}");
    }
}
