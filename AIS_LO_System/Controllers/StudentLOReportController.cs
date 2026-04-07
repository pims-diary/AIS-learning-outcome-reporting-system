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

        [HttpGet]
        public async Task<IActionResult> Overview(
            int studentId,
            string courseCode,
            string courseTitle,
            int year,
            int trimester)
        {
            var course = await _context.Courses
                .FirstOrDefaultAsync(c =>
                    c.Code == courseCode &&
                    c.Year == year &&
                    c.Trimester == trimester);

            if (course == null)
            {
                TempData["Error"] = "Course not found.";
                return RedirectToAction(nameof(Index), new { courseCode, courseTitle, year, trimester });
            }

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == studentId);

            if (student == null)
            {
                TempData["Error"] = "Student not found.";
                return RedirectToAction(nameof(Index), new { courseCode, courseTitle, year, trimester });
            }

            var isEnrolled = await _context.StudentCourseEnrolments
                .AnyAsync(e => e.CourseId == course.Id && e.StudentId == studentId);

            if (!isEnrolled)
            {
                TempData["Error"] = "This student is not enrolled in the selected course.";
                return RedirectToAction(nameof(Index), new { courseCode, courseTitle, year, trimester });
            }

            var assignments = await _context.Assignments
                .Where(a =>
                    a.CourseCode == courseCode &&
                    a.Year == year &&
                    a.Trimester == trimester)
                .OrderBy(a => a.AssessmentName)
                .ToListAsync();

            var assessmentStatuses = new List<StudentAssessmentStatusItemViewModel>();

            foreach (var assignment in assignments)
            {
                var isGraded = await _context.StudentAssessmentMarks.AnyAsync(m =>
                    m.StudentRefId == studentId &&
                    m.CourseCode == courseCode &&
                    m.AssessmentName == assignment.AssessmentName &&
                    m.IsMarked);

                assessmentStatuses.Add(new StudentAssessmentStatusItemViewModel
                {
                    AssignmentId = assignment.Id,
                    AssessmentName = assignment.AssessmentName,
                    IsGraded = isGraded
                });
            }

            var vm = new StudentCourseLOOverviewViewModel
            {
                StudentInternalId = student.Id,
                StudentId = student.StudentId,
                StudentName = student.FullName,
                CourseCode = courseCode,
                CourseTitle = courseTitle,
                Year = year,
                Trimester = trimester,
                Assessments = assessmentStatuses
            };

            return View(vm);
        }
    }
}