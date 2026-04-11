using AIS_LO_System.Data;
using AIS_LO_System.Models;
using AIS_LO_System.Models.Reports;
using AIS_LO_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIS_LO_System.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class StudentLOReportController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SubmissionService _submissions;

        public StudentLOReportController(ApplicationDbContext context, SubmissionService submissions)
        {
            _context = context;
            _submissions = submissions;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string courseCode,
            string courseTitle,
            int year,
            int trimester,
            string? searchTerm,
            bool moderatorView = false)
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

            // Auto-submit report to moderator when lecturer opens it (not when moderator views it)
            int.TryParse(User.FindFirst("UserId")?.Value, out int reportUserId);
            if (!moderatorView && course.ModeratorId != null && reportUserId > 0)
            {
                await _submissions.SubmitAsync(
                    courseCode, year, trimester,
                    SubmissionItemType.StudentLOReport, null,
                    $"Student LO Report — {courseCode} {year} T{trimester}",
                    reportUserId);
            }

            ViewBag.Submission = await _submissions.GetLatestAsync(
                courseCode, year, trimester, SubmissionItemType.StudentLOReport);

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
            var gradedAssignments = new List<AIS_LO_System.Models.Assignment>();

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

                if (isGraded)
                    gradedAssignments.Add(assignment);
            }

            var gradedAssignmentIds = gradedAssignments.Select(a => a.Id).ToList();

            var learningOutcomes = await _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToListAsync();

            var mappings = await _context.CriterionLOMappings
                .Include(m => m.RubricCriterion)
                    .ThenInclude(c => c.Levels)
                .Include(m => m.RubricCriterion)
                    .ThenInclude(c => c.Rubric)
                .Include(m => m.LearningOutcome)
                .Where(m =>
                    m.LearningOutcome.CourseCode == courseCode &&
                    gradedAssignmentIds.Contains(m.RubricCriterion.Rubric.AssignmentId))
                .ToListAsync();

            var savedMarks = await _context.StudentCriterionMarks
                .Where(x => gradedAssignmentIds.Contains(x.AssignmentId) && x.StudentRefId == studentId)
                .ToListAsync();

            var loSummaries = learningOutcomes.Select(lo =>
            {
                var loMappings = mappings
                    .Where(m => m.LearningOutcomeId == lo.Id)
                    .ToList();

                decimal achievedScore = 0;
                decimal maxScore = 0;

                foreach (var mapping in loMappings)
                {
                    var saved = savedMarks.FirstOrDefault(x =>
                        x.AssignmentId == mapping.RubricCriterion.Rubric.AssignmentId &&
                        x.RubricCriterionId == mapping.RubricCriterionId);

                    if (saved != null)
                        achievedScore += saved.CalculatedScore;

                    var maxLevel = mapping.RubricCriterion?.Levels?
                        .OrderByDescending(l => l.Score)
                        .FirstOrDefault();

                    if (maxLevel != null)
                        maxScore += maxLevel.Score * mapping.Weight;
                }

                var percentage = maxScore > 0
                    ? Math.Round((achievedScore / maxScore) * 100, 2)
                    : 0;

                var status = percentage >= 50 ? "Achieved" : "Not Achieved";

                return new StudentCourseLOSummaryItemViewModel
                {
                    LearningOutcomeId = lo.Id,
                    Label = $"LO{lo.OrderNumber}",
                    LearningOutcomeText = lo.LearningOutcomeText,
                    AchievedScore = achievedScore,
                    MaxScore = maxScore,
                    Percentage = percentage,
                    Status = status
                };
            }).ToList();

            var loAnalyses = new List<StudentCourseLOAnalysisItemViewModel>();

            foreach (var lo in loSummaries)
            {
                var assessmentBreakdown = new List<string>();

                foreach (var assignment in assignments)
                {
                    var statusItem = assessmentStatuses.FirstOrDefault(x => x.AssignmentId == assignment.Id);
                    var isGraded = statusItem?.IsGraded ?? false;

                    if (!isGraded)
                    {
                        assessmentBreakdown.Add($"{assignment.AssessmentName}: Not Graded");
                        continue;
                    }

                    var assignmentMappings = mappings
                        .Where(m =>
                            m.LearningOutcomeId == lo.LearningOutcomeId &&
                            m.RubricCriterion.Rubric.AssignmentId == assignment.Id)
                        .ToList();

                    if (!assignmentMappings.Any())
                    {
                        assessmentBreakdown.Add($"{assignment.AssessmentName}: LO not assessed");
                        continue;
                    }

                    decimal assignmentAchieved = 0;
                    decimal assignmentMax = 0;

                    foreach (var mapping in assignmentMappings)
                    {
                        var saved = savedMarks.FirstOrDefault(x =>
                            x.AssignmentId == assignment.Id &&
                            x.RubricCriterionId == mapping.RubricCriterionId);

                        if (saved != null)
                            assignmentAchieved += saved.CalculatedScore;

                        var maxLevel = mapping.RubricCriterion?.Levels?
                            .OrderByDescending(l => l.Score)
                            .FirstOrDefault();

                        if (maxLevel != null)
                            assignmentMax += maxLevel.Score * mapping.Weight;
                    }

                    var assignmentPercentage = assignmentMax > 0
                        ? Math.Round((assignmentAchieved / assignmentMax) * 100, 2)
                        : 0;

                    assessmentBreakdown.Add($"{assignment.AssessmentName}: {assignmentPercentage:0.##}%");
                }

                loAnalyses.Add(new StudentCourseLOAnalysisItemViewModel
                {
                    LearningOutcomeId = lo.LearningOutcomeId,
                    Label = lo.Label,
                    LearningOutcomeText = lo.LearningOutcomeText,
                    Percentage = lo.Percentage,
                    Status = lo.Status,
                    AssessmentBreakdown = assessmentBreakdown
                });
            }

            var contributionTable = new List<StudentCourseLOContributionItemViewModel>();

            foreach (var assignment in assignments)
            {
                var statusItem = assessmentStatuses.FirstOrDefault(x => x.AssignmentId == assignment.Id);

                var contributionItem = new StudentCourseLOContributionItemViewModel
                {
                    AssignmentId = assignment.Id,
                    AssessmentName = assignment.AssessmentName,
                    IsGraded = statusItem?.IsGraded ?? false,
                    StatusText = (statusItem?.IsGraded ?? false) ? "Graded" : "Not Graded"
                };

                if (!contributionItem.IsGraded)
                {
                    contributionItem.Contributions.Add("Not Graded");
                    contributionItem.Achievements.Add("Not Graded");
                }
                else
                {
                    foreach (var lo in loSummaries)
                    {
                        var mappingsForLO = mappings
                            .Where(m =>
                                m.LearningOutcomeId == lo.LearningOutcomeId &&
                                m.RubricCriterion.Rubric.AssignmentId == assignment.Id)
                            .ToList();

                        if (!mappingsForLO.Any())
                        {
                            contributionItem.Contributions.Add($"{lo.Label}: LO not assessed");
                            contributionItem.Achievements.Add($"{lo.Label}: LO not assessed");
                            continue;
                        }

                        decimal achieved = 0;
                        decimal max = 0;

                        foreach (var mapping in mappingsForLO)
                        {
                            var saved = savedMarks.FirstOrDefault(x =>
                                x.AssignmentId == assignment.Id &&
                                x.RubricCriterionId == mapping.RubricCriterionId);

                            if (saved != null)
                                achieved += saved.CalculatedScore;

                            var maxLevel = mapping.RubricCriterion?.Levels?
                                .OrderByDescending(l => l.Score)
                                .FirstOrDefault();

                            if (maxLevel != null)
                                max += maxLevel.Score * mapping.Weight;
                        }

                        var percentage = max > 0
                            ? Math.Round((achieved / max) * 100, 2)
                            : 0;

                        contributionItem.Contributions.Add($"{lo.Label}: {max:0.##}");
                        contributionItem.Achievements.Add($"{lo.Label}: {percentage:0.##}%");
                    }
                }

                contributionTable.Add(contributionItem);
            }

            var achievedCount = loSummaries.Count(x => x.Status == "Achieved");
            var notAchievedCount = loSummaries.Count(x => x.Status == "Not Achieved");

            var vm = new StudentCourseLOOverviewViewModel
            {
                StudentInternalId = student.Id,
                StudentId = student.StudentId,
                StudentName = student.FullName,
                CourseCode = courseCode,
                CourseTitle = courseTitle,
                Year = year,
                Trimester = trimester,
                Assessments = assessmentStatuses,
                LOSummaries = loSummaries,
                LOAnalyses = loAnalyses,
                Contributions = contributionTable,
                AchievedCount = achievedCount,
                NotAchievedCount = notAchievedCount,
                TotalLOCount = loSummaries.Count
            };

            return View(vm);
        }
    }
}