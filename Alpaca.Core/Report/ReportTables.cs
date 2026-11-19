using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Report
{
    /// <summary>
    /// The tables a template can ask for by name, each worked out of the models it was given:
    /// what the model is made of, and what each analysis found. A table that cannot be made - no
    /// model of its kind, or a result the recorder did not keep - says why instead.
    /// </summary>
    public static class ReportTables
    {
        /// <summary>Every table, by the name a template asks for it with, and what it holds.</summary>
        public static readonly (string Name, string What)[] Catalogue =
        {
            ("model.summary", "Nodes, elements by type, supports, load patterns, mass and extent."),
            ("model.sections", "Each beam section: material, how many elements, their length, A, Iy, Iz and J."),
            ("model.materials", "Each beam material: E, G, Poisson's ratio and density."),
            ("model.supports", "Each support: where it is and which of Tx Ty Tz Rx Ry Rz it holds."),
            ("static.displacements", "The largest and smallest displacement along X, Y and Z, and the largest overall, with their nodes."),
            ("static.reactions", "The reactions at each support, and their sum."),
            ("vibration.modes", "Each mode's eigenvalue, angular frequency, frequency and period."),
            ("vibration.massRatios", "Each mode's participating mass along X, Y and Z, as a percentage, and its running total."),
            ("buckling.factors", "Each buckling mode's load factor."),
            ("footfall.response", "The nodes that respond most to footfall, worst first: their response factor R, its steady-state and transient parts, their accelerations and the pace that gives R."),
            ("loads.patterns", "Each load pattern: its factor, its loads, and the force they add up to along X, Y and Z, with the total of all of them."),
            ("loads.combinations", "Each load combination given, from Load Combination: its type and its patterns with their factors."),
            ("static.combinations", "Each combination analysed - or each static model given - with its largest displacement and the sum of its reactions."),
            ("design.utilisation", "The steel members checked to EN 1993-1-1 over the ultimate combinations: each section's worst member, its governing check and its utilisation."),
            ("design.members", "Every steel member checked to EN 1993-1-1 over the ultimate combinations, worst first: its section, grade, class, governing check and utilisation."),
            ("report.about", "What the report was made with: Alpaca4d and OpenSees, the units, how many models, and when it was written."),
            ("report.analyses", "Each model given and how it was solved: its analysis and combination, and OpenSees' integrator, steps, algorithm, test and solver."),
        };

        /// <summary>
        /// What each table can show or leave out, by the names Report Result Table's checkboxes give
        /// them: its columns, and rows such as a sum, and "Note below", the note under it. The first
        /// column - what a row is of - is always there.
        /// </summary>
        public static readonly Dictionary<string, string[]> Options = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["model.summary"] = new[] { "Nodes", "Elements", "Supports", "Load patterns", "Mass", "Extent", "Analyses" },
            ["model.sections"] = new[] { "Material", "Elements", "Length", "A", "Iy", "Iz", "J" },
            ["model.materials"] = new[] { "E", "G", "Poisson's ratio", "Density" },
            ["model.supports"] = new[] { "X", "Y", "Z", "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" },
            ["static.displacements"] = new[] { "Ux", "Uy", "Uz", "|U|", "Smallest", "Nodes", "Note below" },
            ["static.reactions"] = new[] { "Fx", "Fy", "Fz", "Mx", "My", "Mz", "Sum", "Note below" },
            ["vibration.modes"] = new[] { "Eigenvalue", "Angular frequency", "Frequency", "Period" },
            ["vibration.massRatios"] = new[] { "Ux", "Uy", "Uz", "Sum Ux", "Sum Uy", "Sum Uz", "Note below" },
            ["buckling.factors"] = new[] { "Note below" },
            ["loads.patterns"] = new[] { "Factor", "Loads", "Fx", "Fy", "Fz", "Total", "Note below" },
            ["loads.combinations"] = new[] { "Type", "Combination" },
            ["static.combinations"] = new[] { "Type", "Largest displacement", "Node", "Sum Fx", "Sum Fy", "Sum Fz", "Note below" },
            ["design.utilisation"] = new[] { "Grade", "Members", "Element", "Governing", "Combination", "Result", "Note below" },
            ["design.members"] = new[] { "Section", "Grade", "Class", "Governing", "Combination", "Result", "Note below" },
            ["report.about"] = new[] { "Software", "Solver", "Units", "Models", "Written" },
            ["report.analyses"] = new[] { "Combination", "Integrator", "Steps", "Algorithm", "Test", "System", "Note below" },
            ["footfall.response"] = new[] { "Response factor R", "Steady-state R", "Transient R", "Steady-state acceleration", "Transient acceleration", "Pace", "Note below" },
        };

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>A number to so many decimals, a negative one that rounds to nothing written 0, not -0.</summary>
        private static string F(double value, int decimals)
        {
            double rounded = Math.Round(value, decimals);
            return (rounded == 0.0 ? 0.0 : value).ToString("F" + decimals, Invariant);
        }

        /// <summary>
        /// The table <paramref name="name"/> names, out of the model of its kind - or the one
        /// <paramref name="model"/> picks - with no more rows than <paramref name="modes"/> lets
        /// through for a table of modes. Null, and why, when it cannot be made.
        /// </summary>
        public static TableBlock Make(string name, ModelSet models, object model, IList<int> modes, out string problem) =>
            Make(name, models, model, modes, null, out problem);

        /// <summary>
        /// As <see cref="Make(string, ModelSet, object, IList{int}, out string)"/>, showing only what
        /// <paramref name="shown"/> names of the table's <see cref="Options"/> - everything when it is null.
        /// </summary>
        public static TableBlock Make(string name, ModelSet models, object model, IList<int> modes, ICollection<string> shown, out string problem) =>
            Make(name, models, model, modes, shown, null, out problem);

        /// <summary>
        /// As <see cref="Make(string, ModelSet, object, IList{int}, ICollection{string}, out string)"/>, sorted
        /// by <paramref name="by"/>, for a table that can be: footfall's, by any of
        /// <see cref="FootfallResponse.Quantities"/>, R when it is null. That column comes first after
        /// the node, and stays whatever <paramref name="shown"/> says.
        /// </summary>
        public static TableBlock Make(string name, ModelSet models, object model, IList<int> modes, ICollection<string> shown, string by, out string problem) =>
            Make(name, models, model, modes, shown, by, null, out problem);

        /// <summary>
        /// As <see cref="Make(string, ModelSet, object, IList{int}, ICollection{string}, string, out string)"/>,
        /// with what a table may be told besides: for the design tables, "gammaM0", "gammaM1" and
        /// "fabrication" (Rolled or Welded) - EN 1993-1-1's recommended 1.0, 1.0 and Rolled when left out.
        /// </summary>
        public static TableBlock Make(string name, ModelSet models, object model, IList<int> modes, ICollection<string> shown, string by,
                                      Newtonsoft.Json.Linq.JObject options, out string problem)
        {
            var table = Whole(name, models, model, modes, by, options, out problem);
            if (table != null && shown != null)
                Filter((name ?? "").Trim(), table, new HashSet<string>(shown, StringComparer.OrdinalIgnoreCase));
            return table;
        }

        private static TableBlock Whole(string name, ModelSet models, object model, IList<int> modes, string by,
                                        Newtonsoft.Json.Linq.JObject options, out string problem)
        {
            problem = null;
            string key = (name ?? "").Trim().ToLowerInvariant();
            string kind = key.Contains(".") ? key.Substring(0, key.IndexOf('.')) : key;

            try
            {
                // The tables of all the models given at once: the combinations, and the members checked over them.
                switch (key)
                {
                    case "loads.combinations": return Combinations(models, out problem);
                    case "static.combinations": return CombinationResults(models, out problem);
                    case "design.utilisation": return Design(models, options, true, out problem);
                    case "design.members": return Design(models, options, false, out problem);
                    case "report.about": return About(models, out problem);
                    case "report.analyses": return Analyses(models, out problem);
                }
            }
            catch (Exception ex)
            {
                problem = $"{name} could not be read: {ex.Message}";
                return null;
            }

            var source = models.Find(model ?? (kind == "loads" ? "model" : kind), out problem);
            if (source == null) return null;

            try
            {
                switch (key)
                {
                    case "loads.patterns": return Patterns(source, out problem);
                    case "model.summary": return Summary(source, models);
                    case "model.sections": return Sections(source, out problem);
                    case "model.materials": return Materials(source, out problem);
                    case "model.supports": return Supports(source, out problem);
                    case "static.displacements": return Displacements(source, out problem);
                    case "static.reactions": return Reactions(source, out problem);
                    case "vibration.modes": return Modes(source, modes, out problem);
                    case "vibration.massratios": return MassRatios(source, modes, out problem);
                    case "buckling.factors": return BucklingFactors(source, modes, out problem);
                    case "footfall.response": return FootfallTable(source, modes, by, out problem);
                }
            }
            catch (Exception ex)
            {
                problem = $"{name} could not be read: {ex.Message}";
                return null;
            }

            problem = $"there is no table called \"{name}\". The tables are: {string.Join(", ", Catalogue.Select(c => c.Name))}.";
            return null;
        }

        /// <summary>
        /// The table without what <paramref name="shown"/> leaves out: each option off takes away its
        /// columns, or its rows, or the note.
        /// </summary>
        private static void Filter(string name, TableBlock table, HashSet<string> shown)
        {
            bool Off(string option) => !shown.Contains(option);
            var columns = new HashSet<int>();
            var rows = new HashSet<int>();

            void Columns(params (string Option, int Column)[] map)
            {
                foreach (var (option, column) in map)
                    if (Off(option)) columns.Add(column);
            }

            switch (name.ToLowerInvariant())
            {
                case "model.summary":
                    for (int r = 0; r < table.Rows.Count; r++)
                    {
                        var label = table.Rows[r][0];
                        var option = Options["model.summary"].FirstOrDefault(o => label.StartsWith(o, StringComparison.OrdinalIgnoreCase));
                        if (option != null && Off(option)) rows.Add(r);
                    }
                    break;
                case "model.sections":
                    Columns(("Material", 1), ("Elements", 2), ("Length", 3), ("A", 4), ("Iy", 5), ("Iz", 6), ("J", 7));
                    break;
                case "model.materials":
                    Columns(("E", 1), ("G", 2), ("Poisson's ratio", 3), ("Density", 4));
                    break;
                case "model.supports":
                    Columns(("X", 1), ("Y", 2), ("Z", 3), ("Tx", 4), ("Ty", 5), ("Tz", 6), ("Rx", 7), ("Ry", 8), ("Rz", 9));
                    break;
                case "static.displacements":
                    Columns(("Smallest", 3), ("Smallest", 4), ("Nodes", 2), ("Nodes", 4));
                    foreach (var (option, row) in new[] { ("Ux", 0), ("Uy", 1), ("Uz", 2), ("|U|", 3) })
                        if (Off(option)) rows.Add(row);
                    break;
                case "static.reactions":
                    Columns(("Fx", 1), ("Fy", 2), ("Fz", 3), ("Mx", 4), ("My", 5), ("Mz", 6));
                    if (Off("Sum") && table.Bold.Count > 0) rows.Add(table.Bold.Max());
                    break;
                case "vibration.modes":
                    Columns(("Eigenvalue", 1), ("Angular frequency", 2), ("Frequency", 3), ("Period", 4));
                    break;
                case "vibration.massratios":
                    Columns(("Ux", 1), ("Uy", 2), ("Uz", 3), ("Sum Ux", 4), ("Sum Uy", 5), ("Sum Uz", 6));
                    break;
                case "loads.patterns":
                    Columns(("Factor", 1), ("Loads", 2), ("Fx", 3), ("Fy", 4), ("Fz", 5));
                    if (Off("Total") && table.Bold.Count > 0) rows.Add(table.Bold.Max());
                    break;
                case "loads.combinations":
                    Columns(("Type", 1), ("Combination", 2));
                    break;
                case "static.combinations":
                    Columns(("Type", 1), ("Largest displacement", 2), ("Node", 3), ("Sum Fx", 4), ("Sum Fy", 5), ("Sum Fz", 6));
                    break;
                case "design.utilisation":
                    Columns(("Grade", 1), ("Members", 2), ("Element", 3), ("Governing", 4), ("Combination", 6), ("Result", 7));
                    break;
                case "design.members":
                    Columns(("Section", 1), ("Grade", 2), ("Class", 3), ("Governing", 4), ("Combination", 6), ("Result", 7));
                    break;
                case "report.about":
                    for (int r = 0; r < table.Rows.Count; r++)
                        if (Off(table.Rows[r][0])) rows.Add(r);
                    break;
                case "report.analyses":
                    Columns(("Combination", 2), ("Integrator", 3), ("Steps", 4), ("Algorithm", 5), ("Test", 6), ("System", 7));
                    break;
                case "footfall.response":
                    // By the header, as the column sorted by comes first: it stays.
                    for (int c = 2; c < table.Header.Count; c++)
                    {
                        var option = FootfallColumns.FirstOrDefault(f => f.Header == table.Header[c]).Option;
                        if (option != null && Off(option)) columns.Add(c);
                    }
                    break;
            }

            // The note under the table: "Note below", or "Note" as a file saved before called it.
            if (Off("Note below") && Off("Note") && Options.TryGetValue(name, out var options) && options.Contains("Note below"))
                table.Note = "";

            int width = table.Header.Count;
            columns.RemoveWhere(c => c <= 0 || c >= width);
            if (columns.Count > 0)
            {
                List<T> Keep<T>(List<T> list) => list.Where((x, i) => !columns.Contains(i)).ToList();
                table.Header = Keep(table.Header);
                table.Align = Keep(table.Align);
                table.Rows = table.Rows.Select(Keep).ToList();
            }

            if (rows.Count > 0)
            {
                var bold = new HashSet<int>();
                var kept = new List<List<string>>();
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    if (rows.Contains(r)) continue;
                    if (table.Bold.Contains(r)) bold.Add(kept.Count);
                    kept.Add(table.Rows[r]);
                }
                table.Rows = kept;
                table.Bold = bold;
            }
        }

        private static TableBlock Two(string caption) => new TableBlock
        {
            Caption = caption,
            Header = new List<string> { "", "" },
            Align = new List<TextAlign> { TextAlign.Left, TextAlign.Left }
        };

        // ── The model ──────────────────────────────────────────────────────────────────────────

        private static TableBlock Summary(Model model, ModelSet models)
        {
            var table = Two("The model");
            table.Header = new List<string> { "Item", "Value" };

            void Add(string item, string value) => table.Rows.Add(new List<string> { item, value });

            Add("Nodes", model.Nodes.Count.ToString(Invariant));

            var byType = model.Elements.GroupBy(e => e.GetType().Name).OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key}");
            Add("Elements", $"{model.Elements.Count}" + (model.Elements.Count > 0 ? $" ({string.Join(", ", byType)})" : ""));
            Add("Supports", model.Supports.Count.ToString(Invariant));
            Add("Load patterns", model.LoadPatterns.Count.ToString(Invariant));

            double? mass = MassOf(model);
            if (mass.HasValue)
                Add("Mass", $"{mass.Value.ToString("N0", Invariant)} kg");

            if (model.Nodes.Count > 0)
            {
                var box = new Rhino.Geometry.BoundingBox(model.Nodes.Select(n => n.Pos));
                var d = box.Diagonal;
                Add("Extent (X × Y × Z)", $"{F(d.X, 2)} × {F(d.Y, 2)} × {F(d.Z, 2)} {Units.Length}");
            }

            var analyses = new List<string>();
            foreach (var m in models.All)
            {
                if (m.IsBuckling) analyses.Add($"linear buckling ({m.BucklingFactors?.Count ?? 0} modes)");
                else if (m.IsModal) analyses.Add($"natural vibration ({m.Eigenvalues?.Count ?? m.NumberOfModes} modes)");
                else if (m.IsAnalysed) analyses.Add("static");
            }
            if (analyses.Count > 0)
                Add("Analyses", string.Join(", ", analyses.Distinct()));

            return table;
        }

        /// <summary>The model's mass in kg, or null where it cannot be added up.</summary>
        public static double? MassOf(Model model)
        {
            try { return model.TotalMass; }
            catch { return null; }
        }

        private static double Cm => ModelLength.MillimetresPerUnit / 10.0;

        private static TableBlock Sections(Model model, out string problem)
        {
            problem = null;
            var beams = model.Elements.OfType<IBeam>().Where(b => b.Section != null).ToList();
            if (beams.Count == 0) { problem = "the model has no beams, so no beam sections"; return null; }

            var table = new TableBlock
            {
                Caption = "Beam sections",
                Header = new List<string> { "Section", "Material", "Elements", $"Length [{Units.Length}]", "A [cm²]", "Iy [cm4]", "Iz [cm4]", "J [cm4]" },
                Align = new List<TextAlign> { TextAlign.Left, TextAlign.Left, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right }
            };

            double c2 = Cm * Cm, c4 = c2 * c2;
            foreach (var group in beams.GroupBy(b => b.Section.SectionName ?? "").OrderBy(g => g.Key))
            {
                var section = group.First().Section;
                double length = group.Sum(b => b.Curve?.GetLength() ?? 0.0);
                table.Rows.Add(new List<string>
                {
                    string.IsNullOrEmpty(group.Key) ? "(unnamed)" : group.Key,
                    section.Material?.MatName ?? "",
                    group.Count().ToString(Invariant),
                    F(length, 2),
                    F(section.Area * c2, 2),
                    Significant(section.Iyy * c4),
                    Significant(section.Izz * c4),
                    Significant(section.J * c4),
                });
            }

            return table;
        }

        private static TableBlock Materials(Model model, out string problem)
        {
            problem = null;
            var materials = model.Elements.OfType<IBeam>().Select(b => b.Section?.Material).Where(m => m != null)
                .GroupBy(m => m.MatName ?? "").Select(g => g.First()).ToList();
            if (materials.Count == 0) { problem = "the model has no beams, so no beam materials"; return null; }

            var table = new TableBlock
            {
                Caption = "Beam materials",
                Header = new List<string> { "Material", "E [MPa]", "G [MPa]", "ν", "ρ [kg/m³]" },
                Align = new List<TextAlign> { TextAlign.Left, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right }
            };

            foreach (var m in materials.OrderBy(m => m.MatName))
            {
                table.Rows.Add(new List<string>
                {
                    m.MatName ?? "",
                    ModelStress.ToMPa(m.E).ToString("N0", Invariant),
                    ModelStress.ToMPa(m.G).ToString("N0", Invariant),
                    F(m.Nu, 2),
                    m.Rho.HasValue ? m.Rho.Value.ToString("N0", Invariant) : "",
                });
            }

            return table;
        }

        private static TableBlock Supports(Model model, out string problem)
        {
            problem = null;
            if (model.Supports.Count == 0) { problem = "the model has no supports"; return null; }

            var table = new TableBlock
            {
                Caption = "Supports",
                Header = new List<string> { "Node", $"X [{Units.Length}]", $"Y [{Units.Length}]", $"Z [{Units.Length}]", "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" },
                Align = new List<TextAlign> { TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right,
                    TextAlign.Centre, TextAlign.Centre, TextAlign.Centre, TextAlign.Centre, TextAlign.Centre, TextAlign.Centre }
            };

            string Held(bool held) => held ? "fixed" : "free";
            foreach (var s in model.Supports)
            {
                table.Rows.Add(new List<string>
                {
                    s.Id?.ToString(Invariant) ?? "", F(s.Pos.X, 2), F(s.Pos.Y, 2), F(s.Pos.Z, 2),
                    Held(s.Tx), Held(s.Ty), Held(s.Tz), Held(s.Rx), Held(s.Ry), Held(s.Rz)
                });
            }

            return table;
        }

        // ── A static analysis ──────────────────────────────────────────────────────────────────

        /// <summary>The step a static model's results are read at: its last, where the loads are all on.</summary>
        public static int LastStep(Model model) => Math.Max(0, Read.StepCount(model) - 1);

        /// <summary>
        /// Why <paramref name="model"/> can have none of a result Model View draws, by the result's
        /// name - "the model has no shells" for "Shell forces" - or null when it can: a beam result
        /// needs beams, a shell result shells, a brick result solid elements.
        /// </summary>
        public static string Lacks(Model model, string result)
        {
            string name = (result ?? "").ToLowerInvariant();
            if (name.Contains("beam") && (model.Beams?.Count ?? 0) == 0) return "the model has no beams";
            if (name.Contains("shell") && (model.Shells?.Count ?? 0) == 0) return "the model has no shells";
            if ((name.Contains("brick") || name.Contains("solid")) && (model.Bricks?.Count ?? 0) == 0) return "the model has no solid elements";
            return null;
        }

        private static TableBlock Displacements(Model model, out string problem)
        {
            problem = null;
            if (!model.IsAnalysed) { problem = "the model is not analysed"; return null; }

            int step = LastStep(model);
            var moves = Read.NodalOutput(model, step, ResultType.DISPLACEMENT).ToList();
            double mm = ModelLength.MillimetresPerUnit;
            string Id(int i) => i >= 0 && i < model.Nodes.Count ? model.Nodes[i].Id?.ToString(Invariant) ?? "" : "";

            var table = new TableBlock
            {
                Caption = "Displacements",
                Header = new List<string> { "Direction", "Largest [mm]", "Node", "Smallest [mm]", "Node" },
                Align = new List<TextAlign> { TextAlign.Left, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right },
                Note = Read.StepCount(model) > 1 ? $"At step {step}, the last the analysis recorded." : ""
            };

            var directions = new (string Name, Func<Rhino.Geometry.Vector3d, double> Of)[]
            {
                ("Ux", v => v.X), ("Uy", v => v.Y), ("Uz", v => v.Z)
            };
            foreach (var (direction, of) in directions)
            {
                int high = Enumerable.Range(0, moves.Count).OrderByDescending(i => of(moves[i])).First();
                int low = Enumerable.Range(0, moves.Count).OrderBy(i => of(moves[i])).First();
                table.Rows.Add(new List<string> { direction, F(of(moves[high]) * mm, 2), Id(high), F(of(moves[low]) * mm, 2), Id(low) });
            }

            int most = Enumerable.Range(0, moves.Count).OrderByDescending(i => moves[i].Length).First();
            table.Rows.Add(new List<string> { "|U|", F(moves[most].Length * mm, 2), Id(most), "", "" });

            return table;
        }

        /// <summary>The largest displacement of a static model, in mm, and the node it is at.</summary>
        public static (double Millimetres, int Node) LargestDisplacement(Model model)
        {
            var moves = Read.NodalOutput(model, LastStep(model), ResultType.DISPLACEMENT).ToList();
            int most = Enumerable.Range(0, moves.Count).OrderByDescending(i => moves[i].Length).First();
            return (moves[most].Length * ModelLength.MillimetresPerUnit, model.Nodes[most].Id ?? most);
        }

        private static TableBlock Reactions(Model model, out string problem)
        {
            problem = null;
            if (!model.IsAnalysed) { problem = "the model is not analysed"; return null; }
            if (model.Supports.Count == 0) { problem = "the model has no supports"; return null; }

            int step = LastStep(model);
            // A skewed support's reaction is on a node of its own beside it; see ResultField.Reaction.
            var nodes = model.Supports.Select(s => s.AuxiliaryNodeId ?? s.Id).ToList();
            var forces = Read.NodalOutput(model, step, ResultType.REACTION_FORCE, nodes).ToList();
            bool hasMoments = model.Supports.Any(s => s.ndf > 3);
            var moments = hasMoments ? Read.NodalOutput(model, step, ResultType.REACTION_MOMENT, nodes).ToList() : null;

            string force = Units.Force.ToString(), moment = $"{Units.Force}{Units.Length}";
            var table = new TableBlock
            {
                Caption = "Support reactions",
                Header = new List<string> { "Node", $"Fx [{force}]", $"Fy [{force}]", $"Fz [{force}]" },
                Align = new List<TextAlign> { TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right },
                Note = "In the global axes." + (Read.StepCount(model) > 1 ? $" At step {step}, the last the analysis recorded." : "")
            };
            if (hasMoments)
            {
                table.Header.AddRange(new[] { $"Mx [{moment}]", $"My [{moment}]", $"Mz [{moment}]" });
                table.Align.AddRange(new[] { TextAlign.Right, TextAlign.Right, TextAlign.Right });
            }

            var sum = new double[6];
            for (int i = 0; i < model.Supports.Count && i < forces.Count; i++)
            {
                var f = forces[i];
                var row = new List<string> { model.Supports[i].Id?.ToString(Invariant) ?? "", F(f.X, 2), F(f.Y, 2), F(f.Z, 2) };
                sum[0] += f.X; sum[1] += f.Y; sum[2] += f.Z;
                if (hasMoments && i < moments.Count)
                {
                    var m = moments[i];
                    row.AddRange(new[] { F(m.X, 2), F(m.Y, 2), F(m.Z, 2) });
                    sum[3] += m.X; sum[4] += m.Y; sum[5] += m.Z;
                }
                table.Rows.Add(row);
            }

            var total = new List<string> { "Sum", F(sum[0], 2), F(sum[1], 2), F(sum[2], 2) };
            if (hasMoments) total.AddRange(new[] { "", "", "" });
            table.Bold.Add(table.Rows.Count);
            table.Rows.Add(total);

            return table;
        }

        // ── Loads and combinations ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Each load pattern of the model as it was assembled - the one a combination was made from,
        /// for a combination - with its factor, what loads it has, and the force they add up to, its
        /// factor on: the total of all of them is what the supports carry when they act together.
        /// </summary>
        private static TableBlock Patterns(Model model, out string problem)
        {
            problem = null;
            var source = model.Combination?.Source ?? model;
            var patterns = source.LoadPatterns ?? new List<Alpaca4d.Loads.LoadPattern>();
            if (patterns.Count == 0) { problem = "the model has no load patterns"; return null; }

            string force = Units.Force.ToString();
            var table = new TableBlock
            {
                Caption = "Load patterns",
                Header = new List<string> { "Pattern", "Factor", "Loads", $"Fx [{force}]", $"Fy [{force}]", $"Fz [{force}]" },
                Align = new List<TextAlign> { TextAlign.Left, TextAlign.Right, TextAlign.Left, TextAlign.Right, TextAlign.Right, TextAlign.Right },
            };

            var total = new Rhino.Geometry.Vector3d();
            int unsummed = 0;
            for (int i = 0; i < patterns.Count; i++)
            {
                var pattern = patterns[i];
                string name = string.IsNullOrWhiteSpace(pattern.Name) ? $"Pattern {i + 1}" : pattern.Name.Trim();
                if (pattern.PatternType != Alpaca4d.Loads.PatternType.Plain)
                {
                    table.Rows.Add(new List<string> { name, F(pattern.Factor, 2), "Ground motion", "", "", "" });
                    continue;
                }
                var sum = Resultant(source, pattern.Load ?? new List<ILoad>(), ref unsummed) * pattern.Factor;
                total += sum;
                table.Rows.Add(new List<string> { name, F(pattern.Factor, 2), Kinds(pattern.Load), F(sum.X, 2), F(sum.Y, 2), F(sum.Z, 2) });
            }
            table.Bold.Add(table.Rows.Count);
            table.Rows.Add(new List<string> { "Total", "", "", F(total.X, 2), F(total.Y, 2), F(total.Z, 2) });

            table.Note = "The forces each pattern's loads add up to, in the global axes, its factor on; self-weight from the sections' density." +
                         (unsummed > 0 ? $" {unsummed} loads in the shells' own axes are not in the sums." : "");
            return table;
        }

        /// <summary>What a pattern's loads are: "4 point, 12 line, self-weight".</summary>
        private static string Kinds(IEnumerable<ILoad> loads)
        {
            var list = (loads ?? Enumerable.Empty<ILoad>()).Where(l => l != null).ToList();
            var parts = new List<string>();
            void Count(int n, string what) { if (n > 0) parts.Add($"{n} {what}"); }
            Count(list.Count(l => l is Alpaca4d.Loads.PointLoad), "point");
            Count(list.Count(l => l is Alpaca4d.Loads.LineLoad), "line");
            Count(list.Count(l => l is Alpaca4d.Loads.MeshLoad), "area");
            if (list.Any(l => l is Alpaca4d.Loads.Gravity)) parts.Add("self-weight");
            int other = list.Count(l => !(l is Alpaca4d.Loads.PointLoad || l is Alpaca4d.Loads.LineLoad || l is Alpaca4d.Loads.MeshLoad || l is Alpaca4d.Loads.Gravity));
            Count(other, "other");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        /// <summary>
        /// The force <paramref name="loads"/> add up to, as Assemble puts them on the model: a line load
        /// on each beam its filter names, times the beam's length; an area load on each shell, times its
        /// area; self-weight as the model's sections weigh. A load on a shell in its own axes is counted
        /// in <paramref name="unsummed"/> instead.
        /// </summary>
        private static Rhino.Geometry.Vector3d Resultant(Model model, IEnumerable<ILoad> loads, ref int unsummed)
        {
            var sum = new Rhino.Geometry.Vector3d();
            foreach (var load in loads)
            {
                switch (load)
                {
                    case Alpaca4d.Loads.PointLoad point:
                        sum += point.Force;
                        break;
                    case Alpaca4d.Loads.LineLoad line:
                        foreach (var beam in line.Element != null ? new List<IBeam> { line.Element } : line.AppliedTo(model.Beams))
                        {
                            if (beam?.Curve == null) continue;
                            double length = beam.Curve.PointAtStart.DistanceTo(beam.Curve.PointAtEnd);
                            var w = line.GlobalForce;
                            if (line.IsLocal && beam.GeomTransf != null)
                            {
                                // Back from the axes GlobalToLocal turns a force into.
                                var plane = new Rhino.Geometry.Plane(Rhino.Geometry.Point3d.Origin, beam.GeomTransf.LocalZ, beam.GeomTransf.LocalY);
                                w = plane.PointAt(line.LocalForce.X, line.LocalForce.Y, line.LocalForce.Z) - Rhino.Geometry.Point3d.Origin;
                            }
                            sum += w * length;
                        }
                        break;
                    case Alpaca4d.Loads.MeshLoad mesh:
                        foreach (var shell in mesh.Element != null ? new List<IShell> { mesh.Element } : mesh.AppliedTo(model.Shells))
                        {
                            if (mesh.IsLocal) { unsummed++; break; }
                            sum += mesh.GlobalForce * Alpaca4d.Utils.MeshArea(shell.Mesh);
                        }
                        break;
                    case Alpaca4d.Loads.Gravity gravity:
                        sum += Resultant(model, model.CreateGravityLoad(gravity), ref unsummed);
                        break;
                }
            }
            return sum;
        }

        /// <summary>The load combinations among the models given, each once, in the order given.</summary>
        private static List<Alpaca4d.Loads.LoadCombination> CombinationsOf(ModelSet models) =>
            models.All.Where(m => m.Combination != null).Select(m => m.Combination)
                  .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        private static TableBlock Combinations(ModelSet models, out string problem)
        {
            problem = null;
            var combinations = CombinationsOf(models);
            if (combinations.Count == 0) { problem = "the AlpacaModels hold no load combinations: add them with Load Combination, and analyse the model"; return null; }

            var table = new TableBlock
            {
                Caption = "Load combinations",
                Header = new List<string> { "Combination", "Type", "Load patterns and factors" },
                Align = new List<TextAlign> { TextAlign.Left, TextAlign.Left, TextAlign.Left },
            };
            foreach (var c in combinations)
                table.Rows.Add(new List<string> { c.Name, Alpaca4d.Loads.LoadCombination.NameOf(c.Kind), c.Formula });
            return table;
        }

        /// <summary>The static models given, each a load case or a combination: not a pushover, nor a time history.</summary>
        private static List<Model> StaticModels(ModelSet models) =>
            models.All.Where(m => ModelSet.KindOf(m) == "static" && !m.IsTransient).ToList();

        /// <summary>What a static model is called in a table: its combination's name, or its place among the models given.</summary>
        private static string Called(ModelSet models, Model model) =>
            model.Combination?.Name ?? $"Model {IndexOf(models, model)}";

        private static int IndexOf(ModelSet models, Model model)
        {
            for (int i = 0; i < models.All.Count; i++)
                if (ReferenceEquals(models.All[i], model)) return i;
            return -1;
        }

        private static TableBlock CombinationResults(ModelSet models, out string problem)
        {
            problem = null;
            var statics = StaticModels(models);
            if (statics.Count == 0) { problem = "none of the AlpacaModels is a static model: connect the ones Run Analysis hands on"; return null; }
            // In the order given, with the combinations that did not solve in their place.
            var rows = models.All.Where(m => statics.Contains(m) || m.CombinationFailure != null).ToList();

            string force = Units.Force.ToString();
            var table = new TableBlock
            {
                Caption = statics.Any(m => m.Combination != null) ? "Results by combination" : "Results by model",
                Header = new List<string> { "Combination", "Type", "Largest |U| [mm]", "Node", $"Sum Fx [{force}]", $"Sum Fy [{force}]", $"Sum Fz [{force}]" },
                Align = new List<TextAlign> { TextAlign.Left, TextAlign.Left, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right },
                Note = "The largest displacement of any node, and the sums of the support reactions in the global axes."
            };
            foreach (var model in rows)
            {
                if (model.CombinationFailure != null)
                {
                    table.Rows.Add(new List<string> { Called(models, model), Alpaca4d.Loads.LoadCombination.NameOf(model.Combination.Kind), "-", "", "-", "-", "-" });
                    table.Note += $" {Called(models, model)} has no results: {model.CombinationFailure}.";
                    continue;
                }
                var (mm, node) = LargestDisplacement(model);
                var sum = new Rhino.Geometry.Vector3d();
                if (model.Supports.Count > 0)
                {
                    var nodes = model.Supports.Select(s => s.AuxiliaryNodeId ?? s.Id).ToList();
                    foreach (var f in Read.NodalOutput(model, LastStep(model), ResultType.REACTION_FORCE, nodes)) sum += f;
                }
                table.Rows.Add(new List<string>
                {
                    Called(models, model), model.Combination != null ? Alpaca4d.Loads.LoadCombination.NameOf(model.Combination.Kind) : "",
                    F(mm, 2), node.ToString(Invariant), F(sum.X, 2), F(sum.Y, 2), F(sum.Z, 2)
                });
            }
            return table;
        }

        // ── Design checks ──────────────────────────────────────────────────────────────────────

        /// <summary>A member at its worst: how it was checked and in which model.</summary>
        private sealed class Worst
        {
            public int Beam;
            public Alpaca4d.Design.MemberUtilisation Check;
            public Model Model;
        }

        /// <summary>
        /// The steel members checked to EN 1993-1-1 over the ultimate combinations given - ULS,
        /// accidental, seismic - or, with no combination given, over every static model: each member
        /// at its worst. <paramref name="sections"/> keeps each section's worst member alone.
        /// </summary>
        private static TableBlock Design(ModelSet models, Newtonsoft.Json.Linq.JObject options, bool sections, out string problem)
        {
            problem = null;
            var statics = StaticModels(models);
            if (statics.Count == 0) { problem = "none of the AlpacaModels is a static model: connect the ones Run Analysis hands on"; return null; }
            var ultimate = statics.Where(m => m.Combination != null && m.Combination.IsUltimate).ToList();
            var unsolved = models.All.Where(m => m.CombinationFailure != null && m.Combination.IsUltimate).ToList();
            var over = ultimate.Count > 0 ? ultimate : statics.Where(m => m.Combination == null).ToList();
            if (over.Count == 0) { problem = "none of the combinations is an ultimate one - ULS, accidental or seismic - to check the members against"; return null; }
            if (over.All(m => m.Beams.Count == 0)) { problem = "the model has no beams to check"; return null; }

            var settings = new Alpaca4d.Design.DesignSettings { Report = false };
            if (options != null)
            {
                if (options["gammaM0"] != null) settings.GammaM0 = (double)options["gammaM0"];
                if (options["gammaM1"] != null) settings.GammaM1 = (double)options["gammaM1"];
                if (string.Equals((string)options["fabrication"], "Welded", StringComparison.OrdinalIgnoreCase))
                    settings.Fabrication = Alpaca4d.Design.Fabrication.Welded;
            }

            var worst = new Dictionary<int, Worst>();
            var notChecked = new Dictionary<int, string>();
            foreach (var model in over)
            {
                // Buckling lengths worked out from the model, as Utilisation takes them when none are given.
                var members = Alpaca4d.Design.BucklingLengths.Of(model).SteelMembers(model);
                var checks = Alpaca4d.Design.Utilisations.Of(model, LastStep(model), members, settings);
                for (int i = 0; i < checks.Count; i++)
                {
                    var check = checks[i];
                    if (!check.Checked || check.Max == null) { if (!worst.ContainsKey(i)) notChecked[i] = check.Type; continue; }
                    notChecked.Remove(i);
                    if (!worst.TryGetValue(i, out var was) || check.Max > was.Check.Max)
                        worst[i] = new Worst { Beam = i, Check = check, Model = model };
                }
            }
            if (worst.Count == 0)
            {
                problem = "none of the beams is steel with a grade to check: give each a steel material, S235 to S460, as Material Library Elastic names them";
                return null;
            }

            var beams = over[0].Beams;
            string Id(int i) => beams[i].Id?.ToString(Invariant) ?? (i + 1).ToString(Invariant);
            string Section(int i) => beams[i].Section?.SectionName ?? "";
            string Grade(int i) => Alpaca4d.Material.DesignGrades.Of(beams[i].Section?.Material)?.Name ?? beams[i].Section?.Material?.MatName ?? "";
            // Not OK while an ultimate combination has no results: the members are not proven under it.
            string Result(double u) => u > 1.0 ? "Fails" : unsolved.Count > 0 ? "Incomplete" : "OK";
            // The check, as EN 1993-1-1 names it.
            string Clause(string check)
            {
                switch (check)
                {
                    case "Axial": return "Axial, 6.2.3-4";
                    case "ShearY": return "Shear y, 6.2.6";
                    case "ShearZ": return "Shear z, 6.2.6";
                    case "Torsion": return "Torsion, 6.2.7";
                    case "BendingY": return "Bending y, 6.2.5";
                    case "BendingZ": return "Bending z, 6.2.5";
                    case "Combined": return "Combined, 6.2.9";
                    case "BucklingY": return "Buckling y, 6.3.1";
                    case "BucklingZ": return "Buckling z, 6.3.1";
                    case "LTB": return "Lateral-torsional, 6.3.2";
                    case "InteractionY": return "Interaction y, 6.3.3";
                    case "InteractionZ": return "Interaction z, 6.3.3";
                    default: return check;
                }
            }

            var rows = worst.Values.OrderByDescending(w => w.Check.Max).ToList();
            var table = new TableBlock
            {
                Caption = sections ? "Steel members: the worst of each section" : "Steel members",
                Align = new List<TextAlign> { TextAlign.Left, TextAlign.Left, TextAlign.Left, TextAlign.Right, TextAlign.Left, TextAlign.Right, TextAlign.Left, TextAlign.Left }
            };
            if (sections)
            {
                table.Header = new List<string> { "Section", "Grade", "Members", "Element", "Governing", "Utilisation", "Combination", "Result" };
                table.Align[2] = TextAlign.Right;
                foreach (var group in rows.GroupBy(w => Section(w.Beam)).OrderByDescending(g => g.First().Check.Max))
                {
                    var w = group.First();
                    double u = w.Check.Max.Value;
                    table.Rows.Add(new List<string> { group.Key, Grade(w.Beam), group.Count().ToString(Invariant), Id(w.Beam), Clause(w.Check.Governing), F(u, 2), Called(models, w.Model), Result(u) });
                }
            }
            else
            {
                table.Header = new List<string> { "Element", "Section", "Grade", "Class", "Governing", "Utilisation", "Combination", "Result" };
                table.Align[0] = TextAlign.Right;
                table.Align[3] = TextAlign.Right;
                foreach (var w in rows)
                {
                    double u = w.Check.Max.Value;
                    table.Rows.Add(new List<string> { Id(w.Beam), Section(w.Beam), Grade(w.Beam), w.Check.Class?.ToString(Invariant) ?? "", Clause(w.Check.Governing), F(u, 2), Called(models, w.Model), Result(u) });
                }
            }

            int fails = rows.Count(w => w.Check.Max > 1.0);
            var note = new List<string>
            {
                unsolved.Count > 0 ? $"The check is incomplete: {string.Join(", ", unsolved.Select(m => m.Combination.Name))} {(unsolved.Count == 1 ? "has" : "have")} no results ({unsolved[0].CombinationFailure})." : "",
                fails == 0 ? (unsolved.Count > 0 ? $"No member fails under the other combinations." : $"All {rows.Count} steel members pass.") : $"{fails} of {rows.Count} steel members fail.",
                $"EN 1993-1-1, γM0 = {F(settings.GammaM0, 2)}, γM1 = {F(settings.GammaM1, 2)}, {settings.Fabrication.ToString().ToLowerInvariant()} sections;",
                "each member held at its ends - buckling lengths node to node, loads at the shear centre, no sway - as Utilisation takes it unless told otherwise.",
                ultimate.Count > 0 ? $"Over {string.Join(", ", ultimate.Select(m => m.Combination.Name))}." : over.Count > 1 ? $"Over the {over.Count} static models given." : ""
            };
            if (notChecked.Count > 0) note.Add($"{notChecked.Count} members are not steel with a grade, and are not checked.");
            table.Note = string.Join(" ", note.Where(n => n.Length > 0));
            return table;
        }

        // ── What the report was made with ──────────────────────────────────────────────────────

        private static TableBlock About(ModelSet models, out string problem)
        {
            problem = null;
            var version = typeof(Model).Assembly.GetName().Version;
            var table = new TableBlock
            {
                Caption = "About this calculation",
                Header = new List<string> { "", "" },
                Align = new List<TextAlign> { TextAlign.Left, TextAlign.Left },
            };
            table.Rows.Add(new List<string> { "Software", $"Alpaca4d {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}" });
            var solver = models.All.Select(m => m.SolverVersion).FirstOrDefault(v => !string.IsNullOrEmpty(v));
            table.Rows.Add(new List<string> { "Solver", solver ?? "OpenSees" });
            table.Rows.Add(new List<string> { "Units", $"Forces in {Units.Force}, lengths in {Units.Length}, stresses in MPa, displacements in mm" });
            table.Rows.Add(new List<string> { "Models", $"{models.All.Count} given; how each was solved is in the table of analyses" });
            table.Rows.Add(new List<string> { "Written", DateTime.Now.ToString("yyyy-MM-dd HH:mm", Invariant) });
            return table;
        }

        /// <summary>
        /// Each model given and how it was solved: what analysis, of which combination, and what
        /// OpenSees was asked to do - its integrator, steps, algorithm, convergence test and solver.
        /// </summary>
        private static TableBlock Analyses(ModelSet models, out string problem)
        {
            problem = null;
            if (models.All.Count == 0) { problem = "no AlpacaModel was given"; return null; }
            var table = new TableBlock
            {
                Caption = "The analyses",
                Header = new List<string> { "Model", "Analysis", "Combination", "Integrator", "Steps", "Algorithm", "Test", "System" },
                Align = new List<TextAlign> { TextAlign.Right, TextAlign.Left, TextAlign.Left, TextAlign.Left, TextAlign.Right, TextAlign.Left, TextAlign.Left, TextAlign.Left },
                Note = "By the names OpenSees gives its commands. Natural vibration and linear buckling solve their eigenvalues on their own."
            };
            for (int i = 0; i < models.All.Count; i++)
            {
                var model = models.All[i];
                string analysis;
                switch (ModelSet.KindOf(model))
                {
                    case "model": analysis = "Not analysed"; break;
                    case "failed": analysis = "Static, no results: " + model.CombinationFailure; break;
                    case "vibration": analysis = $"Natural vibration, {model.NumberOfModes} modes"; break;
                    case "buckling": analysis = $"Linear buckling, {model.BucklingFactors?.Count ?? 0} modes"; break;
                    case "pushover": analysis = "Pushover"; break;
                    case "footfall": analysis = "Footfall, SCI P354"; break;
                    default: analysis = model.IsTransient ? "Time history" : "Static"; break;
                }
                var s = model.Settings;
                string Of(object type) => type?.ToString() ?? "";
                table.Rows.Add(new List<string>
                {
                    i.ToString(Invariant), analysis, model.Combination?.Name ?? "",
                    Of(s?.Integrator?.Type), s?.AnalysisStep != null ? s.AnalysisStep.NumIncr.ToString(Invariant) : "",
                    Of(s?.Algorithm?.Type), Of(s?.Test?.Type), Of(s?.SystemEquation?.Type)
                });
            }
            return table;
        }

        // ── The tables of what a picture draws ─────────────────────────────────────────────────

        /// <summary>
        /// The tables of the results a picture draws, from their values as it draws them: the
        /// reactions as a row for each support, with a column for each component drawn and the sum of
        /// the forces; any other result as a row for each component drawn, with its largest and its
        /// smallest value and where each is. A table to a result, in the order they come.
        /// </summary>
        public static List<TableBlock> OfValues(IEnumerable<ResultValues> values, string note = "")
        {
            var tables = new List<TableBlock>();
            foreach (var result in (values ?? Enumerable.Empty<ResultValues>()).Where(v => v != null).GroupBy(v => v.Result))
            {
                var parts = result.ToList();
                string extra = string.Join(" ", parts.Select(p => p.Note).Where(n => !string.IsNullOrEmpty(n)).Distinct());
                string said = string.Join(" ", new[] { extra, note }.Where(n => !string.IsNullOrEmpty(n)));
                tables.Add(parts.All(p => p.At == ResultValues.AtSupport) ? BySupport(result.Key, parts, said) : Extremes(result.Key, parts, said));
            }
            return tables;
        }

        private static string Header(ResultValues part) => string.IsNullOrEmpty(part.Unit) ? part.Component : $"{part.Component} [{part.Unit}]";

        /// <summary>A row for each support, a column for each component, the forces summed under them.</summary>
        private static TableBlock BySupport(string result, List<ResultValues> parts, string note)
        {
            var table = new TableBlock
            {
                Caption = result == "Reactions" ? "Support reactions" : result,
                Header = new List<string> { "Support" },
                Align = new List<TextAlign> { TextAlign.Right },
                Note = note
            };
            foreach (var part in parts)
            {
                table.Header.Add(Header(part));
                table.Align.Add(TextAlign.Right);
            }

            int rows = parts.Max(p => p.Values.Count);
            // The rounding left in a force that is 0 - 5E-17 beside 0.29 kN - read as the 0 it is.
            var scales = parts.Select(p => p.Values.Count == 0 ? 0.0 : p.Values.Max(v => Math.Abs(v.Value))).ToList();
            for (int i = 0; i < rows; i++)
            {
                var row = new List<string> { parts.Select(p => i < p.Values.Count ? p.Values[i].Key : null).FirstOrDefault(k => k != null) ?? "" };
                row.AddRange(parts.Select((p, k) => i >= p.Values.Count ? ""
                    : ReportLayout.FormatValue(Math.Abs(p.Values[i].Value) <= 1e-9 * scales[k] ? 0.0 : p.Values[i].Value)));
                table.Rows.Add(row);
            }

            if (parts.Any(p => p.Summed))
            {
                var total = new List<string> { "Sum" };
                total.AddRange(parts.Select(p => p.Summed ? ReportLayout.FormatValue(Sum(p.Values.Select(v => v.Value))) : ""));
                table.Bold.Add(table.Rows.Count);
                table.Rows.Add(total);
            }
            return table;
        }

        /// <summary>A column's sum, the rounding left over from forces that balance - 6E-17 - read as the 0 it is.</summary>
        private static double Sum(IEnumerable<double> values)
        {
            var list = values.ToList();
            double sum = list.Sum(), scale = list.Count == 0 ? 0.0 : list.Max(v => Math.Abs(v));
            return Math.Abs(sum) <= 1e-9 * scale ? 0.0 : sum;
        }

        /// <summary>A row for each component: its largest and its smallest value, and the node or element each is at.</summary>
        private static TableBlock Extremes(string result, List<ResultValues> parts, string note)
        {
            string at = parts.Select(p => p.At).Distinct().Count() == 1 ? parts[0].At : "At";
            var table = new TableBlock
            {
                Caption = result,
                Header = new List<string> { "Component", "Largest", at, "Smallest", at },
                Align = new List<TextAlign> { TextAlign.Left, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right },
                Note = note
            };
            foreach (var part in parts)
            {
                if (part.Values.Count == 0)
                {
                    table.Rows.Add(new List<string> { Header(part), "-", "", "-", "" });
                    continue;
                }
                var high = part.Values.Aggregate((a, b) => b.Value > a.Value ? b : a);
                var low = part.Values.Aggregate((a, b) => b.Value < a.Value ? b : a);
                table.Rows.Add(new List<string> { Header(part), ReportLayout.FormatValue(high.Value), high.Key, ReportLayout.FormatValue(low.Value), low.Key });
            }
            return table;
        }

        // ── Modes ──────────────────────────────────────────────────────────────────────────────

        /// <summary>The modes a table or a repeat goes over, from 1: those asked for, out of those there are.</summary>
        private static IEnumerable<int> Picked(int count, IList<int> modes) =>
            modes == null || modes.Count == 0 ? Enumerable.Range(1, count) : modes.Where(m => m >= 1 && m <= count);

        private static TableBlock Modes(Model model, IList<int> modes, out string problem)
        {
            problem = null;
            var eigen = model.Eigenvalues;
            if (!model.IsModal || eigen == null || eigen.Count == 0) { problem = "the model holds no natural vibration modes"; return null; }

            var table = new TableBlock
            {
                Caption = "Natural frequencies",
                Header = new List<string> { "Mode", "Eigenvalue ω² [rad²/s²]", "ω [rad/s]", "f [Hz]", "T [s]" },
                Align = new List<TextAlign> { TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right }
            };

            foreach (int mode in Picked(eigen.Count, modes))
            {
                double lambda = eigen[mode - 1];
                double omega = Math.Sqrt(Math.Max(0.0, lambda));
                double f = omega / (2.0 * Math.PI);
                table.Rows.Add(new List<string>
                {
                    mode.ToString(Invariant), Significant(lambda, 5), F(omega, 3), F(f, 3), f > 0.0 ? F(1.0 / f, 3) : "∞"
                });
            }

            return table;
        }

        private static TableBlock MassRatios(Model model, IList<int> modes, out string problem)
        {
            problem = null;
            if (!model.IsModal) { problem = "the model holds no natural vibration modes"; return null; }
            string file = model.ModalAnalysisReportFile;
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) { problem = "the modal report Natural Vibration writes was not found"; return null; }

            var sections = Alpaca4d.Eigen.ReportSections(File.ReadAllLines(file));
            var share = Numbers(sections[Array.IndexOf(Alpaca4d.Eigen.ReportSectionNumbers, 9)]);
            var running = Numbers(sections[Array.IndexOf(Alpaca4d.Eigen.ReportSectionNumbers, 10)]);
            if (share.Count == 0) { problem = "the modal report holds no participating masses"; return null; }

            var table = new TableBlock
            {
                Caption = "Participating mass",
                Header = new List<string> { "Mode", "Ux [%]", "Uy [%]", "Uz [%]", "ΣUx [%]", "ΣUy [%]", "ΣUz [%]" },
                Align = Enumerable.Repeat(TextAlign.Right, 7).ToList(),
                Note = "As a percentage of the mass free to move, per mode and summed over the modes up to it."
            };

            foreach (int mode in Picked(share.Count, modes))
            {
                var s = share[mode - 1];
                var r = mode - 1 < running.Count ? running[mode - 1] : null;
                string At(double[] row, int i) => row != null && i < row.Length ? F(row[i], 1) : "";
                table.Rows.Add(new List<string> { mode.ToString(Invariant), At(s, 1), At(s, 2), At(s, 3), At(r, 1), At(r, 2), At(r, 3) });
            }

            return table;
        }

        /// <summary>The rows of numbers in a section of the modal report, its comment lines left out.</summary>
        private static List<double[]> Numbers(IEnumerable<string> lines)
        {
            var rows = new List<double[]>();
            foreach (var line in lines)
            {
                var text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#") || text.StartsWith("*")) continue;
                var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var values = new double[parts.Length];
                bool ok = true;
                for (int i = 0; i < parts.Length && ok; i++)
                    ok = double.TryParse(parts[i], NumberStyles.Float, Invariant, out values[i]);
                if (ok) rows.Add(values);
            }
            return rows;
        }

        private static TableBlock BucklingFactors(Model model, IList<int> modes, out string problem)
        {
            problem = null;
            var factors = model.BucklingFactors;
            if (!model.IsBuckling || factors == null || factors.Count == 0) { problem = "the model holds no buckling modes"; return null; }

            var table = new TableBlock
            {
                Caption = "Buckling load factors",
                Header = new List<string> { "Mode", "Load factor αcr" },
                Align = new List<TextAlign> { TextAlign.Right, TextAlign.Right },
                Note = "The model's loads times αcr make it buckle in that mode."
            };

            foreach (int mode in Picked(factors.Count, modes))
                table.Rows.Add(new List<string> { mode.ToString(Invariant), F(factors[mode - 1], 3) });

            return table;
        }

        /// <summary>Footfall's columns: the option that shows each, and its header.</summary>
        private static readonly (string Option, string Header)[] FootfallColumns =
        {
            ("Response factor R", "R"),
            ("Steady-state R", "R steady state"),
            ("Transient R", "R transient"),
            ("Steady-state acceleration", "a steady state [mm/s²]"),
            ("Transient acceleration", "a transient [mm/s²]"),
            ("Pace", "Pace [Hz]"),
        };

        /// <summary>
        /// The nodes that respond most, worst by <paramref name="by"/> first - R, unless it names another of
        /// <see cref="FootfallResponse.Quantities"/> - ten of them, or as many as "modes" says (a number,
        /// for once, is a count of nodes here). That quantity's column first, after the node, and then the
        /// others: R, its steady-state and transient parts, their accelerations and the pace.
        /// </summary>
        private static TableBlock FootfallTable(Model model, IList<int> count, string by, out string problem)
        {
            problem = null;
            var footfall = model.Footfall;
            if (footfall == null || footfall.Factors.Length == 0) { problem = "the model holds no footfall response: give the Report the model Footfall Analysis hands on"; return null; }

            string quantity = FootfallResponse.Quantity(by);
            if (quantity == null) { problem = $"footfall has no \"{by}\" to sort by: it has {string.Join(", ", FootfallResponse.Quantities)}"; return null; }
            var sortBy = footfall.Of(quantity);
            if (sortBy.Length == 0)
            {
                problem = FootfallResponse.IsTransient(quantity) && !footfall.TransientChecked
                    ? $"the transient response was not checked - not for a stair, nor for a floor above the cut-off - so there is no {quantity.ToLowerInvariant()}"
                    : $"the model holds no {quantity.ToLowerInvariant()}: run Footfall Analysis again for it";
                return null;
            }

            // The column sorted by first, then the others the model has.
            var columns = new List<(string Option, string Header, double[] Values)>();
            foreach (var (option, header) in FootfallColumns)
            {
                double[] values = option == "Pace" ? footfall.CriticalFrequencies : footfall.Of(option);
                if (values.Length == 0) continue;
                var column = (option, header, values);
                if (option == quantity) columns.Insert(0, column);
                else columns.Add(column);
            }

            int rows = count != null && count.Count > 0 ? Math.Max(1, count.Max()) : 10;
            var table = new TableBlock
            {
                Caption = quantity == FootfallResponse.Quantities[0] ? "Footfall response" : "Footfall response, by " + quantity.ToLowerInvariant(),
                Header = new List<string> { "Node" }.Concat(columns.Select(c => c.Header)).ToList(),
                Align = Enumerable.Repeat(TextAlign.Right, columns.Count + 1).ToList(),
                Note = $"After SCI P354: {footfall.Activity.ToLowerInvariant()}, {footfall.Damping * 100.0:0.#}% damping. R = 1 is the threshold of perception; " +
                       "a is the weighted rms acceleration, and R the larger of the steady state and the transient" +
                       (footfall.TransientChecked ? "" : ", which was not checked") + ". " +
                       $"The {Math.Min(rows, sortBy.Length)} of {sortBy.Length} nodes that respond most" +
                       (quantity == FootfallResponse.Quantities[0] ? "." : $", by {quantity.ToLowerInvariant()}.")
            };

            foreach (int i in Enumerable.Range(0, sortBy.Length).OrderByDescending(i => sortBy[i]).Take(rows))
            {
                var row = new List<string> { i < footfall.Nodes.Length ? footfall.Nodes[i].ToString(Invariant) : "" };
                row.AddRange(columns.Select(c => i < c.Values.Length ? F(c.Values[i], 2) : ""));
                table.Rows.Add(row);
            }
            return table;
        }

        /// <summary>A number in so many significant figures, without an exponent while it is not huge.</summary>
        private static string Significant(double value, int figures = 4)
        {
            double a = Math.Abs(value);
            if (a == 0.0) return "0";
            if (a >= 1e7 || a < 1e-4) return value.ToString("0.###E+0", Invariant);
            int decimals = Math.Max(0, figures - 1 - (int)Math.Floor(Math.Log10(a)));
            return value.ToString("N" + decimals, Invariant);
        }
    }
}
