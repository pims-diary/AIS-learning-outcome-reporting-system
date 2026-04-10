using AIS_LO_System.Data;
using AIS_LO_System.Models;
using AIS_LO_System.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIS_LO_System.Controllers
{
    public class LOMappingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SubmissionService _submissions;   // FIX #2

        public LOMappingController(ApplicationDbContext context, SubmissionService submissions)
        {
            _context = context;
            _submissions = submissions;
        }

        // ======================================================
        // LOAD LO MAPPING PAGE
        // ======================================================
        [HttpGet]
        public async Task<IActionResult> Index(
            int assignmentId,
            string assessmentName,
            string courseCode,
            string courseTitle,
            int year,
            int trimester)
        {
            if (assignmentId == 0)
                return BadRequest("Assignment ID is required.");

            ViewBag.AssignmentId = assignmentId;
            ViewBag.AssessmentName = assessmentName;
            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = courseTitle;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;

            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.LOMappings)
                        .ThenInclude(m => m.LearningOutcome)
                .FirstOrDefaultAsync(r => r.AssignmentId == assignmentId);

            if (rubric == null)
            {
                TempData["Error"] = "Please create a rubric first before mapping learning outcomes.";
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

            var learningOutcomes = await _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToListAsync();

            if (!learningOutcomes.Any())
            {
                TempData["Error"] = "Please add learning outcomes in Course Information first.";
                return RedirectToAction("LearningOutcomes", "CourseInformation", new
                {
                    courseCode,
                    year,
                    trimester
                });
            }

            if (assignment != null &&
                assignment.LOsLockedByOutline &&
                string.IsNullOrWhiteSpace(assignment.SelectedLearningOutcomeIds))
            {
                await TryRecoverAssignmentLOsFromOutlineAsync(assignment, learningOutcomes);
            }

            List<int> selectedLOIds = new();
            if (!string.IsNullOrEmpty(assignment?.SelectedLearningOutcomeIds))
            {
                selectedLOIds = assignment.SelectedLearningOutcomeIds
                    .Split(',')
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .ToList();
            }
            else
            {
                selectedLOIds = learningOutcomes.Select(lo => lo.Id).ToList();
            }

            bool losLocked = assignment?.LOsLockedByOutline ?? false;
            if (losLocked && selectedLOIds.Any())
            {
                learningOutcomes = learningOutcomes
                    .Where(lo => selectedLOIds.Contains(lo.Id))
                    .ToList();
            }

            // FIX #2: Pass existing submission status so the page can show it
            var existingSubmission = await _submissions.GetLatestAsync(
                courseCode, year, trimester, SubmissionItemType.LOMapping, assignmentId);
            ViewBag.LOMappingSubmission = existingSubmission;

            var viewModel = new LOMappingViewModel
            {
                Rubric = rubric,
                LearningOutcomes = learningOutcomes,
                AssignmentId = assignmentId,
                SelectedLOIds = selectedLOIds,
                LOsLockedByOutline = losLocked
            };

            return View(viewModel);
        }

        private async Task TryRecoverAssignmentLOsFromOutlineAsync(
            Assignment assignment,
            List<LearningOutcome> learningOutcomes)
        {
            var outlinesDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "outlines");
            var baseName = $"{assignment.CourseCode}-{assignment.Year}-T{assignment.Trimester}";

            string? outlinePath = new[] { ".docx", ".pdf" }
                .Select(ext => Path.Combine(outlinesDir, baseName + ext))
                .FirstOrDefault(System.IO.File.Exists);

            if (string.IsNullOrWhiteSpace(outlinePath))
                return;

            var assessments = DocumentService.ExtractAssessments(outlinePath);
            if (!assessments.Any())
                return;

            var matched = assessments.FirstOrDefault(a =>
                NormalizeAssessmentName(a.Title) == NormalizeAssessmentName(assignment.AssessmentName));

            if (matched == null || !matched.LONumbers.Any())
                return;

            var loIds = learningOutcomes
                .Where(lo => matched.LONumbers.Contains(lo.OrderNumber))
                .Select(lo => lo.Id)
                .ToList();

            if (!loIds.Any())
                return;

            assignment.SelectedLearningOutcomeIds = string.Join(",", loIds);
            if (matched.MarksPercentage > 0)
                assignment.MarksPercentage = matched.MarksPercentage;

            await _context.SaveChangesAsync();
        }

        private static string NormalizeAssessmentName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.ToLowerInvariant();
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-z0-9]+", " ");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        // ======================================================
        // SAVE LO MAPPINGS
        // FIX #2: After a successful save, submit the LO mapping to the moderator
        // ======================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveMappings(
            int assignmentId,
            string assessmentName,
            string courseCode,
            string courseTitle,
            int year,
            int trimester,
            List<MappingInput> mappings,
            List<int> selectedLOIds)
        {
            try
            {
                var assignment = await _context.Assignments
                    .FirstOrDefaultAsync(a => a.Id == assignmentId);

                if (assignment != null)
                {
                    if (!assignment.LOsLockedByOutline)
                    {
                        if (selectedLOIds != null && selectedLOIds.Any())
                            assignment.SelectedLearningOutcomeIds = string.Join(",", selectedLOIds);
                        else
                            assignment.SelectedLearningOutcomeIds = null;
                    }
                }

                var rubric = await _context.Rubrics
                    .Include(r => r.Criteria)
                        .ThenInclude(c => c.LOMappings)
                    .FirstOrDefaultAsync(r => r.AssignmentId == assignmentId);

                if (rubric == null)
                    return NotFound("Rubric not found.");

                // Server-side validation — every criterion must have LO + weight
                if (mappings != null)
                {
                    var errors = new List<string>();

                    foreach (var input in mappings)
                    {
                        var criterion = rubric.Criteria.FirstOrDefault(c => c.Id == input.CriterionId);
                        var name = criterion?.CriterionName ?? "Unknown";

                        if (input.Weight <= 0)
                            errors.Add($"\"{name}\" has no weight assigned.");

                        if (input.SelectedLOIds == null || !input.SelectedLOIds.Any())
                            errors.Add($"\"{name}\" is not mapped to any Learning Outcome.");
                    }

                    var totalWeight = mappings.Sum(m => m.Weight);
                    if (Math.Abs(totalWeight - 100) > 0.01m)
                        errors.Add($"Total weight is {totalWeight:F0}% — it must equal 100%.");

                    if (errors.Any())
                    {
                        TempData["Error"] = "Cannot save: " + string.Join(" ", errors);
                        return RedirectToAction(nameof(Index), new
                        {
                            assignmentId,
                            assessmentName,
                            courseCode,
                            courseTitle,
                            year,
                            trimester
                        });
                    }
                }

                // Remove old mappings
                var existingMappings = rubric.Criteria.SelectMany(c => c.LOMappings).ToList();
                _context.CriterionLOMappings.RemoveRange(existingMappings);

                // Add new mappings
                if (mappings != null && mappings.Any())
                {
                    foreach (var input in mappings)
                    {
                        var criterion = rubric.Criteria.FirstOrDefault(c => c.Id == input.CriterionId);
                        if (criterion == null) continue;
                        if (input.Weight <= 0) continue;

                        if (input.SelectedLOIds != null && input.SelectedLOIds.Any())
                        {
                            foreach (var loId in input.SelectedLOIds)
                            {
                                _context.CriterionLOMappings.Add(new CriterionLOMapping
                                {
                                    RubricCriterionId = input.CriterionId,
                                    LearningOutcomeId = loId,
                                    Weight = input.Weight
                                });
                            }
                        }
                    }
                }

                await _context.SaveChangesAsync();

                TempData["Success"] = "LO mappings and weights saved successfully!";

                // FIX #2: Submit the LO mapping to the moderator for approval
                int.TryParse(User.FindFirst("UserId")?.Value, out int userId);
                var course = await _context.Courses.FirstOrDefaultAsync(c =>
                    c.Code == courseCode && c.Year == year && c.Trimester == trimester);

                if (course?.ModeratorId != null && userId > 0)
                {
                    await _submissions.SubmitAsync(
                        courseCode, year, trimester,
                        SubmissionItemType.LOMapping, assignmentId,
                        $"{assessmentName} — LO Mapping",
                        userId);
                    TempData["Info"] = "📨 LO mapping submitted to moderator for approval.";
                }

                return RedirectToAction(nameof(Index), new
                {
                    assignmentId,
                    assessmentName,
                    courseCode,
                    courseTitle,
                    year,
                    trimester
                });
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error saving mappings: {ex.Message}";
                return RedirectToAction(nameof(Index), new
                {
                    assignmentId,
                    assessmentName,
                    courseCode,
                    courseTitle,
                    year,
                    trimester
                });
            }
        }
    }

    // ======================================================
    // VIEW MODELS
    // ======================================================
    public class LOMappingViewModel
    {
        public Rubric Rubric { get; set; }
        public List<LearningOutcome> LearningOutcomes { get; set; }
        public int AssignmentId { get; set; }
        public List<int> SelectedLOIds { get; set; } = new();
        public bool LOsLockedByOutline { get; set; }
    }

    public class MappingInput
    {
        public int CriterionId { get; set; }
        public List<int> SelectedLOIds { get; set; } = new();
        public decimal Weight { get; set; }
    }
}