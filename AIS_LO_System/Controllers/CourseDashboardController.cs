using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer,Admin")]
    public class CourseDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CourseDashboardController(ApplicationDbContext context)
        {
            _context = context;
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

            var markedAssessmentNames = await _context.StudentAssessmentMarks
                .Where(m => m.CourseCode == courseCode)
                .Select(m => m.AssessmentName)
                .Distinct()
                .ToListAsync();

            var assessments = assignments
                .GroupBy(a => NormalizeAssessmentTitle(a.AssessmentName))
                .Select(group => group
                    .OrderByDescending(a => rubricAssignmentIds.Contains(a.Id))
                    .ThenByDescending(a => markedAssessmentNames.Contains(a.AssessmentName))
                    .ThenByDescending(a => a.AssessmentName?.Length ?? 0)
                    .ThenBy(a => a.Id)
                    .First())
                .OrderBy(a => a.Id)
                .ToList();

            ViewBag.Assessments = assessments;

            return View();
        }

        private static string NormalizeAssessmentTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var normalized = title.ToLowerInvariant();
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\bassign\b", "assignment");
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
