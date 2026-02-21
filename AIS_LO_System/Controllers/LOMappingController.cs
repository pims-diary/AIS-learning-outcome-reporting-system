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

            // Get all learning outcomes for this course
            var learningOutcomes = await _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToListAsync();

            // If no LOs exist, create default ones
            if (!learningOutcomes.Any())
            {
                learningOutcomes = await CreateDefaultLearningOutcomes(courseCode);
            }

            // ✅ NEW: Get selected LO IDs from assignment
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
                SelectedLOIds = selectedLOIds // ✅ Pass selected LO IDs to view
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
            List<int> selectedLOIds) // ✅ NEW: Receive selected LO IDs from form
        {
            try
            {
                // Get the assignment
                var assignment = await _context.Assignments
                    .FirstOrDefaultAsync(a => a.Id == assignmentId);

                if (assignment != null)
                {
                    // ✅ NEW: Save selected LO IDs
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

        // ======================================================
        // CREATE DEFAULT LEARNING OUTCOMES
        // ======================================================
        private async Task<List<LearningOutcome>> CreateDefaultLearningOutcomes(string courseCode)
        {
            var defaultLOs = new List<LearningOutcome>
            {
                new LearningOutcome
                {
                    CourseCode = courseCode,
                    OrderNumber = 1,
                    LearningOutcomeText = "Analyse client requirements using current analysis techniques."
                },
                new LearningOutcome
                {
                    CourseCode = courseCode,
                    OrderNumber = 2,
                    LearningOutcomeText = "Identify and relate whenever required appropriate project control techniques in an industry environment."
                },
                new LearningOutcome
                {
                    CourseCode = courseCode,
                    OrderNumber = 3,
                    LearningOutcomeText = "Produce a comprehensive project plan for an industrial IT project; apply project principles and task management, resource management, risk management, project tracking, and project tools in industry environment."
                },
                new LearningOutcome
                {
                    CourseCode = courseCode,
                    OrderNumber = 4,
                    LearningOutcomeText = "Implement the industrial IT project following the appropriate project management framework and System Development Life Cycle."
                },
                new LearningOutcome
                {
                    CourseCode = courseCode,
                    OrderNumber = 5,
                    LearningOutcomeText = "Produce all relevant documentation."
                },
                new LearningOutcome
                {
                    CourseCode = courseCode,
                    OrderNumber = 6,
                    LearningOutcomeText = "Develop both IT and workplace soft-skills, including working in groups, writing formal reports, carrying out individual research and/or delivering oral presentations."
                }
            };

            _context.LearningOutcomes.AddRange(defaultLOs);
            await _context.SaveChangesAsync();

            return defaultLOs;
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
        public List<int> SelectedLOIds { get; set; } = new(); // ✅ NEW: Track which LOs are selected
    }

    public class MappingInput
    {
        public int CriterionId { get; set; }
        public List<int> SelectedLOIds { get; set; } = new();
        public decimal Weight { get; set; }
    }
}
