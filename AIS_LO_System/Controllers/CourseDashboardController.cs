using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer,Admin")]
    public class CourseDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CourseDashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string courseCode, int year, int trimester)
        {
            var course = await _context.Courses
                .FirstOrDefaultAsync(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);

            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = course?.Title ?? "";
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;
            ViewBag.DateRange = GetTrimesterDateRange(year, trimester);

            // Load assessments from database (extracted from course outline)
            var assessments = await _context.Assignments
                .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                .OrderBy(a => a.Id)
                .ToListAsync();

            ViewBag.Assessments = assessments;

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