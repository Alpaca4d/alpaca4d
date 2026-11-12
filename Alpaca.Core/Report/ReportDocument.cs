using System.Collections.Generic;
using System.Drawing;

namespace Alpaca4d.Report
{
    /// <summary>How a column of a table, or a line of text, lines up.</summary>
    public enum TextAlign
    {
        Left,
        Centre,
        Right
    }

    /// <summary>
    /// A report as content, before it is laid out on pages: a title, a few details under it, and
    /// blocks one after the other - headings, paragraphs, tables, figures. A template is turned into
    /// one of these (<see cref="ReportBuilder"/>), and <see cref="ReportLayout"/> puts it on pages.
    /// </summary>
    public sealed class ReportDocument
    {
        public string Title = "";
        public string Subtitle = "";

        /// <summary>Name and value pairs under the title: project, author, date.</summary>
        public List<KeyValuePair<string, string>> Details = new List<KeyValuePair<string, string>>();

        /// <summary>The page, in points (1/72 inch).</summary>
        public double PageWidth = PageSizes.A4.Width;
        public double PageHeight = PageSizes.A4.Height;

        /// <summary>
        /// The margins, in points: the content stays inside them. The title block, or the plain header
        /// and footer when there is none, goes in the top and bottom ones.
        /// </summary>
        public double MarginTop = 68.0, MarginBottom = 62.0, MarginLeft = 56.0, MarginRight = 56.0;

        /// <summary>
        /// What is drawn on every page in place of the plain header and footer: the title block, in
        /// page points from the top left corner. {page} and {pages} in its text are filled in page by
        /// page; empty for none.
        /// </summary>
        public List<PageItem> TitleBlock = new List<PageItem>();

        /// <summary>Whether the first page opens with the title, the subtitle and the details.</summary>
        public bool Cover = true;

        public List<ReportBlock> Blocks = new List<ReportBlock>();
    }

    /// <summary>Page sizes in points, portrait.</summary>
    public static class PageSizes
    {
        public const double PointsPerMillimetre = 72.0 / 25.4;

        public static readonly SizeF A4 = Millimetres(210, 297);
        public static readonly SizeF A3 = Millimetres(297, 420);
        public static readonly SizeF A2 = Millimetres(420, 594);
        public static readonly SizeF A1 = Millimetres(594, 841);
        public static readonly SizeF A0 = Millimetres(841, 1189);
        public static readonly SizeF Letter = new SizeF(612f, 792f);
        public static readonly SizeF Legal = new SizeF(612f, 1008f);

        public static SizeF Millimetres(double width, double height) =>
            new SizeF((float)(width * PointsPerMillimetre), (float)(height * PointsPerMillimetre));

        /// <summary>The names a page size can be given by, in the order a menu offers them.</summary>
        public static readonly string[] Names = { "A4", "A3", "A2", "A1", "A0", "Letter", "Legal" };

        /// <summary>A page size by its name, or null for one there is not.</summary>
        public static SizeF? Named(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "a4": return A4;
                case "a3": return A3;
                case "a2": return A2;
                case "a1": return A1;
                case "a0": return A0;
                case "letter": return Letter;
                case "legal": return Legal;
                default: return null;
            }
        }
    }

    public abstract class ReportBlock
    {
    }

    /// <summary>The title, the subtitle and the details under them, set large where the block comes.</summary>
    public sealed class CoverBlock : ReportBlock
    {
        public string Title = "";
        public string Subtitle = "";
    }

    /// <summary>A heading: level 1 for a chapter, 2 for a section in it, 3 for a part of a section.</summary>
    public sealed class HeadingBlock : ReportBlock
    {
        public int Level = 1;
        public string Text = "";

        /// <summary>Its number, as the report counts its headings - 1, 1.2, 1.2.3 - written before it; empty for none.</summary>
        public string Number = "";

        /// <summary>Whether it is numbered and counted: not an appendix or a summary that asks not to be.</summary>
        public bool Numbered = true;
    }

    /// <summary>Running text. A line break in it starts a new line.</summary>
    public sealed class ParagraphBlock : ReportBlock
    {
        public string Text = "";

        /// <summary>Set small and grey, as a note.</summary>
        public bool Small;
    }

    /// <summary>A table of text: a header row, then rows, each cell already written out.</summary>
    public sealed class TableBlock : ReportBlock
    {
        /// <summary>Written above the table after its number: "Table 2  Natural frequencies".</summary>
        public string Caption = "";

        public List<string> Header = new List<string>();
        public List<List<string>> Rows = new List<List<string>>();

        /// <summary>Per column; a column with none is left-aligned.</summary>
        public List<TextAlign> Align = new List<TextAlign>();

        /// <summary>Rows to set apart from the rest, a sum for instance, by their place in <see cref="Rows"/>.</summary>
        public HashSet<int> Bold = new HashSet<int>();

        /// <summary>A line of small print under the table, or empty.</summary>
        public string Note = "";
    }

    /// <summary>
    /// The colours a figure's result is drawn in and the values they stand for, as Model View's
    /// Values and Colors outputs give them: a value per colour, or with iso-surfaces (Banded) the
    /// edges of the bands, one more than there are colours.
    /// </summary>
    public sealed class FigureLegend
    {
        public string Title = "";
        public List<double> Values = new List<double>();
        public List<Color> Colours = new List<Color>();
        public bool Banded;
    }

    /// <summary>A picture: a capture of the model, or an image file, with a caption under it.</summary>
    public sealed class FigureBlock : ReportBlock
    {
        public string ImagePath = "";
        public int PixelWidth = 1;
        public int PixelHeight = 1;

        /// <summary>How much of the text's width the picture takes, 0 to 1. In a row, of the row's share.</summary>
        public double Width = 1.0;

        /// <summary>Written under the picture after its number: "Figure 3  Mode 1".</summary>
        public string Caption = "";

        /// <summary>The colour scale under the picture, or null for none.</summary>
        public FigureLegend Legend;
    }

    /// <summary>Figures side by side, as many as there are, each an equal share of the width.</summary>
    public sealed class FigureRowBlock : ReportBlock
    {
        public List<FigureBlock> Figures = new List<FigureBlock>();

        /// <summary>
        /// How many figures the row has room for: the last row of a grid can have fewer figures,
        /// and is drawn at the same size as the rows above it.
        /// </summary>
        public int Columns = 1;
    }

    /// <summary>The next block starts on a new page.</summary>
    public sealed class PageBreakBlock : ReportBlock
    {
    }
}
