using AIS_LO_System.Models;
using AIS_LO_System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIS_LO_System.Controllers
{
    public class AssignmentController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AssignmentController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index(
    string assessmentName,
    string courseCode,
    string courseTitle,
    int year,
    int trimester)
        {
            var assignment = _context.Assignments.FirstOrDefault(a =>
                a.AssessmentName == assessmentName &&
                a.CourseCode == courseCode &&
                a.Year == year &&
                a.Trimester == trimester);

            if (assignment == null)
            {
                assignment = new Assignment
                {
                    AssessmentName = assessmentName,
                    CourseCode = courseCode,
                    CourseTitle = courseTitle,
                    Year = year,
                    Trimester = trimester
                };

                _context.Assignments.Add(assignment);
                _context.SaveChanges();
            }

            return View(assignment);
        }

        private string GetTrimesterDateRange(int year, int trimester)
        {
            return trimester switch
            {
                1 => $"February – April {year}",
                2 => $"July – September {year}",
                3 => $"November – February {year + 1}",
                _ => year.ToString()
            };
        }

        [HttpGet]
        public IActionResult Information(int id)
        {
            var assignment = _context.Assignments
                .Include(a => a.Files)
                .FirstOrDefault(a => a.Id == id);

            if (assignment == null)
                return NotFound();

            // Pass ViewBag data for breadcrumb
            ViewBag.CourseCode = assignment.CourseCode;
            ViewBag.CourseTitle = assignment.CourseTitle;
            ViewBag.Year = assignment.Year;
            ViewBag.Trimester = assignment.Trimester;
            ViewBag.AssessmentName = assignment.AssessmentName;

            return View(assignment);
        }

        [HttpPost]
        public async Task<IActionResult> UploadAssignment(IFormFile file, int assignmentId)
        {
            if (file == null || file.Length == 0)
                return RedirectToAction("Information", new { id = assignmentId });

            // Only PDF
            if (Path.GetExtension(file.FileName).ToLower() != ".pdf")
            {
                TempData["Error"] = "Only PDF files are allowed.";
                return RedirectToAction("Information", new { id = assignmentId });
            }

            var assignment = _context.Assignments
                .FirstOrDefault(a => a.Id == assignmentId);

            if (assignment == null)
                return NotFound();

            var latestVersion = _context.AssignmentFiles
                .Where(f => f.AssignmentId == assignment.Id)
                .OrderByDescending(f => f.VersionNumber)
                .FirstOrDefault()?.VersionNumber ?? 0;

            var folder = Path.Combine(Directory.GetCurrentDirectory(),
                                      "wwwroot", "uploads", "assignments");

            Directory.CreateDirectory(folder);

            var storedName = Guid.NewGuid() + ".pdf";
            var path = Path.Combine(folder, storedName);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            _context.AssignmentFiles.Add(new AssignmentFile
            {
                AssignmentId = assignment.Id,
                OriginalFileName = file.FileName,
                StoredFileName = storedName,
                FilePath = "/uploads/assignments/" + storedName,
                VersionNumber = latestVersion + 1,
                UploadDate = DateTime.Now
            });

            await _context.SaveChangesAsync();

            return RedirectToAction("Information", new { id = assignment.Id });
        }
    }
}