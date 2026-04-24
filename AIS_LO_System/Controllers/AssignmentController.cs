using AIS_LO_System.Models;
using AIS_LO_System.Data;
using AIS_LO_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace AIS_LO_System.Controllers
{
    [Authorize(Roles = "Lecturer,Admin")]
    public class AssignmentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SubmissionService _submissions;

        public AssignmentController(ApplicationDbContext context, SubmissionService submissions)
        {
            _context = context;
            _submissions = submissions;
        }

        private int GetUserId()
        {
            int.TryParse(User.FindFirst("UserId")?.Value, out int id);
            return id;
        }

        private async Task<bool> IsLecturerForCourse(string courseCode, int year, int trimester)
        {
            if (User.IsInRole("Admin")) return true;
            var userId = GetUserId();
            var course = await _context.Courses.FirstOrDefaultAsync(c =>
                c.Code == courseCode && c.Year == year && c.Trimester == trimester);
            if (course == null) return false;
            if (course.LecturerId == userId || course.ModeratorId == userId) return true;
            return await _context.LecturerCourseEnrolments
                .AnyAsync(e => e.UserId == userId && e.CourseId == course.Id);
        }

        [HttpGet]
        public async Task<IActionResult> Index(
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

            if (!await IsLecturerForCourse(courseCode, year, trimester))
                return Forbid();

            var assessmentDraftLock = await BlockIfAssessmentDraftPendingAsync(courseCode, year, trimester);
            if (assessmentDraftLock != null)
                return assessmentDraftLock;

            var normalizedAssessmentName = NormalizeAssessmentTitle(assessmentName);

            var assignment = _context.Assignments.FirstOrDefault(a =>
                a.AssessmentName == assessmentName &&
                a.CourseCode == courseCode &&
                a.Year == year &&
                a.Trimester == trimester);

            if (assignment == null)
            {
                assignment = _context.Assignments
                    .AsEnumerable()
                    .FirstOrDefault(a =>
                        a.CourseCode == courseCode &&
                        a.Year == year &&
                        a.Trimester == trimester &&
                        NormalizeAssessmentTitle(a.AssessmentName) == normalizedAssessmentName);
            }

            if (assignment == null)
            {
                var hasAnyOutlineAssignments = _context.Assignments.Any(a =>
                    a.CourseCode == courseCode &&
                    a.Year == year &&
                    a.Trimester == trimester);

                TempData["Error"] = hasAnyOutlineAssignments
                    ? "This assessment link no longer matches an existing assessment. Please review the Assessments list or re-upload the course outline."
                    : "No assessments are available for this course yet. Please upload the course outline first.";

                return RedirectToAction("Assessments", "CourseInformation", new { courseCode, year, trimester });
            }

            ViewBag.AssessmentName = assignment.AssessmentName;
            ViewBag.CourseCode = assignment.CourseCode;
            ViewBag.CourseTitle = assignment.CourseTitle;
            ViewBag.Year = assignment.Year;
            ViewBag.Trimester = assignment.Trimester;

            return View(assignment);
        }

        [HttpGet]
        public async Task<IActionResult> Information(int id)
        {
            var assignment = _context.Assignments
                .Include(a => a.Files)
                .FirstOrDefault(a => a.Id == id);

            if (assignment == null) return NotFound();

            if (!await IsLecturerForCourse(assignment.CourseCode, assignment.Year, assignment.Trimester))
                return Forbid();

            var assessmentDraftLock = await BlockIfAssessmentDraftPendingAsync(
                assignment.CourseCode,
                assignment.Year,
                assignment.Trimester);
            if (assessmentDraftLock != null)
                return assessmentDraftLock;

            ViewBag.CourseCode = assignment.CourseCode;
            ViewBag.CourseTitle = assignment.CourseTitle;
            ViewBag.Year = assignment.Year;
            ViewBag.Trimester = assignment.Trimester;
            ViewBag.AssessmentName = assignment.AssessmentName;

            // Submission status for this assignment document
            ViewBag.Submission = await _submissions.GetLatestAsync(
                assignment.CourseCode, assignment.Year, assignment.Trimester,
                SubmissionItemType.AssignmentDocument, assignment.Id);

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
                        if (TryBuildCrossCheck(
                            assignment,
                            filePath,
                            out var crossCheck,
                            out var assignmentLOsFound,
                            out var outlineLOsFound))
                        {
                            ViewBag.CrossCheck = crossCheck;
                        }

                        ViewBag.AssignmentLOsFound = assignmentLOsFound;
                        ViewBag.OutlineLOsFound = outlineLOsFound;
                    }
                }
                catch { }
            }

            return View(assignment);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
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

            if (!await IsLecturerForCourse(assignment.CourseCode, assignment.Year, assignment.Trimester))
                return Forbid();

            var assessmentDraftLock = await BlockIfAssessmentDraftPendingAsync(
                assignment.CourseCode,
                assignment.Year,
                assignment.Trimester);
            if (assessmentDraftLock != null)
                return assessmentDraftLock;

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

            var uploadMessage = "Assignment document uploaded successfully.";

            try
            {
                if (TryBuildCrossCheck(
                    assignment,
                    savedPath,
                    out var crossCheck,
                    out var assignmentLOsFound,
                    out var outlineLOsFound))
                {
                    var wordingDifferences = crossCheck.Matched.Count(m => m.Status == "Wording Differs");

                    if (crossCheck.IsFullyAligned)
                    {
                        TempData["Success"] = $"{uploadMessage} LO check: all detected LOs align with the course outline.";
                    }
                    else
                    {
                        var issues = new List<string>();
                        if (crossCheck.Missing.Any())
                            issues.Add($"{crossCheck.Missing.Count} missing");
                        if (crossCheck.Extra.Any())
                            issues.Add($"{crossCheck.Extra.Count} extra");
                        if (wordingDifferences > 0)
                            issues.Add($"{wordingDifferences} wording difference(s)");

                        TempData["Success"] = uploadMessage;
                        TempData["Error"] = $"LO check found issues: {string.Join(", ", issues)}. Review the LO Alignment Check below.";
                    }
                }
                else if (!outlineLOsFound)
                {
                    TempData["Success"] = uploadMessage;
                    TempData["Error"] = "Assignment uploaded, but no course outline Learning Outcomes were found for this course yet.";
                }
                else if (!assignmentLOsFound)
                {
                    TempData["Success"] = uploadMessage;
                    TempData["Error"] = "Assignment uploaded, but no Learning Outcomes were detected in this document.";
                }
                else
                {
                    TempData["Success"] = uploadMessage;
                }
            }
            catch
            {
                TempData["Success"] = uploadMessage;
            }

            // Auto-submit to moderator for approval
            int.TryParse(User.FindFirst("UserId")?.Value, out int userId);
            var course = _context.Courses.FirstOrDefault(c =>
                c.Code == assignment.CourseCode && c.Year == assignment.Year && c.Trimester == assignment.Trimester);
            if (course?.ModeratorId != null && userId > 0)
            {
                await _submissions.SubmitAsync(
                    assignment.CourseCode, assignment.Year, assignment.Trimester,
                    SubmissionItemType.AssignmentDocument, assignment.Id,
                    $"{assignment.AssessmentName} — Assignment Document",
                    userId);
                TempData["Info"] = "📨 Submitted to moderator for approval.";
            }

            return RedirectToAction("Information", new { id = assignment.Id });
        }

        private bool TryBuildCrossCheck(
            Assignment assignment,
            string filePath,
            out LOCrossCheckResult? crossCheck,
            out bool assignmentLOsFound,
            out bool outlineLOsFound)
        {
            crossCheck = null;
            assignmentLOsFound = false;
            outlineLOsFound = false;

            var assignmentLOs = DocumentService.ExtractLOsFromAssignmentDoc(filePath);
            assignmentLOsFound = assignmentLOs.Any();
            if (!assignmentLOsFound)
                return false;

            var outlineLOs = _context.LearningOutcomes
                .Where(lo => lo.CourseCode == assignment.CourseCode)
                .OrderBy(lo => lo.OrderNumber)
                .Select(lo => new { lo.OrderNumber, lo.LearningOutcomeText })
                .ToList()
                .Select(lo => (lo.OrderNumber, lo.LearningOutcomeText))
                .ToList();

            outlineLOsFound = outlineLOs.Any();
            if (!outlineLOsFound)
                return false;

            var allowedLOIds = new List<int>();
            if (!string.IsNullOrEmpty(assignment.SelectedLearningOutcomeIds))
            {
                allowedLOIds = assignment.SelectedLearningOutcomeIds
                    .Split(',')
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .ToList();
            }

            var allowedLONumbers = _context.LearningOutcomes
                .Where(lo => allowedLOIds.Contains(lo.Id))
                .Select(lo => lo.OrderNumber)
                .ToList();

            if (!allowedLONumbers.Any())
                allowedLONumbers = outlineLOs.Select(lo => lo.OrderNumber).ToList();

            crossCheck = DocumentService.CrossCheckLOs(assignmentLOs, outlineLOs, allowedLONumbers);
            return true;
        }

        private static string NormalizeAssessmentTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var normalized = title.ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"\bassign(?:ment)?\b", "assignment");
            normalized = Regex.Replace(normalized, @"\bassessment\b", "assignment");
            normalized = Regex.Replace(normalized, @"\bmid\s+term\b", "midterm");
            normalized = Regex.Replace(normalized, @"[^a-z0-9]+", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private async Task<IActionResult?> BlockIfAssessmentDraftPendingAsync(string courseCode, int year, int trimester)
        {
            var latestAssessmentSubmission = await _submissions.GetLatestAsync(
                courseCode,
                year,
                trimester,
                SubmissionItemType.Assessments);

            if (latestAssessmentSubmission?.Status != SubmissionStatus.Pending)
                return null;

            TempData["Error"] = "Assessment changes are waiting for moderator approval. Assignment pages are temporarily locked until the approved assessment setup goes live.";
            return RedirectToAction("Assessments", "CourseInformation", new { courseCode, year, trimester });
        }
    }
}