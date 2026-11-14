using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using Alpaca4d.Generic;
using Rhino.Geometry;

namespace Alpaca4d.Element
{
    /// <summary>
    /// The quadratic tetrahedron: four corners and a node on the middle of each edge, so its
    /// displacement is quadratic and its strain linear. A four node tetrahedron has one constant
    /// strain and is far too stiff in bending; this one bends.
    ///
    /// Written as TenNodeTetrahedronStd rather than TenNodeTetrahedron, for two reasons. Std takes
    /// the edge nodes in the standard order, the one <see cref="Utils.TetrahedronEdges"/> lists.
    /// And only an OpenSees that has José Abell's 2025-26 fixes to the element knows it, which
    /// makes the command name a version check. The TenNodeTetrahedron of 3.8.0 and earlier
    /// integrates over a sixth of the volume, so it comes out six times too soft, too light and
    /// too lightly loaded by its own weight, and asking it for its stresses writes past the end of
    /// a buffer. Run on such an OpenSees, the deck stops at this element's line instead of
    /// answering wrongly.
    /// </summary>
    public partial class TenNodeTetrahedron : ISerialize, IBrick, IElement, IStructure
    {
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public Mesh Mesh { get; set; }
        public IMultiDimensionMaterial Material { get; set; }
        public ElementType Type => ElementType.Brick;
        public ElementClass ElementClass => ElementClass.TenNodeTetrahedron;
        public List<int?> IndexNodes { get; set; }
        public double? BodyForce { get; set; }
        public Color Color { get; set; } = Alpaca4d.Colors.DefaultBrick;
        public int Ndf => 3;

        /// <summary>The element command, which an OpenSees without the fixes does not know.</summary>
        public const string Command = "TenNodeTetrahedronStd";

        /// <summary>
        /// Why the run stopped, when what OpenSees printed says it does not know <see cref="Command"/>;
        /// null otherwise. OpenSees only says the element type is unknown, which reads like a typing
        /// mistake rather than what it is: a solver that predates the element.
        /// </summary>
        public static string SolverTooOld(string log)
        {
            if (log == null || !log.Contains($"{Command} not known"))
                return null;

            return "This OpenSees does not know the Ten Node Tetrahedron. The element needs the fixes of OpenSees " +
                   "pull request #1833, merged on 16 October 2026: in OpenSees 3.8.0 and earlier it is six times too " +
                   "soft. Use an OpenSees built from the master branch since then, or a release after 3.8.0.";
        }

        public TenNodeTetrahedron(Mesh mesh, IMultiDimensionMaterial material)
        {
            if (mesh.Vertices.Count != 10)
                throw new Exception("Mesh vertices must be 10!");
            this.Mesh = mesh;
            this.Material = material;
        }

        /// <summary>
        /// The element's own axes: those of the tetrahedron its four corners make, which are its
        /// first four nodes. See Utils.SolidFrame.
        /// </summary>
        public Plane LocalPlane => Alpaca4d.Utils.SolidAxes(this.Mesh.Vertices.ToPoint3dArray());

        public void SetTags()
        {

        }

        /// <summary>
        /// The ten nodes this tetrahedron sits on, in the order the mesh carries its vertices -
        /// corners first, then the edge nodes - which is the order TenNodeTetrahedronStd wants,
        /// because Utils.CleanTenNodeTetrahedron put them in it. An edge node is shared with every
        /// tetrahedron on that edge exactly as a corner is, by standing at the same point.
        /// </summary>
        public void SetTopologyRTree(Alpaca4d.Model model)
        {
            var meshPoints = this.Mesh.Vertices.ToPoint3dArray();

            this.IndexNodes = Alpaca4d.Utils
                .NodeSearch(model.NodeIndexThreeNDF, meshPoints, $"Ten Node Tetrahedron {this.Id}")
                .Select(x => (int?)(x + 1))
                .ToList();
        }

        public string WriteTcl()
        {
            string tcl = $"element {Command} {this.Id} {string.Join(" ", this.IndexNodes)} {this.Material.Id} {this.BodyForce} {this.BodyForce} {this.BodyForce}\n";
            return tcl;
        }

        /// <summary>
        /// How a uniform body force over the element is shared among its nodes: the integral of
        /// each node's shape function over the volume, as a fraction of the whole. A corner takes
        /// -1/20 and an edge node 1/5, so the corners are pushed against the weight and the edge
        /// nodes carry all of it and more. That is the load the element itself forms from a body
        /// force, and the one that leaves its stresses undistorted; a tenth on each node would
        /// load it the way no weight does. The total is the weight, all the same.
        /// </summary>
        public static readonly IReadOnlyList<double> BodyForceShares = new[]
        {
            -1.0 / 20.0, -1.0 / 20.0, -1.0 / 20.0, -1.0 / 20.0,
            1.0 / 5.0, 1.0 / 5.0, 1.0 / 5.0, 1.0 / 5.0, 1.0 / 5.0, 1.0 / 5.0,
        };

        /// <summary>
        /// How many integration points OpenSees gives the element, and so how many blocks of
        /// stresses each of its rows in the recorder file holds.
        /// </summary>
        public const int GaussPoints = 4;

        // The two volume coordinates of TenNodeTetrahedron's four point rule: point k sits at
        // Alpha along corner k and Beta along the other three, so point k is the one nearest to
        // corner k (tet_coords in TenNodeTetrahedron.cpp).
        private static readonly double Alpha = (5.0 + 3.0 * Math.Sqrt(5.0)) / 20.0;
        private static readonly double Beta = (5.0 - Math.Sqrt(5.0)) / 20.0;

        /// <summary>
        /// A quantity known at the four integration points, at the ten nodes instead.
        ///
        /// The strain of this element is linear, and so is the stress of an elastic material, so
        /// the four values are four samples of one linear field, which four samples settle
        /// exactly. Written in volume coordinates, f = sum of c_i L_i with c_i the value at corner
        /// i, the value at point k is Beta S + (Alpha - Beta) c_k, where S is the sum of the c_i,
        /// and the four values sum to S itself because Alpha + 3 Beta = 1. So
        /// c_k = (v_k - Beta S) / (Alpha - Beta), and an edge node is the mean of its two corners.
        ///
        /// For a material that has yielded the field is not linear any more, and this is the
        /// linear field through the four samples - which is what any extrapolation to the nodes
        /// of this element would draw.
        /// </summary>
        public static double[] AtNodes(IList<double> atGaussPoints)
        {
            if (atGaussPoints.Count != GaussPoints)
                throw new ArgumentException($"A ten node tetrahedron has {GaussPoints} integration points, not {atGaussPoints.Count}.");

            double sum = atGaussPoints.Sum();

            var nodes = new double[10];
            for (int k = 0; k < GaussPoints; k++)
                nodes[k] = (atGaussPoints[k] - Beta * sum) / (Alpha - Beta);

            for (int e = 0; e < Utils.TetrahedronEdges.Length; e++)
                nodes[4 + e] = 0.5 * (nodes[Utils.TetrahedronEdges[e][0]] + nodes[Utils.TetrahedronEdges[e][1]]);

            return nodes;
        }
    }
}
