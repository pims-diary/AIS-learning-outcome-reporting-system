using AIS_LO_System.Data;
using LOARS.Web.Models.Dashboard;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
            var allCourses = await _context.Courses
                .OrderByDescending(c => c.Year)
                .ThenBy(c => c.Trimester)
                .ToListAsync();

            // Get all years that have at least one course
            var years = allCourses.Select(c => c.Year).Distinct().OrderByDescending(y => y).ToList();

            // Always show all 3 trimesters for every year (even if some are empty)
            var trimestersByYear = years.ToDictionary(y => y, y => new List<int> { 1, 2, 3 });

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