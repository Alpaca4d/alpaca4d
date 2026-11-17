// Checks of the report's template, tables and page layout, which need no Rhino: the figures are
// drawn by a stand-in that records what it was asked for. Run with run.sh.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Alpaca4d;
using Alpaca4d.Report;
using Newtonsoft.Json.Linq;

static class ReportCheck
{
    static int passed, failed;

    static void Check(string what, bool ok, string detail = "")
    {
        if (ok) { passed++; Console.WriteLine($"[PASS] {what}"); }
        else { failed++; Console.WriteLine($"[FAIL] {what} {detail}"); }
    }

    /// <summary>Draws nothing: notes the step and settings it was asked for, and hands back a 2:1 picture.</summary>
    sealed class Recorder : IFigureMaker
    {
        public readonly List<(Model Model, JObject Figure)> Asked = new List<(Model, JObject)>();

        public FigureBlock Draw(Model model, JObject figure, out string problem)
        {
            problem = null;
            Asked.Add((model, figure));
            return new FigureBlock { ImagePath = $"figure-{Asked.Count}.png", PixelWidth = 2000, PixelHeight = 1000 };
        }

        public Size? ImageSize(string path) => new Size(100, 50);

        /// <summary>How many times a figure's tables were read, and canned values for them.</summary>
        public int ValuesAsked;

        public IList<ResultValues> Values(Model model, JObject figure, out string problem)
        {
            problem = null;
            ValuesAsked++;
            var made = new List<ResultValues>();
            foreach (var entry in ((JArray)figure["results"]).OfType<JObject>().Where(e => (bool?)e["table"] == true))
            {
                string result = (string)entry["result"];
                if (result == "Reactions")
                {
                    var forces = new[] { "Fx", "Fy", "Fz" };
                    var values = new[] { new[] { 1.0, -1.0 }, new[] { 0.5, 0.5 }, new[] { 10.0, 30.0 } };
                    for (int c = 0; c < 3; c++)
                    {
                        var part = new ResultValues { Result = result, Component = forces[c], Unit = "kN", At = ResultValues.AtSupport, Summed = true };
                        for (int i = 0; i < 2; i++) part.Values.Add(new KeyValuePair<string, double>((i + 1).ToString(), values[c][i]));
                        made.Add(part);
                    }
                }
                else
                {
                    var part = new ResultValues { Result = result, Component = (string)entry["component"] ?? "N", Unit = "mm" };
                    part.Values.Add(new KeyValuePair<string, double>("3", -12.5));
                    part.Values.Add(new KeyValuePair<string, double>("7", 4.25));
                    made.Add(part);
                }
            }
            return made;
        }
    }

    static Model Vibration(params double[] hertz) => new Model
    {
        IsAnalysed = true,
        IsModal = true,
        Eigenvalues = hertz.Select(f => Math.Pow(2 * Math.PI * f, 2)).ToList()
    };

    static Model Buckling(params double[] factors) => new Model
    {
        IsAnalysed = true,
        IsBuckling = true,
        BucklingFactors = factors.ToList()
    };

    static IEnumerable<string> Texts(ReportPages pages, int page) => pages.Pages[page].Items.OfType<TextItem>().Select(t => t.Text);

    static int Main()
    {
        // ── Wrapping ───────────────────────────────────────────────────────────────────────────
        var text = "The first mode of the frame sways along X at a little under one hertz, and the second along Y.";
        var lines = TextMetrics.Wrap(text, 10, false, 120);
        Check("wrap: more than one line", lines.Count > 1, $"{lines.Count}");
        Check("wrap: every line fits", lines.All(l => TextMetrics.Width(l, 10) <= 120.0 + 1e-9));
        Check("wrap: no word lost", string.Join(" ", lines) == text);
        Check("wrap: a line break is kept", TextMetrics.Wrap("one\ntwo", 10, false, 500).Count == 2);
        var longWord = TextMetrics.Wrap(new string('W', 60), 10, false, 100);
        Check("wrap: a word too long is broken", longWord.Count > 1 && longWord.All(l => TextMetrics.Width(l, 10) <= 100.0 + 1e-9));
        Check("width: bold is wider", TextMetrics.Width("Natural frequencies", 10, true) > TextMetrics.Width("Natural frequencies", 10));
        Check("fit: cut with an ellipsis", TextMetrics.Fit("Natural frequencies of the frame", 10, false, 60).EndsWith("…"));

        // ── Legend values ──────────────────────────────────────────────────────────────────────
        Check("value: 12.345 is 12.3", ReportLayout.FormatValue(12.345) == "12.3", ReportLayout.FormatValue(12.345));
        Check("value: 0.012345 is 0.0123", ReportLayout.FormatValue(0.012345) == "0.0123", ReportLayout.FormatValue(0.012345));
        Check("value: 0 is 0", ReportLayout.FormatValue(0.0) == "0");
        Check("value: 250000 in an exponent", ReportLayout.FormatValue(250000).Contains("E"), ReportLayout.FormatValue(250000));

        // ── A long table runs over pages, its header on each ───────────────────────────────────
        var table = new TableBlock { Caption = "Many rows", Header = new List<string> { "Mode", "f [Hz]" } };
        for (int i = 1; i <= 120; i++) table.Rows.Add(new List<string> { i.ToString(), (i * 0.5).ToString("F2") });
        var doc = new ReportDocument { Title = "Layout" };
        doc.Blocks.Add(table);
        var pages = ReportLayout.Lay(doc);
        Check("table: runs over more than one page", pages.Pages.Count > 1, $"{pages.Pages.Count}");
        Check("table: continued on the next page", Texts(pages, 1).Contains("Table 1 (continued)"));
        Check("table: header on the next page", Texts(pages, 1).Count(t => t == "f [Hz]") == 1);
        Check("table: every row written once", Enumerable.Range(1, 120).All(i => pages.Pages.Sum(p => p.Items.OfType<TextItem>().Count(t => t.Text == (i * 0.5).ToString("F2"))) >= 1));
        Check("footer: page numbers", Texts(pages, 0).Contains($"Page 1 of {pages.Pages.Count}"));
        double bottom = pages.Height - 62.0;
        Check("table: no row below the margin", pages.Pages.All(p => p.Items.OfType<RectItem>().All(r => r.Y + r.H <= bottom + 1e-6)));

        // ── A page break, and a heading kept with what follows ─────────────────────────────────
        doc = new ReportDocument { Title = "Breaks" };
        doc.Blocks.Add(new ParagraphBlock { Text = "Before." });
        doc.Blocks.Add(new PageBreakBlock());
        doc.Blocks.Add(new HeadingBlock { Text = "After the break" });
        pages = ReportLayout.Lay(doc);
        Check("page break: a second page", pages.Pages.Count == 2);
        Check("page break: the heading on it", Texts(pages, 1).Contains("After the break"));

        // ── A figure most of the way onto what is left of a page is made smaller to fit ───────
        doc = new ReportDocument { Title = "Fit" };
        doc.Blocks.Add(new ParagraphBlock { Text = string.Join("\n", Enumerable.Repeat("A line of text.", 24)) });
        doc.Blocks.Add(new FigureBlock { ImagePath = "a.png", PixelWidth = 2000, PixelHeight = 1240, Caption = "Made smaller" });
        pages = ReportLayout.Lay(doc);
        var shrunk = pages.Pages[0].Items.OfType<ImageItem>().ToList();
        Check("figure: made smaller to fit, not moved on", pages.Pages.Count == 1 && shrunk.Count == 1 && shrunk[0].W < 480.0,
              $"{pages.Pages.Count} pages, width {(shrunk.Count > 0 ? shrunk[0].W : 0):F0}");
        Check("figure: still inside the margin", shrunk.Count == 1 && shrunk[0].Y + shrunk[0].H <= pages.Height - 62.0);

        // ── A table's note stays with its last row ─────────────────────────────────────────────
        bool together = true;
        for (int n = 20; n <= 60; n++)
        {
            var noted = new TableBlock { Caption = "Noted", Header = new List<string> { "Row" }, Note = "The note under the table." };
            for (int i = 0; i < n; i++) noted.Rows.Add(new List<string> { "row " + i });
            doc = new ReportDocument { Title = "Notes" };
            doc.Blocks.Add(noted);
            pages = ReportLayout.Lay(doc);
            int lastRow = pages.Pages.FindIndex(p => p.Items.OfType<TextItem>().Any(t => t.Text == "row " + (n - 1)));
            int note = pages.Pages.FindIndex(p => p.Items.OfType<TextItem>().Any(t => t.Text == "The note under the table."));
            if (lastRow != note) { together = false; Console.WriteLine($"  {n} rows: last row on page {lastRow + 1}, note on {note + 1}"); }
            int onLast = pages.Pages[lastRow].Items.OfType<TextItem>().Count(t => t.Text.StartsWith("row "));
            if (onLast < 2) { together = false; Console.WriteLine($"  {n} rows: {onLast} row alone on the last page"); }
        }
        Check("table: the note and two rows at least on the last page", together);

        // A short table near the foot of a page goes over whole.
        bool whole = true;
        for (int filled = 30; filled <= 44; filled++)
        {
            var little = new TableBlock { Caption = "Little", Header = new List<string> { "Row" }, Note = "Its note." };
            for (int i = 0; i < 5; i++) little.Rows.Add(new List<string> { "little " + i });
            doc = new ReportDocument { Title = "Whole" };
            doc.Blocks.Add(new ParagraphBlock { Text = string.Join("\n", Enumerable.Repeat("A line.", filled)) });
            doc.Blocks.Add(little);
            pages = ReportLayout.Lay(doc);
            var where = Enumerable.Range(0, 5).Select(i => pages.Pages.FindIndex(p => p.Items.OfType<TextItem>().Any(t => t.Text == "little " + i))).Distinct().Count();
            if (where != 1) { whole = false; Console.WriteLine($"  after {filled} lines the little table is on {where} pages"); }
        }
        Check("table: a short one kept whole", whole);

        // ── A template: modes repeated two to a row, placeholders filled in ────────────────────
        var vibration = Vibration(1.0, 2.0, 3.0);
        var buckling = Buckling(4.5, 7.25);
        var recorder = new Recorder();
        var log = new List<string>();
        var template = @"{
          ""title"": ""Frame - {project}"", ""project"": ""Test"", ""author"": ""A4D"", ""date"": ""2026-11-11"",
          ""content"": [
            { ""heading"": ""Natural vibration"" },
            { ""text"": ""The first mode is at {vibration.frequency[1]:F2} Hz; there are {vibration.modes}."" },
            { ""table"": ""vibration.modes"" },
            { ""repeat"": ""mode"", ""of"": ""vibration"", ""columns"": 2, ""content"": [
                { ""figure"": { ""result"": ""Displacement"", ""caption"": ""Mode {mode}: f = {vibration.frequency[mode]:F2} Hz"" } }
            ] },
            { ""table"": ""buckling.factors"", ""modes"": 2 },
            { ""table"": ""buckling.factors"", ""modes"": ""2"" },
            { ""figure"": { ""model"": ""buckling"", ""mode"": 2, ""caption"": ""alpha = {buckling.factor[2]:F2}"" } },
            { ""text"": ""{nothing.here}"" },
            { ""table"": ""static.reactions"" }
          ] }";
        var report = ReportBuilder.Build(template, new[] { vibration, buckling }, recorder, null, log);

        Check("title: placeholder filled", report.Title == "Frame - Test", report.Title);
        Check("details: project, author, date", report.Details.Select(d => d.Key).SequenceEqual(new[] { "Project", "Author", "Date" }));
        var paragraph = report.Blocks.OfType<ParagraphBlock>().First();
        Check("text: frequency and count filled", paragraph.Text == "The first mode is at 1.00 Hz; there are 3.", paragraph.Text);

        var modes = report.Blocks.OfType<TableBlock>().First();
        Check("vibration.modes: a row per mode", modes.Rows.Count == 3);
        Check("vibration.modes: f of mode 2", modes.Rows[1][3] == "2.000", modes.Rows[1][3]);
        Check("vibration.modes: T of mode 2", modes.Rows[1][4] == "0.500", modes.Rows[1][4]);

        var rows = report.Blocks.OfType<FigureRowBlock>().ToList();
        Check("repeat: three modes in two rows", rows.Count == 2 && rows[0].Figures.Count == 2 && rows[1].Figures.Count == 1);
        Check("repeat: rows keep two columns", rows.All(r => r.Columns == 2));
        Check("repeat: caption of mode 3", rows[1].Figures[0].Caption == "Mode 3: f = 3.00 Hz", rows[1].Figures[0].Caption);
        Check("repeat: the vibration model drawn", recorder.Asked.Take(3).All(a => a.Model == vibration));
        Check("repeat: mode n drawn as step n - 1", recorder.Asked.Take(3).Select(a => (int)a.Figure["step"]).SequenceEqual(new[] { 0, 1, 2 }));

        var factors = report.Blocks.OfType<TableBlock>().ElementAt(1);
        Check("buckling.factors: modes 2 is the first two", factors.Rows.Count == 2 && factors.Rows[1][1] == "7.250", string.Join("|", factors.Rows.Select(r => string.Join(",", r))));
        var second = report.Blocks.OfType<TableBlock>().ElementAt(2);
        Check("buckling.factors: modes \"2\" is mode 2 alone", second.Rows.Count == 1 && second.Rows[0][0] == "2", string.Join("|", second.Rows.Select(r => string.Join(",", r))));
        Check("figure: mode 2 of the buckling model is step 1", recorder.Asked[3].Model == buckling && (int)recorder.Asked[3].Figure["step"] == 1);
        var single = report.Blocks.OfType<FigureBlock>().Single();
        Check("figure: caption filled", single.Caption == "alpha = 7.25", single.Caption);

        Check("placeholder: unknown one left as it is", report.Blocks.OfType<ParagraphBlock>().Any(p => p.Text == "{nothing.here}"));
        Check("placeholder: unknown one logged", log.Any(l => l.Contains("{nothing.here}")), string.Join(" | ", log));
        Check("table: no static model, noted in the report", report.Blocks.OfType<ParagraphBlock>().Any(p => p.Text.StartsWith("[Table \"static.reactions\" not made")));
        Check("table: no static model, logged", log.Any(l => l.Contains("static.reactions") && l.Contains("Run Analysis")), string.Join(" | ", log));

        // A figure of a mode the model does not have.
        log.Clear();
        report = ReportBuilder.Build(@"{ ""content"": [ { ""figure"": { ""model"": ""vibration"", ""mode"": 9 } } ] }", new[] { vibration }, new Recorder(), null, log);
        Check("figure: mode 9 of 3 refused", log.Any(l => l.Contains("3 modes")), string.Join(" | ", log));

        // A model by its place in the list.
        log.Clear();
        recorder = new Recorder();
        ReportBuilder.Build(@"{ ""content"": [ { ""figure"": { ""model"": 1, ""mode"": 1 } } ] }", new[] { vibration, buckling }, recorder, null, log);
        Check("model: by its place in the list", recorder.Asked.Count == 1 && recorder.Asked[0].Model == buckling);

        // ── What goes wrong ────────────────────────────────────────────────────────────────────
        try
        {
            ReportBuilder.Build("{ \"content\": [ { \"text\": \"a\" } ", new[] { vibration }, new Recorder(), null, new List<string>());
            Check("bad JSON: refused", false);
        }
        catch (FormatException ex)
        {
            Check("bad JSON: refused, with where", ex.Message.Contains("line"), ex.Message);
        }

        log.Clear();
        report = ReportBuilder.Build(@"{ ""content"": [ { ""picture"": ""x"" }, { ""heading"": ""Still here"" } ] }", new[] { vibration }, new Recorder(), null, log);
        Check("unknown entry: skipped and logged, the rest made", log.Count == 1 && report.Blocks.OfType<HeadingBlock>().Any());

        // ── Paper, margins, a title block and a cover of the report's own ─────────────────────
        log.Clear();
        var paper = @"{
          ""title"": ""Paper"", ""project"": ""P1"", ""cover"": false, ""width"": 200, ""height"": 100, ""orientation"": ""landscape"",
          ""margins"": { ""top"": 20, ""bottom"": 20, ""left"": 30, ""right"": 15 },
          ""titleBlock"": { ""frame"": [200, 100], ""items"": [
            { ""line"": [[0, 10], [200, 10]], ""colour"": ""#4288F7"", ""width"": 1 },
            { ""fill"": [[0, 95], [200, 95], [200, 100], [0, 100]], ""colour"": ""#2A2D31"" },
            { ""text"": ""Page {page} of {pages} - {project}"", ""at"": [195, 4], ""size"": 3, ""align"": ""right"" }
          ] },
          ""content"": [
            { ""title"": ""A cover of its own"", ""subtitle"": ""Under it"" },
            { ""note"": ""A note, small and grey."" },
            { ""table"": { ""caption"": ""Own table"", ""header"": [""Mode"", ""f"", ""Shape""], ""units"": ["""", ""Hz"", """"],
                         ""rows"": [[1, 4.7, ""sway X""], [2, 6.23, ""sway Y""]] } },
            { ""repeat"": ""mode"", ""of"": ""vibration"", ""content"": [ { ""text"": ""Mode {mode} at {frequency:F2} Hz"" } ] },
            { ""pagebreak"": true },
            { ""text"": ""The second page."" }
          ] }";
        report = ReportBuilder.Build(paper, new[] { vibration }, new Recorder(), null, log);
        Check("paper: 200 x 100 mm, landscape", Math.Abs(report.PageWidth - 200 * 72 / 25.4) < 0.1 && Math.Abs(report.PageHeight - 100 * 72 / 25.4) < 0.1);
        Check("paper: margins in mm", Math.Abs(report.MarginLeft - 30 * 72 / 25.4) < 0.1 && Math.Abs(report.MarginTop - 20 * 72 / 25.4) < 0.1);
        Check("cover: off", !report.Cover);
        Check("cover: a block of its own", report.Blocks.OfType<CoverBlock>().Any(c => c.Title == "A cover of its own" && c.Subtitle == "Under it"));
        Check("note: small", report.Blocks.OfType<ParagraphBlock>().Any(p => p.Small && p.Text.StartsWith("A note")));
        var own = report.Blocks.OfType<TableBlock>().Single();
        Check("own table: units in the header", own.Header.SequenceEqual(new[] { "Mode", "f [Hz]", "Shape" }), string.Join("|", own.Header));
        Check("own table: numbers right, text left", own.Align.SequenceEqual(new[] { TextAlign.Right, TextAlign.Right, TextAlign.Left }));
        Check("own table: the cells as given", own.Rows[1][1] == "6.23" && own.Rows[1][2] == "sway Y");
        Check("repeat: {frequency} of the mode it is on", report.Blocks.OfType<ParagraphBlock>().Any(p => p.Text == "Mode 2 at 2.00 Hz"));
        Check("title block: three items", report.TitleBlock.Count == 3, $"{report.TitleBlock.Count}");
        Check("title block: {project} filled in now, {page} kept", report.TitleBlock.OfType<TextItem>().Single().Text == "Page {page} of {pages} - P1");
        Check("title block: y up from the foot", Math.Abs(report.TitleBlock.OfType<PolylineItem>().Single().Points[0].Y - (report.PageHeight - 10 * 72 / 25.4)) < 0.1);
        Check("title block: nothing logged", log.Count == 0, string.Join(" | ", log));

        pages = ReportLayout.Lay(report);
        Check("title block: on every page", pages.Pages.All(p => p.Items.OfType<PolylineItem>().Count() == 1 && p.Items.OfType<PolygonItem>().Count() == 1));
        Check("title block: page numbers filled in", Enumerable.Range(0, pages.Pages.Count).All(i =>
            pages.Pages[i].Items.OfType<TextItem>().Count(t => t.Text == $"Page {i + 1} of {pages.Pages.Count} - P1") == 1));
        Check("title block: no plain footer as well", pages.Pages.All(p => !p.Items.OfType<TextItem>().Any(t => t.Text.StartsWith("Page ") && !t.Text.EndsWith("P1"))));
        Check("cover: no title at the top of page 1", !pages.Pages[0].Items.OfType<TextItem>().Any(t => t.Text == "Paper"));
        Check("cover: the block set large", pages.Pages[0].Items.OfType<TextItem>().Any(t => t.Text == "A cover of its own" && t.Size == 24.0));
        double left = 30 * 72 / 25.4;
        Check("margins: content inside the left one", pages.Pages.All(p => p.Items.OfType<TextItem>().Where(t => t.Align == TextAlign.Left && t.Size != 3 * 72 / 25.4).All(t => t.X >= left - 1e-6)));

        Check("paper: A2 by name", Math.Abs(PageSizes.Named("A2").Value.Width - 420 * 72 / 25.4) < 0.1);

        // ── Topics: the pushover's model, stability for buckling, a repeat over steps ──────────
        var pushover = new Model { IsAnalysed = true, IsStatic = true, IsPushover = true };
        Check("topic: a pushover's model is its own kind", ModelSet.KindOf(pushover) == "pushover");
        var set = new ModelSet(new[] { vibration, buckling, pushover });
        Check("topic: stability is the buckling model", set.Find("stability", out _) == buckling);
        Check("topic: natural vibration is the vibration model", set.Find("natural vibration", out _) == vibration);
        Check("topic: pushover found", set.Find("pushover", out _) == pushover);
        recorder = new Recorder();
        log.Clear();
        report = ReportBuilder.Build(@"{ ""content"": [ { ""repeat"": ""step"", ""of"": ""pushover"", ""steps"": [3, 7], ""columns"": 2,
            ""content"": [ { ""figure"": { ""result"": ""Displacement"", ""hinges"": true, ""caption"": ""Step {step}"" } } ] } ] }",
            new[] { vibration, buckling, pushover }, recorder, null, log);
        Check("steps: the pushover drawn at each step asked for", recorder.Asked.Count == 2 && recorder.Asked.All(a => a.Model == pushover)
            && recorder.Asked.Select(a => (int)a.Figure["step"]).SequenceEqual(new[] { 3, 7 }), string.Join(" | ", log));
        Check("steps: {step} in the caption", report.Blocks.OfType<FigureRowBlock>().Single().Figures[1].Caption == "Step 7");

        // ── Footfall: its model, its table, its placeholder ─────────────────────────────────────
        var footfallModel = new Model
        {
            IsAnalysed = true, IsModal = true, Eigenvalues = vibration.Eigenvalues,
            Footfall = new FootfallResponse { Nodes = new[] { 4, 9, 12 }, Factors = new[] { 2.5, 7.75, 1.0 }, CriticalFrequencies = new[] { 2.0, 2.1, 1.9 }, Activity = "Walking", Damping = 0.03 }
        };
        Check("footfall: its own kind, though modal", ModelSet.KindOf(footfallModel) == "footfall");
        log.Clear();
        report = ReportBuilder.Build(@"{ ""content"": [ { ""table"": ""footfall.response"" }, { ""text"": ""R at most {footfall.maxFactor:F2}, node {footfall.maxNode}"" } ] }",
            new[] { vibration, footfallModel }, new Recorder(), null, log);
        var ft = report.Blocks.OfType<TableBlock>().Single();
        Check("footfall: worst node first", ft.Rows.Count == 3 && ft.Rows[0][0] == "9" && ft.Rows[0][1] == "7.75", string.Join("|", ft.Rows.Select(r => string.Join(",", r))));
        Check("footfall: largest R in the text", report.Blocks.OfType<ParagraphBlock>().Single().Text == "R at most 7.75, node 9");
        Check("footfall: the vibration model stays vibration", new ModelSet(new[] { vibration, footfallModel }).Find("vibration", out _) == vibration);

        // Its steady state and transient: the table sorted by what is drawn, that column first.
        var parts = new Model
        {
            IsAnalysed = true, IsModal = true, Eigenvalues = vibration.Eigenvalues,
            Footfall = new FootfallResponse
            {
                Nodes = new[] { 4, 9, 12 }, Factors = new[] { 2.5, 7.75, 3.0 }, CriticalFrequencies = new[] { 2.0, 2.1, 1.9 },
                SteadyStateFactors = new[] { 2.5, 1.0, 3.0 }, TransientFactors = new[] { 0.5, 7.75, 0.2 },
                SteadyStateAccelerations = new[] { 0.0125, 0.005, 0.015 }, TransientAccelerations = new[] { 0.0025, 0.03875, 0.001 },
                Activity = "Walking", Damping = 0.03
            }
        };
        log.Clear();
        report = ReportBuilder.Build(@"{ ""content"": [
            { ""table"": ""footfall.response"", ""by"": ""Steady-state R"" },
            { ""table"": ""footfall.response"", ""by"": ""Transient acceleration"", ""columns"": [""Pace""] },
            { ""text"": ""a at most {footfall.maxSteadyAcceleration:F1} mm/s², node {footfall.maxSteadyAccelerationNode}; transient R {footfall.maxTransientFactor:F2}"" } ] }",
            new[] { parts }, new Recorder(), null, log);
        var byPart = report.Blocks.OfType<TableBlock>().ToList();
        Check("footfall: sorted by the steady state, its column first",
              byPart[0].Header.Take(3).SequenceEqual(new[] { "Node", "R steady state", "R" }) && byPart[0].Rows.Select(r => r[0]).SequenceEqual(new[] { "12", "4", "9" }),
              string.Join("|", byPart[0].Header) + " / " + string.Join("|", byPart[0].Rows.Select(r => r[0])));
        Check("footfall: every part a column", byPart[0].Header.Count == 7, string.Join("|", byPart[0].Header));
        Check("footfall: an acceleration in mm/s², kept though not ticked", byPart[1].Header.SequenceEqual(new[] { "Node", "a transient [mm/s²]", "Pace [Hz]" })
              && byPart[1].Rows[0][0] == "9" && byPart[1].Rows[0][1] == "38.75", string.Join("|", byPart[1].Header) + " / " + string.Join(",", byPart[1].Rows[0]));
        Check("footfall: the largest of a part in the text", report.Blocks.OfType<ParagraphBlock>().Single().Text == "a at most 15.0 mm/s², node 12; transient R 7.75",
              report.Blocks.OfType<ParagraphBlock>().Single().Text);

        // A stair has no transient to sort by.
        parts.Footfall.TransientChecked = false;
        log.Clear();
        report = ReportBuilder.Build(@"{ ""content"": [ { ""table"": ""footfall.response"", ""by"": ""Transient R"" }, { ""table"": ""footfall.response"" } ] }",
            new[] { parts }, new Recorder(), null, log);
        var stair = report.Blocks.OfType<TableBlock>().ToList();
        Check("footfall: no transient on a stair, and said why", stair.Count == 1 && !stair[0].Header.Any(h => h.Contains("transient"))
              && string.Join(" ", log).Contains("transient response was not checked"), string.Join(" | ", log));

        // ── What a result table shows ──────────────────────────────────────────────────────────
        log.Clear();
        report = ReportBuilder.Build(@"{ ""content"": [
            { ""table"": ""vibration.modes"", ""columns"": [""Frequency"", ""Period""] },
            { ""table"": ""buckling.factors"", ""columns"": [] },
            { ""table"": ""footfall.response"", ""columns"": [""Note""] },
            { ""table"": ""buckling.factors"", ""columns"": [""Note below""] } ] }",
            new[] { vibration, buckling, footfallModel }, new Recorder(), null, log);
        var tables = report.Blocks.OfType<TableBlock>().ToList();
        Check("columns: frequencies with f and T alone", tables[0].Header.SequenceEqual(new[] { "Mode", "f [Hz]", "T [s]" }) && tables[0].Rows[1].Count == 3,
              string.Join("|", tables[0].Header));
        Check("columns: the note left out", tables[1].Note == "" && tables[1].Rows.Count == 2);
        Check("columns: footfall without its pace, with its note, as a file saved before names it", tables[2].Header.Count == 2 && tables[2].Note.Length > 0);
        Check("columns: the note kept by its name in the menu", tables[3].Note.Length > 0);
        Check("columns: every table's options listed", ReportTables.Catalogue.All(c => ReportTables.Options.ContainsKey(c.Name)));

        // ── Headings numbered as they come: 1, 1.1, 1.3.2 ──────────────────────────────────────
        const string outline = @"{ ""cover"": false, ""content"": [
            { ""heading"": ""One"" }, { ""subheading"": ""A"" }, { ""subheading"": ""B"" }, { ""subheading"": ""C"" },
            { ""subsubheading"": ""C first"" }, { ""subsubheading"": ""C second"" },
            { ""heading"": ""Two"" }, { ""subheading"": ""A"" },
            { ""heading"": ""Appendix"", ""numbered"": false }, { ""heading"": ""Part"", ""level"": 3 } ] }";
        log.Clear();
        report = ReportBuilder.Build(outline, new[] { vibration }, new Recorder(), null, log);
        var numbers = report.Blocks.OfType<HeadingBlock>().Select(h => h.Number).ToList();
        Check("headings: numbered as they come", numbers.SequenceEqual(new[] { "1", "1.1", "1.2", "1.3", "1.3.1", "1.3.2", "2", "2.1", "", "2.1.1" }),
              string.Join(",", numbers));
        Check("headings: three levels", report.Blocks.OfType<HeadingBlock>().Count(h => h.Level == 3) == 3);
        var numbered = ReportLayout.Lay(report);
        var at = numbered.Pages[0].Items.OfType<TextItem>().ToList();
        var number = at.FirstOrDefault(t => t.Text == "1.3.1");
        var headingText = at.FirstOrDefault(t => t.Text == "C first");
        Check("headings: the number on the heading's line, its text after it",
              number != null && headingText != null && Math.Abs(number.Y - headingText.Y) < 1e-6 && headingText.X > number.X,
              string.Join(" | ", Texts(numbered, 0).Take(12)));
        Check("headings: an appendix left as it is", at.Any(t => t.Text == "Appendix") && !at.Any(t => t.Text == "3"));

        // Headings at the foot of a page go over with the picture after them, not leave it alone.
        doc = new ReportDocument { Title = "Orphans" };
        doc.Blocks.Add(new ParagraphBlock { Text = string.Join("\n", Enumerable.Repeat("A line of text.", 34)) });
        doc.Blocks.Add(new HeadingBlock { Text = "Forces", Level = 2, Number = "2.2" });
        doc.Blocks.Add(new HeadingBlock { Text = "In the members", Level = 3, Number = "2.2.1" });
        doc.Blocks.Add(new FigureBlock { ImagePath = "a.png", PixelWidth = 2000, PixelHeight = 1240, Caption = "Members" });
        pages = ReportLayout.Lay(doc);
        Check("headings: kept with the picture after them", pages.Pages.Count == 2 && Texts(pages, 1).Contains("Forces")
              && Texts(pages, 1).Contains("In the members") && pages.Pages[1].Items.OfType<ImageItem>().Count() == 1,
              $"{pages.Pages.Count} pages; page 1: {string.Join(" | ", Texts(pages, 0).Skip(30).Take(6))}");

        report = ReportBuilder.Build(outline.Replace(@"""cover"": false,", @"""cover"": false, ""numbered"": false,"), new[] { vibration }, new Recorder(), null, log);
        Check("headings: not numbered when the template says so", report.Blocks.OfType<HeadingBlock>().All(h => h.Number == ""));

        // ── A picture with its tables, as Report View hands them out ───────────────────────────
        log.Clear();
        recorder = new Recorder();
        report = ReportBuilder.Build(@"{ ""content"": [ { ""group"": [
            { ""table"": ""vibration.modes"", ""of"": ""vibration"", ""columns"": [""Frequency""] },
            { ""repeat"": ""mode"", ""of"": ""vibration"", ""modes"": ""1-2"", ""columns"": 2, ""content"": [ { ""figure"": { ""result"": ""Displacement"" } } ] },
            { ""table"": ""buckling.factors"", ""of"": ""buckling"" } ] } ] }",
            new[] { vibration, buckling }, recorder, null, log);
        var grouped = report.Blocks.Where(b => !(b is CoverBlock)).ToList();
        Check("group: table, pictures, table, in that order", grouped.Count == 3 && grouped[0] is TableBlock && grouped[1] is FigureRowBlock && grouped[2] is TableBlock,
              string.Join(", ", grouped.Select(b => b.GetType().Name)));
        Check("group: the first table's columns as ticked", grouped[0] is TableBlock first && first.Header.SequenceEqual(new[] { "Mode", "f [Hz]" }),
              grouped[0] is TableBlock t0 ? string.Join("|", t0.Header) : "");
        Check("group: the pictures of the model it names", recorder.Asked.Count == 2 && recorder.Asked.All(a => a.Model == vibration));
        Check("group: nothing logged", log.Count == 0, string.Join(" | ", log));

        // ── The tables of what a picture draws ─────────────────────────────────────────────────
        log.Clear();
        recorder = new Recorder();
        var staticModel = new Model { IsAnalysed = true };
        report = ReportBuilder.Build(@"{ ""content"": [
            { ""figure"": { ""model"": ""static"", ""step"": 0, ""results"": [
                { ""result"": ""Displacement"", ""component"": ""Uz"", ""unit"": ""mm"", ""table"": true },
                { ""result"": ""Beam forces"", ""forces"": [""Mz""] },
                { ""result"": ""Reactions"", ""component"": ""Force"", ""table"": true } ] } },
            { ""figure"": { ""model"": ""static"", ""step"": 0, ""results"": [ { ""result"": ""Displacement"", ""component"": ""Uz"" } ] } } ] }",
            new[] { staticModel }, recorder, null, log);
        var after = report.Blocks.Where(b => !(b is CoverBlock)).ToList();
        Check("result tables: picture, its two tables, then the next picture", after.Count == 4 && after[0] is FigureBlock && after[1] is TableBlock && after[2] is TableBlock && after[3] is FigureBlock,
              string.Join(", ", after.Select(b => b.GetType().Name)));
        Check("result tables: read once, for the picture that asks", recorder.ValuesAsked == 1, $"{recorder.ValuesAsked}");
        var moved = after[1] as TableBlock;
        Check("result tables: a row for the component drawn, largest and smallest with their nodes",
              moved != null && moved.Caption == "Displacement" && moved.Header.SequenceEqual(new[] { "Component", "Largest", "Node", "Smallest", "Node" })
              && moved.Rows.Count == 1 && moved.Rows[0][0] == "Uz [mm]" && moved.Rows[0][1] == "4.25" && moved.Rows[0][2] == "7" && moved.Rows[0][3] == "-12.5" && moved.Rows[0][4] == "3",
              moved == null ? "" : string.Join("|", moved.Header) + " / " + string.Join("|", moved.Rows[0]));
        var reactions = after[2] as TableBlock;
        Check("result tables: reactions a row per support, a column per force, summed",
              reactions != null && reactions.Caption == "Support reactions" && reactions.Header.SequenceEqual(new[] { "Support", "Fx [kN]", "Fy [kN]", "Fz [kN]" })
              && reactions.Rows.Count == 3 && reactions.Rows[2][0] == "Sum" && reactions.Rows[2][3] == "40" && reactions.Bold.Contains(2),
              reactions == null ? "" : string.Join(" / ", reactions.Rows.Select(r => string.Join("|", r))));
        Check("result tables: beam forces left out of a model with no beams, and said so",
              log.Count == 1 && log[0].Contains("beam forces left out") && !recorder.Asked[0].Figure["results"].Any(r => (string)r["result"] == "Beam forces"),
              string.Join(" | ", log));

        // ── A picture of results the model cannot have ─────────────────────────────────────────
        log.Clear();
        recorder = new Recorder();
        report = ReportBuilder.Build(@"{ ""content"": [ { ""heading"": ""Shells"" },
            { ""figure"": { ""model"": ""static"", ""results"": [ { ""result"": ""Shell forces"", ""component"": ""Mxx"", ""table"": true } ] } },
            { ""figures"": [ { ""model"": ""static"", ""result"": ""Brick stresses"" }, { ""model"": ""static"", ""result"": ""Displacement"" } ] } ] }",
            new[] { staticModel }, recorder, null, log);
        var lacking = report.Blocks.Where(b => !(b is CoverBlock)).ToList();
        Check("left out: a picture of shell forces of a frame - no picture, no table, no placeholder",
              lacking.Count == 2 && lacking[0] is HeadingBlock && lacking[1] is FigureRowBlock row && row.Figures.Count == 1 && recorder.Asked.Count == 1,
              string.Join(", ", lacking.Select(b => b.GetType().Name)));
        Check("left out: each said so in the Log", log.Count == 2 && log[0].Contains("shells") && log[1].Contains("solid elements"), string.Join(" | ", log));

        // In a repeat, one set of tables - of the last pass - after all the pictures.
        recorder = new Recorder();
        report = ReportBuilder.Build(@"{ ""content"": [ { ""repeat"": ""mode"", ""of"": ""vibration"", ""columns"": 2, ""content"": [
            { ""figure"": { ""results"": [ { ""result"": ""Displacement"", ""component"": ""Magnitude"", ""table"": true } ] } } ] } ] }",
            new[] { vibration }, recorder, null, log);
        var repeated = report.Blocks.Where(b => !(b is CoverBlock)).ToList();
        Check("result tables: a repeat's table once, after its rows", repeated.Count == 3 && repeated[0] is FigureRowBlock && repeated[1] is FigureRowBlock && repeated[2] is TableBlock,
              string.Join(", ", repeated.Select(b => b.GetType().Name)));
        Check("result tables: read on the last pass only", recorder.ValuesAsked == 1, $"{recorder.ValuesAsked}");

        // ── A cover's title is the report's ────────────────────────────────────────────────────
        report = ReportBuilder.Build(@"{ ""cover"": false, ""titleBlock"": { ""items"": [ { ""text"": ""{title}"", ""at"": [20, 10] } ] },
            ""content"": [ { ""title"": ""Bridge over the river"", ""subtitle"": ""Checks"" } ] }", new[] { vibration }, new Recorder(), null, log);
        Check("cover: its title is the report's", report.Title == "Bridge over the river");
        Check("cover: and the title block's {title}", report.TitleBlock.OfType<TextItem>().Single().Text == "Bridge over the river");

        // ── The default template ───────────────────────────────────────────────────────────────
        var standard = JObject.Parse(ReportBuilder.DefaultTemplate(new[] { vibration, buckling }));
        var headings = standard["content"].Select(b => (string)b["heading"]).Where(h => h != null).ToList();
        Check("default: the model, vibration and buckling, and what it was made with",
              headings.SequenceEqual(new[] { "The model", "Natural vibration", "Linear buckling", "About this calculation" }), string.Join(", ", headings));
        Check("default: contents first", (string)standard["content"][0]["contents"] == "Contents");
        recorder = new Recorder();
        log.Clear();
        report = ReportBuilder.Build(standard.ToString(), new[] { vibration, buckling }, recorder, null, log);
        var all = ReportLayout.Lay(report);
        Check("default: laid out", all.Pages.Count >= 2, $"{all.Pages.Count} pages");
        Func<JObject, bool> Mode = f => ((string)f["caption"] ?? "").StartsWith("Mode");
        Check("default: three vibration and two buckling modes drawn",
            recorder.Asked.Count(a => Mode(a.Figure) && a.Model == vibration) == 3 && recorder.Asked.Count(a => Mode(a.Figure) && a.Model == buckling) == 2);

        Contents();
        Combinations();
        LoadTables();
        Word();

        Console.WriteLine($"\n{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    // ── The table of contents ──────────────────────────────────────────────────────────────

    static void Contents()
    {
        var doc = new ReportDocument { Cover = false };
        doc.Blocks.Add(new ContentsBlock { Title = "Contents", Depth = 2 });
        doc.Blocks.Add(new HeadingBlock { Level = 1, Text = "The model", Number = "1" });
        doc.Blocks.Add(new ParagraphBlock { Text = "Text." });
        doc.Blocks.Add(new PageBreakBlock());
        doc.Blocks.Add(new HeadingBlock { Level = 1, Text = "Static analysis", Number = "2" });
        doc.Blocks.Add(new HeadingBlock { Level = 2, Text = "Member forces", Number = "2.1" });
        doc.Blocks.Add(new HeadingBlock { Level = 3, Text = "Too deep to list", Number = "2.1.1" });
        doc.Blocks.Add(new PageBreakBlock());
        doc.Blocks.Add(new HeadingBlock { Level = 1, Text = "Conclusions", Number = "3" });
        var pages = ReportLayout.Lay(doc);
        var first = pages.Pages[0].Items.OfType<TextItem>().ToList();
        double right = doc.PageWidth - doc.MarginRight;
        var numbers = first.Where(t => t.Align == TextAlign.Right && Math.Abs(t.X - right) < 0.01 && !t.Text.StartsWith("Page")).Select(t => t.Text).ToList();
        Check("contents: the headings down to its depth, each with its page", numbers.SequenceEqual(new[] { "1", "2", "2", "3" }), string.Join(",", numbers));
        Check("contents: its title, then the numbers and texts",
              first.Any(t => t.Text == "Contents" && t.Bold) && first.Any(t => t.Text == "2.1") && first.Any(t => t.Text == "Member forces") && !first.Any(t => t.Text == "Too deep to list"));
        Check("contents: the headings on their pages", pages.Pages.Count == 3);

        var log = new List<string>();
        var built = ReportBuilder.Build(@"{ ""content"": [ { ""contents"": true, ""depth"": 1 }, { ""heading"": ""A"" } ] }", new[] { new Model() }, null, null, log);
        var block = built.Blocks.OfType<ContentsBlock>().SingleOrDefault();
        Check("contents: from a template, true for Contents", block != null && block.Title == "Contents" && block.Depth == 1);
    }

    // ── Load combinations ──────────────────────────────────────────────────────────────────

    static Alpaca4d.Loads.LoadPattern Pattern(string name, double factor, params Alpaca4d.Generic.ILoad[] loads) =>
        new Alpaca4d.Loads.LoadPattern(Alpaca4d.Loads.PatternType.Plain, Alpaca4d.TimeSeries.Constant.Default(), loads.ToList(), factor) { Name = name };

    static void Combinations()
    {
        string problem;
        var c = Alpaca4d.Loads.LoadCombination.Parse("ULS 1", "1.35 G + 1.5*Q + -0.9 Wind -X + g", Alpaca4d.Loads.CombinationKind.ULS, out problem);
        Check("combination: read from its formula",
              c != null && c.Terms.Count == 3 && c.Terms[0].Equals(("G", 2.35)) && c.Terms[1].Equals(("Q", 1.5)) && c.Terms[2].Equals(("Wind -X", -0.9)),
              c == null ? problem : string.Join("; ", c.Terms));
        Check("combination: written back", c != null && c.Formula == "2.35 G + 1.5 Q + -0.9 Wind -X", c?.Formula);
        Check("combination: a factor with no pattern is refused",
              Alpaca4d.Loads.LoadCombination.Parse("", "1.35 G + 1.5", Alpaca4d.Loads.CombinationKind.ULS, out problem) == null && problem.Contains("\"1.5\""), problem);
        Check("combination: no name, the formula", Alpaca4d.Loads.LoadCombination.Parse(" ", "G + 1.5 Q", Alpaca4d.Loads.CombinationKind.SLSCharacteristic, out _).Name == "G + 1.5 Q");

        var model = new Model { Tcl = new List<string> { "model basic -ndm 3 -ndf 6", "node 1 0 0 0" }, LoadPatternTclStart = 2 };
        model.LoadPatterns = new List<Alpaca4d.Loads.LoadPattern> { Pattern("G", 1.0), Pattern("Q", 1.0), Pattern("W", 1.0) };
        model.Tcl.Add("pattern G"); model.Tcl.Add("pattern Q"); model.Tcl.Add("pattern W");
        var uls = Alpaca4d.Loads.LoadCombination.Parse("ULS 1", "1.35 g + 1.5 Q", Alpaca4d.Loads.CombinationKind.ULS, out _);
        var combined = uls.Apply(model, out problem);
        Check("combination: the model with its patterns alone, scaled",
              combined != null && combined.LoadPatterns.Count == 2 && combined.LoadPatterns[0].Name == "G" && combined.LoadPatterns[0].Factor == 1.35 && combined.LoadPatterns[1].Factor == 1.5,
              problem);
        Check("combination: its deck the model's up to the patterns, then the two",
              combined != null && combined.Tcl.Count == 4 && combined.Tcl[1] == "node 1 0 0 0" && combined.Tcl[2].Contains("-fact 1.35") && combined.Tcl[3].Contains("-fact 1.5"),
              combined == null ? "" : string.Join(" | ", combined.Tcl.Skip(2)));
        Check("combination: knows what it is, and the model it came from",
              combined?.Combination?.Name == "ULS 1" && combined.Combination.Source == model && combined.Combination.IsUltimate);
        Check("combination: the model given unchanged", model.LoadPatterns.Count == 3 && model.Tcl.Count == 5 && model.Combination == null);
        var wrong = Alpaca4d.Loads.LoadCombination.Parse("", "1.5 Snow", Alpaca4d.Loads.CombinationKind.ULS, out _).Apply(model, out problem);
        Check("combination: a name no pattern has, and the names there are", wrong == null && problem.Contains("Snow") && problem.Contains("G, Q, W"), problem);
        Check("combination: an analysed model is refused", uls.Apply(new Model { IsAnalysed = true, LoadPatternTclStart = 0 }, out problem) == null && problem.Contains("analysed"), problem);

        // The tables of them.
        var sls = Alpaca4d.Loads.LoadCombination.Parse("SLS 1", "G + Q", Alpaca4d.Loads.CombinationKind.SLSCharacteristic, out _).Apply(model, out _);
        var set = new ModelSet(new[] { combined, sls, combined });
        var table = ReportTables.Make("loads.combinations", set, null, null, out problem);
        Check("combination table: each once, its type and formula",
              table != null && table.Rows.Count == 2 && table.Rows[0].SequenceEqual(new[] { "ULS 1", "ULS", "1.35 G + 1.5 Q" }) && table.Rows[1][1] == "SLS characteristic",
              table == null ? problem : string.Join(" / ", table.Rows.Select(r => string.Join("|", r))));
        Check("combination table: none given, why", ReportTables.Make("loads.combinations", new ModelSet(new[] { model }), null, null, out problem) == null && problem.Contains("Load Combination"), problem);

        // The type, as Load Combination's Type input takes it.
        Alpaca4d.Loads.CombinationKind kind;
        Check("type: empty is ULS", Alpaca4d.Loads.LoadCombination.TryKind("  ", out kind, out _) && kind == Alpaca4d.Loads.CombinationKind.ULS);
        Check("type: whatever its case, spaces and hyphens",
              Alpaca4d.Loads.LoadCombination.TryKind("sls-quasi-permanent", out kind, out _) && kind == Alpaca4d.Loads.CombinationKind.SLSQuasiPermanent
              && Alpaca4d.Loads.LoadCombination.TryKind("SLS characteristic", out kind, out _) && kind == Alpaca4d.Loads.CombinationKind.SLSCharacteristic
              && Alpaca4d.Loads.LoadCombination.TryKind("seismic", out kind, out _) && kind == Alpaca4d.Loads.CombinationKind.Seismic);
        Check("type: SLS alone is three, and says so", !Alpaca4d.Loads.LoadCombination.TryKind("SLS", out _, out problem) && problem.Contains("quasi-permanent"), problem);
        Check("type: anything else lists the types", !Alpaca4d.Loads.LoadCombination.TryKind("Wind", out _, out problem) && problem.Contains("SLS frequent"), problem);

        // Added to the model, in one go or a chain.
        var gq = Alpaca4d.Loads.LoadCombination.Parse("SLS 1", "g + q", Alpaca4d.Loads.CombinationKind.SLSCharacteristic, out _);
        var with = Alpaca4d.Loads.LoadCombination.AddTo(model, new[] { uls, gq }, out problem);
        Check("add: the model with both, named as its patterns are",
              with != null && with.Combinations.Count == 2 && with.Combinations[1].Formula == "G + Q" && with.Combinations[0].Name == "ULS 1" && model.Combinations.Count == 0,
              problem);
        var chained = Alpaca4d.Loads.LoadCombination.AddTo(with, new[] { Alpaca4d.Loads.LoadCombination.Parse("", "G + 0.3 Q", Alpaca4d.Loads.CombinationKind.SLSQuasiPermanent, out _) }, out problem);
        Check("add: a chain adds to what the model has", chained != null && chained.Combinations.Count == 3 && chained.Combinations[2].Name == "G + 0.3 Q" && with.Combinations.Count == 2, problem);
        Check("add: a name taken is refused", Alpaca4d.Loads.LoadCombination.AddTo(with, new[] { uls }, out problem) == null && problem.Contains("ULS 1"), problem);
        Check("add: a pattern it has not, led by the combination's name",
              Alpaca4d.Loads.LoadCombination.AddTo(model, new[] { Alpaca4d.Loads.LoadCombination.Parse("ULS 9", "1.5 Snow", Alpaca4d.Loads.CombinationKind.ULS, out _) }, out problem) == null
              && problem.StartsWith("ULS 9:") && problem.Contains("G, Q, W"), problem);

        // Each combination's model, as Run Analysis solves it: its patterns alone, scaled, under fresh tags.
        var each = chained.Combinations.Select(x => x.Apply(chained, out problem)).ToList();
        Check("each: its patterns, scaled, and the combination it is",
              each.All(m => m != null) && each[2].Combination.Name == "G + 0.3 Q" && each[2].Combination.Source == chained && each[0].LoadPatterns.Count == 2
              && each[2].Tcl.Skip(each[2].LoadPatternTclStart).Any(l => l.Contains("-fact 0.3")) && each[2].Combinations.Count == 3,
              problem);

        // One model holding them, each picked by its name or place.
        each[0].IsAnalysed = true; each[2].IsAnalysed = true;
        each[1].CombinationFailure = "it did not converge";
        var handed = each[0].ShallowCopy();
        handed.CombinationResults = each;
        Check("pick: empty is the first that solved", handed.OfCombination(null, out _) == each[0]);
        Check("pick: by name, whatever its case, and by place", handed.OfCombination("g + 0.3 q", out _) == each[2] && handed.OfCombination("2", out _) == each[2]);
        Check("pick: one with no results says why", handed.OfCombination("SLS 1", out problem) == null && problem.Contains("did not converge"), problem);
        Check("pick: a name it has not lists the names", handed.OfCombination("ULS 5", out problem) == null && problem.Contains("ULS 1, SLS 1, G + 0.3 Q"), problem);
        Check("pick: a model without combinations is itself, and refuses a name",
              model.OfCombination("", out _) == model && model.OfCombination("ULS 1", out problem) == null, problem);
        var held = new ModelSet(new[] { handed });
        Check("report: a model holding combinations counts as each", held.All.Count == 3 && ModelSet.KindOf(held.All[1]) == "failed" && held.Find("static", out _) == each[0]);
        var listed = ReportTables.Make("loads.combinations", held, null, null, out problem);
        Check("report: the table of combinations lists all three", listed != null && listed.Rows.Count == 3, problem);
        var analysed = ReportTables.Make("report.analyses", held, null, null, out problem);
        Check("report: the one with no results says so", analysed != null && analysed.Rows[1][1].Contains("did not converge"),
              analysed == null ? problem : string.Join(" / ", analysed.Rows.Select(r => string.Join("|", r))));
    }

    static void LoadTables()
    {
        string problem;
        var down = new Alpaca4d.Loads.PointLoad(1, new Rhino.Geometry.Vector3d(0, 0, -10), new Rhino.Geometry.Vector3d(), Alpaca4d.TimeSeries.Constant.Default());
        var side = new Alpaca4d.Loads.PointLoad(2, new Rhino.Geometry.Vector3d(4, 0, 0), new Rhino.Geometry.Vector3d(), Alpaca4d.TimeSeries.Constant.Default());
        var model = new Model { LoadPatterns = new List<Alpaca4d.Loads.LoadPattern> { Pattern("G", 1.5, down, down), Pattern(null, 1.0, side) } };
        var table = ReportTables.Make("loads.patterns", new ModelSet(new[] { model }), null, null, out problem);
        Check("load patterns: each with its loads and the force they add up to, its factor on",
              table != null && table.Rows.Count == 3 && table.Rows[0].SequenceEqual(new[] { "G", "1.50", "2 point", "0.00", "0.00", "-30.00" })
              && table.Rows[1][0] == "Pattern 2" && table.Rows[1][3] == "4.00",
              table == null ? problem : string.Join(" / ", table.Rows.Select(r => string.Join("|", r))));
        Check("load patterns: the total, bold", table != null && table.Rows[2][0] == "Total" && table.Rows[2][5] == "-30.00" && table.Bold.Contains(2));
        var some = ReportTables.Make("loads.patterns", new ModelSet(new[] { model }), null, null, new[] { "Fz" }, out problem);
        Check("load patterns: columns and the total as ticked", some != null && some.Header.Count == 2 && some.Rows.Count == 2, some == null ? problem : string.Join("|", some.Header));

        var about = ReportTables.Make("report.about", new ModelSet(new[] { model, new Model { IsAnalysed = true, SolverVersion = "OpenSees 3.8.0" } }), null, null, out problem);
        var labels = about?.Rows.Select(r => r[0]).ToList() ?? new List<string>();
        Check("about: software, solver, units, models and when",
              labels.SequenceEqual(new[] { "Software", "Solver", "Units", "Models", "Written" }) && about.Rows[0][1].StartsWith("Alpaca4d 0.") && about.Rows[1][1] == "OpenSees 3.8.0"
              && about.Rows[3][1].StartsWith("2 given"),
              about == null ? problem : string.Join(" / ", about.Rows.Select(r => string.Join("|", r))));
        var analyses = ReportTables.Make("report.analyses", new ModelSet(new[] { model, new Model { IsAnalysed = true } }), null, null, out problem);
        Check("analyses: a row a model, what it is", analyses != null && analyses.Rows.Count == 2 && analyses.Rows[0][1] == "Not analysed" && analyses.Rows[1][1] == "Static",
              analyses == null ? problem : string.Join(" / ", analyses.Rows.Select(r => string.Join("|", r))));
        var fewer = ReportTables.Make("report.about", new ModelSet(new[] { model }), null, null, new[] { "Software", "Solver" }, out problem);
        Check("about: rows as ticked", fewer != null && fewer.Rows.Count == 2, fewer == null ? problem : string.Join(",", fewer.Rows.Select(r => r[0])));
    }

    // ── Word ───────────────────────────────────────────────────────────────────────────────

    static void Word()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "alpaca-docx-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        System.IO.Directory.CreateDirectory(folder);
        string picture = System.IO.Path.Combine(folder, "figure-01.png");
        // A one-pixel PNG, as its bytes: no GDI+ needed.
        System.IO.File.WriteAllBytes(picture, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

        var doc = new ReportDocument { Title = "Footbridge & ramp", Subtitle = "Checks" };
        doc.Details.Add(new KeyValuePair<string, string>("Date", "2026-11-16"));
        doc.Blocks.Add(new ContentsBlock());
        doc.Blocks.Add(new HeadingBlock { Level = 1, Text = "The model", Number = "1" });
        doc.Blocks.Add(new ParagraphBlock { Text = "Two lines:\nthe second <here>." });
        doc.Blocks.Add(new TableBlock { Caption = "Revisions", Numbered = false, Header = { "Revision", "Date" }, Rows = { new List<string> { "A", "2026-11-16" } } });
        doc.Blocks.Add(new TableBlock { Caption = "Support reactions", Header = { "Node", "Fz [kN]" }, Align = { TextAlign.Right, TextAlign.Right }, Rows = { new List<string> { "1", "10.00" }, new List<string> { "Sum", "10.00" } }, Bold = { 1 }, Note = "In the global axes." });
        doc.Blocks.Add(new HeadingBlock { Level = 2, Text = "Displacement", Number = "1.1" });
        doc.Blocks.Add(new FigureBlock { ImagePath = picture, PixelWidth = 40, PixelHeight = 20, Caption = "Displacement",
            Legend = new FigureLegend { Title = "Uz [mm]", Values = { 0, 1, 2 }, Colours = { Color.Blue, Color.Green, Color.Red } } });
        doc.Blocks.Add(new FigureRowBlock { Columns = 2, Figures = { new FigureBlock { ImagePath = picture, PixelWidth = 40, PixelHeight = 20, Caption = "Mode 1" } } });
        doc.Blocks.Add(new PageBreakBlock());

        string path = System.IO.Path.Combine(folder, "report.docx");
        ReportDocx.Write(doc, path);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Check("word: the parts of a Word document, and the pictures",
                  new[] { "[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/styles.xml", "word/_rels/document.xml.rels", "word/header1.xml", "word/footer1.xml", "word/media/image1.png", "word/media/image2.png" }.All(names.Contains),
                  string.Join(", ", names));
            Func<string, string> Read = name => { using (var r = new System.IO.StreamReader(zip.GetEntry(name).Open())) return r.ReadToEnd(); };
            bool wellFormed = true; string why = "";
            foreach (var name in names.Where(n => n.EndsWith(".xml") || n.EndsWith(".rels")))
            {
                try { System.Xml.Linq.XDocument.Parse(Read(name)); }
                catch (Exception ex) { wellFormed = false; why += name + ": " + ex.Message + " "; }
            }
            Check("word: every part is well-formed XML", wellFormed, why);
            var body = Read("word/document.xml");
            Check("word: headings in Word's styles, the numbers the report gave them",
                  body.Contains("w:val=\"Heading1\"") && body.Contains("1   The model") && body.Contains("w:val=\"Heading2\""));
            Check("word: a contents field, and its headings until Word fills it", body.Contains("TOC \\o \"1-2\"") && Read("word/settings.xml").Contains("updateFields"));
            Check("word: tables numbered as in the PDF, the revisions not", body.Contains(">Table 1  <") && !body.Contains("Table 2") && body.Contains(">Revisions<"));
            Check("word: text escaped, line breaks kept", body.Contains("&lt;here&gt;") && body.Contains("<w:br/>"));
            Check("word: figures numbered, in a row of two", body.Contains(">Figure 1  <") && body.Contains(">Figure 2  <") && body.Contains("r:embed=\"rIdImage2\""));
            Check("word: the page as the report's, and a page number", body.Contains("w:w=\"11906\"") && Read("word/footer1.xml").Contains("NUMPAGES"));
        }

        // What another program makes of it: macOS's own converter reads Word documents.
        if (System.IO.File.Exists("/usr/bin/textutil"))
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/textutil", $"-convert txt -stdout \"{path}\"") { RedirectStandardOutput = true, UseShellExecute = false });
            string text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Check("word: another program reads it", p.ExitCode == 0 && text.Contains("Footbridge & ramp") && text.Contains("Support reactions") && text.Contains("10.00"), text.Length > 300 ? text.Substring(0, 300) : text);
        }
    }
}
