using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Section
{
    /// <summary>A family of catalogue sections: an IPE, HEA, HEB or HEM series, or every CHS, RHS or pair of angles.</summary>
    public sealed class SectionFamily
    {
        /// <summary>What the family is called: IPE, HEA, HEB, HEM, CHS, RHS or 2L.</summary>
        public string Name;

        /// <summary>Its sections, smallest area first.</summary>
        public List<IUniaxialSection> Sections = new List<IUniaxialSection>();
    }

    /// <summary>
    /// The steel sections of the database embedded in this assembly, the ones Section Library offers:
    /// I-sections (IPE, HEA, HEB, HEM), circular hollow sections (O), rectangular hollow sections ([])
    /// and pairs of angles (2L), with their dimensions in mm.
    /// </summary>
    public static class SteelCatalogue
    {
        public const string ResourceName = "Alpaca4d.Resources.Section.section.json";

        private static readonly object Lock = new object();
        private static JObject _database;

        /// <summary>The database, read once.</summary>
        public static JObject Database
        {
            get
            {
                if (_database != null) return _database;
                lock (Lock)
                {
                    if (_database != null) return _database;
                    using (var stream = typeof(SteelCatalogue).Assembly.GetManifestResourceStream(ResourceName))
                    {
                        if (stream == null) throw new FileNotFoundException("Embedded resource not found", ResourceName);
                        using (var reader = new StreamReader(stream))
                            _database = JObject.Parse(reader.ReadToEnd());
                    }
                    return _database;
                }
            }
        }

        /// <summary>
        /// The family <paramref name="section"/> belongs to, made of the same material, smallest area
        /// first, with the section itself in its place; null when it is not one of the catalogue's. An
        /// I-section's family is its series, read off the front of its name - IPE, HEA, HEB or HEM - and
        /// a hollow section's or a pair of angles' is every one of its kind, the angles with the gap of
        /// the pair given. A section is known by its kind and its name, as Section Library names it.
        /// </summary>
        public static SectionFamily FamilyOf(IUniaxialSection section)
        {
            string group, name, label;
            double gap = 0.01;
            switch (section)
            {
                case ISection i: group = "I"; name = i.SectionName; label = null; break;
                case CircleCS o: group = "O"; name = o.SectionName; label = "CHS"; break;
                case RectangleHollowCS r: group = "[]"; name = r.SectionName; label = "RHS"; break;
                case DoubleLAngleCS l: group = "2L"; name = l.SectionName; label = "2L"; gap = l.Gap; break;
                default: return null;
            }

            if (string.IsNullOrWhiteSpace(name) || !(Database[group] is JObject entries))
                return null;

            var own = entries.Properties().FirstOrDefault(entry => entry.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (own == null)
                return null;

            if (group == "I")
                label = Series(own.Name);

            var members = entries.Properties()
                .Where(entry => group != "I" || Series(entry.Name).Equals(label, StringComparison.OrdinalIgnoreCase))
                .Select(entry => new
                {
                    entry.Name,
                    Area = PlateArea(group, entry.Value as JObject),
                    Section = entry == own ? section : Create(group, entry.Name, section.Material, gap),
                })
                .Where(member => member.Section != null)
                .OrderBy(member => member.Area)
                .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                .Select(member => member.Section)
                .ToList();

            return new SectionFamily { Name = label, Sections = members };
        }

        /// <summary>The letters a section's name starts with: IPE of IPE200, HEA of HEA300.</summary>
        public static string Series(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";
            int i = 0;
            while (i < name.Length && !char.IsDigit(name[i]))
                i++;
            return i > 0 ? name.Substring(0, i) : "Unknown";
        }

        /// <summary>Cross-sectional area of a database entry, in mm², from the plates it is built of.</summary>
        public static double PlateArea(string family, JObject entry)
        {
            if (entry == null) return double.MaxValue;

            if (family.Equals("O", StringComparison.OrdinalIgnoreCase))
            {
                double d = TryGetDouble(entry, "d"), t = TryGetDouble(entry, "t");
                double inner = d - 2.0 * t;
                return Math.PI / 4.0 * (d * d - inner * inner);
            }
            if (family.Equals("[]", StringComparison.Ordinal))
            {
                double h = TryGetDouble(entry, "h"), b = TryGetDouble(entry, "b"), t = TryGetDouble(entry, "t");
                return b * h - (b - 2.0 * t) * (h - 2.0 * t);
            }
            if (family.Equals("2L", StringComparison.OrdinalIgnoreCase))
            {
                double h = TryGetDouble(entry, "h"), b = TryGetDouble(entry, "b"), t = TryGetDouble(entry, "t");
                return 2.0 * (h * t + (b - t) * t);
            }

            double hi = TryGetDouble(entry, "h"), bi = TryGetDouble(entry, "b");
            double tw = TryGetDouble(entry, "tw"), tf = TryGetDouble(entry, "tf");
            return 2.0 * bi * tf + (hi - 2.0 * tf) * tw;
        }

        /// <summary>
        /// Creates the appropriate section type based on the family.
        /// Returns CircleCS for O family, RectangleHollowCS for [] family, 
        /// DoubleLAngleCS for 2L family, and ISection for I family.
        /// </summary>
        public static IUniaxialSection Create(string family, string sectionName, IUniaxialMaterial material, double gap)
        {
            JObject sectionEntry = FindEntry(family, sectionName);
            if (sectionEntry == null) return null;

            const double mmToM = 0.001;

            if (family.Equals("O", StringComparison.OrdinalIgnoreCase))
            {
                // Circular hollow section: d (diameter), t (thickness)
                var d = TryGetDouble(sectionEntry, "d");
                var t = TryGetDouble(sectionEntry, "t");

                if (double.IsNaN(d) || double.IsNaN(t))
                    return null;

                return new CircleCS(sectionName, d * mmToM, t * mmToM, material);
            }
            else if (family.Equals("[]", StringComparison.Ordinal))
            {
                // Rectangular hollow section: h, b, t
                var h = TryGetDouble(sectionEntry, "h");
                var b = TryGetDouble(sectionEntry, "b");
                var t = TryGetDouble(sectionEntry, "t");

                if (double.IsNaN(h) || double.IsNaN(b) || double.IsNaN(t))
                    return null;

                // RectangleHollowCS constructor: (secName, width, height, web, topFlange, bottomFlange, material)
                // For rectangular hollow sections with uniform thickness, all wall thicknesses are the same
                return new RectangleHollowCS(sectionName, b * mmToM, h * mmToM, t * mmToM, t * mmToM, t * mmToM, material);
            }
            else if (family.Equals("2L", StringComparison.OrdinalIgnoreCase))
            {
                // Double L-section (angle): h, b, t
                var h = TryGetDouble(sectionEntry, "h");
                var b = TryGetDouble(sectionEntry, "b");
                var t = TryGetDouble(sectionEntry, "t");

                if (double.IsNaN(h) || double.IsNaN(b) || double.IsNaN(t))
                    return null;

                // DoubleLAngleCS constructor: (secName, height, width, thickness, gap, material)
                return new DoubleLAngleCS(sectionName, h * mmToM, b * mmToM, t * mmToM, gap, material);
            }
            else
            {
                // I-section or similar: h, b, tw, tf
                var h = TryGetDouble(sectionEntry, "h");
                var b = TryGetDouble(sectionEntry, "b");
                var tw = TryGetDouble(sectionEntry, "tw");
                var tf = TryGetDouble(sectionEntry, "tf");

                if (double.IsNaN(h) || double.IsNaN(b) || double.IsNaN(tw) || double.IsNaN(tf))
                    return null;

                return new ISection(sectionName, h * mmToM, b * mmToM, tf * mmToM, b * mmToM, tf * mmToM, tw * mmToM, material);
            }
        }

        /// <summary>The database entry of a section, by family and name: its dimensions in mm. Null when there is none.</summary>
        public static JObject FindEntry(string family, string sectionName)
        {
            var db = Database;
            if (db == null || string.IsNullOrEmpty(sectionName)) return null;

            // First try schema with explicit families: db[family][sectionName]
            if (!string.IsNullOrEmpty(family))
            {
                var famToken = db[family] as JObject;
                if (famToken != null)
                {
                    var entry = famToken[sectionName] as JObject;
                    if (entry != null) return entry;
                }
            }

            // Fallback: if there's a single top-level group (e.g. "I"), search inside it.
            var singleGroup = db.Properties().FirstOrDefault()?.Value as JObject;
            var candidate = singleGroup?[sectionName] as JObject;
            if (candidate != null) return candidate;

            // As a last resort, search all families for a matching section name.
            foreach (var famProp in db.Properties())
            {
                if (famProp.Value is JObject famObj)
                {
                    var entry = famObj[sectionName] as JObject;
                    if (entry != null) return entry;
                }
            }

            return null;
        }

        private static double TryGetDouble(JObject obj, string key)
        {
            if (obj == null) return double.NaN;
            var token = obj[key];
            return token != null ? token.Value<double>() : double.NaN;
        }
    }
}
