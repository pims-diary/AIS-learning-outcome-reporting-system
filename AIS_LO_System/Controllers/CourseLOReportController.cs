using AIS_LO_System.Data;
using AIS_LO_System.Models.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIS_LO_System.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class CourseLOReportController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CourseLOReportController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
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
                return RedirectToAction("Index", "LecturerDashboard", new { year, trimester });
            }

            var enrolledStudents = await _context.StudentCourseEnrolments
                .Where(e => e.CourseId == course.Id)
                .Include(e => e.Student)
                .Select(e => new CourseLOStudentItemViewModel
                {
                    StudentInternalId = e.Student.Id,
                    StudentId = e.Student.StudentId,
                    StudentName = e.Student.FullName
                })
                .Distinct()
                .OrderBy(s => s.StudentId)
                .ToListAsync();

            var rawAssignments = await _context.Assignments
                .Where(a =>
                    a.CourseCode == courseCode &&
                    a.Year == year &&
                    a.Trimester == trimester)
                .OrderBy(a => a.AssessmentName)
                .ToListAsync();

            var assessmentNames = rawAssignments.Select(a => a.AssessmentName).ToList();

            var gradedAssessmentStatuses = await _context.StudentAssessmentMarks
                .Where(m =>
                    m.CourseCode == courseCode &&
                    assessmentNames.Contains(m.AssessmentName) &&
                    m.IsMarked)
                .ToListAsync();

            var assessments = rawAssignments
                .Select(a => new CourseLOAssessmentItemViewModel
                {
                    AssignmentId = a.Id,
                    AssessmentName = a.AssessmentName,
                    MarksPercentage = a.MarksPercentage,
                    HasAnyGradedStudent = gradedAssessmentStatuses.Any(m => m.AssessmentName == a.AssessmentName)
                })
                .ToList();

            var learningOutcomes = await _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToListAsync();

            var assignmentIds = rawAssignments.Select(a => a.Id).ToList();

            var mappings = await _context.CriterionLOMappings
                .Include(m => m.RubricCriterion)
                    .ThenInclude(c => c.Levels)
                .Include(m => m.RubricCriterion)
                    .ThenInclude(c => c.Rubric)
                .Include(m => m.LearningOutcome)
                .Where(m =>
                    m.LearningOutcome.CourseCode == courseCode &&
                    assignmentIds.Contains(m.RubricCriterion.Rubric.AssignmentId))
                .ToListAsync();

            var allSavedMarks = await _context.StudentCriterionMarks
                .Where(x => assignmentIds.Contains(x.AssignmentId))
                .ToListAsync();

            var studentResults = new List<CourseLOStudentResultItemViewModel>();
            var loAggregateRows = new List<(int LearningOutcomeId, decimal Percentage, string Status)>();
            var studentAssessmentLOPercentages = new List<(int LearningOutcomeId, int AssignmentId, decimal Percentage, bool IsGraded)>();

            foreach (var student in enrolledStudents)
            {
                var studentAssessmentStatuses = gradedAssessmentStatuses
                    .Where(x => x.StudentRefId == student.StudentInternalId)
                    .ToList();

                var gradedAssignmentsForStudent = rawAssignments
                    .Where(a => studentAssessmentStatuses.Any(s => s.AssessmentName == a.AssessmentName))
                    .ToList();

                var gradedAssignmentIdsForStudent = gradedAssignmentsForStudent
                    .Select(a => a.Id)
                    .ToList();

                var studentSavedMarks = allSavedMarks
                    .Where(x =>
                        x.StudentRefId == student.StudentInternalId &&
                        gradedAssignmentIdsForStudent.Contains(x.AssignmentId))
                    .ToList();

                int achievedCount = 0;
                int notAchievedCount = 0;

                foreach (var lo in learningOutcomes)
                {
                    var loMappings = mappings
                        .Where(m =>
                            m.LearningOutcomeId == lo.Id &&
                            gradedAssignmentIdsForStudent.Contains(m.RubricCriterion.Rubric.AssignmentId))
                        .ToList();

                    decimal achievedScore = 0;
                    decimal maxScore = 0;

                    foreach (var mapping in loMappings)
                    {
                        var saved = studentSavedMarks.FirstOrDefault(x =>
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

                    if (status == "Achieved")
                        achievedCount++;
                    else
                        notAchievedCount++;

                    loAggregateRows.Add((lo.Id, percentage, status));
                }

                foreach (var assignment in rawAssignments)
                {
                    var isGradedForStudent = gradedAssignmentsForStudent.Any(a => a.Id == assignment.Id);

                    foreach (var lo in learningOutcomes)
                    {
                        if (!isGradedForStudent)
                        {
                            studentAssessmentLOPercentages.Add((lo.Id, assignment.Id, 0, false));
                            continue;
                        }

                        var assignmentMappings = mappings
                            .Where(m =>
                                m.LearningOutcomeId == lo.Id &&
                                m.RubricCriterion.Rubric.AssignmentId == assignment.Id)
                            .ToList();

                        if (!assignmentMappings.Any())
                        {
                            studentAssessmentLOPercentages.Add((lo.Id, assignment.Id, 0, true));
                            continue;
                        }

                        decimal assignmentAchieved = 0;
                        decimal assignmentMax = 0;

                        foreach (var mapping in assignmentMappings)
                        {
                            var saved = studentSavedMarks.FirstOrDefault(x =>
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

                        studentAssessmentLOPercentages.Add((lo.Id, assignment.Id, assignmentPercentage, true));
                    }
                }

                studentResults.Add(new CourseLOStudentResultItemViewModel
                {
                    StudentInternalId = student.StudentInternalId,
                    StudentId = student.StudentId,
                    StudentName = student.StudentName,
                    AchievedLOCount = achievedCount,
                    NotAchievedLOCount = notAchievedCount
                });
            }

            var loSummaries = learningOutcomes
                .Select(lo =>
                {
                    var loRows = loAggregateRows
                        .Where(x => x.LearningOutcomeId == lo.Id)
                        .ToList();

                    var averagePercentage = loRows.Any()
                        ? Math.Round(loRows.Average(x => x.Percentage), 2)
                        : 0;

                    var achievedStudentsCount = loRows.Count(x => x.Status == "Achieved");
                    var notAchievedStudentsCount = loRows.Count(x => x.Status == "Not Achieved");

                    var status = averagePercentage >= 50 ? "Achieved" : "Not Achieved";

                    return new CourseLOSummaryItemViewModel
                    {
                        LearningOutcomeId = lo.Id,
                        Label = $"LO{lo.OrderNumber}",
                        LearningOutcomeText = lo.LearningOutcomeText,
                        AveragePercentage = averagePercentage,
                        AchievedStudentsCount = achievedStudentsCount,
                        NotAchievedStudentsCount = notAchievedStudentsCount,
                        Status = status
                    };
                })
                .ToList();

            var loAnalyses = learningOutcomes
                .Select(lo =>
                {
                    var summary = loSummaries.First(x => x.LearningOutcomeId == lo.Id);
                    var breakdown = new List<string>();

                    foreach (var assignment in rawAssignments)
                    {
                        var rows = studentAssessmentLOPercentages
                            .Where(x =>
                                x.LearningOutcomeId == lo.Id &&
                                x.AssignmentId == assignment.Id)
                            .ToList();

                        if (!rows.Any() || rows.All(x => !x.IsGraded))
                        {
                            breakdown.Add($"{assignment.AssessmentName}: Not graded yet");
                            continue;
                        }

                        var gradedRows = rows.Where(x => x.IsGraded).ToList();

                        var average = gradedRows.Any()
                            ? Math.Round(gradedRows.Average(x => x.Percentage), 2)
                            : 0;

                        breakdown.Add($"{assignment.AssessmentName}: {average:0.##}% average");
                    }

                    return new CourseLOAnalysisItemViewModel
                    {
                        LearningOutcomeId = lo.Id,
                        Label = summary.Label,
                        LearningOutcomeText = lo.LearningOutcomeText,
                        AveragePercentage = summary.AveragePercentage,
                        Status = summary.Status,
                        AssessmentBreakdown = breakdown
                    };
                })
                .ToList();

            var totalAchievedLOs = loSummaries.Count(x => x.Status == "Achieved");
            var totalNotAchievedLOs = loSummaries.Count(x => x.Status == "Not Achieved");

            var vm = new CourseLOReportViewModel
            {
                CourseCode = courseCode,
                CourseTitle = courseTitle,
                Year = year,
                Trimester = trimester,
                TotalStudentsEnrolled = enrolledStudents.Count,
                TotalAssessments = assessments.Count,
                TotalAchievedLOs = totalAchievedLOs,
                TotalNotAchievedLOs = totalNotAchievedLOs,
                Students = enrolledStudents,
                Assessments = assessments,
                LOSummaries = loSummaries,
                StudentResults = studentResults,
                LOAnalyses = loAnalyses,
                Messages = new List<string>
                {
                    "Class-wide LO graph and analysis have been added.",
                    "Assessment contribution and student drill-down can be added next."
                }
            };

            return View(vm);
        }
    }
}