using AIS_LO_System.Models;
using AIS_LO_System.Data;
using AIS_LO_System.Services;
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
            if (string.IsNullOrEmpty(assessmentName))
            {
                TempData["Error"] = "Assessment name is required.";
                return RedirectToAction("Index", "CourseDashboard", new { courseCode, year, trimester });
            }

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

            ViewBag.AssessmentName = assignment.AssessmentName;
            ViewBag.CourseCode = assignment.CourseCode;
            ViewBag.CourseTitle = assignment.CourseTitle;
            ViewBag.Year = assignment.Year;
            ViewBag.Trimester = assignment.Trimester;

            return View(assignment);
        }

        [HttpGet]
        public IActionResult Information(int id)
        {
            var assignment = _context.Assignments
                .Include(a => a.Files)
                .FirstOrDefault(a => a.Id == id);

            if (assignment == null) return NotFound();

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

            if (!DocumentService.IsAllowed(file.FileName))
            {
                TempData["Error"] = "Only PDF and Word documents (.docx) are allowed.";
                return RedirectToAction("Information", new { id = assignmentId });
            }

            var assignment = _context.Assignments.FirstOrDefault(a => a.Id == assignmentId);
            if (assignment == null) return NotFound();

            var latestVersion = _context.AssignmentFiles
                .Where(f => f.AssignmentId == assignment.Id)
                .OrderByDescending(f => f.VersionNumber)
                .FirstOrDefault()?.VersionNumber ?? 0;

            var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "assignments");
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var storedName = Guid.NewGuid() + ext;
            var savedPath = Path.Combine(folder, storedName);

            using (var stream = new FileStream(savedPath, FileMode.Create))
                await file.CopyToAsync(stream);

            // If Word doc — convert to PDF so it can be previewed in browser
            string pdfStoredName = storedName;
            if (DocumentService.IsWordDocument(file.FileName))
            {
                var pdfPath = DocumentService.ConvertDocxToPdf(savedPath);
                if (!string.IsNullOrEmpty(pdfPath))
                    pdfStoredName = Path.GetFileName(pdfPath);
            }

            _context.AssignmentFiles.Add(new AssignmentFile
            {
                AssignmentId = assignment.Id,
                OriginalFileName = file.FileName,
                StoredFileName = storedName,
                FilePath = "/uploads/assignments/" + pdfStoredName, // serve the PDF version
                VersionNumber = latestVersion + 1,
                UploadDate = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = DocumentService.IsWordDocument(file.FileName)
                ? "Word document uploaded and converted to PDF successfully."
                : "PDF uploaded successfully.";

            return RedirectToAction("Information", new { id = assignment.Id });
        }
    }
}