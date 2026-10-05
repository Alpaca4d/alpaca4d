using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;
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
    }

    public static class Utilisations
    {
        /// <summary>
        /// The utilisation of every beam in the model at one recorded step, in the order of Model.Beams.
        ///
        /// What a beam is checked against follows from its material's design grade: steel goes to
        /// EN 1993-1-1. A beam whose material has no grade, or one of a family with no check yet, comes
        /// back with its type and nothing checked.
        /// </summary>
        /// <param name="elementLengths">Each beam's length, node to node, in the order of Model.Beams.</param>
        /// <param name="bucklingLengths">Each beam's L_cr, or null to take its length.</param>
        public static List<MemberUtilisation> Of(Model model, int step, IReadOnlyList<double> elementLengths,
                                                 IReadOnlyList<double> bucklingLengths, DesignSettings settings)
        {
            settings = settings ?? new DesignSettings();
            var output = new List<MemberUtilisation>();

            if (model.Beams.Count == 0)
                return output;

            var (n, mz, vy, my, vz, t) = Result.Read.ForceBeamColumn(model, step);

            // Most models share a handful of sections between many beams.
            var steelSections = new Dictionary<(IUniaxialSection, SteelGrade), SteelSection>();

            for (int i = 0; i < model.Beams.Count; i++)
            {
                var beam = model.Beams[i];
                var grade = DesignGrades.Of(beam.Section?.Material);

                var steel = grade as SteelGrade;
                if (steel == null)
                {
                    output.Add(new MemberUtilisation
                    {
                        Type = grade?.Family ?? "Unknown",
                        Report = !settings.Report ? "" : grade == null
                            ? "The material has no design grade. Make it with Material Library Elastic, or name it after a grade (\"S355\"), to have it checked."
                            : $"{grade.Family} members are not checked yet."
                    });
                    continue;
                }

                var key = (beam.Section, steel);
                if (!steelSections.TryGetValue(key, out var section))
                {
                    section = SteelSection.Of(beam.Section, steel, settings.Fabrication);
                    steelSections[key] = section;
                }

                if (section == null)
                {
                    output.Add(new MemberUtilisation
                    {
                        Type = steel.Family,
                        Report = settings.Report ? "The section has no shape to check - an Elastic Section is a list of properties with no plates behind them." : ""
                    });
                    continue;
                }

                double length = elementLengths != null && i < elementLengths.Count ? elementLengths[i] : 0.0;
                double lcr = bucklingLengths != null && i < bucklingLengths.Count ? bucklingLengths[i] : length;

                var forces = new List<SectionForces>();
                for (int k = 0; k < n[i].Count; k++)
                    forces.Add(new SectionForces(n[i][k], vy[i][k], vz[i][k], t[i][k], my[i][k], mz[i][k]));

                IReadOnlyList<double> stations = null;
                if (beam.BeamIntegration != null && length > 0.0)
                {
                    stations = beam.BeamIntegration.SectionLocations(length);
                    if (stations.Count != forces.Count) stations = null;
                }

                output.Add(SteelCheck.Run(section, forces, lcr, settings.GammaM0, settings.GammaM1, stations, settings.Report));
            }

            return output;
        }
    }
}
