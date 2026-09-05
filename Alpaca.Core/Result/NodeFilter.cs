using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

namespace Alpaca4d.Result
{
    /// <summary>
    /// Which nodes a nodal result component should report on.
    ///
    /// Much simpler than <see cref="ElementFilter"/>, and deliberately so: a node has no free-text
    /// identifier to match. Nobody authors a node - they fall out of the element geometry, and
    /// Assemble numbers the unique points it finds. No glob, no patterns.
    ///
    /// Two ways in, because the two components have different things to offer. Nodal Displacements
    /// covers every node in the model, most of which the user never placed, so it filters by
    /// <see cref="Select"/> on the tag. Reaction Forces covers the supports, which the user did
    /// place and can point at, so it filters by <see cref="SelectByPoint"/> on where they are.
    /// </summary>
    public static class NodeFilter
    {
        /// <summary>
        /// The positions in <paramref name="availableTags"/> to report, in the order the model
        /// holds them, and through <paramref name="missing"/> the requested tags that are not
        /// there at all.
        ///
        /// An empty request is not an empty answer - it is the default, and means every node.
        ///
        /// The order is the model's rather than the order the tags were typed in, so every output
        /// of a component stays in step with every other and with the model itself. A tag asked
        /// for twice is reported once.
        ///
        /// Missing tags are worth handing back rather than ignoring. On Reaction Forces especially,
        /// a tag that is not a support would otherwise drop out silently and read as a support
        /// carrying nothing, which is a very different statement from "there is no support there".
        /// </summary>
        public static List<int> Select(IEnumerable<int> requested, IReadOnlyList<int?> availableTags, out List<int> missing)
        {
            missing = new List<int>();
            var kept = new List<int>();

            if (availableTags == null)
                return kept;

            var wanted = new HashSet<int>((requested ?? Enumerable.Empty<int>()).Where(tag => tag > 0));

            if (wanted.Count == 0)
            {
                kept.AddRange(Enumerable.Range(0, availableTags.Count));
                return kept;
            }

            for (int i = 0; i < availableTags.Count; i++)
            {
                if (availableTags[i].HasValue && wanted.Contains(availableTags[i].Value))
                    kept.Add(i);
            }

            var found = new HashSet<int>(kept.Select(i => availableTags[i].Value));
            missing.AddRange(wanted.Where(tag => !found.Contains(tag)).OrderBy(tag => tag));

            return kept;
        }

        /// <summary>
        /// The positions in <paramref name="available"/> whose point is one of those asked for,
        /// and through <paramref name="missing"/> the points that are nowhere near one.
        ///
        /// For picking supports out by where they are rather than by number. Supports are the one
        /// thing in a model the user placed themselves and can point at on screen, so a point is a
        /// handle they already have; a node tag is one they would have to go and look up.
        ///
        /// An empty request is not an empty answer - it is the default, and means every one of
        /// them.
        ///
        /// <paramref name="tolerance"/> should be the model's own: it is the distance below which
        /// Assemble treated two points as the same node in the first place, so it is exactly the
        /// distance at which the user's point and the support's are the same place. A point that
        /// lands on several coincident supports reports all of them, and a support asked for twice
        /// is reported once.
        /// </summary>
        public static List<int> SelectByPoint(IEnumerable<Point3d> requested, IReadOnlyList<Point3d> available,
                                              double tolerance, out List<Point3d> missing)
        {
            missing = new List<Point3d>();
            var kept = new List<int>();

            if (available == null)
                return kept;

            var wanted = (requested ?? Enumerable.Empty<Point3d>()).Where(point => point.IsValid).ToList();

            if (wanted.Count == 0)
            {
                kept.AddRange(Enumerable.Range(0, available.Count));
                return kept;
            }

            // A tolerance of zero would make this an exact-equality test on floating point, which
            // no point picked off the screen would ever pass. Fall back to the same default the
            // Assemble component offers.
            if (!(tolerance > 0.0))
                tolerance = 0.01;

            var matched = new HashSet<int>();

            foreach (var point in wanted)
            {
                bool found = false;

                for (int i = 0; i < available.Count; i++)
                {
                    if (available[i].DistanceTo(point) > tolerance)
                        continue;

                    matched.Add(i);
                    found = true;
                }

                if (!found)
                    missing.Add(point);
            }

            // Walked in the model's order rather than the order the points were given, so every
            // output of a component stays in step with every other and with the model itself.
            kept.AddRange(Enumerable.Range(0, available.Count).Where(matched.Contains));

            return kept;
        }

        /// <summary>A short list of what is actually there, so the user can see what to ask for.</summary>
        public static string Preview(IEnumerable<int?> tags)
        {
            var present = (tags ?? Enumerable.Empty<int?>())
                          .Where(tag => tag.HasValue)
                          .Select(tag => tag.Value)
                          .OrderBy(tag => tag)
                          .ToList();
            const int shown = 12;

            return present.Count <= shown
                ? string.Join(", ", present)
                : string.Join(", ", present.Take(shown)) + $", ... ({present.Count} in all)";
        }
    }
}
