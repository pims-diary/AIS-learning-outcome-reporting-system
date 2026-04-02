using AIS_LO_System.Data;
using LOARS.Web.Models.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class LecturerDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public LecturerDashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? year, int? trimester)
        {
            // Get logged-in user's ID from claims
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return RedirectToAction("Login", "Account");

            // Only load courses this lecturer is assigned to
            var allCourses = await _context.LecturerCourseEnrolments
                .Where(e => e.UserId == userId)
                .Include(e => e.Course)
                .Select(e => e.Course)
                .OrderByDescending(c => c.Year)
                .ThenBy(c => c.Trimester)
                .ThenBy(c => c.Code)
                .ToListAsync();

            // Also include courses where they are the primary lecturer/moderator
            // (in case assigned via Course.LecturerId rather than enrolment table)
            var primaryCourses = await _context.Courses
                .Where(c => c.LecturerId == userId || c.ModeratorId == userId)
                .ToListAsync();

            // Merge and deduplicate
            var mergedCourses = allCourses
                .Union(primaryCourses)
                .OrderByDescending(c => c.Year)
                .ThenBy(c => c.Trimester)
                .ThenBy(c => c.Code)
                .ToList();

            // Build year/trimester options from the lecturer's own courses only
            var years = mergedCourses.Select(c => c.Year).Distinct().OrderByDescending(y => y).ToList();
            var trimestersByYear = years.ToDictionary(
                y => y,
                y => mergedCourses.Where(c => c.Year == y).Select(c => c.Trimester).Distinct().OrderBy(t => t).ToList()
            );

            // Fallback if lecturer has no courses yet
            if (!years.Any())
            {
                int currentYr = DateTime.Now.Year;
                int currentTri = GetTrimesterFromMonth(DateTime.Now.Month);
                years.Add(currentYr);
                trimestersByYear[currentYr] = new List<int> { currentTri };
            }

            // Default to current year/trimester if available, otherwise first available
            int nowYear = DateTime.Now.Year;
            int nowTri = GetTrimesterFromMonth(DateTime.Now.Month);

            var selectedYear = year ?? (years.Contains(nowYear) ? nowYear : years.First());
            if (!years.Contains(selectedYear)) selectedYear = years.First();

            var availableTris = trimestersByYear.ContainsKey(selectedYear)
                ? trimestersByYear[selectedYear]
                : new List<int> { 1 };

            var selectedTrimester = trimester
                ?? (availableTris.Contains(nowTri) ? nowTri : availableTris.First());
            if (!availableTris.Contains(selectedTrimester)) selectedTrimester = availableTris.First();

            var courses = mergedCourses
                .Where(c => c.Year == selectedYear && c.Trimester == selectedTrimester)
                .Select(c => new CourseCard
                {
                    Code = c.Code,
                    Title = c.Title,
                    School = c.School
                })
                .ToList();

            var vm = new LecturerDashboardViewModel
            {
                SelectedYear = selectedYear,
                SelectedTrimester = selectedTrimester,
                Years = years,
                TrimestersByYear = trimestersByYear,
                Courses = courses
            };

            return View(vm);
        }

        private static int GetTrimesterFromMonth(int month)
        {
            if (month >= 1 && month <= 4) return 1;
            if (month >= 5 && month <= 8) return 2;
            return 3;
        }
    }
}