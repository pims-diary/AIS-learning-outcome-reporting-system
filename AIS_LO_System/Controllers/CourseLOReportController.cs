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
        m.LearningOutcome != null &&
        m.RubricCriterion != null &&
        m.RubricCriterion.Rubric != null &&
        m.LearningOutcome.CourseCode == courseCode &&
        assignmentIds.Contains(m.RubricCriterion.Rubric.AssignmentId))
    .ToListAsync();

            var allSavedMarks = await _context.StudentCriterionMarks
                .Where(x => assignmentIds.Contains(x.AssignmentId))
                .ToListAsync();

            var assessments = rawAssignments
                .Select(a =>
                {
                    var gradedStudentsCount = gradedAssessmentStatuses
                        .Where(m => m.AssessmentName == a.AssessmentName)
                        .Select(m => m.StudentRefId)
                        .Distinct()
                        .Count();

                    string statusText;
                    if (gradedStudentsCount == 0)
                        statusText = "Not Started";
                    else if (gradedStudentsCount < enrolledStudents.Count)
                        statusText = "Partially Graded";
                    else
                        statusText = "Fully Graded";

                    return new CourseLOAssessmentItemViewModel
                    {
                        AssignmentId = a.Id,
                        AssessmentName = a.AssessmentName,
                        MarksPercentage = a.MarksPercentage,
                        HasAnyGradedStudent = gradedStudentsCount > 0,
                        GradedStudentsCount = gradedStudentsCount,
                        StatusText = statusText
                    };
                })
                .ToList();

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
                        var rubricAssignmentId = mapping.RubricCriterion?.Rubric?.AssignmentId;

                        if (rubricAssignmentId == null)
                        {
                            continue;
                        }

                        var saved = studentSavedMarks.FirstOrDefault(x =>
                            x.AssignmentId == rubricAssignmentId.Value &&
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

            var contributionTable = new List<CourseLOContributionItemViewModel>();

            foreach (var assignment in rawAssignments)
            {
                var assessmentVm = assessments.First(x => x.AssignmentId == assignment.Id);

                var contributionItem = new CourseLOContributionItemViewModel
                {
                    AssignmentId = assignment.Id,
                    AssessmentName = assignment.AssessmentName,
                    HasAnyGradedStudent = assessmentVm.HasAnyGradedStudent,
                    StatusText = assessmentVm.StatusText
                };

                if (!assessmentVm.HasAnyGradedStudent)
                {
                    contributionItem.Contributions.Add("Not graded yet");
                    contributionItem.ClassAchievements.Add("Not graded yet");
                }
                else
                {
                    foreach (var lo in learningOutcomes)
                    {
                        var mappingsForLO = mappings
                            .Where(m =>
                                m.LearningOutcomeId == lo.Id &&
                                m.RubricCriterion.Rubric.AssignmentId == assignment.Id)
                            .ToList();

                        if (!mappingsForLO.Any())
                        {
                            contributionItem.Contributions.Add($"LO{lo.OrderNumber}: LO not assessed");
                            contributionItem.ClassAchievements.Add($"LO{lo.OrderNumber}: LO not assessed");
                            continue;
                        }

                        decimal totalContribution = 0;

                        foreach (var mapping in mappingsForLO)
                        {
                            var maxLevel = mapping.RubricCriterion?.Levels?
                                .OrderByDescending(l => l.Score)
                                .FirstOrDefault();

                            if (maxLevel != null)
                                totalContribution += maxLevel.Score * mapping.Weight;
                        }

                        var rows = studentAssessmentLOPercentages
                            .Where(x =>
                                x.LearningOutcomeId == lo.Id &&
                                x.AssignmentId == assignment.Id &&
                                x.IsGraded)
                            .ToList();

                        var averageAchievement = rows.Any()
                            ? Math.Round(rows.Average(x => x.Percentage), 2)
                            : 0;

                        contributionItem.Contributions.Add($"LO{lo.OrderNumber}: {totalContribution:0.##}");
                        contributionItem.ClassAchievements.Add($"LO{lo.OrderNumber}: {averageAchievement:0.##}%");
                    }
                }

                contributionTable.Add(contributionItem);
            }

            var totalAchievedLOs = loSummaries.Count(x => x.Status == "Achieved");
            var totalNotAchievedLOs = loSummaries.Count(x => x.Status == "Not Achieved");

            var strongestLOs = loSummaries
                .OrderByDescending(x => x.AveragePercentage)
                .Take(3)
                .ToList();

            var weakestLOs = loSummaries
                .OrderBy(x => x.AveragePercentage)
                .Take(3)
                .ToList();

            var loRecommendations = new Dictionary<string, string>();
            foreach (var lo in loSummaries)
            {
                if (lo.AveragePercentage < 50)
                {
                    loRecommendations[lo.Label] = "Low class performance. Review teaching approach, rubric alignment, or provide reinforcement activities.";
                }
                else if (lo.AveragePercentage < 70)
                {
                    loRecommendations[lo.Label] = "Moderate performance. Some students may need targeted support and more practice.";
                }
                else
                {
                    loRecommendations[lo.Label] = "Strong class performance for this learning outcome.";
                }
            }

            int atRiskThreshold = learningOutcomes.Count > 0
                ? (int)System.Math.Ceiling(learningOutcomes.Count / 2.0)
                : 0;

            var atRiskStudents = studentResults
                .Where(s => s.NotAchievedLOCount >= atRiskThreshold && learningOutcomes.Count > 0)
                .OrderByDescending(s => s.NotAchievedLOCount)
                .ThenBy(s => s.StudentId)
                .ToList();

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
                Contributions = contributionTable,
                AtRiskStudents = atRiskStudents,
                StrongestLOs = strongestLOs,
                WeakestLOs = weakestLOs,
                LORecommendations = loRecommendations,
                Messages = new List<string>
                {
                    "Course LO report has been enhanced with class insights.",
                    "Strongest/weakest LOs, at-risk students, and grading progress are now included."
                }
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> AssignmentReport(
            int assignmentId,
            string courseCode,
            string courseTitle,
            string assessmentName,
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

            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a =>
                    a.Id == assignmentId &&
                    a.CourseCode == courseCode &&
                    a.Year == year &&
                    a.Trimester == trimester);

            if (assignment == null)
            {
                TempData["Error"] = "Assessment not found.";
                return RedirectToAction(nameof(Index), new { courseCode, courseTitle, year, trimester });
            }

            var enrolledStudents = await _context.StudentCourseEnrolments
                .Where(e => e.CourseId == course.Id)
                .Include(e => e.Student)
                .Select(e => e.Student)
                .Distinct()
                .OrderBy(s => s.StudentId)
                .ToListAsync();

            var markedStatuses = await _context.StudentAssessmentMarks
                .Where(m =>
                    m.CourseCode == courseCode &&
                    m.AssessmentName == assignment.AssessmentName &&
                    m.IsMarked)
                .ToListAsync();

            var markedStudentIds = markedStatuses
                .Select(x => x.StudentRefId)
                .Distinct()
                .ToList();

            var mappings = await _context.CriterionLOMappings
                .Include(m => m.RubricCriterion)
                    .ThenInclude(c => c.Levels)
                .Include(m => m.RubricCriterion)
                .Include(m => m.LearningOutcome)
                .Where(m => m.RubricCriterion.Rubric.AssignmentId == assignmentId)
                .ToListAsync();

            var learningOutcomes = mappings
                .Where(m => m.LearningOutcome != null)
                .Select(m => m.LearningOutcome!)
                .DistinctBy(lo => lo.Id)
                .OrderBy(lo => lo.OrderNumber)
                .ToList();

            var savedMarks = await _context.StudentCriterionMarks
                .Where(x => x.AssignmentId == assignmentId)
                .ToListAsync();

            var loAggregateRows = new List<(int LearningOutcomeId, decimal Percentage, string Status)>();
            var assignmentLOPercentages = new List<(int StudentId, int LearningOutcomeId, decimal Percentage, string Status)>();
            var studentResults = new List<CourseLOStudentResultItemViewModel>();

            foreach (var student in enrolledStudents)
            {
                bool isMarked = markedStudentIds.Contains(student.Id);
                var studentMarks = savedMarks
                    .Where(x => x.StudentRefId == student.Id)
                    .ToList();

                int achievedCount = 0;
                int notAchievedCount = 0;

                foreach (var lo in learningOutcomes)
                {
                    if (!isMarked)
                    {
                        assignmentLOPercentages.Add((student.Id, lo.Id, 0, "Not Achieved"));
                        notAchievedCount++;
                        loAggregateRows.Add((lo.Id, 0, "Not Achieved"));
                        continue;
                    }

                    var loMappings = mappings
                        .Where(m => m.LearningOutcomeId == lo.Id)
                        .ToList();

                    decimal achievedScore = 0;
                    decimal maxScore = 0;

                    foreach (var mapping in loMappings)
                    {
                        var saved = studentMarks.FirstOrDefault(x => x.RubricCriterionId == mapping.RubricCriterionId);

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

                    assignmentLOPercentages.Add((student.Id, lo.Id, percentage, status));
                    loAggregateRows.Add((lo.Id, percentage, status));
                }

                studentResults.Add(new CourseLOStudentResultItemViewModel
                {
                    StudentInternalId = student.Id,
                    StudentId = student.StudentId,
                    StudentName = student.FullName,
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

                    var breakdown = studentResults
                        .Where(s => markedStudentIds.Contains(s.StudentInternalId))
                        .Select(s =>
                        {
                            var row = assignmentLOPercentages.FirstOrDefault(x =>
                                x.StudentId == s.StudentInternalId &&
                                x.LearningOutcomeId == lo.Id);

                            return $"{s.StudentName}: {row.Percentage:0.##}%";
                        })
                        .ToList();

                    if (!breakdown.Any())
                    {
                        breakdown.Add("No students have been marked for this assessment yet.");
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

            var strongestLOs = loSummaries
                .OrderByDescending(x => x.AveragePercentage)
                .Take(3)
                .ToList();

            var weakestLOs = loSummaries
                .OrderBy(x => x.AveragePercentage)
                .Take(3)
                .ToList();

            var loRecommendations = new Dictionary<string, string>();
            foreach (var lo in loSummaries)
            {
                if (lo.AveragePercentage < 50)
                {
                    loRecommendations[lo.Label] = "Low class performance for this assessment. Review criterion mapping, assessment difficulty, or provide revision support.";
                }
                else if (lo.AveragePercentage < 70)
                {
                    loRecommendations[lo.Label] = "Moderate performance for this assessment. Some targeted reinforcement may help improve this LO.";
                }
                else
                {
                    loRecommendations[lo.Label] = "Strong class performance for this assessment LO.";
                }
            }

            int atRiskThreshold = learningOutcomes.Count > 0
                ? (int)System.Math.Ceiling(learningOutcomes.Count / 2.0)
                : 0;

            var atRiskStudents = studentResults
                .Where(s => markedStudentIds.Contains(s.StudentInternalId))
                .Where(s => s.NotAchievedLOCount >= atRiskThreshold && learningOutcomes.Count > 0)
                .OrderByDescending(s => s.NotAchievedLOCount)
                .ThenBy(s => s.StudentId)
                .ToList();

            var vm = new AssignmentClassLOReportViewModel
            {
                AssignmentId = assignmentId,
                AssessmentName = assignment.AssessmentName,
                CourseCode = courseCode,
                CourseTitle = courseTitle,
                Year = year,
                Trimester = trimester,
                TotalStudentsEnrolled = enrolledStudents.Count,
                TotalMarkedStudents = markedStudentIds.Count,
                TotalAchievedLOs = loSummaries.Count(x => x.Status == "Achieved"),
                TotalNotAchievedLOs = loSummaries.Count(x => x.Status == "Not Achieved"),
                LOSummaries = loSummaries,
                LOAnalyses = loAnalyses,
                AtRiskStudents = atRiskStudents,
                StudentResults = studentResults.Where(s => markedStudentIds.Contains(s.StudentInternalId)).ToList(),
                StrongestLOs = strongestLOs,
                WeakestLOs = weakestLOs,
                LORecommendations = loRecommendations
            };

            return View(vm);
        }
    }
}