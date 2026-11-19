using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    /// <summary>What Mesh Solid, Mesh Surface and Mesh Refinement share.</summary>
    internal static class GmshSetup
    {
        /// <summary>
        /// The gmsh to run: the one set in Alpaca4d ▸ Settings, or else one found where its
        /// installers put it. Null, with an error on <paramref name="component"/>, when there is none.
        /// </summary>
        public static string Executable(GH_Component component)
        {
            string gmsh = Alpaca4d.Meshing.Gmsh.Find(AlpacaSettings.GmshPath, out string problem);
            if (gmsh == null)
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return gmsh;
        }

        /// <summary>Stops gmsh when Escape is pressed, as Grasshopper stops a long solution.</summary>
        public static bool EscapePressed() => GH_Document.IsEscapeKeyDown();

        /// <summary>
        /// The path of an output branch: <paramref name="tail"/> alone when the input is one list, as
        /// it mostly is; after the branch's own path when the component runs once per branch.
        /// </summary>
        public static GH_Path Path(GH_Component component, IGH_DataAccess access, params int[] tail)
        {
            if (component.Params.Input[0].VolatileData.PathCount <= 1)
                return new GH_Path(tail);
            var path = access.ParameterTargetPath(0);
            foreach (int index in tail)
                path = path.AppendElement(index);
            return path;
        }

        /// <summary>
        /// Assemble Model welds nodes nearer than its Tolerance, 1 cm unless set: a mesh with elements that
        /// small would come apart in the model, or fold. Said as a remark, from twice that.
        /// </summary>
        public static void ToleranceRemark(GH_Component component, double shortest)
        {
            const double assembleDefault = 0.01;
            if (shortest > 0 && shortest < 2 * assembleDefault)
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"The shortest element edge is {shortest:0.####} {Units.Length}: set Assemble Model's Tolerance below it. " +
                    $"Its default, {assembleDefault} {Units.Length}, would weld these nodes together.");
        }

        /// <summary>The refinements wired in, whatever wrapper Grasshopper put them in.</summary>
        public static List<Alpaca4d.Meshing.MeshRefinement> Refinements(IGH_DataAccess access, int input, GH_Component component)
        {
            var goos = new List<IGH_Goo>();
            var refinements = new List<Alpaca4d.Meshing.MeshRefinement>();
            access.GetDataList(input, goos);
            foreach (var goo in goos)
            {
                if (goo == null)
                    continue;
                if (goo.ScriptVariable() is Alpaca4d.Meshing.MeshRefinement refinement)
                    refinements.Add(refinement);
                else
                    component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Refinements takes what Mesh Refinement gives, not a {goo.TypeName}: it is left out.");
            }
            return refinements;
        }

        /// <summary>A point, curve, Brep or mesh out of whatever Grasshopper holds it in.</summary>
        public static GeometryBase Geometry(IGH_Goo goo)
        {
            if (goo == null)
                return null;
            switch (goo)
            {
                case GH_Point point:
                    return new Point(point.Value);
                case GH_Mesh mesh:
                    return mesh.Value;
                case GH_Brep brep:
                    return brep.Value;
            }

            Curve curve = null;
            if (GH_Convert.ToCurve(goo, ref curve, GH_Conversion.Both) && curve != null)
                return curve;
            Brep b = null;
            if (GH_Convert.ToBrep(goo, ref b, GH_Conversion.Both) && b != null)
                return b;
            Point3d p = Point3d.Unset;
            if (GH_Convert.ToPoint3d(goo, ref p, GH_Conversion.Both) && p.IsValid)
                return new Point(p);
            Mesh m = null;
            if (GH_Convert.ToMesh(goo, ref m, GH_Conversion.Both) && m != null)
                return m;
            return null;
        }
    }
}
