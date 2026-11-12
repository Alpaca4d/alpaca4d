using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alpaca4d.Report
{
    /// <summary>
    /// What draws the pictures of a report: Model View, in the Grasshopper plugin, which this
    /// assembly knows nothing of. Kept behind this so that the rest of a report - the template,
    /// the tables, the pages - can be checked without Rhino.
    /// </summary>
    public interface IFigureMaker
    {
        /// <summary>
        /// A picture of <paramref name="model"/> drawn as <paramref name="figure"/> asks - the
        /// figure's settings from the template, its placeholders filled in and "step" set to the
        /// step or mode to draw, from 0 - with its file, its size in pixels and its legend; or
        /// null, and why it could not be drawn.
        /// </summary>
        FigureBlock Draw(Model model, JObject figure, out string problem);

        /// <summary>A picture file's size in pixels, or null if it cannot be read.</summary>
        Size? ImageSize(string path);

        /// <summary>
        /// The values of each result of <paramref name="figure"/> that asks for a table ("table": true),
        /// a component at a time, as the picture draws them; or null, and why they could not be read.
        /// </summary>
        IList<ResultValues> Values(Model model, JObject figure, out string problem);
    }

    /// <summary>
    /// The models a report is made of, found by what they are: the model as it was built, and the
    /// models a static analysis, a natural vibration analysis and a linear buckling analysis handed
    /// on - or by their place in the list.
    /// </summary>
    public sealed class ModelSet
    {
        public readonly IReadOnlyList<Model> All;

        public ModelSet(IEnumerable<Model> models)
        {
            All = (models ?? Enumerable.Empty<Model>()).Where(m => m != null).ToList();
        }

        /// <summary>The kinds a template can name a model by, and what else it may call them.</summary>
        public static readonly string[] Kinds = { "model", "static", "vibration", "buckling", "pushover", "footfall" };

        public static string KindOf(Model model) =>
            model.Footfall != null ? "footfall" : model.IsBuckling ? "buckling" : model.IsModal ? "vibration" : model.IsPushover ? "pushover" : model.IsAnalysed ? "static" : "model";

        private static string Kind(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "": case "model": case "assembled": case "structure": return "model";
                case "static": case "analysis": case "run analysis": case "static analysis": return "static";
                case "vibration": case "modal": case "eigen": case "natural vibration": case "frequencies": return "vibration";
                case "buckling": case "linear buckling": case "stability": return "buckling";
                case "pushover": case "pushover analysis": return "pushover";
                case "footfall": case "footfall analysis": return "footfall";
                default: return null;
            }
        }

        /// <summary>
        /// The model <paramref name="key"/> names: a kind (model, static, vibration, buckling) or a
        /// place in the list from 0. "model" is the model as it was built - one no analysis has run,
        /// or the one the first analysed model was built from. Null, and why, if there is none.
        /// </summary>
        public Model Find(object key, out string problem)
        {
            problem = null;
            if (All.Count == 0) { problem = "no AlpacaModel was given"; return null; }

            if (key is JValue value) key = value.Value;
            if (key is string digits && int.TryParse(digits.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int place)) key = place;
            if (key is long || key is int || key is double)
            {
                int index = Convert.ToInt32(key, CultureInfo.InvariantCulture);
                if (index >= 0 && index < All.Count) return All[index];
                problem = $"there is no AlpacaModel {index}: {All.Count} were given, numbered from 0";
                return null;
            }

            string kind = Kind(key as string);
            if (kind == null)
            {
                problem = $"\"{key}\" is not a model: name one by its place in the list from 0, or as model, static, vibration or buckling";
                return null;
            }

            if (kind == "model")
                return All.FirstOrDefault(m => !m.IsAnalysed) ?? All[0].Unanalysed() ?? All[0];

            var found = All.FirstOrDefault(m => KindOf(m) == kind);
            if (found == null)
            {
                string from = kind == "static" ? "Run Analysis" : kind == "vibration" ? "Natural Vibration" : kind == "pushover" ? "Pushover Analysis"
                    : kind == "footfall" ? "Footfall Analysis" : "Linear Buckling";
                problem = $"none of the AlpacaModels is a {kind} model: connect the one {from} hands on";
            }
            return found;
        }
    }

    /// <summary>
    /// Turns a template - JSON listing what goes in the report, in order - into a
    /// <see cref="ReportDocument"/>: the tables made, the figures drawn and the placeholders in
    /// the text filled in, out of the models it is given. Something that cannot be made is noted
    /// in the log, and in the report itself where it would have gone, and the rest goes on.
    /// </summary>
    public sealed class ReportBuilder
    {
        private readonly ModelSet _models;
        private readonly IFigureMaker _maker;
        private readonly string _folder;
        private readonly List<string> _log;
        private readonly Dictionary<string, string> _meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private int _figures;

        private ReportBuilder(ModelSet models, IFigureMaker maker, string folder, List<string> log)
        {
            _models = models;
            _maker = maker;
            _folder = folder;
            _log = log;
        }

        /// <summary>
        /// The report <paramref name="template"/> describes. <paramref name="folder"/> is where an
        /// image's path is read from when it is not a full one. What could not be made is added to
        /// <paramref name="log"/>. Throws when the template is not JSON at all.
        /// </summary>
        public static ReportDocument Build(string template, IEnumerable<Model> models, IFigureMaker maker, string folder, List<string> log)
        {
            JObject root;
            try
            {
                root = JObject.Parse(template ?? "");
            }
            catch (JsonReaderException ex)
            {
                throw new FormatException($"The template is not valid JSON, at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}");
            }

            var builder = new ReportBuilder(new ModelSet(models), maker, folder, log);
            return builder.Document(root);
        }

        /// <summary>
        /// The report a template left empty asks for: the model, then a chapter for each analysis
        /// among <paramref name="models"/> - its tables, and its shapes drawn, the first six
        /// vibration modes and the first four buckling modes two to a row. Also a starting point to
        /// copy and change.
        /// </summary>
        public static string DefaultTemplate(IEnumerable<Model> models)
        {
            var set = new ModelSet(models);
            bool Has(string kind) => set.All.Any(m => ModelSet.KindOf(m) == kind);

            var content = new JArray
            {
                new JObject { ["heading"] = "The model" },
                new JObject { ["text"] = "The model has {model.nodes} nodes and {model.elements} elements, and weighs {model.mass:N0} kg." },
                new JObject { ["table"] = "model.summary" },
                new JObject { ["table"] = "model.sections" },
                new JObject { ["table"] = "model.materials" },
                new JObject { ["figure"] = new JObject { ["model"] = "model", ["colourBy"] = "Section", ["supports"] = true, ["loads"] = true, ["caption"] = "The model, coloured by section, with its supports and loads" } },
            };

            if (Has("static"))
            {
                content.Add(new JObject { ["heading"] = "Static analysis" });
                content.Add(new JObject { ["text"] = "The largest displacement is {static.maxDisplacement:F2} mm, at node {static.maxNode}." });
                content.Add(new JObject { ["figure"] = new JObject { ["model"] = "static", ["result"] = "Displacement", ["component"] = "Magnitude", ["unit"] = "mm", ["caption"] = "Displacement" } });
                content.Add(new JObject { ["table"] = "static.displacements" });
                content.Add(new JObject { ["table"] = "static.reactions" });
            }

            if (Has("vibration"))
            {
                content.Add(new JObject { ["heading"] = "Natural vibration" });
                content.Add(new JObject { ["text"] = "The first mode vibrates at {vibration.frequency[1]:F2} Hz." });
                content.Add(new JObject { ["table"] = "vibration.modes" });
                content.Add(new JObject { ["table"] = "vibration.massRatios" });
                content.Add(new JObject
                {
                    ["repeat"] = "mode", ["of"] = "vibration", ["modes"] = "1-6", ["columns"] = 2,
                    ["content"] = new JArray { new JObject { ["figure"] = new JObject { ["result"] = "Displacement", ["legend"] = false, ["caption"] = "Mode {mode}: f = {vibration.frequency[mode]:F2} Hz" } } }
                });
            }

            if (Has("buckling"))
            {
                content.Add(new JObject { ["heading"] = "Linear buckling" });
                content.Add(new JObject { ["text"] = "The model buckles first at {buckling.factor[1]:F2} times its loads." });
                content.Add(new JObject { ["table"] = "buckling.factors" });
                content.Add(new JObject
                {
                    ["repeat"] = "mode", ["of"] = "buckling", ["modes"] = "1-4", ["columns"] = 2,
                    ["content"] = new JArray { new JObject { ["figure"] = new JObject { ["result"] = "Displacement", ["legend"] = false, ["caption"] = "Mode {mode}: αcr = {buckling.factor[mode]:F2}" } } }
                });
            }

            var root = new JObject
            {
                ["title"] = "Analysis report",
                ["subtitle"] = "",
                ["project"] = "",
                ["author"] = "",
                ["page"] = "A4",
                ["content"] = content
            };
            return root.ToString(Formatting.Indented);
        }

        private ReportDocument Document(JObject root)
        {
            _meta["date"] = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var key in new[] { "title", "subtitle", "project", "author", "date" })
            {
                var value = root[key];
                if (value != null && value.Type == JTokenType.String)
                    _meta[key] = (string)value;
            }

            var vars = new Dictionary<string, object>();
            var doc = new ReportDocument
            {
                Title = Fill(Meta("title", "Analysis report"), vars),
                Subtitle = Fill(Meta("subtitle", ""), vars)
            };
            foreach (var (key, label) in new[] { ("project", "Project"), ("author", "Author"), ("date", "Date") })
            {
                var value = Fill(Meta(key, ""), vars);
                if (!string.IsNullOrWhiteSpace(value))
                    doc.Details.Add(new KeyValuePair<string, string>(label, value));
            }

            // The paper: by name, or "width" and "height" in mm; turned for landscape.
            var size = PageSize((string)root["page"]);
            double? width = Millimetres(root["width"]), height = Millimetres(root["height"]);
            if (width > 0 && height > 0)
                size = PageSizes.Millimetres(width.Value, height.Value);
            bool landscape = string.Equals((string)root["orientation"], "landscape", StringComparison.OrdinalIgnoreCase);
            doc.PageWidth = landscape ? Math.Max(size.Width, size.Height) : Math.Min(size.Width, size.Height);
            doc.PageHeight = landscape ? Math.Min(size.Width, size.Height) : Math.Max(size.Width, size.Height);

            // The margins, in mm.
            if (root["margins"] is JObject margins)
            {
                double Margin(string key, double fallback) => Millimetres(margins[key]) is double mm && mm >= 0 ? mm * PageSizes.PointsPerMillimetre : fallback;
                doc.MarginTop = Margin("top", doc.MarginTop);
                doc.MarginBottom = Margin("bottom", doc.MarginBottom);
                doc.MarginLeft = Margin("left", doc.MarginLeft);
                doc.MarginRight = Margin("right", doc.MarginRight);
                if (doc.MarginTop + doc.MarginBottom > doc.PageHeight * 0.8 || doc.MarginLeft + doc.MarginRight > doc.PageWidth * 0.8)
                    Note("the margins leave less than a fifth of the page for the content");
            }

            doc.Cover = (bool?)root["cover"] ?? true;

            var content = root["content"] ?? root["blocks"];
            if (content is JArray blocks)
                doc.Blocks.AddRange(Blocks(blocks, vars, null));
            else
                Note("the template has no \"content\": a list of what goes in the report, in order");

            // 1, 1.1, 1.1.1, unless the template says "numbered": false.
            if ((bool?)root["numbered"] ?? true)
                NumberHeadings(doc.Blocks);

            // A report with no title of its own takes its cover's: {title}, and the header's.
            var cover = doc.Blocks.OfType<CoverBlock>().FirstOrDefault();
            if (root["title"] == null && cover != null && !string.IsNullOrWhiteSpace(cover.Title))
            {
                _meta["title"] = cover.Title;
                doc.Title = cover.Title;
            }

            if (root["titleBlock"] is JObject titleBlock)
                doc.TitleBlock = TitleBlock(titleBlock, doc, vars);

            return doc;
        }

        private string Meta(string key, string fallback) => _meta.TryGetValue(key, out var v) ? v : fallback;

        private SizeF PageSize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return PageSizes.A4;
            var size = PageSizes.Named(name);
            if (size != null) return size.Value;
            Note($"page \"{name}\" is not one of {string.Join(", ", PageSizes.Names)}: A4 it is");
            return PageSizes.A4;
        }

        private static double? Millimetres(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) return (double)token;
            return double.TryParse((string)token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : (double?)null;
        }

        /// <summary>
        /// The title block's items on the page, from their places in mm on its frame, from the frame's
        /// bottom left corner with y up as they were drawn: the frame stretched to the page. Its text
        /// has the template's own placeholders filled in now, and {page} and {pages} page by page.
        /// </summary>
        private List<PageItem> TitleBlock(JObject block, ReportDocument doc, Dictionary<string, object> vars)
        {
            var items = new List<PageItem>();
            var frame = block["frame"] as JArray;
            double frameWidth = frame != null && frame.Count == 2 ? (double)frame[0] : doc.PageWidth / PageSizes.PointsPerMillimetre;
            double frameHeight = frame != null && frame.Count == 2 ? (double)frame[1] : doc.PageHeight / PageSizes.PointsPerMillimetre;
            if (frameWidth <= 0 || frameHeight <= 0) { Note("the title block's frame has no size"); return items; }

            double sx = doc.PageWidth / frameWidth, sy = doc.PageHeight / frameHeight;
            if (Math.Abs(sx / sy - 1.0) > 0.01)
                Note($"the title block is drawn for {frameWidth:0} x {frameHeight:0} mm and the page is " +
                     $"{doc.PageWidth / PageSizes.PointsPerMillimetre:0} x {doc.PageHeight / PageSizes.PointsPerMillimetre:0} mm: it is stretched to fit");
            double ss = Math.Min(sx, sy);

            PointF At(JToken xy) => new PointF((float)((double)xy[0] * sx), (float)(doc.PageHeight - (double)xy[1] * sy));
            Color Colour(JToken c, Color fallback)
            {
                if (c == null || c.Type == JTokenType.Null) return fallback;
                try { return ColorTranslator.FromHtml((string)c); }
                catch { Note($"\"{c}\" in the title block is not a colour: write it as #RRGGBB"); return fallback; }
            }

            foreach (var item in (block["items"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if (item["line"] is JArray line && line.Count >= 2)
                {
                    items.Add(new PolylineItem { Points = line.Select(At).ToList(), Colour = Colour(item["colour"], ReportLayout.Ink), Width = (double?)item["width"] ?? 0.5 });
                }
                else if (item["fill"] is JArray fill && fill.Count >= 3)
                {
                    var points = fill.Select(At).ToList();
                    if (points[0] != points[points.Count - 1]) points.Add(points[0]);
                    items.Add(new PolygonItem
                    {
                        Points = points, Fill = Colour(item["colour"], ReportLayout.Accent),
                        Stroke = Colour(item["stroke"], Color.Empty), Width = (double?)item["width"] ?? 0.0
                    });
                }
                else if (item["text"] != null && item["at"] is JArray at && at.Count >= 2)
                {
                    var p = At(at);
                    string align = ((string)item["align"] ?? "left").Trim().ToLowerInvariant();
                    items.Add(new TextItem
                    {
                        X = p.X, Y = p.Y, Text = Fill((string)item["text"], vars),
                        Size = ((double?)item["size"] ?? 3.5) * ss, Bold = (bool?)item["bold"] ?? false,
                        Colour = Colour(item["colour"], ReportLayout.Ink),
                        Align = align.StartsWith("c") ? TextAlign.Centre : align.StartsWith("r") ? TextAlign.Right : TextAlign.Left
                    });
                }
                else if (item["image"] != null && item["at"] is JArray corner && item["size"] is JArray extent)
                {
                    string path = (string)item["image"];
                    if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(_folder)) path = Path.Combine(_folder, path);
                    if (!File.Exists(path)) { Note($"the title block's image {path} is not there"); continue; }
                    double w = (double)extent[0] * sx, h = (double)extent[1] * sy;
                    var p = At(corner);
                    items.Add(new ImageItem { Path = path, X = p.X, Y = p.Y - h, W = w, H = h });
                }
                else
                {
                    Note($"skipped {item.ToString(Formatting.None)} in the title block: each item is a line, a fill, a text or an image");
                }
            }

            return items;
        }

        private void Note(string problem) => _log.Add(problem);

        /// <summary>Something that could not be made: in the log, and in the report where it would have been.</summary>
        private ReportBlock Missing(string what, string problem)
        {
            Note($"{what}: {problem}");
            return new ParagraphBlock { Text = $"[{what} not made: {problem}]" };
        }

        // ── Blocks ─────────────────────────────────────────────────────────────────────────────

        private List<ReportBlock> Blocks(JArray blocks, Dictionary<string, object> vars, string defaultModel)
        {
            var made = new List<ReportBlock>();
            foreach (var token in blocks)
            {
                if (!(token is JObject block))
                {
                    if (token.Type == JTokenType.String)
                        made.Add(new ParagraphBlock { Text = Fill((string)token, vars) });
                    else
                        Note($"skipped {token.ToString(Formatting.None)}: each entry of content is an object such as {{\"text\": \"...\"}}");
                    continue;
                }

                // One entry that cannot be read - a number where text was wanted - costs that entry,
                // not the report.
                try
                {
                    made.AddRange(Block(block, vars, defaultModel));
                }
                catch (Exception ex)
                {
                    made.Add(Missing($"{block.ToString(Formatting.None)}", ex.Message));
                }
            }
            return made;
        }

        /// <summary>A heading of <paramref name="level"/>, numbered unless the block says "numbered": false.</summary>
        private HeadingBlock Heading(JObject block, string key, int level, Dictionary<string, object> vars) => new HeadingBlock
        {
            Text = Fill((string)block[key], vars),
            Level = level,
            Numbered = (bool?)block["numbered"] ?? true
        };

        /// <summary>
        /// The headings numbered as they come: a heading 1, 2, 3; a subheading under it 1.1, 1.2; a
        /// sub-subheading 1.2.1. A heading numbered "numbered": false is left out of the count.
        /// </summary>
        public static void NumberHeadings(IEnumerable<ReportBlock> blocks)
        {
            var counts = new int[3];
            foreach (var heading in blocks.OfType<HeadingBlock>())
            {
                if (!heading.Numbered) { heading.Number = ""; continue; }
                int level = Math.Max(1, Math.Min(3, heading.Level));
                counts[level - 1]++;
                for (int k = level; k < counts.Length; k++) counts[k] = 0;
                // From the first level counted: a subheading before any heading is 1, not 0.1.
                int first = Array.FindIndex(counts, c => c > 0);
                heading.Number = string.Join(".", counts.Skip(first).Take(level - first));
            }
        }

        private IEnumerable<ReportBlock> Block(JObject block, Dictionary<string, object> vars, string defaultModel)
        {
            // Blocks that go together, as Report View hands out a picture with its tables.
            if (block["group"] is JArray group)
                return Blocks(group, vars, defaultModel);

            if (block["heading"] != null)
                return new[] { Heading(block, "heading", Math.Max(1, Math.Min(3, (int?)block["level"] ?? 1)), vars) };
            if (block["title"] != null)
                return new[] { new CoverBlock { Title = Fill((string)block["title"], vars), Subtitle = Fill((string)block["subtitle"] ?? "", vars) } };
            if (block["note"] != null && block["table"] == null)
                return new[] { new ParagraphBlock { Text = Fill((string)block["note"], vars), Small = true } };
            if (block["subheading"] != null)
                return new[] { Heading(block, "subheading", 2, vars) };
            if (block["subsubheading"] != null)
                return new[] { Heading(block, "subsubheading", 3, vars) };

            if (block["text"] != null)
            {
                var text = block["text"];
                var paragraphs = text is JArray lines ? lines.Select(l => (string)l) : new[] { (string)text };
                bool small = string.Equals((string)block["style"], "note", StringComparison.OrdinalIgnoreCase);
                return paragraphs.Select(p => (ReportBlock)new ParagraphBlock { Text = Fill(p, vars), Small = small }).ToList();
            }

            if (block["pagebreak"] != null || block["pageBreak"] != null)
                return new[] { new PageBreakBlock() };

            if (block["table"] != null)
                return new[] { Table(block, vars, defaultModel) };

            if (block["figure"] != null)
            {
                var made = new List<ReportBlock> { Figure(block, vars, defaultModel, out var drawn) };
                if (drawn != null && WantsTables(drawn.Settings) && Writes(vars))
                    made.AddRange(ResultTables(drawn.Model, drawn.Settings));
                return made;
            }

            if (block["figures"] is JArray many)
            {
                int columns = (int?)block["columns"] ?? many.Count;
                var figures = many.OfType<JObject>().Select(f => Figure(new JObject { ["figure"] = f }, vars, defaultModel)).ToList();
                return Grid(figures, columns);
            }

            if (block["image"] != null)
                return new[] { Image(block, vars) };

            if (block["repeat"] != null)
                return Repeat(block, vars, defaultModel);

            Note($"skipped {block.ToString(Formatting.None)}: it is none of heading, subheading, subsubheading, title, text, note, table, figure, figures, image, repeat, group or pagebreak");
            return Enumerable.Empty<ReportBlock>();
        }

        private ReportBlock Table(JObject block, Dictionary<string, object> vars, string defaultModel)
        {
            if (block["table"] is JObject own)
                return OwnTable(own, vars);

            string name = Fill((string)block["table"], vars);
            var model = block["of"] ?? block["model"];
            object key = model != null ? Value(model, vars) : null;
            if (key == null && defaultModel != null && name.StartsWith(defaultModel + ".", StringComparison.OrdinalIgnoreCase)) key = defaultModel;

            var shown = block["columns"] is JArray picked ? picked.Select(c => (string)c).ToList() : null;
            string by = block["by"] != null ? Fill((string)block["by"], vars) : null;
            var table = ReportTables.Make(name, _models, key, Modes(block["modes"], null), shown, by, out string problem);
            if (table == null) return Missing($"Table \"{name}\"", problem);

            if (block["caption"] != null) table.Caption = Fill((string)block["caption"], vars);
            if (block["note"] != null) table.Note = Fill((string)block["note"], vars);
            return table;
        }

        /// <summary>
        /// A table given whole: "header" a name per column, "units" a unit per column, written under
        /// it in brackets, "rows" a list of rows of cells, "align" left, centre or right per column -
        /// a column of numbers is right-aligned unless it says otherwise - and "caption" and "note".
        /// </summary>
        private ReportBlock OwnTable(JObject own, Dictionary<string, object> vars)
        {
            var header = (own["header"] as JArray ?? new JArray()).Select(h => Fill((string)h ?? "", vars)).ToList();
            var units = (own["units"] as JArray ?? new JArray()).Select(u => (string)u ?? "").ToList();
            var rows = (own["rows"] as JArray ?? new JArray()).Select(r => r is JArray cells
                ? cells.Select(c => c.Type == JTokenType.Null ? "" : Fill(Convert.ToString(((JValue)c).Value, CultureInfo.InvariantCulture), vars)).ToList()
                : new List<string> { Fill((string)r, vars) }).ToList();

            int columns = Math.Max(header.Count, rows.Select(r => r.Count).DefaultIfEmpty(0).Max());
            if (columns == 0) return Missing("A table", "it has no header and no rows");

            var table = new TableBlock
            {
                Caption = Fill((string)own["caption"] ?? "", vars),
                Note = Fill((string)own["note"] ?? "", vars)
            };
            var aligns = (own["align"] as JArray ?? new JArray()).Select(a => ((string)a ?? "").Trim().ToLowerInvariant()).ToList();
            for (int c = 0; c < columns; c++)
            {
                string name = c < header.Count ? header[c] : "";
                string unit = c < units.Count ? units[c].Trim() : "";
                table.Header.Add(unit.Length > 0 ? $"{name} [{unit}]" : name);

                string align = c < aligns.Count ? aligns[c] : "";
                bool numbers = rows.Where(r => c < r.Count && r[c].Length > 0)
                                   .All(r => double.TryParse(r[c], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _));
                table.Align.Add(align.StartsWith("c") ? TextAlign.Centre : align.StartsWith("r") ? TextAlign.Right
                              : align.StartsWith("l") ? TextAlign.Left : numbers ? TextAlign.Right : TextAlign.Left);
            }
            table.Rows = rows;
            return table;
        }

        /// <summary>
        /// A figure: the model its "model" names drawn as the rest of its settings say. A mode is
        /// counted from 1, as a report numbers them, and drawn as Model View's step mode - 1; a
        /// static model is drawn at its last step unless "step" says otherwise.
        /// </summary>
        private ReportBlock Figure(JObject block, Dictionary<string, object> vars, string defaultModel) =>
            Figure(block, vars, defaultModel, out _);

        /// <summary>What a figure was drawn of and with, for the tables that follow it.</summary>
        private sealed class Drawn
        {
            public Model Model;
            public JObject Settings;
        }

        private ReportBlock Figure(JObject block, Dictionary<string, object> vars, string defaultModel, out Drawn drawn)
        {
            drawn = null;
            _figures++;
            string what = $"Figure {_figures}";

            var settings = block["figure"] as JObject ?? new JObject();
            settings = (JObject)FillAll(settings.DeepClone(), vars);
            foreach (var key in new[] { "caption", "width" })
                if (settings[key] == null && block[key] != null) settings[key] = FillAll(block[key].DeepClone(), vars);

            var modelKey = settings["model"] != null ? Value(settings["model"], vars) : defaultModel;
            var model = _models.Find(modelKey, out string problem);
            if (model == null) return Missing(what, problem);

            int step;
            if (settings["mode"] != null)
            {
                if (!int.TryParse(Convert.ToString(Value(settings["mode"], vars), CultureInfo.InvariantCulture), out int mode) || mode < 1)
                    return Missing(what, $"mode \"{settings["mode"]}\" is not a mode number, from 1");
                int count = ModeCount(model);
                if (count > 0 && mode > count)
                    return Missing(what, $"the model has {count} modes, so no mode {mode}");
                step = mode - 1;
            }
            else if (settings["step"] != null)
            {
                step = Convert.ToInt32(Value(settings["step"], vars), CultureInfo.InvariantCulture);
            }
            else if ((model.IsModal || model.IsBuckling) && vars.TryGetValue("mode", out var looped))
            {
                step = Convert.ToInt32(looped, CultureInfo.InvariantCulture) - 1;
            }
            else if (!model.IsModal && !model.IsBuckling && vars.TryGetValue("step", out var walked))
            {
                step = Convert.ToInt32(walked, CultureInfo.InvariantCulture);
            }
            else
            {
                step = model.IsAnalysed && !model.IsModal && !model.IsBuckling ? ReportTables.LastStep(model) : 0;
            }
            settings["step"] = step;

            FigureBlock figure;
            try
            {
                figure = _maker.Draw(model, settings, out problem);
            }
            catch (Exception ex)
            {
                figure = null;
                problem = ex.Message;
            }
            if (figure == null) return Missing(what, problem ?? "nothing was drawn");

            figure.Caption = settings["caption"] != null ? (string)settings["caption"] : "";
            figure.Width = (double?)settings["width"] ?? 1.0;
            drawn = new Drawn { Model = model, Settings = settings };
            return figure;
        }

        // ── The tables of what a picture draws ─────────────────────────────────────────────────

        /// <summary>The tables made after a picture, so that a repeat can put them after all its pictures.</summary>
        private readonly HashSet<ReportBlock> _resultTables = new HashSet<ReportBlock>();

        /// <summary>Set false in a repeat's every pass but its last: its pictures share one set of tables.</summary>
        private const string TablesHere = "\u0001tables";

        private static bool Writes(Dictionary<string, object> vars) =>
            !vars.TryGetValue(TablesHere, out var here) || !(here is bool b) || b;

        /// <summary>Whether any result of a figure asks for its table.</summary>
        private static bool WantsTables(JObject settings) =>
            settings["results"] is JArray results && results.OfType<JObject>().Any(r => (bool?)r["table"] == true)
            || (bool?)settings["table"] == true;

        /// <summary>
        /// The tables of what a picture draws, read as it draws them: each result that asks for one, in
        /// the unit, at the step, through the layer and in the axes of the picture.
        /// </summary>
        private IEnumerable<ReportBlock> ResultTables(Model model, JObject settings)
        {
            IList<ResultValues> values;
            string problem;
            try { values = _maker.Values(model, settings, out problem); }
            catch (Exception ex) { values = null; problem = ex.Message; }
            if (values == null) return new[] { Missing("The table of the picture", problem ?? "nothing was read") };

            string note = "";
            try
            {
                int step = (int?)settings["step"] ?? 0;
                if (!model.IsModal && !model.IsBuckling && Alpaca4d.Result.Read.StepCount(model) > 1)
                    note = $"At step {step}.";
            }
            catch { }

            var tables = ReportTables.OfValues(values, note);
            foreach (var table in tables) _resultTables.Add(table);
            return tables;
        }

        /// <summary>
        /// A repeat's blocks with the tables of its last pass taken out of the run of pictures and put
        /// after it, where they read as the tables of all of them.
        /// </summary>
        private List<ReportBlock> TablesLast(List<ReportBlock> made, int columns)
        {
            var tables = made.Where(_resultTables.Contains).ToList();
            var rest = made.Where(b => !_resultTables.Contains(b)).ToList();
            var laid = columns > 1 ? Grid(rest, columns) : rest;
            laid.AddRange(tables);
            return laid;
        }

        private ReportBlock Image(JObject block, Dictionary<string, object> vars)
        {
            _figures++;
            string what = $"Figure {_figures}";
            var spec = block["image"] as JObject;
            string path = Fill(spec != null ? (string)spec["path"] : (string)block["image"], vars);
            if (string.IsNullOrWhiteSpace(path)) return Missing(what, "the image has no path");

            if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(_folder))
                path = Path.Combine(_folder, path);
            if (!File.Exists(path)) return Missing(what, $"there is no file {path}");

            var size = _maker.ImageSize(path);
            if (size == null) return Missing(what, $"{path} is not a picture that can be read");

            JToken Get(string key) => spec?[key] ?? block[key];
            return new FigureBlock
            {
                ImagePath = path,
                PixelWidth = size.Value.Width,
                PixelHeight = size.Value.Height,
                Width = (double?)Get("width") ?? 1.0,
                Caption = Fill((string)Get("caption") ?? "", vars)
            };
        }

        /// <summary>
        /// The blocks inside, once for each mode: "repeat" names the variable ("mode"), "of" the
        /// model whose modes they are, "modes" which of them ("all", "1-4", "1,3,5" or a list), and
        /// "columns" how many of the figures it makes go side by side.
        /// </summary>
        private IEnumerable<ReportBlock> Repeat(JObject block, Dictionary<string, object> vars, string defaultModel)
        {
            string name = (string)block["repeat"];
            if (string.IsNullOrWhiteSpace(name)) name = "mode";
            if (name == "step" || block["steps"] != null)
                return RepeatSteps(block, vars, defaultModel);

            var ofToken = block["of"] ?? block["model"];
            string of = ofToken != null ? Convert.ToString(Value(ofToken, vars), CultureInfo.InvariantCulture) : defaultModel;
            if (of == null)
                of = _models.All.Any(m => m.IsModal) ? "vibration" : _models.All.Any(m => m.IsBuckling) ? "buckling" : null;

            var model = _models.Find(of, out string problem);
            if (model == null) return new[] { Missing($"Repeat over {name}", problem) };

            int count = ModeCount(model);
            if (count == 0) return new[] { Missing($"Repeat over {name}", "the model has no modes to repeat over") };

            var modes = Modes(block["modes"] ?? block["range"], count) ?? Enumerable.Range(1, count).ToList();
            var content = block["content"] as JArray ?? block["blocks"] as JArray;
            if (content == null) return new[] { Missing($"Repeat over {name}", "it has no \"content\" to repeat") };

            var made = new List<ReportBlock>();
            var passes = modes.Where(m => m >= 1 && m <= count).ToList();
            foreach (int mode in passes)
            {
                var inner = new Dictionary<string, object>(vars) { [name] = mode, [TablesHere] = mode == passes[passes.Count - 1] };
                if (model.IsModal && model.Eigenvalues != null && mode <= model.Eigenvalues.Count)
                {
                    double omega = Math.Sqrt(Math.Max(0.0, model.Eigenvalues[mode - 1]));
                    inner["eigenvalue"] = model.Eigenvalues[mode - 1];
                    inner["omega"] = omega;
                    inner["frequency"] = omega / (2.0 * Math.PI);
                    inner["period"] = omega > 0.0 ? 2.0 * Math.PI / omega : double.PositiveInfinity;
                }
                if (model.IsBuckling && model.BucklingFactors != null && mode <= model.BucklingFactors.Count)
                    inner["factor"] = model.BucklingFactors[mode - 1];
                made.AddRange(Blocks(content, inner, of));
            }

            return TablesLast(made, (int?)block["columns"] ?? 1);
        }

        /// <summary>
        /// The blocks inside once for each of the steps "steps" picks of the model "of" names - a static
        /// or pushover model: "last", a list of steps from 0, or { "spread": n } for n steps spread over
        /// the analysis, the last at its end. {step} is the step it is on, {steps} how many there are.
        /// </summary>
        private IEnumerable<ReportBlock> RepeatSteps(JObject block, Dictionary<string, object> vars, string defaultModel)
        {
            var ofToken = block["of"] ?? block["model"];
            object of = ofToken != null ? Value(ofToken, vars) : defaultModel ?? (object)"static";
            var model = _models.Find(of, out string problem);
            if (model == null) return new[] { Missing("Repeat over steps", problem) };

            int count;
            try { count = Alpaca4d.Result.Read.StepCount(model); }
            catch (Exception ex) { return new[] { Missing("Repeat over steps", $"the steps could not be read: {ex.Message}") }; }

            var steps = new List<int>();
            var token = block["steps"];
            if (token is JObject spread && spread["spread"] != null)
            {
                int n = Math.Max(1, (int)spread["spread"]);
                for (int i = 1; i <= n; i++) steps.Add((int)Math.Round(i * (count - 1) / (double)n));
            }
            else if (token is JArray list)
            {
                steps.AddRange(list.Select(t => (int)t));
            }
            else
            {
                string text = ((string)token ?? "last").Trim().ToLowerInvariant();
                if (text == "last" || text == "") steps.Add(count - 1);
                else if (text.StartsWith("spread:") && int.TryParse(text.Substring(7), out int n))
                    for (int i = 1; i <= Math.Max(1, n); i++) steps.Add((int)Math.Round(i * (count - 1) / (double)Math.Max(1, n)));
                else
                    steps.AddRange((Modes(token, null) ?? new List<int>()));
            }
            steps = steps.Where(s => s >= 0 && (count == 0 || s < count)).Distinct().ToList();
            if (steps.Count == 0) return new[] { Missing("Repeat over steps", $"none of the steps asked for is among the {count} the analysis recorded") };

            var content = block["content"] as JArray ?? block["blocks"] as JArray;
            if (content == null) return new[] { Missing("Repeat over steps", "it has no \"content\" to repeat") };

            var made = new List<ReportBlock>();
            string ofName = of as string ?? Convert.ToString(of, CultureInfo.InvariantCulture);
            foreach (int step in steps)
            {
                var inner = new Dictionary<string, object>(vars) { ["step"] = step, ["steps"] = count, [TablesHere] = step == steps[steps.Count - 1] };
                made.AddRange(Blocks(content, inner, ofName));
            }

            return TablesLast(made, (int?)block["columns"] ?? 1);
        }

        /// <summary>Runs of figures put into rows of <paramref name="columns"/>; anything else left as it is.</summary>
        private static List<ReportBlock> Grid(List<ReportBlock> blocks, int columns)
        {
            var grid = new List<ReportBlock>();
            FigureRowBlock row = null;
            foreach (var block in blocks)
            {
                if (block is FigureBlock figure && columns > 1)
                {
                    if (row == null || row.Figures.Count == columns)
                    {
                        row = new FigureRowBlock { Columns = columns };
                        grid.Add(row);
                    }
                    row.Figures.Add(figure);
                }
                else
                {
                    row = null;
                    grid.Add(block);
                }
            }
            return grid;
        }

        private static int ModeCount(Model model)
        {
            if (model.IsBuckling) return model.BucklingFactors?.Count ?? 0;
            if (model.IsModal) return model.Eigenvalues?.Count ?? model.NumberOfModes;
            return 0;
        }

        /// <summary>Which modes, from 1: "all", "1-4", "1,3,5", a number n for the first n, or a list. Null for all.</summary>
        private static List<int> Modes(JToken token, int? count)
        {
            if (token == null) return null;
            if (token is JArray list) return list.Select(t => (int)t).ToList();
            if (token.Type == JTokenType.Integer) return Enumerable.Range(1, (int)token).ToList();

            string text = ((string)token ?? "").Trim().ToLowerInvariant();
            if (text == "" || text == "all") return null;

            var modes = new List<int>();
            foreach (var part in text.Split(','))
            {
                var ends = part.Split('-');
                if (ends.Length == 2 && int.TryParse(ends[0].Trim(), out int a) && int.TryParse(ends[1].Trim(), out int b))
                    modes.AddRange(Enumerable.Range(Math.Min(a, b), Math.Abs(b - a) + 1));
                else if (int.TryParse(part.Trim(), out int one))
                    modes.Add(one);
            }
            return modes;
        }

        // ── Placeholders ───────────────────────────────────────────────────────────────────────

        private static readonly Regex Placeholder = new Regex(@"\{([A-Za-z_][A-Za-z0-9_.]*)(?:\[([^\]]+)\])?(?::([^}]+))?\}");

        /// <summary>
        /// Every string in <paramref name="token"/> with its placeholders filled in; a string that
        /// was nothing but one placeholder for a number becomes that number, so "{mode}" can stand
        /// where a number is wanted.
        /// </summary>
        private JToken FillAll(JToken token, Dictionary<string, object> vars)
        {
            switch (token)
            {
                case JObject obj:
                    foreach (var property in obj.Properties().ToList())
                        property.Value = FillAll(property.Value, vars);
                    return obj;
                case JArray array:
                    for (int i = 0; i < array.Count; i++)
                        array[i] = FillAll(array[i], vars);
                    return array;
                case JValue value when value.Type == JTokenType.String:
                    var text = (string)value;
                    var match = Placeholder.Match(text);
                    if (match.Success && match.Length == text.Length && match.Groups[3].Length == 0)
                    {
                        var found = Lookup(match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : null, vars);
                        if (found is int || found is double) return new JValue(found);
                    }
                    return new JValue(Fill(text, vars));
                default:
                    return token;
            }
        }

        private object Value(JToken token, Dictionary<string, object> vars)
        {
            var filled = FillAll(token.DeepClone(), vars);
            return filled is JValue v ? v.Value : filled.ToString();
        }

        /// <summary>
        /// The text with each {name}, {name[index]} or {name:format} in it replaced by its value -
        /// the index a number or a variable such as mode, the format a .NET one such as F2. One
        /// with no value is left as it is, and noted.
        /// </summary>
        public string Fill(string text, Dictionary<string, object> vars)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? "";

            return Placeholder.Replace(text, match =>
            {
                string name = match.Groups[1].Value;
                string index = match.Groups[2].Success ? match.Groups[2].Value : null;
                string format = match.Groups[3].Success ? match.Groups[3].Value : null;

                object value;
                try
                {
                    value = Lookup(name, index, vars);
                }
                catch (Exception ex)
                {
                    Note($"{match.Value} could not be read: {ex.Message}");
                    return match.Value;
                }

                if (value == null)
                {
                    Note($"{match.Value} has no value: see the placeholders the Report component's description lists");
                    return match.Value;
                }

                try
                {
                    switch (value)
                    {
                        case double d: return d.ToString(format ?? "G4", CultureInfo.InvariantCulture);
                        case int i: return i.ToString(format ?? "D", CultureInfo.InvariantCulture);
                        default: return Convert.ToString(value, CultureInfo.InvariantCulture);
                    }
                }
                catch (FormatException)
                {
                    Note($"{match.Value}: \"{format}\" is not a number format such as F2 or N0");
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                }
            });
        }

        /// <summary>The placeholders a template can use, and what each stands for.</summary>
        public static readonly (string Name, string What)[] Placeholders =
        {
            ("title, subtitle, project, author, date", "The template's own"),
            ("mode", "Inside a repeat: the mode it is on, from 1"),
            ("frequency, period, omega, eigenvalue, factor", "Inside a repeat: that mode's f [Hz], T [s], ω, ω², or its buckling load factor"),
            ("step, steps", "Inside a repeat over steps: the step it is on, from 0, and how many the analysis recorded"),
            ("page, pages", "In a title block: the page's number, and how many there are"),
            ("model.nodes, model.elements, model.beams, model.shells, model.solids", "How many the model has"),
            ("model.supports, model.loadPatterns", "How many the model has"),
            ("model.mass", "The model's mass in kg"),
            ("static.maxDisplacement, static.maxNode", "The largest displacement of the static model, in mm, and its node"),
            ("vibration.modes", "How many modes Natural Vibration found"),
            ("vibration.frequency[i], vibration.period[i], vibration.omega[i], vibration.eigenvalue[i]", "Mode i's f [Hz], T [s], ω [rad/s] and ω² [rad²/s²]"),
            ("buckling.modes, buckling.factor[i]", "How many buckling modes, and mode i's load factor"),
            ("footfall.maxFactor, footfall.maxNode", "The largest footfall response factor, and its node"),
        };

        private object Lookup(string name, string index, Dictionary<string, object> vars)
        {
            if (vars.TryGetValue(name, out var variable)) return variable;
            if (_meta.TryGetValue(name, out var meta)) return meta;
            if (name == "page" || name == "pages") return "{" + name + "}";

            int? at = null;
            if (index != null)
            {
                if (int.TryParse(index.Trim(), out int n)) at = n;
                else if (vars.TryGetValue(index.Trim(), out var v)) at = Convert.ToInt32(v, CultureInfo.InvariantCulture);
                else return null;
            }

            string key = name.ToLowerInvariant();
            int dot = key.IndexOf('.');
            if (dot < 0) return null;

            var model = _models.Find(key.Substring(0, dot), out _);
            if (model == null) return null;
            string what = key.Substring(dot + 1);

            switch (key.Substring(0, dot))
            {
                case "model":
                    switch (what)
                    {
                        case "nodes": return model.Nodes.Count;
                        case "elements": return model.Elements.Count;
                        case "beams": return model.Beams.Count;
                        case "shells": return model.Shells.Count;
                        case "solids": case "bricks": return model.Bricks.Count;
                        case "supports": return model.Supports.Count;
                        case "loadpatterns": return model.LoadPatterns.Count;
                        case "mass": return ReportTables.MassOf(model);
                    }
                    return null;

                case "static":
                    switch (what)
                    {
                        case "maxdisplacement": return ReportTables.LargestDisplacement(model).Millimetres;
                        case "maxnode": return ReportTables.LargestDisplacement(model).Node;
                    }
                    return null;

                case "vibration":
                {
                    var eigen = model.Eigenvalues ?? new List<double>();
                    if (what == "modes") return eigen.Count;
                    if (at == null || at < 1 || at > eigen.Count) return null;
                    double lambda = eigen[at.Value - 1];
                    double omega = Math.Sqrt(Math.Max(0.0, lambda));
                    switch (what)
                    {
                        case "eigenvalue": return lambda;
                        case "omega": return omega;
                        case "frequency": case "f": return omega / (2.0 * Math.PI);
                        case "period": case "t": return omega > 0.0 ? 2.0 * Math.PI / omega : double.PositiveInfinity;
                    }
                    return null;
                }

                case "footfall":
                {
                    // maxFactor and maxNode for R; maxSteadyFactor, maxTransientAcceleration and the rest,
                    // and each with Node after it, for the others.
                    if (model.Footfall == null || !what.StartsWith("max")) return null;
                    bool node = what.EndsWith("node");
                    string of = what.Substring(3, what.Length - 3 - (node ? 4 : 0));
                    if (of == "" || of == "factor") of = "response factor r";
                    else if (of.EndsWith("factor")) of = of.Substring(0, of.Length - 6) + " r";
                    of = of.Replace("steady", "steady-state ").Replace("transient", "transient ").Replace("  ", " ").Trim();
                    var quantity = FootfallResponse.Quantities.FirstOrDefault(q => string.Equals(q, of, StringComparison.OrdinalIgnoreCase));
                    if (quantity == null) return null;
                    var largest = model.Footfall.Largest(quantity);
                    return node ? (object)largest.Node : largest.Factor;
                }

                case "buckling":
                {
                    var factors = model.BucklingFactors ?? new List<double>();
                    if (what == "modes") return factors.Count;
                    if (what != "factor" || at == null || at < 1 || at > factors.Count) return null;
                    return factors[at.Value - 1];
                }
            }

            return null;
        }
    }
}
