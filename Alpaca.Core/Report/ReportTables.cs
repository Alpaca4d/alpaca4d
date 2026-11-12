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
            ["footfall.response"] = new[] { "Response factor R", "Steady-state R", "Transient R", "Steady-state acceleration", "Transient acceleration", "Pace", "Note below" },
        };

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private static string F(double value, int decimals) => value.ToString("F" + decimals, Invariant);

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
        public static TableBlock Make(string name, ModelSet models, object model, IList<int> modes, ICollection<string> shown, string by, out string problem)
        {
            var table = Whole(name, models, model, modes, by, out problem);
            if (table != null && shown != null)
                Filter((name ?? "").Trim(), table, new HashSet<string>(shown, StringComparer.OrdinalIgnoreCase));
            return table;
        }

        private static TableBlock Whole(string name, ModelSet models, object model, IList<int> modes, string by, out string problem)
        {
            problem = null;
            string key = (name ?? "").Trim().ToLowerInvariant();
            string kind = key.Contains(".") ? key.Substring(0, key.IndexOf('.')) : key;

            var source = models.Find(model ?? kind, out problem);
            if (source == null) return null;

            try
            {
                switch (key)
                {
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
            for (int i = 0; i < rows; i++)
            {
                var row = new List<string> { parts.Select(p => i < p.Values.Count ? p.Values[i].Key : null).FirstOrDefault(k => k != null) ?? "" };
                row.AddRange(parts.Select(p => i < p.Values.Count ? ReportLayout.FormatValue(p.Values[i].Value) : ""));
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
