using LOARS.Web.Models.Dashboard;
using LOARS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class LecturerDashboardController : Controller
    {
        [HttpGet]
        public IActionResult Index(int? year, int? trimester)
        {
            // Default context: current year + calculated trimester
            var defaultYear = DateTime.Now.Year;
            var defaultTrimester = GetTrimesterFromMonth(DateTime.Now.Month);

            var selectedYear = year ?? defaultYear;
            var selectedTrimester = trimester ?? defaultTrimester;

            // If selected year not available in data, fallback to newest year
            var years = FakeTeachingData.GetYears();
            if (!years.Contains(selectedYear))
                selectedYear = years.First();

            // If selected trimester not available, fallback to first available trimester
            var trimestersByYear = FakeTeachingData.GetTrimestersByYear();
            var availableTris = trimestersByYear[selectedYear];
            if (!availableTris.Contains(selectedTrimester))
                selectedTrimester = availableTris.First();

            var vm = new LecturerDashboardViewModel
            {
                SelectedYear = selectedYear,
                SelectedTrimester = selectedTrimester,
                Years = years,
                TrimestersByYear = trimestersByYear,
                Courses = FakeTeachingData.GetCourses(selectedYear, selectedTrimester)
            };

            return View(vm);
        }

        private static int GetTrimesterFromMonth(int month)
        {
            // Simple assumption:
            // Tri 1: Jan-Apr, Tri 2: May-Aug, Tri 3: Sep-Dec
            if (month >= 1 && month <= 4) return 1;
            if (month >= 5 && month <= 8) return 2;
            return 3;
        }
    }
}
