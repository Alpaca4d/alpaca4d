using System;
using System.Collections.Generic;

namespace Alpaca4d.Seismic
{
    /// <summary>
    /// The spectrum of the Italian NTC 2018, 3.2.3: drawn from the three numbers the hazard grid gives for
    /// a site and a return period - ag, Fo and Tc* - with the ground and the topography.
    ///
    /// Elastic (3.2.3.2) when no behaviour factor is given, scaled for damping by η. With one it is the
    /// design spectrum of 3.2.3.5: the same expressions with η replaced by 1/q, and never below 0.2·ag.
    /// With q = 1 that is the elastic spectrum at 5%, which is what SLO and SLD use.
    /// </summary>
    public class NtcSpectrum : ResponseSpectrum
    {
        public override string Code => "NTC 2018";

        public override double MassRatioRequired => 0.85;

        /// <summary>Peak ground acceleration on type A ground, in g.</summary>
        public double Ag { get; }

        /// <summary>Peak amplification of the spectrum on type A ground.</summary>
        public double F0 { get; }

        /// <summary>Tc*, the period at the start of the constant velocity branch on type A ground, in s.</summary>
        public double TcStar { get; }

        /// <summary>A to E.</summary>
        public string Soil { get; }

        /// <summary>T1 to T4.</summary>
        public string Topography { get; }

        public SpectrumComponent Component { get; }

        /// <summary>The behaviour factor of a design spectrum, or null for the elastic one.</summary>
        public double? Q { get; }

        public double SS { get; }
        public double CC { get; }
        public double ST { get; }
        public double S => SS * ST;
        public double TB { get; }
        public double TC { get; }
        public double TD { get; }

        /// <summary>Peak amplification of the vertical spectrum, [3.2.9]; 0 for the horizontal one.</summary>
        public double Fv { get; }

        /// <param name="ag">Peak ground acceleration on type A ground, in g.</param>
        /// <param name="f0">Fo, at least 2.2 by the code; checked only to be positive.</param>
        /// <param name="tcStar">Tc*, in s.</param>
        /// <param name="soil">Ground category, A to E.</param>
        /// <param name="topography">Topographic category, T1 to T4.</param>
        /// <param name="q">Behaviour factor for the design spectrum; null for the elastic one.</param>
        /// <param name="dampingPercent">Viscous damping in percent.</param>
        public NtcSpectrum(double ag, double f0, double tcStar, string soil, string topography,
                           SpectrumComponent component, double? q = null, double dampingPercent = 5.0)
        {
            Ag = GroundAcceleration(ag, "ag");
            if (!(f0 > 0.0))
                throw new ArgumentException($"Fo is {Number(f0)}. It is the spectrum's peak amplification, about 2.2 to 3.");
            F0 = f0;
            if (!(tcStar > 0.0))
                throw new ArgumentException($"Tc* is {Number(tcStar)}. It is a period in seconds, greater than zero.");
            TcStar = tcStar;
            Soil = OneOf(soil, "Soil", "A", "B", "C", "D", "E");
            Topography = OneOf(topography, "Topography", "T1", "T2", "T3", "T4");
            Component = component;
            Q = BehaviourFactor(q);
            Damping = DampingRatio(dampingPercent);

            // Tab. 3.2.V, the value at the top of a slope or a ridge; the code lets it fall linearly to 1
            // at the foot.
            switch (Topography)
            {
                case "T2":
                case "T3": ST = 1.2; break;
                case "T4": ST = 1.4; break;
                default: ST = 1.0; break;
            }

            if (component == SpectrumComponent.Horizontal)
            {
                (SS, CC) = Stratigraphic(Soil, Ag, F0, TcStar);
                TC = CC * TcStar;           // [3.2.5]
                TB = TC / 3.0;              // [3.2.6]
                TD = 4.0 * Ag + 1.6;        // [3.2.7], ag already in g
            }
            else
            {
                // Tab. 3.2.VI: the same for every ground category.
                SS = 1.0;
                CC = 1.0;
                TB = 0.05;
                TC = 0.15;
                TD = 1.0;
                Fv = 1.35 * F0 * Math.Sqrt(Ag);   // [3.2.9]
            }
        }

        /// <summary>Tab. 3.2.IV: SS, held between its bounds, and CC.</summary>
        private static (double ss, double cc) Stratigraphic(string soil, double ag, double f0, double tcStar)
        {
            double f0ag = f0 * ag;
            switch (soil)
            {
                case "B": return (Clamp(1.40 - 0.40 * f0ag, 1.00, 1.20), 1.10 * Math.Pow(tcStar, -0.20));
                case "C": return (Clamp(1.70 - 0.60 * f0ag, 1.00, 1.50), 1.05 * Math.Pow(tcStar, -0.33));
                case "D": return (Clamp(2.40 - 1.50 * f0ag, 0.90, 1.80), 1.25 * Math.Pow(tcStar, -0.50));
                case "E": return (Clamp(2.00 - 1.10 * f0ag, 1.00, 1.60), 1.15 * Math.Pow(tcStar, -0.40));
                default: return (1.0, 1.0);
            }
        }

        private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));

        public override IEnumerable<double> Corners => new[] { TB, TC, TD };

        /// <summary>
        /// [3.2.2] for the horizontal component, [3.2.8] for the vertical one, with η for an elastic
        /// spectrum and 1/q for a design one (3.2.3.5).
        /// </summary>
        public override double At(double period)
        {
            double t = Math.Max(0.0, period);
            double eta = Q.HasValue ? 1.0 / Q.Value : Eta(Damping);
            double amplification = Component == SpectrumComponent.Horizontal ? F0 : Fv;
            double a = Ag * S * eta * amplification;

            double value;
            if (t < TB)
                // The vertical branch divides by η·Fo too, not by η·Fv: [3.2.8] as printed.
                value = a * (t / TB + 1.0 / (eta * F0) * (1.0 - t / TB));
            else if (t < TC)
                value = a;
            else if (t < TD)
                value = a * TC / t;
            else
                value = a * TC * TD / (t * t);

            return Q.HasValue ? Math.Max(value, 0.2 * Ag) : value;
        }

        public override List<string> Parameters
        {
            get
            {
                var lines = new List<string>
                {
                    $"NTC 2018, soil {Soil}, topography {Topography}, {Component.ToString().ToLowerInvariant()}",
                    Q.HasValue ? $"Design spectrum, q = {Number(Q.Value)}" : $"Elastic spectrum, ξ = {Number(Damping * 100.0)}%",
                    Line("ag", Ag, "g"),
                    Line("Fo", F0),
                    Line("Tc*", TcStar, "s"),
                };
                if (Component == SpectrumComponent.Vertical)
                    lines.Add(Line("Fv", Fv));
                lines.Add(Line("SS", SS));
                if (Component == SpectrumComponent.Horizontal)
                    lines.Add(Line("CC", CC));
                lines.Add(Line("ST", ST));
                lines.Add(Line("S", S));
                lines.Add(Line("TB", TB, "s"));
                lines.Add(Line("TC", TC, "s"));
                lines.Add(Line("TD", TD, "s"));
                lines.Add(Q.HasValue ? Line("1/q", 1.0 / Q.Value) : Line("η", Eta(Damping)));
                return lines;
            }
        }
    }
}
