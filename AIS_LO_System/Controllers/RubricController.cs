using AIS_LO_System.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System.Linq;
using System.IO;
using AIS_LO_System.Data;

namespace AIS_LO_System.Controllers
{
    public class RubricController : Controller
    {
        private readonly ApplicationDbContext _context;

        public RubricController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ================================
        // LOAD RUBRIC MANAGEMENT PAGE
        // ================================
        public IActionResult Index(
            int? assignmentId,
            string assessmentName,
            string courseCode,
            string courseTitle,
            int year,
            int trimester)
        {
            ViewBag.AssignmentId = assignmentId;
            ViewBag.AssessmentName = assessmentName;
            ViewBag.CourseCode = courseCode;
            ViewBag.CourseTitle = courseTitle;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;

            var rubric = _context.Rubrics
                .Include(r => r.Criteria)
                .ThenInclude(c => c.Levels)
                .FirstOrDefault(r => r.AssignmentId == assignmentId);

            if (assignmentId == null)
            {
                return Content("AssignmentId missing in route.");
            }

            return View(rubric);
        }

        // ================================
        // UPLOAD RUBRIC EXCEL
        // ================================
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
                return RedirectToAction("Index", new
                {
                    assignmentId,
                    assessmentName,
                    courseCode,
                    courseTitle,
                    year,
                    trimester
                });

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);

            using var package = new ExcelPackage(stream);
            var worksheet = package.Workbook.Worksheets[0];

            // ================================
            // GET ASSIGNMENT
            // ================================
            var assignment = _context.Assignments
                .FirstOrDefault(a => a.Id == assignmentId);

            if (assignment == null)
                return RedirectToAction("Index");

            // ================================
            // CHECK IF RUBRIC EXISTS
            // ================================
            var rubric = _context.Rubrics
                .Include(r => r.Criteria)
                .ThenInclude(c => c.Levels)
                .FirstOrDefault(r => r.AssignmentId == assignmentId);

            if (rubric != null)
            {
                // delete old levels
                var oldLevels = rubric.Criteria
                    .SelectMany(c => c.Levels);

                _context.RubricLevels.RemoveRange(oldLevels);

                // delete criteria
                _context.RubricCriteria.RemoveRange(rubric.Criteria);

                await _context.SaveChangesAsync();
            }
            else
            {
                rubric = new Rubric
                {
                    AssignmentId = assignmentId,
                    CreatedDate = DateTime.Now
                };

                _context.Rubrics.Add(rubric);
                await _context.SaveChangesAsync();
            }

            // ================================
            // EXTRACT EXCEL DATA
            // ================================
            int row = 2;

            while (worksheet.Cells[row, 1].Value != null)
            {
                var criterion = new RubricCriterion
                {
                    RubricId = rubric.Id,
                    CriterionName = worksheet.Cells[row, 1].Text
                };

                _context.RubricCriteria.Add(criterion);
                await _context.SaveChangesAsync();

                for (int col = 2; col <= 6; col++)
                {
                    var level = new RubricLevel
                    {
                        RubricCriterionId = criterion.Id,
                        Score = 6 - col,
                        Description = worksheet.Cells[row, col].Text
                    };

                    _context.RubricLevels.Add(level);
                }

                row++;
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Preview", new { id = rubric.Id });
        }

        // ================================
        // RUBRIC PREVIEW
        // ================================
        public IActionResult Preview(int id)
        {
            var rubric = _context.Rubrics
                .Include(r => r.Criteria)
                .ThenInclude(c => c.Levels)
                .Include(r => r.Assignment)
                .FirstOrDefault(r => r.Id == id);

            if (rubric == null)
                return RedirectToAction("Index");

            return View(rubric);
        }

        public IActionResult Edit(int id)
        {
            var rubric = _context.Rubrics
                .Include(r => r.Criteria)
                .ThenInclude(c => c.Levels)
                .FirstOrDefault(r => r.Id == id);

            if (rubric == null)
                return RedirectToAction("Index");

            return View(rubric);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Rubric rubric)
        {
            foreach (var criterion in rubric.Criteria)
            {
                foreach (var level in criterion.Levels)
                {
                    _context.RubricLevels.Update(level);
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Preview", new { id = rubric.Id });
        }


    }
}