using Microsoft.AspNetCore.Mvc;

namespace AIS_LO_System.Controllers
{
    public class AssignmentController : Controller
    {
        [HttpGet]
        public IActionResult Index(
            string assessmentName,
            string courseCode,
            string courseTitle,
            int year,
            int trimester)
        {
            ViewBag.AssessmentName = assessmentName;
            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = courseTitle;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;
            ViewBag.CourseDate = GetTrimesterDateRange(year, trimester);

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
