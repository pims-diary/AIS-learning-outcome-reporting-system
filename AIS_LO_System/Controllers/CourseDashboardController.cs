using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LOARS.Web.Services;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class CourseDashboardController : Controller
    {
        [HttpGet]
        public IActionResult Index(string courseCode, int year, int trimester)
        {
            var courses = FakeTeachingData.GetCourses(year, trimester);
            var course = courses.FirstOrDefault(c => c.Code == courseCode);

            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = course?.Title ?? "";
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;
            ViewBag.DateRange = GetTrimesterDateRange(year, trimester);

            return View();
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
