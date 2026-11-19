using System.Collections.Generic;

namespace Alpaca4d.Design
{
    /// <summary>
    /// Everything the EN 1995-1-1 check of one member worked out, as values: Utilisation's Timber
    /// Report output. A Panel shows it as the report's text; Deconstruct Anything splits it into its
    /// fields, and <see cref="Checks"/> once more into each check's.
    ///
    /// Strengths of the grade in N/mm², as tabulated; section properties, design strengths and M_cr in
    /// the model's units, as the forces are.
    /// </summary>
    public sealed class TimberReport
    {
        /// <summary>The strength class, "C24", "GL24h".</summary>
        public string Grade;
        /// <summary>"solid timber" or "glulam".</summary>
        public string Product;
        /// <summary>f_m,k, f_t,0,k, f_c,0,k, f_v,k, E_0,05 and G_0,05 [N/mm²]; ρ_k [kg/m³].</summary>
        public double FmK, Ft0K, Fc0K, FvK, E005, G005, RhoK;

        /// <summary>The member's service class, 1 to 3.</summary>
        public int ServiceClass;
        /// <summary>The load-duration class k_mod is for: Permanent ... Instantaneous.</summary>
        public string Duration;
        /// <summary>Where the duration came from: the patterns that set it, and those that gave none.</summary>
        public string DurationSource;
        /// <summary>k_mod, Table 3.1.</summary>
        public double Kmod;
        /// <summary>γM, and where it came from: Table 2.3, an accidental combination, or given.</summary>
        public double GammaM;
        public string GammaMSource;
        /// <summary>k_h for My, for Mz and in tension (3.1, 3.2); k_cr (6.1.7); k_m (6.1.6); k_shape (6.15).</summary>
        public double KhY, KhZ, KhTension, Kcr, Km, Kshape;

        /// <summary>The section, as "rectangle 0.2 x 0.1 (Height x Width)".</summary>
        public string Section;
        public double A, Iy, Iz, It, Wy, Wz;

        /// <summary>The design strengths, k_mod k_h f_k / γM [the model's stress unit].</summary>
        public double Ft0d, Fc0d, FmYd, FmZd, Fvd;

        /// <summary>6.3.2: the largest compression along the member, the relative slenderness and k_c about each axis.</summary>
        public double Compression, LambdaRelY, LambdaRelZ, KcY, KcZ;
        /// <summary>6.3.3, for the worst piece between lateral restraints: M_cr, σ_m,crit, λ_rel,m and k_crit. Null with no major axis bending or for a circle.</summary>
        public double? Mcr, SigmaMCrit, LambdaRelM, Kcrit;

        /// <summary>Each check: its name, utilisation, clause, where it governs and how it was worked out.</summary>
        public List<TimberReportCheck> Checks = new List<TimberReportCheck>();

        /// <summary>The largest utilisation, and the check that gives it - or, with none, what could not be checked.</summary>
        public double? Max;
        public string Governing;

        /// <summary>What EN 1995-1-1 asks that the check does not cover.</summary>
        public string NotCovered;

        private readonly string _text;

        public TimberReport(string text) { _text = text ?? ""; }

        /// <summary>The report as text, for a Panel.</summary>
        public override string ToString() => _text;
    }

    /// <summary>One check of a <see cref="TimberReport"/>.</summary>
    public sealed class TimberReportCheck
    {
        /// <summary>As the output is named: Axial, ShearY, ... InteractionZ.</summary>
        public string Name;
        /// <summary>The utilisation, 1 at the limit; null when it could not be worked out.</summary>
        public double? Value;
        /// <summary>The clause or expressions of EN 1995-1-1 it is.</summary>
        public string Clause;
        /// <summary>The integration section it governs at, from 1; 0 for a check of the member as a whole.</summary>
        public int Section;
        /// <summary>Where that section is along the member, 0 to 1; null when the positions are not known or for the member as a whole.</summary>
        public double? Position;
        /// <summary>How it was worked out, in words and numbers.</summary>
        public string Detail;

        public override string ToString() =>
            $"{Name} {(Value.HasValue ? Value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "-")} ({Clause}): {Detail}";
    }
}
