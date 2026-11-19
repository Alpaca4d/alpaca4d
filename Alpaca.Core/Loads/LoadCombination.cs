using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Alpaca4d.Loads
{
    /// <summary>What a combination is checked for, as EN 1990 names them.</summary>
    public enum CombinationKind
    {
        ULS,
        SLSCharacteristic,
        SLSFrequent,
        SLSQuasiPermanent,
        Accidental,
        Seismic
    }

    /// <summary>
    /// A load combination: load patterns of a model, each by its name and times a factor - "ULS 1",
    /// 1.35 G + 1.5 Q - acting together.
    ///
    /// Combinations belong to the model (<see cref="Model.Combinations"/>): Load Combination adds them
    /// to it (<see cref="AddTo"/>), and Run Analysis solves each, an OpenSees run each, the model with
    /// only its patterns on, scaled (<see cref="Apply"/>). What it hands on is one model that holds
    /// every combination's results (<see cref="Model.CombinationResults"/>), each a model of its own
    /// that knows the combination it is (<see cref="Model.Combination"/>).
    ///
    /// A run each, not one for all: in one run, OpenSees numbers the MPCO file's model stage and steps
    /// on from the combination before - reset restarts neither - where a file of its own starts at
    /// stage 1 and step 0, as every result reader reads it. And a run that stops on an error stops for
    /// its combination alone.
    /// </summary>
    public sealed class LoadCombination
    {
        public string Name { get; set; } = "";

        public CombinationKind Kind { get; set; } = CombinationKind.ULS;

        /// <summary>The patterns, by name, and their factors, in the order written.</summary>
        public List<(string Pattern, double Factor)> Terms { get; set; } = new List<(string, double)>();

        /// <summary>The model whose patterns it combines, as assembled: the Report lists them from it. Set on the combination a result is of.</summary>
        public Model Source { get; private set; }

        /// <summary>The kinds as a report names them, and as Load Combination's Type takes them.</summary>
        public static readonly string[] KindNames =
        {
            "ULS", "SLS characteristic", "SLS frequent", "SLS quasi-permanent", "Accidental", "Seismic"
        };

        public static string NameOf(CombinationKind kind) => KindNames[(int)kind];

        /// <summary>Whether its members are checked against it: an ultimate limit state, accidental or seismic.</summary>
        public bool IsUltimate => Kind == CombinationKind.ULS || Kind == CombinationKind.Accidental || Kind == CombinationKind.Seismic;

        /// <summary>The combination as written: 1.35 G + 1.5 Q, and G + Q for factors of 1.</summary>
        public string Formula =>
            string.Join(" + ", Terms.Select(t => t.Factor == 1.0 ? t.Pattern : $"{t.Factor.ToString("0.###", CultureInfo.InvariantCulture)} {t.Pattern}"));

        public override string ToString() => $"{Name} ({NameOf(Kind)}): {Formula}";

        /// <summary>
        /// The type a text names, as <see cref="KindNames"/> has them, whatever its case and with or
        /// without its spaces and hyphens: "ULS", "SLS characteristic", "sls-quasi-permanent". Empty is
        /// ULS. False, and why, for anything else - "SLS" alone among them, which is three types.
        /// </summary>
        public static bool TryKind(string text, out CombinationKind kind, out string problem)
        {
            kind = CombinationKind.ULS;
            problem = null;
            string Plain(string s) => new string((s ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
            string typed = Plain(text);
            if (typed.Length == 0) return true;

            for (int i = 0; i < KindNames.Length; i++)
            {
                if (Plain(KindNames[i]) == typed || Plain(((CombinationKind)i).ToString()) == typed)
                {
                    kind = (CombinationKind)i;
                    return true;
                }
            }
            problem = typed == "sls"
                ? "\"SLS\" is three types: write SLS characteristic, SLS frequent or SLS quasi-permanent"
                : $"\"{text.Trim()}\" is not a type: write {string.Join(", ", KindNames.Take(KindNames.Length - 1))} or {KindNames.Last()}";
            return false;
        }

        /// <summary>
        /// A combination read from its formula: terms joined by "+", each a factor and a pattern's
        /// name - 1.35 G, 1.35*G, 1.35 × G, or G alone for 1 - and a negative factor as + -1.5 W, as a
        /// pattern's name may hold a minus: "Wind -X". Null, and why, when it cannot be read.
        /// </summary>
        public static LoadCombination Parse(string name, string formula, CombinationKind kind, out string problem)
        {
            problem = null;
            var combination = new LoadCombination { Name = (name ?? "").Trim(), Kind = kind };
            var term = new Regex(@"^([+-]?\d+(?:[.,]\d+)?(?:[eE][+-]?\d+)?)(?:\s*[*×·]\s*|\s+)(.+)$");

            foreach (var part in (formula ?? "").Split('+').Select(p => p.Trim()))
            {
                if (part.Length == 0) continue;

                double factor = 1.0;
                string pattern = part;
                var match = term.Match(part);
                if (match.Success)
                {
                    factor = double.Parse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
                    pattern = match.Groups[2].Value.Trim();
                }
                if (double.TryParse(pattern.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    problem = $"\"{part}\" has a factor but no load pattern: write each term as a factor and a pattern's name, 1.5 Q";
                    return null;
                }

                int same = combination.Terms.FindIndex(t => MassFromLoads.Names(t.Pattern, pattern));
                if (same >= 0) combination.Terms[same] = (combination.Terms[same].Pattern, combination.Terms[same].Factor + factor);
                else combination.Terms.Add((pattern, factor));
            }

            if (combination.Terms.Count == 0)
            {
                problem = "the combination names no load pattern: write it as 1.35 G + 1.5 Q, with the names given to Load Pattern";
                return null;
            }
            if (combination.Name.Length == 0) combination.Name = combination.Formula;
            return combination;
        }

        /// <summary>The model's pattern a term names, or null and why: none, two, or a ground motion.</summary>
        private static LoadPattern PatternOf(Model model, string name, out string problem)
        {
            problem = null;
            var named = model.LoadPatterns.Where(p => MassFromLoads.Names(p.Name, name)).ToList();
            if (named.Count == 0)
            {
                var names = model.LoadPatterns.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.Name.Trim()).ToList();
                problem = names.Count == 0
                    ? $"the model has no load pattern named \"{name}\": its patterns have no names - give each one in Load Pattern's Name input"
                    : $"the model has no load pattern named \"{name}\": it has {string.Join(", ", names)}";
                return null;
            }
            if (named.Count > 1) { problem = $"{named.Count} load patterns are named \"{name}\": give each a name of its own"; return null; }
            if (named[0].PatternType != PatternType.Plain) { problem = $"\"{name}\" is a ground motion, not a pattern of loads to combine"; return null; }
            return named[0];
        }

        /// <summary>
        /// The assembled <paramref name="model"/> with <paramref name="combinations"/> added to the ones
        /// it has: each checked against its patterns, and written as the model names them - "G" for a
        /// "g" typed. A copy; the model given is left as it is. Null, and why - led by the combination's
        /// name - when the model is not assembled or already analysed, when a combination names a
        /// pattern the model has not, or two, or when two combinations share a name.
        /// </summary>
        public static Model AddTo(Model model, IEnumerable<LoadCombination> combinations, out string problem)
        {
            problem = null;
            if (model == null) { problem = "no AlpacaModel was given"; return null; }
            if (model.IsAnalysed) { problem = "the model is analysed already: add the combinations to the model Assemble Model hands on, then analyse it"; return null; }
            if (model.LoadPatternTclStart < 0) { problem = "the model is not assembled: give the model Assemble Model hands on"; return null; }

            var all = new List<LoadCombination>(model.Combinations ?? new List<LoadCombination>());
            foreach (var combination in combinations)
            {
                var terms = new List<(string, double)>();
                foreach (var (name, factor) in combination.Terms)
                {
                    var pattern = PatternOf(model, name, out string why);
                    if (pattern == null) { problem = $"{combination.Name}: {why}"; return null; }
                    terms.Add((pattern.Name.Trim(), factor));
                }

                var added = new LoadCombination { Kind = combination.Kind, Terms = terms };
                // Named as typed; one named by its formula is named by the formula as the model writes it.
                added.Name = combination.Name == combination.Formula ? added.Formula : combination.Name;
                if (all.Any(c => string.Equals(c.Name, added.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    problem = $"{added.Name} is a combination of the model already: give each its own name";
                    return null;
                }
                all.Add(added);
            }

            var with = model.ShallowCopy();
            with.Combinations = all;
            return with;
        }

        /// <summary>
        /// The assembled <paramref name="model"/> with this combination's loads on it and no others:
        /// the patterns it names, each scaled by its factor on top of its own. Its other loads and its
        /// mass stay as they are; so do its combinations. Its patterns are copies, their time series
        /// left as they are. Null, and why, when the model is not assembled, or already analysed, or has no pattern of a
        /// name the combination gives - or two.
        /// </summary>
        public Model Apply(Model model, out string problem)
        {
            problem = null;
            if (model == null) { problem = "no AlpacaModel was given"; return null; }
            if (model.IsAnalysed) { problem = "the model is analysed already: give the model Assemble Model hands on"; return null; }
            if (model.LoadPatternTclStart < 0) { problem = "the model is not assembled: give the model Assemble Model hands on"; return null; }

            var patterns = new List<LoadPattern>();
            var terms = new List<(string, double)>();
            foreach (var (name, factor) in Terms)
            {
                var pattern = PatternOf(model, name, out problem);
                if (pattern == null) return null;
                // Its own time series, not changed, and its factor times the combination's.
                patterns.Add(new LoadPattern
                {
                    Id = pattern.Id, PatternType = pattern.PatternType, TimeSeries = pattern.TimeSeries,
                    Load = pattern.Load, Factor = pattern.Factor * factor, Name = pattern.Name,
                    Duration = pattern.Duration
                });
                // Written as the pattern is named: "g" typed for "G".
                terms.Add((pattern.Name.Trim(), factor));
            }

            var combined = model.ShallowCopy();
            combined.Tcl = model.Tcl.Take(model.LoadPatternTclStart).ToList();
            combined.LoadPatterns = patterns;
            combined.addLoadPattern(patterns);

            var applied = new LoadCombination { Kind = Kind, Terms = terms, Source = model };
            applied.Name = Name == Formula ? applied.Formula : Name;
            combined.Combination = applied;
            return combined;
        }
    }
}
