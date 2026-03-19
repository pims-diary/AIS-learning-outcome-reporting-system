using DocumentFormat.OpenXml.Packaging;

using WParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using WDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;

using iText.Kernel.Pdf;

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
        // Extract plain text from .docx — used for LO detection
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

            // Normalise line endings
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            // Find the LO header line
            var headerMatch = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(LEARNING OUTCOMES?|Learning Outcomes?|Course Learning Outcomes?|The learners will be able to)[^\n]*\n",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!headerMatch.Success) return results;

            // Collect lines after the header, stopping at the next section boundary
            var afterHeader = text.Substring(headerMatch.Index + headerMatch.Length);
            var rawLines = afterHeader.Split('\n');
            var loLines = new List<string>();

            foreach (var raw in rawLines)
            {
                var line = raw.Trim();

                // Stop at blank line followed by content (section separator)
                if (string.IsNullOrWhiteSpace(line))
                    break;

                // Stop at an ALL CAPS section header (e.g. COURSE DURATION, LEARNING HOURS)
                if (line.Length < 60 &&
                    line == line.ToUpper() &&
                    !line.EndsWith(".") &&
                    line.Any(char.IsLetter))
                    break;

                loLines.Add(line);
            }

            var section = string.Join("\n", loLines);

            // Try numbered format: "1. text" or "1) text"
            var numbered = System.Text.RegularExpressions.Regex.Matches(
                section,
                @"(?:^|\n)\s*\d+[\.\)]\s+(.+?)(?=\n\s*\d+[\.\)]|\Z)",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match m in numbered)
            {
                var lo = System.Text.RegularExpressions.Regex
                    .Replace(m.Groups[1].Value.Trim(), @"\s+", " ")
                    .Replace("\n", " ");
                if (lo.Length > 20) results.Add(lo);
            }

            // If no numbered LOs, each non-empty line is a separate LO
            if (!results.Any())
            {
                foreach (var line in loLines)
                {
                    var lo = System.Text.RegularExpressions.Regex
                        .Replace(line, @"\s+", " ").Trim();

                    if (lo.Length < 20) continue; // too short
                    if (lo.EndsWith(":")) continue; // header line
                    if (lo == lo.ToUpper()) continue; // ALL CAPS

                    results.Add(lo);
                }
            }

            return results;
        }
    }
}