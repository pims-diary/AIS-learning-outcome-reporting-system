using DocumentFormat.OpenXml.Packaging;

using WParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using WDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;
using WTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using WTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;
using WTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;

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
                foreach (var para in body.Descendants<WParagraph>())
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
        // Extract structured sections from DOCX using Heading2 styles
        // Returns a dictionary of section name (lowercase) → list of paragraph texts
        // -------------------------------------------------------
        private static Dictionary<string, List<string>> ExtractSectionsFromDocx(string filePath)
        {
            var sections = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);

            try
            {
                using var doc = WordprocessingDocument.Open(filePath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return sections;

                string? currentSection = null;

                foreach (var para in body.Descendants<WParagraph>())
                {
                    if (ContainsOnlyDrawings(para)) continue;

                    var text = para.InnerText?.Trim();
                    if (string.IsNullOrEmpty(text)) continue;

                    var style = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";

                    // Heading2 marks a new section boundary
                    if (style.Equals("Heading2", System.StringComparison.OrdinalIgnoreCase))
                    {
                        currentSection = text.Trim();
                        if (!sections.ContainsKey(currentSection))
                            sections[currentSection] = new List<string>();
                        continue;
                    }

                    // Collect content under current section
                    if (currentSection != null && sections.ContainsKey(currentSection))
                    {
                        sections[currentSection].Add(text);
                    }
                }
            }
            catch { }

            return sections;
        }

        // -------------------------------------------------------
        // Find the section key that matches a keyword (case-insensitive, partial match)
        // -------------------------------------------------------
        private static string? FindSectionKey(Dictionary<string, List<string>> sections, string keyword)
        {
            return sections.Keys.FirstOrDefault(k =>
                k.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // -------------------------------------------------------
        // Extract LOs from a heading-based section
        // -------------------------------------------------------
        private static List<string> ParseLOsFromSectionLines(List<string> lines)
        {
            var results = new List<string>();
            var loLines = new List<string>();

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var lineLower = line.ToLower();

                // Skip the intro line
                if (lineLower.Contains("learners will be able to") ||
                    lineLower.Contains("students will be able to"))
                    continue;

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

                    if (lo.Length < 20) continue;
                    if (lo.EndsWith(":")) continue;
                    if (lo == lo.ToUpper()) continue;

                    results.Add(lo);
                }
            }

            return results;
        }

        // -------------------------------------------------------
        // Extract Learning Outcomes from PDF or DOCX
        // -------------------------------------------------------
        public static List<string> ExtractLearningOutcomes(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            // For DOCX: try heading-based extraction first
            if (ext == ".docx")
            {
                var sections = ExtractSectionsFromDocx(filePath);
                var loKey = FindSectionKey(sections, "learning outcome");

                if (loKey != null && sections[loKey].Any())
                {
                    var headingResult = ParseLOsFromSectionLines(sections[loKey]);
                    if (headingResult.Any())
                        return headingResult;
                }

                // Fallback to text-based parsing
                return ParseLearningOutcomes(ExtractTextFromDocx(filePath));
            }

            // PDF: text-based parsing only
            return ParseLearningOutcomes(ExtractTextFromPdf(filePath));
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
            int consecutiveBlanks = 0;

            foreach (var raw in rawLines)
            {
                var line = raw.Trim();

                // Track blank lines — 2+ consecutive blanks = section break
                if (string.IsNullOrWhiteSpace(line))
                {
                    consecutiveBlanks++;
                    if (consecutiveBlanks >= 2) break;
                    continue;
                }
                consecutiveBlanks = 0;

                // Stop at an ALL CAPS section header (e.g. COURSE DURATION, LEARNING HOURS)
                if (line.Length < 60 &&
                    line == line.ToUpper() &&
                    !line.EndsWith(".") &&
                    line.Any(char.IsLetter))
                    break;

                // Stop at known section headers (mixed case like "Course DURATION")
                var lineLower = line.ToLower();
                if (lineLower.StartsWith("course duration") ||
                    lineLower.StartsWith("course content") ||
                    lineLower.StartsWith("course expectation") ||
                    lineLower.StartsWith("course assessment") ||
                    lineLower.StartsWith("learning hours") ||
                    lineLower.StartsWith("readings and") ||
                    lineLower.StartsWith("late submission") ||
                    lineLower.StartsWith("learning support"))
                    break;

                // Skip header lines like "The learners will be able to:"
                if (lineLower.Contains("learners will be able to") ||
                    lineLower.Contains("students will be able to"))
                    continue;

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
        // -------------------------------------------------------
        // Extract Assessments from course outline (docx tables or PDF text)
        // Returns list of (Title, MarksPercent, LO numbers)
        // -------------------------------------------------------
        public static List<AssessmentInfo> ExtractAssessments(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".docx")
                return ExtractAssessmentsFromDocx(filePath);
            if (ext == ".pdf")
                return ExtractAssessmentsFromPdfText(filePath);

            return new List<AssessmentInfo>();
        }

        private static List<AssessmentInfo> ExtractAssessmentsFromDocx(string filePath)
        {
            var results = new List<AssessmentInfo>();

            try
            {
                using var doc = WordprocessingDocument.Open(filePath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return results;

                // Strategy 1: Find the assessment table by Heading2 anchor
                // Walk body elements in document order, find the Heading2 that
                // contains "assessment", then parse the next table after it.
                WTable? targetTable = null;
                bool foundHeading = false;

                foreach (var element in body.ChildElements)
                {
                    if (element is WParagraph para)
                    {
                        var style = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                        if (style.Equals("Heading2", System.StringComparison.OrdinalIgnoreCase))
                        {
                            var text = para.InnerText?.Trim() ?? "";
                            if (text.IndexOf("assessment", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                foundHeading = true;
                                continue;
                            }
                        }
                    }

                    if (foundHeading && element is WTable table)
                    {
                        targetTable = table;
                        break;
                    }

                    // If we hit another Heading2 after the assessment heading
                    // but before finding a table, stop searching
                    if (foundHeading && element is WParagraph nextPara)
                    {
                        var nextStyle = nextPara.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                        if (nextStyle.Equals("Heading2", System.StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                }

                // If heading-based search found a table, parse it
                if (targetTable != null)
                {
                    results = ParseAssessmentTable(targetTable);
                }

                // Strategy 2: Fallback — scan all tables for one with Title+Marks columns
                if (!results.Any())
                {
                    foreach (var table in body.Elements<WTable>())
                    {
                        results = ParseAssessmentTable(table);
                        if (results.Any()) break;
                    }
                }
            }
            catch { }

            if (results.Count >= 2 && results.Count(a => a.LONumbers.Any()) >= 2)
                return results;

            var fallbackResults = ParseAssessmentsFromText(ExtractTextFromDocx(filePath));
            return ChooseBetterAssessmentResults(results, fallbackResults);
        }

        /// <summary>
        /// Parse a single DOCX table as an assessment table.
        /// Returns empty list if the table doesn't look like an assessment table.
        /// </summary>
        private static List<AssessmentInfo> ParseAssessmentTable(WTable table)
        {
            var results = new List<AssessmentInfo>();
            var rows = table.Elements<WTableRow>().ToList();
            if (rows.Count < 2) return results;

            var headerCells = rows[0].Elements<WTableCell>()
                .Select(c => c.InnerText.Trim().ToLower()).ToList();

            bool hasTitle = headerCells.Any(h => h.Contains("title"));
            bool hasMarks = headerCells.Any(h => h.Contains("mark"));

            if (!hasTitle || !hasMarks) return results;

            int titleCol = headerCells.FindIndex(h => h.Contains("title"));
            int marksCol = headerCells.FindIndex(h => h.Contains("mark"));
            int loCol = headerCells.FindIndex(h => h.Contains("learning") || h.Contains("outcome"));

            for (int r = 1; r < rows.Count; r++)
            {
                var cells = rows[r].Elements<WTableCell>()
                    .Select(c => (c.InnerText ?? string.Empty).Trim())
                    .ToList();

                if (cells.Count < 2) continue;

                int detectedMarksCol = marksCol >= 0 && marksCol < cells.Count && TryParseMarks(cells[marksCol], out _)
                    ? marksCol
                    : cells.FindIndex(c => TryParseMarks(c, out _));

                if (detectedMarksCol < 0) continue;

                var title = titleCol >= 0 && titleCol < cells.Count
                    ? cells[titleCol]
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(title))
                {
                    title = cells
                        .Take(detectedMarksCol)
                        .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;
                }

                title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();

                if (string.IsNullOrWhiteSpace(title) ||
                    title.Equals("Total", System.StringComparison.OrdinalIgnoreCase) ||
                    IsSuspiciousAssessmentTitle(title))
                    continue;

                TryParseMarks(cells[detectedMarksCol], out int marks);

                var loNumbers = new List<int>();
                string loText = string.Empty;

                if (loCol >= 0 && loCol < cells.Count && LooksLikeLearningOutcomeCell(cells[loCol]))
                {
                    loText = cells[loCol];
                }
                else
                {
                    loText = cells
                        .Skip(detectedMarksCol + 1)
                        .FirstOrDefault(LooksLikeLearningOutcomeCell) ?? string.Empty;
                }

                loNumbers = ExtractLearningOutcomeNumbers(loText);

                results.Add(new AssessmentInfo
                {
                    Title = title,
                    MarksPercentage = marks,
                    LONumbers = loNumbers.Distinct().ToList()
                });
            }

            return results;
        }

        private static List<AssessmentInfo> ExtractAssessmentsFromPdfText(string filePath)
        {
            try
            {
                var text = ExtractTextFromPdf(filePath);
                return ParseAssessmentsFromText(text);
            }
            catch { }

            return new List<AssessmentInfo>();
        }

        private static List<AssessmentInfo> ParseAssessmentsFromText(string text)
        {
            var results = new List<AssessmentInfo>();
            if (string.IsNullOrWhiteSpace(text)) return results;

            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<=\b)(\d)\s+(\d)\s*%", "$1$2%");

            var sectionMatch = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(?:COURSE ASSESSMENTS?|Course Assessments?)\s*\n",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!sectionMatch.Success) return results;

            var afterHeader = text.Substring(sectionMatch.Index + sectionMatch.Length);

            var stopMatch = System.Text.RegularExpressions.Regex.Match(
                afterHeader,
                @"\n(?:Submission|LATE|Late|Passing|LEARNING SUPPORT|Learning Support)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (stopMatch.Success)
                afterHeader = afterHeader.Substring(0, stopMatch.Index);

            var lines = afterHeader.Split('\n');

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var pctMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s*%");
                if (!pctMatch.Success) continue;

                int marks = int.Parse(pctMatch.Groups[1].Value);
                if (marks >= 100) continue;

                var title = line.Substring(0, pctMatch.Index).Trim();
                title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();

                if (string.IsNullOrWhiteSpace(title)) continue;
                if (title.Equals("Total", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (title.ToLower().Contains("course") && title.ToLower().Contains("mark")) continue;
                if (title.ToLower().Contains("title")) continue;
                if (IsSuspiciousAssessmentTitle(title)) continue;

                var afterPct = line.Substring(pctMatch.Index + pctMatch.Length);
                afterPct = System.Text.RegularExpressions.Regex.Replace(afterPct, @"\d{1,2}\s*(?:st|nd|rd|th)?\s+[A-Za-z]+\s+\d{2,4}", " ");
                afterPct = System.Text.RegularExpressions.Regex.Replace(afterPct, @"\d{2}/\d{2}/\d{2,4}", " ");
                afterPct = System.Text.RegularExpressions.Regex.Replace(afterPct, @"[\(\s]*Week\s*\d+[\)\s]*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                afterPct = System.Text.RegularExpressions.Regex.Replace(afterPct, @"CLASS.*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                afterPct = afterPct.Trim();

                var loNumbers = new List<int>();
                if (!string.IsNullOrWhiteSpace(afterPct))
                {
                    var nums = System.Text.RegularExpressions.Regex.Matches(afterPct, @"\d+");
                    foreach (System.Text.RegularExpressions.Match n in nums)
                    {
                        if (int.TryParse(n.Value, out int num) && num <= 20)
                            loNumbers.Add(num);
                    }
                }

                results.Add(new AssessmentInfo
                {
                    Title = title,
                    MarksPercentage = marks,
                    LONumbers = loNumbers.Distinct().ToList()
                });
            }

            if (results.Count >= 2)
                return results;

            results.Clear();

            var skipPatterns = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                { "title", "course", "marks", "marks %", "release", "date", "due", "due date",
                  "learning", "outcomes", "learning outcomes", "corresponding", "course content",
                  "total", "the assessments for this course are as follows:", "marks % date",
                  "release date", "course marks %", "due date*" };

            var titles = new List<string>();
            var marksList = new List<int>();
            var loList = new List<List<int>>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (skipPatterns.Contains(line.ToLower())) continue;

                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[\(\s]*Week\s*\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^CLASS", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d{2}/\d{2}/\d{2,4}$")) continue;
                if (line.StartsWith("*")) continue;

                var lineWithoutDates = System.Text.RegularExpressions.Regex.Replace(line, @"\d{2}/\d{2}/\d{2,4}", " ").Trim();
                lineWithoutDates = System.Text.RegularExpressions.Regex.Replace(lineWithoutDates, @"\d{1,2}\s*(?:st|nd|rd|th)?\s+[A-Za-z]+\s+\d{2,4}", " ").Trim();

                var titlePctMatch = System.Text.RegularExpressions.Regex.Match(line, @"^(.+?)\s+(\d+)\s*%\s*$");
                if (titlePctMatch.Success)
                {
                    var t = titlePctMatch.Groups[1].Value.Trim();
                    int m = int.Parse(titlePctMatch.Groups[2].Value);
                    if (!t.Equals("Total", System.StringComparison.OrdinalIgnoreCase) &&
                        !skipPatterns.Contains(t.ToLower()) &&
                        !IsSuspiciousAssessmentTitle(t) &&
                        m < 100)
                    {
                        titles.Add(t);
                        marksList.Add(m);
                        continue;
                    }
                }

                var pctOnly = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\s*%\s*$");
                if (pctOnly.Success)
                {
                    int m = int.Parse(pctOnly.Groups[1].Value);
                    if (m < 100) marksList.Add(m);
                    continue;
                }

                lineWithoutDates = System.Text.RegularExpressions.Regex.Replace(lineWithoutDates, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(lineWithoutDates) &&
                    System.Text.RegularExpressions.Regex.IsMatch(lineWithoutDates, @"^[\d,\s]+$"))
                {
                    var nums = System.Text.RegularExpressions.Regex.Matches(lineWithoutDates, @"\d+");
                    var loNums = new List<int>();
                    foreach (System.Text.RegularExpressions.Match n in nums)
                    {
                        if (int.TryParse(n.Value, out int num) && num <= 20)
                            loNums.Add(num);
                    }
                    if (loNums.Any()) loList.Add(loNums);
                    continue;
                }

                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\d{2}/\d{2}/\d{2,4}|\d{1,2}\s*(?:st|nd|rd|th)\s+[A-Za-z]+"))
                {
                    var remaining = lineWithoutDates;
                    if (!string.IsNullOrWhiteSpace(remaining) &&
                        System.Text.RegularExpressions.Regex.IsMatch(remaining, @"\d"))
                    {
                        var loMatch = System.Text.RegularExpressions.Regex.Match(remaining, @"([\d][\d,\s]*[\d])");
                        if (loMatch.Success)
                        {
                            var nums = System.Text.RegularExpressions.Regex.Matches(loMatch.Value, @"\d+");
                            var loNums = new List<int>();
                            foreach (System.Text.RegularExpressions.Match n in nums)
                            {
                                if (int.TryParse(n.Value, out int num) && num <= 20)
                                    loNums.Add(num);
                            }
                            if (loNums.Any()) loList.Add(loNums);
                        }
                    }
                    continue;
                }

                if (line.Length > 4 &&
                    !System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d") &&
                    System.Text.RegularExpressions.Regex.IsMatch(line, @"[a-zA-Z]{3,}") &&
                    !IsSuspiciousAssessmentTitle(line))
                {
                    titles.Add(line);
                }
            }

            int count = System.Math.Min(titles.Count, marksList.Count);
            for (int i = 0; i < count; i++)
            {
                results.Add(new AssessmentInfo
                {
                    Title = titles[i],
                    MarksPercentage = marksList[i],
                    LONumbers = i < loList.Count ? loList[i].Distinct().ToList() : new List<int>()
                });
            }

            return results;
        }

        private static bool TryParseMarks(string text, out int marks)
        {
            var match = System.Text.RegularExpressions.Regex.Match(text ?? string.Empty, @"(\d+)\s*%");
            if (match.Success && int.TryParse(match.Groups[1].Value, out marks))
                return true;

            marks = 0;
            return false;
        }

        private static bool LooksLikeLearningOutcomeCell(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var normalized = text.Trim().ToLowerInvariant();
            if (normalized.Contains("week") || normalized.Contains("release") || normalized.Contains("due"))
                return false;

            var numbers = ExtractLearningOutcomeNumbers(text);
            return numbers.Any() &&
                   numbers.All(n => n <= 20) &&
                   System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^(?:lo\s*)?\d+(?:\s*,\s*(?:lo\s*)?\d+)*$");
        }

        private static List<int> ExtractLearningOutcomeNumbers(string text)
        {
            var numbers = new List<int>();
            if (string.IsNullOrWhiteSpace(text))
                return numbers;

            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\d+");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (int.TryParse(match.Value, out int num) && num <= 20)
                    numbers.Add(num);
            }

            return numbers;
        }

        private static bool IsSuspiciousAssessmentTitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var normalized = System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();

            return normalized.Contains("corresponding course content") ||
                   normalized.Contains("learning outcomes") ||
                   normalized.Contains("release date") ||
                   normalized.Contains("due date") ||
                   normalized.Contains("course marks") ||
                   System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^weeks?\s+\d") ||
                   System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^week\s+\d");
        }

        private static List<AssessmentInfo> ChooseBetterAssessmentResults(
            List<AssessmentInfo> primary,
            List<AssessmentInfo> fallback)
        {
            if (!primary.Any()) return fallback;
            if (!fallback.Any()) return primary;

            int Score(List<AssessmentInfo> items) =>
                items.Count +
                items.Count(i => i.MarksPercentage > 0) +
                (items.Count(i => i.LONumbers.Any()) * 2);

            return Score(fallback) > Score(primary) ? fallback : primary;
        }

        // -------------------------------------------------------
        // Extract LOs from an assignment document
        // Handles explicit LO numbers and plain-text LO wording after an LO header.
        // -------------------------------------------------------
        public static List<AssignmentLO> ExtractLOsFromAssignmentDoc(string filePath)
        {
            var results = new List<AssignmentLO>();
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            string text = ext == ".docx"
                ? ExtractTextFromDocx(filePath)
                : ext == ".pdf" ? ExtractTextFromPdf(filePath) : "";

            if (string.IsNullOrWhiteSpace(text)) return results;

            // Strategy 1: Match "Learning Outcome 1: text" or "LO 1: text"
            var matches = System.Text.RegularExpressions.Regex.Matches(
                text,
                @"(?:Learning\s+Outcome|LO)\s*(\d+)\s*[:\-–]\s*(.+?)(?=(?:Learning\s+Outcome|LO)\s*\d+\s*[:\-–]|General\s+Instruction|Instructions?\b|Tasks?\s*\(|Note:|\n\s*\n|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (int.TryParse(m.Groups[1].Value, out int loNum))
                {
                    var loText = NormalizeAssignmentOutcomeText(m.Groups[2].Value);

                    if (!IsPlausibleAssignmentLOText(loText))
                        continue;

                    results.Add(new AssignmentLO
                    {
                        Number = loNum,
                        Text = loText
                    });
                }
            }

            // Strategy 2: If Strategy 1 found nothing, inspect the text after a
            // learning outcomes header. This supports numbered items and plain
            // bullet/paragraph LO wording copied from the outline.
            if (!results.Any())
            {
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");

                var headerMatch = System.Text.RegularExpressions.Regex.Match(
                    text,
                    @"(?im)^.*learning\s+outcomes?.*$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (headerMatch.Success)
                {
                    var afterLOHeader = text.Substring(headerMatch.Index + headerMatch.Length);

                    var sectionEnd = System.Text.RegularExpressions.Regex.Match(
                        afterLOHeader,
                        @"\n\s*\n\s*\n|General\s+Instruction|Instructions?\b|TASK\s|Task\s+\d|Marking|MARKING|Submission|SUBMISSION|The\s+assessment\s+has|Final\s+Product\s*:|Final\s+Presentation\s*:|Assessment\s+Criteria|Scope\s+of\s+Work",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    var loSection = sectionEnd.Success
                        ? afterLOHeader.Substring(0, sectionEnd.Index)
                        : afterLOHeader;

                    var numberedItems = System.Text.RegularExpressions.Regex.Matches(
                        loSection,
                        @"(?:^|\n)\s*(\d+)[\.\)]\s+(.+?)(?=\n\s*\d+[\.\)]\s|$)",
                        System.Text.RegularExpressions.RegexOptions.Singleline);

                    foreach (System.Text.RegularExpressions.Match m in numberedItems)
                    {
                        if (int.TryParse(m.Groups[1].Value, out int loNum) && loNum <= 20)
                        {
                            var loText = NormalizeAssignmentOutcomeText(m.Groups[2].Value);

                            if (IsPlausibleAssignmentLOText(loText))
                            {
                                results.Add(new AssignmentLO
                                {
                                    Number = loNum,
                                    Text = loText
                                });
                            }
                        }
                    }

                    // If the document lists LO wording with no explicit numbers,
                    // keep the text and let CrossCheckLOs infer the matching LO.
                    if (!results.Any())
                    {
                        var plainItems = loSection
                            .Split('\n')
                            .Select(NormalizeAssignmentOutcomeText)
                            .Where(IsPlausibleAssignmentLOText)
                            .Distinct(System.StringComparer.OrdinalIgnoreCase);

                        foreach (var item in plainItems)
                        {
                            results.Add(new AssignmentLO
                            {
                                Number = 0,
                                Text = item
                            });
                        }
                    }
                }
            }

            return results;
        }

        // -------------------------------------------------------
        // Cross-check assignment LOs against course outline LOs
        // -------------------------------------------------------
        public static LOCrossCheckResult CrossCheckLOs(
            List<AssignmentLO> assignmentLOs,
            List<(int OrderNumber, string Text)> outlineLOs,
            List<int> allowedLONumbers)
        {
            var result = new LOCrossCheckResult();
            var usedAssignmentIndexes = new HashSet<int>();

            foreach (var allowed in allowedLONumbers.Distinct())
            {
                var outlineLO = outlineLOs.FirstOrDefault(o => o.OrderNumber == allowed);
                if (string.IsNullOrWhiteSpace(outlineLO.Text))
                    continue;

                int matchedIndex = FindBestAssignmentMatchIndex(
                    assignmentLOs,
                    usedAssignmentIndexes,
                    allowed,
                    outlineLO.Text);

                if (matchedIndex < 0)
                {
                    result.Missing.Add(new LOCheckItem
                    {
                        LONumber = allowed,
                        OutlineText = outlineLO.Text,
                        Status = "Missing"
                    });
                }
                else
                {
                    usedAssignmentIndexes.Add(matchedIndex);
                    var assignmentLO = assignmentLOs[matchedIndex];

                    var similarity = ComputeSimilarity(
                        NormalizeText(outlineLO.Text),
                        NormalizeText(assignmentLO.Text));

                    result.Matched.Add(new LOCheckItem
                    {
                        LONumber = allowed,
                        OutlineText = outlineLO.Text,
                        AssignmentText = assignmentLO.Text,
                        Status = similarity > 0.7 ? "Aligned" : "Wording Differs",
                        Similarity = similarity
                    });
                }
            }

            for (int i = 0; i < assignmentLOs.Count; i++)
            {
                if (usedAssignmentIndexes.Contains(i))
                    continue;

                var aLO = assignmentLOs[i];
                if (!allowedLONumbers.Contains(aLO.Number))
                {
                    if (aLO.Number <= 0)
                        continue;

                    result.Extra.Add(new LOCheckItem
                    {
                        LONumber = aLO.Number,
                        AssignmentText = aLO.Text,
                        Status = "Not in course outline for this assessment"
                    });
                }
            }

            return result;
        }

        private static bool IsPlausibleAssignmentLOText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var normalized = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            if (normalized.Length < 25)
                return false;

            if (normalized.Length > 320)
                return false;

            var lower = normalized.ToLowerInvariant();

            if (lower.StartsWith("due:") ||
                lower.StartsWith("where:") ||
                lower.StartsWith("instructions") ||
                lower.StartsWith("this assessment is worth") ||
                lower.StartsWith("the assessment has") ||
                lower.StartsWith("final product") ||
                lower.StartsWith("final presentation") ||
                lower.StartsWith("scope of work") ||
                lower.StartsWith("assessment criteria") ||
                lower.StartsWith("submission"))
                return false;

            if (ContainsInstructionNoise(lower))
                return false;

            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"^\d+\s*%"))
                return false;

            return lower.Any(char.IsLetter);
        }

        private static string NormalizeAssignmentOutcomeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"^\p{P}+", "");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized.TrimEnd('.', ' ', ';', ':');
        }

        private static bool ContainsInstructionNoise(string lower)
        {
            var noiseMarkers = new[]
            {
                "open-book",
                "invigilator",
                "moodle",
                "teams",
                "ais-issued laptop",
                "monitoring software",
                "channel ",
                "disciplinary committee",
                "special permission",
                "files submitted",
                "debugging purposes",
                "sql server",
                "visual studio",
                "asp.net core mvc"
            };

            return noiseMarkers.Any(lower.Contains);
        }

        private static int FindBestAssignmentMatchIndex(
            List<AssignmentLO> assignmentLOs,
            HashSet<int> usedAssignmentIndexes,
            int allowedNumber,
            string outlineText)
        {
            for (int i = 0; i < assignmentLOs.Count; i++)
            {
                if (usedAssignmentIndexes.Contains(i))
                    continue;

                if (assignmentLOs[i].Number == allowedNumber)
                    return i;
            }

            var normalizedOutlineText = NormalizeText(outlineText);
            double bestSimilarity = 0;
            int bestIndex = -1;

            for (int i = 0; i < assignmentLOs.Count; i++)
            {
                if (usedAssignmentIndexes.Contains(i))
                    continue;

                var candidate = assignmentLOs[i];
                if (string.IsNullOrWhiteSpace(candidate.Text))
                    continue;

                var similarity = ComputeSimilarity(
                    normalizedOutlineText,
                    NormalizeText(candidate.Text));

                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestIndex = i;
                }
            }

            return bestSimilarity >= 0.45 ? bestIndex : -1;
        }

        private static string NormalizeText(string text)
        {
            var normalized = System.Text.RegularExpressions.Regex
                .Replace(text.ToLower().Trim(), @"[^\w\s]", "");

            return System.Text.RegularExpressions.Regex
                .Replace(normalized, @"\s+", " ")
                .Trim();
        }

        private static double ComputeSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;

            var wordsA = a.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var wordsB = b.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            int intersection = wordsA.Intersect(wordsB).Count();
            int union = wordsA.Union(wordsB).Count();

            return union == 0 ? 0 : (double)intersection / union;
        }
    }

    public class AssessmentInfo
    {
        public string Title { get; set; } = string.Empty;
        public int MarksPercentage { get; set; }
        public List<int> LONumbers { get; set; } = new();
    }

    public class AssignmentLO
    {
        public int Number { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class LOCheckItem
    {
        public int LONumber { get; set; }
        public string OutlineText { get; set; } = string.Empty;
        public string AssignmentText { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public double Similarity { get; set; }
    }

    public class LOCrossCheckResult
    {
        public List<LOCheckItem> Matched { get; set; } = new();
        public List<LOCheckItem> Missing { get; set; } = new();
        public List<LOCheckItem> Extra { get; set; } = new();
        public bool IsFullyAligned => !Missing.Any() && !Extra.Any() &&
            Matched.All(m => m.Status == "Aligned");
    }
}
