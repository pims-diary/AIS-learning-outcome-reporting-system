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

            // Cross-check LOs if there's an uploaded file
            var latest = assignment.Files?
                .OrderByDescending(f => f.VersionNumber)
                .FirstOrDefault();

            if (latest != null)
            {
                try
                {
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", latest.FilePath.TrimStart('/'));

                    if (System.IO.File.Exists(filePath))
                    {
                        // Extract LOs from the assignment document
                        var assignmentLOs = DocumentService.ExtractLOsFromAssignmentDoc(filePath);

                        if (assignmentLOs.Any())
                        {
                            // Get course outline LOs
                            var outlineLOs = _context.LearningOutcomes
                                .Where(lo => lo.CourseCode == assignment.CourseCode)
                                .OrderBy(lo => lo.OrderNumber)
                                .Select(lo => new { lo.OrderNumber, lo.LearningOutcomeText })
                                .ToList()
                                .Select(lo => (lo.OrderNumber, lo.LearningOutcomeText))
                                .ToList();

                            // Get allowed LO numbers for this assessment
                            var allowedLOIds = new List<int>();
                            if (!string.IsNullOrEmpty(assignment.SelectedLearningOutcomeIds))
                            {
                                allowedLOIds = assignment.SelectedLearningOutcomeIds
                                    .Split(',')
                                    .Where(s => int.TryParse(s, out _))
                                    .Select(int.Parse)
                                    .ToList();
                            }

                            // Convert IDs to order numbers
                            var allowedLONumbers = _context.LearningOutcomes
                                .Where(lo => allowedLOIds.Contains(lo.Id))
                                .Select(lo => lo.OrderNumber)
                                .ToList();

                            // If no specific LOs set, use all
                            if (!allowedLONumbers.Any())
                                allowedLONumbers = outlineLOs.Select(lo => lo.OrderNumber).ToList();

                            var crossCheck = DocumentService.CrossCheckLOs(assignmentLOs, outlineLOs, allowedLONumbers);
                            ViewBag.CrossCheck = crossCheck;
                            ViewBag.AssignmentLOsFound = true;
                        }
                        else
                        {
                            ViewBag.AssignmentLOsFound = false;
                        }
                    }
                }
                catch { }
            }

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

            TempData["Success"] = DocumentService.IsWordDocument(file.FileName)
                ? "Word document uploaded and converted to PDF successfully."
                : "PDF uploaded successfully.";

            return RedirectToAction("Information", new { id = assignment.Id });
        }
    }
}