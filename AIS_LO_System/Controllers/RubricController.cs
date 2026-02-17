using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System.IO;

namespace AIS_LO_System.Controllers
{
    public class RubricController : Controller
    {
        private readonly ApplicationDbContext _context;

        public RubricController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ======================================================
        // LOAD RUBRIC MANAGEMENT PAGE
        // ======================================================
        public async Task<IActionResult> Index(
    int? assignmentId,
    string assessmentName,
    string courseCode,
    string courseTitle,
    int year,
    int trimester)
        {
            if (assignmentId == null)
                return BadRequest("AssignmentId is required.");

            ViewBag.AssignmentId = assignmentId;
            ViewBag.AssessmentName = assessmentName;
            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = courseTitle;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                .ThenInclude(c => c.Levels)
                .FirstOrDefaultAsync(r => r.AssignmentId == assignmentId);

            // Order levels by score descending for each criterion
            if (rubric != null)
            {
                foreach (var criterion in rubric.Criteria)
                {
                    criterion.Levels = criterion.Levels.OrderByDescending(l => l.Score).ToList();
                }
            }

            return View(rubric);
        }

        // ======================================================
        // UPLOAD RUBRIC EXCEL
        // ======================================================
        [HttpPost]
        public async Task<IActionResult> UploadRubricExcel(
    IFormFile file,
    int assignmentId,
    string assessmentName,
    string courseCode,
    string courseTitle,
    int year,
    int trimester)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return RedirectToAction("Index", new
                {
                    assignmentId,
                    assessmentName,
                    courseCode,
                    courseTitle,
                    year,
                    trimester
                });
            }

            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);

                using var package = new ExcelPackage(stream);
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();

                if (worksheet == null)
                {
                    TempData["Error"] = "Invalid Excel file.";
                    return RedirectToAction("Index", new { assignmentId, assessmentName, courseCode, courseTitle, year, trimester });
                }

                // Validate Assignment
                var assignmentExists = await _context.Assignments
                    .AnyAsync(a => a.Id == assignmentId);

                if (!assignmentExists)
                {
                    TempData["Error"] = "Assignment not found.";
                    return RedirectToAction("Index", new { assignmentId, assessmentName, courseCode, courseTitle, year, trimester });
                }

                // Remove existing rubric
                var existingRubric = await _context.Rubrics
                    .Include(r => r.Criteria)
                    .ThenInclude(c => c.Levels)
                    .FirstOrDefaultAsync(r => r.AssignmentId == assignmentId);

                if (existingRubric != null)
                {
                    _context.Rubrics.Remove(existingRubric);
                    await _context.SaveChangesAsync();
                }

                // Read scale names from row 2 (columns 2-6)
                var scaleNames = new string[5];
                for (int col = 2; col <= 6; col++)
                {
                    scaleNames[col - 2] = worksheet.Cells[2, col].Value?.ToString()?.Trim() ?? $"Level {6 - col}";
                }

                // Create new rubric
                var rubric = new Rubric
                {
                    AssignmentId = assignmentId,
                    CreatedDate = DateTime.Now,
                    Criteria = new List<RubricCriterion>()
                };

                // Start reading criteria from row 3 (after both header rows)
                int row = 3;

                while (worksheet.Cells[row, 1].Value != null)
                {
                    var criterionName = worksheet.Cells[row, 1].Value?.ToString()?.Trim();

                    if (string.IsNullOrWhiteSpace(criterionName))
                    {
                        row++;
                        continue;
                    }

                    var criterion = new RubricCriterion
                    {
                        CriterionName = criterionName,
                        Levels = new List<RubricLevel>()
                    };

                    // Columns 2–6 = Levels (Score 4,3,2,1,0)
                    for (int col = 2; col <= 6; col++)
                    {
                        var description = worksheet.Cells[row, col].Value?.ToString()?.Trim() ?? string.Empty;

                        var level = new RubricLevel
                        {
                            Score = 6 - col,  // 4, 3, 2, 1, 0
                            ScaleName = scaleNames[col - 2],  // Excellent, Good, Satisfactory, Limited, Unsatisfactory
                            Description = description
                        };

                        criterion.Levels.Add(level);
                    }

                    rubric.Criteria.Add(criterion);
                    row++;
                }

                if (!rubric.Criteria.Any())
                {
                    TempData["Error"] = "No valid criteria found in the Excel file. Please check the format.";
                    return RedirectToAction("Index", new { assignmentId, assessmentName, courseCode, courseTitle, year, trimester });
                }

                _context.Rubrics.Add(rubric);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Rubric uploaded successfully! {rubric.Criteria.Count} criteria imported.";
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
                TempData["Error"] = $"Error uploading rubric: {ex.Message}";
                return RedirectToAction("Index", new { assignmentId, assessmentName, courseCode, courseTitle, year, trimester });
            }
        }

        // ======================================================
        // PREVIEW RUBRIC
        // ======================================================
        public async Task<IActionResult> Preview(int id)
        {
            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                .ThenInclude(c => c.Levels)
                .Include(r => r.Assignment)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rubric == null)
                return NotFound();

            return View(rubric);
        }

        // ======================================================
        // EDIT RUBRIC (GET)
        // ======================================================
        public async Task<IActionResult> Edit(int id)
        {
            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                .ThenInclude(c => c.Levels)
                .Include(r => r.Assignment)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rubric == null)
                return NotFound();

            if (rubric.Assignment != null)
            {
                ViewBag.AssignmentId = rubric.Assignment.Id;
                ViewBag.AssessmentName = rubric.Assignment.AssessmentName;
                ViewBag.CourseCode = rubric.Assignment.CourseCode;
                ViewBag.CourseTitle = rubric.Assignment.CourseTitle;
                ViewBag.Year = rubric.Assignment.Year;
                ViewBag.Trimester = rubric.Assignment.Trimester;
            }

            return View(rubric);
        }

        // ======================================================
        // EDIT RUBRIC (POST)
        // ======================================================
        [HttpPost]
        [ActionName("Edit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPost(Rubric model, int assignmentId, List<RubricLevel> performanceLevels)
        {
            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                .ThenInclude(c => c.Levels)
                .FirstOrDefaultAsync(r => r.Id == model.Id);

            if (rubric == null)
                return NotFound();

            // Update criterion names and level scores
            foreach (var criterion in rubric.Criteria)
            {
                var updatedCriterion = model.Criteria
                    .FirstOrDefault(c => c.Id == criterion.Id);

                if (updatedCriterion != null)
                {
                    criterion.CriterionName = updatedCriterion.CriterionName;

                    foreach (var level in criterion.Levels)
                    {
                        var updatedLevel = updatedCriterion.Levels
                            .FirstOrDefault(l => l.Id == level.Id);

                        if (updatedLevel != null)
                        {
                            level.Score = updatedLevel.Score;
                        }
                    }
                }
            }

            // Update ScaleNames across ALL criteria using submitted PerformanceLevels
            if (performanceLevels != null && performanceLevels.Any())
            {
                // Build a score -> scaleName lookup from the submitted performance levels
                // They come in ordered highest to lowest (4,3,2,1,0)
                int[] scores = { 4, 3, 2, 1, 0 };

                for (int i = 0; i < performanceLevels.Count; i++)
                {
                    var newScaleName = performanceLevels[i].ScaleName;
                    var score = scores[i];

                    // Update this scale name for every criterion at this score
                    foreach (var criterion in rubric.Criteria)
                    {
                        var levelToUpdate = criterion.Levels
                            .FirstOrDefault(l => l.Score == score);

                        if (levelToUpdate != null && !string.IsNullOrEmpty(newScaleName))
                        {
                            levelToUpdate.ScaleName = newScaleName;
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Rubric updated successfully!";

            var assignment = await _context.Assignments.FindAsync(assignmentId);

            if (assignment == null)
                return RedirectToAction(nameof(Index), new { assignmentId });

            return RedirectToAction(nameof(Index), new
            {
                assignmentId = assignment.Id,
                assessmentName = assignment.AssessmentName,
                courseCode = assignment.CourseCode,
                courseTitle = assignment.CourseTitle,
                year = assignment.Year,
                trimester = assignment.Trimester
            });
        }
    }
}
