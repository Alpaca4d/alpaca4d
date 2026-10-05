using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alpaca4d
{
    public static class Units
    {
        public static LengthUnit Length = LengthUnit.m;
        public static ForceUnit Force = ForceUnit.kN;
        public static MassUnits Mass = MassUnits.kg;
        public static TimeUnit Time = TimeUnit.s;
        public static AngleUnit Angle = AngleUnit.rad;
    }

    /// <summary>
    /// The one place that knows how to turn a mass the user typed into the mass the solver
    /// wants.
    ///
    /// OpenSees has no unit system. "mass" writes its arguments straight onto the diagonal
    /// of the node's mass matrix, an nDMaterial's rho is read back by every solid and shell
    /// as mass per unit volume, and a beam's -mass is mass per unit length; DOF_Group and
    /// FE_Element then add all of them into the same matrix at a factor of one. So they all
    /// have to be in one unit, and with <see cref="Units.Force"/> in kN and
    /// <see cref="Units.Length"/> in m that unit is kN s^2/m - the tonne.
    ///
    /// Alpaca4d takes density and mass from the user in kg, so everything crossing into a
    /// Tcl deck goes through here. It exists because this conversion used to be written out
    /// by hand at each call site, and three of the four got it wrong: point masses carried a
    /// stray 9.81 (the kg-to-kN factor for a *weight*, which a mass matrix must not have),
    /// and the nD materials carried no conversion at all, making every shell, brick and
    /// tetrahedron a thousand times too heavy.
    /// </summary>
    public static class ModelMass
    {
        /// <summary>kg to the solver's mass unit.</summary>
        public const double KgToModelMass = 1.0 / 1000.0;

        /// <summary>A mass, or a mass density, given in kg - converted for the deck.</summary>
        public static double FromKg(double kg) => kg * KgToModelMass;

        /// <summary>As <see cref="FromKg(double)"/>, treating no value as no mass.</summary>
        public static double FromKg(double? kg) => (kg ?? 0.0) * KgToModelMass;

        /// <summary>Per-axis masses given in kg - converted for the deck.</summary>
        public static Vector3d FromKg(Vector3d kg) => kg * KgToModelMass;

        /// <summary>A value read off a deck, back in kg.</summary>
        public static double ToKg(double mass) => mass / KgToModelMass;

        /// <summary>Per-axis values read off a deck, back in kg.</summary>
        public static Vector3d ToKg(Vector3d mass) => mass / KgToModelMass;
    }

    /// <summary>
    /// Stresses between N/mm² - the unit material tables and the Eurocodes are written in - and the
    /// model's own unit, which with <see cref="Units.Force"/> in kN and <see cref="Units.Length"/> in m
    /// is kN/m², a thousand times smaller.
    /// </summary>
    public static class ModelStress
    {
        /// <summary>How many model stress units make one N/mm².</summary>
        public static double PerMPa
        {
            get
            {
                double newtonsPerForce = Units.Force == ForceUnit.kN ? 1000.0 : 1.0;
                double millimetresPerLength = ModelLength.MillimetresPerUnit;
                return millimetresPerLength * millimetresPerLength / newtonsPerForce;
            }
        }

        public static double FromMPa(double mpa) => mpa * PerMPa;

        public static double ToMPa(double stress) => stress / PerMPa;
    }

    /// <summary>Lengths in millimetres, which is what thickness limits in the Eurocodes are written in.</summary>
    public static class ModelLength
    {
        public static double MillimetresPerUnit => Units.Length == LengthUnit.m ? 1000.0 : 1.0;

        public static double ToMillimetres(double length) => length * MillimetresPerUnit;
    }

    public enum AngleUnit
    {
        deg,
        rad,
    }
    public enum TimeUnit
    {
        s,
        min,
        h,
    }

    public enum LengthUnit
    {
        mm,
        m,
    }

    public enum ForceUnit
    {
        N,
        kN,
    }

    public enum MassUnits
    {
        kg,
    }
}
