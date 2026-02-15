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

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);

            using var package = new ExcelPackage(stream);
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
                return BadRequest("Invalid Excel file.");

            // Validate Assignment
            var assignmentExists = await _context.Assignments
                .AnyAsync(a => a.Id == assignmentId);

            if (!assignmentExists)
                return NotFound("Assignment not found.");

            // Remove existing rubric (full clean replace)
            var existingRubric = await _context.Rubrics
                .Include(r => r.Criteria)
                .ThenInclude(c => c.Levels)
                .FirstOrDefaultAsync(r => r.AssignmentId == assignmentId);

            if (existingRubric != null)
            {
                _context.Rubrics.Remove(existingRubric);
                await _context.SaveChangesAsync();
            }

            // Create new rubric
            var rubric = new Rubric
            {
                AssignmentId = assignmentId,
                CreatedDate = DateTime.Now,
                Criteria = new List<RubricCriterion>()
            };

            int row = 2;

            while (worksheet.Cells[row, 1].Value != null)
            {
                var criterion = new RubricCriterion
                {
                    CriterionName = worksheet.Cells[row, 1].Text,
                    Levels = new List<RubricLevel>()
                };

                // Columns 2–6 = Levels
                for (int col = 2; col <= 6; col++)
                {
                    var level = new RubricLevel
                    {
                        Score = 6 - col, // adjust if needed
                        Description = worksheet.Cells[row, col].Text
                    };

                    criterion.Levels.Add(level);
                }

                rubric.Criteria.Add(criterion);
                row++;
            }

            _context.Rubrics.Add(rubric);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Preview), new { id = rubric.Id });
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
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rubric == null)
                return NotFound();

            return View(rubric);
        }

        // ======================================================
        // EDIT RUBRIC (POST)
        // ======================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Rubric model)
        {
            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                .ThenInclude(c => c.Levels)
                .FirstOrDefaultAsync(r => r.Id == model.Id);

            if (rubric == null)
                return NotFound();

            // Update descriptions only (safe update)
            foreach (var criterion in rubric.Criteria)
            {
                var updatedCriterion = model.Criteria
                    .FirstOrDefault(c => c.Id == criterion.Id);

                if (updatedCriterion == null)
                    continue;

                foreach (var level in criterion.Levels)
                {
                    var updatedLevel = updatedCriterion.Levels
                        .FirstOrDefault(l => l.Id == level.Id);

                    if (updatedLevel != null)
                    {
                        level.Description = updatedLevel.Description;
                    }
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Preview), new { id = rubric.Id });
        }
    }
}
