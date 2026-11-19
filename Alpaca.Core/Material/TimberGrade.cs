using System;
using System.IO;
using System.Linq;

using Newtonsoft.Json.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Material
{
    /// <summary>What a timber product is, for the factors EN 1995-1-1 sets by product: k_h, γM, βc.</summary>
    public enum TimberKind
    {
        /// <summary>Solid timber, softwood or hardwood: EN 338 strength classes C and D.</summary>
        Solid,
        /// <summary>Glued laminated timber: EN 14080 classes GL..h and GL..c.</summary>
        Glulam
    }

    /// <summary>
    /// A timber strength class: the characteristic values EN 1995-1-1 checks a member with, from
    /// EN 338 (solid timber) or EN 14080 (glulam).
    ///
    /// Strengths and stiffnesses are held in N/mm², as tabulated, densities in kg/m³.
    /// </summary>
    public class TimberGrade : IDesignGrade
    {
        public string Family => "Timber";
        public string Name { get; }
        public TimberKind Kind { get; }

        /// <summary>Bending strength f_m,k [N/mm²].</summary>
        public double FmK { get; }
        /// <summary>Tension strength along the grain f_t,0,k [N/mm²].</summary>
        public double Ft0K { get; }
        /// <summary>Tension strength across the grain f_t,90,k [N/mm²].</summary>
        public double Ft90K { get; }
        /// <summary>Compression strength along the grain f_c,0,k [N/mm²].</summary>
        public double Fc0K { get; }
        /// <summary>Compression strength across the grain f_c,90,k [N/mm²].</summary>
        public double Fc90K { get; }
        /// <summary>Shear strength f_v,k [N/mm²].</summary>
        public double FvK { get; }
        /// <summary>Mean modulus of elasticity along the grain E_0,mean [N/mm²].</summary>
        public double E0Mean { get; }
        /// <summary>5th percentile modulus of elasticity along the grain E_0,05 [N/mm²].</summary>
        public double E005 { get; }
        /// <summary>Mean shear modulus G_mean [N/mm²].</summary>
        public double GMean { get; }
        /// <summary>5th percentile shear modulus G_0,05 [N/mm²], as tabulated or, where it is not, <see cref="GMean"/> E_0,05 / E_0,mean.</summary>
        public double G005 { get; }
        /// <summary>Whether <see cref="G005"/> comes from the table rather than the ratio.</summary>
        public bool G005Tabulated { get; }
        /// <summary>Characteristic density ρ_k [kg/m³].</summary>
        public double RhoK { get; }
        /// <summary>Mean density ρ_mean [kg/m³].</summary>
        public double RhoMean { get; }

        public TimberGrade(string name, TimberKind kind, double fmK, double ft0K, double ft90K, double fc0K, double fc90K,
                           double fvK, double e0Mean, double e005, double gMean, double? g005, double rhoK, double rhoMean)
        {
            this.Name = name;
            this.Kind = kind;
            this.FmK = fmK;
            this.Ft0K = ft0K;
            this.Ft90K = ft90K;
            this.Fc0K = fc0K;
            this.Fc90K = fc90K;
            this.FvK = fvK;
            this.E0Mean = e0Mean;
            this.E005 = e005;
            this.GMean = gMean;
            this.G005Tabulated = g005.HasValue && g005.Value > 0.0;
            this.G005 = this.G005Tabulated ? g005.Value : e0Mean > 0.0 ? gMean * e005 / e0Mean : 0.0;
            this.RhoK = rhoK;
            this.RhoMean = rhoMean;
        }

        /// <summary>
        /// The values the check cannot go without and the grade lacks - zero or missing in its table -
        /// by name; none for a complete grade.
        /// </summary>
        public string[] Missing()
        {
            return new (string Name, double Value)[]
            {
                ("f_m,k", FmK), ("f_t,0,k", Ft0K), ("f_c,0,k", Fc0K), ("f_v,k", FvK), ("E_0,05", E005), ("G_mean", GMean),
            }.Where(v => !(v.Value > 0.0)).Select(v => v.Name).ToArray();
        }

        public override string ToString() => $"{this.Name} ({(this.Kind == TimberKind.Glulam ? "glulam" : "solid timber")}, f_m,k {this.FmK} N/mm²)";

        /// <summary>
        /// A grade from one entry of a material database - the schema of timber_properties.json, which
        /// is also the schema of the custom databases Material Library Elastic accepts. Null unless the
        /// entry says it is timber. Its "type" names the product: Glulam, or solid timber otherwise.
        /// </summary>
        public static TimberGrade FromEntry(string name, JObject entry)
        {
            if (entry == null)
                return null;

            var family = entry["material_type"]?.Value<string>();
            if (!string.Equals(family, "Timber", StringComparison.OrdinalIgnoreCase))
                return null;

            double? Read(string key) => entry[key] != null && entry[key].Type != JTokenType.Null ? entry[key].Value<double>() : (double?)null;

            var type = entry["type"]?.Value<string>() ?? "";
            var kind = type.IndexOf("glulam", StringComparison.OrdinalIgnoreCase) >= 0 ? TimberKind.Glulam : TimberKind.Solid;

            return new TimberGrade(name, kind,
                Read("fm_k") ?? 0.0, Read("ft_0_k") ?? 0.0, Read("ft_90_k") ?? 0.0, Read("fc_0_k") ?? 0.0, Read("fc_90_k") ?? 0.0,
                Read("fv_k") ?? 0.0, Read("E_mean") ?? 0.0, Read("E0_05") ?? 0.0, Read("G_mean") ?? 0.0, Read("G_05"),
                Read("rho_k") ?? 0.0, Read("rho_mean") ?? 0.0);
        }

        /// <summary>
        /// The grade a material name stands for, looked up in Alpaca4d's own timber table, whatever its
        /// case, spaces, hyphens and underscores: "C24", "c 24", "GL24h", "GL 24 h". Null for a name
        /// that is not a class the table holds.
        /// </summary>
        public static TimberGrade Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var table = Table();
            if (table == null)
                return null;

            string Plain(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            string typed = Plain(name);
            var entry = table.Properties().FirstOrDefault(p => Plain(p.Name) == typed);
            return entry != null ? FromEntry(entry.Name, entry.Value as JObject) : null;
        }

        private static JObject _table;
        private static readonly object TableLock = new object();

        private static JObject Table()
        {
            if (_table != null)
                return _table;

            lock (TableLock)
            {
                if (_table != null)
                    return _table;

                using (var stream = typeof(TimberGrade).Assembly.GetManifestResourceStream("Alpaca4d.Resources.Material.timber_properties.json"))
                {
                    if (stream == null)
                        return null;

                    using (var reader = new StreamReader(stream))
                        _table = JObject.Parse(reader.ReadToEnd());
                }

                return _table;
            }
        }
    }
}
