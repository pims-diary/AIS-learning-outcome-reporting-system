using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LOARS.Web.Models.Dashboard;

namespace AIS_LO_System.ViewComponents
{
    public class SidebarViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public SidebarViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync(int? year, int? trimester)
        {
            int defaultYear = DateTime.Now.Year;
            int defaultTrimester = GetTrimesterFromMonth(DateTime.Now.Month);

            // Get logged-in user's ID
            var userIdClaim = HttpContext.User.FindFirst("UserId")?.Value;
            int.TryParse(userIdClaim, out int userId);

            List<Course> allCourses = new();

            if (userId > 0)
            {
                // Courses via enrolment table
                var enrolCourses = await _context.LecturerCourseEnrolments
                    .Where(e => e.UserId == userId)
                    .Include(e => e.Course)
                    .Select(e => e.Course)
                    .ToListAsync();

                // Courses via primary lecturer/moderator FK
                var primaryCourses = await _context.Courses
                    .Where(c => c.LecturerId == userId || c.ModeratorId == userId)
                    .ToListAsync();

                allCourses = enrolCourses.Union(primaryCourses)
                    .OrderByDescending(c => c.Year)
                    .ThenBy(c => c.Trimester)
                    .ThenBy(c => c.Code)
                    .ToList();
            }

            var years = allCourses.Select(c => c.Year).Distinct().OrderByDescending(y => y).ToList();
            var trimestersByYear = years.ToDictionary(
                y => y,
                y => allCourses.Where(c => c.Year == y).Select(c => c.Trimester).Distinct().OrderBy(t => t).ToList()
            );

            // Fallback if no courses
            if (!years.Any())
            {
                years.Add(defaultYear);
                trimestersByYear[defaultYear] = new List<int> { defaultTrimester };
            }

            var selectedYear = year ?? (years.Contains(defaultYear) ? defaultYear : years.First());
            if (!years.Contains(selectedYear)) selectedYear = years.First();

            var availableTris = trimestersByYear.ContainsKey(selectedYear)
                ? trimestersByYear[selectedYear]
                : new List<int> { 1 };

            var selectedTrimester = trimester
                ?? (availableTris.Contains(defaultTrimester) ? defaultTrimester : availableTris.First());
            if (!availableTris.Contains(selectedTrimester)) selectedTrimester = availableTris.First();

            var vm = new LecturerDashboardViewModel
            {
                SelectedYear = selectedYear,
                SelectedTrimester = selectedTrimester,
                Years = years,
                TrimestersByYear = trimestersByYear
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