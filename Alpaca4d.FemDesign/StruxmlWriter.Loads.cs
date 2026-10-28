using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Rhino.Geometry;

using StruSoft.Interop.StruXml.Data;

namespace Alpaca4d.FemDesign
{
    /// <summary>
    /// Loads. Alpaca has no load cases: every pattern of a model acts at once. So each pattern becomes a
    /// load case holding its loads as they are, gravity becomes a dead-load case - FEM-Design works out the
    /// self-weight itself - and one combination adds them all with the patterns' factors, which is the
    /// load Alpaca analyses.
    /// </summary>
    public sealed partial class StruxmlWriter
    {
        /// <summary>The combination that reproduces Alpaca's analysis.</summary>
        public const string CombinationName = "Alpaca";

        private const double NoLoad = 1e-12;
        private readonly HashSet<string> caseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int loadCount;

        private void WriteLoads()
        {
            var combination = new List<(Load_case_type Case, double Factor)>();
            double selfWeight = 0.0;
            int number = 0;

            foreach (var pattern in model.LoadCases)
            {
                number++;
                selfWeight += pattern.Factor * pattern.GravityFactor;

                bool hasLoads = pattern.PointLoads.Any(l => l.Force.Length > NoLoad || l.Moment.Length > NoLoad)
                             || pattern.BeamLoads.Any(l => l.Force.Length > NoLoad)
                             || pattern.ShellLoads.Any(l => l.Pressure.Length > NoLoad);
                if (!hasLoads)
                {
                    if (pattern.GravityFactor == 0.0)
                        report.Remark("A load pattern without loads is not exported.", pattern.Name ?? $"pattern {number}");
                    continue;
                }

                var loadCase = Load_case_type.Create(CaseName(pattern.Name, $"LP{(pattern.Number > 0 ? pattern.Number : number)}"));
                Add(loadCase, loadCase.Guid);
                loadCaseNames.Add(loadCase.Name);
                combination.Add((loadCase, pattern.Factor));

                WritePointLoads(pattern, loadCase);
                WriteBeamLoads(pattern, loadCase);
                WriteShellLoads(pattern, loadCase);
            }

            if (selfWeight != 0.0)
            {
                var deadLoad = Load_case_type.Create(CaseName("Self-weight", "Self-weight"), Loadcasetype_type.Dead_load);
                Add(deadLoad, deadLoad.Guid);
                loadCaseNames.Add(deadLoad.Name);
                combination.Add((deadLoad, selfWeight));
                report.Remark($"Gravity is exported as the dead-load case {deadLoad.Name}, in which FEM-Design works out the self-weight from its own materials.", deadLoad.Name);
            }

            if (combination.Count == 0)
                return;

            report.Remark("Load cases are exported as static and permanent; set their types and durations in FEM-Design, which its design depends on.");
            var fdCombination = Load_combination_type.Create(CombinationName, Loadcombtype.Ultimate_ordinary,
                combination.Select(c => c.Factor).ToList(), combination.Select(c => c.Case.Guid).ToList());
            Add(fdCombination, fdCombination.Guid);
            report.Remark($"The combination {CombinationName} adds every load case with its load pattern's factor, which is what Alpaca analyses.", CombinationName);
        }

        /// <summary>A load case name FEM-Design takes: at most 80 characters, unique in the model.</summary>
        private string CaseName(string name, string fallback)
        {
            string stem = string.IsNullOrWhiteSpace(name) ? fallback : Clean(name.Trim());
            if (stem.Length > 70) stem = stem.Substring(0, 70);
            string unique = stem;
            for (int n = 2; !caseNames.Add(unique); n++)
                unique = $"{stem} ({n})";
            return unique;
        }

        private void WritePointLoads(ExportLoadCase pattern, Load_case_type loadCase)
        {
            foreach (var load in pattern.PointLoads)
            {
                foreach (var (vector, type) in new[] { (load.Force, Force_load_type.Force), (load.Moment, Force_load_type.Moment) })
                {
                    double value = vector.Length;
                    if (value <= NoLoad)
                        continue;
                    var fdLoad = Point_load_type.Create(loadCase.Guid, P(load.Pos), value, V(vector / value), type);
                    Add(fdLoad, fdLoad.Guid);
                    loadCount++;
                }
            }
        }

        private void WriteBeamLoads(ExportLoadCase pattern, Load_case_type loadCase)
        {
            // Two loads on one beam go in as their sum.
            var forces = new Dictionary<int, Vector3d>();
            foreach (var load in pattern.BeamLoads)
            {
                forces.TryGetValue(load.Beam, out var sum);
                forces[load.Beam] = sum + load.Force;
            }

            foreach (var pair in forces)
            {
                var bar = pair.Key < bars.Length ? bars[pair.Key] : null;
                double value = pair.Value.Length;
                if (bar == null || value <= NoLoad)
                    continue;
                // FEM-Design puts a line load on the bar under it.
                var fdLoad = Line_load_type.UniformForce(loadCase.Guid, P(bar.Start), P(bar.End), value, V(pair.Value / value), Force_load_type.Force, loadProjection: false);
                Add(fdLoad, fdLoad.Guid);
                loadCount++;
            }
        }

        /// <summary>
        /// Shell loads, as surface loads over the parts of each plate or wall that carry the same pressure:
        /// one load for a whole loaded floor, not one per element.
        /// </summary>
        private void WriteShellLoads(ExportLoadCase pattern, Load_case_type loadCase)
        {
            var pressureOn = new Dictionary<int, Vector3d>();
            foreach (var load in pattern.ShellLoads)
                foreach (int face in load.Shell < shellFaces.Length ? shellFaces[load.Shell] : new List<int>())
                {
                    pressureOn.TryGetValue(face, out var sum);
                    pressureOn[face] = sum + load.Pressure;
                }

            string PressureKey(Vector3d v) => string.Join(",", new[] { v.X, v.Y, v.Z }.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));

            var parts = pressureOn.Where(pair => pair.Value.Length > NoLoad && groups[faces[pair.Key].Group].Written)
                                  .GroupBy(pair => (faces[pair.Key].Group, PressureKey(pair.Value)));
            foreach (var part in parts)
            {
                var pressure = part.First().Value;
                double value = pressure.Length;
                var normal = groups[part.Key.Group].Normal;
                var regions = Outline(part.Select(pair => pair.Key), normal)
                           ?? part.SelectMany(pair => Outline(new[] { pair.Key }, normal) ?? new List<Region_type>()).ToList();
                foreach (var region in regions)
                {
                    var fdLoad = Surface_load_type.CreateUniformForce(loadCase.Guid, region, value, V(pressure / value), loadProjection: false);
                    Add(fdLoad, fdLoad.Guid);
                    loadCount++;
                }
            }
        }

        // ---------------------------------------------------------------- masses

        private void WriteMasses()
        {
            foreach (var mass in model.Masses)
            {
                var m = mass.TransMass;
                double value = Math.Max(m.X, Math.Max(m.Y, m.Z));
                string where = $"({mass.Pos.X:0.###}, {mass.Pos.Y:0.###}, {mass.Pos.Z:0.###})";
                if (value <= 0)
                    continue;
                if (Math.Abs(m.X - m.Y) > 1e-9 * value || Math.Abs(m.X - m.Z) > 1e-9 * value)
                    report.Warning("FEM-Design's point masses are the same in every direction; a mass that differs by direction is exported at its largest.", where);
                if (mass.RotationMass.Length > 0)
                    report.Warning("FEM-Design's point masses have no rotational inertia; it is left out.", where);

                var fdMass = Mass_point_type.Create(P(mass.Pos), value / 1000.0); // kg to t
                Add(fdMass, fdMass.Guid);
            }
        }

        private void ReportCounts()
        {
            var parts = new List<string>();
            void Count(int n, string one, string many) { if (n > 0) parts.Add($"{n} {(n == 1 ? one : many)}"); }

            Count(bars.Count(b => b?.Kind == BarKind.Beam), "beam", "beams");
            Count(bars.Count(b => b?.Kind == BarKind.Column), "column", "columns");
            Count(bars.Count(b => b?.Kind == BarKind.Truss), "truss", "trusses");
            Count(bars.Count(b => b?.Kind == BarKind.Fictitious), "fictitious bar", "fictitious bars");
            Count(plateCount, "plate", "plates");
            Count(wallCount, "wall", "walls");
            if (plateCount + wallCount > 0)
                parts[parts.Count - 1] += $" out of {model.Shells.Count} shell elements";
            Count(supportCount, "support", "supports");
            Count(connectionCount, "connection", "connections");
            Count(diaphragmCount, "diaphragm", "diaphragms");
            Count(loadCaseNames.Count, "load case", "load cases");
            Count(loadCount, "load", "loads");

            report.Remark($"Exported {(parts.Count > 0 ? string.Join(", ", parts) : "nothing")}.");
        }
    }
}
