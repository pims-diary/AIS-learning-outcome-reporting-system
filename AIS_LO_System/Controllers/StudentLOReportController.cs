using AIS_LO_System.Data;
using AIS_LO_System.Models.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIS_LO_System.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class StudentLOReportController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StudentLOReportController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string courseCode,
            string courseTitle,
            int year,
            int trimester,
            string? searchTerm)
        {
            var course = await _context.Courses
                .FirstOrDefaultAsync(c =>
                    c.Code == courseCode &&
                    c.Year == year &&
                    c.Trimester == trimester);

            if (course == null)
            {
                TempData["Error"] = "Course not found.";
                return RedirectToAction("Index", "LecturerDashboard", new { year, trimester });
            }

            var enrolledStudentsQuery = _context.StudentCourseEnrolments
                .Where(e => e.CourseId == course.Id)
                .Include(e => e.Student)
                .Select(e => e.Student)
                .Distinct()
                .AsQueryable();

            var totalStudentsEnrolled = await enrolledStudentsQuery.CountAsync();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                enrolledStudentsQuery = enrolledStudentsQuery.Where(s =>
                    s.StudentId.ToLower().Contains(term));
            }

            var students = await enrolledStudentsQuery
                .OrderBy(s => s.StudentId)
                .Select(s => new StudentLOReportListItemViewModel
                {
                    InternalId = s.Id,
                    StudentId = s.StudentId,
                    StudentName = s.FullName
                })
                .ToListAsync();

            var vm = new StudentLOReportListViewModel
            {
                CourseCode = courseCode,
                CourseTitle = courseTitle,
                Year = year,
                Trimester = trimester,
                SearchTerm = searchTerm,
                TotalStudentsEnrolled = totalStudentsEnrolled,
                Students = students
            };

            return View(vm);
        }
    }
}