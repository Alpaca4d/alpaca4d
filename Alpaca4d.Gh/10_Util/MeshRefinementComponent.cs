using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Alpaca4d.Meshing;

namespace Alpaca4d.Gh
{
    public class MeshRefinementComponent : GH_Component
    {
        public MeshRefinementComponent()
          : base("Mesh Refinement (Alpaca4d)", "Mesh Refinement",
            "Where Mesh Solid and Mesh Surface make smaller elements: near a point, a curve or a surface, growing back to the mesh's size away from it.",
            "Alpaca4d", "10_Utility")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Geometry", "Geometry", "Point, curve, surface, Brep or mesh near which the elements are smaller.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Size", "Size", $"Edge length of the elements at the geometry [{Units.Length}].", GH_ParamAccess.item);
            pManager.AddNumberParameter("Reach", "Reach", $"Distance from the geometry at which the elements are back to the mesh's size [{Units.Length}]. Empty: a gradual growth.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Refinement", "Refinement", "Connect to Mesh Solid or Mesh Surface's Refinements.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_Goo goo = null;
            if (!DA.GetData(0, ref goo)) return;
            double size = 0;
            if (!DA.GetData(1, ref size)) return;
            double reach = 0;
            DA.GetData(2, ref reach);

            if (!(size > 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Size must be more than zero.");
                return;
            }
            if (reach < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Reach cannot be less than zero.");
                return;
            }
            if (reach > 0 && reach < size)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Reach is shorter than Size: the elements jump back to the mesh's size within one of them.");

            MeshRefinement refinement;
            switch (GmshSetup.Geometry(goo))
            {
                case Point point:
                    refinement = MeshRefinement.FromPoint(point.Location, size, reach);
                    break;
                case Curve curve:
                    refinement = MeshRefinement.FromCurve(curve, size, reach);
                    break;
                case Brep brep:
                    refinement = MeshRefinement.FromBrep(brep, size, reach);
                    break;
                case Mesh mesh:
                    refinement = MeshRefinement.FromMesh(mesh, size, reach);
                    break;
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Geometry takes a point, curve, surface, Brep or mesh, not a {goo.TypeName}.");
                    return;
            }

            DA.SetData(0, new GH_ObjectWrapper(refinement));
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.Mesh_Refinement__Alpaca4d_;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public override Guid ComponentGuid => new Guid("{3F6B2C1E-8D4A-4E7B-9C2F-5A1D7E8B4C63}");
    }
}
