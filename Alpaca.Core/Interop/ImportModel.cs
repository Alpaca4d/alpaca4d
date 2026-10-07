using System.Collections.Generic;
using Rhino.Geometry;

namespace Alpaca4d.Interop
{
    /// <summary>
    /// A structural model read from another program, before it becomes Alpaca4d objects.
    ///
    /// A reader - the Karamba3D one today - fills this in and stops there, and
    /// <see cref="ImportModelBuilder"/> turns it into Alpaca models. Keeping the two apart keeps every
    /// Alpaca decision out of the reader, so another source can reuse the builder, and the builder can
    /// be tested without the program the model came from.
    ///
    /// Everything is held in Alpaca's units - m, kN, kg - but in the source's own local axes: the
    /// builder, not the reader, maps a section onto Alpaca's axes.
    /// </summary>
    public class ImportModel
    {
        /// <summary>The program the model came from, for messages - "Karamba3D 3.1.60921", say.</summary>
        public string Source { get; set; } = "the imported model";

        public List<Point3d> Nodes { get; } = new List<Point3d>();
        public List<ImportBeam> Beams { get; } = new List<ImportBeam>();
        public List<ImportShell> Shells { get; } = new List<ImportShell>();
        public List<ImportSupport> Supports { get; } = new List<ImportSupport>();
        public List<ImportPointMass> PointMasses { get; } = new List<ImportPointMass>();

        public List<ImportPointLoad> PointLoads { get; } = new List<ImportPointLoad>();
        public List<ImportLineLoad> LineLoads { get; } = new List<ImportLineLoad>();
        public List<ImportGravity> Gravities { get; } = new List<ImportGravity>();

        /// <summary>The load cases, in the order the source lists them.</summary>
        public List<string> LoadCases { get; } = new List<string>();

        /// <summary>What the reader already found it could not carry over. The builder adds to it.</summary>
        public ConversionReport Report { get; } = new ConversionReport();
    }

    public class ImportMaterial
    {
        public string Name { get; set; }

        /// <summary>Young's modulus [kN/m2].</summary>
        public double E { get; set; }

        /// <summary>Shear modulus [kN/m2].</summary>
        public double G { get; set; }

        public double Nu { get; set; }

        /// <summary>Weight per volume [kN/m3]: what self-weight is made of.</summary>
        public double SpecificWeight { get; set; }

        /// <summary>
        /// Mass per volume [kg/m3]: what the mass matrix is made of. Held apart from the specific
        /// weight because the source's g need not be Alpaca's - Karamba3D uses 10 m/s2 by default -
        /// and each should match the source's own figure.
        /// </summary>
        public double Density { get; set; }
    }

    public enum ImportSectionShape
    {
        /// <summary>Only the section properties are known, or the shape has no Alpaca counterpart.</summary>
        General,
        Rectangle,
        RectangularHollow,
        Circle,
        I,
    }

    /// <summary>
    /// A beam cross-section in the source's local axes: z along the depth of the section and y across
    /// it, which is how Karamba3D and most programs other than OpenSees orient one. Dimensions are
    /// only meaningful for the shape named in <see cref="Shape"/>; the properties are always set.
    /// </summary>
    public class ImportBeamSection
    {
        public string Name { get; set; }
        public ImportMaterial Material { get; set; }
        public ImportSectionShape Shape { get; set; }

        /// <summary>The source's own name for the kind of section ("T-section", "Polygon"), for messages.</summary>
        public string ShapeName { get; set; }

        // Dimensions [m], for the shape that uses them.
        public double Height { get; set; }
        public double Width { get; set; }
        public double TopWidth { get; set; }
        public double TopFlange { get; set; }
        public double BottomWidth { get; set; }
        public double BottomFlange { get; set; }
        public double Web { get; set; }
        public double Diameter { get; set; }

        /// <summary>Wall thickness of a hollow circle; zero for a solid one.</summary>
        public double WallThickness { get; set; }

        /// <summary>Root or corner radius, which Alpaca's shapes do not have. Only used to explain a mismatch.</summary>
        public double FilletRadius { get; set; }

        // Properties [m2, m4], about the source's local axes through the centroid.
        public double A { get; set; }
        public double Ay { get; set; }
        public double Az { get; set; }
        public double Iyy { get; set; }
        public double Izz { get; set; }
        public double Iyz { get; set; }
        public double J { get; set; }
    }

    public class ImportBeam
    {
        public string Id { get; set; }
        public int NodeI { get; set; }
        public int NodeJ { get; set; }
        public ImportBeamSection Section { get; set; }

        /// <summary>The source's local z axis: the direction of the section's depth.</summary>
        public Vector3d LocalZ { get; set; }
    }

    /// <summary>One triangular shell element.</summary>
    public class ImportShell
    {
        public string Id { get; set; }
        public int NodeA { get; set; }
        public int NodeB { get; set; }
        public int NodeC { get; set; }
        public double Thickness { get; set; }
        public ImportMaterial Material { get; set; }
    }

    public class ImportSupport
    {
        public int Node { get; set; }

        /// <summary>The axes the conditions refer to, or null for the global ones.</summary>
        public Plane? Orientation { get; set; }

        public bool Tx { get; set; }
        public bool Ty { get; set; }
        public bool Tz { get; set; }
        public bool Rx { get; set; }
        public bool Ry { get; set; }
        public bool Rz { get; set; }
    }

    public class ImportPointMass
    {
        public int Node { get; set; }

        /// <summary>Translational mass [kg], the same in every direction.</summary>
        public double Mass { get; set; }
    }

    public abstract class ImportLoad
    {
        /// <summary>The load case it belongs to, or null for a load that acts in every case.</summary>
        public string Case { get; set; }
    }

    public class ImportPointLoad : ImportLoad
    {
        public int Node { get; set; }

        /// <summary>Global force [kN].</summary>
        public Vector3d Force { get; set; }

        /// <summary>Global moment [kNm].</summary>
        public Vector3d Moment { get; set; }
    }

    /// <summary>A load spread evenly over the whole length of one beam.</summary>
    public class ImportLineLoad : ImportLoad
    {
        /// <summary>Index into <see cref="ImportModel.Beams"/>.</summary>
        public int Beam { get; set; }

        /// <summary>Global force per length [kN/m].</summary>
        public Vector3d Force { get; set; }
    }

    /// <summary>Self-weight of every element in one load case.</summary>
    public class ImportGravity : ImportLoad
    {
        /// <summary>Acceleration in units of g: (0, 0, -1) is ordinary self-weight.</summary>
        public Vector3d Factor { get; set; }
    }
}
