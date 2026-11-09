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
        Values,
        Gravity,
        Masses,
        /// <summary>The elements' edges, drawn over their colours: black, or softer for a model of
        /// thousands of thin members, which black edges would turn black.</summary>
        Edges
    }

    /// <summary>
    /// Alpaca4d's settings, kept in settings.json in <see cref="Application.UserFolder"/>: where
    /// OpenSees is, and the colours Model View and View Results draw their text and loads in.
    ///
    /// The file used to sit beside the plugin, and was left behind by every update. Where there is
    /// none in the user folder yet, the newest one an earlier version kept is brought over.
    ///
    /// The file used to hold the OpenSees path alone, as a JSON string. It is still read that way,
    /// and is written as an object from the first save on.
    /// </summary>
    public static class AlpacaSettings
    {
        private static readonly string SettingsFilePath =
            System.IO.Path.Combine(Application.UserFolder, "settings.json");

        private static string _openSeesPath;
        private static bool _loaded;

        /// <summary>
        /// The colours everything is drawn in until the user picks others: dark, so that they read
        /// on Rhino's light grey viewport and on the white plate Model View draws behind every label.
        /// </summary>
        private static readonly Dictionary<DisplayItem, Color> Defaults = new Dictionary<DisplayItem, Color>
        {
            { DisplayItem.NodeIds, Color.FromArgb(42, 45, 49) },
            { DisplayItem.ElementIds, Color.FromArgb(31, 58, 147) },
            { DisplayItem.SectionNames, Color.FromArgb(106, 27, 154) },
            { DisplayItem.PointLoads, Color.FromArgb(178, 34, 34) },
            { DisplayItem.LineLoads, Color.FromArgb(46, 125, 50) },
            { DisplayItem.AreaLoads, Color.FromArgb(192, 74, 0) },
            { DisplayItem.Values, Color.Black },
            { DisplayItem.Gravity, Color.FromArgb(69, 90, 100) },
            { DisplayItem.Masses, Color.FromArgb(109, 76, 65) },
            { DisplayItem.Edges, Color.Black },
        };

        /// <summary>
        /// The defaults until 0.12: white, yellow and cyan text that read on a dark background only,
        /// and could hardly be seen on Rhino's light grey one, and pale loads.
        /// </summary>
        private static readonly Dictionary<DisplayItem, Color> FirstDefaults = new Dictionary<DisplayItem, Color>
        {
            { DisplayItem.NodeIds, Color.White },
            { DisplayItem.ElementIds, Color.Yellow },
            { DisplayItem.SectionNames, Color.Cyan },
            { DisplayItem.PointLoads, Color.IndianRed },
            { DisplayItem.LineLoads, Color.DarkSeaGreen },
            { DisplayItem.AreaLoads, Color.OrangeRed },
        };

        /// <summary>
        /// What a settings.json holds since the colours left at their defaults are no longer written
        /// into it. Every file before held every colour, picked or not.
        /// </summary>
        private const int FileVersion = 2;

        private static readonly Dictionary<DisplayItem, Color> _colours = new Dictionary<DisplayItem, Color>(Defaults);

        /// <summary>What settings.json holds.</summary>
        private class Stored
        {
            public string OpenSeesPath;

            /// <summary><see cref="FileVersion"/>; missing from the files written before it.</summary>
            public int Version;

            /// <summary>By <see cref="DisplayItem"/> name, as #RRGGBB: the colours not left at their defaults.</summary>
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
                Application.AdoptFromPluginFolders("settings.json", SettingsFilePath);
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
                            var colour = ColorTranslator.FromHtml(entry.Value);
                            if (stored.Version < FileVersion && WasFirstDefault(item, colour))
                                continue;
                            _colours[item] = colour;
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

        /// <summary>
        /// Whether <paramref name="colour"/>, read from a file written before <see cref="FileVersion"/>,
        /// is the default <paramref name="item"/> had then: those files held every colour, so a
        /// default in one was most likely never picked. Within a few steps of it: a file from a Mac
        /// held #FFFEFF for white and #FF5D00 for OrangeRed, likely the colour picker handing them
        /// back converted between colour spaces.
        /// </summary>
        private static bool WasFirstDefault(DisplayItem item, Color colour)
        {
            if (!FirstDefaults.TryGetValue(item, out var first))
                return false;
            const int steps = 32;
            return Math.Abs(colour.R - first.R) <= steps && Math.Abs(colour.G - first.G) <= steps && Math.Abs(colour.B - first.B) <= steps;
        }

        public static void Save()
        {
            try
            {
                var stored = new Stored
                {
                    OpenSeesPath = _openSeesPath,
                    Version = FileVersion,
                    Colours = new Dictionary<string, string>()
                };

                // Only the colours picked, so that new defaults reach everyone who has not.
                foreach (var entry in _colours)
                    if (entry.Value.ToArgb() != Defaults[entry.Key].ToArgb())
                        stored.Colours[entry.Key.ToString()] = $"#{entry.Value.R:X2}{entry.Value.G:X2}{entry.Value.B:X2}";

                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));
                File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(stored, Formatting.Indented));
            }
            catch
            {
                // Best-effort save
            }
        }
    }
}
