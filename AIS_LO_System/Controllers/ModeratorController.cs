using AIS_LO_System.Data;
using AIS_LO_System.Models;
using AIS_LO_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AIS_LO_System.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class ModeratorController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SubmissionService _submissions;
        private readonly IWebHostEnvironment _env;

        public ModeratorController(ApplicationDbContext context, SubmissionService submissions, IWebHostEnvironment env)
        {
            _context = context;
            _submissions = submissions;
            _env = env;
        }

        // ─────────────────────────────────────────────────
        // Helper: get current user ID, verify they are moderator of the course
        // ─────────────────────────────────────────────────
        private int GetUserId()
        {
            int.TryParse(User.FindFirst("UserId")?.Value, out int id);
            return id;
        }

        private async Task<bool> IsModerator(string courseCode, int year, int trimester)
        {
            var userId = GetUserId();
            var course = await _context.Courses.FirstOrDefaultAsync(c =>
                c.Code == courseCode && c.Year == year && c.Trimester == trimester);
            return course?.ModeratorId == userId;
        }

        // ─────────────────────────────────────────────────
        // INBOX — all pending submissions across all moderated courses
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Inbox()
        {
            var userId = GetUserId();

            var moderatedCourses = await _context.Courses
                .Where(c => c.ModeratorId == userId)
                .ToListAsync();

            ViewBag.ModeratedCourses = moderatedCourses;

            var pending = await _submissions.GetPendingForModeratorAsync(userId);
            var all = await _submissions.GetAllForModeratorAsync(userId);

            ViewBag.PendingCount = pending.Count;
            ViewBag.AllSubmissions = all;

            return View(pending);
        }

        // ─────────────────────────────────────────────────
        // REVIEW — approve or deny a submission
        // ─────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Review(int submissionId, string decision, string? comment)
        {
            var submission = await _context.CourseSubmissions.FindAsync(submissionId);
            if (submission == null) return NotFound();

            // Verify moderator owns this course
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester))
                return Forbid();

            var userId = GetUserId();
            submission.Status = decision == "approve" ? SubmissionStatus.Approved : SubmissionStatus.Denied;
            submission.ModeratorComment = comment?.Trim();
            submission.ReviewedAt = DateTime.Now;
            submission.ReviewedByUserId = userId;

            await _context.SaveChangesAsync();

            TempData["Success"] = decision == "approve"
                ? $"✅ Approved: {submission.ItemLabel}"
                : $"❌ Denied: {submission.ItemLabel}";

            return RedirectToAction(nameof(Inbox));
        }

        // ─────────────────────────────────────────────────
        // VIEW COURSE OUTLINE (read-only for moderator)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewOutline(string courseCode, int year, int trimester, int submissionId)
        {
            if (!await IsModerator(courseCode, year, trimester)) return Forbid();

            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            ViewBag.CourseCode = courseCode;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;
            ViewBag.Submission = submission;

            var baseName = $"{courseCode}-{year}-T{trimester}";
            var dir = Path.Combine(_env.WebRootPath, "uploads", "outlines");
            string? outlineUrl = null;
            foreach (var ext in new[] { ".pdf", ".docx" })
            {
                if (System.IO.File.Exists(Path.Combine(dir, baseName + ext)))
                {
                    outlineUrl = $"/uploads/outlines/{baseName}{ext}";
                    break;
                }
            }
            ViewBag.OutlineUrl = outlineUrl;

            return View();
        }

        // ─────────────────────────────────────────────────
        // VIEW ASSIGNMENT DOCUMENT (read-only for moderator)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewAssignment(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var assignment = await _context.Assignments
                .Include(a => a.Files)
                .FirstOrDefaultAsync(a => a.Id == submission.ItemRefId);

            if (assignment == null) return NotFound();

            ViewBag.Submission = submission;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;

            return View(assignment);
        }

        // ─────────────────────────────────────────────────
        // VIEW RUBRIC (read-only for moderator)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewRubric(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.Levels)
                .Include(r => r.Assignment)
                .FirstOrDefaultAsync(r => r.Id == submission.ItemRefId);

            if (rubric != null)
                foreach (var criterion in rubric.Criteria)
                    criterion.Levels = criterion.Levels.OrderByDescending(l => l.Score).ToList();

            ViewBag.Submission = submission;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;
            ViewBag.AssessmentName = rubric?.Assignment?.AssessmentName ?? submission.ItemLabel;

            return View(rubric);
        }

        // ─────────────────────────────────────────────────
        // VIEW STUDENT MARKS (read-only for moderator)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewMarks(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == submission.ItemRefId);

            if (assignment == null) return NotFound();

            var course = await _context.Courses.FirstOrDefaultAsync(c =>
                c.Code == submission.CourseCode && c.Year == submission.Year && c.Trimester == submission.Trimester);

            var marks = await _context.StudentAssessmentMarks
                .Include(m => m.Student)
                .Where(m => m.CourseCode == submission.CourseCode && m.AssessmentName == assignment.AssessmentName)
                .ToListAsync();

            var criterionMarks = await _context.StudentCriterionMarks
                .Where(m => m.AssignmentId == assignment.Id)
                .ToListAsync();

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria).ThenInclude(c => c.Levels)
                .FirstOrDefaultAsync(r => r.AssignmentId == assignment.Id);

            ViewBag.Submission = submission;
            ViewBag.Assignment = assignment;
            ViewBag.Marks = marks;
            ViewBag.CriterionMarks = criterionMarks;
            ViewBag.Rubric = rubric;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;

            return View();
        }

        // ─────────────────────────────────────────────────
        // VIEW STUDENT LO REPORT (read-only for moderator)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewStudentLOReport(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            ViewBag.Submission = submission;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.CourseTitle = (await _context.Courses.FirstOrDefaultAsync(c =>
                c.Code == submission.CourseCode && c.Year == submission.Year && c.Trimester == submission.Trimester))?.Title ?? "";
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;

            return View();
        }
    }
}