using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alpaca4d
{
    /// <summary>What Settings ▸ Display gives a colour of its own.</summary>
    public enum DisplayItem
    {
        NodeIds,
        ElementIds,
        SectionNames,
        PointLoads,
        LineLoads,
        AreaLoads,
        Values
    }

    /// <summary>
    /// Alpaca4d's settings, kept in settings.json beside the plugin: where OpenSees is, and the
    /// colours Model View and View Results draw their text and loads in.
    ///
    /// The file used to hold the OpenSees path alone, as a JSON string. It is still read that way,
    /// and is written as an object from the first save on.
    /// </summary>
    public static class AlpacaSettings
    {
        private static readonly string SettingsFilePath =
            System.IO.Path.Combine(Application.GhAlpacaFolder, "settings.json");

        private static string _openSeesPath;
        private static bool _loaded;

        /// <summary>The colours everything is drawn in until the user picks others - the ones it always had.</summary>
        private static readonly Dictionary<DisplayItem, Color> Defaults = new Dictionary<DisplayItem, Color>
        {
            { DisplayItem.NodeIds, Color.White },
            { DisplayItem.ElementIds, Color.Yellow },
            { DisplayItem.SectionNames, Color.Cyan },
            { DisplayItem.PointLoads, Color.IndianRed },
            { DisplayItem.LineLoads, Color.DarkSeaGreen },
            { DisplayItem.AreaLoads, Color.OrangeRed },
            { DisplayItem.Values, Color.Black },
        };

        private static readonly Dictionary<DisplayItem, Color> _colours = new Dictionary<DisplayItem, Color>(Defaults);

        /// <summary>What settings.json holds.</summary>
        private class Stored
        {
            public string OpenSeesPath;

            /// <summary>By <see cref="DisplayItem"/> name, as #RRGGBB.</summary>
            public Dictionary<string, string> Colours;
        }

        public static string OpenSeesPath
        {
            get
            {
                if (!_loaded)
                    Load();
                return _openSeesPath;
            }
            set
            {
                if (!_loaded)
                    Load();
                _openSeesPath = value;
                Save();
            }
        }

        /// <summary>The colour <paramref name="item"/> is drawn in.</summary>
        public static Color Colour(DisplayItem item)
        {
            if (!_loaded)
                Load();
            return _colours.TryGetValue(item, out var colour) ? colour : Defaults[item];
        }

        public static Color DefaultColour(DisplayItem item) => Defaults[item];

        public static void SetColour(DisplayItem item, Color colour)
        {
            if (!_loaded)
                Load();
            _colours[item] = colour;
            Save();
        }

        public static void ResetColours()
        {
            if (!_loaded)
                Load();
            foreach (var entry in Defaults)
                _colours[entry.Key] = entry.Value;
            Save();
        }

        public static void Load()
        {
            _loaded = true;

            // From the defaults, so whatever the file leaves out or gets wrong is the default and
            // not what happened to be held before.
            _openSeesPath = null;
            foreach (var entry in Defaults)
                _colours[entry.Key] = entry.Value;

            try
            {
                if (!File.Exists(SettingsFilePath))
                    return;

                var token = JToken.Parse(File.ReadAllText(SettingsFilePath));

                // The path on its own: a file written before there was anything else to keep.
                if (token.Type == JTokenType.String)
                {
                    _openSeesPath = token.Value<string>();
                    return;
                }

                var stored = token.ToObject<Stored>();
                if (stored == null)
                    return;

                _openSeesPath = stored.OpenSeesPath;

                if (stored.Colours != null)
                {
                    foreach (var entry in stored.Colours)
                    {
                        // One bad entry - a name from a newer version, a mistyped colour - is
                        // left at its default rather than losing the rest.
                        if (!Enum.TryParse(entry.Key, out DisplayItem item))
                            continue;
                        try
                        {
                            _colours[item] = ColorTranslator.FromHtml(entry.Value);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
                // Best-effort load
            }
        }

        public static void Save()
        {
            try
            {
                var stored = new Stored
                {
                    OpenSeesPath = _openSeesPath,
                    Colours = new Dictionary<string, string>()
                };

                foreach (var entry in _colours)
                    stored.Colours[entry.Key.ToString()] = $"#{entry.Value.R:X2}{entry.Value.G:X2}{entry.Value.B:X2}";

                File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(stored, Formatting.Indented));
            }
            catch
            {
                // Best-effort save
            }
        }
    }
}
