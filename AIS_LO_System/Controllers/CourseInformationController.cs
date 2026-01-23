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

            // redirect back to Outline -> it will now show iframe preview
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

            // Provide BOTH names so your cshtml won't crash no matter what it expects
            ViewBag.LearningOutcomes = los;
            ViewBag.LOs = los;

            ViewBag.EditMode = false;
            return View();
        }

        // If your cshtml links to EditLearningOutcomes, this prevents “local error”
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

        private List<string> DefaultLos() => new()
        {
            "Analyse client requirements using current analysis techniques.",
            "Identify and relate whenever required appropriate project control techniques in an industry environment.",
            "Produce a comprehensive project plan for an industrial IT project; apply project principles and task management, resource management, risk management, project tracking, and project tools in industry environment.",
            "Implement the industrial IT project following the appropriate project management framework and System Development Life Cycle.",
            "Produce all relevant documentation.",
            "Develop both IT and workplace soft-skills, including working in groups, writing formal reports, carrying out individual research and/or delivering oral presentations."
        };

        private List<string> LoadLos(string courseCode, int year, int trimester)
        {
            var path = GetLosPath(courseCode, year, trimester);
            if (!System.IO.File.Exists(path)) return DefaultLos();

            try
            {
                var json = System.IO.File.ReadAllText(path);
                var los = JsonSerializer.Deserialize<List<string>>(json);
                return (los != null && los.Count > 0) ? los : DefaultLos();
            }
            catch
            {
                return DefaultLos();
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
