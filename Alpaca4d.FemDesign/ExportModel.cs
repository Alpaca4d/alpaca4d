using System.Collections.Generic;

using Rhino.Geometry;

using Alpaca4d.Constraints;
using Alpaca4d.Element;
using Alpaca4d.Loads;

namespace Alpaca4d.FemDesign
{
    /// <summary>
    /// An Alpaca model read into plain values, which is what <see cref="StruxmlWriter"/> turns into a
    /// FEM-Design model. It holds no Rhino curve or mesh, only points and vectors, so the writer - where
    /// every mapping decision is made - runs and is tested outside Rhino.
    ///
    /// Everything is in Alpaca's units (m, kN, kg) and in Alpaca's local axes: a beam's local y runs
    /// along the depth of its section. Turning that into FEM-Design's units and axes is the writer's job.
    /// Materials and sections are listed once and referred to by index.
    /// </summary>
    public class ExportModel
    {
        public List<ExportMaterial> Materials { get; } = new List<ExportMaterial>();
        public List<ExportBeamSection> BeamSections { get; } = new List<ExportBeamSection>();
        public List<ExportShellSection> ShellSections { get; } = new List<ExportShellSection>();

        public List<ExportBeam> Beams { get; } = new List<ExportBeam>();
        public List<ExportShell> Shells { get; } = new List<ExportShell>();
        public List<ExportLink> Links { get; } = new List<ExportLink>();

        public List<ExportSupport> Supports { get; } = new List<ExportSupport>();
        public List<RigidLink> RigidLinks { get; } = new List<RigidLink>();
        public List<EqualDOF> EqualDofs { get; } = new List<EqualDOF>();
        public List<RigidDiaphragm> RigidDiaphragms { get; } = new List<RigidDiaphragm>();

        /// <summary>One per load pattern, in the order of the model's patterns.</summary>
        public List<ExportLoadCase> LoadCases { get; } = new List<ExportLoadCase>();
        public List<MassLoad> Masses { get; } = new List<MassLoad>();

        /// <summary>The distance within which Alpaca took two points to be one node [m].</summary>
        public double Tolerance { get; set; } = 0.01;
    }

    public class ExportMaterial
    {
        /// <summary>The material's name, such as "S355" or "C30_37"; null when it has none.</summary>
        public string Name { get; set; }

        /// <summary>The design grade's name, which only steel has; null otherwise.</summary>
        public string Grade { get; set; }

        /// <summary>[kN/m²]</summary>
        public double E { get; set; }
        /// <summary>[kN/m²]</summary>
        public double G { get; set; }
        public double Nu { get; set; }
        /// <summary>[kg/m³]</summary>
        public double Rho { get; set; }

        /// <summary>An orthotropic shell material, whose stiffness is in Ex..NuZx rather than E, G and Nu.</summary>
        public bool IsOrthotropic { get; set; }
        public double Ex { get; set; }
        public double Ey { get; set; }
        public double Ez { get; set; }
        public double Gxy { get; set; }
        public double Gyz { get; set; }
        public double Gzx { get; set; }
        public double NuXy { get; set; }
        public double NuYz { get; set; }
        public double NuZx { get; set; }
    }

    public enum ExportSectionShape
    {
        Rectangle,
        Circle,
        RectangleHollow,
        I,
        DoubleAngle,
        /// <summary>Only A, I and J are known, not a shape.</summary>
        Generic,
    }

    /// <summary>
    /// A beam section. The dimensions that matter depend on <see cref="Shape"/>, as in Alpaca's section
    /// classes; widths run along local z and heights along local y.
    /// </summary>
    public class ExportBeamSection
    {
        public string Name { get; set; }
        public ExportSectionShape Shape { get; set; }
        public int Material { get; set; }

        // [m]
        public double Height { get; set; }
        public double Width { get; set; }
        public double Diameter { get; set; }
        /// <summary>Wall of a circle, 0 for a solid one; legs of a double angle.</summary>
        public double Thickness { get; set; }
        /// <summary>Web of an I, side walls of a hollow rectangle.</summary>
        public double Web { get; set; }
        /// <summary>Top flange of an I, top wall of a hollow rectangle - a thickness.</summary>
        public double TopFlange { get; set; }
        /// <summary>Bottom flange of an I, bottom wall of a hollow rectangle - a thickness.</summary>
        public double BottomFlange { get; set; }
        public double TopWidth { get; set; }
        public double BottomWidth { get; set; }
        /// <summary>Between the two angles of a double angle.</summary>
        public double Gap { get; set; }

        /// <summary>[m²]</summary>
        public double Area { get; set; }
        /// <summary>Strong axis, about local z [m⁴].</summary>
        public double Izz { get; set; }
        /// <summary>Weak axis, about local y [m⁴].</summary>
        public double Iyy { get; set; }
        /// <summary>[m⁴]</summary>
        public double J { get; set; }
    }

    public class ExportShellSection
    {
        public string Name { get; set; }
        /// <summary>[m]</summary>
        public double Thickness { get; set; }
        public int Material { get; set; }
        /// <summary>The number of layers of a layered section, 1 for any other.</summary>
        public int Layers { get; set; } = 1;
    }

    public class ExportBeam
    {
        public int Id { get; set; }
        /// <summary>The user's label for the element, which need not be unique; may be null.</summary>
        public string Label { get; set; }
        public Point3d Start { get; set; }
        public Point3d End { get; set; }
        /// <summary>Unit local z, square to the beam.</summary>
        public Vector3d LocalZ { get; set; }
        public int Section { get; set; }
        /// <summary>The ends' releases, true where held; null for a beam rigid at both ends.</summary>
        public Release ReleaseI { get; set; }
        public Release ReleaseJ { get; set; }
        /// <summary>False when the beam's curve is not a straight line; it is exported as its chord.</summary>
        public bool IsStraight { get; set; } = true;
        /// <summary>True for an Alpaca truss: pinned at both ends, so it goes out as a FEM-Design truss.</summary>
        public bool IsTruss { get; set; }
    }

    public class ExportShell
    {
        public int Id { get; set; }
        public string Label { get; set; }
        /// <summary>Three or four corners, in the mesh face's order.</summary>
        public Point3d[] Vertices { get; set; }
        public int Section { get; set; }
        /// <summary>The local x asked for, or <see cref="Vector3d.Zero"/> for none.</summary>
        public Vector3d LocalX { get; set; }
    }

    /// <summary>A support: which directions it holds, in its own axes.</summary>
    public class ExportSupport
    {
        public Point3d Position { get; set; }
        /// <summary>Unit axes the restraints are read in; global X and Y unless the support was given a plane.</summary>
        public Vector3d XAxis { get; set; } = Vector3d.XAxis;
        public Vector3d YAxis { get; set; } = Vector3d.YAxis;
        public bool Tx { get; set; }
        public bool Ty { get; set; }
        public bool Tz { get; set; }
        public bool Rx { get; set; }
        public bool Ry { get; set; }
        public bool Rz { get; set; }
        /// <summary>Where it is and what it holds, for a message.</summary>
        public string Description { get; set; }
    }

    /// <summary>A spring between two points, in its own axes.</summary>
    public class ExportLink
    {
        public int Id { get; set; }
        public Point3d Start { get; set; }
        public Point3d End { get; set; }
        public Vector3d LocalX { get; set; }
        public Vector3d LocalY { get; set; }
        /// <summary>
        /// Stiffness along local x, y, z [kN/m] and about them [kNm/rad]; 0 where the link has no
        /// spring, which leaves that direction free.
        /// </summary>
        public double[] Stiffness { get; } = new double[6];
    }

    /// <summary>The loads of one load pattern.</summary>
    public class ExportLoadCase
    {
        /// <summary>The pattern's name; null when it has none.</summary>
        public string Name { get; set; }
        /// <summary>Where the pattern is among the model's, from 1; an unnamed pattern's load case is called LP and this.</summary>
        public int Number { get; set; }
        /// <summary>What the pattern's loads are multiplied by: its factor times its time series' factor.</summary>
        public double Factor { get; set; } = 1.0;
        /// <summary>The sum of the pattern's gravity factors; 0 for a pattern without gravity.</summary>
        public double GravityFactor { get; set; }

        public List<PointLoad> PointLoads { get; } = new List<PointLoad>();
        public List<ExportBeamLoad> BeamLoads { get; } = new List<ExportBeamLoad>();
        public List<ExportShellLoad> ShellLoads { get; } = new List<ExportShellLoad>();
    }

    /// <summary>A uniform load over a whole beam, in global axes.</summary>
    public class ExportBeamLoad
    {
        public int Beam { get; set; }
        /// <summary>[kN/m]</summary>
        public Vector3d Force { get; set; }
    }

    /// <summary>A uniform pressure over a whole shell element, in global axes.</summary>
    public class ExportShellLoad
    {
        public int Shell { get; set; }
        /// <summary>[kN/m²]</summary>
        public Vector3d Pressure { get; set; }
    }
}
