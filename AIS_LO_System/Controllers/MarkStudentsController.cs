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

            var savedMarks = await _context.StudentCriterionMarks
                .Where(x => x.AssignmentId == assignmentId && x.StudentRefId == studentId)
                .ToListAsync();

            var criteria = rubric.Criteria
                .OrderBy(c => c.Id)
                .Select(c =>
                {
                    var saved = savedMarks.FirstOrDefault(x => x.RubricCriterionId == c.Id);

                    return new RubricCriterionMarkingViewModel
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
                        SelectedLevel = saved?.SelectedLevel,
                        CalculatedMarks = saved?.CalculatedScore ?? 0
                    };
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
                TotalScore = criteria.Sum(c => c.CalculatedMarks),
                Comment = savedMarks.FirstOrDefault()?.Comment
            };

            return View(vm);
        }

        [HttpGet]
        public IActionResult LOAchievementReport(
    int studentId,
    int assignmentId,
    string courseCode,
    string courseTitle,
    string assessmentName,
    int year,
    int trimester)
        {
            ViewBag.StudentId = studentId;
            ViewBag.AssignmentId = assignmentId;
            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = courseTitle;
            ViewBag.AssessmentName = assessmentName;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveMarks(
            int assignmentId,
            int studentId,
            string courseCode,
            string courseTitle,
            string assessmentName,
            int year,
            int trimester,
            string? comment,
            string selectedLevelsJson)
        {
            var student = await _context.Students.FirstOrDefaultAsync(s => s.Id == studentId);
            if (student == null)
                return NotFound();

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                .FirstOrDefaultAsync(r => r.AssignmentId == assignmentId);

            if (rubric == null)
            {
                TempData["Error"] = "Rubric not found.";
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

            var existingMarks = await _context.StudentCriterionMarks
                .Where(x => x.AssignmentId == assignmentId && x.StudentRefId == studentId)
                .ToListAsync();

            if (existingMarks.Any())
                _context.StudentCriterionMarks.RemoveRange(existingMarks);

            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<CriterionSelectionInput>>(
                selectedLevelsJson ?? "[]",
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<CriterionSelectionInput>();

            if (!parsed.Any())
            {
                TempData["Error"] = "Please select at least one level before saving.";

                return RedirectToAction("MarkStudent", new
                {
                    studentId,
                    assignmentId,
                    courseCode,
                    courseTitle,
                    assessmentName,
                    year,
                    trimester
                });
            }

            foreach (var item in parsed)
            {
                var criterion = rubric.Criteria.FirstOrDefault(c => c.Id == item.CriterionId);
                if (criterion == null) continue;

                var weight = await _context.CriterionLOMappings
                    .Where(m => m.RubricCriterionId == item.CriterionId)
                    .Select(m => m.Weight)
                    .FirstOrDefaultAsync();

                var calculated = item.SelectedLevel * weight;

                _context.StudentCriterionMarks.Add(new AIS_LO_System.Models.StudentCriterionMark
                {
                    AssignmentId = assignmentId,
                    StudentRefId = studentId,
                    RubricCriterionId = item.CriterionId,
                    SelectedLevel = item.SelectedLevel,
                    Weight = weight,
                    CalculatedScore = calculated,
                    Comment = comment
                });
            }

            var status = await _context.StudentAssessmentMarks
                .FirstOrDefaultAsync(x =>
                    x.StudentRefId == studentId &&
                    x.CourseCode == courseCode &&
                    x.AssessmentName == assessmentName);

            if (status == null)
            {
                _context.StudentAssessmentMarks.Add(new AIS_LO_System.Models.StudentAssessmentMark
                {
                    StudentRefId = studentId,
                    CourseCode = courseCode,
                    AssessmentName = assessmentName,
                    IsMarked = true
                });
            }
            else
            {
                status.IsMarked = true;
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Marks saved successfully.";
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearMarks(
            int assignmentId,
            int studentId,
            string courseCode,
            string courseTitle,
            string assessmentName,
            int year,
            int trimester)
        {
            var existingMarks = await _context.StudentCriterionMarks
                .Where(x => x.AssignmentId == assignmentId && x.StudentRefId == studentId)
                .ToListAsync();

            if (existingMarks.Any())
            {
                _context.StudentCriterionMarks.RemoveRange(existingMarks);
            }

            var status = await _context.StudentAssessmentMarks
                .FirstOrDefaultAsync(x =>
                    x.StudentRefId == studentId &&
                    x.CourseCode == courseCode &&
                    x.AssessmentName == assessmentName);

            if (status != null)
            {
                status.IsMarked = false;
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Marks cleared successfully.";

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
    }

    public class CriterionSelectionInput
    {
        public int CriterionId { get; set; }
        public int SelectedLevel { get; set; }
    }
}