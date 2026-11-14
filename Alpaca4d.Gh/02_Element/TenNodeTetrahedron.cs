using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using Alpaca4d.Generic;
using Alpaca4d;

namespace Alpaca4d.Gh
{
    public class TenNodeTetrahedron : GH_Component
    {
        public TenNodeTetrahedron()
          : base("TenNodeTetrahedron (Alpaca4d)", "Ten Node Tetrahedron",
            "Construct a quadratic tetrahedron from a four vertex mesh: the four corners and a node at " +
            "the middle of each edge. Its strain varies linearly through it, so it bends where a Four " +
            "Node Tetrahedron, whose strain is constant, locks up - a cantilever of 60 of them is within " +
            "0.2% of beam theory, where 60 four node ones reach a quarter of the deflection.\n" +
            "The corners are reordered into the node order OpenSees wants and checked for a positive " +
            "Jacobian. It replaces Four Node Tetrahedron.\n" +
            "\n" +
            "A support holds only the node at its point. A face supported at its mesh vertices is held at " +
            "its corners and free at the edge nodes between them, so support those too - Points has all " +
            "ten nodes. Assemble Model warns when it finds an edge node free between two supported " +
            "corners.\n" +
            "\n" +
            "Needs an OpenSees newer than 3.8.0: one built from the master branch since 16 October 2026. " +
            "Up to 3.8.0 the element is six times too soft, so Run Analysis stops rather than run it.\n" +
            "\n" +
            "A solid has no local axes. OpenSees gives the element no orientation argument, and an nD " +
            "material has no orientation either, so the material's own directions - Ex, Ey and Ez on an " +
            "Elastic Orthotropic - are the global X, Y and Z, and so are the stresses read back. " +
            "Where a solid meets a beam or a shell at a point, the two are tied in translation only: a " +
            "solid node carries no moment, so the junction is a pin.",
            "Alpaca4d", "02_Element")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "Mesh", $"Tetrahedron with four vertices, its corners. The nodes on its edges are added at their midpoints. [{Units.Length}]", GH_ParamAccess.item);
            pManager.AddGenericParameter("Material", "Material", "Material the solid is made of. Connect an nD material.", GH_ParamAccess.item);
            pManager.AddColourParameter("Colour", "Colour", "Colour to draw the element in. Display only; it changes nothing in the analysis.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.ElementIdInput, GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Element", "Element", "Element");
            pManager.Register_PointParam("Points", "Points",
                "The element's ten nodes in OpenSees order: the four corners, then the middles of edges " +
                "1-2, 2-3, 3-1, 1-4, 2-4 and 3-4. Supports and point loads go on nodes, so take them from " +
                "here to reach the edge nodes.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh mesh = null;
            DA.GetData(0, ref mesh);

            IMultiDimensionMaterial material = null;
            DA.GetData(1, ref material);

            Color color = Alpaca4d.Colors.DefaultBrick;
            DA.GetData(2, ref color);

            var _mesh = Utils.CleanTenNodeTetrahedron(mesh);

            string elementId = null;
            DA.GetData(3, ref elementId);

            var element = new Alpaca4d.Element.TenNodeTetrahedron(_mesh, material);
            element.Color = color;
            element.ElementId = elementId;
            DA.SetData(0, element);
            DA.SetDataList(1, _mesh.Vertices.ToPoint3dArray());
        }


        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Ten_Node_Tetrahedron__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{1f38cdfc-f909-4f3f-969e-9e2c63f9a321}");
    }
}
