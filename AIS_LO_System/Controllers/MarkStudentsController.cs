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
        public async Task<IActionResult> Index(string courseCode, string assessmentName, int year, int trimester, string? searchTerm)
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

            ViewBag.AssignmentId = 1; // temporary

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

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.Levels)
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.LOMappings)
                        .ThenInclude(m => m.LearningOutcome)
                .FirstOrDefaultAsync(r => r.AssignmentId == assignmentId);

            if (rubric == null)
            {
                TempData["Error"] = "No rubric found for this assignment.";
                return RedirectToAction("Index", new
                {
                    assignmentId,
                    courseCode,
                    courseTitle,
                    assessmentName,
                    year,
                    trimester
                });
            }

            var criteria = rubric.Criteria
                .OrderBy(c => c.Id)
                .Select(c => new RubricCriterionMarkingViewModel
                {
                    CriterionId = c.Id,
                    CriterionTitle = c.CriterionName,
                    Weight = c.LOMappings != null && c.LOMappings.Any()
                        ? c.LOMappings.First().Weight
                        : 0,
                    LOs = c.LOMappings != null && c.LOMappings.Any()
                        ? string.Join(", ", c.LOMappings
                            .Where(m => m.LearningOutcome != null)
                            .Select(m => m.LearningOutcome.LearningOutcomeText))
                        : "Not Mapped",
                    AvailableLevels = c.Levels
                        .OrderByDescending(l => l.Score)
                        .Select(l => l.Score)
                        .ToList(),
                    SelectedLevel = null,
                    CalculatedMarks = 0
                })
                .ToList();

            var allLevels = rubric.Criteria
                .SelectMany(c => c.Levels)
                .Select(l => l.Score)
                .Distinct()
                .OrderByDescending(s => s)
                .ToList();

            var vm = new MarkStudentViewModel
            {
                StudentInternalId = student.Id,
                StudentId = student.StudentId,
                StudentName = student.FullName,
                AssignmentId = assignmentId,
                CourseCode = courseCode,
                CourseTitle = courseTitle,
                AssessmentName = assessmentName,
                Year = year,
                Trimester = trimester,
                Criteria = criteria,
                Levels = allLevels,
                TotalWeight = criteria.Sum(c => c.Weight),
                TotalScore = 0
            };

            return View(vm);
        }
    }
}