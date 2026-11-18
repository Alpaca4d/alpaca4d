using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Alpaca4d.Loads;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// How a topology optimisation weighs the compliances of several load cases - a model's load
    /// combinations - against each other.
    /// </summary>
    public enum LoadCaseObjective
    {
        /// <summary>
        /// Their sum: the design stiffest on average over the cases. Every method can, as the 99 and 88
        /// line codes do (Sigmund 2001; Andreassen et al. 2011, section 5.2).
        /// </summary>
        Sum,

        /// <summary>
        /// The largest: the design whose worst case is stiffest. Only an optimiser with MMA can, through
        /// its bound formulation (Svanberg 2007): minimise z with every case's compliance at most z.
        /// </summary>
        Worst,
    }

    /// <summary>
    /// The load cases of a topology optimisation: the model's load combinations, each solved on its own
    /// in the one OpenSees the optimisation keeps open.
    ///
    /// The deck carries every pattern a combination names, each with a Path time series in place of its
    /// own: at time k the series holds the pattern's factor in combination k. So the analysis of case k
    /// goes from rest at time k - 1 to time k (<see cref="TopologyAnalysis"/>), with exactly that
    /// combination's loads on. Each factor sits on a plateau, from k - 0.5 to k, so the series reads it
    /// at time k as it is, without interpolating.
    ///
    /// A combination is solved as Run Analysis solves it: its patterns, each scaled by its factor on top of
    /// its own (<see cref="LoadCombination.Apply"/>). A pattern no combination names is left out, as is a
    /// ground motion.
    /// </summary>
    public static class TopologyLoadCases
    {
        // A pattern as LoadPattern.WriteTcl writes it: its series, then the pattern on it.
        private static readonly Regex Pattern = new Regex(
            @"^timeSeries (?<type>Linear|Constant) (?<series>\d+)(?<factor>(?: -factor \S+)?)[ \t]*\n" +
            @"(?<head>pattern Plain \d+ )(?<on>\d+)(?<rest>(?: -fact \S+)? \{\n[\s\S]*)$");

        /// <summary>Whether the model's load cases are its combinations: when it has any.</summary>
        public static bool FromCombinations(Model model) => model.Combinations != null && model.Combinations.Count > 0;

        /// <summary>How many load cases the model is optimised for: one per combination, or one, its loads as they are.</summary>
        public static int Count(Model model) => FromCombinations(model) ? model.Combinations.Count : 1;

        /// <summary>
        /// The model's deck, <paramref name="tcl"/> as <see cref="TopologyDeck.From"/> writes it, with its
        /// patterns written for the model's combinations, case k combination k.
        /// </summary>
        public static List<string> Write(Model model, IReadOnlyList<string> tcl)
        {
            if (model.LoadPatternTclStart < 0)
                throw new ArgumentException("The model is not assembled: give the model Assemble Model hands on.");
            return Write(tcl, model.LoadPatternTclStart, model.LoadPatterns.Select(p => p.Name).ToList(), model.Combinations);
        }

        /// <summary>
        /// <paramref name="tcl"/> with its patterns - the items from <paramref name="patternStart"/> on, one a
        /// pattern, named in order by <paramref name="patternNames"/> - written for <paramref name="combinations"/>.
        /// </summary>
        public static List<string> Write(IReadOnlyList<string> tcl, int patternStart, IReadOnlyList<string> patternNames,
                                         IReadOnlyList<LoadCombination> combinations)
        {
            if (combinations == null || combinations.Count == 0)
                throw new ArgumentException("There are no load combinations to write.");
            if (tcl.Count - patternStart != patternNames.Count)
                throw new ArgumentException($"The deck has {tcl.Count - patternStart} patterns for the model's {patternNames.Count}. Assemble the model again.");

            var deck = tcl.Take(patternStart).ToList();
            for (int p = 0; p < patternNames.Count; p++)
            {
                var factors = combinations.Select(c => FactorOf(c, patternNames[p])).ToArray();
                if (factors.All(f => f == 0.0))
                    continue;

                var match = Pattern.Match(tcl[patternStart + p]);
                if (!match.Success)
                    throw new ArgumentException(
                        $"Load pattern {patternNames[p]} is not a plain pattern on a Linear or Constant time series, which is what topology " +
                        "optimisation takes in a load combination.");
                if (match.Groups["series"].Value != match.Groups["on"].Value)
                    throw new ArgumentException($"Load pattern {patternNames[p]} is not on the time series written with it. Assemble the model again.");

                deck.Add($"timeSeries Path {match.Groups["series"].Value} -time {{{Times(factors.Length)}}} -values {{{Values(factors)}}}{match.Groups["factor"].Value}\n" +
                         match.Groups["head"].Value + match.Groups["on"].Value + match.Groups["rest"].Value);
            }
            return deck;
        }

        /// <summary>The factor <paramref name="combination"/> gives the pattern named <paramref name="pattern"/>: 0 when it names it not.</summary>
        public static double FactorOf(LoadCombination combination, string pattern) =>
            combination.Terms.Where(t => MassFromLoads.Names(pattern, t.Pattern)).Sum(t => t.Factor);

        /// <summary>The names of the patterns no combination names: they are not in the optimisation.</summary>
        public static List<string> Unused(Model model) =>
            model.LoadPatterns
                .Where(p => p.PatternType == PatternType.Plain && model.Combinations.All(c => FactorOf(c, p.Name) == 0.0))
                .Select(p => string.IsNullOrWhiteSpace(p.Name) ? $"#{model.LoadPatterns.IndexOf(p) + 1}" : p.Name.Trim())
                .ToList();

        // 0, then a plateau per case: k - 0.5 and k.
        private static string Times(int cases) =>
            string.Join(" ", new[] { "0" }.Concat(Enumerable.Range(1, cases).SelectMany(k => new[] { N(k - 0.5), N(k) })));

        private static string Values(double[] factors) =>
            string.Join(" ", new[] { "0" }.Concat(factors.SelectMany(f => new[] { N(f), N(f) })));

        private static string N(double x) => x.ToString("R", CultureInfo.InvariantCulture);
    }
}
