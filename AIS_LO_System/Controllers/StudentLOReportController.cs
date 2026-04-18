using AIS_LO_System.Data;
using AIS_LO_System.Models;
using AIS_LO_System.Models.Reports;
using AIS_LO_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AIS_LO_System.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class StudentLOReportController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public StudentLOReportController(
            ApplicationDbContext context,
            SubmissionService submissions,
            IWebHostEnvironment env)
        {
            _context = context;
            _submissions = submissions;
            _env = env;
        }

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

            // FIX #3: Only auto-submit if there's no existing Pending or Approved submission
            var existingSubmission = await _submissions.GetLatestAsync(
                courseCode, year, trimester, SubmissionItemType.StudentLOReport);

            int.TryParse(User.FindFirst("UserId")?.Value, out int reportUserId);
            if (!moderatorView &&
                course.ModeratorId != null &&
                reportUserId > 0 &&
                (existingSubmission == null || existingSubmission.Status == SubmissionStatus.Denied))
            {
                await _submissions.SubmitAsync(
                    courseCode, year, trimester,
                    SubmissionItemType.StudentLOReport, null,
                    $"Student LO Report — {courseCode} {year} T{trimester}",
                    reportUserId);

                // Refresh submission after creating it
                existingSubmission = await _submissions.GetLatestAsync(
                    courseCode, year, trimester, SubmissionItemType.StudentLOReport);
            }

            ViewBag.Submission = existingSubmission;

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
            var vm = await BuildStudentCourseOverviewViewModel(
                studentId, courseCode, courseTitle, year, trimester);

            if (vm == null)
            {
                TempData["Error"] = "Unable to load the student course LO report.";
                return RedirectToAction(nameof(Index), new { courseCode, courseTitle, year, trimester });
            }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadStudentCourseReportPdf(
            int studentId,
            string courseCode,
            string courseTitle,
            int year,
            int trimester)
        {
            var vm = await BuildStudentCourseOverviewViewModel(
                studentId, courseCode, courseTitle, year, trimester);

            if (vm == null)
                return NotFound();

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("Student Course LO Report")
                            .FontSize(18).Bold();

                        col.Item().Text($"{vm.StudentName} ({vm.StudentId})");
                        col.Item().Text($"{vm.CourseCode} - {vm.CourseTitle}");
                        col.Item().Text($"{vm.TrimesterLabel}");
                    });

                    page.Content().Column(col =>
                    {
                        col.Spacing(12);

                        col.Item().Text(
                            $"{vm.AchievedCount} of {vm.TotalLOCount} Learning Outcomes Achieved")
                            .Bold();

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(60);
                                columns.RelativeColumn(3);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(80);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("LO").Bold();
                                header.Cell().Element(CellStyle).Text("Outcome").Bold();
                                header.Cell().Element(CellStyle).Text("Score").Bold();
                                header.Cell().Element(CellStyle).Text("Max").Bold();
                                header.Cell().Element(CellStyle).Text("%").Bold();
                                header.Cell().Element(CellStyle).Text("Status").Bold();
                            });

                            foreach (var lo in vm.LOSummaries)
                            {
                                table.Cell().Element(CellStyle).Text(lo.Label);
                                table.Cell().Element(CellStyle).Text(lo.LearningOutcomeText);
                                table.Cell().Element(CellStyle).Text(lo.AchievedScore.ToString("0.00"));
                                table.Cell().Element(CellStyle).Text(lo.MaxScore.ToString("0.00"));
                                table.Cell().Element(CellStyle).Text(lo.Percentage.ToString("0.##") + "%");
                                table.Cell().Element(CellStyle).Text(lo.Status);
                            }
                        });

                        col.Item().Text("Learning Outcome Analysis")
                            .Bold().FontSize(12);

                        foreach (var item in vm.LOAnalyses)
                        {
                            col.Item().Text($"{item.Label} - {item.Percentage:0.##}% ({item.Status})")
                                .Bold();

                            col.Item().Text(item.LearningOutcomeText);

                            foreach (var line in item.AssessmentBreakdown)
                            {
                                col.Item().Text("- " + line);
                            }
                        }

                        col.Item().Text("Assessment Contribution to Course Learning Outcomes")
                            .Bold().FontSize(12);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.ConstantColumn(80);
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(3);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Assessment").Bold();
                                header.Cell().Element(CellStyle).Text("Status").Bold();
                                header.Cell().Element(CellStyle).Text("Contribution").Bold();
                                header.Cell().Element(CellStyle).Text("Achievement").Bold();
                            });

                            foreach (var item in vm.Contributions)
                            {
                                table.Cell().Element(CellStyle).Text(item.AssessmentName);
                                table.Cell().Element(CellStyle).Text(item.StatusText);
                                table.Cell().Element(CellStyle).Column(col =>
                                {
                                    foreach (var c in item.Contributions)
                                        col.Item().Text(c);
                                });
                                table.Cell().Element(CellStyle).Column(col =>
                                {
                                    foreach (var a in item.Achievements)
                                        col.Item().Text(a);
                                });
                            }
                        });
                    });

                    page.Footer()
                        .AlignCenter()
                        .Text("Generated from AIS LO System");
                });
            }).GeneratePdf();

            var directory = Path.Combine(_env.WebRootPath, "uploads", "reports", "student-lo");
            Directory.CreateDirectory(directory);

            var student = await _context.Students.FindAsync(studentId);
            var filename = $"{student?.StudentId}_{courseCode}_{year}_T{trimester}_StudentLOReport.pdf";
            var fullPath = Path.Combine(directory, filename);
            await System.IO.File.WriteAllBytesAsync(fullPath, pdfBytes);

            // Auto-submit to moderator
            var course = await _context.Courses.FirstOrDefaultAsync(c =>
                c.Code == courseCode && c.Year == year && c.Trimester == trimester);

            var existingSubmission = await _submissions.GetLatestAsync(
                courseCode, year, trimester, SubmissionItemType.StudentLOReport, studentId);

            int.TryParse(User.FindFirst("UserId")?.Value, out int userId);
            if (course?.ModeratorId != null &&
                userId > 0 &&
                (existingSubmission == null || existingSubmission.Status == SubmissionStatus.Denied))
            {
                await _submissions.SubmitAsync(
                    courseCode, year, trimester,
                    SubmissionItemType.StudentLOReport,
                    studentId, // ItemRefId = studentId
                    $"Student LO Report — {student?.StudentId} ({student?.FullName})",
                    userId);
            }

            return File(pdfBytes, "application/pdf", $"{student?.StudentId}_{courseCode}_{year}_T{trimester}_StudentLOReport.pdf");

            static IContainer CellStyle(IContainer container)
            {
                return container
                    .Border(1)
                    .BorderColor(Colors.Grey.Lighten2)
                    .Padding(6);
            }
        }

        private async Task<StudentCourseLOOverviewViewModel?> BuildStudentCourseOverviewViewModel(
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
                return null;

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == studentId);

            if (student == null)
                return null;

            var isEnrolled = await _context.StudentCourseEnrolments
                .AnyAsync(e => e.CourseId == course.Id && e.StudentId == studentId);

            if (!isEnrolled)
                return null;

            var assignments = await _context.Assignments
                .Where(a =>
                    a.CourseCode == courseCode &&
                    a.Year == year &&
                    a.Trimester == trimester)
                .OrderBy(a => a.AssessmentName)
                .ToListAsync();

            var assessmentStatuses = new List<StudentAssessmentStatusItemViewModel>();
            var gradedAssignments = new List<Assignment>();

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
                    m.LearningOutcome != null &&
                    m.RubricCriterion != null &&
                    m.RubricCriterion.Rubric != null &&
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
                    var rubricAssignmentId = mapping.RubricCriterion?.Rubric?.AssignmentId;
                    if (rubricAssignmentId == null)
                        continue;

                    var saved = savedMarks.FirstOrDefault(x =>
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
                            m.RubricCriterion?.Rubric?.AssignmentId == assignment.Id)
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
                                m.RubricCriterion?.Rubric?.AssignmentId == assignment.Id)
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

            return vm;
        }
    }
}