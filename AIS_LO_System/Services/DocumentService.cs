using DocumentFormat.OpenXml.Packaging;

using WParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using WTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;
using WTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using WRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using WDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;

using iText.Kernel.Pdf;
using iText.Kernel.Font;
using iText.IO.Font.Constants;

using ITextDocument = iText.Layout.Document;
using ITextParagraph = iText.Layout.Element.Paragraph;
using ITextTable = iText.Layout.Element.Table;
using ITextCell = iText.Layout.Element.Cell;
using ITextUnit = iText.Layout.Properties.UnitValue;

namespace AIS_LO_System.Services
{
    public static class DocumentService
    {
        public static readonly string[] AllowedExtensions = { ".pdf", ".docx" };

        public static bool IsAllowed(string fileName)
            => AllowedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

        public static bool IsWordDocument(string fileName)
            => Path.GetExtension(fileName).ToLowerInvariant() == ".docx";

        // -------------------------------------------------------
        // Extract plain text from .docx — skips images/drawings
        // -------------------------------------------------------
        public static string ExtractTextFromDocx(string filePath)
        {
            try
            {
                using var doc = WordprocessingDocument.Open(filePath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return string.Empty;

                var sb = new System.Text.StringBuilder();
                foreach (var para in body.Elements<WParagraph>())
                {
                    if (ContainsOnlyDrawings(para)) continue;
                    sb.AppendLine(para.InnerText);
                }
                return sb.ToString();
            }
            catch { return string.Empty; }
        }

        private static bool ContainsOnlyDrawings(WParagraph para)
        {
            var runs = para.Elements<WRun>().ToList();
            if (!runs.Any()) return false;
            return runs.All(r =>
                r.Elements<WDrawing>().Any() &&
                string.IsNullOrWhiteSpace(r.InnerText));
        }

        // Replaces smart quotes, en/em dashes and other non-Latin1 chars
        // that HELVETICA cannot encode
        private static string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text
                .Replace('\u2018', '\'') // left single quote
                .Replace('\u2019', '\'') // right single quote
                .Replace('\u201C', '"')  // left double quote
                .Replace('\u201D', '"')  // right double quote
                .Replace('\u2013', '-')  // en dash
                .Replace('\u2014', '-')  // em dash
                .Replace('\u2026', '.')  // ellipsis
                .Replace('\u00A0', ' ')  // non-breaking space
                .Replace('\u2022', '-')  // bullet
                .Replace("\u2122", "(TM)")
                .Replace("\u00AE", "(R)")
                // Replace any remaining non-Latin1 chars with '?'
                .Aggregate(new System.Text.StringBuilder(), (sb, c) =>
                {
                    sb.Append(c > 255 ? '?' : c);
                    return sb;
                }).ToString();
        }

        // -------------------------------------------------------
        // Convert .docx to PDF
        // Writes to MemoryStream first so iText7 never holds a
        // file handle — then flushes bytes to disk in one shot.
        // -------------------------------------------------------
        public static string ConvertDocxToPdf(string docxPath)
        {
            var pdfPath = Path.ChangeExtension(docxPath, ".pdf");

            // Step 1: Read Word content into memory (doc closed after this block)
            var paragraphs = new List<(string text, bool isHeading, bool isList)>();
            var tables = new List<List<List<string>>>();
            var order = new List<(string type, int index)>();

            try
            {
                using var doc = WordprocessingDocument.Open(docxPath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return string.Empty;

                foreach (var element in body.Elements())
                {
                    if (element is WParagraph para)
                    {
                        if (ContainsOnlyDrawings(para)) continue;
                        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                        var isHeading = styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase);
                        var isList = styleId.Contains("List", StringComparison.OrdinalIgnoreCase);
                        paragraphs.Add((para.InnerText, isHeading, isList));
                        order.Add(("p", paragraphs.Count - 1));
                    }
                    else if (element is WTable table)
                    {
                        var tableData = new List<List<string>>();
                        foreach (var row in table.Elements<WTableRow>())
                            tableData.Add(row.Elements<WTableCell>()
                                .Select(c => c.InnerText).ToList());
                        tables.Add(tableData);
                        order.Add(("t", tables.Count - 1));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DocumentService] DOCX read failed: {ex.Message}");
                return string.Empty;
            }

            // Step 2: Render PDF entirely into a MemoryStream
            // iText7 never touches the output file — no file locks
            byte[] pdfBytes;
            try
            {
                using var ms = new MemoryStream();

                using (var writer = new PdfWriter(ms, new WriterProperties().SetFullCompressionMode(false)))
                using (var pdf = new PdfDocument(writer))
                using (var layout = new ITextDocument(pdf))
                {
                    layout.SetMargins(50, 50, 50, 50);

                    var normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                    var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

                    foreach (var (type, index) in order)
                    {
                        if (type == "p")
                        {
                            var (text, isHeading, isList) = paragraphs[index];

                            if (string.IsNullOrWhiteSpace(text))
                            {
                                layout.Add(new ITextParagraph(" ")
                                    .SetFont(normalFont).SetFontSize(4));
                                continue;
                            }

                            var p = new ITextParagraph(SanitizeText(isList ? $"* {text}" : text));
                            if (isHeading)
                                p.SetFont(boldFont).SetFontSize(13)
                                 .SetMarginTop(10).SetMarginBottom(4);
                            else if (isList)
                                p.SetFont(normalFont).SetFontSize(11)
                                 .SetMarginLeft(16).SetMarginTop(1);
                            else
                                p.SetFont(normalFont).SetFontSize(11).SetMarginTop(2);

                            layout.Add(p);
                        }
                        else if (type == "t")
                        {
                            var tableData = tables[index];
                            if (!tableData.Any()) continue;

                            var colCount = tableData.Max(r => r.Count);
                            if (colCount == 0) continue;

                            var iTable = new ITextTable(ITextUnit.CreatePercentArray(colCount))
                                .UseAllAvailableWidth()
                                .SetMarginTop(8).SetMarginBottom(8);

                            for (int ri = 0; ri < tableData.Count; ri++)
                            {
                                var row = tableData[ri];
                                for (int ci = 0; ci < colCount; ci++)
                                {
                                    var cellText = ci < row.Count ? row[ci] : "";
                                    iTable.AddCell(new ITextCell().Add(
                                        new ITextParagraph(SanitizeText(cellText))
                                            .SetFont(ri == 0 ? boldFont : normalFont)
                                            .SetFontSize(10)));
                                }
                            }

                            layout.Add(iTable);
                        }
                    }
                } // <-- PdfDocument + PdfWriter fully disposed here, ms still alive

                pdfBytes = ms.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DocumentService] PDF render failed: {ex.Message}");
                return string.Empty;
            }

            // Step 3: Write bytes to disk in one atomic operation — no file lock issues
            try
            {
                File.WriteAllBytes(pdfPath, pdfBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DocumentService] PDF save failed: {ex.Message}");
                return string.Empty;
            }

            return pdfPath;
        }

        // -------------------------------------------------------
        // Extract Learning Outcomes from PDF or DOCX
        // -------------------------------------------------------
        public static List<string> ExtractLearningOutcomes(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var text = ext == ".docx"
                ? ExtractTextFromDocx(filePath)
                : ExtractTextFromPdf(filePath);
            return ParseLearningOutcomes(text);
        }

        private static string ExtractTextFromPdf(string pdfPath)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                using var reader = new PdfReader(pdfPath);
                using var pdf = new PdfDocument(reader);
                for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
                    sb.Append(iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor
                        .GetTextFromPage(pdf.GetPage(i)));
                return sb.ToString();
            }
            catch { return string.Empty; }
        }

        private static List<string> ParseLearningOutcomes(string text)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return results;

            var loMatch = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(?:LEARNING OUTCOMES?|Learning Outcomes?|Course Learning Outcomes?).*?(?=\n[A-Z][A-Z\s]+\n|\Z)",
                System.Text.RegularExpressions.RegexOptions.Singleline |
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!loMatch.Success) return results;

            var numbered = System.Text.RegularExpressions.Regex.Matches(
                loMatch.Value,
                @"(?:^|\n)\s*(\d+)\.\s+(.+?)(?=(?:\n\s*\d+\.|\Z))",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match m in numbered)
            {
                if (m.Groups.Count < 3) continue;
                var lo = System.Text.RegularExpressions.Regex
                    .Replace(m.Groups[2].Value.Trim(), @"\s+", " ")
                    .Replace("\n", " ").Replace("\r", "");
                if (lo.Length > 20) results.Add(lo);
            }

            return results;
        }
    }
}