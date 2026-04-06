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

                foreach (var table in body.Elements<WTable>())
                {
                    var rows = table.Elements<WTableRow>().ToList();
                    if (rows.Count < 2) continue;

                    var headerCells = rows[0].Elements<WTableCell>()
                        .Select(c => c.InnerText.Trim().ToLower()).ToList();

                    bool hasTitle = headerCells.Any(h => h.Contains("title"));
                    bool hasMarks = headerCells.Any(h => h.Contains("mark"));

                    if (!hasTitle || !hasMarks) continue;

                    int titleCol = headerCells.FindIndex(h => h.Contains("title"));
                    int marksCol = headerCells.FindIndex(h => h.Contains("mark"));
                    int loCol = headerCells.FindIndex(h => h.Contains("learning") || h.Contains("outcome"));

                    for (int r = 1; r < rows.Count; r++)
                    {
                        var cells = rows[r].Elements<WTableCell>().ToList();
                        if (cells.Count <= titleCol) continue;

                        var title = cells[titleCol].InnerText.Trim();

                        if (string.IsNullOrWhiteSpace(title) ||
                            title.Equals("Total", System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        int marks = 0;
                        if (marksCol >= 0 && marksCol < cells.Count)
                        {
                            var marksText = cells[marksCol].InnerText.Trim().Replace("%", "");
                            int.TryParse(marksText, out marks);
                        }

                        var loNumbers = new List<int>();
                        if (loCol >= 0 && loCol < cells.Count)
                        {
                            var loText = cells[loCol].InnerText.Trim();
                            var matches = System.Text.RegularExpressions.Regex.Matches(loText, @"\d+");
                            foreach (System.Text.RegularExpressions.Match m in matches)
                            {
                                if (int.TryParse(m.Value, out int num))
                                    loNumbers.Add(num);
                            }
                        }

                        results.Add(new AssessmentInfo
                        {
                            Title = title,
                            MarksPercentage = marks,
                            LONumbers = loNumbers
                        });
                    }

                    if (results.Any()) break;
                }
            }
            catch { }

            return results;
        }

        private static List<AssessmentInfo> ExtractAssessmentsFromPdfText(string filePath)
        {
            var results = new List<AssessmentInfo>();

            try
            {
                var text = ExtractTextFromPdf(filePath);
                if (string.IsNullOrWhiteSpace(text)) return results;

                text = text.Replace("\r\n", "\n").Replace("\r", "\n");

                // Find the assessment section
                var sectionMatch = System.Text.RegularExpressions.Regex.Match(
                    text,
                    @"(?:COURSE ASSESSMENTS?|Course Assessments?)\s*\n",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (!sectionMatch.Success) return results;

                var afterHeader = text.Substring(sectionMatch.Index + sectionMatch.Length);

                // Cut off at next major section
                var stopMatch = System.Text.RegularExpressions.Regex.Match(
                    afterHeader,
                    @"\n(?:Submission|LATE|Late|Passing|LEARNING SUPPORT|Learning Support)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (stopMatch.Success)
                    afterHeader = afterHeader.Substring(0, stopMatch.Index);

                var lines = afterHeader.Split('\n');

                // ============================================================
                // APPROACH 1: Find lines with percentage anywhere (iText style)
                // Handles: "ASSIGNMENT 1 30 % 13/02/2026 ... 1, 2, 3, 5 CLASS 1-10"
                // ============================================================
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Find percentage anywhere in line
                    var pctMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s*%");
                    if (!pctMatch.Success) continue;

                    int marks = int.Parse(pctMatch.Groups[1].Value);
                    if (marks >= 100) continue;

                    // Title = text before the percentage
                    var title = line.Substring(0, pctMatch.Index).Trim();
                    title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();

                    // Skip junk
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    if (title.Equals("Total", System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (title.ToLower().Contains("course") && title.ToLower().Contains("mark")) continue;
                    if (title.ToLower().Contains("title")) continue;

                    // Get text after percentage, strip dates, weeks, CLASS refs
                    var afterPct = line.Substring(pctMatch.Index + pctMatch.Length);
                    afterPct = System.Text.RegularExpressions.Regex.Replace(afterPct, @"\d{2}/\d{2}/\d{2,4}", " ");
                    afterPct = System.Text.RegularExpressions.Regex.Replace(afterPct, @"[\(\s]*Week\s*\d+[\)\s]*", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    afterPct = System.Text.RegularExpressions.Regex.Replace(afterPct, @"CLASS.*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    afterPct = afterPct.Trim();

                    // Find LO numbers from remaining text
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

                // If Approach 1 found 2+ results, use them
                if (results.Count >= 2) return results;

                // ============================================================
                // APPROACH 2: Column-by-column (pdftotext style)
                // Titles, marks, and LOs are on separate lines
                // ============================================================
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

                    // Title + percentage together
                    var titlePctMatch = System.Text.RegularExpressions.Regex.Match(line, @"^(.+?)\s+(\d+)\s*%\s*$");
                    if (titlePctMatch.Success)
                    {
                        var t = titlePctMatch.Groups[1].Value.Trim();
                        int m = int.Parse(titlePctMatch.Groups[2].Value);
                        if (!t.Equals("Total", System.StringComparison.OrdinalIgnoreCase) &&
                            !skipPatterns.Contains(t.ToLower()) && m < 100)
                        {
                            titles.Add(t);
                            marksList.Add(m);
                            continue;
                        }
                    }

                    // Standalone percentage
                    var pctOnly = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\s*%\s*$");
                    if (pctOnly.Success)
                    {
                        int m = int.Parse(pctOnly.Groups[1].Value);
                        if (m < 100) marksList.Add(m);
                        continue;
                    }

                    // LO numbers after stripping dates
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

                    // Mixed date + LO line
                    if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\d{2}/\d{2}/\d{2,4}"))
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

                    // Title
                    if (line.Length > 4 &&
                        !System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d") &&
                        System.Text.RegularExpressions.Regex.IsMatch(line, @"[a-zA-Z]{3,}"))
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
            }
            catch { }

            return results;
        }

        // -------------------------------------------------------
        // Extract LOs from an assignment document
        // Handles: "Learning Outcome 1: text" AND numbered lists after LO header
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
                @"(?:Learning\s+Outcome|LO)\s*(\d+)\s*[:\-–]\s*(.+?)(?=(?:Learning\s+Outcome|LO)\s*\d+\s*[:\-–]|General\s+Instruction|Tasks?\s*\(|Note:|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (int.TryParse(m.Groups[1].Value, out int loNum))
                {
                    var loText = System.Text.RegularExpressions.Regex
                        .Replace(m.Groups[2].Value.Trim(), @"\s+", " ")
                        .TrimEnd('.', ' ');

                    results.Add(new AssignmentLO
                    {
                        Number = loNum,
                        Text = loText
                    });
                }
            }

            // Strategy 2: If Strategy 1 found nothing, look for numbered list after "learning outcomes" header
            if (!results.Any())
            {
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");

                // Find the LO header line
                var headerMatch = System.Text.RegularExpressions.Regex.Match(
                    text,
                    @"learning\s+outcomes.*?:\s*\n",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (headerMatch.Success)
                {
                    var afterLOHeader = text.Substring(headerMatch.Index + headerMatch.Length);

                    // Cut off at next section (General Instructions, empty line gap, or any ALL CAPS header)
                    var sectionEnd = System.Text.RegularExpressions.Regex.Match(
                        afterLOHeader,
                        @"\n\s*\n|General\s+Instruction|TASK\s|Task\s+\d|Marking|MARKING|Submission|SUBMISSION",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    var loSection = sectionEnd.Success
                        ? afterLOHeader.Substring(0, sectionEnd.Index)
                        : afterLOHeader;

                    // Extract numbered items from just the LO section
                    var numberedItems = System.Text.RegularExpressions.Regex.Matches(
                        loSection,
                        @"(\d+)\.\s+(.+?)(?=\n\s*\d+\.\s|$)",
                        System.Text.RegularExpressions.RegexOptions.Singleline);

                    foreach (System.Text.RegularExpressions.Match m in numberedItems)
                    {
                        if (int.TryParse(m.Groups[1].Value, out int loNum) && loNum <= 20)
                        {
                            var loText = System.Text.RegularExpressions.Regex
                                .Replace(m.Groups[2].Value.Trim(), @"\s+", " ")
                                .TrimEnd('.', ' ');

                            if (loText.Length > 15)
                            {
                                results.Add(new AssignmentLO
                                {
                                    Number = loNum,
                                    Text = loText
                                });
                            }
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

            foreach (var allowed in allowedLONumbers)
            {
                var outlineLO = outlineLOs.FirstOrDefault(o => o.OrderNumber == allowed);
                var assignmentLO = assignmentLOs.FirstOrDefault(a => a.Number == allowed);

                if (outlineLO.Text == null) continue;

                if (assignmentLO == null)
                {
                    // LO is in course outline for this assessment but missing from assignment doc
                    result.Missing.Add(new LOCheckItem
                    {
                        LONumber = allowed,
                        OutlineText = outlineLO.Text,
                        Status = "Missing"
                    });
                }
                else
                {
                    // Compare wording
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

            // Check for extra LOs in assignment doc that aren't in the allowed list
            foreach (var aLO in assignmentLOs)
            {
                if (!allowedLONumbers.Contains(aLO.Number))
                {
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

        private static string NormalizeText(string text)
        {
            return System.Text.RegularExpressions.Regex
                .Replace(text.ToLower().Trim(), @"[^\w\s]", "")
                .Replace("  ", " ");
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
