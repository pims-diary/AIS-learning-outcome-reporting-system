using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AIS_LO_System.Services;
using System.Text.Json;
using System.Linq;
using AIS_LO_System.Data;
using AIS_LO_System.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using System.Text.RegularExpressions;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class CourseInformationController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _context;

        public CourseInformationController(IWebHostEnvironment env, ApplicationDbContext context)
        {
            _env = env;
            _context = context;
        }

        private bool AllowOutlineReupload(string courseCode, int year, int trimester)
        {
            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);
            return course?.CanReuploadOutline ?? true;
        }

        private bool AllowLOEdit(string courseCode, int year, int trimester)
        {
            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);
            return course?.CanEditLO ?? true;
        }

        // -------------------------
        // COURSE OUTLINE (VIEW)
        // -------------------------
        [HttpGet]
        public IActionResult Outline(string courseCode, int year, int trimester)
        {
            SetCourseContext(courseCode, year, trimester);

            ViewBag.CanReupload = AllowOutlineReupload(courseCode, year, trimester);

            // Check for the outline in all supported formats — prefer PDF for browser preview
            var dir = Path.Combine(_env.WebRootPath, "uploads", "outlines");
            var baseName = GetOutlineBaseName(courseCode, year, trimester);

            string? outlineUrl = null;
            foreach (var ext in new[] { ".pdf", ".docx" })
            {
                var filePath = Path.Combine(dir, baseName + ext);
                if (System.IO.File.Exists(filePath))
                {
                    outlineUrl = $"/uploads/outlines/{baseName}{ext}";
                    break;
                }
            }

            ViewBag.OutlineUrl = outlineUrl;
            return View();
        }

        // -------------------------
        // COURSE OUTLINE (UPLOAD) — supports PDF and Word (.docx)
        // -------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadOutline(IFormFile file, string courseCode, int year, int trimester)
        {
            if (!AllowOutlineReupload(courseCode, year, trimester))
                return Forbid();

            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please choose a PDF or Word document.";
                return RedirectToAction(nameof(Outline), new { courseCode, year, trimester });
            }

            if (!DocumentService.IsAllowed(file.FileName))
            {
                TempData["Error"] = "Only PDF and Word documents (.docx) are supported.";
                return RedirectToAction(nameof(Outline), new { courseCode, year, trimester });
            }

            var dir = Path.Combine(_env.WebRootPath, "uploads", "outlines");
            Directory.CreateDirectory(dir);

            // Keep the original extension — do NOT rename or convert
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var baseName = GetOutlineBaseName(courseCode, year, trimester);
            var savedPath = Path.Combine(dir, baseName + ext);

            // Delete any previous version (different extension) before saving
            foreach (var old in new[] { ".pdf", ".docx" })
            {
                var oldPath = Path.Combine(dir, baseName + old);
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            using (var stream = System.IO.File.Create(savedPath))
                await file.CopyToAsync(stream);

            // Check if the uploaded file's course code matches this course
            string mismatchWarning = null;
            try
            {
                var fileText = ext == ".docx"
                    ? DocumentService.ExtractTextFromDocx(savedPath)
                    : "";

                if (ext == ".pdf")
                {
                    // Use reflection-free approach — just read first bit of text
                    using var pdfReader = new iText.Kernel.Pdf.PdfReader(savedPath);
                    using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(pdfReader);
                    if (pdfDoc.GetNumberOfPages() > 0)
                        fileText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor
                            .GetTextFromPage(pdfDoc.GetPage(1));
                }

                if (!string.IsNullOrWhiteSpace(fileText))
                {
                    // Look for course codes like COMP720, SOFT708, INFO712 in the first chunk of text
                    var codeMatch = Regex.Match(fileText.Substring(0, Math.Min(fileText.Length, 2000)),
                        @"\b([A-Z]{3,4}\d{3})\b");

                    if (codeMatch.Success)
                    {
                        var detectedCode = codeMatch.Groups[1].Value;
                        if (!detectedCode.Equals(courseCode, StringComparison.OrdinalIgnoreCase))
                        {
                            mismatchWarning = $"⚠️ Warning: This file appears to be for {detectedCode}, but you uploaded it to {courseCode}. Please check you uploaded the correct file.";
                        }
                    }
                }
            }
            catch { }

            // Extract Learning Outcomes from the uploaded file
            try
            {
                var extractedLOs = DocumentService.ExtractLearningOutcomes(savedPath);
                if (extractedLOs.Any())
                {
                    SaveLos(courseCode, year, trimester, extractedLOs);
                    SyncLosToDatabase(courseCode, extractedLOs);
                }

                // Extract Assessments from the course outline table
                var extractedAssessments = DocumentService.ExtractAssessments(savedPath);
                if (extractedAssessments.Any())
                {
                    SyncAssessmentsToDatabase(courseCode, year, trimester, extractedAssessments);
                }

                // Build success message
                var parts = new List<string> { "Course outline uploaded!" };
                if (extractedLOs.Any())
                    parts.Add($"{extractedLOs.Count} Learning Outcomes auto-extracted.");
                if (extractedAssessments.Any())
                    parts.Add($"{extractedAssessments.Count} Assessment(s) auto-extracted.");
                if (!extractedLOs.Any() && !extractedAssessments.Any())
                    parts.Add("(No Learning Outcomes or Assessments found — add them manually)");

                TempData["Success"] = string.Join(" ", parts);

                if (mismatchWarning != null)
                    TempData["Error"] = mismatchWarning;
            }
            catch
            {
                TempData["Success"] = "Course outline uploaded!";
            }

            return RedirectToAction(nameof(Outline), new { courseCode, year, trimester });
        }

        // ✅ NEW: Extract Learning Outcomes from PDF
        private List<string> ExtractLearningOutcomesFromPDF(string pdfPath)
        {
            var learningOutcomes = new List<string>();

            try
            {
                using (PdfReader reader = new PdfReader(pdfPath))
                using (PdfDocument pdfDoc = new PdfDocument(reader))
                {
                    // Extract text from all pages
                    string fullText = "";
                    for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                    {
                        var page = pdfDoc.GetPage(i);
                        fullText += PdfTextExtractor.GetTextFromPage(page);
                    }

                    // Find Learning Outcomes section
                    learningOutcomes = ExtractLOsFromText(fullText);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                Console.WriteLine($"Error extracting LOs from PDF: {ex.Message}");
            }

            return learningOutcomes;
        }

        // ✅ NEW: Parse text to extract numbered Learning Outcomes
        private List<string> ExtractLOsFromText(string text)
        {
            var learningOutcomes = new List<string>();

            try
            {
                // Find "LEARNING OUTCOMES" section
                var loSectionMatch = Regex.Match(
                    text,
                    @"LEARNING OUTCOMES.*?(?=\n[A-Z][A-Z\s]+\n|\Z)",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase
                );

                if (!loSectionMatch.Success)
                {
                    // Try alternative headers
                    loSectionMatch = Regex.Match(
                        text,
                        @"(?:Learning Outcomes|Course Learning Outcomes|Upon completion).*?(?=\n[A-Z][A-Z\s]+\n|\Z)",
                        RegexOptions.Singleline | RegexOptions.IgnoreCase
                    );
                }

                if (loSectionMatch.Success)
                {
                    string loSection = loSectionMatch.Value;

                    // Extract numbered items (1., 2., 3., etc.)
                    var numberedItems = Regex.Matches(
                        loSection,
                        @"(?:^|\n)\s*(\d+)\.\s+(.+?)(?=(?:\n\s*\d+\.|\Z))",
                        RegexOptions.Singleline
                    );

                    foreach (Match match in numberedItems)
                    {
                        if (match.Groups.Count >= 3)
                        {
                            var loText = match.Groups[2].Value.Trim();

                            // Clean up the text
                            loText = Regex.Replace(loText, @"\s+", " "); // Remove extra whitespace
                            loText = loText.Replace("\n", " ").Replace("\r", "");

                            // Skip if too short (probably not a real LO)
                            if (loText.Length > 20)
                            {
                                learningOutcomes.Add(loText);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LOs from text: {ex.Message}");
            }

            return learningOutcomes;
        }

        // -------------------------
        // LEARNING OUTCOMES (VIEW)
        // -------------------------
        [HttpGet]
        public IActionResult LearningOutcomes(string courseCode, int year, int trimester)
        {
            SetCourseContext(courseCode, year, trimester);

            ViewBag.CanEditLO = AllowLOEdit(courseCode, year, trimester);

            var los = LoadLos(courseCode, year, trimester);

            ViewBag.LearningOutcomes = los;
            ViewBag.LOs = los;

            ViewBag.EditMode = false;
            return View();
        }

        [HttpGet]
        public IActionResult EditLearningOutcomes(string courseCode, int year, int trimester)
        {
            SetCourseContext(courseCode, year, trimester);

            ViewBag.CanEditLO = AllowLOEdit(courseCode, year, trimester);

            var los = LoadLos(courseCode, year, trimester);
            ViewBag.LearningOutcomes = los;
            ViewBag.LOs = los;

            ViewBag.EditMode = true;
            return View("LearningOutcomes");
        }

        // -------------------------
        // LEARNING OUTCOMES (SAVE)
        // -------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveLearningOutcomes(string courseCode, int year, int trimester, List<string> outcomes)
        {
            if (!AllowLOEdit(courseCode, year, trimester))
                return Forbid();

            var cleaned = (outcomes ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            // Save to JSON (existing functionality)
            SaveLos(courseCode, year, trimester, cleaned);

            // Sync to database so Epic 8 can use them
            SyncLosToDatabase(courseCode, cleaned);

            TempData["Success"] = "Learning outcomes saved successfully!";
            return RedirectToAction(nameof(LearningOutcomes), new { courseCode, year, trimester });
        }

        // -------------------------
        // Helpers
        // -------------------------
        private void SetCourseContext(string courseCode, int year, int trimester)
        {
            ViewBag.CourseCode = courseCode;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;

            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);

            ViewBag.CourseTitle = course?.Title ?? "";
        }

        private static string GetOutlineBaseName(string courseCode, int year, int trimester)
            => $"{courseCode}-{year}-T{trimester}";

        // Keep for backwards compatibility with any existing .pdf files
        private static string GetOutlineFileName(string courseCode, int year, int trimester)
            => $"{courseCode}-{year}-T{trimester}.pdf";

        private string GetLosPath(string courseCode, int year, int trimester)
        {
            var dir = Path.Combine(_env.ContentRootPath, "App_Data", "learning-outcomes");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{courseCode}-{year}-T{trimester}.json");
        }

        private List<string> LoadLos(string courseCode, int year, int trimester)
        {
            var path = GetLosPath(courseCode, year, trimester);

            if (!System.IO.File.Exists(path))
                return new List<string>();

            try
            {
                var json = System.IO.File.ReadAllText(path);
                var los = JsonSerializer.Deserialize<List<string>>(json);
                return los ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private void SaveLos(string courseCode, int year, int trimester, List<string> los)
        {
            var path = GetLosPath(courseCode, year, trimester);
            var json = JsonSerializer.Serialize(los, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(path, json);
        }

        // Sync LOs to database for Epic 8
        private void SyncLosToDatabase(string courseCode, List<string> loTexts)
        {
            try
            {
                var existingLOs = _context.LearningOutcomes
                    .Where(lo => lo.CourseCode == courseCode)
                    .ToList();

                _context.LearningOutcomes.RemoveRange(existingLOs);

                int orderNumber = 1;
                foreach (var loText in loTexts)
                {
                    var lo = new LearningOutcome
                    {
                        CourseCode = courseCode,
                        LearningOutcomeText = loText,
                        OrderNumber = orderNumber++
                    };
                    _context.LearningOutcomes.Add(lo);
                }

                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error syncing LOs to database: {ex.Message}");
            }
        }

        // Sync extracted assessments to database
        private void SyncAssessmentsToDatabase(string courseCode, int year, int trimester,
            List<AssessmentInfo> assessments)
        {
            try
            {
                var course = _context.Courses
                    .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);

                var courseTitle = course?.Title ?? "";

                // Get all existing assignments for this course
                var existingAssignments = _context.Assignments
                    .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                    .ToList();

                // Get existing LOs to map order numbers to IDs
                var courseLOs = _context.LearningOutcomes
                    .Where(lo => lo.CourseCode == courseCode)
                    .ToList();

                var extractedNames = assessments.Select(a => a.Title).ToList();

                // Remove old assignments that are NOT in the new outline
                // BUT only if they have no rubric or marks (safe to delete)
                foreach (var old in existingAssignments)
                {
                    if (!extractedNames.Contains(old.AssessmentName))
                    {
                        bool hasRubric = _context.Rubrics.Any(r => r.AssignmentId == old.Id);
                        bool hasMarks = _context.StudentAssessmentMarks
                            .Any(m => m.CourseCode == courseCode && m.AssessmentName == old.AssessmentName);

                        if (!hasRubric && !hasMarks)
                        {
                            _context.Assignments.Remove(old);
                        }
                    }
                }
                _context.SaveChanges();

                // Refresh existing list after cleanup
                existingAssignments = _context.Assignments
                    .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                    .ToList();

                foreach (var assess in assessments)
                {
                    // Map LO order numbers to database IDs
                    var loIds = new List<int>();
                    foreach (var loNum in assess.LONumbers)
                    {
                        var lo = courseLOs.FirstOrDefault(l => l.OrderNumber == loNum);
                        if (lo != null) loIds.Add(lo.Id);
                    }

                    var selectedLOIds = loIds.Any() ? string.Join(",", loIds) : null;

                    // Check if this assessment already exists
                    var existing = existingAssignments.FirstOrDefault(a => a.AssessmentName == assess.Title);

                    if (existing != null)
                    {
                        // Update existing — safe, doesn't delete rubric or marks
                        existing.MarksPercentage = assess.MarksPercentage;
                        existing.SelectedLearningOutcomeIds = selectedLOIds;
                        existing.LOsLockedByOutline = true;
                    }
                    else
                    {
                        // Create new
                        _context.Assignments.Add(new Assignment
                        {
                            AssessmentName = assess.Title,
                            CourseCode = courseCode,
                            CourseTitle = courseTitle,
                            Year = year,
                            Trimester = trimester,
                            MarksPercentage = assess.MarksPercentage,
                            SelectedLearningOutcomeIds = selectedLOIds,
                            LOsLockedByOutline = true
                        });
                    }
                }

                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error syncing assessments to database: {ex.Message}");
            }
        }
    }
}