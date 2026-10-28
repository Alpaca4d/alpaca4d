using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Interop;
using StruSoft.Interop.StruXml.Data;

namespace Alpaca4d.FemDesign
{
    /// <summary>
    /// Builds a FEM-Design model - a StruXml <see cref="Database"/> - out of an <see cref="ExportModel"/>.
    /// Every decision about how an Alpaca object becomes a FEM-Design one is made here, and every
    /// approximation is reported:
    /// <list type="bullet">
    /// <item>A material whose name or grade is in FEM-Design's library (S355, C30_37, C24...) becomes that
    /// library material, so FEM-Design can design with it; any other becomes a custom material, which
    /// FEM-Design analyses but does not design.</item>
    /// <item>A catalogue profile becomes FEM-Design's profile of that size; any other shape becomes a
    /// section drawn from its outline. A section known only by A, I and J has no outline, and its beam
    /// becomes a fictitious bar of the same stiffness.</item>
    /// <item>A beam with hinges released in bending at both ends and loaded only at its ends becomes a
    /// truss; a vertical one a column; anything else a beam, with its end releases.</item>
    /// <item>Coplanar shell elements of one section that share edges become one plate or wall.</item>
    /// <item>Each load pattern becomes a load case and gravity a dead-load case, and one combination adds
    /// them with the patterns' factors - what Alpaca analyses.</item>
    /// </list>
    /// </summary>
    public sealed partial class StruxmlWriter
    {
        /// <summary>The stiffness FEM-Design's own rigid point supports and connections use [kN/m, kNm/rad].</summary>
        private const double Rigid = 1e10;

        private readonly ExportModel model;
        private readonly ConversionReport report;
        private readonly Database database;
        private readonly IReadOnlyList<Material_type> library;

        /// <summary>The GUIDs already in <see cref="database"/>, so nothing is added twice.</summary>
        private readonly HashSet<string> added = new HashSet<string>();
        private readonly Dictionary<string, int> numbering = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> loadCaseNames = new List<string>();

        private StruxmlWriter(ExportModel model, Eurocodetype country, ConversionReport report)
        {
            this.model = model;
            this.report = report;
            this.database = new Database(country);
            this.library = MaterialDatabase.For(country);
            this.materials = new Material_type[model.Materials.Count];
            this.families = new MaterialFamily[model.Materials.Count];
        }

        /// <summary>The FEM-Design model of <paramref name="model"/>, with its materials from <paramref name="country"/>'s library.</summary>
        public static FemDesignExportResult Write(ExportModel model, Eurocodetype country, ConversionReport report)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (report == null) throw new ArgumentNullException(nameof(report));

            var writer = new StruxmlWriter(model, country, report);
            writer.WriteBars();
            writer.WriteShells();
            writer.WriteSupports();
            writer.WriteLinks();
            writer.WriteConstraints();
            writer.WriteLoads();
            writer.WriteMasses();
            writer.ReportCounts();
            return new FemDesignExportResult(writer.database, report, writer.loadCaseNames);
        }

        private void Add(object entity, string guid)
        {
            if (added.Add(guid))
                database.Add(entity);
        }

        // ---------------------------------------------------------------- names

        /// <summary>
        /// The next name of a kind: "B.1", "B.2"..., the numbering FEM-Design gives its own elements. The
        /// user's label replaces the prefix when there is one, so elements keep it in FEM-Design.
        /// </summary>
        private string NextName(string prefix, string label = null)
        {
            string stem = Identifier(label) ?? prefix;
            numbering.TryGetValue(stem, out int count);
            numbering[stem] = ++count;
            return $"{stem}.{count}";
        }

        /// <summary>
        /// A label as the stem of a FEM-Design identifier: at most 50 characters, none of $ &amp; &lt; &gt; @,
        /// and no dots, which FEM-Design reads as part numbers.
        /// </summary>
        private static string Identifier(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return null;
            var name = Clean(label.Trim(), "$&<>@.");
            return name.Length > 50 ? name.Substring(0, 50) : name;
        }

        /// <summary>A name with the characters FEM-Design's names never hold replaced, and those in <paramref name="also"/>.</summary>
        private static string Clean(string name, string also = "$&<>@")
        {
            var chars = name.Select(c => c < ' ' || also.IndexOf(c) >= 0 || c == '￾' || c == '￿' ? '_' : c);
            return new string(chars.ToArray());
        }

        private static string Mm(double metres) => (metres * 1000.0).ToString("0.##", CultureInfo.InvariantCulture);

        private static string Describe(int id, string label) => string.IsNullOrWhiteSpace(label) ? $"element {id}" : label;

        // ---------------------------------------------------------------- geometry

        private static Point_type_3d P(Point3d p) => new Point_type_3d { X = p.X, Y = p.Y, Z = p.Z };

        private static Point_type_3d V(Vector3d v) => new Point_type_3d { X = v.X, Y = v.Y, Z = v.Z };

        /// <summary>
        /// <paramref name="v"/> at unit length, Zero when it has none. RhinoCommon's Unitize runs in Rhino's
        /// native code, which the writer does without, so that it also runs outside Rhino.
        /// </summary>
        private static Vector3d Unit(Vector3d v)
        {
            double length = v.Length;
            return length > 1e-12 ? v / length : Vector3d.Zero;
        }

        /// <summary><paramref name="v"/> less its part along unit <paramref name="axis"/>, of unit length; Zero when nothing is left.</summary>
        private static Vector3d SquareTo(Vector3d v, Vector3d axis) => Unit(v - axis * (v * axis));

        // ---------------------------------------------------------------- materials

        private enum MaterialFamily { Steel, Concrete, Timber, Custom }

        private readonly Material_type[] materials;
        private readonly MaterialFamily[] families;
        private readonly HashSet<string> customNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The FEM-Design material of an Alpaca one, added to the model the first time it is asked for.</summary>
        private Material_type MaterialOf(int index, out MaterialFamily family)
        {
            if (materials[index] == null)
            {
                var material = model.Materials[index];
                var match = LibraryMaterial(material);
                if (match != null)
                {
                    materials[index] = match;
                    families[index] = match.Steel != null ? MaterialFamily.Steel
                                    : match.Concrete != null ? MaterialFamily.Concrete
                                    : match.Timber != null ? MaterialFamily.Timber
                                    : MaterialFamily.Custom;
                    CompareWithLibrary(material, match);
                }
                else
                {
                    materials[index] = CustomMaterial(material, index);
                    families[index] = MaterialFamily.Custom;
                }
                Add(materials[index], materials[index].Guid);
            }

            family = families[index];
            return materials[index];
        }

        private Material_type LibraryMaterial(ExportMaterial material)
        {
            if (material.IsOrthotropic)
                return null;

            // The grade first: it is what the steel design reads. Alpaca's names, such as C30_37, differ from
            // FEM-Design's, C30/37, only in punctuation, which the lookup ignores.
            foreach (var name in new[] { material.Grade, material.Name }.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
            {
                try
                {
                    return library.GetMaterialByName(name);
                }
                catch (KeyNotFoundException)
                {
                }
                catch (InvalidOperationException)
                {
                    report.Warning($"More than one material in FEM-Design's library is called {name}; it is exported as a custom material instead.", name);
                    return null;
                }
            }
            return null;
        }

        /// <summary>FEM-Design analyses a library material with its own values, so say when they are not Alpaca's.</summary>
        private void CompareWithLibrary(ExportMaterial material, Material_type match)
        {
            double? libraryE = match.Steel?.Ek ?? match.Concrete?.Ecm ?? match.Timber?.E0mean; // N/mm²
            double? libraryRho = match.Steel != null ? match.Steel.Mass * 1000.0
                               : match.Concrete != null ? match.Concrete.Mass * 1000.0
                               : match.Timber?.Rhomean; // kg/m³

            double alpacaE = material.E / 1000.0;
            if (libraryE is double e && alpacaE > 0 && Math.Abs(e - alpacaE) > 0.01 * alpacaE)
                report.Warning($"FEM-Design's {match.Name} has E = {e:0.#} N/mm², which is not Alpaca's {alpacaE:0.#} N/mm²; FEM-Design analyses it with its own.", match.Name);

            if (libraryRho is double rho && material.Rho > 0 && Math.Abs(rho - material.Rho) > 0.02 * material.Rho)
                report.Remark($"FEM-Design weighs {match.Name} at {rho:0.#} kg/m³ rather than Alpaca's {material.Rho:0.#} kg/m³, which changes its self-weight.", match.Name);
        }

        private Material_type CustomMaterial(ExportMaterial material, int index)
        {
            string stem = Clean(!string.IsNullOrWhiteSpace(material.Name) ? material.Name.Trim()
                              : !string.IsNullOrWhiteSpace(material.Grade) ? material.Grade.Trim()
                              : $"Alpaca material {index + 1}");
            if (stem.Length > 240) stem = stem.Substring(0, 240);
            string name = stem;
            for (int n = 2; !customNames.Add(name); n++)
                name = $"{stem} ({n})";

            Material_typeCustom custom;
            if (material.IsOrthotropic)
            {
                custom = new Material_typeCustom
                {
                    E_0 = material.Ex, E_1 = material.Ey, E_2 = material.Ez,
                    Nu_0 = material.NuXy, Nu_1 = material.NuYz, Nu_2 = material.NuZx,
                    G_0 = material.Gxy, G_1 = material.Gyz, G_2 = material.Gzx,
                };
                report.Warning("An orthotropic material is exported with Ex, Ey and Ez as FEM-Design's E 0, 1 and 2; check its directions in FEM-Design.", name);
            }
            else
            {
                double g = material.G > 0 ? material.G : material.E / (2.0 * (1.0 + material.Nu));
                // The same values for all three entries is right whether FEM-Design reads them as directions
                // or as anything else.
                custom = new Material_typeCustom
                {
                    E_0 = material.E, E_1 = material.E, E_2 = material.E,
                    Nu_0 = material.Nu, Nu_1 = material.Nu, Nu_2 = material.Nu,
                    G_0 = g, G_1 = g, G_2 = g,
                };
            }
            custom.Mass = Math.Max(material.Rho, 0.0) / 1000.0; // t/m³
            custom.Alfa_0 = custom.Alfa_1 = custom.Alfa_2 = 1.0e-5;

            report.Remark("Not a grade in FEM-Design's material library, so exported as a custom material, which FEM-Design analyses but does not design. " +
                          "A grade from Material Library (Alpaca4d) is exported as FEM-Design's own.", name);

            return new Material_type
            {
                Guid = Guid.NewGuid().ToString(),
                Last_change = DateTime.Now,
                Action = Modification_type.Added,
                Hash_order_id = "-1",
                Name = name,
                Standard = Standardtype.General,
                Country = Eurocodetype.Na,
                Custom = custom,
            };
        }
    }
}
