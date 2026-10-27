using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

namespace Alpaca4d.Optimisation
{
    /// <summary>The solids a topology optimisation can work on, by the OpenSees element that solves them.</summary>
    public enum SolidShape
    {
        /// <summary>Eight nodes. An SSP Brick, solved as a stdBrick: see <see cref="TopologyDeck"/>.</summary>
        Hexahedron,

        /// <summary>Four nodes, FourNodeTetrahedron.</summary>
        Tetrahedron,

        /// <summary>Ten nodes, TenNodeTetrahedronStd.</summary>
        QuadraticTetrahedron,
    }

    /// <summary>
    /// One solid element of a topology optimisation: what the optimiser needs to know about it - where
    /// it is and how big, for the filter and the volume - and what the analysis needs, its tag and its
    /// nodes. Its density is the design variable, and its modulus follows from it.
    /// </summary>
    public sealed class SolidCell
    {
        /// <summary>The element's tag in the deck.</summary>
        public int Tag { get; }

        public SolidShape Shape { get; }

        /// <summary>The element's node tags, in its own order: the order its "forces" response lists them in.</summary>
        public IReadOnlyList<int> Nodes { get; }

        /// <summary>The mean of its corners.</summary>
        public Point3d Centroid { get; }

        public double Volume { get; }

        /// <summary>Young's modulus of its material: what the element has where it is solid.</summary>
        public double E { get; }

        /// <summary>Kept solid whatever the optimisation finds: a non-design element.</summary>
        public bool Passive { get; set; }

        public SolidCell(int tag, SolidShape shape, IReadOnlyList<int> nodes, Point3d centroid, double volume, double e)
        {
            if (nodes == null || nodes.Count != NodeCount(shape))
                throw new ArgumentException($"A {shape} has {NodeCount(shape)} nodes, not {nodes?.Count ?? 0}.");
            if (!(volume > 0.0))
                throw new ArgumentException($"Element {tag} has no volume.");
            if (!(e > 0.0))
                throw new ArgumentException($"Element {tag} has no stiffness: its material's E is {e}.");

            Tag = tag;
            Shape = shape;
            Nodes = nodes.ToArray();
            Centroid = centroid;
            Volume = volume;
            E = e;
        }

        public static int NodeCount(SolidShape shape)
        {
            switch (shape)
            {
                case SolidShape.Hexahedron: return 8;
                case SolidShape.Tetrahedron: return 4;
                default: return 10;
            }
        }

        /// <summary>How many corners: the nodes the centroid and the outline are taken from, which come first.</summary>
        public static int CornerCount(SolidShape shape) => shape == SolidShape.Hexahedron ? 8 : 4;
    }
}
