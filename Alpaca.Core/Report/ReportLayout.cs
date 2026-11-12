using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace Alpaca4d.Report
{
    // ── What a page is drawn with ───────────────────────────────────────────────────────────────
    //
    // Points (1/72 inch), from the top left corner of the page, y going down: the layout knows
    // nothing of how a PDF counts, and the writer turns these into whatever it needs.

    public abstract class PageItem
    {
    }

    /// <summary>A line of text. Y is its baseline; X its left end, middle or right end by Align.</summary>
    public sealed class TextItem : PageItem
    {
        public double X, Y, Size;
        public bool Bold;
        public string Text = "";
        public Color Colour;
        public TextAlign Align;
    }

    public sealed class LineItem : PageItem
    {
        public double X1, Y1, X2, Y2, Width;
        public Color Colour;
    }

    /// <summary>A rectangle from its top left corner. Color.Empty leaves out the fill, or the outline.</summary>
    public sealed class RectItem : PageItem
    {
        public double X, Y, W, H, StrokeWidth;
        public Color Fill = Color.Empty;
        public Color Stroke = Color.Empty;
    }

    /// <summary>A picture from its top left corner, stretched to W by H.</summary>
    public sealed class ImageItem : PageItem
    {
        public string Path = "";
        public double X, Y, W, H;
    }

    /// <summary>An open line through its points.</summary>
    public sealed class PolylineItem : PageItem
    {
        public List<PointF> Points = new List<PointF>();
        public Color Colour;
        public double Width;
    }

    /// <summary>A closed outline through its points. Color.Empty leaves out the fill, or the outline.</summary>
    public sealed class PolygonItem : PageItem
    {
        public List<PointF> Points = new List<PointF>();
        public Color Fill = Color.Empty;
        public Color Stroke = Color.Empty;
        public double Width;
    }

    public sealed class ReportPage
    {
        public List<PageItem> Items = new List<PageItem>();
    }

    public sealed class ReportPages
    {
        public double Width, Height;
        public List<ReportPage> Pages = new List<ReportPage>();
    }

    /// <summary>
    /// How wide a line of text is set in Helvetica, or Arial, which has the same widths: what the
    /// layout wraps lines and sizes table columns by. The widths are Helvetica's own, in thousandths
    /// of the type size, for the printable ASCII characters; anything else - σ, ω, ², é - is taken
    /// to be as wide as a digit, which is near enough for wrapping.
    /// </summary>
    public static class TextMetrics
    {
        private static readonly short[] Regular =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
        };

        private static readonly short[] Bold =
        {
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
        };

        public static double Width(string text, double size, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return 0.0;

            var table = bold ? Bold : Regular;
            double units = 0.0;
            foreach (char c in text)
                units += c >= 32 && c <= 126 ? table[c - 32] : 556;
            return units * size / 1000.0;
        }

        /// <summary>
        /// The text broken into lines no wider than <paramref name="width"/>, at spaces, and at its own
        /// line breaks. The first line is <paramref name="firstIndent"/> shorter, to leave room for a
        /// label in front of it. A word too long for a line on its own is broken where it has to be.
        /// </summary>
        public static List<string> Wrap(string text, double size, bool bold, double width, double firstIndent = 0.0)
        {
            var lines = new List<string>();
            if (text == null) return lines;

            foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = "";
                foreach (var word in paragraph.Split(' '))
                {
                    double room = width - (lines.Count == 0 ? firstIndent : 0.0);
                    var tried = line.Length == 0 ? word : line + " " + word;
                    if (Width(tried, size, bold) <= room || line.Length == 0 && Width(word, size, bold) <= room)
                    {
                        line = tried;
                        continue;
                    }

                    if (line.Length > 0)
                    {
                        lines.Add(line);
                        line = "";
                        room = width;
                    }

                    // The word alone, broken into as many lines as it takes.
                    var rest = word;
                    while (Width(rest, size, bold) > room && rest.Length > 1)
                    {
                        int fit = 1;
                        while (fit < rest.Length && Width(rest.Substring(0, fit + 1), size, bold) <= room) fit++;
                        lines.Add(rest.Substring(0, fit));
                        rest = rest.Substring(fit);
                        room = width;
                    }
                    line = rest;
                }
                lines.Add(line);
            }

            return lines;
        }

        /// <summary>The text cut short with an ellipsis to fit <paramref name="width"/>.</summary>
        public static string Fit(string text, double size, bool bold, double width)
        {
            if (Width(text, size, bold) <= width) return text;
            for (int n = text.Length - 1; n > 0; n--)
            {
                var cut = text.Substring(0, n) + "…";
                if (Width(cut, size, bold) <= width) return cut;
            }
            return "";
        }
    }

    /// <summary>
    /// Puts a <see cref="ReportDocument"/> on pages: the title on the first, then each block in turn,
    /// starting a page where the next does not fit. A heading is kept with what follows it, a table
    /// carries its header onto each page it runs over, and a figure is kept whole. Tables and figures
    /// are numbered as they come. The header and the footer, with the page numbers, go on last, once
    /// it is known how many pages there are.
    /// </summary>
    public sealed class ReportLayout
    {
        // The brand's colours: Dark Tech for the text, Alpaca Light Blue for the accents.
        public static readonly Color Ink = Color.FromArgb(42, 45, 49);
        public static readonly Color Muted = Color.FromArgb(108, 114, 124);
        public static readonly Color Accent = Color.FromArgb(66, 136, 247);
        public static readonly Color Rule = Color.FromArgb(200, 205, 214);
        public static readonly Color Stripe = Color.FromArgb(244, 246, 249);
        public static readonly Color HeaderFill = Color.FromArgb(230, 238, 253);


        private const double BodySize = 10.0;
        private const double BodyLeading = 14.5;
        private const double CaptionSize = 9.0;
        private const double TableSize = 8.5;
        private const double CellPadding = 5.0;
        private const double LegendHeight = 32.0;
        private const double FigureGap = 14.0;

        private readonly ReportDocument _doc;
        private readonly ReportPages _pages;
        private ReportPage _page;
        private double _y;
        private int _tables, _figures;

        private double TopMargin => _doc.MarginTop;
        private double Left => _doc.MarginLeft;
        private double Right => _doc.PageWidth - _doc.MarginRight;
        private double TextWidth => Right - Left;
        private double Bottom => _doc.PageHeight - _doc.MarginBottom;
        private bool AtTop => _y <= TopMargin + 0.5;

        private ReportLayout(ReportDocument doc)
        {
            _doc = doc;
            _pages = new ReportPages { Width = doc.PageWidth, Height = doc.PageHeight };
        }

        public static ReportPages Lay(ReportDocument doc)
        {
            var layout = new ReportLayout(doc);
            layout.NewPage();
            if (doc.Cover) layout.Cover(doc.Title, doc.Subtitle);

            for (int i = 0; i < doc.Blocks.Count; i++)
            {
                var block = doc.Blocks[i];
                // Headings, one under another, go over to the next page with the start of what follows them.
                if (block is HeadingBlock && (i == 0 || !(doc.Blocks[i - 1] is HeadingBlock)))
                    layout.KeepWithNext(doc.Blocks, i);
                switch (block)
                {
                    case HeadingBlock h: layout.Heading(h); break;
                    case ParagraphBlock p: layout.Paragraph(p); break;
                    case TableBlock t: layout.Table(t); break;
                    case FigureBlock f: layout.Figures(new List<FigureBlock> { f }, 1); break;
                    case FigureRowBlock r: layout.Figures(r.Figures, Math.Max(1, r.Columns)); break;
                    case PageBreakBlock _: if (!layout.AtTop) layout.NewPage(); break;
                    case CoverBlock c: layout.Cover(c.Title, c.Subtitle); break;
                }
            }

            layout.HeadersAndFooters();
            return layout._pages;
        }

        // ── Pages ──────────────────────────────────────────────────────────────────────────────

        private void NewPage()
        {
            _page = new ReportPage();
            _pages.Pages.Add(_page);
            _y = TopMargin;
        }

        /// <summary>A new page if <paramref name="height"/> does not fit on what is left of this one.</summary>
        private void Ensure(double height)
        {
            if (_y + height > Bottom && !AtTop)
                NewPage();
        }

        private void Text(double x, double baseline, string text, double size, bool bold = false, Color? colour = null, TextAlign align = TextAlign.Left)
        {
            if (string.IsNullOrEmpty(text)) return;
            _page.Items.Add(new TextItem { X = x, Y = baseline, Text = text, Size = size, Bold = bold, Colour = colour ?? Ink, Align = align });
        }

        private void Line(double x1, double y1, double x2, double y2, double width, Color colour) =>
            _page.Items.Add(new LineItem { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Width = width, Colour = colour });

        private void Rect(double x, double y, double w, double h, Color fill, Color stroke = default, double strokeWidth = 0.0) =>
            _page.Items.Add(new RectItem { X = x, Y = y, W = w, H = h, Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth });

        /// <summary>The baseline of a line of text of <paramref name="size"/> whose top is at <paramref name="top"/>.</summary>
        private static double Baseline(double top, double size) => top + size * 0.8;

        // ── The title ──────────────────────────────────────────────────────────────────────────

        private void Cover(string title, string subtitle)
        {
            // About a third of a page: on a page of its own if less is left.
            Ensure(Math.Min(260.0, Bottom - TopMargin));
            _y = AtTop ? TopMargin + 8.0 : _y + 8.0;

            foreach (var line in TextMetrics.Wrap(title, 24.0, true, TextWidth))
            {
                Text(Left, Baseline(_y, 24.0), line, 24.0, true);
                _y += 29.0;
            }

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                foreach (var line in TextMetrics.Wrap(subtitle, 13.0, false, TextWidth))
                {
                    Text(Left, Baseline(_y, 13.0), line, 13.0, false, Muted);
                    _y += 17.0;
                }
            }

            _y += 10.0;
            double labels = _doc.Details.Select(d => TextMetrics.Width(d.Key, 9.0, true)).DefaultIfEmpty(0.0).Max() + 14.0;
            foreach (var detail in _doc.Details)
            {
                Text(Left, Baseline(_y, 9.0), detail.Key, 9.0, true, Muted);
                foreach (var line in TextMetrics.Wrap(detail.Value, 9.0, false, TextWidth - labels))
                {
                    Text(Left + labels, Baseline(_y, 9.0), line, 9.0);
                    _y += 13.0;
                }
            }

            _y += 8.0;
            Line(Left, _y, Right, _y, 1.5, Accent);
            _y += 22.0;
        }

        // ── Text ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A new page for the headings starting at <paramref name="first"/>, if they and the least of
        /// what follows them - two lines of text, a table's first rows, a picture made as small as it
        /// may be - do not fit on what is left of this one: a heading alone at the foot of a page says
        /// nothing.
        /// </summary>
        private void KeepWithNext(List<ReportBlock> blocks, int first)
        {
            if (AtTop) return;
            double height = 0.0;
            int i = first;
            for (; i < blocks.Count && blocks[i] is HeadingBlock heading; i++)
                height += HeadingHeight(heading, i == first);

            if (i < blocks.Count)
            {
                switch (blocks[i])
                {
                    case FigureBlock f: height += FiguresNeed(new List<FigureBlock> { f }, 1, 0.55); break;
                    case FigureRowBlock r: height += FiguresNeed(r.Figures, Math.Max(1, r.Columns), 0.55); break;
                    case TableBlock _: height += 70.0; break;
                    case PageBreakBlock _: return;
                    default: height += 2 * BodyLeading; break;
                }
            }
            if (_y + height > Bottom)
                NewPage();
        }

        private static (double Size, double Leading, double Before, double After) HeadingStyle(HeadingBlock heading)
        {
            bool chapter = heading.Level <= 1, part = heading.Level >= 3;
            double size = chapter ? 15.0 : part ? 10.5 : 12.0;
            return (size, size * 1.3, chapter ? 12.0 : part ? 6.0 : 8.0, chapter ? 11.0 : part ? 3.0 : 4.0);
        }

        /// <summary>The number of a heading and the room before it: the text of its lines past the number.</summary>
        private List<string> HeadingLines(HeadingBlock heading, double size, out double indent)
        {
            string number = heading.Number ?? "";
            indent = number.Length > 0 ? TextMetrics.Width(number, size, true) + size * 0.7 : 0.0;
            return TextMetrics.Wrap(heading.Text, size, true, TextWidth - indent);
        }

        /// <summary>How much of the page a heading takes, the room before it as well unless <paramref name="first"/> starts a page.</summary>
        private double HeadingHeight(HeadingBlock heading, bool first)
        {
            var (size, leading, before, after) = HeadingStyle(heading);
            int lines = HeadingLines(heading, size, out _).Count;
            return (first && AtTop ? 0.0 : before) + lines * leading + after;
        }

        private void Heading(HeadingBlock heading)
        {
            bool chapter = heading.Level <= 1;
            bool part = heading.Level >= 3;
            var (size, leading, _, _) = HeadingStyle(heading);

            // Its number first, and the text beside it, its lines under one another past the number.
            string number = heading.Number ?? "";
            var lines = HeadingLines(heading, size, out double indent);

            // Kept with the start of what follows it: a heading alone at the foot of a page says
            // nothing.
            Ensure(lines.Count * leading + 90.0);
            if (!AtTop) _y += chapter ? 12.0 : part ? 6.0 : 8.0;

            if (number.Length > 0)
                Text(Left, Baseline(_y, size), number, size, true);
            foreach (var line in lines)
            {
                Text(Left + indent, Baseline(_y, size), line, size, true);
                _y += leading;
            }

            if (chapter)
            {
                _y += 1.0;
                Line(Left, _y, Right, _y, 0.8, Accent);
                _y += 10.0;
            }
            else
            {
                _y += part ? 3.0 : 4.0;
            }
        }

        private void Paragraph(ParagraphBlock paragraph)
        {
            double size = paragraph.Small ? 8.0 : BodySize;
            double leading = paragraph.Small ? 11.5 : BodyLeading;
            foreach (var line in TextMetrics.Wrap(paragraph.Text, size, false, TextWidth))
            {
                Ensure(leading);
                Text(Left, Baseline(_y, size), line, size, false, paragraph.Small ? Muted : Ink);
                _y += leading;
            }
            _y += paragraph.Small ? 5.0 : 7.0;
        }

        /// <summary>
        /// "Table 2  caption" or "Figure 3  caption", the label bold and the caption after it,
        /// wrapped to <paramref name="width"/>, from <paramref name="x"/> or about its middle.
        /// Returns the height it took.
        /// </summary>
        private double Caption(string label, string caption, double x, double top, double width, bool centred)
        {
            double gap = 5.0;
            double labelWidth = TextMetrics.Width(label, CaptionSize, true) + gap;
            var lines = string.IsNullOrWhiteSpace(caption)
                ? new List<string> { "" }
                : TextMetrics.Wrap(caption, CaptionSize, false, width, labelWidth);

            double leading = CaptionSize * 1.35;
            for (int i = 0; i < lines.Count; i++)
            {
                double used = TextMetrics.Width(lines[i], CaptionSize) + (i == 0 ? labelWidth : 0.0);
                double start = centred ? x + (width - used) / 2.0 : x;
                double baseline = Baseline(top + i * leading, CaptionSize);
                if (i == 0)
                {
                    Text(start, baseline, label, CaptionSize, true);
                    start += labelWidth;
                }
                Text(start, baseline, lines[i], CaptionSize, false);
            }

            return lines.Count * leading;
        }

        private double CaptionHeight(string label, string caption, double width)
        {
            if (string.IsNullOrWhiteSpace(caption)) return CaptionSize * 1.35;
            double labelWidth = TextMetrics.Width(label, CaptionSize, true) + 5.0;
            return TextMetrics.Wrap(caption, CaptionSize, false, width, labelWidth).Count * CaptionSize * 1.35;
        }

        // ── Tables ─────────────────────────────────────────────────────────────────────────────

        private void Table(TableBlock table)
        {
            int columns = Math.Max(table.Header.Count, table.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max());
            if (columns == 0) return;

            string Cell(List<string> row, int c) => c < row.Count ? row[c] ?? "" : "";
            TextAlign Align(int c) => c < table.Align.Count ? table.Align[c] : TextAlign.Left;

            // Each column as wide as what is in it; the whole fitted to the page, smaller type
            // first and then cut cells, if it is too wide.
            double size = TableSize;
            double[] widths = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                widths = new double[columns];
                for (int c = 0; c < columns; c++)
                {
                    double widest = TextMetrics.Width(Cell(table.Header, c), size, true);
                    for (int r = 0; r < table.Rows.Count; r++)
                        widest = Math.Max(widest, TextMetrics.Width(Cell(table.Rows[r], c), size, table.Bold.Contains(r)));
                    widths[c] = widest + 2 * CellPadding;
                }
                double natural = widths.Sum();
                if (natural <= TextWidth || attempt == 1) break;
                size = Math.Max(6.5, size * TextWidth / natural);
            }

            double total = widths.Sum();
            // A table of a few columns at its own width, a little roomier; one of many across the page.
            double target = total > TextWidth ? TextWidth
                : columns >= 4 ? TextWidth
                : Math.Min(TextWidth, total * 1.35);
            for (int c = 0; c < columns; c++)
                widths[c] *= target / total;

            double rowHeight = size * 1.75;
            double captionHeight = CaptionHeight("Table 00", table.Caption, TextWidth) + 4.0;

            // The note under the table goes with its last rows, not on a page of its own.
            var noteLines = string.IsNullOrWhiteSpace(table.Note) ? new List<string>() : TextMetrics.Wrap(table.Note, 7.5, false, TextWidth);
            double noteHeight = noteLines.Count > 0 ? 3.0 + noteLines.Count * 10.0 : 0.0;

            // A short table is kept whole; a long one starts with its caption, header and first
            // few rows together.
            double whole = captionHeight + rowHeight * (1 + table.Rows.Count) + noteHeight;
            Ensure(whole <= (Bottom - TopMargin) * 0.5 ? whole : captionHeight + rowHeight * (1 + Math.Min(3, table.Rows.Count)));
            _tables++;
            string label = $"Table {_tables}";
            _y += Caption(label, table.Caption, Left, _y, TextWidth, false) + 4.0;

            void HeaderRow()
            {
                Rect(Left, _y, target, rowHeight, HeaderFill);
                Row(table.Header, true, Ink);
                Line(Left, _y, Left + target, _y, 0.8, Accent);
            }

            void Row(List<string> cells, bool bold, Color colour)
            {
                double x = Left;
                double baseline = _y + rowHeight / 2.0 + size * 0.35;
                for (int c = 0; c < columns; c++)
                {
                    var text = TextMetrics.Fit(Cell(cells, c), size, bold, widths[c] - 2 * CellPadding);
                    switch (Align(c))
                    {
                        case TextAlign.Right: Text(x + widths[c] - CellPadding, baseline, text, size, bold, colour, TextAlign.Right); break;
                        case TextAlign.Centre: Text(x + widths[c] / 2.0, baseline, text, size, bold, colour, TextAlign.Centre); break;
                        default: Text(x + CellPadding, baseline, text, size, bold, colour); break;
                    }
                    x += widths[c];
                }
                _y += rowHeight;
            }

            HeaderRow();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                // The last two rows go over together, and with the note: a page that carries the
                // table on carries more of it than a single row.
                int left = table.Rows.Count - r;
                double below = left <= 2 ? (left - 1) * rowHeight + noteHeight : 0.0;
                if (_y + rowHeight + below > Bottom)
                {
                    Line(Left, _y, Left + target, _y, 0.6, Rule);
                    NewPage();
                    Text(Left, Baseline(_y, CaptionSize), $"{label} (continued)", CaptionSize, true, Muted);
                    _y += CaptionSize * 1.35 + 4.0;
                    HeaderRow();
                }

                if (r % 2 == 1)
                    Rect(Left, _y, target, rowHeight, Stripe);
                Row(table.Rows[r], table.Bold.Contains(r), Ink);
            }
            Line(Left, _y, Left + target, _y, 0.6, Rule);

            if (noteLines.Count > 0)
            {
                _y += 3.0;
                foreach (var line in noteLines)
                {
                    Ensure(10.0);
                    Text(Left, Baseline(_y, 7.5), line, 7.5, false, Muted);
                    _y += 10.0;
                }
            }

            _y += 14.0;
        }

        // ── Figures ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Figures side by side, <paramref name="columns"/> to the width of the text, a row kept
        /// whole on one page. A row of fewer figures than columns is centred, at the same size.
        /// </summary>
        /// <summary>The height a row of figures needs, its pictures made <paramref name="scale"/> times their size.</summary>
        private double FiguresNeed(List<FigureBlock> figures, int columns, double scale)
        {
            if (figures.Count == 0) return 0.0;
            columns = Math.Max(columns, figures.Count);
            double cell = (TextWidth - FigureGap * (columns - 1)) / columns;
            double maxPicture = (Bottom - TopMargin) * (columns == 1 ? 0.5 : 0.42);
            double pictures = figures.Max(f =>
            {
                double w = cell * Math.Max(0.2, Math.Min(1.0, f.Width));
                return Math.Min(maxPicture, w * f.PixelHeight / Math.Max(1, f.PixelWidth));
            });
            double rest = figures.Select((f, i) => 6.0 + (f.Legend != null ? LegendHeight : 0.0) + CaptionHeight($"Figure {_figures + i + 1}", f.Caption, cell)).Max();
            return pictures * scale + rest + 6.0;
        }

        private void Figures(List<FigureBlock> figures, int columns)
        {
            if (figures.Count == 0) return;
            columns = Math.Max(columns, figures.Count);

            double cell = (TextWidth - FigureGap * (columns - 1)) / columns;
            double maxPicture = (Bottom - TopMargin) * (columns == 1 ? 0.5 : 0.42);

            var sizes = figures.Select(f =>
            {
                double w = cell * Math.Max(0.2, Math.Min(1.0, f.Width));
                double h = w * f.PixelHeight / Math.Max(1, f.PixelWidth);
                if (h > maxPicture) { w *= maxPicture / h; h = maxPicture; }
                return (W: w, H: h);
            }).ToList();

            // The pictures of a row stand on one line, each centred in the height of the tallest, so
            // that their legends and captions line up under them.
            double pictures = sizes.Max(s => s.H);
            var labels = figures.Select((f, i) => $"Figure {_figures + i + 1}").ToList();
            var heights = figures.Select((f, i) =>
                pictures + 6.0 + (f.Legend != null ? LegendHeight : 0.0) + CaptionHeight(labels[i], f.Caption, cell)).ToList();

            // Most of the way onto what is left of the page, the pictures are made smaller to fit
            // rather than leave that much of it empty; much further, they go over to the next.
            double needed = heights.Max() + 6.0;
            double room = Bottom - _y;
            if (needed > room && !AtTop)
            {
                double factor = (room - (needed - pictures)) / pictures;
                if (factor >= 0.55)
                {
                    sizes = sizes.Select(s => (W: s.W * factor, H: s.H * factor)).ToList();
                    heights = heights.Select(h => h - pictures * (1.0 - factor)).ToList();
                    pictures *= factor;
                }
            }

            Ensure(heights.Max() + 6.0);

            double used = figures.Count * cell + (figures.Count - 1) * FigureGap;
            double x0 = Left + (TextWidth - used) / 2.0;
            double top = _y;

            for (int i = 0; i < figures.Count; i++)
            {
                double x = x0 + i * (cell + FigureGap);
                var (w, h) = sizes[i];
                double y = top;

                _page.Items.Add(new ImageItem { Path = figures[i].ImagePath, X = x + (cell - w) / 2.0, Y = y + (pictures - h) / 2.0, W = w, H = h });
                y += pictures + 6.0;

                if (figures[i].Legend != null)
                {
                    Legend(figures[i].Legend, x + (cell - w) / 2.0, y, w);
                    y += LegendHeight;
                }

                Caption(labels[i], figures[i].Caption, x, y, cell, true);
            }

            _figures += figures.Count;
            _y = top + heights.Max() + 12.0;
        }

        /// <summary>
        /// The colour scale under a figure: its title, a box per colour, and the values under them -
        /// one under each box, or with bands one at each edge between them - every few, if they
        /// would run into each other.
        /// </summary>
        private void Legend(FigureLegend legend, double x, double top, double width)
        {
            int count = legend.Colours.Count;
            if (count == 0) return;

            Text(x, Baseline(top, 7.5), legend.Title, 7.5, true, Muted);
            double boxTop = top + 10.0;
            double box = width / count;
            for (int i = 0; i < count; i++)
                Rect(x + i * box, boxTop, box + 0.3, 8.0, legend.Colours[i]);
            Rect(x, boxTop, width, 8.0, Color.Empty, Rule, 0.4);

            var values = legend.Values;
            if (values.Count == 0) return;

            var texts = values.Select(FormatValue).ToList();
            double widest = texts.Max(t => TextMetrics.Width(t, 6.5)) + 4.0;
            double spacing = legend.Banded || values.Count < 2 ? box : width / values.Count;
            int every = Math.Max(1, (int)Math.Ceiling(widest / Math.Max(1e-6, spacing)));

            // Every few, and the last always: the top of the scale is what a reader looks for. When
            // it comes too close to the one before, that one makes way for it.
            int last = values.Count - 1;
            var shown = Enumerable.Range(0, values.Count).Where(i => i % every == 0).ToList();
            if (shown[shown.Count - 1] != last)
            {
                if ((last - shown[shown.Count - 1]) * spacing < widest && shown.Count > 1)
                    shown.RemoveAt(shown.Count - 1);
                shown.Add(last);
            }

            double baseline = Baseline(boxTop + 10.5, 6.5);
            foreach (int i in shown)
            {
                double at = legend.Banded ? x + i * box : x + (i + 0.5) * width / values.Count;
                Text(at, baseline, texts[i], 6.5, false, Ink, TextAlign.Centre);
            }
        }

        /// <summary>A value of a legend, in three figures or so: 0.0123, 1.23, 1230, 1.2E+05.</summary>
        public static string FormatValue(double value)
        {
            double a = Math.Abs(value);
            if (a == 0.0) return "0";
            if (a >= 1e5 || a < 1e-3) return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
            int decimals = Math.Max(0, 2 - (int)Math.Floor(Math.Log10(a)));
            return Math.Round(value, decimals).ToString("0." + new string('#', Math.Max(1, decimals)), CultureInfo.InvariantCulture);
        }

        // ── Header and footer ──────────────────────────────────────────────────────────────────

        private void HeadersAndFooters()
        {
            int count = _pages.Pages.Count;
            string date = _doc.Details.FirstOrDefault(d => d.Key == "Date").Value ?? "";

            for (int i = 0; i < count; i++)
            {
                _page = _pages.Pages[i];

                // The user's own title block, on every page, its page numbers filled in.
                if (_doc.TitleBlock.Count > 0)
                {
                    _page.Items.InsertRange(0, _doc.TitleBlock.Select(item => PageNumbers(item, i + 1, count)));
                    continue;
                }

                // Or a plain header and footer in the margins: the title and Alpaca4d over the content
                // of every page but the first, the date and the page number under all of them.
                double head = Math.Max(16.0, TopMargin - 28.0);
                if (i > 0)
                {
                    Text(Left, head, TextMetrics.Fit(_doc.Title, 8.0, false, TextWidth - 80.0), 8.0, false, Muted);
                    Text(Right, head, "Alpaca4d", 8.0, true, Accent, TextAlign.Right);
                    Line(Left, head + 6.0, Right, head + 6.0, 0.5, Rule);
                }

                double foot = Math.Min(_doc.PageHeight - 12.0, Bottom + 22.0);
                Line(Left, foot - 10.0, Right, foot - 10.0, 0.5, Rule);
                Text(Left, foot, date, 8.0, false, Muted);
                Text(Right, foot, $"Page {i + 1} of {count}", 8.0, false, Muted, TextAlign.Right);
            }
        }

        /// <summary>A title block's item for one page: a text with {page} and {pages} in it filled in.</summary>
        private static PageItem PageNumbers(PageItem item, int page, int pages)
        {
            if (!(item is TextItem text) || text.Text.IndexOf('{') < 0) return item;
            return new TextItem
            {
                X = text.X, Y = text.Y, Size = text.Size, Bold = text.Bold, Colour = text.Colour, Align = text.Align,
                Text = text.Text.Replace("{page}", page.ToString(CultureInfo.InvariantCulture))
                                .Replace("{pages}", pages.ToString(CultureInfo.InvariantCulture))
            };
        }
    }
}
