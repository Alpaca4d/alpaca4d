using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Alpaca4d.Meshing;

namespace Alpaca4d.Gh
{
    public class MeshSolid : GH_Component
    {
        public MeshSolid()
          : base("Mesh Solid (Alpaca4d)", "Mesh Solid",
            "Tetrahedra filling closed Breps and closed meshes, made by gmsh, smaller where refinements ask and with a node at each inclusion point. Feed them to Ten Node Tetrahedron.",
            "Alpaca4d", "10_Utility")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Solids", "Solids", "Closed Breps or closed meshes to fill. Breps that touch are meshed as one and share their nodes.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Size", "Size", $"Edge length the tetrahedra aim for [{Units.Length}], one for each solid; the last one for the rest.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Refinements", "Refinements", "Where the tetrahedra are to be smaller. Connect Mesh Refinement.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddPointParameter("Points", "Points", "Points the mesh must have a node at, such as point loads and supports.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Feature Angle", "FeatureAngle", "Closed meshes only: their edges folding by more than this many degrees stay sharp.", GH_ParamAccess.item, 40.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Tetrahedra", "Tetrahedra", "Four node tetrahedra, a branch for each solid. Feed them to Ten Node Tetrahedron.", GH_ParamAccess.tree);
            pManager.AddPointParameter("Face Nodes", "FaceNodes", "Nodes on each face, a branch {solid;face}, the middles of the tetrahedra's edges included. For supports.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Quality", "Quality", "Each tetrahedron's shape: 1 for a regular one, 0 for a flat one.", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var goos = new List<IGH_Goo>();
            if (!DA.GetDataList(0, goos)) return;
            var sizes = new List<double>();
            if (!DA.GetDataList(1, sizes)) return;
            var refinements = GmshSetup.Refinements(DA, 2, this);
            var points = new List<Point3d>();
            DA.GetDataList(3, points);
            double featureAngle = 40.0;
            DA.GetData(4, ref featureAngle);

            if (sizes.Any(s => !(s > 0)))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Every Size must be more than zero.");
                return;
            }

            var solids = goos.Select(Solid).ToList();
            string gmsh = GmshSetup.Executable(this);
            if (gmsh == null) return;

            var warnings = new List<string>();
            var errors = new Dictionary<int, string>();
            string folder = RhinoGmsh.NewWorkFolder();
            SolidMesh result;
            try
            {
                var job = RhinoGmsh.SolidJob(solids, sizes, points, refinements, DocumentTolerance(), featureAngle, folder, warnings, errors);
                result = SolidMesher.Create(job, gmsh, folder, GmshSetup.EscapePressed);
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                return;
            }

            foreach (var failure in result.Failures)
                errors[failure.Key] = failure.Value;
            // A failed run keeps its files, for the user to open its script in gmsh.
            if (errors.Count == 0)
                RhinoGmsh.Remove(folder);

            foreach (var error in errors.OrderBy(e => e.Key))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, errors.Values.Distinct().Count() == 1 && errors.Count > 1
                    ? $"Solids {string.Join(", ", errors.Keys.OrderBy(k => k))}: {error.Value}"
                    : $"Solid {error.Key}: {error.Value}");
            foreach (var warning in warnings.Concat(result.Warnings).Distinct().Take(8))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            var tetrahedra = new GH_Structure<GH_Mesh>();
            var faceNodes = new GH_Structure<GH_Point>();
            var quality = new GH_Structure<GH_Number>();
            double worst = 1.0;
            for (int solid = 0; solid < solids.Count; solid++)
            {
                var path = GmshSetup.Path(this, DA, solid);
                tetrahedra.EnsurePath(path);
                quality.EnsurePath(path);
                foreach (var tet in result.Tetrahedra[solid])
                {
                    var p = tet.Select(i => result.Nodes[i]).ToArray();
                    tetrahedra.Append(new GH_Mesh(RhinoGmsh.Tetrahedron(p[0], p[1], p[2], p[3])), path);
                }
                foreach (var q in result.Quality[solid])
                {
                    quality.Append(new GH_Number(q), path);
                    worst = Math.Min(worst, q);
                }
                for (int face = 0; face < result.FaceNodes[solid].Count; face++)
                {
                    var facePath = GmshSetup.Path(this, DA, solid, face);
                    faceNodes.EnsurePath(facePath);
                    faceNodes.AppendRange(result.FaceNodes[solid][face].Select(p => new GH_Point(p)), facePath);
                }
            }

            int count = result.Tetrahedra.Sum(t => t.Count);
            if (count > 0 && worst < MeshQuality.Poor)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The worst tetrahedron is shaped {worst:0.###} (1 is a regular one): stresses next to it are unreliable. A smaller Size or a refinement there helps.");
            if (count > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{count} tetrahedra, {result.Nodes.Count} corner nodes.");
                double shortest = result.Tetrahedra.SelectMany(t => t).Min(t =>
                    new[] { (0, 1), (0, 2), (0, 3), (1, 2), (1, 3), (2, 3) }.Min(e => result.Nodes[t[e.Item1]].DistanceTo(result.Nodes[t[e.Item2]])));
                GmshSetup.ToleranceRemark(this, shortest);
            }

            DA.SetDataTree(0, tetrahedra);
            DA.SetDataTree(1, faceNodes);
            DA.SetDataTree(2, quality);
        }

        /// <summary>A closed Brep, a closed mesh, or what Rhino can make a Brep of: a box, an extrusion.</summary>
        private static GeometryBase Solid(IGH_Goo goo)
        {
            switch (goo)
            {
                case null:
                    return null;
                case GH_Mesh mesh:
                    return mesh.Value;
                case GH_Brep brep:
                    return brep.Value;
            }
            Brep b = null;
            if (GH_Convert.ToBrep(goo, ref b, GH_Conversion.Both) && b != null)
                return b;
            Mesh m = null;
            if (GH_Convert.ToMesh(goo, ref m, GH_Conversion.Both) && m != null)
                return m;
            return null;
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.Mesh_Solid__Alpaca4d_;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public override Guid ComponentGuid => new Guid("{8C2E4A71-5B9D-4F36-A1E8-2D7C6B3F9E05}");
    }
}
