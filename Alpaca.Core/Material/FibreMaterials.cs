using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Alpaca4d.Core.Utils;
using Alpaca4d.Generic;

namespace Alpaca4d.Material
{
    /// <summary>
    /// What the fibre analyses need to know of a uniaxial material that is not one of the three they were
    /// first written for (Concrete01, Steel01, ReinforcingSteel): whether it is concrete, its initial
    /// modulus, where it yields and fails. Stresses in kN/m2, strains dimensionless, all strains given here
    /// positive.
    /// </summary>
    public interface IFibreMaterial : IMaterial
    {
        /// <summary>Concrete is held to its crushing strain in compression and carries no limit in tension.</summary>
        bool IsConcrete { get; }

        /// <summary>The slope the law starts with, kN/m2; 0 when it has none to give.</summary>
        double InitialModulus { get; }

        /// <summary>The strain it first yields at, or NaN for a law that does not yield.</summary>
        double YieldStrain { get; }

        /// <summary>Concrete's strain at its peak stress, positive; 0 for other materials.</summary>
        double PeakStrain { get; }

        /// <summary>Concrete's crushing strain, positive; 0 for other materials.</summary>
        double CrushingStrain { get; }

        /// <summary>What it weighs when it is given no density, kg/m3: 2 500 concrete, 7 850 steel, 0 other.</summary>
        double DefaultDensity { get; }

        MinMax MinMax { get; }
    }

    /// <summary>uniaxialMaterial Concrete02: Concrete01's compression with a tensile strength and linear tension softening.</summary>
    public class Concrete02 : EntityBase, IFibreMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public System.Drawing.Color? Color { get; set; }
        public double? Rho { get; set; }
        public MinMax MinMax { get; set; }
        public string MaterialDimension => "UniaxialMaterial";

        /// <summary>Compressive strength, kN/m2. Negative.</summary>
        public double Fpc { get; set; }
        /// <summary>Strain at Fpc. Negative.</summary>
        public double EpsC0 { get; set; }
        /// <summary>Crushing strength, kN/m2. Negative.</summary>
        public double Fpcu { get; set; }
        /// <summary>Strain at the crushing strength. Negative.</summary>
        public double EpsU { get; set; }
        /// <summary>Ratio between the unloading slope at EpsU and the initial slope.</summary>
        public double Lambda { get; set; }
        /// <summary>Tensile strength, kN/m2. Positive.</summary>
        public double Ft { get; set; }
        /// <summary>Tension softening stiffness, kN/m2: the slope of the falling branch. Positive.</summary>
        public double Ets { get; set; }

        public static Concrete02 C2530 => new Concrete02("C25/30", -25000.0, -0.002, -20000.0, -0.0035, 0.2, 2600.0, 2000000.0, false);

        public Concrete02(string name, double fpc, double epsC0, double fpcu, double epsU, double lambda, double ft, double ets, bool isMinMax)
        {
            MatName = name; Fpc = fpc; EpsC0 = epsC0; Fpcu = fpcu; EpsU = epsU; Lambda = lambda; Ft = ft; Ets = ets;
            if (isMinMax)
                MinMax = new MinMax(name, this, EpsU, 0.0);
        }

        public bool IsConcrete => true;
        public double InitialModulus => EpsC0 != 0.0 ? Math.Abs(2.0 * Fpc / EpsC0) : 0.0;
        public double YieldStrain => double.NaN;
        public double PeakStrain => Math.Abs(EpsC0);
        public double CrushingStrain => Math.Abs(EpsU);
        public double DefaultDensity => 2500.0;

        public override string ToString() => string.IsNullOrWhiteSpace(MatName) ? base.ToString() : MatName;

        public override string WriteTcl()
        {
            string tcl = $"uniaxialMaterial Concrete02 {Id} {TclNumber.Write(Fpc)} {TclNumber.Write(EpsC0)} {TclNumber.Write(Fpcu)} " +
                         $"{TclNumber.Write(EpsU)} {TclNumber.Write(Lambda)} {TclNumber.Write(Ft)} {TclNumber.Write(Ets)}\n";
            return MinMax != null ? tcl + MinMax.WriteTcl() : tcl;
        }
    }

    /// <summary>uniaxialMaterial Concrete04: Popovics' compression curve, with an exponential tension branch when Fct is given.</summary>
    public class Concrete04 : EntityBase, IFibreMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public System.Drawing.Color? Color { get; set; }
        public double? Rho { get; set; }
        public MinMax MinMax { get; set; }
        public string MaterialDimension => "UniaxialMaterial";

        /// <summary>Compressive strength, kN/m2. Negative.</summary>
        public double Fc { get; set; }
        /// <summary>Strain at Fc. Negative.</summary>
        public double EpsC { get; set; }
        /// <summary>Crushing strain. Negative.</summary>
        public double EpsCu { get; set; }
        /// <summary>Initial modulus, kN/m2.</summary>
        public double Ec { get; set; }
        /// <summary>Tensile strength, kN/m2, or null for none: the three after it are then not written.</summary>
        public double? Fct { get; set; }
        /// <summary>Strain at the tensile strength.</summary>
        public double? Et { get; set; }
        /// <summary>Exponential decay of the tension branch; null takes OpenSees' own.</summary>
        public double? Beta { get; set; }

        public static Concrete04 C2530 => new Concrete04("C25/30", -25000.0, -0.002, -0.0035, 31000000.0, null, null, null, false);

        public Concrete04(string name, double fc, double epsC, double epsCu, double ec, double? fct, double? et, double? beta, bool isMinMax)
        {
            MatName = name; Fc = fc; EpsC = epsC; EpsCu = epsCu; Ec = ec; Fct = fct; Et = et; Beta = beta;
            if (isMinMax)
                MinMax = new MinMax(name, this, EpsCu, 0.0);
        }

        public bool IsConcrete => true;
        public double InitialModulus => Ec;
        public double YieldStrain => double.NaN;
        public double PeakStrain => Math.Abs(EpsC);
        public double CrushingStrain => Math.Abs(EpsCu);
        public double DefaultDensity => 2500.0;

        public override string ToString() => string.IsNullOrWhiteSpace(MatName) ? base.ToString() : MatName;

        public override string WriteTcl()
        {
            if (Fct.HasValue != Et.HasValue)
                throw new InvalidOperationException("Concrete04 takes the tensile strength fct and its strain et together or not at all.");
            if (Beta.HasValue && !Fct.HasValue)
                throw new InvalidOperationException("Concrete04's beta goes with the tension branch: give fct and et as well.");

            string tcl = $"uniaxialMaterial Concrete04 {Id} {TclNumber.Write(Fc)} {TclNumber.Write(EpsC)} {TclNumber.Write(EpsCu)} {TclNumber.Write(Ec)}";
            if (Fct.HasValue)
                tcl += $" {TclNumber.Write(Fct.Value)} {TclNumber.Write(Et.Value)}";
            if (Beta.HasValue)
                tcl += $" {TclNumber.Write(Beta.Value)}";
            tcl += "\n";
            return MinMax != null ? tcl + MinMax.WriteTcl() : tcl;
        }
    }

    /// <summary>uniaxialMaterial Steel02: Giuffre-Menegotto-Pinto steel, with isotropic hardening when a1 to a4 are given.</summary>
    public class Steel02 : EntityBase, IFibreMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public System.Drawing.Color? Color { get; set; }
        public double? Rho { get; set; }
        public MinMax MinMax { get; set; }
        public string MaterialDimension => "UniaxialMaterial";

        public double Fy { get; set; }
        public double E0 { get; set; }
        /// <summary>Strain-hardening ratio: the post-yield tangent over E0.</summary>
        public double B { get; set; }
        /// <summary>Elastic to plastic transition, 10 to 20 (OpenSees suggests 18).</summary>
        public double R0 { get; set; }
        public double CR1 { get; set; }
        public double CR2 { get; set; }
        public double? A1 { get; set; }
        public double? A2 { get; set; }
        public double? A3 { get; set; }
        public double? A4 { get; set; }
        /// <summary>Initial stress, kN/m2, or null for none.</summary>
        public double? SigInit { get; set; }

        public static Steel02 S355 => new Steel02("S355", 355000.0, 210000000.0, 0.01, 18.0, 0.925, 0.15);

        public Steel02(string name, double fy, double e0, double b, double r0, double cr1, double cr2)
        {
            MatName = name; Fy = fy; E0 = e0; B = b; R0 = r0; CR1 = cr1; CR2 = cr2;
        }

        public bool IsConcrete => false;
        public double InitialModulus => E0;
        public double YieldStrain => E0 > 0.0 ? Math.Abs(Fy) / E0 : double.NaN;
        public double PeakStrain => 0.0;
        public double CrushingStrain => 0.0;
        public double DefaultDensity => 7850.0;

        public override string ToString() => string.IsNullOrWhiteSpace(MatName) ? base.ToString() : MatName;

        public override string WriteTcl()
        {
            string tcl = $"uniaxialMaterial Steel02 {Id} {TclNumber.Write(Fy)} {TclNumber.Write(E0)} {TclNumber.Write(B)} " +
                         $"{TclNumber.Write(R0)} {TclNumber.Write(CR1)} {TclNumber.Write(CR2)}";
            bool any = A1.HasValue || A2.HasValue || A3.HasValue || A4.HasValue;
            if (any && !(A1.HasValue && A2.HasValue && A3.HasValue && A4.HasValue))
                throw new InvalidOperationException("Steel02 takes a1, a2, a3 and a4 together or not at all.");
            if (SigInit.HasValue && !any)
                tcl += " 0 1 0 1";   // sigInit follows the four, which then do nothing
            if (any)
                tcl += $" {TclNumber.Write(A1.Value)} {TclNumber.Write(A2.Value)} {TclNumber.Write(A3.Value)} {TclNumber.Write(A4.Value)}";
            if (SigInit.HasValue)
                tcl += $" {TclNumber.Write(SigInit.Value)}";
            return tcl + "\n";
        }
    }

    /// <summary>uniaxialMaterial ElasticPP: elastic, then perfectly plastic, the same in tension and compression unless told otherwise.</summary>
    public class ElasticPP : EntityBase, IFibreMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public System.Drawing.Color? Color { get; set; }
        public double? Rho { get; set; }
        public MinMax MinMax { get; set; }
        public string MaterialDimension => "UniaxialMaterial";

        public double E { get; set; }
        /// <summary>Yield strain in tension. Positive.</summary>
        public double EpsyP { get; set; }
        /// <summary>Yield strain in compression, negative; null mirrors EpsyP.</summary>
        public double? EpsyN { get; set; }
        /// <summary>Strain at zero stress at the start, or null for none.</summary>
        public double? Eps0 { get; set; }

        public ElasticPP(string name, double e, double epsyP, double? epsyN, double? eps0)
        {
            MatName = name; E = e; EpsyP = epsyP; EpsyN = epsyN; Eps0 = eps0;
        }

        public bool IsConcrete => false;
        public double InitialModulus => E;
        public double YieldStrain => Math.Abs(EpsyP);
        public double PeakStrain => 0.0;
        public double CrushingStrain => 0.0;
        public double DefaultDensity => 0.0;

        public override string ToString() => string.IsNullOrWhiteSpace(MatName) ? base.ToString() : MatName;

        public override string WriteTcl()
        {
            string tcl = $"uniaxialMaterial ElasticPP {Id} {TclNumber.Write(E)} {TclNumber.Write(EpsyP)}";
            if (EpsyN.HasValue || Eps0.HasValue)
                tcl += $" {TclNumber.Write(EpsyN ?? -EpsyP)}";
            if (Eps0.HasValue)
                tcl += $" {TclNumber.Write(Eps0.Value)}";
            return tcl + "\n";
        }
    }

    /// <summary>
    /// uniaxialMaterial Hysteretic: a bilinear or trilinear backbone, each side of its own, with pinching and
    /// damage. Stress in kN/m2. The negative side, when not given, mirrors the positive.
    /// </summary>
    public class Hysteretic : EntityBase, IFibreMaterial
    {
        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public System.Drawing.Color? Color { get; set; }
        public double? Rho { get; set; }
        public MinMax MinMax { get; set; }
        public string MaterialDimension => "UniaxialMaterial";

        /// <summary>The backbone's points in tension, (strain, stress), 2 or 3, positive and rising.</summary>
        public List<(double Strain, double Stress)> Positive { get; set; } = new List<(double, double)>();
        /// <summary>The backbone's points in compression, negative, the same number as Positive; null mirrors Positive.</summary>
        public List<(double Strain, double Stress)> Negative { get; set; }
        public double PinchX { get; set; } = 1.0;
        public double PinchY { get; set; } = 1.0;
        public double Damage1 { get; set; }
        public double Damage2 { get; set; }
        /// <summary>Power of the degrading unloading stiffness, or null for none.</summary>
        public double? Beta { get; set; }

        /// <summary>The tension backbone as strains and the stresses at them, the same number of each.</summary>
        public Hysteretic(string name, double[] strains, double[] stresses)
        {
            if (strains == null || stresses == null || strains.Length != stresses.Length)
                throw new ArgumentException("Hysteretic's backbone needs a stress for every strain.");
            MatName = name;
            Positive = strains.Zip(stresses, (e, f) => (e, f)).ToList();
        }

        /// <summary>The compression backbone, negative: strains and the stresses at them.</summary>
        public void SetNegative(double[] strains, double[] stresses)
        {
            Negative = strains.Zip(stresses, (e, f) => (e, f)).ToList();
        }

        public bool IsConcrete => false;
        public double InitialModulus => Positive.Count > 0 && Positive[0].Strain != 0.0 ? Positive[0].Stress / Positive[0].Strain : 0.0;
        public double YieldStrain => Positive.Count > 0 ? Math.Abs(Positive[0].Strain) : double.NaN;
        public double PeakStrain => 0.0;
        public double CrushingStrain => 0.0;
        public double DefaultDensity => 0.0;

        public override string ToString() => string.IsNullOrWhiteSpace(MatName) ? base.ToString() : MatName;

        public override string WriteTcl()
        {
            if (Positive.Count < 2 || Positive.Count > 3)
                throw new InvalidOperationException("Hysteretic takes two or three points of backbone in tension.");
            var negative = Negative ?? Positive.Select(p => (-p.Strain, -p.Stress)).ToList();
            if (negative.Count != Positive.Count)
                throw new InvalidOperationException("Hysteretic's backbone has as many points in compression as in tension.");

            string Side(List<(double Strain, double Stress)> points) =>
                string.Join(" ", points.Select(p => $"{TclNumber.Write(p.Stress)} {TclNumber.Write(p.Strain)}"));

            string tcl = $"uniaxialMaterial Hysteretic {Id} {Side(Positive)} {Side(negative)} " +
                         $"{TclNumber.Write(PinchX)} {TclNumber.Write(PinchY)} {TclNumber.Write(Damage1)} {TclNumber.Write(Damage2)}";
            if (Beta.HasValue)
                tcl += $" {TclNumber.Write(Beta.Value)}";
            return tcl + "\n";
        }
    }

    /// <summary>
    /// uniaxialMaterial Elastic, linear to a rupture strain: a carbon or glass fibre laminate, a tendon, a brittle
    /// plate. Past either rupture strain a MinMax wrapper fails the fibre, so the section analyses end there.
    /// </summary>
    public class ElasticFibre : EntityBase, IFibreMaterial
    {
        /// <summary>Strain taken for a side that has no rupture: far past any a section reaches.</summary>
        public const double NoRupture = 1.0;

        public int? Id { get; set; } = IdGenerator.GenerateId();
        public string MatName { get; set; }
        public System.Drawing.Color? Color { get; set; }
        public double? Rho { get; set; }
        public MinMax MinMax { get; set; }
        public string MaterialDimension => "UniaxialMaterial";

        /// <summary>Elastic modulus in tension, kN/m2.</summary>
        public double E { get; set; }
        /// <summary>Elastic modulus in compression, kN/m2; null takes E.</summary>
        public double? Eneg { get; set; }
        /// <summary>Strain at rupture in tension, positive; null for none.</summary>
        public double? RuptureTension { get; set; }
        /// <summary>Strain at rupture in compression, positive; null for none.</summary>
        public double? RuptureCompression { get; set; }

        public ElasticFibre(string name, double e, double? eneg, double? ruptureTension, double? ruptureCompression)
        {
            MatName = name; E = e; Eneg = eneg; RuptureTension = ruptureTension; RuptureCompression = ruptureCompression;
            if (ruptureTension.HasValue || ruptureCompression.HasValue)
                MinMax = new MinMax(name, this, -(ruptureCompression ?? NoRupture), ruptureTension ?? NoRupture);
        }

        public bool IsConcrete => false;
        public double InitialModulus => E;
        public double YieldStrain => double.NaN;
        public double PeakStrain => 0.0;
        public double CrushingStrain => 0.0;
        public double DefaultDensity => 0.0;

        public override string ToString() => string.IsNullOrWhiteSpace(MatName) ? base.ToString() : MatName;

        public override string WriteTcl()
        {
            string tcl = $"uniaxialMaterial Elastic {Id} {TclNumber.Write(E)}";
            if (Eneg.HasValue)
                tcl += $" 0 {TclNumber.Write(Eneg.Value)}";
            tcl += "\n";
            return MinMax != null ? tcl + MinMax.WriteTcl() : tcl;
        }
    }
}
