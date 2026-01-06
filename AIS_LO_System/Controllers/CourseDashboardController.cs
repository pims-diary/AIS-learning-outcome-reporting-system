using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class CourseDashboardController : Controller
    {
        [HttpGet]
        public IActionResult Index(string courseCode, int year, int trimester)
        {
            ViewBag.CourseCode = courseCode;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;
            return View();
        }
    }
}
