using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using Alpaca4d.Generic;
using Alpaca4d.Result;
using Alpaca4d.Section;

namespace Alpaca4d.Design
{
    /// <summary>A design group as it lands on one model: the beams it holds and its sections, in the order given.</summary>
    public sealed class SizingGroup
    {
        public DesignGroup Design;

        /// <summary>The group's beams, as positions in Model.Beams.</summary>
        public List<int> Beams = new List<int>();

        /// <summary>The group's sections in the order the user gave them, each once: the order they are tried in.</summary>
        public List<IUniaxialSection> Sections = new List<IUniaxialSection>();

        /// <summary>The group's name, or its place in the list it came in, from 1, when it has none.</summary>
        public string Label = "";

        /// <summary>
        /// Every beam of the group has the same section, as a design group's do. A family's beams, in
        /// no design group, each have their own.
        /// </summary>
        public bool Uniform;
    }

    /// <summary>What the optimiser is asked for, besides the model and the groups.</summary>
    public sealed class SizingOptions
    {
        /// <summary>The utilisation every grouped beam should end up at or below.</summary>
        public double Target = 1.0;

        /// <summary>How many analyses at most.</summary>
        public int MaxRounds = 10;

        /// <summary>The recorded steps to check, the worst of them governing; null or empty for the default (see <see cref="SectionSizing.StepsOf"/>).</summary>
        public IReadOnlyList<int> Steps;

        public DesignSettings Design = new DesignSettings { Report = false };

        /// <summary>One per beam, in the order of Model.Beams: its length and how it is held. They stay with the beam whatever its section.</summary>
        public IReadOnlyList<SteelMember> Members;

        /// <summary>
        /// Rule a candidate out on <see cref="SteelCheck.LowerBound"/> before its whole check, when the bound
        /// alone is above the target. It never changes what is chosen, only how many checks it takes.
        /// </summary>
        public bool Screen = true;

        /// <summary>Check the beams on several threads at once. It never changes what is chosen.</summary>
        public bool Parallel = true;
    }

    /// <summary>One analysis of the optimiser and what came of it.</summary>
    /// <summary>
    /// One iteration of a run: what the design analysed weighed and how far it was from working, what
    /// the iteration changed, and what it took. Section Optimiser's History output, read as a line of
    /// text in a Panel and field by field with Deconstruct Anything.
    /// </summary>
    public sealed class SizingRound
    {
        /// <summary>Which iteration, from 1.</summary>
        public int Iteration;

        /// <summary>The mass of the design analysed in this round, as Assemble Model counts it.</summary>
        public double Mass;

        /// <summary>The largest utilisation of any beam in the design analysed, or null when none could be checked.</summary>
        public double? MaxUtilisation;

        /// <summary>The utilisation the run sizes beams to: SizingOptions.Target, the optimiser's MaxUtilisation.</summary>
        public double Target;

        /// <summary>Grouped beams above the target, or that could not be checked, in the design analysed.</summary>
        public int Failing;

        /// <summary>How many beams this round moved to another section, for the next.</summary>
        public int Changed;

        /// <summary>Beams could only move on down their group's list in this round, because a design had come round again.</summary>
        public bool OnwardOnly;

        /// <summary>Building the model and solving it, in this round.</summary>
        public double AnalysisSeconds;

        /// <summary>Reading the forces back, checking and choosing, in this round.</summary>
        public double CheckSeconds;

        /// <summary>Whole EN 1993-1-1 checks made, one per beam and section.</summary>
        public int Checks;

        /// <summary>Sections ruled out on their lower bound alone, without a whole check.</summary>
        public int Screened;

        /// <summary>The iteration as one line, for a Panel.</summary>
        public override string ToString()
        {
            string max = this.MaxUtilisation.HasValue ? this.MaxUtilisation.Value.ToString("0.###") : "-";
            return $"Iteration {this.Iteration}: mass {this.Mass:0.###} {Units.Mass}, largest utilisation {max}, " +
                   $"{this.Failing} beam(s) sized above {this.Target:0.###}, {this.Changed} section(s) changed" +
                   (this.OnwardOnly ? ", onward only" : "") +
                   $". Analysis {this.AnalysisSeconds:0.00} s, checks {this.CheckSeconds:0.00} s " +
                   $"({this.Checks} checked, {this.Screened} ruled out on a quick bound)";
        }
    }

    public sealed class SizingResult
    {
        /// <summary>The last design, analysed.</summary>
        public Model Model;

        /// <summary>Each beam's section in it, in the order of Model.Beams.</summary>
        public List<IUniaxialSection> Sections = new List<IUniaxialSection>();

        /// <summary>Each beam's utilisation in it, the worst of the steps checked; null where it could not be checked.</summary>
        public List<double?> Utilisation = new List<double?>();

        /// <summary>The check that gives each beam's utilisation, or why there is none.</summary>
        public List<string> Governing = new List<string>();

        /// <summary>Each beam's group, or -1 for a beam in none.</summary>
        public List<int> Group = new List<int>();

        public List<SizingRound> Rounds = new List<SizingRound>();

        /// <summary>The last round changed nothing: every grouped beam has the first section of its group's list that works for it.</summary>
        public bool Converged;

        /// <summary>A design came round again, so the rounds after it only let beams move on down their group's list.</summary>
        public bool Oscillated;

        /// <summary>The steps checked in the last round.</summary>
        public List<int> Steps = new List<int>();
    }

    /// <summary>
    /// Sizes beams from lists of sections: analyse, check every grouped beam with every section of
    /// its group under the forces it carried, put it on the first of the group's list that keeps it at
    /// or below the target, and analyse again, until no section changes.
    ///
    /// The list is tried in the order the user gave it, never sorted: the user says which section
    /// comes first - the lightest, the cheapest, the one in stock - and a beam takes the first that
    /// works. Analysing again matters. A section changes the beam's weight, and in a statically
    /// indeterminate structure its stiffness moves forces between beams, so a beam sized on the
    /// forces of one round may not hold on those of the next. A beam can move back up the list as
    /// well as down it, which can send a design round in a circle: when one comes back, beams may
    /// only move on down their lists from then on, which ends.
    /// </summary>
    public static class SectionSizing
    {
        /// <summary>The sections in the order given, each once - where it first comes - and nulls left out.</summary>
        public static List<IUniaxialSection> InOrder(IEnumerable<IUniaxialSection> sections)
        {
            var distinct = new List<IUniaxialSection>();
            foreach (var section in sections ?? Enumerable.Empty<IUniaxialSection>())
                if (section != null && !distinct.Any(other => ReferenceEquals(other, section)))
                    distinct.Add(section);
            return distinct;
        }

        /// <summary>
        /// The groups as they land on <paramref name="beams"/>, holding only the beams that are to be
        /// optimised. A group that matches no beam, or has no section, is a warning and left out; a beam
        /// in two groups is an error.
        /// </summary>
        /// <param name="optimise">
        /// Which beams take part, as the result filters take them; null, or a filter of nothing, for every
        /// beam. A beam it leaves out keeps its section, whatever group it is in, and one it picks that
        /// has no sections to go to is a warning.
        /// </param>
        /// <param name="remarks">Where to say that a group takes no part, all its beams being left out; null for nowhere.</param>
        /// <param name="family">
        /// For a beam that takes part and is in no design group, the family of the section it starts
        /// with, to size it from - <see cref="SteelCatalogue.FamilyOf"/>; null when it has none, and the
        /// beam keeps its section. Each beam has its own section of the family. Null for no families.
        /// </param>
        public static List<SizingGroup> Resolve(IReadOnlyList<IBeam> beams, IReadOnlyList<DesignGroup> groups,
                                                List<string> errors, List<string> warnings,
                                                ElementFilter optimise = null, List<string> remarks = null,
                                                Func<IUniaxialSection, SectionFamily> family = null)
        {
            bool everyBeam = optimise == null || optimise.MatchesEverything;
            var picked = beams.Select(beam => everyBeam || optimise.Matches(beam)).ToArray();
            var resolved = new List<SizingGroup>();
            var owner = new int[beams.Count];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
            var shared = new List<int>();

            groups = groups ?? new List<DesignGroup>();
            for (int g = 0; g < groups.Count; g++)
            {
                var design = groups[g];
                if (design == null)
                    continue;

                string label = string.IsNullOrWhiteSpace(design.Name) ? $"Design group {g + 1}" : $"Design group \"{design.Name.Trim()}\"";

                var filter = ElementFilter.Create(design.ElementIds, out var badTerms);
                foreach (var problem in badTerms)
                    warnings.Add($"{label}: {problem}");

                if (filter.MatchesEverything)
                {
                    errors.Add($"{label} names no beams. Give it the ElementIds of the beams it holds.");
                    continue;
                }

                var sections = InOrder(design.Sections);
                if (sections.Count == 0)
                {
                    warnings.Add($"{label} has no sections, so its beams keep the ones they have.");
                    continue;
                }

                var group = new SizingGroup
                {
                    Design = design,
                    Label = string.IsNullOrWhiteSpace(design.Name) ? $"Group {g + 1}" : design.Name.Trim(),
                    Sections = sections,
                    Uniform = true,
                };
                int matched = 0;
                for (int i = 0; i < beams.Count; i++)
                {
                    if (!filter.Matches(beams[i]))
                        continue;

                    matched++;
                    if (!picked[i])
                        continue;

                    if (owner[i] >= 0)
                    {
                        shared.Add(i);
                        continue;
                    }

                    owner[i] = resolved.Count;
                    group.Beams.Add(i);
                }

                if (matched == 0)
                {
                    warnings.Add($"{label} matches no beam in the model.");
                    continue;
                }

                if (group.Beams.Count == 0)
                {
                    remarks?.Add($"{label} holds none of the beams to optimise, so it takes no part.");
                    continue;
                }

                resolved.Add(group);
            }

            // The beams in no group go to the family of the section they start with: one group per
            // section they start with, of which each beam has its own.
            var homeless = new List<int>();
            var families = new Dictionary<IUniaxialSection, SizingGroup>();
            for (int i = 0; i < beams.Count; i++)
            {
                if (!picked[i] || owner[i] >= 0)
                    continue;

                var start = beams[i].Section;
                SizingGroup group = null;
                if (family != null && start != null && !families.TryGetValue(start, out group))
                {
                    var members = family(start);
                    group = members == null || members.Sections.Count == 0 ? null : new SizingGroup
                    {
                        Design = new DesignGroup { Name = $"{members.Name} family", Sections = members.Sections },
                        Label = $"{members.Name} family",
                        Sections = InOrder(members.Sections),
                    };
                    families[start] = group;
                    if (group != null)
                        resolved.Add(group);
                }

                if (group == null)
                {
                    homeless.Add(i);
                    continue;
                }

                owner[i] = resolved.IndexOf(group);
                group.Beams.Add(i);
            }

            if (homeless.Count > 0 && (!everyBeam || family != null))
            {
                var tags = homeless.Select(i => beams[i].Id?.ToString() ?? "?").ToList();
                string message = $"{tags.Count} beam(s) {(everyBeam ? "are" : "picked to optimise are")} in no design group" +
                                 (family != null ? ", and their section is not one of Section Library's, so there is no family to size them from" : "") +
                                 $"; they keep the sections they have (element {string.Join(", ", tags.Take(10))}{(tags.Count > 10 ? ", ..." : "")}).";
                if (everyBeam)
                    remarks?.Add(message);
                else
                    warnings.Add(message);
            }

            if (shared.Count > 0)
            {
                var tags = shared.Distinct().Select(i => beams[i].Id?.ToString() ?? "?").ToList();
                errors.Add($"{tags.Count} beam(s) are in more than one design group (element {string.Join(", ", tags.Take(10))}{(tags.Count > 10 ? ", ..." : "")}). " +
                           "A beam can only take its sections from one list.");
            }

            return resolved;
        }

        /// <summary>
        /// The section each beam of a group goes to next, as positions in its sections: the first whose
        /// utilisation is at most <paramref name="target"/>, or, when none is, the one with the lowest -
        /// the nearest the list comes. Uniform, the group takes one section, judged by its worst beam.
        /// -1 for a beam none of whose sections could be checked, which keeps the section it has.
        /// </summary>
        /// <param name="utilisation">[b][c]: the group's beam b with its section c, null where it could not be checked.</param>
        /// <param name="floor">Per beam, the first position it may go to, so that it can only move on down the list; null for no floor.</param>
        public static int[] Choose(SizingGroup group, double?[][] utilisation, double target, int[] floor = null)
        {
            return Choose(group, utilisation.Length, (b, c) => utilisation[b][c], target, floor);
        }

        /// <summary>
        /// As above, with each utilisation asked for as it is needed: a section after the first that
        /// passes is never asked about, which on a family of two hundred sections is most of the work.
        /// </summary>
        /// <param name="beams">How many beams the group has.</param>
        /// <param name="ruledOut">
        /// [b, c]: true when beam b surely fails with section c, which then needs no utilisation to be
        /// passed over (<see cref="SteelCheck.LowerBound"/>); null to rule nothing out. Only the search for
        /// the first that passes uses it: when none passes, the nearest is found from the utilisations.
        /// </param>
        /// <param name="parallel">Walk the beams' lists on several threads; a uniform group has one walk.</param>
        public static int[] Choose(SizingGroup group, int beams, Func<int, int, double?> utilisation, double target, int[] floor = null,
                                   Func<int, int, bool> ruledOut = null, bool parallel = false)
        {
            int sections = group.Sections.Count;
            var chosen = new int[beams];

            if (group.Uniform)
            {
                int lowest = floor == null || beams == 0 ? 0 : floor.Max();
                int pick = -1;

                // The first section every beam passes with, a beam that fails ending the question.
                for (int c = Math.Max(0, lowest); c < sections && pick < 0 && beams > 0; c++)
                {
                    bool passes = true;
                    for (int b = 0; b < beams && passes; b++)
                    {
                        if (ruledOut != null && ruledOut(b, c))
                        {
                            passes = false;
                            break;
                        }
                        var u = utilisation(b, c);
                        passes = Known(u) && u.Value <= target;
                    }
                    if (passes) pick = c;
                }

                // None: the one whose worst beam comes nearest.
                if (pick < 0)
                {
                    double nearest = 0.0;
                    for (int c = Math.Max(0, lowest); c < sections && beams > 0; c++)
                    {
                        double worst = 0.0;
                        bool known = true;
                        for (int b = 0; b < beams && known; b++)
                        {
                            var u = utilisation(b, c);
                            known = Known(u);
                            if (known) worst = Math.Max(worst, u.Value);
                        }
                        if (known && (pick < 0 || worst < nearest))
                        {
                            pick = c;
                            nearest = worst;
                        }
                    }
                }

                for (int b = 0; b < beams; b++) chosen[b] = pick;
                return chosen;
            }

            ForEach(beams, parallel, b =>
                chosen[b] = Pick(sections, c => utilisation(b, c), target, floor == null ? 0 : floor[b],
                                 ruledOut == null ? (Func<int, bool>)null : c => ruledOut(b, c)));
            return chosen;
        }

        private static bool Known(double? u) => u.HasValue && !double.IsNaN(u.Value);

        private static int Pick(int sections, Func<int, double?> utilisation, double target, int lowest, Func<int, bool> ruledOut)
        {
            lowest = Math.Max(0, lowest);

            // The first that passes; a section ruled out on its bound is passed over unchecked.
            for (int c = lowest; c < sections; c++)
            {
                if (ruledOut != null && ruledOut(c))
                    continue;
                var u = utilisation(c);
                if (Known(u) && u.Value <= target)
                    return c;
            }

            // None passes: the nearest the list comes, which takes every one's utilisation.
            int nearest = -1;
            double best = 0.0;
            for (int c = lowest; c < sections; c++)
            {
                var u = utilisation(c);
                if (!Known(u))
                    continue;
                if (nearest < 0 || u.Value < best)
                {
                    nearest = c;
                    best = u.Value;
                }
            }
            return nearest;
        }

        /// <summary>Runs <paramref name="body"/> for 0 to count - 1, on several threads or one; an exception comes out as itself.</summary>
        private static void ForEach(int count, bool parallel, Action<int> body)
        {
            if (!parallel || count < 2)
            {
                for (int i = 0; i < count; i++) body(i);
                return;
            }

            try
            {
                Parallel.For(0, count, body);
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
            {
                ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
            }
        }

        /// <summary>
        /// The recorded steps to check: those asked for, or, with none asked for, the last step of a
        /// static analysis - the whole load, when it is applied in increments - and every step of a
        /// transient one.
        /// </summary>
        public static List<int> StepsOf(Model analysed, IReadOnlyList<int> asked)
        {
            if (asked != null && asked.Count > 0)
                return asked.Distinct().ToList();

            int count = Read.StepCount(analysed);
            if (count == 0)
                throw new Exception("The analysis recorded no steps to check.");

            return analysed.IsTransient ? Enumerable.Range(0, count).ToList() : new List<int> { count - 1 };
        }

        /// <summary>
        /// Sizes the grouped beams of <paramref name="model"/>, an assembled model, starting from the
        /// sections it has.
        /// </summary>
        /// <param name="analyse">Solves an assembled model and returns it analysed; throws when the analysis fails.</param>
        /// <param name="cancelled">Asked between rounds; true stops after the round done, as if it were the last.</param>
        /// <param name="rebuild">
        /// The model again with one section per beam, assembled; null for <see cref="Model.WithBeamSections"/>.
        /// For a test, which has no Rhino to assemble with.
        /// </param>
        public static SizingResult Run(Model model, IReadOnlyList<SizingGroup> groups, SizingOptions options,
                                       Func<Model, Model> analyse, Func<bool> cancelled = null,
                                       Func<Model, IReadOnlyList<IUniaxialSection>, Model> rebuild = null)
        {
            rebuild = rebuild ?? ((original, each) => original.WithBeamSections(each));
            options = options ?? new SizingOptions();
            var settings = options.Design ?? new DesignSettings { Report = false };
            int count = model.Beams.Count;

            var groupOf = Enumerable.Repeat(-1, count).ToArray();
            for (int g = 0; g < groups.Count; g++)
                foreach (int i in groups[g].Beams)
                    groupOf[i] = g;

            var result = new SizingResult { Group = groupOf.ToList() };
            var sections = model.Beams.Select(beam => beam.Section).ToArray();
            var analysedDesigns = new List<IUniaxialSection[]>();
            bool onwardOnly = false;

            for (int round = 1; round <= Math.Max(1, options.MaxRounds); round++)
            {
                var watch = Stopwatch.StartNew();
                var assembled = round == 1 ? model : rebuild(model, sections);
                var analysed = analyse(assembled);
                analysedDesigns.Add((IUniaxialSection[])sections.Clone());
                double analysisSeconds = watch.Elapsed.TotalSeconds;
                watch.Restart();

                var steps = StepsOf(analysed, options.Steps);
                var forces = steps.Select(step => Utilisations.Forces(analysed, step)).ToList();
                var checks = new SteelSections();
                int fullChecks = 0, screened = 0;

                // Every beam with the section it has, the worst step governing.
                var own = new MemberUtilisation[count];
                var ownMax = new double?[count];
                ForEach(count, options.Parallel, i =>
                {
                    (ownMax[i], own[i]) = Envelope(analysed.Beams[i], sections[i], forces, i, options.Members, settings, checks);
                    Interlocked.Increment(ref fullChecks);
                });

                // Then the grouped beams with the sections of their groups, each worked out the first
                // time it is asked for and kept for the rest of the round; the bound likewise.
                var analysedDesign = sections;
                var tables = new Table[groups.Count];
                int failing = 0;
                for (int g = 0; g < groups.Count; g++)
                {
                    var group = groups[g];
                    tables[g] = new Table(group.Beams.Count, group.Sections.Count,
                        (b, c) =>
                        {
                            int i = group.Beams[b];
                            if (ReferenceEquals(group.Sections[c], analysedDesign[i]))
                                return ownMax[i];
                            Interlocked.Increment(ref fullChecks);
                            return Envelope(analysed.Beams[i], group.Sections[c], forces, i, options.Members, settings, checks).max;
                        },
                        (b, c) =>
                        {
                            int i = group.Beams[b];
                            if (ReferenceEquals(group.Sections[c], analysedDesign[i]))
                                return 0.0;
                            double bound = forces.Max(step => Utilisations.LowerBound(group.Sections[c], step[i], settings, checks));
                            if (bound > options.Target)
                                Interlocked.Increment(ref screened);
                            return bound;
                        });

                    failing += group.Beams.Count(i => !(ownMax[i] <= options.Target));
                }

                IUniaxialSection[] Propose(bool onward)
                {
                    var proposal = (IUniaxialSection[])sections.Clone();
                    var picks = new int[groups.Count][];
                    ForEach(groups.Count, options.Parallel, g =>
                    {
                        var group = groups[g];
                        var table = tables[g];
                        Func<int, int, bool> ruledOut = null;
                        if (options.Screen)
                            ruledOut = (b, c) => table.Bound(b, c) > options.Target;
                        picks[g] = Choose(group, group.Beams.Count, table.Get, options.Target,
                                          onward ? Floor(group, sections) : null, ruledOut, options.Parallel);
                    });
                    for (int g = 0; g < groups.Count; g++)
                        for (int b = 0; b < groups[g].Beams.Count; b++)
                            if (picks[g][b] >= 0)
                                proposal[groups[g].Beams[b]] = groups[g].Sections[picks[g][b]];
                    return proposal;
                }

                var next = Propose(onwardOnly);
                int changed = Changed(sections, next);

                // A design analysed before, other than this one, has come round again: from here on
                // beams only move on down their lists, so the rounds come to an end.
                if (changed > 0 && !onwardOnly && analysedDesigns.Take(analysedDesigns.Count - 1).Any(design => Changed(design, next) == 0))
                {
                    onwardOnly = true;
                    result.Oscillated = true;
                    next = Propose(true);
                    changed = Changed(sections, next);
                }

                result.Rounds.Add(new SizingRound
                {
                    Iteration = round,
                    Mass = analysed.TotalMass ?? 0.0,
                    MaxUtilisation = Largest(ownMax),
                    Target = options.Target,
                    Failing = failing,
                    Changed = changed,
                    OnwardOnly = onwardOnly,
                    AnalysisSeconds = analysisSeconds,
                    CheckSeconds = watch.Elapsed.TotalSeconds,
                    Checks = fullChecks,
                    Screened = screened,
                });

                result.Model = analysed;
                result.Steps = steps;
                result.Sections = sections.ToList();
                result.Utilisation = ownMax.ToList();
                result.Governing = own.Select(u => u?.Governing).ToList();

                if (changed == 0)
                {
                    result.Converged = true;
                    break;
                }

                if (cancelled != null && cancelled())
                    break;

                sections = next;
            }

            return result;
        }

        /// <summary>
        /// Per beam of the group, the position of the section it has in the group's list: it may stay or
        /// move on down from there. A beam whose section is not on the list - one none of the list could
        /// be checked for - may go anywhere on it.
        /// </summary>
        private static int[] Floor(SizingGroup group, IReadOnlyList<IUniaxialSection> sections)
        {
            return group.Beams
                .Select(i => Math.Max(0, group.Sections.FindIndex(section => ReferenceEquals(section, sections[i]))))
                .ToArray();
        }

        /// <summary>
        /// The utilisations of a group's beams with its sections, and their lower bounds, each worked out
        /// once, when it is first asked for. One beam's row is only ever asked for by one thread at a time.
        /// </summary>
        private sealed class Table
        {
            private readonly double?[,] _values;
            private readonly bool[,] _known;
            private readonly Func<int, int, double?> _work;
            private readonly double[,] _bounds;
            private readonly bool[,] _bounded;
            private readonly Func<int, int, double> _bound;

            public Table(int beams, int sections, Func<int, int, double?> work, Func<int, int, double> bound)
            {
                _values = new double?[beams, sections];
                _known = new bool[beams, sections];
                _work = work;
                _bounds = new double[beams, sections];
                _bounded = new bool[beams, sections];
                _bound = bound;
            }

            public double? Get(int beam, int section)
            {
                if (!_known[beam, section])
                {
                    _values[beam, section] = _work(beam, section);
                    _known[beam, section] = true;
                }
                return _values[beam, section];
            }

            public double Bound(int beam, int section)
            {
                if (!_bounded[beam, section])
                {
                    _bounds[beam, section] = _bound(beam, section);
                    _bounded[beam, section] = true;
                }
                return _bounds[beam, section];
            }
        }

        /// <summary>How many beams have another section in <paramref name="b"/> than in <paramref name="a"/>.</summary>
        private static int Changed(IReadOnlyList<IUniaxialSection> a, IReadOnlyList<IUniaxialSection> b)
        {
            int changed = 0;
            for (int i = 0; i < a.Count; i++)
                if (!ReferenceEquals(a[i], b[i]))
                    changed++;
            return changed;
        }

        private static double? Largest(IEnumerable<double?> values)
        {
            double? largest = null;
            foreach (var value in values)
                if (value.HasValue && !double.IsNaN(value.Value) && (!largest.HasValue || value.Value > largest.Value))
                    largest = value;
            return largest;
        }

        /// <summary>
        /// A beam with a section, at every step: the largest utilisation, null when any step could not be
        /// checked, and the check of the step that governs.
        /// </summary>
        private static (double? max, MemberUtilisation governing) Envelope(IBeam beam, IUniaxialSection section,
                                                                          List<List<List<SectionForces>>> forces, int i,
                                                                          IReadOnlyList<SteelMember> members, DesignSettings settings,
                                                                          SteelSections checks)
        {
            var member = members != null && i < members.Count ? members[i] : null;
            double? max = null;
            MemberUtilisation governing = null;

            foreach (var step in forces)
            {
                var check = Utilisations.Check(beam, section, step[i], member, settings, checks);
                var value = check.Max;
                if (!value.HasValue || double.IsNaN(value.Value))
                    return (null, check);

                if (!max.HasValue || value.Value > max.Value)
                {
                    max = value;
                    governing = check;
                }
            }

            return (max, governing);
        }
    }
}
