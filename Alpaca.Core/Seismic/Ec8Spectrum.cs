using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Seismic
{
    /// <summary>
    /// The spectrum of EN 1998-1 3.2.2 with the values the code recommends, before any National Annex:
    /// the generic Eurocode spectrum.
    ///
    /// Elastic (3.2.2.2 and 3.2.2.3) when no behaviour factor is given, scaled for damping by η; design
    /// (3.2.2.5) when one is, which assumes 5% damping and has a floor of β·ag past TC. Ground types A to
    /// E only: S1 and S2 need a study of the site.
    /// </summary>
    public class Ec8Spectrum : ResponseSpectrum
    {
        public override string Code => "EN 1998-1";

        /// <summary>Reference peak ground acceleration on type A ground, in g.</summary>
        public double AgR { get; }

        /// <summary>Importance factor γI.</summary>
        public double Importance { get; }

        /// <summary>Design ground acceleration on type A ground, ag = γI·agR, in g.</summary>
        public double Ag => Importance * AgR;

        /// <summary>A to E.</summary>
        public string Ground { get; }

        /// <summary>1 or 2: where earthquakes above or below magnitude 5.5 govern the hazard.</summary>
        public int Type { get; }

        public SpectrumComponent Component { get; }

        /// <summary>The behaviour factor of a design spectrum, or null for the elastic one.</summary>
        public double? Q { get; }

        /// <summary>Lower bound factor β of the design spectrum. 0.2 is recommended.</summary>
        public double Beta { get; }

        /// <summary>Soil factor; 1 for the vertical component.</summary>
        public double S { get; }
        public double TB { get; }
        public double TC { get; }
        public double TD { get; }

        /// <summary>The peak ground acceleration the shape is scaled by: ag, or avg for the vertical component.</summary>
        public double Peak { get; }

        // Tables 3.2 (Type 1) and 3.3 (Type 2): S, TB, TC, TD per ground type.
        private static readonly Dictionary<string, double[]> Type1 = new Dictionary<string, double[]>
        {
            ["A"] = new[] { 1.00, 0.15, 0.40, 2.0 },
            ["B"] = new[] { 1.20, 0.15, 0.50, 2.0 },
            ["C"] = new[] { 1.15, 0.20, 0.60, 2.0 },
            ["D"] = new[] { 1.35, 0.20, 0.80, 2.0 },
            ["E"] = new[] { 1.40, 0.15, 0.50, 2.0 },
        };

        private static readonly Dictionary<string, double[]> Type2 = new Dictionary<string, double[]>
        {
            ["A"] = new[] { 1.00, 0.05, 0.25, 1.2 },
            ["B"] = new[] { 1.35, 0.05, 0.25, 1.2 },
            ["C"] = new[] { 1.50, 0.10, 0.25, 1.2 },
            ["D"] = new[] { 1.80, 0.10, 0.30, 1.2 },
            ["E"] = new[] { 1.60, 0.05, 0.25, 1.2 },
        };

        /// <param name="agR">Reference peak ground acceleration on type A ground, in g.</param>
        /// <param name="importance">Importance factor γI.</param>
        /// <param name="ground">Ground type, A to E.</param>
        /// <param name="type">Spectrum type, 1 or 2.</param>
        /// <param name="q">Behaviour factor for the design spectrum; null for the elastic one.</param>
        /// <param name="dampingPercent">Viscous damping in percent.</param>
        /// <param name="beta">Lower bound factor of the design spectrum.</param>
        public Ec8Spectrum(double agR, double importance, string ground, int type, SpectrumComponent component,
                           double? q = null, double dampingPercent = 5.0, double beta = 0.2)
        {
            AgR = GroundAcceleration(agR, "agR");
            if (!(importance > 0.0))
                throw new ArgumentException($"γI is {Number(importance)}. The importance factor is greater than zero; 1 for ordinary buildings.");
            Importance = importance;
            Ground = OneOf(ground, "Ground", "A", "B", "C", "D", "E");
            if (type != 1 && type != 2)
                throw new ArgumentException($"Type is {type}. It is 1, where earthquakes above magnitude 5.5 govern the hazard, or 2.");
            Type = type;
            Component = component;
            Q = BehaviourFactor(q);
            if (!(beta >= 0.0))
                throw new ArgumentException($"β is {Number(beta)}. The lower bound factor is zero or more; 0.2 is recommended.");
            Beta = beta;
            Damping = DampingRatio(dampingPercent);

            if (component == SpectrumComponent.Horizontal)
            {
                var row = (type == 1 ? Type1 : Type2)[Ground];
                S = row[0];
                TB = row[1];
                TC = row[2];
                TD = row[3];
                Peak = Ag;
            }
            else
            {
                // Table 3.4: one set for every ground type, avg a share of ag.
                S = 1.0;
                TB = 0.05;
                TC = 0.15;
                TD = 1.0;
                Peak = (type == 1 ? 0.90 : 0.45) * Ag;
            }
        }

        public override IEnumerable<double> Corners => new[] { TB, TC, TD };

        public override double At(double period)
        {
            double t = Math.Max(0.0, period);
            return Q.HasValue ? Design(t, Q.Value) : Elastic(t);
        }

        /// <summary>(3.2) to (3.5), or (3.8) to (3.11) for the vertical component.</summary>
        private double Elastic(double t)
        {
            double eta = Eta(Damping);
            double plateau = Component == SpectrumComponent.Horizontal ? 2.5 : 3.0;
            double a = Peak * S;

            if (t <= TB) return a * (1.0 + t / TB * (eta * plateau - 1.0));
            if (t <= TC) return a * eta * plateau;
            if (t <= TD) return a * eta * plateau * TC / t;
            return a * eta * plateau * TC * TD / (t * t);
        }

        /// <summary>
        /// (3.13) to (3.16). The vertical component uses the same expressions with avg for ag and S = 1
        /// (3.2.2.5(5)), so its floor is β·avg.
        /// </summary>
        private double Design(double t, double q)
        {
            double a = Peak * S;
            double floor = Beta * Peak;

            if (t <= TB) return a * (2.0 / 3.0 + t / TB * (2.5 / q - 2.0 / 3.0));
            if (t <= TC) return a * 2.5 / q;
            if (t <= TD) return Math.Max(a * 2.5 / q * TC / t, floor);
            return Math.Max(a * 2.5 / q * TC * TD / (t * t), floor);
        }

        public override List<string> Parameters
        {
            get
            {
                var lines = new List<string>
                {
                    $"EN 1998-1, recommended values, type {Type}, ground {Ground}, {Component.ToString().ToLowerInvariant()}",
                    Q.HasValue ? $"Design spectrum, q = {Number(Q.Value)}" : $"Elastic spectrum, ξ = {Number(Damping * 100.0)}%",
                    Line("ag", Ag, "g"),
                };
                if (Component == SpectrumComponent.Vertical)
                    lines.Add(Line("avg", Peak, "g"));
                lines.Add(Line("S", S));
                lines.Add(Line("TB", TB, "s"));
                lines.Add(Line("TC", TC, "s"));
                lines.Add(Line("TD", TD, "s"));
                if (Q.HasValue)
                    lines.Add(Line("β", Beta));
                else
                    lines.Add(Line("η", Eta(Damping)));
                return lines;
            }
        }
    }
}
