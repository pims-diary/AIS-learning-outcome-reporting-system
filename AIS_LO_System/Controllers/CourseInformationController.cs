using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LOARS.Web.Services;
using System.Text.Json;
using System.Linq;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class CourseInformationController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public CourseInformationController(IWebHostEnvironment env)
        {
            _env = env;
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

            SaveLos(courseCode, year, trimester, cleaned);

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

        // ✅ FIXED: No more DefaultLos() - returns empty list instead!
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
    }
}
