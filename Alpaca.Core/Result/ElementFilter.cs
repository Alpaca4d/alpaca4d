using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Alpaca4d.Generic;

namespace Alpaca4d.Result
{
    /// <summary>
    /// Which elements a result component should report on, out of the ones its recorder wrote.
    ///
    /// A model of any size records far more than anyone reads at once, so every element result
    /// component takes one list of terms. Empty is the default and means every element - the
    /// filter is opt-in, and a definition that never touches it behaves as it always did.
    ///
    /// One input rather than two because an element has two handles and the user should not have
    /// to sort out which is which before typing: the ElementId they gave the element component,
    /// which is free text and need not be unique, and the tag Assemble hands out, which is the
    /// unique number OpenSees knows the element by. A term that reads as a plain whole number is
    /// tried as both; anything else is a pattern. The terms are OR-ed, so
    ///
    ///     MyBeam*   Col_3   47
    ///
    /// reports every element labelled MyBeam-something, the one labelled Col_3, and element 47.
    /// </summary>
    public class ElementFilter
    {
        /// <summary>
        /// Marks a term as a raw .NET regular expression rather than a glob. Case-insensitive,
        /// so "Regex:" reads as well as "regex:".
        /// </summary>
        public const string RegexPrefix = "regex:";

        /// <summary>
        /// One thing the user typed, and both ways it can land.
        ///
        /// A term is never only a tag. "12" is tried against the tags and against the ElementIds,
        /// because nothing stops someone labelling an element "12" and there is no way to tell
        /// from the term alone which they meant. Trying both matches more rather than guessing
        /// wrong, and a model that does not use numeric ElementIds - almost all of them - cannot
        /// tell the difference.
        /// </summary>
        private class Term
        {
            public string Text;
            public int? Tag;
            public Regex Pattern;

            public bool Matches(IElement element, string elementId)
            {
                if (Tag.HasValue && element.Id == Tag.Value)
                    return true;

                return elementId != null && Pattern.IsMatch(elementId);
            }
        }

        private readonly List<Term> _terms;

        private ElementFilter(List<Term> terms)
        {
            _terms = terms;
        }

        /// <summary>
        /// True when nothing was asked for and every element passes. Callers can use it to skip
        /// the work of filtering, but they do not have to - <see cref="Matches"/> answers true
        /// for everything in that case.
        /// </summary>
        public bool MatchesEverything => _terms.Count == 0;

        /// <summary>
        /// Builds a filter from the raw input. Nulls and blank strings are dropped rather than
        /// rejected: an unplugged Grasshopper input arrives as an empty list, and a panel with a
        /// trailing newline arrives as a blank string.
        ///
        /// A term that will not compile is reported through <paramref name="badTerms"/> and left
        /// out, so one typo in a list does not cost the user the other terms.
        /// </summary>
        public static ElementFilter Create(IEnumerable<string> terms, out List<string> badTerms)
        {
            badTerms = new List<string>();
            var compiled = new List<Term>();

            foreach (var raw in (terms ?? Enumerable.Empty<string>()))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var text = raw.Trim();
                try
                {
                    compiled.Add(new Term { Text = text, Tag = AsTag(text), Pattern = Compile(text) });
                }
                catch (ArgumentException ex)
                {
                    badTerms.Add($"\"{text}\" is not a valid pattern: {ex.Message}");
                }
            }

            return new ElementFilter(compiled);
        }

        /// <summary>
        /// The tag a term asks for, or null when it does not read as one.
        ///
        /// Only the canonical spelling of a positive whole number counts. "007" and "+7" parse as
        /// 7 but were far more likely typed as an ElementId than as a tag, and reading them as
        /// element 7 would be a surprise; they stay patterns. So does "0", since Assemble numbers
        /// elements from 1.
        /// </summary>
        private static int? AsTag(string text)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int tag))
                return null;

            if (tag <= 0 || tag.ToString(CultureInfo.InvariantCulture) != text)
                return null;

            return tag;
        }

        /// <summary>
        /// Turns one term into a regex to try against the ElementIds.
        ///
        /// A bare term is a glob, because that is what "MyBeam*" means to everyone who has ever
        /// used a file dialog: "*" stands for any run of characters and "?" for one. As a regular
        /// expression "MyBeam*" would instead mean "MyBea" followed by any number of "m"s - it
        /// would match "MyBea" and miss "MyBeam_1" - so reading it as a regex would quietly do the
        /// opposite of what was asked. Everything else is escaped, which is what keeps an
        /// identifier holding "." or "+" matchable at all.
        ///
        /// A "regex:" prefix hands the rest through untouched, for the cases a glob cannot
        /// express - "regex:^(Col|Beam)_[0-9]+$" and the like.
        ///
        /// Either way the match is anchored and case-insensitive: "mybeam*" finds MyBeam_0, and
        /// "MyBeam" alone matches only the elements actually labelled MyBeam.
        /// </summary>
        private static Regex Compile(string term)
        {
            const RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

            if (term.StartsWith(RegexPrefix, StringComparison.OrdinalIgnoreCase))
                return new Regex(term.Substring(RegexPrefix.Length), options);

            return new Regex("^" + GlobToRegex(term) + "$", options);
        }

        private static string GlobToRegex(string glob)
        {
            var builder = new StringBuilder();

            foreach (char c in glob)
            {
                if (c == '*')
                    builder.Append(".*");
                else if (c == '?')
                    builder.Append('.');
                else
                    builder.Append(Regex.Escape(c.ToString()));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Whether this element should be reported. An element with no ElementId is reachable by
        /// tag only; it can never match a pattern, not even "*".
        /// </summary>
        public bool Matches(IElement element)
        {
            if (element == null)
                return false;
            if (MatchesEverything)
                return true;

            var elementId = Trimmed(element);

            return _terms.Any(term => term.Matches(element, elementId));
        }

        /// <summary>The element's ElementId ready to match against, or null when it has none.</summary>
        private static string Trimmed(IElement element)
        {
            return string.IsNullOrWhiteSpace(element.ElementId) ? null : element.ElementId.Trim();
        }

        /// <summary>
        /// The positions in <paramref name="elements"/> that pass, in the order given.
        ///
        /// Positions rather than the elements themselves because that is what the callers need:
        /// a result component holds one row of recorder data per element in this same order, and
        /// slices every one of its outputs by the same index list.
        /// </summary>
        public List<int> SelectIndices<T>(IReadOnlyList<T> elements) where T : IElement
        {
            var kept = new List<int>();
            if (elements == null)
                return kept;

            for (int i = 0; i < elements.Count; i++)
            {
                if (Matches(elements[i]))
                    kept.Add(i);
            }

            return kept;
        }

        /// <summary>
        /// What the user asked for that is not in the model, one message per term, for a component
        /// to report as warnings.
        ///
        /// Worth saying out loud because the failure is otherwise silent: a filter that matches
        /// nothing produces empty outputs, which looks exactly like an analysis that produced
        /// nothing. Checked against every element in the model rather than against the ones a
        /// single component reads, so asking Beam Forces for a shell's ElementId is not reported
        /// as a missing one - it is reported by that component as a type mismatch instead.
        /// </summary>
        public List<string> Unmatched(IEnumerable<IElement> allElements)
        {
            var problems = new List<string>();
            var elements = (allElements ?? Enumerable.Empty<IElement>()).ToList();

            foreach (var term in _terms)
            {
                if (elements.Any(element => term.Matches(element, Trimmed(element))))
                    continue;

                problems.Add(Explain(term, elements));
            }

            return problems;
        }

        /// <summary>
        /// Why one term found nothing, said in terms of what the term could have meant - a term
        /// that reads as a number missed both the tags and the ElementIds, and has to say so or
        /// the user is left wondering which half was wrong.
        /// </summary>
        private static string Explain(Term term, List<IElement> elements)
        {
            var labels = elements.Select(Trimmed).Where(label => label != null).ToList();

            if (term.Tag.HasValue)
            {
                return $"\"{term.Text}\" matched nothing: no element has the tag {term.Tag.Value} " +
                       $"- the model has {elements.Count} - and none is labelled \"{term.Text}\" either.";
            }

            return labels.Count == 0
                ? $"\"{term.Text}\" matched nothing - no element in this model has an ElementId. Type one into the ElementId input of the element components, or give an element's tag instead."
                : $"\"{term.Text}\" matched none of the {labels.Count} elements that have an ElementId. ElementIds in this model: {Preview(labels)}.";
        }

        /// <summary>A few of the real ones, so the user can see the spelling they are missing.</summary>
        private static string Preview(List<string> labels)
        {
            var distinct = labels.Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                                 .ToList();
            const int shown = 8;

            return distinct.Count <= shown
                ? string.Join(", ", distinct)
                : string.Join(", ", distinct.Take(shown)) + $", ... ({distinct.Count} in all)";
        }
    }
}
