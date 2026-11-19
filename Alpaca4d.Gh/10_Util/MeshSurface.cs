using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Alpaca4d.Meshing;

namespace Alpaca4d.Gh
{
    public class MeshSurface : GH_Component
    {
        public MeshSurface()
          : base("Mesh Surface (Alpaca4d)", "Mesh Surface",
            "Mesh of surfaces and polysurfaces for shell elements, made by gmsh: quads or triangles, smaller where refinements ask, following inclusion points and curves.",
            "Alpaca4d", "10_Utility")
        {
            UpdateMessage();
        }

        private SurfaceElements _elements = SurfaceElements.Quads;
        private const string ElementsKey = "MeshSurfaceElements";

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Breps", "Breps", "Surfaces or polysurfaces to mesh, as one mesh, sharing nodes where they touch.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Size", "Size", $"Edge length the elements aim for [{Units.Length}], one for each Brep; the last one for the rest.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Refinements", "Refinements", "Where the elements are to be smaller. Connect Mesh Refinement.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddPointParameter("Points", "Points", "Points the mesh must have a node at, such as point loads and columns.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddCurveParameter("Curves", "Curves", "Curves on the surfaces the mesh must have a line of nodes along, such as walls and beams.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "Mesh", "The mesh, joined into one. Feed it to ASD Shell.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Quality", "Quality", "Each element's shape, in the mesh's face order: 1 for a square or equilateral one, 0 for a flat one.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var breps = new List<Brep>();
            if (!DA.GetDataList(0, breps)) return;
            var sizes = new List<double>();
            if (!DA.GetDataList(1, sizes)) return;
            var refinements = GmshSetup.Refinements(DA, 2, this);
            var points = new List<Point3d>();
            DA.GetDataList(3, points);
            var curves = new List<Curve>();
            DA.GetDataList(4, curves);

            if (sizes.Any(s => !(s > 0)))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Every Size must be more than zero.");
                return;
            }
            breps.RemoveAll(b => b == null);
            if (breps.Count == 0) return;

            string gmsh = GmshSetup.Executable(this);
            if (gmsh == null) return;

            var warnings = new List<string>();
            string folder = RhinoGmsh.NewWorkFolder();
            SurfaceMesh result;
            try
            {
                var job = RhinoGmsh.SurfaceJob(breps, sizes, points, curves, refinements, _elements, DocumentTolerance(), folder, warnings);
                result = SurfaceMesher.Create(job, gmsh, folder, GmshSetup.EscapePressed);
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                return;
            }

            foreach (var warning in warnings.Concat(result.Warnings).Distinct().Take(8))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            if (result.Failure != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, result.Failure);
                return;
            }
            RhinoGmsh.Remove(folder);

            double worst = result.Quality.Count > 0 ? result.Quality.Min() : 1.0;
            if (worst < MeshQuality.Poor)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The worst element is shaped {worst:0.###} (1 is a square): stresses next to it are unreliable. A smaller Size or a refinement there helps.");
            int triangles = result.Faces.Count(f => f.Length == 3);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, _elements == SurfaceElements.Quads && triangles > 0
                ? $"{result.Faces.Count} elements, {triangles} of them triangles where the quads could not all pair up; ASD Shell takes both."
                : $"{result.Faces.Count} elements, {result.Nodes.Count} nodes.");

            if (result.Faces.Count > 0)
                GmshSetup.ToleranceRemark(this, result.Faces.Min(f =>
                    Enumerable.Range(0, f.Length).Min(k => result.Nodes[f[k]].DistanceTo(result.Nodes[f[(k + 1) % f.Length]]))));

            DA.SetData(0, RhinoGmsh.ToRhino(result));
            DA.SetDataList(1, result.Quality);
        }

        public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            Menu_AppendSeparator(menu);
            var quads = Menu_AppendItem(menu, "Quads", (s, e) => SetElements(SurfaceElements.Quads), true, _elements == SurfaceElements.Quads);
            quads.ToolTipText = "Quadrangles, paired from gmsh's triangles; a few triangles stay where the mesh grades.";
            var triangles = Menu_AppendItem(menu, "Triangles", (s, e) => SetElements(SurfaceElements.Triangles), true, _elements == SurfaceElements.Triangles);
            triangles.ToolTipText = "Triangles only: they grade evenly around refinements and tight curves.";
        }

        private void SetElements(SurfaceElements elements)
        {
            if (elements == _elements)
                return;
            RecordUndoEvent("Mesh Surface elements");
            _elements = elements;
            UpdateMessage();
            ExpireSolution(true);
        }

        private void UpdateMessage() => this.Message = $"{_elements}\n{this.Category}";

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetString(ElementsKey, _elements.ToString());
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            string stored = null;
            _elements = reader.TryGetString(ElementsKey, ref stored) && Enum.TryParse(stored, out SurfaceElements parsed)
                ? parsed
                : SurfaceElements.Quads;
            UpdateMessage();
            return base.Read(reader);
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.Mesh_Surface__Alpaca4d_;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public override Guid ComponentGuid => new Guid("{D4A97F12-3E6C-4B85-8F21-9B0E5C7A2D48}");
    }
}
