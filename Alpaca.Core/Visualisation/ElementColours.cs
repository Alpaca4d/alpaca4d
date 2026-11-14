using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Alpaca4d.Generic;

namespace Alpaca4d.Visualisation
{
    /// <summary>What Model View colours the elements by. Saved by position in a dropdown, so append only.</summary>
    public enum ColourBy
    {
        /// <summary>The colour each element component gave its elements.</summary>
        Element,

        /// <summary>One colour per section. A solid has none, and goes by its material.</summary>
        Section,

        /// <summary>One colour per material; for a layered shell, its first layer's.</summary>
        Material,
    }

    /// <summary>
    /// The colour each element of a model is drawn in: its own, its section's or its material's.
    ///
    /// By section or material, the elements that share one share a colour - the one the user gave
    /// it in the Section or Material Library, or else one picked from <see cref="Colors.Categorical"/>,
    /// a different one for each section or material in the order the model meets them.
    ///
    /// Sharing goes by name rather than by object. Two Section Library components both set to
    /// IPE200 make two objects, and two tags in OpenSees, but one section to the eye. Something with
    /// no name goes by what it is, the Tcl it writes with its own tag left out, so the elastic steel
    /// every Section Library falls back on is one material however many components fell back on it.
    /// </summary>
    public class ElementColours
    {
        private readonly ColourBy _by;

        // By section or material object. Nothing here overrides Equals, so these are by reference.
        private readonly Dictionary<object, Color> _colours = new Dictionary<object, Color>();

        public ElementColours(Model model, ColourBy by)
        {
            _by = by;
            if (by == ColourBy.Element || model == null) return;

            var sources = model.Beams.Select(SourceOf)
                         .Concat(model.Shells.Select(SourceOf))
                         .Concat(model.Bricks.Select(SourceOf))
                         .Where(source => source != null);

            // Each section or material once, under the key that says which others it is the same as,
            // and the first colour the user gave any of those.
            var keyOf = new Dictionary<object, string>();
            var keys = new List<string>();
            var seen = new HashSet<string>();
            var given = new Dictionary<string, Color>();
            foreach (var source in sources)
            {
                if (keyOf.ContainsKey(source)) continue;

                string key = Key(source);
                keyOf[source] = key;
                if (seen.Add(key)) keys.Add(key);

                var colour = (source as ISection)?.Color ?? (source as IMaterial)?.Color;
                if (colour.HasValue && !given.ContainsKey(key)) given[key] = colour.Value;
            }

            var byKey = new Dictionary<string, Color>(given);
            int next = 0;
            foreach (var key in keys)
            {
                if (!byKey.ContainsKey(key))
                    byKey[key] = Colors.Categorical(next++);
            }

            foreach (var pair in keyOf)
                _colours[pair.Key] = byKey[pair.Value];
        }

        public Color Of(IBeam beam) => Lookup(SourceOf(beam), beam.Color);

        public Color Of(IShell shell) => Lookup(SourceOf(shell), shell.Color);

        public Color Of(IBrick brick) => Lookup(SourceOf(brick), brick.Color);

        private Color Lookup(object source, Color own)
        {
            return source != null && _colours.TryGetValue(source, out var colour) ? colour : own;
        }

        private object SourceOf(IBeam beam)
        {
            if (_by == ColourBy.Section) return beam.Section;
            if (_by == ColourBy.Material) return beam.Section?.Material;
            return null;
        }

        private object SourceOf(IShell shell)
        {
            if (_by == ColourBy.Section) return shell.Section;
            if (_by == ColourBy.Material) return shell.Section?.Material;
            return null;
        }

        private object SourceOf(IBrick brick)
        {
            return _by == ColourBy.Element ? null : brick.Material;
        }

        /// <summary>
        /// Which sections or materials <paramref name="source"/> is the same as: those of its name,
        /// or with no name, those that write the same Tcl. Sections and materials are kept apart,
        /// since a solid stands in its material's colour among the sections.
        /// </summary>
        private static string Key(object source)
        {
            if (source is ISection section)
                return "section " + (Named(section.SectionName) ?? Definition(section, section.Id));

            var material = (IMaterial)source;
            return "material " + (Named(material.MatName) ?? Definition(material, material.Id));
        }

        private static string Named(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? null : "name " + name.Trim();
        }

        private static string Definition(ISerialize source, int? id)
        {
            try
            {
                // "section Elastic 7 ...", "uniaxialMaterial Elastic 3 ...": the third word is the tag.
                var words = source.WriteTcl().Split((char[])null, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (words.Count > 2 && id.HasValue && words[2] == id.Value.ToString())
                    words.RemoveAt(2);
                return "tcl " + string.Join(" ", words);
            }
            catch (Exception)
            {
                // One that cannot write itself is the same only as itself.
                return "object " + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(source) + " " + id;
            }
        }
    }
}
