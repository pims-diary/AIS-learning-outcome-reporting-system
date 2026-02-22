using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIS_LO_System.Controllers
{
    public class LOMappingController : Controller
    {
        private readonly ApplicationDbContext _context;

        public LOMappingController(ApplicationDbContext context)
        {
            _context = context;
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

            // Pass context to view
            ViewBag.AssignmentId = assignmentId;
            ViewBag.AssessmentName = assessmentName;
            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = courseTitle;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;

            // Get the assignment to check selected LOs
            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

            // Get the rubric with criteria
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

            // ✅ UPDATED: Get LOs from database (synced from Epic 4)
            var learningOutcomes = await _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToListAsync();

            // ✅ UPDATED: If no LOs exist, show error - teacher must add them in Epic 4!
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

            // Get selected LO IDs from assignment
            List<int> selectedLOIds = new List<int>();
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
                // If no selection saved, select all by default
                selectedLOIds = learningOutcomes.Select(lo => lo.Id).ToList();
            }

            // Create view model
            var viewModel = new LOMappingViewModel
            {
                Rubric = rubric,
                LearningOutcomes = learningOutcomes,
                AssignmentId = assignmentId,
                SelectedLOIds = selectedLOIds
            };

            return View(viewModel);
        }

        // ======================================================
        // SAVE LO MAPPINGS
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
                // Get the assignment
                var assignment = await _context.Assignments
                    .FirstOrDefaultAsync(a => a.Id == assignmentId);

                if (assignment != null)
                {
                    // Save selected LO IDs
                    if (selectedLOIds != null && selectedLOIds.Any())
                    {
                        assignment.SelectedLearningOutcomeIds = string.Join(",", selectedLOIds);
                    }
                    else
                    {
                        assignment.SelectedLearningOutcomeIds = null;
                    }
                }

                // Get the rubric
                var rubric = await _context.Rubrics
                    .Include(r => r.Criteria)
                        .ThenInclude(c => c.LOMappings)
                    .FirstOrDefaultAsync(r => r.AssignmentId == assignmentId);

                if (rubric == null)
                    return NotFound("Rubric not found.");

                // Remove all existing mappings for this rubric's criteria
                var existingMappings = rubric.Criteria
                    .SelectMany(c => c.LOMappings)
                    .ToList();

                _context.CriterionLOMappings.RemoveRange(existingMappings);

                // Add new mappings
                if (mappings != null && mappings.Any())
                {
                    foreach (var input in mappings)
                    {
                        // Validate criterion exists
                        var criterion = rubric.Criteria.FirstOrDefault(c => c.Id == input.CriterionId);
                        if (criterion == null) continue;

                        // Validate weight
                        if (input.Weight <= 0) continue;

                        // Add mappings for each selected LO
                        if (input.SelectedLOIds != null && input.SelectedLOIds.Any())
                        {
                            foreach (var loId in input.SelectedLOIds)
                            {
                                var mapping = new CriterionLOMapping
                                {
                                    RubricCriterionId = input.CriterionId,
                                    LearningOutcomeId = loId,
                                    Weight = input.Weight
                                };

                                _context.CriterionLOMappings.Add(mapping);
                            }
                        }
                    }
                }

                await _context.SaveChangesAsync();

                TempData["Success"] = "LO mappings and weights saved successfully!";
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
    }

    public class MappingInput
    {
        public int CriterionId { get; set; }
        public List<int> SelectedLOIds { get; set; } = new();
        public decimal Weight { get; set; }
    }
}
