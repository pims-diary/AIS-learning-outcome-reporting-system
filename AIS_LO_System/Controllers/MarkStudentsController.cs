using AIS_LO_System.Data;
using AIS_LO_System.Models;
using AIS_LO_System.Models.MarkStudents;
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
    public class MarkStudentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SubmissionService _submissions;

        public MarkStudentsController(ApplicationDbContext context, SubmissionService submissions)
        {
            _context = context;
            _submissions = submissions;
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
            var draftLock = await BlockIfAssessmentDraftPendingAsync(courseCode, year, trimester);
            if (draftLock != null)
                return draftLock;

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

            var enrolledStudentIds = await _context.StudentCourseEnrolments
                .Where(e => e.CourseId == course.Id)
                .Select(e => e.StudentId)
                .Distinct()
                .ToListAsync();

            var query = _context.Students
                .Where(s => enrolledStudentIds.Contains(s.Id))
                .AsQueryable();

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
                .OrderBy(s => s.StudentId)
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
                return RedirectToAction("Index", "Rubric", new
                {
                    assignmentId,
                    assessmentName,
                    courseCode,
                    courseTitle,
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
                                .Select(m => m.LearningOutcome.OrderNumber))
                            : "Not Mapped",
                        AvailableLevels = c.Levels
                            .OrderByDescending(l => l.Score)
                            .Select(l => l.Score)
                            .ToList(),
                        LevelDescriptions = c.Levels
                            .OrderByDescending(l => l.Score)
                            .Select(l => new RubricLevelDisplayViewModel
                            {
                                Score = l.Score,
                                ScaleName = l.ScaleName,
                                Description = l.Description
                            })
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
        public async Task<IActionResult> LOAchievementReport(
            int studentId,
            int assignmentId,
            string courseCode,
            string courseTitle,
            string assessmentName,
            int year,
            int trimester)
        {
            var vm = await BuildLOAchievementReportViewModel(
                studentId,
                assignmentId,
                courseCode,
                courseTitle,
                assessmentName,
                year,
                trimester);

            if (vm == null)
                return NotFound();

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadLOAchievementReportPdf(
            int studentId,
            int assignmentId,
            string courseCode,
            string courseTitle,
            string assessmentName,
            int year,
            int trimester)
        {
            var vm = await BuildLOAchievementReportViewModel(
                studentId,
                assignmentId,
                courseCode,
                courseTitle,
                assessmentName,
                year,
                trimester);

            if (vm == null)
                return NotFound();

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Column(column =>
                    {
                        column.Item().Text($"LO Achievement Report - {vm.StudentName}")
                            .FontSize(18).Bold();
                        column.Item().Text($"{vm.CourseCode} {vm.CourseTitle}");
                        column.Item().Text($"Assessment: {vm.AssessmentName}");
                        column.Item().Text($"Student ID: {vm.StudentId}");
                        column.Item().Text($"Semester: {vm.Year} - Trimester {vm.Trimester}");
                    });

                    page.Content().Column(column =>
                    {
                        column.Spacing(14);

                        column.Item().Text($"Overall Result: {vm.OverallStatusText}")
                            .Bold().FontSize(12);

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(70);
                                columns.RelativeColumn(3);
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(80);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("LO").Bold();
                                header.Cell().Element(CellStyle).Text("Learning Outcome").Bold();
                                header.Cell().Element(CellStyle).Text("Achieved").Bold();
                                header.Cell().Element(CellStyle).Text("Maximum").Bold();
                                header.Cell().Element(CellStyle).Text("%").Bold();
                                header.Cell().Element(CellStyle).Text("Status").Bold();
                            });

                            foreach (var lo in vm.LearningOutcomes)
                            {
                                table.Cell().Element(CellStyle).Text(lo.Label);
                                table.Cell().Element(CellStyle).Text(lo.LearningOutcomeText);
                                table.Cell().Element(CellStyle).Text(lo.AchievedScore.ToString("0.00"));
                                table.Cell().Element(CellStyle).Text(lo.MaxScore.ToString("0.00"));
                                table.Cell().Element(CellStyle).Text(lo.Percentage.ToString("0.##") + "%");
                                table.Cell().Element(CellStyle).Text(lo.Status);
                            }
                        });

                        foreach (var lo in vm.LearningOutcomes)
                        {
                            column.Item().PaddingTop(6).Column(loColumn =>
                            {
                                loColumn.Item().Text($"{lo.Label} - {lo.Percentage:0.##}% Achieved")
                                    .Bold().FontSize(12);

                                loColumn.Item().Text(lo.LearningOutcomeText);

                                if (lo.Insights.Any())
                                {
                                    foreach (var insight in lo.Insights)
                                    {
                                        loColumn.Item().Text(
                                            $"- {insight.CriterionTitle}: Rubric Score {insight.RubricScore} ({insight.RubricLabel})");
                                    }
                                }
                                else
                                {
                                    loColumn.Item().Text("No significant weaknesses identified.");
                                }
                            });
                        }
                    });

                    page.Footer()
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.Span("Generated from AIS LO System");
                        });
                });
            }).GeneratePdf();

            var fileName = $"{vm.StudentId}_{vm.AssessmentName.Replace(" ", "_")}_LO_Report.pdf";

            return File(pdfBytes, "application/pdf", fileName);

            static IContainer CellStyle(IContainer container)
            {
                return container
                    .Border(1)
                    .BorderColor(Colors.Grey.Lighten2)
                    .Padding(6);
            }
        }

        private async Task<LOAchievementReportViewModel?> BuildLOAchievementReportViewModel(
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
                return null;

            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

            var learningOutcomes = await _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToListAsync();

            var mappings = await _context.CriterionLOMappings
                .Include(m => m.RubricCriterion)
                    .ThenInclude(c => c.Levels)
                .Include(m => m.LearningOutcome)
                .Where(m =>
                    m.LearningOutcome != null &&
                    m.LearningOutcome.CourseCode == courseCode &&
                    m.RubricCriterion != null &&
                    m.RubricCriterion.RubricId != 0)
                .ToListAsync();

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.Levels)
                .FirstOrDefaultAsync(r => r.AssignmentId == assignmentId);

            if (rubric == null)
                return null;

            var rubricCriterionIds = rubric.Criteria.Select(c => c.Id).ToList();

            var filteredMappings = mappings
                .Where(m => rubricCriterionIds.Contains(m.RubricCriterionId))
                .ToList();

            var savedMarks = await _context.StudentCriterionMarks
                .Where(x => x.AssignmentId == assignmentId && x.StudentRefId == studentId)
                .ToListAsync();

            var loItems = learningOutcomes
                .Where(lo => filteredMappings.Any(m => m.LearningOutcomeId == lo.Id))
                .Select(lo =>
                {
                    var loMappings = filteredMappings
                        .Where(m => m.LearningOutcomeId == lo.Id)
                        .ToList();

                    decimal achievedScore = 0;
                    decimal maxScore = 0;

                    var insights = new List<LOInsightItemViewModel>();

                    foreach (var mapping in loMappings)
                    {
                        var saved = savedMarks.FirstOrDefault(x => x.RubricCriterionId == mapping.RubricCriterionId);

                        if (saved != null)
                            achievedScore += saved.CalculatedScore;

                        var maxLevel = mapping.RubricCriterion?.Levels?
                            .OrderByDescending(l => l.Score)
                            .FirstOrDefault();

                        if (maxLevel != null)
                            maxScore += maxLevel.Score * mapping.Weight;

                        if (saved != null && saved.SelectedLevel <= 2)
                        {
                            string rubricLabel = saved.SelectedLevel switch
                            {
                                4 => "Excellent",
                                3 => "Good",
                                2 => "Satisfactory",
                                1 => "Limited",
                                0 => "Unsatisfactory",
                                _ => "Unknown"
                            };

                            insights.Add(new LOInsightItemViewModel
                            {
                                CriterionTitle = mapping.RubricCriterion?.CriterionName ?? "Unknown Criterion",
                                RubricScore = saved.SelectedLevel,
                                RubricLabel = rubricLabel
                            });
                        }
                    }

                    var percentage = maxScore > 0
                        ? Math.Round((achievedScore / maxScore) * 100, 2)
                        : 0;

                    string status = percentage >= 50 ? "Achieved" : "Not Achieved";

                    return new LOAchievementItemViewModel
                    {
                        LearningOutcomeId = lo.Id,
                        Label = $"LO{lo.OrderNumber}",
                        LearningOutcomeText = lo.LearningOutcomeText,
                        AchievedScore = achievedScore,
                        MaxScore = maxScore,
                        Percentage = percentage,
                        Status = status,
                        Insights = insights
                    };
                })
                .ToList();

            var achievedCount = loItems.Count(x => x.Status == "Achieved");
            var notAchievedCount = loItems.Count(x => x.Status == "Not Achieved");
            var totalLOCount = loItems.Count;

            string overallStatusText = $"{achievedCount} of {totalLOCount} Learning Outcomes Achieved";

            return new LOAchievementReportViewModel
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
                AssessmentWeight = assignment?.MarksPercentage ?? 0,
                AchievedCount = achievedCount,
                PartialCount = 0,
                NotAchievedCount = notAchievedCount,
                TotalLOCount = totalLOCount,
                OverallStatusText = overallStatusText,
                LearningOutcomes = loItems
            };
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
            var draftLock = await BlockIfAssessmentDraftPendingAsync(courseCode, year, trimester);
            if (draftLock != null)
                return draftLock;

            var student = await _context.Students.FirstOrDefaultAsync(s => s.Id == studentId);
            if (student == null)
                return NotFound();

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                .FirstOrDefaultAsync(r => r.AssignmentId == assignmentId);

            if (rubric == null)
            {
                TempData["Error"] = "Rubric not found.";
                return RedirectToAction("Index", "Rubric", new
                {
                    assignmentId,
                    assessmentName,
                    courseCode,
                    courseTitle,
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
            var draftLock = await BlockIfAssessmentDraftPendingAsync(courseCode, year, trimester);
            if (draftLock != null)
                return draftLock;

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

        private async Task<IActionResult?> BlockIfAssessmentDraftPendingAsync(string courseCode, int year, int trimester)
        {
            var sub = await _submissions.GetLatestAsync(courseCode, year, trimester, SubmissionItemType.Assessments);
            if (sub?.Status != SubmissionStatus.Pending)
                return null;

            TempData["Error"] = "Assessment changes are waiting for moderator approval. Marking is temporarily locked until the approved assessment setup goes live.";
            return RedirectToAction("Assessments", "CourseInformation", new { courseCode, year, trimester });
        }

    }

    public class CriterionSelectionInput
    {
        public int CriterionId { get; set; }
        public int SelectedLevel { get; set; }
    }
}
