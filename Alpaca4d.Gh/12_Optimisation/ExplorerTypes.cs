using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel.Types;

namespace Alpaca4d.Gh
{
    /// <summary>What Explorer Image hands to Explorer Record: which views to take, and how big.</summary>
    public sealed class ExplorerImageSettings
    {
        public const int DefaultWidth = 800;
        public const int DefaultHeight = 600;

        /// <summary>Viewport or named view names. Empty means the active viewport.</summary>
        public IReadOnlyList<string> Views { get; }
        public int Width { get; }
        public int Height { get; }
        public bool Transparent { get; }

        public ExplorerImageSettings(IEnumerable<string> views, int width, int height, bool transparent)
        {
            Views = (views ?? Enumerable.Empty<string>())
                .Where(view => !string.IsNullOrWhiteSpace(view))
                .Select(view => view.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            Width = width;
            Height = height;
            Transparent = transparent;
        }

        /// <summary>What Explorer Record takes when no Explorer Image is connected: the active viewport.</summary>
        public static ExplorerImageSettings Default => new ExplorerImageSettings(null, DefaultWidth, DefaultHeight, false);

        /// <summary>The name of each image a design gets; one name per image column.</summary>
        public IList<string> ImageNames => Views.Count == 0 ? new List<string> { "Active" } : Views.ToList();

        public override string ToString()
        {
            string views = Views.Count == 0 ? "active viewport" : string.Join(", ", Views);
            return $"Explorer image: {views}, {Width} × {Height}{(Transparent ? ", transparent" : "")}";
        }
    }

    /// <summary>What Explorer Subset hands to Explorer Inputs: how much of the design space to run.</summary>
    public sealed class ExplorerSubsetSettings
    {
        public IReadOnlyList<int> Divisions { get; }
        public IReadOnlyList<KeyValuePair<double, double>> Ranges { get; }

        public ExplorerSubsetSettings(IEnumerable<int> divisions, IEnumerable<KeyValuePair<double, double>> ranges)
        {
            Divisions = (divisions ?? Enumerable.Empty<int>()).ToList();
            Ranges = (ranges ?? Enumerable.Empty<KeyValuePair<double, double>>()).ToList();
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Divisions.Count > 0) parts.Add("divisions " + string.Join(", ", Divisions));
            if (Ranges.Count > 0) parts.Add("designs " + string.Join(", ", Ranges.Select(r => $"{r.Key:0} to {r.Value:0}")));
            return "Explorer subset: " + (parts.Count > 0 ? string.Join("; ", parts) : "everything");
        }
    }

    internal static class ExplorerGoo
    {
        /// <summary>
        /// The object inside what arrived at a generic input. Grasshopper wraps anything it does not
        /// know in a GH_ObjectWrapper, and hands some things over already unwrapped.
        /// </summary>
        public static T As<T>(object data) where T : class
        {
            if (data is T value) return value;
            if (data is GH_ObjectWrapper wrapper) return wrapper.Value as T;
            if (data is IGH_Goo goo) return goo.ScriptVariable() as T;
            return null;
        }
    }
}
