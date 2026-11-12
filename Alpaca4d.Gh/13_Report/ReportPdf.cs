using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

using Rhino.DocObjects;
using Font = Rhino.DocObjects.Font;
using Rhino.FileIO;

using Alpaca4d.Report;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Writes laid-out pages (<see cref="ReportLayout"/>) to a PDF with Rhino's own PDF writer, so
    /// that a report needs nothing Rhino does not have, on Windows or on a Mac. The pages are drawn
    /// at 72 dots an inch, a dot to a point, so the layout's numbers go in as they are.
    /// </summary>
    internal static class ReportPdf
    {
        private const int Dpi = 72;

        public static void Write(ReportPages pages, string path)
        {
            var pdf = FilePdf.Create();
            var regular = new Font("Arial");
            var bold = new Font("Arial", Font.FontWeight.Bold, Font.FontStyle.Upright, false, false);
            var bitmaps = new Dictionary<string, Bitmap>();

            try
            {
                foreach (var page in pages.Pages)
                {
                    int index = pdf.AddPage((int)Math.Round(pages.Width), (int)Math.Round(pages.Height), Dpi);

                    // Fills first, then pictures, lines and text on top: a table's stripes under its rules
                    // and its words.
                    foreach (var rect in page.Items.OfType<RectItem>())
                    {
                        var corners = new[]
                        {
                            P(pages, rect.X, rect.Y), P(pages, rect.X + rect.W, rect.Y),
                            P(pages, rect.X + rect.W, rect.Y + rect.H), P(pages, rect.X, rect.Y + rect.H), P(pages, rect.X, rect.Y)
                        };
                        bool outline = !rect.Stroke.IsEmpty && rect.StrokeWidth > 0.0;
                        pdf.DrawPolyline(index, corners,
                            rect.Fill.IsEmpty ? Color.Transparent : rect.Fill,
                            outline ? rect.Stroke : Color.Transparent,
                            outline ? (float)rect.StrokeWidth : 0f);
                    }

                    // The title block's filled areas.
                    foreach (var polygon in page.Items.OfType<PolygonItem>())
                    {
                        bool outline = !polygon.Stroke.IsEmpty && polygon.Width > 0.0;
                        pdf.DrawPolyline(index, polygon.Points.Select(q => P(pages, q.X, q.Y)).ToArray(),
                            polygon.Fill.IsEmpty ? Color.Transparent : polygon.Fill,
                            outline ? polygon.Stroke : Color.Transparent,
                            outline ? (float)polygon.Width : 0f);
                    }

                    foreach (var image in page.Items.OfType<ImageItem>())
                    {
                        if (!bitmaps.TryGetValue(image.Path, out var bitmap))
                        {
                            bitmap = File.Exists(image.Path) ? new Bitmap(image.Path) : null;
                            bitmaps[image.Path] = bitmap;
                        }
                        if (bitmap == null) continue;

                        var corner = P(pages, image.X, image.Y);
                        pdf.DrawBitmap(index, bitmap, corner.X, corner.Y, (float)image.W, (float)image.H, 0f);
                    }

                    foreach (var line in page.Items.OfType<LineItem>())
                        pdf.DrawLine(index, P(pages, line.X1, line.Y1), P(pages, line.X2, line.Y2), line.Colour, (float)line.Width);

                    // The title block's lines.
                    foreach (var polyline in page.Items.OfType<PolylineItem>())
                        pdf.DrawPolyline(index, polyline.Points.Select(q => P(pages, q.X, q.Y)).ToArray(), Color.Transparent, polyline.Colour, (float)polyline.Width);

                    foreach (var text in page.Items.OfType<TextItem>())
                    {
                        var at = P(pages, text.X, text.Y);
                        pdf.DrawText(index, text.Text, at.X, at.Y, (float)text.Size, text.Bold ? bold : regular,
                            text.Colour, Color.Transparent, 0f, 0f, Horizontal(text.Align), TextVerticalAlignment.Bottom);
                    }
                }

                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                pdf.Write(path);
            }
            finally
            {
                foreach (var bitmap in bitmaps.Values) bitmap?.Dispose();
            }
        }

        /// <summary>
        /// A point of the layout on Rhino's page, which counts as the layout does - down from the top
        /// left corner, in dots, here points - and not up from the foot as PDF itself does.
        /// </summary>
        private static PointF P(ReportPages pages, double x, double y) => new PointF((float)x, (float)y);

        private static TextHorizontalAlignment Horizontal(TextAlign align)
        {
            switch (align)
            {
                case TextAlign.Right: return TextHorizontalAlignment.Right;
                case TextAlign.Centre: return TextHorizontalAlignment.Center;
                default: return TextHorizontalAlignment.Left;
            }
        }
    }
}
