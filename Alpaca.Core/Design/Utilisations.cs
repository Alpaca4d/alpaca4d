using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;
using Alpaca4d.Loads;
using Alpaca4d.Material;

namespace Alpaca4d.Design
{
    /// <summary>The choices a design check needs that the model cannot answer.</summary>
    public class DesignSettings
    {
        public Fabrication Fabrication = Fabrication.Rolled;
        public double GammaM0 = 1.0;
        public double GammaM1 = 1.0;

        /// <summary>Write each member's report. Off, nothing is formatted, which on a large model is most of the work.</summary>
        public bool Report = true;

        /// <summary>
        /// Timber: the load-duration class of the loads, for k_mod. Null to take it from the model's load
        /// patterns (<see cref="For"/>), or, with no model to ask, permanent - the lowest k_mod.
        /// </summary>
        public LoadDuration? Duration;

        /// <summary>Where <see cref="Duration"/> came from, for the report: the patterns that set it, or those that gave none.</summary>
        public string DurationNote;

        /// <summary>Timber: the combination is accidental, so γM is 1.0 (Table 2.3).</summary>
        public bool Accidental;

        /// <summary>
        /// The one family to check - "Steel" or "Timber", as <see cref="IDesignGrade.Family"/> names it -
        /// or null for every family with a check. A member of another family comes back with its type,
        /// nothing checked, and <see cref="MemberUtilisation.NotCheckedWhy"/> saying so: Utilisation's
        /// Steel and Timber types each check their own.
        /// </summary>
        public string Family;

        /// <summary>Timber: γM in place of Table 2.3's recommended value for the product - a National Annex's, say. Null for the table's.</summary>
        public double? GammaM;

        /// <summary>
        /// A copy, with what the model can tell filled in: the duration of its loads, unless one was
        /// given (<see cref="LoadDurations.Governing"/>), and whether it is read for an accidental combination.
        /// </summary>
        public DesignSettings For(Model model)
        {
            var copy = (DesignSettings)MemberwiseClone();
            if (model == null)
                return copy;

            if (!copy.Duration.HasValue)
            {
                copy.Duration = LoadDurations.Governing(model, out var unset);
                var setBy = (model.LoadPatterns ?? new List<LoadPattern>())
                    .Where(p => p != null && p.Factor != 0.0 && p.Duration == copy.Duration)
                    .Select(p => string.IsNullOrWhiteSpace(p.Name) ? $"pattern {p.Id}" : p.Name.Trim())
                    .ToList();
                var notes = new List<string>();
                if (setBy.Count > 0) notes.Add("set by " + string.Join(", ", setBy));
                if (unset.Count > 0) notes.Add("no duration given for " + string.Join(", ", unset) + ", taken as permanent");
                copy.DurationNote = string.Join("; ", notes);
            }

            if (model.Combination?.Kind == CombinationKind.Accidental)
                copy.Accidental = true;
            return copy;
        }
    }

    public static class Utilisations
    {
        /// <summary>
        /// The utilisation of every beam in the model at one recorded step, in the order of Model.Beams.
        ///
        /// What a beam is checked against follows from its material's design grade: steel goes to
        /// EN 1993-1-1, timber to EN 1995-1-1. A beam whose material has no grade, or one of a family
        /// with no check yet, comes back with its type and nothing checked.
        /// </summary>
        /// <param name="elementLengths">Each beam's length, node to node, in the order of Model.Beams.</param>
        /// <param name="bucklingLengths">Each beam's L_cr about both axes and its L_LT, or null to take its length.</param>
        public static List<MemberUtilisation> Of(Model model, int step, IReadOnlyList<double> elementLengths,
                                                 IReadOnlyList<double> bucklingLengths, DesignSettings settings)
        {
            var members = new List<SteelMember>();
            for (int i = 0; i < model.Beams.Count; i++)
            {
                double length = elementLengths != null && i < elementLengths.Count ? elementLengths[i] : 0.0;
                double lcr = bucklingLengths != null && i < bucklingLengths.Count ? bucklingLengths[i] : length;
                members.Add(new SteelMember
                {
                    Length = length,
                    BucklingLengthY = lcr,
                    BucklingLengthZ = lcr,
                    LateralTorsionalLength = length,
                });
            }

            return Of(model, step, members, settings);
        }

        /// <summary>
        /// The utilisation of every beam in the model at one recorded step, in the order of Model.Beams,
        /// each with what its check needs to know about how it is held and loaded.
        /// </summary>
        /// <param name="members">One per beam, in the order of Model.Beams: its length, node to node, and its buckling lengths.</param>
        public static List<MemberUtilisation> Of(Model model, int step, IReadOnlyList<SteelMember> members, DesignSettings settings)
        {
            settings = (settings ?? new DesignSettings()).For(model);
            var output = new List<MemberUtilisation>();

            if (model.Beams.Count == 0)
                return output;

            var forces = Forces(model, step);
            var sections = new SteelSections();

            for (int i = 0; i < model.Beams.Count; i++)
            {
                var beam = model.Beams[i];
                var member = members != null && i < members.Count ? members[i] : null;
                output.Add(Check(beam, beam.Section, forces[i], member, settings, sections));
            }

            return output;
        }

        /// <summary>
        /// The section forces along every beam at one recorded step, in the order of Model.Beams: one
        /// per integration section, I end to J end.
        /// </summary>
        public static List<List<SectionForces>> Forces(Model model, int step)
        {
            var output = new List<List<SectionForces>>();
            if (model.Beams.Count == 0)
                return output;

            var (n, mz, vy, my, vz, t) = Result.Read.ForceBeamColumn(model, step);

            for (int i = 0; i < model.Beams.Count; i++)
            {
                var forces = new List<SectionForces>();
                for (int k = 0; k < n[i].Count; k++)
                    forces.Add(new SectionForces(n[i][k], vy[i][k], vz[i][k], t[i][k], my[i][k], mz[i][k]));
                output.Add(forces);
            }

            return output;
        }

        /// <summary>
        /// One beam checked against the design code of its section's material, under the forces it
        /// carried. The section need not be the beam's own: the optimiser tries other sections on the
        /// forces of the one the beam was analysed with.
        ///
        /// What the beam is checked against follows from the material's design grade: steel goes to
        /// EN 1993-1-1, timber to EN 1995-1-1 (<see cref="TimberCheck"/>, with the duration of the loads
        /// from <paramref name="settings"/>). A material with no grade, or one of a family with no check
        /// yet, comes back with its type and nothing checked.
        /// </summary>
        /// <param name="forces">The section forces at each integration section of the beam, I end to J end.</param>
        /// <param name="member">The beam's length, node to node, and its buckling lengths; null for none.</param>
        /// <param name="sections">Where the steel sections made on the way are kept, to be made once each.</param>
        public static MemberUtilisation Check(IBeam beam, IUniaxialSection section, IReadOnlyList<SectionForces> forces,
                                              SteelMember member, DesignSettings settings, SteelSections sections)
        {
            settings = settings ?? new DesignSettings();
            sections = sections ?? new SteelSections();

            var grade = DesignGrades.Of(section?.Material);
            if (grade != null && settings.Family != null && !string.Equals(grade.Family, settings.Family, StringComparison.OrdinalIgnoreCase))
            {
                return new MemberUtilisation
                {
                    Type = grade.Family,
                    NotCheckedWhy = $"it is {grade.Family.ToLowerInvariant()}, which the {settings.Family.ToLowerInvariant()} check leaves alone",
                    Report = settings.Report ? $"{grade.Name} is {grade.Family.ToLowerInvariant()}: not checked here, where only {settings.Family.ToLowerInvariant()} is." : ""
                };
            }

            member = member ?? new SteelMember();
            double length = member.Length;

            IReadOnlyList<double> stations = null;
            if (beam?.BeamIntegration != null && length > 0.0)
            {
                stations = beam.BeamIntegration.SectionLocations(length);
                if (forces == null || stations.Count != forces.Count) stations = null;
            }

            if (grade is TimberGrade timber)
            {
                var timberSection = TimberSection.Of(section, out string why);
                if (timberSection == null)
                {
                    return new MemberUtilisation
                    {
                        Type = timber.Family,
                        NotCheckedWhy = $"its section is {why}",
                        Report = settings.Report ? $"EN 1995-1-1, {timber.Name}: not checked, its section is {why}." : ""
                    };
                }
                return TimberCheck.Run(timberSection, timber, forces, member, settings, stations);
            }

            var steel = grade as SteelGrade;
            if (steel == null)
            {
                return new MemberUtilisation
                {
                    Type = grade?.Family ?? "Unknown",
                    Report = !settings.Report ? "" : grade == null
                        ? "The material has no design grade. Make it with Material Library Elastic, or name it after a grade (\"S355\", \"GL24h\"), to have it checked."
                        : $"{grade.Family} members are not checked yet."
                };
            }

            var steelSection = sections.Of(section, steel, settings.Fabrication);
            if (steelSection == null)
            {
                return new MemberUtilisation
                {
                    Type = steel.Family,
                    Report = settings.Report ? "The section has no shape to check - an Elastic Section is a list of properties with no plates behind them." : ""
                };
            }

            return SteelCheck.Run(steelSection, forces, member, settings.GammaM0, settings.GammaM1, stations, settings.Report);
        }

        /// <summary>
        /// A lower bound on the Max of <see cref="Check"/> for the same beam, section and forces
        /// (<see cref="SteelCheck.LowerBound"/>): when it is above a target, so is the Max, if the check has one.
        /// Zero for a section the check has nothing to say about, which rules nothing out.
        /// </summary>
        public static double LowerBound(IUniaxialSection section, IReadOnlyList<SectionForces> forces,
                                        DesignSettings settings, SteelSections sections)
        {
            settings = settings ?? new DesignSettings();
            sections = sections ?? new SteelSections();

            var grade = DesignGrades.Of(section?.Material);
            if (grade != null && settings.Family != null && !string.Equals(grade.Family, settings.Family, StringComparison.OrdinalIgnoreCase))
                return 0.0;
            if (grade is TimberGrade timber)
                return TimberCheck.LowerBound(TimberSection.Of(section, out _), timber, forces, settings);
            if (!(grade is SteelGrade steel))
                return 0.0;

            var steelSection = sections.Of(section, steel, settings.Fabrication);
            return steelSection == null ? 0.0 : SteelCheck.LowerBound(steelSection, forces, settings.GammaM0);
        }
    }

    /// <summary>
    /// Steel sections as EN 1993-1-1 sees them, each made once. Most models share a handful of
    /// sections between many beams, and the optimiser tries every one of its candidates on many beams.
    ///
    /// Safe to share between threads: the optimiser checks its beams in parallel. A section is known by
    /// its grade's name rather than the grade itself, since a material named after its grade is given a
    /// new grade every time it is asked for one.
    /// </summary>
    public sealed class SteelSections
    {
        private readonly Dictionary<(IUniaxialSection, string, Fabrication), SteelSection> _made =
            new Dictionary<(IUniaxialSection, string, Fabrication), SteelSection>();

        /// <summary>The section as the check sees it, or null when it has no shape to check.</summary>
        public SteelSection Of(IUniaxialSection section, SteelGrade grade, Fabrication fabrication)
        {
            var key = (section, grade?.Name, fabrication);
            lock (_made)
            {
                if (!_made.TryGetValue(key, out var steel))
                {
                    steel = SteelSection.Of(section, grade, fabrication);
                    _made[key] = steel;
                }
                return steel;
            }
        }
    }
}
