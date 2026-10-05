using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Newtonsoft.Json.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Material
{
    /// <summary>
    /// What a material is for design, as opposed to for analysis: which family it belongs to and the
    /// characteristic strengths a code check needs. The solver never sees it - an elastic material is
    /// E and G to OpenSees whatever it is made of - so it rides along on the material for the
    /// components that check members after the analysis.
    ///
    /// One implementation per family, because the strengths a steel check needs (fy, fu) have nothing
    /// in common with what a timber one does (fm,k, fv,k, fc,0,k, ...).
    /// </summary>
    public interface IDesignGrade
    {
        /// <summary>"Steel", "Timber", "Concrete" - what the Utilisation component reports as the type.</summary>
        string Family { get; }

        /// <summary>The grade as named in its standard, "S355".</summary>
        string Name { get; }
    }

    /// <summary>
    /// A structural steel grade: yield and ultimate strength from EN 1993-1-1 Table 3.1, including the
    /// lower values that apply to elements thicker than 40 mm.
    ///
    /// Strengths are held in N/mm², as tabulated. <see cref="Fy"/> turns them into the model's unit.
    /// </summary>
    public class SteelGrade : IDesignGrade
    {
        public string Family => "Steel";
        public string Name { get; }

        /// <summary>fy for a nominal thickness up to 40 mm [N/mm²].</summary>
        public double FyMPa { get; }
        /// <summary>fu for a nominal thickness up to 40 mm [N/mm²].</summary>
        public double FuMPa { get; }
        /// <summary>fy for 40 mm &lt; t ≤ 80 mm [N/mm²], or null where the table has no value.</summary>
        public double? FyThickMPa { get; }
        /// <summary>fu for 40 mm &lt; t ≤ 80 mm [N/mm²], or null where the table has no value.</summary>
        public double? FuThickMPa { get; }

        public SteelGrade(string name, double fyMPa, double fuMPa, double? fyThickMPa = null, double? fuThickMPa = null)
        {
            this.Name = name;
            this.FyMPa = fyMPa;
            this.FuMPa = fuMPa;
            this.FyThickMPa = fyThickMPa;
            this.FuThickMPa = fuThickMPa;
        }

        /// <summary>
        /// fy [N/mm²] for an element whose thickest plate is <paramref name="thicknessMm"/>. Table 3.1
        /// stops at 80 mm; past it the 40-80 mm value is returned and <paramref name="beyondTable"/> says so.
        /// </summary>
        public double YieldStrengthMPa(double thicknessMm, out bool beyondTable)
        {
            beyondTable = thicknessMm > 80.0 || (thicknessMm > 40.0 && !this.FyThickMPa.HasValue);

            if (thicknessMm <= 40.0 || !this.FyThickMPa.HasValue)
                return this.FyMPa;

            return this.FyThickMPa.Value;
        }

        /// <summary>fy in the model's stress unit, for an element whose thickest plate is <paramref name="thicknessMm"/>.</summary>
        public double Fy(double thicknessMm) => ModelStress.FromMPa(YieldStrengthMPa(thicknessMm, out _));

        public override string ToString() => $"{this.Name} (fy {this.FyMPa} N/mm²)";

        /// <summary>
        /// A grade from one entry of a material database - the schema of steel_properties.json, which
        /// is also the schema of the custom databases Material Library Elastic accepts. Null unless the
        /// entry says it is steel and gives fy.
        /// </summary>
        public static SteelGrade FromEntry(string name, JObject entry)
        {
            if (entry == null)
                return null;

            var family = entry["material_type"]?.Value<string>();
            if (!string.Equals(family, "Steel", StringComparison.OrdinalIgnoreCase))
                return null;

            double? Read(string key) => entry[key] != null && entry[key].Type != JTokenType.Null ? entry[key].Value<double>() : (double?)null;

            var fy = Read("fy");
            if (!fy.HasValue || fy.Value <= 0.0)
                return null;

            return new SteelGrade(name, fy.Value, Read("fu") ?? 0.0, Read("fy_40_80"), Read("fu_40_80"));
        }

        /// <summary>
        /// The grade a material name stands for, looked up in Alpaca4d's own steel table: "S355", and
        /// the EN 10025 spellings around it - "S355JR", "s355 j2", "S 355" - all find S355. Null for a
        /// name that is not a grade the table holds.
        /// </summary>
        public static SteelGrade Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var table = Table();
            if (table == null)
                return null;

            var exact = table.Properties().FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return FromEntry(exact.Name, exact.Value as JObject);

            var match = Regex.Match(name, @"^\s*S\s*(\d{3})", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            var key = "S" + match.Groups[1].Value;
            var entry = table.Properties().FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
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

                using (var stream = typeof(SteelGrade).Assembly.GetManifestResourceStream("Alpaca4d.Resources.Material.steel_properties.json"))
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

    public static class DesignGrades
    {
        /// <summary>
        /// The design grade of a material: the one it carries, or else the one its name stands for, so
        /// a material made by hand and called "S355" is checked as S355. Null when neither says.
        /// </summary>
        public static IDesignGrade Of(IUniaxialMaterial material)
        {
            if (material == null)
                return null;

            if (material.Grade != null)
                return material.Grade;

            var elastic = material as UniaxialMaterialElastic;
            return elastic != null ? SteelGrade.Find(elastic.MatName) : null;
        }
    }
}
