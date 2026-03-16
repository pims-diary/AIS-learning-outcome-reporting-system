using AIS_LO_System.Data;
using AIS_LO_System.Models.MarkStudents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIS_LO_System.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class MarkStudentsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MarkStudentsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            int assignmentId,
            string courseCode,
            string courseTitle,
            string assessmentName,
            int year,
            int trimester,
            string? searchTerm)
        {
            var query = _context.Students.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(s =>
                    s.FullName.ToLower().Contains(term) ||
                    s.StudentId.ToLower().Contains(term));
            }

            var students = await query
                .Select(s => new StudentListItemViewModel
                {
                    InternalId = s.Id,
                    StudentId = s.StudentId,
                    StudentName = s.FullName,
                    Status = _context.StudentAssessmentMarks.Any(m =>
                                m.StudentRefId == s.Id &&
                                m.CourseCode == courseCode &&
                                m.AssessmentName == assessmentName &&
                                m.IsMarked)
                             ? "Marked"
                             : "Not Marked"
                })
                .ToListAsync();

            var vm = new MarkStudentsViewModel
            {
                CourseCode = courseCode,
                AssessmentName = assessmentName,
                Year = year,
                Trimester = trimester,
                SearchTerm = searchTerm,
                Students = students
            };

            ViewBag.AssignmentId = assignmentId;
            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = courseTitle;
            ViewBag.AssessmentName = assessmentName;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> MarkStudent(
            int studentId,
            int assignmentId,
            string courseCode,
            string courseTitle,
            string assessmentName,
            int year,
            int trimester)
        {
            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == studentId);

            if (student == null)
                return NotFound();

            ViewBag.StudentName = student.FullName;
            ViewBag.StudentId = student.StudentId;

            ViewBag.AssignmentId = assignmentId;
            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = courseTitle;
            ViewBag.AssessmentName = assessmentName;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;

            return View();
        }
    }
}