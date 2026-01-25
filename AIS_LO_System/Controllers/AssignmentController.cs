using Microsoft.AspNetCore.Mvc;

namespace AIS_LO_System.Controllers
{
    public class AssignmentController : Controller
    {
        [HttpGet]
        public IActionResult Index(int id, string courseCode, string courseTitle)
        {
            ViewBag.AssignmentId = id;
            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = courseTitle;
            return View();
        }
    }
}
