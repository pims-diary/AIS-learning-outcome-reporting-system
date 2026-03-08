using Microsoft.AspNetCore.Mvc;
using LOARS.Web.Models.Dashboard;
using LOARS.Web.Services;   // For FakeTeachingData

namespace AIS_LO_System.ViewComponents
{
    public class SidebarViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke(int? year, int? trimester)
        {
            var defaultYear = DateTime.Now.Year;
            var defaultTrimester = GetTrimesterFromMonth(DateTime.Now.Month);

            var selectedYear = year ?? defaultYear;
            var selectedTrimester = trimester ?? defaultTrimester;

            var years = FakeTeachingData.GetYears();
            var trimestersByYear = FakeTeachingData.GetTrimestersByYear();

            if (!years.Contains(selectedYear))
                selectedYear = years.First();

            if (!trimestersByYear[selectedYear].Contains(selectedTrimester))
                selectedTrimester = trimestersByYear[selectedYear].First();

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