using AIS_LO_System.Data;
using AIS_LO_System.Models;
using AIS_LO_System.Models.Reports;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AIS_LO_System.Services
{
    /// <summary>
    /// Service for generating report PDFs and submitting them to moderators
    /// </summary>
    public class ReportSubmissionService
    {
        private readonly ApplicationDbContext _context;
        private readonly SubmissionService _submissions;
        private readonly IWebHostEnvironment _env;

        public ReportSubmissionService(
            ApplicationDbContext context,
            SubmissionService submissions,
            IWebHostEnvironment env)
        {
            _context = context;
            _submissions = submissions;
            _env = env;
        }

        /// <summary>
        /// Generate PDF for Student LO Report (all assessments) and submit to moderator
        /// </summary>
        public async Task<bool> SubmitStudentLOReportAsync(
            string courseCode,
            int year,
            int trimester,
            int userId)
        {
            try
            {
                // Check if course has moderator
                var course = await _context.Courses.FirstOrDefaultAsync(c =>
                    c.Code == courseCode && c.Year == year && c.Trimester == trimester);

                if (course?.ModeratorId == null)
                    return false;

                // Generate placeholder PDF (in reality, you'd generate the actual report)
                var pdfPath = await GenerateStudentLOReportPdfAsync(courseCode, year, trimester);

                if (string.IsNullOrEmpty(pdfPath))
                    return false;

                // Submit to moderator
                await _submissions.SubmitAsync(
                    courseCode, year, trimester,
                    SubmissionItemType.StudentLOReport,
                    null, // No ItemRefId for course-level report
                    $"Student LO Report — {courseCode} {year} T{trimester}",
                    userId);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generate PDF for Course LO Report and submit to moderator
        /// </summary>
        public async Task<bool> SubmitCourseLOReportAsync(
            string courseCode,
            int year,
            int trimester,
            CourseLOReportViewModel vm,
            int userId)
        {
            try
            {
                // Check if course has moderator
                var course = await _context.Courses.FirstOrDefaultAsync(c =>
                    c.Code == courseCode && c.Year == year && c.Trimester == trimester);

                if (course?.ModeratorId == null)
                    return false;

                // Generate and save PDF
                var pdfPath = await GenerateCourseLOReportPdfAsync(vm);

                if (string.IsNullOrEmpty(pdfPath))
                    return false;

                // Submit to moderator
                await _submissions.SubmitAsync(
                    courseCode, year, trimester,
                    SubmissionItemType.CourseLOReport,
                    null, // No ItemRefId for course-level report
                    $"Course LO Report — {courseCode} {year} T{trimester}",
                    userId);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generate PDF for Assessment LO Report and submit to moderator
        /// </summary>
        public async Task<bool> SubmitAssessmentLOReportAsync(
            string courseCode,
            int year,
            int trimester,
            int assignmentId,
            string assessmentName,
            int userId)
        {
            try
            {
                // Check if course has moderator
                var course = await _context.Courses.FirstOrDefaultAsync(c =>
                    c.Code == courseCode && c.Year == year && c.Trimester == trimester);

                if (course?.ModeratorId == null)
                    return false;

                // Generate and save PDF
                var pdfPath = await GenerateAssessmentLOReportPdfAsync(
                    courseCode, year, trimester, assignmentId, assessmentName);

                if (string.IsNullOrEmpty(pdfPath))
                    return false;

                // Submit to moderator
                await _submissions.SubmitAsync(
                    courseCode, year, trimester,
                    SubmissionItemType.AssessmentLOReport,
                    assignmentId, // ItemRefId references the assignment
                    $"{assessmentName} — Assessment LO Report",
                    userId);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ======================================================
        // PRIVATE PDF GENERATION METHODS
        // ======================================================

        private async Task<string?> GenerateStudentLOReportPdfAsync(
            string courseCode,
            int year,
            int trimester)
        {
            // This would contain the actual PDF generation logic
            // For now, returning a placeholder path
            var directory = Path.Combine(_env.WebRootPath, "uploads", "reports", "student-lo");
            Directory.CreateDirectory(directory);

            var filename = $"{courseCode}_{year}_T{trimester}_StudentLOReport.pdf";
            var fullPath = Path.Combine(directory, filename);

            return await Task.FromResult($"/uploads/reports/student-lo/{filename}");
        }

        private async Task<string?> GenerateCourseLOReportPdfAsync(CourseLOReportViewModel vm)
        {
            try
            {
                var directory = Path.Combine(_env.WebRootPath, "uploads", "reports", "course-lo");
                Directory.CreateDirectory(directory);

                var filename = $"{vm.CourseCode}_{vm.Year}_T{vm.Trimester}_CourseLOReport.pdf";
                var fullPath = Path.Combine(directory, filename);

                // Generate PDF using QuestPDF (same logic as DownloadCourseLOReportPdf)
                var pdfBytes = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(30);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Header().Column(col =>
                        {
                            col.Item().Text("Course LO Report").FontSize(18).Bold();
                            col.Item().Text($"{vm.CourseCode} - {vm.CourseTitle}");
                            col.Item().Text(vm.TrimesterLabel);
                            col.Item().Text($"Students Enrolled: {vm.TotalStudentsEnrolled}");
                        });

                        page.Content().Column(col =>
                        {
                            col.Spacing(12);
                            col.Item().Text($"{vm.TotalAchievedLOs} of {vm.LOSummaries.Count} Learning Outcomes Achieved by Class").Bold();

                        });
                    });
                }).GeneratePdf();

                await File.WriteAllBytesAsync(fullPath, pdfBytes);
                return $"/uploads/reports/course-lo/{filename}";
            }
            catch
            {
                return null;
            }
        }

        private async Task<string?> GenerateAssessmentLOReportPdfAsync(
            string courseCode,
            int year,
            int trimester,
            int assignmentId,
            string assessmentName)
        {
            try
            {
                var directory = Path.Combine(_env.WebRootPath, "uploads", "reports", "assessment-lo");
                Directory.CreateDirectory(directory);

                var safeAssessmentName = assessmentName.Replace(" ", "_");
                var filename = $"{courseCode}_{year}_T{trimester}_{safeAssessmentName}_AssessmentLOReport.pdf";
                var fullPath = Path.Combine(directory, filename);

                return await Task.FromResult($"/uploads/reports/assessment-lo/{filename}");
            }
            catch
            {
                return null;
            }
        }
    }
}