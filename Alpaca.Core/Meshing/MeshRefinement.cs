using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Alpaca4d.Meshing
{
    /// <summary>
    /// Where a mesh is to be finer: elements of <see cref="Size"/> at a point, a curve or a surface,
    /// growing back to the mesh's own size by <see cref="Reach"/> from it.
    ///
    /// gmsh measures the distance from points only, so the geometry is kept as points on it, half an
    /// element apart: the distance it measures is then out by a quarter of an element at most. The
    /// mesh need not follow the geometry, nor have nodes on it - that is what inclusion points and
    /// curves are for.
    /// </summary>
    public sealed class MeshRefinement
    {
        /// <summary>
        /// How much each ring of elements may grow on the one inside it when <see cref="Reach"/> is
        /// left to the mesher. 1.3 keeps the change gentle enough that no element is much larger
        /// than its neighbour.
        /// </summary>
        public const double Growth = 1.3;

        public IReadOnlyList<Point3d> Samples { get; }

        /// <summary>The elements' size at the geometry.</summary>
        public double Size { get; }

        /// <summary>
        /// How far from the geometry the elements are back to the mesh's size. Zero or less: left to
        /// the mesher, which grows them by <see cref="Growth"/> a ring.
        /// </summary>
        public double Reach { get; }

        /// <summary>"point", "curve", "surface" or "mesh": what it was made from, for messages.</summary>
        public string Kind { get; }

        /// <summary>What it was made from, to draw.</summary>
        public GeometryBase Geometry { get; }

        public MeshRefinement(IEnumerable<Point3d> samples, double size, double reach, string kind, GeometryBase geometry = null)
        {
            if (!(size > 0))
                throw new ArgumentException("A refinement's Size must be more than zero.");
            Samples = samples.ToList();
            if (Samples.Count == 0)
                throw new ArgumentException("A refinement needs geometry to be near.");
            Size = size;
            Reach = reach;
            Kind = kind;
            Geometry = geometry;
        }

        /// <summary>The distance at which the elements are back to <paramref name="meshSize"/>.</summary>
        public double ReachFor(double meshSize)
        {
            if (Reach > 0)
                return Reach;
            // Elements growing by a factor g a ring, from s to S, span (S - s) / (g - 1).
            return Math.Max(Size, (meshSize - Size) / (Growth - 1.0));
        }

        public static MeshRefinement FromPoint(Point3d point, double size, double reach) =>
            new MeshRefinement(new[] { point }, size, reach, "point", new Point(point));

        /// <summary>Points along <paramref name="curve"/>, half an element apart, its kinks among them.</summary>
        public static MeshRefinement FromCurve(Curve curve, double size, double reach)
        {
            var samples = new List<Point3d> { curve.PointAtStart, curve.PointAtEnd };
            double spacing = size / 2.0;
            var parameters = curve.DivideByLength(spacing, true);
            if (parameters != null)
                samples.AddRange(parameters.Select(curve.PointAt));
            if (curve.TryGetPolyline(out var polyline))
                samples.AddRange(polyline);
            return new MeshRefinement(samples, size, reach, "curve", curve.DuplicateCurve());
        }

        /// <summary>Points over <paramref name="brep"/>, half an element apart: the vertices of a mesh of it that fine.</summary>
        public static MeshRefinement FromBrep(Brep brep, double size, double reach)
        {
            var parameters = new MeshingParameters
            {
                MaximumEdgeLength = size / 2.0,
                MinimumEdgeLength = 0.0,
                GridAspectRatio = 1.0,
                JaggedSeams = false,
                SimplePlanes = false,
                RefineGrid = true,
            };
            var samples = new List<Point3d>();
            foreach (var mesh in Mesh.CreateFromBrep(brep, parameters) ?? new Mesh[0])
                samples.AddRange(mesh.Vertices.ToPoint3dArray());
            return new MeshRefinement(samples, size, reach, "surface", brep.DuplicateBrep());
        }

        /// <summary>The vertices of <paramref name="mesh"/>, and the middles of its faces and edges.</summary>
        public static MeshRefinement FromMesh(Mesh mesh, double size, double reach)
        {
            var samples = new List<Point3d>(mesh.Vertices.ToPoint3dArray());
            for (int i = 0; i < mesh.Faces.Count; i++)
                samples.Add(mesh.Faces.GetFaceCenter(i));
            for (int i = 0; i < mesh.TopologyEdges.Count; i++)
                samples.Add(mesh.TopologyEdges.EdgeLine(i).PointAt(0.5));
            return new MeshRefinement(samples, size, reach, "mesh", mesh.DuplicateMesh());
        }

        public override string ToString() =>
            $"Refinement: elements of {Size:0.###} at a {Kind}, " +
            (Reach > 0 ? $"back to the mesh's size {Reach:0.###} from it" : "growing back to the mesh's size gradually");
    }
}
