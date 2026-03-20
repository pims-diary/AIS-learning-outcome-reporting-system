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
            // Load all courses from DB
            var allCourses = await _context.Courses
                .OrderByDescending(c => c.Year)
                .ThenBy(c => c.Trimester)
                .ThenBy(c => c.Code)
                .ToListAsync();

            // Build year/trimester structure — always show T1, T2, T3 for every year
            var years = allCourses.Select(c => c.Year).Distinct().OrderByDescending(y => y).ToList();
            var trimestersByYear = years.ToDictionary(y => y, y => new List<int> { 1, 2, 3 });

            // Fallback to current date if no data
            if (!years.Any())
            {
                years.Add(DateTime.Now.Year);
                trimestersByYear[DateTime.Now.Year] = new List<int> { GetTrimesterFromMonth(DateTime.Now.Month) };
            }

            var selectedYear = year ?? years.First();
            if (!years.Contains(selectedYear)) selectedYear = years.First();

            var availableTris = trimestersByYear.ContainsKey(selectedYear)
                ? trimestersByYear[selectedYear]
                : new List<int> { 1 };

            var selectedTrimester = trimester ?? availableTris.First();
            if (!availableTris.Contains(selectedTrimester)) selectedTrimester = availableTris.First();

            var courses = allCourses
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