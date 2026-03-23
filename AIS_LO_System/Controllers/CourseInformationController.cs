using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LOARS.Web.Services;
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

        private bool AllowOutlineReupload(string courseCode) => true;
        private bool AllowLOEdit(string courseCode) => true;

        // -------------------------
        // COURSE OUTLINE (VIEW)
        // -------------------------
        [HttpGet]
        public IActionResult Outline(string courseCode, int year, int trimester)
        {
            SetCourseContext(courseCode, year, trimester);

            ViewBag.CanReupload = AllowOutlineReupload(courseCode);

            var fileName = GetOutlineFileName(courseCode, year, trimester);
            var physicalPath = Path.Combine(_env.WebRootPath, "uploads", "outlines", fileName);

            ViewBag.OutlineUrl = System.IO.File.Exists(physicalPath)
                ? $"/uploads/outlines/{fileName}"
                : null;

            return View();
        }

        // -------------------------
        // COURSE OUTLINE (UPLOAD) - ✅ UPDATED WITH PDF EXTRACTION
        // -------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadOutline(IFormFile file, string courseCode, int year, int trimester)
        {
            if (!AllowOutlineReupload(courseCode))
                return Forbid();

            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please choose a PDF file.";
                return RedirectToAction(nameof(Outline), new { courseCode, year, trimester });
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".pdf")
            {
                TempData["Error"] = "Only PDF files are supported.";
                return RedirectToAction(nameof(Outline), new { courseCode, year, trimester });
            }

            var dir = Path.Combine(_env.WebRootPath, "uploads", "outlines");
            Directory.CreateDirectory(dir);

            var fileName = GetOutlineFileName(courseCode, year, trimester);
            var savePath = Path.Combine(dir, fileName);

            // Save the PDF file
            using (var stream = System.IO.File.Create(savePath))
            {
                await file.CopyToAsync(stream);
            }

            // ✅ NEW: Extract Learning Outcomes from PDF
            try
            {
                var extractedLOs = ExtractLearningOutcomesFromPDF(savePath);

                if (extractedLOs.Any())
                {
                    // Save to JSON (existing functionality)
                    SaveLos(courseCode, year, trimester, extractedLOs);

                    // Sync to database
                    SyncLosToDatabase(courseCode, extractedLOs);

                    TempData["Success"] = $"Course outline uploaded successfully! {extractedLOs.Count} Learning Outcomes auto-extracted.";
                }
                else
                {
                    TempData["Success"] = "Course outline uploaded successfully! (No Learning Outcomes found in PDF)";
                }
            }
            catch (Exception ex)
            {
                // PDF uploaded but extraction failed - that's OK
                TempData["Success"] = "Course outline uploaded successfully! (Could not auto-extract Learning Outcomes)";
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

            ViewBag.CanEditLO = AllowLOEdit(courseCode);

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

            ViewBag.CanEditLO = AllowLOEdit(courseCode);

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
            if (!AllowLOEdit(courseCode))
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

            var course = FakeTeachingData.GetCourses(year, trimester)
                .FirstOrDefault(c => c.Code == courseCode);

            ViewBag.CourseTitle = course?.Title ?? "";
        }

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
    }
}
