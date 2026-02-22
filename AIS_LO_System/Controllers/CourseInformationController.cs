using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LOARS.Web.Services;
using System.Text.Json;
using System.Linq;
using AIS_LO_System.Data; // ✅ NEW: Add database context
using AIS_LO_System.Models; // ✅ NEW: Add models

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class CourseInformationController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _context; // ✅ NEW: Database access

        // ✅ UPDATED: Inject database context
        public CourseInformationController(IWebHostEnvironment env, ApplicationDbContext context)
        {
            _env = env;
            _context = context;
        }

        // For now (testing stage): allow Lecturer to test everything.
        // Later: replace with admin/moderator + DB toggle.
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

            // Determine if an outline PDF exists already
            var fileName = GetOutlineFileName(courseCode, year, trimester);
            var physicalPath = Path.Combine(_env.WebRootPath, "uploads", "outlines", fileName);

            ViewBag.OutlineUrl = System.IO.File.Exists(physicalPath)
                ? $"/uploads/outlines/{fileName}"
                : null;

            return View();
        }

        // -------------------------
        // COURSE OUTLINE (UPLOAD)
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

            using var stream = System.IO.File.Create(savePath);
            await file.CopyToAsync(stream);

            TempData["Success"] = "Course outline uploaded successfully!";
            return RedirectToAction(nameof(Outline), new { courseCode, year, trimester });
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

            // ✅ Save to JSON (existing functionality)
            SaveLos(courseCode, year, trimester, cleaned);

            // ✅ NEW: Save to DATABASE so Epic 8 can use them!
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

        // Store LOs in a json file per course/year/tri (temporary until DB)
        private string GetLosPath(string courseCode, int year, int trimester)
        {
            var dir = Path.Combine(_env.ContentRootPath, "App_Data", "learning-outcomes");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{courseCode}-{year}-T{trimester}.json");
        }

        private List<string> LoadLos(string courseCode, int year, int trimester)
        {
            var path = GetLosPath(courseCode, year, trimester);

            // If file doesn't exist, return EMPTY LIST (not defaults)
            if (!System.IO.File.Exists(path))
                return new List<string>();

            try
            {
                var json = System.IO.File.ReadAllText(path);
                var los = JsonSerializer.Deserialize<List<string>>(json);

                // If deserialization fails or list is null, return EMPTY
                return los ?? new List<string>();
            }
            catch
            {
                // If there's an error reading, return EMPTY LIST
                return new List<string>();
            }
        }

        private void SaveLos(string courseCode, int year, int trimester, List<string> los)
        {
            var path = GetLosPath(courseCode, year, trimester);
            var json = JsonSerializer.Serialize(los, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(path, json);
        }

        // ✅ NEW METHOD: Sync LOs to database for Epic 8
        private void SyncLosToDatabase(string courseCode, List<string> loTexts)
        {
            try
            {
                // Remove all existing LOs for this course
                var existingLOs = _context.LearningOutcomes
                    .Where(lo => lo.CourseCode == courseCode)
                    .ToList();

                _context.LearningOutcomes.RemoveRange(existingLOs);

                // Add new LOs
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
                // Log error but don't break the flow
                // JSON is still saved, database sync can be retried
                Console.WriteLine($"Error syncing LOs to database: {ex.Message}");
            }
        }
    }
}
