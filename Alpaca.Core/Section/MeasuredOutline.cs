using System;
using System.Linq;
using Rhino.Geometry;

namespace Alpaca4d.Section
{
    /// <summary>
    /// The area and centroidal moments of inertia of a section's outline, measured once for each
    /// shape it takes. AreaMassProperties of a planar Brep built from the outline costs about a
    /// millisecond, and Assemble reads the area of every beam several times over - its mass, its
    /// weight - so a frame of I-sections spent most of its time measuring the same outline.
    /// </summary>
    internal sealed class MeasuredOutline
    {
        private double[] _shape;

        public double Area { get; private set; }

        /// <summary>CentroidCoordinatesMomentsOfInertia.X.</summary>
        public double Izz { get; private set; }

        /// <summary>CentroidCoordinatesMomentsOfInertia.Y.</summary>
        public double Iyy { get; private set; }

        /// <summary>
        /// Measured off <paramref name="brep"/> when <paramref name="shape"/>, the dimensions the
        /// outline is drawn from, differs from what was measured last.
        /// </summary>
        public MeasuredOutline Of(double[] shape, Func<Brep> brep)
        {
            if (_shape != null && _shape.SequenceEqual(shape))
                return this;

            var properties = AreaMassProperties.Compute(brep());
            this.Area = properties.Area;
            this.Izz = properties.CentroidCoordinatesMomentsOfInertia.X;
            this.Iyy = properties.CentroidCoordinatesMomentsOfInertia.Y;
            _shape = shape;
            return this;
        }
    }
}
