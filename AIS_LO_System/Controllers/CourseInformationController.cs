using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AIS_LO_System.Services;
using System.Text.Json;
using System.Linq;
using AIS_LO_System.Data;
using AIS_LO_System.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using System.Text.RegularExpressions;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class CourseInformationController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _context;
        private readonly SubmissionService _submissions;
        private readonly ModerationDraftService _moderationDrafts;

        public CourseInformationController(
            IWebHostEnvironment env,
            ApplicationDbContext context,
            SubmissionService submissions,
            ModerationDraftService moderationDrafts)
        {
            _env = env;
            _context = context;
            _submissions = submissions;
            _moderationDrafts = moderationDrafts;
        }

        private async Task<bool> AllowOutlineReupload(string courseCode, int year, int trimester)
        {
            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);
            if (!(course?.CanReuploadOutline ?? true))
                return false;

            // Block re-upload once moderator has approved the outline
            if (await _submissions.IsApprovedAsync(courseCode, year, trimester, SubmissionItemType.CourseOutline))
                return false;

            return true;
        }

        private bool AllowLOEdit(string courseCode, int year, int trimester)
        {
            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);
            return course?.CanEditLO ?? true;
        }

        private bool AllowAssignmentEdit(string courseCode, int year, int trimester)
        {
            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);
            var canEditAssignment = course?.CanEditAssignment ?? true;
            return canEditAssignment && HasAssessmentFallbackIssue(courseCode, year, trimester);
        }

        private bool HasOutlineFile(string courseCode, int year, int trimester)
        {
            var dir = Path.Combine(_env.WebRootPath, "uploads", "outlines");
            var baseName = GetOutlineBaseName(courseCode, year, trimester);

            return new[] { ".pdf", ".docx" }
                .Any(ext => System.IO.File.Exists(Path.Combine(dir, baseName + ext)));
        }

        private CourseSubmission? GetLatestAssessmentSubmission(string courseCode, int year, int trimester)
        {
            return _context.CourseSubmissions
                .Where(s => s.CourseCode == courseCode
                         && s.Year == year
                         && s.Trimester == trimester
                         && s.ItemType == SubmissionItemType.Assessments)
                .OrderByDescending(s => s.SubmittedAt)
                .FirstOrDefault();
        }

        private bool HasAssessmentFallbackIssue(string courseCode, int year, int trimester)
        {
            var latestAssessmentSubmission = GetLatestAssessmentSubmission(courseCode, year, trimester);
            if (latestAssessmentSubmission != null && latestAssessmentSubmission.Status != SubmissionStatus.Approved)
                return true;

            if (!HasOutlineFile(courseCode, year, trimester))
                return false;

            var assignments = _context.Assignments
                .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                .ToList();

            if (!assignments.Any())
                return true;

            return assignments.Sum(a => a.MarksPercentage) != 100;
        }

        private string GetAssessmentFallbackMessage(string courseCode, int year, int trimester)
        {
            var latestAssessmentSubmission = GetLatestAssessmentSubmission(courseCode, year, trimester);
            if (latestAssessmentSubmission?.Status == SubmissionStatus.Pending)
            {
                return "Assessment changes are currently waiting for moderator approval. The live assessment setup stays locked until that review is finished.";
            }

            if (!HasOutlineFile(courseCode, year, trimester))
            {
                return "Manual assessment editing is only available after a course outline is uploaded and the extracted assessment data needs review.";
            }

            var assignments = _context.Assignments
                .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                .ToList();

            if (!assignments.Any())
            {
                return "No assessments were extracted from the current course outline. You can use the Assessments page as a fallback and submit the correction for moderator approval.";
            }

            var assessmentTotal = assignments.Sum(a => a.MarksPercentage);
            if (assessmentTotal != 100)
            {
                return $"Assessment totals currently add up to {assessmentTotal}%, not 100%. You can use the Assessments page as a fallback and submit the correction for moderator approval.";
            }

            return "Manual assessment editing is only available when the uploaded course outline needs assessment correction.";
        }

        // -------------------------
        // COURSE OUTLINE (VIEW)
        // -------------------------
        [HttpGet]
        public async Task<IActionResult> Outline(string courseCode, int year, int trimester)
        {
            SetCourseContext(courseCode, year, trimester);

            ViewBag.CanReupload = await AllowOutlineReupload(courseCode, year, trimester);
            var currentAssessmentTotal = GetAssessmentTotalMarks(courseCode, year, trimester);
            var currentAssessmentCount = _context.Assignments
                .Count(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester);
            ViewBag.AssessmentTotalWarning = currentAssessmentCount > 0 && currentAssessmentTotal != 100
                ? $"Assessment totals currently add up to {currentAssessmentTotal}%, not 100%. Please review the extracted assessments or update them from the Assessments page."
                : null;

            var dir = Path.Combine(_env.WebRootPath, "uploads", "outlines");
            var baseName = GetOutlineBaseName(courseCode, year, trimester);

            string? outlineUrl = null;
            foreach (var ext in new[] { ".pdf", ".docx" })
            {
                var filePath = Path.Combine(dir, baseName + ext);
                if (System.IO.File.Exists(filePath))
                {
                    outlineUrl = $"/uploads/outlines/{baseName}{ext}";
                    break;
                }
            }

            ViewBag.OutlineUrl = outlineUrl;

            // Show current submission status
            ViewBag.Submission = await _submissions.GetLatestAsync(
                courseCode, year, trimester, SubmissionItemType.CourseOutline);

            return View();
        }

        // -------------------------
        // COURSE OUTLINE (UPLOAD) — supports PDF and Word (.docx)
        // -------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadOutline(IFormFile file, string courseCode, int year, int trimester)
        {
            if (!await AllowOutlineReupload(courseCode, year, trimester))
                return Forbid();

            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please choose a PDF or Word document.";
                return RedirectToAction(nameof(Outline), new { courseCode, year, trimester });
            }

            if (!DocumentService.IsAllowed(file.FileName))
            {
                TempData["Error"] = "Only PDF and Word documents (.docx) are supported.";
                return RedirectToAction(nameof(Outline), new { courseCode, year, trimester });
            }

            var dir = Path.Combine(_env.WebRootPath, "uploads", "outlines");
            Directory.CreateDirectory(dir);

            // Keep the original extension — do NOT rename or convert
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var baseName = GetOutlineBaseName(courseCode, year, trimester);
            var savedPath = Path.Combine(dir, baseName + ext);
            var tempPath = Path.Combine(dir, $"{Guid.NewGuid()}{ext}");

            using (var stream = System.IO.File.Create(tempPath))
                await file.CopyToAsync(stream);

            var detectedCode = TryDetectCourseCodeFromOutline(tempPath, ext);
            if (!string.IsNullOrWhiteSpace(detectedCode) &&
                !detectedCode.Equals(courseCode, StringComparison.OrdinalIgnoreCase))
            {
                System.IO.File.Delete(tempPath);
                TempData["Error"] = $"Upload rejected: this file appears to be for {detectedCode}, not {courseCode}. The current course outline and assessment links were left unchanged.";
                return RedirectToAction(nameof(Outline), new { courseCode, year, trimester });
            }

            // Delete any previous version (different extension) before saving
            foreach (var old in new[] { ".pdf", ".docx" })
            {
                var oldPath = Path.Combine(dir, baseName + old);
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            System.IO.File.Move(tempPath, savedPath);

            // Extract Learning Outcomes from the uploaded file
            try
            {
                var existingAssignmentCount = _context.Assignments
                    .Count(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester);
                var warnings = new List<string>();

                var extractedLOs = DocumentService.ExtractLearningOutcomes(savedPath);
                if (extractedLOs.Any())
                {
                    SaveLos(courseCode, year, trimester, extractedLOs);
                    SyncLosToDatabase(courseCode, extractedLOs);
                }

                // Extract Assessments from the course outline table
                var extractedAssessments = DocumentService.ExtractAssessments(savedPath);
                if (extractedAssessments.Any())
                {
                    SyncAssessmentsToDatabase(courseCode, year, trimester, extractedAssessments);
                }
                else if (existingAssignmentCount > 0)
                {
                    ClearAssignmentsForCourseOffering(courseCode, year, trimester);
                }

                // Build success message
                var parts = new List<string> { "Course outline uploaded!" };
                if (extractedLOs.Any())
                    parts.Add($"{extractedLOs.Count} Learning Outcomes auto-extracted.");
                if (extractedAssessments.Any())
                    parts.Add($"{extractedAssessments.Count} Assessment(s) auto-extracted.");
                if (!extractedLOs.Any() && !extractedAssessments.Any())
                    parts.Add("(No Learning Outcomes or Assessments found — add them manually)");

                TempData["Success"] = string.Join(" ", parts);
                if (!extractedAssessments.Any() && existingAssignmentCount > 0)
                {
                    warnings.Add("No assessments were extracted from the uploaded outline. Previous assessment links were removed, so the Assessments section is now blank until valid assessments are extracted.");
                }

                var currentAssessmentTotal = GetAssessmentTotalMarks(courseCode, year, trimester);
                var currentAssessmentCount = _context.Assignments
                    .Count(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester);
                if (currentAssessmentCount > 0 && currentAssessmentTotal != 100)
                {
                    warnings.Add($"Assessment totals currently add up to {currentAssessmentTotal}%, not 100%. Please review the extracted assessments or update them from the Assessments page.");
                }

                if (warnings.Any())
                    TempData["Error"] = string.Join(" ", warnings);
            }
            catch (Exception ex)
            {
                TempData["Success"] = "Course outline uploaded, but some outline data could not be extracted.";
                TempData["Error"] = $"Upload succeeded, but Learning Outcomes or Assessments could not be synced from this file. {ex.Message}";
            }

            // Auto-submit to moderator for approval
            int.TryParse(User.FindFirst("UserId")?.Value, out int userId);
            var course = _context.Courses.FirstOrDefault(c =>
                c.Code == courseCode && c.Year == year && c.Trimester == trimester);
            if (course?.ModeratorId != null && userId > 0)
            {
                await _submissions.SubmitAsync(
                    courseCode, year, trimester,
                    SubmissionItemType.CourseOutline, null,
                    $"Course Outline — {courseCode} {year} T{trimester}",
                    userId);
                TempData["Info"] = "📨 Submitted to moderator for approval.";
            }

            return RedirectToAction(nameof(Outline), new { courseCode, year, trimester });
        }

        // ✅ NEW: Extract Learning Outcomes from PDF
        private List<string> ExtractLearningOutcomesFromPDF(string pdfPath)
        {
            var learningOutcomes = new List<string>();

            try
            {
                using (PdfReader reader = new PdfReader(pdfPath))
                using (PdfDocument pdfDoc = new PdfDocument(reader))
                {
                    // Extract text from all pages
                    string fullText = "";
                    for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                    {
                        var page = pdfDoc.GetPage(i);
                        fullText += PdfTextExtractor.GetTextFromPage(page);
                    }

                    // Find Learning Outcomes section
                    learningOutcomes = ExtractLOsFromText(fullText);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                Console.WriteLine($"Error extracting LOs from PDF: {ex.Message}");
            }

            return learningOutcomes;
        }

        private string? TryDetectCourseCodeFromOutline(string filePath, string ext)
        {
            try
            {
                var fileText = ext == ".docx"
                    ? DocumentService.ExtractTextFromDocx(filePath)
                    : string.Empty;

                if (ext == ".pdf")
                {
                    using var pdfReader = new PdfReader(filePath);
                    using var pdfDoc = new PdfDocument(pdfReader);
                    if (pdfDoc.GetNumberOfPages() > 0)
                        fileText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(1));
                }

                if (string.IsNullOrWhiteSpace(fileText))
                    return null;

                var codeMatch = Regex.Match(
                    fileText.Substring(0, Math.Min(fileText.Length, 2000)),
                    @"\b([A-Z]{3,4}\d{3})\b");

                return codeMatch.Success ? codeMatch.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        // ✅ NEW: Parse text to extract numbered Learning Outcomes
        private List<string> ExtractLOsFromText(string text)
        {
            var learningOutcomes = new List<string>();

            try
            {
                // Find "LEARNING OUTCOMES" section
                var loSectionMatch = Regex.Match(
                    text,
                    @"LEARNING OUTCOMES.*?(?=\n[A-Z][A-Z\s]+\n|\Z)",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase
                );

                if (!loSectionMatch.Success)
                {
                    // Try alternative headers
                    loSectionMatch = Regex.Match(
                        text,
                        @"(?:Learning Outcomes|Course Learning Outcomes|Upon completion).*?(?=\n[A-Z][A-Z\s]+\n|\Z)",
                        RegexOptions.Singleline | RegexOptions.IgnoreCase
                    );
                }

                if (loSectionMatch.Success)
                {
                    string loSection = loSectionMatch.Value;

                    // Extract numbered items (1., 2., 3., etc.)
                    var numberedItems = Regex.Matches(
                        loSection,
                        @"(?:^|\n)\s*(\d+)\.\s+(.+?)(?=(?:\n\s*\d+\.|\Z))",
                        RegexOptions.Singleline
                    );

                    foreach (Match match in numberedItems)
                    {
                        if (match.Groups.Count >= 3)
                        {
                            var loText = match.Groups[2].Value.Trim();

                            // Clean up the text
                            loText = Regex.Replace(loText, @"\s+", " "); // Remove extra whitespace
                            loText = loText.Replace("\n", " ").Replace("\r", "");

                            // Skip if too short (probably not a real LO)
                            if (loText.Length > 20)
                            {
                                learningOutcomes.Add(loText);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LOs from text: {ex.Message}");
            }

            return learningOutcomes;
        }

        // -------------------------
        // LEARNING OUTCOMES (VIEW)
        // -------------------------
        [HttpGet]
        public async Task<IActionResult> LearningOutcomes(string courseCode, int year, int trimester)
        {
            SetCourseContext(courseCode, year, trimester);

            ViewBag.CanEditLO = AllowLOEdit(courseCode, year, trimester);

            var los = LoadLos(courseCode, year, trimester);
            var submission = await _submissions.GetLatestAsync(
                courseCode, year, trimester, SubmissionItemType.LearningOutcomes);

            ViewBag.LearningOutcomes = los;
            ViewBag.LOs = los;
            ViewBag.Submission = submission;
            ViewBag.PendingApprovalNote = submission?.Status == SubmissionStatus.Pending
                ? "Pending learning outcome changes are waiting for moderator approval. The current live learning outcomes stay unchanged until approval."
                : null;
            ViewBag.EditMode = false;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> EditLearningOutcomes(string courseCode, int year, int trimester)
        {
            SetCourseContext(courseCode, year, trimester);

            ViewBag.CanEditLO = AllowLOEdit(courseCode, year, trimester);

            var los = LoadLos(courseCode, year, trimester);
            var submission = await _submissions.GetLatestAsync(
                courseCode, year, trimester, SubmissionItemType.LearningOutcomes);
            if (submission != null && submission.Status != SubmissionStatus.Approved)
            {
                var draft = await _moderationDrafts.LoadLearningOutcomeDraftAsync(submission.Id);
                if (draft?.Outcomes?.Any() == true)
                    los = draft.Outcomes;
            }
            ViewBag.LearningOutcomes = los;
            ViewBag.LOs = los;
            ViewBag.Submission = submission;
            ViewBag.PendingApprovalNote = submission?.Status == SubmissionStatus.Pending
                ? "You are editing a pending learning outcome draft. These changes are not live until the moderator approves them."
                : null;
            ViewBag.EditMode = true;
            return View("LearningOutcomes");
        }

        // -------------------------
        // LEARNING OUTCOMES (SAVE)
        // -------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveLearningOutcomes(string courseCode, int year, int trimester, List<string> outcomes)
        {
            if (!AllowLOEdit(courseCode, year, trimester))
                return Forbid();

            var cleaned = (outcomes ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);

            int.TryParse(User.FindFirst("UserId")?.Value, out int userId);
            if (course?.ModeratorId == null || userId <= 0)
            {
                TempData["Error"] = "No moderator is assigned to this course yet, so learning outcome changes cannot be submitted for approval.";
                return RedirectToAction(nameof(EditLearningOutcomes), new { courseCode, year, trimester });
            }

            var submission = await _submissions.SubmitAsync(
                courseCode, year, trimester,
                SubmissionItemType.LearningOutcomes, null,
                $"Learning Outcomes - {courseCode} {year} T{trimester}",
                userId);

            await _moderationDrafts.SaveLearningOutcomeDraftAsync(
                submission.Id,
                courseCode,
                year,
                trimester,
                cleaned);

            TempData["Success"] = "Learning outcome changes were submitted to the moderator for approval. They are not live yet.";
            return RedirectToAction(nameof(LearningOutcomes), new { courseCode, year, trimester });
        }

        // -------------------------
        // Helpers
        // -------------------------
        private void SetCourseContext(string courseCode, int year, int trimester)
        {
            ViewBag.CourseCode = courseCode;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;

            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);

            ViewBag.CourseTitle = course?.Title ?? "";
        }

        private static string GetOutlineBaseName(string courseCode, int year, int trimester)
            => $"{courseCode}-{year}-T{trimester}";

        // Keep for backwards compatibility with any existing .pdf files
        private static string GetOutlineFileName(string courseCode, int year, int trimester)
            => $"{courseCode}-{year}-T{trimester}.pdf";

        private int GetAssessmentTotalMarks(string courseCode, int year, int trimester)
        {
            return _context.Assignments
                .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                .Sum(a => (int?)a.MarksPercentage) ?? 0;
        }

        private string GetLosPath(string courseCode, int year, int trimester)
        {
            var dir = Path.Combine(_env.ContentRootPath, "App_Data", "learning-outcomes");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{courseCode}-{year}-T{trimester}.json");
        }

        private List<string> LoadLos(string courseCode, int year, int trimester)
        {
            var path = GetLosPath(courseCode, year, trimester);

            if (!System.IO.File.Exists(path))
                return new List<string>();

            try
            {
                var json = System.IO.File.ReadAllText(path);
                var los = JsonSerializer.Deserialize<List<string>>(json);
                return los ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private void SaveLos(string courseCode, int year, int trimester, List<string> los)
        {
            var path = GetLosPath(courseCode, year, trimester);
            var json = JsonSerializer.Serialize(los, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(path, json);
        }

        // Sync LOs to database for Epic 8
        private void SyncLosToDatabase(string courseCode, List<string> loTexts)
        {
            var existingLOs = _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToList();

            var existingByOrder = existingLOs
                .ToDictionary(lo => lo.OrderNumber);

            int orderNumber = 1;
            foreach (var loText in loTexts)
            {
                if (existingByOrder.TryGetValue(orderNumber, out var existing))
                {
                    existing.LearningOutcomeText = loText;
                }
                else
                {
                    var lo = new LearningOutcome
                    {
                        CourseCode = courseCode,
                        LearningOutcomeText = loText,
                        OrderNumber = orderNumber++
                    };
                    _context.LearningOutcomes.Add(lo);
                    continue;
                }

                orderNumber++;
            }

            var extraLOs = existingLOs
                .Where(lo => lo.OrderNumber > loTexts.Count)
                .ToList();

            if (extraLOs.Any())
                _context.LearningOutcomes.RemoveRange(extraLOs);

            _context.SaveChanges();
        }

        // Sync extracted assessments to database
        private void SyncAssessmentsToDatabase(string courseCode, int year, int trimester,
            List<AssessmentInfo> assessments)
        {
            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);

            var courseTitle = course?.Title ?? "";

            // Get all existing assignments for this course
            var existingAssignments = _context.Assignments
                .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                .ToList();
            var originalAssignmentIds = existingAssignments
                .Select(a => a.Id)
                .ToHashSet();

            // Get existing LOs to map order numbers to IDs
            var courseLOs = _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .ToList();

            var matchedAssignmentIds = new HashSet<int>();

            foreach (var assess in assessments)
            {
                var normalizedAssessmentTitle = NormalizeAssessmentTitle(assess.Title);
                if (string.IsNullOrWhiteSpace(normalizedAssessmentTitle))
                    continue;

                // Map LO order numbers to database IDs
                var loIds = new List<int>();
                foreach (var loNum in assess.LONumbers)
                {
                    var lo = courseLOs.FirstOrDefault(l => l.OrderNumber == loNum);
                    if (lo != null) loIds.Add(lo.Id);
                }

                var selectedLOIds = loIds.Any() ? string.Join(",", loIds) : null;

                // Check if this assessment already exists
                var existing = FindBestExistingAssignmentMatch(
                    courseCode,
                    existingAssignments,
                    matchedAssignmentIds,
                    normalizedAssessmentTitle,
                    assess.MarksPercentage);

                if (existing != null)
                {
                    matchedAssignmentIds.Add(existing.Id);
                    RenameAssessmentMarksIfNeeded(courseCode, existing.AssessmentName, assess.Title);

                    // Update existing — safe, doesn't delete rubric or marks
                    existing.AssessmentName = assess.Title;
                    existing.MarksPercentage = assess.MarksPercentage;
                    existing.SelectedLearningOutcomeIds = selectedLOIds;
                    existing.LOsLockedByOutline = true;
                }
                else
                {
                    // Create new
                    var created = new Assignment
                    {
                        AssessmentName = assess.Title,
                        CourseCode = courseCode,
                        CourseTitle = courseTitle,
                        Year = year,
                        Trimester = trimester,
                        MarksPercentage = assess.MarksPercentage,
                        SelectedLearningOutcomeIds = selectedLOIds,
                        LOsLockedByOutline = true
                    };

                    _context.Assignments.Add(created);
                }
            }

            _context.SaveChanges();

            if (assessments.Any())
            {
                var assignmentsToRemove = existingAssignments
                    .Where(a => originalAssignmentIds.Contains(a.Id) && !matchedAssignmentIds.Contains(a.Id))
                    .ToList();

                foreach (var assignment in assignmentsToRemove)
                {
                    RemoveAssignmentAndRelatedData(courseCode, year, trimester, assignment);
                }

                _context.SaveChanges();
            }
        }

        private void RemoveDuplicateAssignments(string courseCode, List<Assignment> assignments)
        {
            var duplicateGroups = assignments
                .GroupBy(a => NormalizeAssessmentTitle(a.AssessmentName))
                .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1);

            foreach (var group in duplicateGroups)
            {
                var ordered = group
                    .OrderByDescending(AssignmentHasRubric)
                    .ThenByDescending(AssignmentHasFiles)
                    .ThenByDescending(a => AssignmentHasMarks(courseCode, a.AssessmentName))
                    .ThenBy(a => a.Id)
                    .ToList();

                var keeper = ordered.First();

                foreach (var duplicate in ordered.Skip(1))
                {
                    if (AssignmentHasRubric(duplicate) ||
                        AssignmentHasFiles(duplicate) ||
                        AssignmentHasMarks(courseCode, duplicate.AssessmentName))
                        continue;

                    _context.Assignments.Remove(duplicate);
                }
            }
        }

        private bool AssignmentHasRubric(Assignment assignment)
            => _context.Rubrics.Any(r => r.AssignmentId == assignment.Id);

        private bool AssignmentHasFiles(Assignment assignment)
            => _context.AssignmentFiles.Any(f => f.AssignmentId == assignment.Id);

        private bool AssignmentHasMarks(string courseCode, string assessmentName)
            => _context.StudentAssessmentMarks.Any(m =>
                m.CourseCode == courseCode && m.AssessmentName == assessmentName);

        private void RemoveAssignmentAndRelatedData(string courseCode, int year, int trimester, Assignment assignment)
        {
            var assignmentFiles = _context.AssignmentFiles
                .Where(f => f.AssignmentId == assignment.Id)
                .ToList();

            foreach (var file in assignmentFiles)
            {
                if (string.IsNullOrWhiteSpace(file.FilePath))
                    continue;

                var relativePath = file.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(_env.WebRootPath, relativePath);
                if (System.IO.File.Exists(fullPath))
                {
                    try
                    {
                        System.IO.File.Delete(fullPath);
                    }
                    catch
                    {
                    }
                }
            }

            var relatedSubmissions = _context.CourseSubmissions
                .Where(s =>
                    s.CourseCode == courseCode &&
                    s.Year == year &&
                    s.Trimester == trimester &&
                    s.ItemRefId == assignment.Id)
                .ToList();

            if (relatedSubmissions.Any())
                _context.CourseSubmissions.RemoveRange(relatedSubmissions);

            var relatedAssessmentMarks = _context.StudentAssessmentMarks
                .Where(m => m.CourseCode == courseCode && m.AssessmentName == assignment.AssessmentName)
                .ToList();

            if (relatedAssessmentMarks.Any())
                _context.StudentAssessmentMarks.RemoveRange(relatedAssessmentMarks);

            _context.Assignments.Remove(assignment);
        }

        private void ClearAssignmentsForCourseOffering(string courseCode, int year, int trimester)
        {
            var assignments = _context.Assignments
                .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                .ToList();

            foreach (var assignment in assignments)
            {
                RemoveAssignmentAndRelatedData(courseCode, year, trimester, assignment);
            }

            _context.SaveChanges();
        }

        private void RenameAssessmentMarksIfNeeded(string courseCode, string? oldName, string? newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) ||
                string.IsNullOrWhiteSpace(newName) ||
                oldName.Equals(newName, StringComparison.Ordinal))
            {
                return;
            }

            var marksToRename = _context.StudentAssessmentMarks
                .Where(m => m.CourseCode == courseCode && m.AssessmentName == oldName)
                .ToList();

            foreach (var mark in marksToRename)
            {
                mark.AssessmentName = newName;
            }
        }

        private static string NormalizeAssessmentTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var normalized = title.ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"\bassign(?:ment)?\b", "assignment");
            normalized = Regex.Replace(normalized, @"\bassessment\b", "assignment");
            normalized = Regex.Replace(normalized, @"\bmid\s+term\b", "midterm");
            normalized = Regex.Replace(normalized, @"[^a-z0-9]+", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private Assignment? FindBestExistingAssignmentMatch(
            string courseCode,
            List<Assignment> existingAssignments,
            HashSet<int> matchedAssignmentIds,
            string normalizedAssessmentTitle,
            int marksPercentage)
        {
            var unmatchedAssignments = existingAssignments
                .Where(a => !matchedAssignmentIds.Contains(a.Id))
                .ToList();

            var exactMatch = unmatchedAssignments.FirstOrDefault(a =>
                NormalizeAssessmentTitle(a.AssessmentName) == normalizedAssessmentTitle);

            if (exactMatch != null)
                return exactMatch;

            var bestCandidate = unmatchedAssignments
                .Select(a => new
                {
                    Assignment = a,
                    Similarity = GetAssessmentTitleSimilarity(
                        normalizedAssessmentTitle,
                        NormalizeAssessmentTitle(a.AssessmentName)),
                    SameMarks = a.MarksPercentage == marksPercentage,
                    HasRubric = AssignmentHasRubric(a),
                    HasFiles = AssignmentHasFiles(a),
                    HasMarks = AssignmentHasMarks(courseCode, a.AssessmentName)
                })
                .Where(x => x.Similarity >= 0.55m || (x.SameMarks && x.Similarity >= 0.40m))
                .OrderByDescending(x => x.Similarity)
                .ThenByDescending(x => x.SameMarks)
                .ThenByDescending(x => x.HasRubric)
                .ThenByDescending(x => x.HasFiles)
                .ThenByDescending(x => x.HasMarks)
                .ThenBy(x => x.Assignment.Id)
                .FirstOrDefault();

            return bestCandidate?.Assignment;
        }

        private static decimal GetAssessmentTitleSimilarity(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return 0m;

            if (left == right)
                return 1m;

            if (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal))
                return 0.9m;

            var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            if (!leftTokens.Any() || !rightTokens.Any())
                return 0m;

            var overlap = leftTokens.Intersect(rightTokens).Count();
            var union = leftTokens.Union(rightTokens).Count();

            return union == 0 ? 0m : (decimal)overlap / union;
        }

        // -------------------------
        // ASSESSMENTS (VIEW)
        // -------------------------
        [HttpGet]
        public async Task<IActionResult> Assessments(string courseCode, int year, int trimester)
        {
            SetCourseContext(courseCode, year, trimester);
            var canEditAssignment = AllowAssignmentEdit(courseCode, year, trimester);
            ViewBag.CanEditAssignment = canEditAssignment;
            ViewBag.AssessmentEditMessage = canEditAssignment
                ? "Manual assessment editing is enabled as a fallback because the current course outline assessment data needs review."
                : GetAssessmentFallbackMessage(courseCode, year, trimester);

            var assignments = _context.Assignments
                .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                .OrderBy(a => a.Id)
                .ToList();
            var submission = await _submissions.GetLatestAsync(
                courseCode, year, trimester, SubmissionItemType.Assessments);
            ViewBag.Assignments = assignments;

            ViewBag.LearningOutcomes = _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToList();
            var assessmentTotal = assignments.Sum(a => a.MarksPercentage);
            ViewBag.AssessmentTotalWarning = assignments.Any() && assessmentTotal != 100
                ? $"Assessment totals currently add up to {assessmentTotal}%, not 100%. Please review the assessments."
                : null;
            ViewBag.Submission = submission;
            ViewBag.PendingApprovalNote = submission?.Status == SubmissionStatus.Pending
                ? "Pending assessment changes are waiting for moderator approval. The current live assessments stay unchanged until approval."
                : null;
            ViewBag.EditMode = false;
            return View();
        }

        // -------------------------
        // ASSESSMENTS (EDIT)
        // -------------------------
        [HttpGet]
        public async Task<IActionResult> EditAssessments(string courseCode, int year, int trimester)
        {
            if (!AllowAssignmentEdit(courseCode, year, trimester))
            {
                TempData["Error"] = GetAssessmentFallbackMessage(courseCode, year, trimester);
                return RedirectToAction(nameof(Assessments), new { courseCode, year, trimester });
            }

            SetCourseContext(courseCode, year, trimester);
            ViewBag.CanEditAssignment = true;
            ViewBag.AssessmentEditMessage = "Manual assessment editing is enabled as a fallback because the current course outline assessment data needs review.";

            var submission = await _submissions.GetLatestAsync(
                courseCode, year, trimester, SubmissionItemType.Assessments);
            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);

            var assignments = _context.Assignments
                .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                .OrderBy(a => a.Id)
                .ToList();

            if (submission != null && submission.Status != SubmissionStatus.Approved)
            {
                var draft = await _moderationDrafts.LoadAssessmentDraftAsync(submission.Id);
                if (draft != null)
                    assignments = BuildAssignmentsFromDraft(draft, course?.Title ?? string.Empty);
            }
            ViewBag.Assignments = assignments;

            ViewBag.LearningOutcomes = _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToList();
            var assessmentTotal = assignments.Sum(a => a.MarksPercentage);
            ViewBag.AssessmentTotalWarning = assignments.Any() && assessmentTotal != 100
                ? $"Assessment totals currently add up to {assessmentTotal}%, not 100%. Please review the assessments."
                : null;
            ViewBag.Submission = submission;
            ViewBag.PendingApprovalNote = submission?.Status == SubmissionStatus.Pending
                ? "You are editing a pending assessment draft. These changes are not live until the moderator approves them."
                : null;
            ViewBag.EditMode = true;
            return View("Assessments");
        }

        // -------------------------
        // ASSESSMENTS (SAVE)
        // -------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAssessments(string courseCode, int year, int trimester, List<AssignmentEditInput> assignments)
        {
            if (!AllowAssignmentEdit(courseCode, year, trimester))
            {
                TempData["Error"] = GetAssessmentFallbackMessage(courseCode, year, trimester);
                return RedirectToAction(nameof(Assessments), new { courseCode, year, trimester });
            }

            var course = _context.Courses
                .FirstOrDefault(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);

            var validLearningOutcomeIds = _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .Select(lo => lo.Id)
                .ToHashSet();

            var errors = new List<string>();
            var seenNames = new HashSet<string>();
            var draftItems = new List<AssessmentDraftItem>();

            foreach (var input in assignments ?? new List<AssignmentEditInput>())
            {
                if (string.IsNullOrWhiteSpace(input.AssessmentName)) continue;

                var normalizedName = input.AssessmentName.Trim();
                var normalizedKey = NormalizeAssessmentTitle(normalizedName);
                if (!seenNames.Add(normalizedKey))
                {
                    errors.Add($"\"{normalizedName}\" duplicates another assessment name in this draft.");
                    continue;
                }

                if (input.MarksPercentage < 0 || input.MarksPercentage > 100)
                {
                    errors.Add($"\"{normalizedName}\" has an invalid marks percentage.");
                    continue;
                }

                var selectedLOIds = ParseAssignmentLearningOutcomeIds(input.SelectedLOIdsStr);
                var invalidLOIds = selectedLOIds
                    .Where(id => !validLearningOutcomeIds.Contains(id))
                    .Distinct()
                    .ToList();

                if (invalidLOIds.Any())
                {
                    errors.Add($"\"{normalizedName}\" contains invalid Learning Outcome selections.");
                    continue;
                }

                draftItems.Add(new AssessmentDraftItem
                {
                    Id = input.Id,
                    AssessmentName = normalizedName,
                    MarksPercentage = input.MarksPercentage,
                    SelectedLOIds = selectedLOIds
                });
            }

            var total = draftItems.Sum(d => d.MarksPercentage);
            if (total != 100)
            {
                errors.Add($"Assessment totals add up to {total}%, not 100%. Please adjust the marks so they total exactly 100%.");
            }

            if (errors.Any())
            {
                TempData["Error"] = string.Join(" ", errors);
                return RedirectToAction(nameof(EditAssessments), new { courseCode, year, trimester });
            }

            int.TryParse(User.FindFirst("UserId")?.Value, out int userId);
            if (course?.ModeratorId == null || userId <= 0)
            {
                TempData["Error"] = "No moderator is assigned to this course yet, so assessment changes cannot be submitted for approval.";
                return RedirectToAction(nameof(EditAssessments), new { courseCode, year, trimester });
            }

            var submission = await _submissions.SubmitAsync(
                courseCode, year, trimester,
                SubmissionItemType.Assessments, null,
                $"Assessments - {courseCode} {year} T{trimester}",
                userId);

            await _moderationDrafts.SaveAssessmentDraftAsync(
                submission.Id,
                courseCode,
                year,
                trimester,
                draftItems);

            TempData["Success"] = "Assessment changes were submitted to the moderator for approval. They are not live yet.";
            return RedirectToAction(nameof(Assessments), new { courseCode, year, trimester });
        }

        private static List<int> ParseAssignmentLearningOutcomeIds(string? ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
                return new List<int>();

            return ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => int.TryParse(value, out _))
                .Select(int.Parse)
                .Distinct()
                .ToList();
        }

        private static List<Assignment> BuildAssignmentsFromDraft(AssessmentDraft draft, string courseTitle)
        {
            return (draft.Assessments ?? new List<AssessmentDraftItem>())
                .Select(item => new Assignment
                {
                    Id = item.Id,
                    AssessmentName = item.AssessmentName,
                    CourseCode = draft.CourseCode,
                    CourseTitle = courseTitle,
                    Year = draft.Year,
                    Trimester = draft.Trimester,
                    MarksPercentage = item.MarksPercentage,
                    SelectedLearningOutcomeIds = item.SelectedLOIds.Any()
                        ? string.Join(",", item.SelectedLOIds.Distinct())
                        : null,
                    LOsLockedByOutline = false
                })
                .ToList();
        }
    }

    public class AssignmentEditInput
    {
        public int Id { get; set; }
        public string AssessmentName { get; set; } = string.Empty;
        public int MarksPercentage { get; set; }
        public string SelectedLOIdsStr { get; set; } = string.Empty;
    }
}
