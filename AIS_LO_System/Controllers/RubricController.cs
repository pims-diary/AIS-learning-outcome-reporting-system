using AIS_LO_System.Data;
using AIS_LO_System.Models;
using AIS_LO_System.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System.IO;

namespace AIS_LO_System.Controllers
{
    public class RubricController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SubmissionService _submissions;

        public RubricController(ApplicationDbContext context, SubmissionService submissions)
        {
            _context = context;
            _submissions = submissions;
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

            // Load submission status for this rubric
            ViewBag.Submission = rubric != null
                ? await _submissions.GetLatestAsync(courseCode, year, trimester, SubmissionItemType.Rubric, rubric.Id)
                : null;

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

                // Temp storage for LO/Weight data from columns 7-8
                var loWeightData = new List<(string? loText, decimal? weight)>();

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

                    // Column 7 = LO (optional, e.g. "1,2,5" or "LO1, LO2, LO5")
                    var loCell = worksheet.Cells[row, 7].Value?.ToString()?.Trim();

                    // Column 8 = Weight % (optional, e.g. "10" or "10.5")
                    decimal? weight = null;
                    var weightCell = worksheet.Cells[row, 8].Value?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(weightCell))
                    {
                        weightCell = weightCell.Replace("%", "").Trim();
                        if (decimal.TryParse(weightCell, out var w))
                            weight = w;
                    }

                    loWeightData.Add((loCell, weight));

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

                // Auto-create LO mappings if LO/Weight columns were filled in
                int mappingsCreated = 0;
                var criteriaList = rubric.Criteria.ToList();

                // Get LOs for this course from database
                var courseLOs = await _context.LearningOutcomes
                    .Where(lo => lo.CourseCode == courseCode)
                    .OrderBy(lo => lo.OrderNumber)
                    .ToListAsync();

                if (courseLOs.Any() && loWeightData.Any())
                {
                    // Get allowed LO IDs for this assignment
                    var assignment = await _context.Assignments
                        .FirstOrDefaultAsync(a => a.Id == assignmentId);

                    var allowedLOIds = new List<int>();
                    if (assignment != null && !string.IsNullOrEmpty(assignment.SelectedLearningOutcomeIds))
                    {
                        allowedLOIds = assignment.SelectedLearningOutcomeIds
                            .Split(',')
                            .Where(s => int.TryParse(s, out _))
                            .Select(int.Parse)
                            .ToList();
                    }

                    var skippedLOs = new List<int>();

                    for (int i = 0; i < criteriaList.Count && i < loWeightData.Count; i++)
                    {
                        var (loText, weight) = loWeightData[i];
                        if (string.IsNullOrWhiteSpace(loText)) continue;

                        // Parse LO numbers from text like "1,2,5" or "LO1, LO2, LO5"
                        var loNumbers = System.Text.RegularExpressions.Regex.Matches(loText, @"\d+")
                            .Select(m => int.Parse(m.Value))
                            .Distinct()
                            .ToList();

                        foreach (var loNum in loNumbers)
                        {
                            // Match by OrderNumber
                            var lo = courseLOs.FirstOrDefault(l => l.OrderNumber == loNum);
                            if (lo == null)
                            {
                                skippedLOs.Add(loNum);
                                continue;
                            }

                            // If assignment has locked LOs, check if this LO is allowed
                            if (allowedLOIds.Any() && !allowedLOIds.Contains(lo.Id))
                            {
                                skippedLOs.Add(loNum);
                                continue;
                            }

                            var mapping = new CriterionLOMapping
                            {
                                RubricCriterionId = criteriaList[i].Id,
                                LearningOutcomeId = lo.Id,
                                Weight = weight ?? 0
                            };

                            _context.CriterionLOMappings.Add(mapping);
                            mappingsCreated++;
                        }
                    }

                    if (mappingsCreated > 0)
                        await _context.SaveChangesAsync();

                    // Track skipped LOs for feedback
                    if (skippedLOs.Any())
                    {
                        var skippedList = string.Join(", ", skippedLOs.Distinct().Select(n => $"LO{n}"));
                        TempData["Error"] = $"Warning: {skippedList} found in the rubric file but not allowed for this assessment per the course outline. These were skipped.";
                    }
                }

                var successMsg = $"Rubric uploaded successfully! {rubric.Criteria.Count} criteria imported.";
                if (mappingsCreated > 0)
                    successMsg += $" {mappingsCreated} LO mapping(s) auto-applied.";

                TempData["Success"] = successMsg;

                // Auto-submit to moderator for approval
                int.TryParse(User.FindFirst("UserId")?.Value, out int uploadUserId);
                var uploadCourse = await _context.Courses.FirstOrDefaultAsync(c =>
                    c.Code == courseCode && c.Year == year && c.Trimester == trimester);
                if (uploadCourse?.ModeratorId != null && uploadUserId > 0)
                {
                    await _submissions.SubmitAsync(
                        courseCode, year, trimester,
                        SubmissionItemType.Rubric, rubric.Id,
                        $"{assessmentName} — Rubric",
                        uploadUserId);
                    TempData["Info"] = "📨 Rubric submitted to moderator for approval.";
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

            // FIX #5: Update criterion names, level scores, AND descriptions
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
                            level.Description = updatedLevel.Description ?? level.Description;  // FIX #5: Update description
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

            // Re-submit to moderator since rubric changed
            int.TryParse(User.FindFirst("UserId")?.Value, out int editUserId);
            var editCourse = await _context.Courses.FirstOrDefaultAsync(c =>
                c.Code == assignment.CourseCode && c.Year == assignment.Year && c.Trimester == assignment.Trimester);
            if (editCourse?.ModeratorId != null && editUserId > 0)
            {
                await _submissions.SubmitAsync(
                    assignment.CourseCode, assignment.Year, assignment.Trimester,
                    SubmissionItemType.Rubric, rubric.Id,
                    $"{assignment.AssessmentName} — Rubric",
                    editUserId);
                TempData["Info"] = "📨 Updated rubric submitted to moderator for approval.";
            }

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