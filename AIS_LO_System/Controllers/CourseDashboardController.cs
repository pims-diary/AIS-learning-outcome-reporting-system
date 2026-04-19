using AIS_LO_System.Data;
using AIS_LO_System.Models;
using AIS_LO_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer,Admin")]
    public class CourseDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SubmissionService _submissions;

        public CourseDashboardController(ApplicationDbContext context, SubmissionService submissions)
        {
            _context = context;
            _submissions = submissions;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string courseCode, int year, int trimester)
        {
            var course = await _context.Courses
                .FirstOrDefaultAsync(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);

            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = course?.Title ?? "";
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;
            ViewBag.DateRange = GetTrimesterDateRange(year, trimester);

            // Load assessments from database (extracted from course outline)
            var assignments = await _context.Assignments
                .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                .OrderBy(a => a.Id)
                .ToListAsync();

            var rubricAssignmentIds = await _context.Rubrics
                .Where(r => assignments.Select(a => a.Id).Contains(r.AssignmentId))
                .Select(r => r.AssignmentId)
                .ToListAsync();

            var assignmentIdsWithFiles = await _context.AssignmentFiles
                .Where(f => assignments.Select(a => a.Id).Contains(f.AssignmentId))
                .Select(f => f.AssignmentId)
                .Distinct()
                .ToListAsync();

            var markedAssessmentNames = await _context.StudentAssessmentMarks
                .Where(m => m.CourseCode == courseCode)
                .Select(m => m.AssessmentName)
                .Distinct()
                .ToListAsync();

            var assessments = assignments
                .GroupBy(a => NormalizeAssessmentTitle(a.AssessmentName))
                .Select(group => group
                    .OrderByDescending(a => rubricAssignmentIds.Contains(a.Id))
                    .ThenByDescending(a => assignmentIdsWithFiles.Contains(a.Id))
                    .ThenByDescending(a => markedAssessmentNames.Contains(a.AssessmentName))
                    .ThenByDescending(a => a.AssessmentName?.Length ?? 0)
                    .ThenBy(a => a.Id)
                    .First())
                .OrderBy(a => a.Id)
                .ToList();

            var assessmentTotal = assessments.Sum(a => a.MarksPercentage);
            ViewBag.AssessmentTotalWarning = assessments.Any() && assessmentTotal != 100
                ? $"Assessment totals currently add up to {assessmentTotal}%, not 100%. Please review the extracted assessments."
                : null;
            var latestAssessmentSubmission = await _submissions.GetLatestAsync(
                courseCode,
                year,
                trimester,
                SubmissionItemType.Assessments);
            ViewBag.PendingAssessmentDraft = latestAssessmentSubmission?.Status == SubmissionStatus.Pending;
            ViewBag.PendingAssessmentDraftWarning = latestAssessmentSubmission?.Status == SubmissionStatus.Pending
                ? "Assessment changes are waiting for moderator approval. The live assessment links below are temporarily locked until that review is complete."
                : null;

            ViewBag.Assessments = assessments;

            // Check if assessment editing is allowed (admin-controlled)
            ViewBag.CanEditAssignment = course?.CanEditAssignment ?? false;

            return View();
        }

        private static string NormalizeAssessmentTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var normalized = title.ToLowerInvariant();
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\bassign(?:ment)?\b", "assignment");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\bassessment\b", "assignment");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\bmid\s+term\b", "midterm");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-z0-9]+", " ");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
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
    }
}
