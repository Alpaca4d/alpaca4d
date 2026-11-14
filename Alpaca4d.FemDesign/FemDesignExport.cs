using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Alpaca4d.Interop;
using StruSoft.Interop.StruXml.Data;

namespace Alpaca4d.FemDesign
{
    /// <summary>
    /// Exports an assembled Alpaca model to FEM-Design: <see cref="ExportModelReader"/> reads it,
    /// <see cref="StruxmlWriter"/> builds the FEM-Design model, and <see cref="FemDesignExportResult.Save"/>
    /// writes it to a .struxml file FEM-Design opens.
    /// </summary>
    public static class FemDesignExport
    {
        /// <summary>
        /// The national annexes the export offers: those whose material library FEM-Design's StruXml
        /// library carries. The key is what a user types, FEM-Design's code or the country's name.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Eurocodetype> Countries = new Dictionary<string, Eurocodetype>(StringComparer.OrdinalIgnoreCase)
        {
            ["common"] = Eurocodetype.Common, ["EC"] = Eurocodetype.Common, ["Eurocode"] = Eurocodetype.Common,
            ["S"] = Eurocodetype.S, ["Sweden"] = Eurocodetype.S,
            ["N"] = Eurocodetype.N, ["Norway"] = Eurocodetype.N,
            ["DK"] = Eurocodetype.DK, ["Denmark"] = Eurocodetype.DK,
            ["FIN"] = Eurocodetype.FIN, ["Finland"] = Eurocodetype.FIN,
            ["H"] = Eurocodetype.H, ["Hungary"] = Eurocodetype.H,
            ["LV"] = Eurocodetype.LV, ["Latvia"] = Eurocodetype.LV,
        };

        /// <summary>The codes <see cref="Countries"/> takes, for a message.</summary>
        public static string CountryCodes => "common, S, N, DK, FIN, H or LV";

        /// <summary>Whether <paramref name="text"/> names a country the export offers; empty means common.</summary>
        public static bool IsCountry(string text) => TryParseCountry(text, out _);

        public static bool TryParseCountry(string text, out Eurocodetype country)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                country = Eurocodetype.Common;
                return true;
            }
            return Countries.TryGetValue(text.Trim(), out country);
        }

        /// <summary>
        /// Exports <paramref name="model"/> with materials from the library of <paramref name="country"/>, one
        /// of <see cref="Countries"/>; empty for common. It takes the country as text so that a caller needs
        /// none of FEM-Design's types.
        /// </summary>
        public static FemDesignExportResult Export(Model model, string country = null)
        {
            if (!TryParseCountry(country, out var code))
                throw new ArgumentException($"The country is one of {CountryCodes}, not {country}.", nameof(country));

            var report = new ConversionReport();
            var exportModel = ExportModelReader.Read(model, report);
            return StruxmlWriter.Write(exportModel, code, report);
        }
    }

    public sealed class FemDesignExportResult
    {
        internal FemDesignExportResult(Database database, ConversionReport report, IReadOnlyList<string> loadCases)
        {
            Database = database;
            Report = report;
            LoadCases = loadCases;
        }

        /// <summary>The FEM-Design model.</summary>
        public Database Database { get; }

        /// <summary>What could not be exported, what was approximated, and what is worth knowing.</summary>
        public ConversionReport Report { get; }

        /// <summary>The load cases, in the order of the load patterns, then the self-weight case if there is one.</summary>
        public IReadOnlyList<string> LoadCases { get; }

        /// <summary>
        /// Writes the model to <paramref name="path"/>, given .struxml if it has no extension, and returns
        /// the path written. The model is first checked against FEM-Design's schema, and anything that
        /// fails the check is added to the report as an error: FEM-Design would not open it as it is.
        /// </summary>
        public string Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("No file to save to.", nameof(path));

            path = path.Trim();
            if (string.IsNullOrEmpty(Path.GetExtension(path)))
                path += ".struxml";
            if (!string.Equals(Path.GetExtension(path), ".struxml", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"FEM-Design reads .struxml files, not {Path.GetExtension(path)} ones.", nameof(path));

            if (!Database.ValidateXmlAgainstSchema(out var errors))
            {
                foreach (var error in errors.Take(10))
                    Report.Error($"The file does not follow FEM-Design's schema, so FEM-Design may not open it: {error}");
                if (errors.Length > 10)
                    Report.Error($"... and {errors.Length - 10} more schema errors.");
            }

            Database.Serialize(path);
            return Path.GetFullPath(path);
        }
    }
}
