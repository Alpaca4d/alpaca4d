using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Loads
{
    /// <summary>
    /// The load-duration classes of EN 1995-1-1 (2.3.1.2, Table 2.1), shortest last: how long a
    /// pattern's loads act, which sets timber's strength through k_mod (Table 3.1). Self-weight is
    /// permanent; storage long-term; imposed floor load medium-term; snow short-term or medium-term,
    /// as the National Annex says; wind and accidental loads instantaneous.
    /// </summary>
    public enum LoadDuration
    {
        Permanent,
        LongTerm,
        MediumTerm,
        ShortTerm,
        Instantaneous
    }

    public static class LoadDurations
    {
        /// <summary>The classes as EN 1995-1-1 names them, in the order of <see cref="LoadDuration"/>.</summary>
        public static readonly string[] Names = { "Permanent", "Long-term", "Medium-term", "Short-term", "Instantaneous" };

        public static string NameOf(LoadDuration duration) => Names[(int)duration];

        /// <summary>The class a text names, whatever its case, spaces and hyphens: "Medium-term", "medium term", "MediumTerm".</summary>
        public static bool TryParse(string text, out LoadDuration duration)
        {
            string Plain(string s) => new string((s ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
            string typed = Plain(text);
            for (int i = 0; i < Names.Length; i++)
            {
                if (Plain(Names[i]) == typed)
                {
                    duration = (LoadDuration)i;
                    return true;
                }
            }
            duration = LoadDuration.Permanent;
            return false;
        }

        /// <summary>
        /// The class of the loads a model carries, for k_mod: the shortest among the patterns that act
        /// on it, as EN 1995-1-1 3.1.3(2) asks of a combination of loads of several classes. A model
        /// read for one of its load combinations carries only that combination's patterns; one
        /// without combinations carries all its patterns at their own factors. A pattern scaled to
        /// nothing does not count.
        ///
        /// A pattern whose duration was not given counts as permanent, the class with the lowest
        /// k_mod: <paramref name="unset"/> names them, for the check to say so. Permanent too when
        /// nothing acts at all.
        /// </summary>
        public static LoadDuration Governing(Model model, out List<string> unset)
        {
            unset = new List<string>();
            var shortest = LoadDuration.Permanent;
            if (model?.LoadPatterns == null)
                return shortest;

            foreach (var pattern in model.LoadPatterns)
            {
                if (pattern == null || pattern.Factor == 0.0)
                    continue;

                if (pattern.Duration.HasValue)
                {
                    if (pattern.Duration.Value > shortest)
                        shortest = pattern.Duration.Value;
                }
                else
                {
                    unset.Add(string.IsNullOrWhiteSpace(pattern.Name) ? $"pattern {pattern.Id}" : pattern.Name.Trim());
                }
            }

            return shortest;
        }
    }
}
