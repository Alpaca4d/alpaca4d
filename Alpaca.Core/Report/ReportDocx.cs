using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Alpaca4d.Report
{
    /// <summary>
    /// A report as a Word document: the same content as the PDF, as Word sets text - headings in
    /// Word's own Heading styles, so that its navigation pane and table of contents find them, tables
    /// as Word tables, the pictures as they were drawn, captions, a header and a footer with the page
    /// number - to be pasted into a house report or changed. Word lays out the pages itself; a title
    /// block drawn for the PDF is left out, for the plain header and footer.
    ///
    /// Written by hand as Office Open XML, as a few files in a zip, so that it needs nothing but the
    /// framework: Word, LibreOffice and Pages open it.
    /// </summary>
    public static class ReportDocx
    {
        private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        /// <summary>Twentieths of a point, Word's measure of the page.</summary>
        private static int Twips(double points) => (int)Math.Round(points * 20.0);

        /// <summary>English Metric Units, Word's measure of a picture: 12700 to the point.</summary>
        private static long Emu(double points) => (long)Math.Round(points * 12700.0);

        private static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

        private static string X(string text) => System.Security.SecurityElement.Escape(text ?? "") ?? "";

        /// <summary>Writes <paramref name="doc"/> to <paramref name="path"/>, a .docx.</summary>
        public static void Write(ReportDocument doc, string path)
        {
            var writer = new Writer(doc);
            string body = writer.Body();

            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            if (File.Exists(path)) File.Delete(path);

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", ContentTypes());
                Add(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                    "</Relationships>");
                Add(zip, "docProps/core.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
                    "xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                    $"<dc:title>{X(doc.Title)}</dc:title><dc:creator>Alpaca4d</dc:creator>" +
                    $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}</dcterms:created>" +
                    "</cp:coreProperties>");
                Add(zip, "word/_rels/document.xml.rels", writer.Relationships());
                Add(zip, "word/document.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    $"<w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\" " +
                    "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
                    "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                    "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
                    $"<w:body>{body}{Section(doc)}</w:body></w:document>");
                Add(zip, "word/styles.xml", Styles());
                Add(zip, "word/settings.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    $"<w:settings xmlns:w=\"{W}\">" +
                    // Word fills in the table of contents' page numbers as it opens the file.
                    (writer.HasContents ? "<w:updateFields w:val=\"true\"/>" : "") +
                    "<w:defaultTabStop w:val=\"720\"/></w:settings>");
                Add(zip, "word/header1.xml", Header(doc));
                Add(zip, "word/footer1.xml", Footer(doc));
                foreach (var (name, file) in writer.Media)
                    zip.CreateEntryFromFile(file, "word/media/" + name, CompressionLevel.Fastest);
            }
        }

        private static void Add(ZipArchive zip, string name, string text)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var w = new StreamWriter(stream, new UTF8Encoding(false)))
                w.Write(text);
        }

        private static string ContentTypes() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
            "<Default Extension=\"jpg\" ContentType=\"image/jpeg\"/>" +
            "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" +
            "<Default Extension=\"bmp\" ContentType=\"image/bmp\"/>" +
            "<Default Extension=\"gif\" ContentType=\"image/gif\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
            "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>" +
            "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>" +
            "<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>" +
            "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
            "</Types>";

        /// <summary>The page: its size and margins as the PDF's, the header and footer in the margins.</summary>
        private static string Section(ReportDocument doc)
        {
            bool landscape = doc.PageWidth > doc.PageHeight;
            return "<w:sectPr>" +
                   "<w:headerReference w:type=\"default\" r:id=\"rIdHeader\"/><w:footerReference w:type=\"default\" r:id=\"rIdFooter\"/>" +
                   $"<w:pgSz w:w=\"{Twips(doc.PageWidth)}\" w:h=\"{Twips(doc.PageHeight)}\"{(landscape ? " w:orient=\"landscape\"" : "")}/>" +
                   $"<w:pgMar w:top=\"{Twips(doc.MarginTop)}\" w:right=\"{Twips(doc.MarginRight)}\" w:bottom=\"{Twips(doc.MarginBottom)}\" w:left=\"{Twips(doc.MarginLeft)}\" " +
                   $"w:header=\"{Twips(Math.Max(18.0, doc.MarginTop - 36.0))}\" w:footer=\"{Twips(Math.Max(18.0, doc.MarginBottom - 36.0))}\" w:gutter=\"0\"/>" +
                   "</w:sectPr>";
        }

        private static string Run(string text, bool bold = false, double? size = null, Color? colour = null)
        {
            var props = new StringBuilder();
            if (bold) props.Append("<w:b/>");
            if (colour.HasValue) props.Append($"<w:color w:val=\"{Hex(colour.Value)}\"/>");
            if (size.HasValue) props.Append($"<w:sz w:val=\"{(int)Math.Round(size.Value * 2)}\"/>");
            string rPr = props.Length > 0 ? $"<w:rPr>{props}</w:rPr>" : "";

            // A line break in the text starts a new line, as in the PDF.
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            var runs = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) runs.Append($"<w:r>{rPr}<w:br/></w:r>");
                if (lines[i].Length > 0) runs.Append($"<w:r>{rPr}<w:t xml:space=\"preserve\">{X(lines[i])}</w:t></w:r>");
            }
            return runs.ToString();
        }

        private static string Field(string instruction, string shown) =>
            $"<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>" +
            $"<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>{X(shown)}</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

        /// <summary>The title and Alpaca4d over the content, as the PDF's plain header has them.</summary>
        private static string Header(ReportDocument doc) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            $"<w:hdr xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:p><w:pPr><w:pStyle w:val=\"Header\"/>" +
            $"<w:tabs><w:tab w:val=\"right\" w:pos=\"{Twips(doc.PageWidth - doc.MarginLeft - doc.MarginRight)}\"/></w:tabs></w:pPr>" +
            Run(doc.Title, false, 8.0, ReportLayout.Muted) + "<w:r><w:tab/></w:r>" + Run("Alpaca4d", true, 8.0, ReportLayout.Accent) +
            "</w:p></w:hdr>";

        /// <summary>The date and the page, "Page 3 of 12", under the content.</summary>
        private static string Footer(ReportDocument doc)
        {
            string date = doc.Details.FirstOrDefault(d => d.Key == "Date").Value ?? "";
            string grey = $"<w:rPr><w:color w:val=\"{Hex(ReportLayout.Muted)}\"/><w:sz w:val=\"16\"/></w:rPr>";
            string Grey(string text) => $"<w:r>{grey}<w:t xml:space=\"preserve\">{X(text)}</w:t></w:r>";
            string GreyField(string instruction) =>
                $"<w:r>{grey}<w:fldChar w:fldCharType=\"begin\"/></w:r><w:r>{grey}<w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>" +
                $"<w:r>{grey}<w:fldChar w:fldCharType=\"separate\"/></w:r><w:r>{grey}<w:t>1</w:t></w:r><w:r>{grey}<w:fldChar w:fldCharType=\"end\"/></w:r>";
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   $"<w:ftr xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:p><w:pPr><w:pStyle w:val=\"Footer\"/>" +
                   $"<w:tabs><w:tab w:val=\"right\" w:pos=\"{Twips(doc.PageWidth - doc.MarginLeft - doc.MarginRight)}\"/></w:tabs></w:pPr>" +
                   Grey(date) + "<w:r><w:tab/></w:r>" + Grey("Page ") + GreyField("PAGE") + Grey(" of ") + GreyField("NUMPAGES") +
                   "</w:p></w:ftr>";
        }

        /// <summary>The styles: the PDF's type and colours, under Word's own names, so its outline and contents find the headings.</summary>
        private static string Styles()
        {
            string ink = Hex(ReportLayout.Ink), muted = Hex(ReportLayout.Muted), accent = Hex(ReportLayout.Accent);
            string Style(string id, string name, string pPr, string rPr, bool paragraph = true) =>
                $"<w:style w:type=\"{(paragraph ? "paragraph" : "table")}\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/>" +
                (id != "Normal" && paragraph ? "<w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/>" : "") +
                "<w:qFormat/>" + (pPr.Length > 0 ? $"<w:pPr>{pPr}</w:pPr>" : "") + (rPr.Length > 0 ? $"<w:rPr>{rPr}</w:rPr>" : "") + "</w:style>";

            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   $"<w:styles xmlns:w=\"{W}\">" +
                   "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:eastAsia=\"Arial\" w:cs=\"Arial\"/>" +
                   $"<w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/><w:color w:val=\"{ink}\"/><w:lang w:val=\"en-GB\"/></w:rPr></w:rPrDefault>" +
                   "<w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"290\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
                   // The font named on Normal too: a reader that skips the defaults still sets Arial.
                   Style("Normal", "Normal", "", "<w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:cs=\"Arial\"/>") +
                   Style("Title", "Title", "<w:spacing w:before=\"160\" w:after=\"80\"/>", "<w:b/><w:sz w:val=\"48\"/>") +
                   Style("Subtitle", "Subtitle", "<w:spacing w:after=\"200\"/>", $"<w:color w:val=\"{muted}\"/><w:sz w:val=\"26\"/>") +
                   Style("Heading1", "heading 1",
                         $"<w:keepNext/><w:spacing w:before=\"360\" w:after=\"160\"/><w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"4\" w:color=\"{accent}\"/></w:pBdr><w:outlineLvl w:val=\"0\"/>",
                         "<w:b/><w:sz w:val=\"32\"/>") +
                   Style("Heading2", "heading 2", "<w:keepNext/><w:spacing w:before=\"240\" w:after=\"100\"/><w:outlineLvl w:val=\"1\"/>", "<w:b/><w:sz w:val=\"26\"/>") +
                   Style("Heading3", "heading 3", "<w:keepNext/><w:spacing w:before=\"200\" w:after=\"80\"/><w:outlineLvl w:val=\"2\"/>", "<w:b/><w:sz w:val=\"22\"/>") +
                   Style("TOCHeading", "TOC Heading", "<w:keepNext/><w:spacing w:before=\"360\" w:after=\"160\"/>", "<w:b/><w:sz w:val=\"32\"/>") +
                   Style("TOC1", "toc 1", "<w:spacing w:before=\"80\" w:after=\"20\"/>", "<w:b/>") +
                   Style("TOC2", "toc 2", "<w:spacing w:after=\"20\"/><w:ind w:left=\"320\"/>", "") +
                   Style("TOC3", "toc 3", "<w:spacing w:after=\"20\"/><w:ind w:left=\"640\"/>", "") +
                   Style("Caption", "caption", "<w:spacing w:before=\"60\" w:after=\"160\"/>", "<w:sz w:val=\"18\"/>") +
                   Style("Note", "Note", "<w:spacing w:after=\"160\"/>", $"<w:color w:val=\"{muted}\"/><w:sz w:val=\"15\"/>") +
                   Style("Header", "header", "", "") +
                   Style("Footer", "footer", "", "") +
                   "</w:styles>";
        }

        /// <summary>The body, block by block, and the pictures it takes along.</summary>
        private sealed class Writer
        {
            private readonly ReportDocument _doc;
            private readonly StringBuilder _xml = new StringBuilder();
            private int _tables, _figures, _drawings;

            /// <summary>The pictures, by their name in the document and the file each is copied from.</summary>
            public readonly List<(string Name, string File)> Media = new List<(string, string)>();

            public bool HasContents;

            private double TextWidth => _doc.PageWidth - _doc.MarginLeft - _doc.MarginRight;

            public Writer(ReportDocument doc) { _doc = doc; }

            public string Relationships()
            {
                var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "<Relationship Id=\"rIdSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
                    "<Relationship Id=\"rIdHeader\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>" +
                    "<Relationship Id=\"rIdFooter\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>");
                for (int i = 0; i < Media.Count; i++)
                    sb.Append($"<Relationship Id=\"rIdImage{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/{Media[i].Name}\"/>");
                sb.Append("</Relationships>");
                return sb.ToString();
            }

            public string Body()
            {
                if (_doc.Cover) Cover(_doc.Title, _doc.Subtitle);
                foreach (var block in _doc.Blocks)
                {
                    switch (block)
                    {
                        case CoverBlock c: Cover(c.Title, c.Subtitle); break;
                        case ContentsBlock c: Contents(c); break;
                        case HeadingBlock h: Heading(h); break;
                        case ParagraphBlock p: Paragraph(p.Text, p.Small ? "Note" : null); break;
                        case TableBlock t: Table(t); break;
                        case FigureBlock f: Figures(new List<FigureBlock> { f }, 1); break;
                        case FigureRowBlock r: Figures(r.Figures, Math.Max(1, r.Columns)); break;
                        case PageBreakBlock _: _xml.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>"); break;
                    }
                }
                // A document ends with a paragraph: after a table, Word would add one of its own.
                _xml.Append("<w:p/>");
                return _xml.ToString();
            }

            private void Paragraph(string text, string style, string runs = null, string extra = "")
            {
                string pPr = style != null || extra.Length > 0 ? $"<w:pPr>{(style != null ? $"<w:pStyle w:val=\"{style}\"/>" : "")}{extra}</w:pPr>" : "";
                _xml.Append($"<w:p>{pPr}{runs ?? Run(text)}</w:p>");
            }

            private void Cover(string title, string subtitle)
            {
                Paragraph(title, "Title");
                if (!string.IsNullOrWhiteSpace(subtitle)) Paragraph(subtitle, "Subtitle");
                foreach (var detail in _doc.Details)
                    Paragraph(null, null, Run(detail.Key + "  ", true, 9.0, ReportLayout.Muted) + Run(detail.Value, false, 9.0), "<w:spacing w:after=\"40\"/>");
                Paragraph(null, null, "", $"<w:pBdr><w:bottom w:val=\"single\" w:sz=\"12\" w:space=\"1\" w:color=\"{Hex(ReportLayout.Accent)}\"/></w:pBdr><w:spacing w:after=\"360\"/>");
            }

            private void Heading(HeadingBlock heading)
            {
                int level = Math.Max(1, Math.Min(3, heading.Level));
                string number = string.IsNullOrEmpty(heading.Number) ? "" : heading.Number + "   ";
                Paragraph(number + heading.Text, $"Heading{level}");
            }

            /// <summary>
            /// The contents as a Word field, which Word fills in with the pages as it opens the file;
            /// until then, and in a program that does not, the headings it will list.
            /// </summary>
            private void Contents(ContentsBlock contents)
            {
                HasContents = true;
                int depth = Math.Max(1, Math.Min(3, contents.Depth));
                if (!string.IsNullOrWhiteSpace(contents.Title)) Paragraph(contents.Title, "TOCHeading");
                var listed = _doc.Blocks.OfType<HeadingBlock>().Where(h => h.Level <= depth && !string.IsNullOrWhiteSpace(h.Text)).ToList();

                _xml.Append("<w:p><w:pPr><w:pStyle w:val=\"TOC1\"/></w:pPr>" +
                            $"<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-{depth}\" \\h \\z \\u </w:instrText></w:r>" +
                            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>");
                for (int i = 0; i < listed.Count; i++)
                {
                    var h = listed[i];
                    string text = (string.IsNullOrEmpty(h.Number) ? "" : h.Number + "   ") + h.Text;
                    if (i > 0) _xml.Append($"</w:p><w:p><w:pPr><w:pStyle w:val=\"TOC{Math.Max(1, Math.Min(3, h.Level))}\"/></w:pPr>");
                    _xml.Append(Run(text));
                }
                _xml.Append("<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
            }

            private void Caption(string label, string caption, bool centred = false) =>
                Paragraph(null, "Caption", Run(label + "  ", true, 9.0) + Run(caption ?? "", false, 9.0), centred ? "<w:jc w:val=\"center\"/>" : "");

            private static string Jc(TextAlign align) =>
                align == TextAlign.Right ? "<w:jc w:val=\"right\"/>" : align == TextAlign.Centre ? "<w:jc w:val=\"center\"/>" : "";

            private void Table(TableBlock table)
            {
                int columns = Math.Max(table.Header.Count, table.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max());
                if (columns == 0) return;

                // Above it, as in the PDF: "Table 2  Support reactions", or a table of the report's own by its caption alone.
                var caption = table.Numbered ? ($"Table {++_tables}", table.Caption) : (table.Caption, "");
                Paragraph(null, "Caption", Run(caption.Item1 + (caption.Item2.Length > 0 ? "  " : ""), true, 9.0) + Run(caption.Item2, false, 9.0), "<w:keepNext/>");

                string rule = Hex(ReportLayout.Rule);
                _xml.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/>" +
                            $"<w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:color=\"{rule}\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"{rule}\"/>" +
                            $"<w:insideH w:val=\"single\" w:sz=\"2\" w:color=\"{rule}\"/></w:tblBorders>" +
                            "<w:tblCellMar><w:left w:w=\"80\" w:type=\"dxa\"/><w:right w:w=\"80\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:tblGrid>");
                for (int c = 0; c < columns; c++) _xml.Append($"<w:gridCol w:w=\"{Twips(TextWidth / columns)}\"/>");
                _xml.Append("</w:tblGrid>");

                string Cell(List<string> row, int c) => c < row.Count ? row[c] ?? "" : "";
                TextAlign Align(int c) => c < table.Align.Count ? table.Align[c] : TextAlign.Left;
                void Row(List<string> cells, bool bold, bool header)
                {
                    _xml.Append("<w:tr>" + (header ? "<w:trPr><w:tblHeader/></w:trPr>" : "<w:trPr><w:cantSplit/></w:trPr>"));
                    for (int c = 0; c < columns; c++)
                    {
                        string shading = header ? $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{Hex(ReportLayout.HeaderFill)}\"/>" : "";
                        string border = header ? $"<w:tcBorders><w:bottom w:val=\"single\" w:sz=\"8\" w:color=\"{Hex(ReportLayout.Accent)}\"/></w:tcBorders>" : "";
                        _xml.Append($"<w:tc><w:tcPr>{border}{shading}</w:tcPr><w:p><w:pPr><w:spacing w:before=\"40\" w:after=\"40\"/>{Jc(Align(c))}</w:pPr>" +
                                    Run(Cell(cells, c), bold, 8.5) + "</w:p></w:tc>");
                    }
                    _xml.Append("</w:tr>");
                }
                Row(table.Header, true, true);
                for (int r = 0; r < table.Rows.Count; r++)
                    Row(table.Rows[r], table.Bold.Contains(r), false);
                _xml.Append("</w:tbl>");

                if (!string.IsNullOrWhiteSpace(table.Note)) Paragraph(table.Note, "Note", null, "<w:spacing w:before=\"60\"/>");
                else Paragraph("", null, "", "<w:spacing w:after=\"120\"/>");
            }

            /// <summary>Pictures side by side, each its share of the width, with its legend and caption under it.</summary>
            private void Figures(List<FigureBlock> figures, int columns)
            {
                if (figures.Count == 0) return;
                double cell = TextWidth / columns;
                if (columns == 1)
                {
                    Figure(figures[0], cell);
                    return;
                }

                _xml.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLayout w:type=\"fixed\"/>" +
                            "<w:tblCellMar><w:left w:w=\"60\" w:type=\"dxa\"/><w:right w:w=\"60\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:tblGrid>");
                for (int c = 0; c < columns; c++) _xml.Append($"<w:gridCol w:w=\"{Twips(cell)}\"/>");
                _xml.Append("</w:tblGrid><w:tr><w:trPr><w:cantSplit/></w:trPr>");
                for (int c = 0; c < columns; c++)
                {
                    _xml.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{Twips(cell)}\" w:type=\"dxa\"/></w:tcPr>");
                    if (c < figures.Count) Figure(figures[c], cell - 6.0);
                    else _xml.Append("<w:p/>");
                    _xml.Append("</w:tc>");
                }
                _xml.Append("</w:tr></w:tbl>");
            }

            private void Figure(FigureBlock figure, double width)
            {
                _figures++;
                string label = $"Figure {_figures}";
                if (!string.IsNullOrEmpty(figure.ImagePath) && File.Exists(figure.ImagePath))
                {
                    double w = width * Math.Max(0.05, Math.Min(1.0, figure.Width));
                    double h = w * Math.Max(1, figure.PixelHeight) / Math.Max(1, figure.PixelWidth);
                    // No taller than most of a page.
                    double most = (_doc.PageHeight - _doc.MarginTop - _doc.MarginBottom) * 0.8;
                    if (h > most) { w *= most / h; h = most; }
                    Paragraph(null, null, Picture(figure.ImagePath, w, h), "<w:keepNext/><w:jc w:val=\"center\"/><w:spacing w:after=\"60\"/>");
                }
                else Paragraph($"[{label}: {figure.ImagePath} is not there]", "Note");

                if (figure.Legend != null) Legend(figure.Legend, Math.Min(width, 300.0));
                Caption(label, figure.Caption, true);
            }

            private string Picture(string path, double w, double h)
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension.Length == 0) extension = ".png";
                int index = Media.Count + 1;
                Media.Add(($"image{index}{extension}", path));
                int id = ++_drawings;
                long cx = Emu(w), cy = Emu(h);
                return "<w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" +
                       $"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:docPr id=\"{id}\" name=\"Picture {id}\"/>" +
                       "<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>" +
                       "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>" +
                       $"<pic:nvPicPr><pic:cNvPr id=\"{id}\" name=\"{X(Path.GetFileName(path))}\"/><pic:cNvPicPr/></pic:nvPicPr>" +
                       $"<pic:blipFill><a:blip r:embed=\"rIdImage{index}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
                       $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
                       "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>";
            }

            /// <summary>The colour scale: a row of coloured cells, and the values under some of them, the last always.</summary>
            private void Legend(FigureLegend legend, double width)
            {
                int count = legend.Colours.Count;
                if (count == 0) return;
                if (!string.IsNullOrWhiteSpace(legend.Title)) Paragraph(null, null, Run(legend.Title, true, 7.5, ReportLayout.Muted), "<w:keepNext/><w:jc w:val=\"center\"/><w:spacing w:after=\"20\"/>");

                double box = width / count;
                _xml.Append("<w:tbl><w:tblPr>" + $"<w:tblW w:w=\"{Twips(width)}\" w:type=\"dxa\"/><w:jc w:val=\"center\"/><w:tblLayout w:type=\"fixed\"/>" +
                            "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:tblGrid>");
                for (int i = 0; i < count; i++) _xml.Append($"<w:gridCol w:w=\"{Twips(box)}\"/>");
                _xml.Append("</w:tblGrid><w:tr><w:trPr><w:trHeight w:val=\"160\" w:hRule=\"exact\"/></w:trPr>");
                foreach (var colour in legend.Colours)
                    _xml.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{Twips(box)}\" w:type=\"dxa\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{Hex(colour)}\"/></w:tcPr><w:p><w:pPr><w:spacing w:after=\"0\"/></w:pPr></w:p></w:tc>");
                _xml.Append("</w:tr>");

                var values = legend.Values;
                if (values.Count > 0)
                {
                    // For bands, the value at each band's lower edge, and the top one in the last; else each colour's own.
                    int every = Math.Max(1, (int)Math.Ceiling(count / 6.0));
                    _xml.Append("<w:tr>");
                    for (int i = 0; i < count; i++)
                    {
                        string text = "";
                        if (i % every == 0 && i < values.Count) text = ReportLayout.FormatValue(values[i]);
                        if (i == count - 1) text = ReportLayout.FormatValue(values[values.Count - 1]);
                        var jc = legend.Banded && i == count - 1 ? "<w:jc w:val=\"right\"/>" : legend.Banded ? "" : "<w:jc w:val=\"center\"/>";
                        _xml.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{Twips(box)}\" w:type=\"dxa\"/></w:tcPr><w:p><w:pPr><w:spacing w:after=\"0\"/>{jc}</w:pPr>{Run(text, false, 6.5)}</w:p></w:tc>");
                    }
                    _xml.Append("</w:tr>");
                }
                _xml.Append("</w:tbl>");
            }
        }
    }
}
