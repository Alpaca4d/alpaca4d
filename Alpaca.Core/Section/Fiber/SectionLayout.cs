using System;
using System.Collections.Generic;
using Alpaca4d.Generic;

namespace Alpaca4d.Section
{
    /// <summary>
    /// What an interaction domain needs of a fibre section: the fibres it integrates, and the
    /// points it holds to their materials' strain limits.
    ///
    /// The two are kept apart because they are not the same points. A patch's fibre sits at the
    /// centroid of its mesh face, half a face inside the edge, and first yield checked there came
    /// out about 1% above Wel fy on an HEA 300 - on the unsafe side, and wrong by more the coarser
    /// the mesh. A patch is held to its limits at its mesh vertices instead, which reach the
    /// outline. A point fibre or a layer's fibre is a bar, and a bar is checked where it is.
    ///
    /// Coordinates are the fibres' own: y along world X, z along world Y, as OpenSees reads them.
    /// </summary>
    public sealed class SectionLayout
    {
        public struct Fibre
        {
            public double Y;
            public double Z;
            public double Area;
            public IMaterial Material;
        }

        public struct CheckPoint
        {
            public double Y;
            public double Z;
            public IMaterial Material;
        }

        public List<Fibre> Fibres { get; } = new List<Fibre>();
        public List<CheckPoint> CheckPoints { get; } = new List<CheckPoint>();

        public void AddFibre(double y, double z, double area, IMaterial material)
        {
            if (material == null)
                throw new ArgumentException("A fibre of the section has no material.", nameof(material));
            Fibres.Add(new Fibre { Y = y, Z = z, Area = area, Material = material });
        }

        public void AddCheckPoint(double y, double z, IMaterial material)
        {
            if (material == null)
                throw new ArgumentException("A point of the section has no material.", nameof(material));
            CheckPoints.Add(new CheckPoint { Y = y, Z = z, Material = material });
        }

        /// <summary>A fibre that is a bar as well: integrated, and checked where it is.</summary>
        public void AddBar(double y, double z, double area, IMaterial material)
        {
            AddFibre(y, z, area, material);
            AddCheckPoint(y, z, material);
        }

        /// <summary>
        /// The layout of <paramref name="section"/>, in the order OpenSees holds its fibres. Reading
        /// a layer or a patch needs RhinoCommon's geometry, so this does not run outside Rhino for a
        /// section that has either.
        /// </summary>
        public static SectionLayout Of(FiberSection section)
        {
            if (section == null)
                throw new ArgumentNullException(nameof(section));

            var layout = new SectionLayout();

            foreach (var fibre in section.PointFibers)
                layout.AddBar(fibre.Pos.X, fibre.Pos.Y, fibre.Area, fibre.Material);

            foreach (var layer in section.Layers)
                foreach (var fibre in layer.Fibers)
                    layout.AddBar(fibre.Pos.X, fibre.Pos.Y, fibre.Area, fibre.Material);

            foreach (var patch in section.Patches)
            {
                foreach (var fibre in patch.Fibers)
                    layout.AddFibre(fibre.Pos.X, fibre.Pos.Y, fibre.Area, fibre.Material);

                if (patch.PatchGeometry == null)
                    continue;

                foreach (var vertex in patch.PatchGeometry.Vertices)
                    layout.AddCheckPoint(vertex.X, vertex.Y, patch.Material);
            }

            return layout;
        }
    }
}
