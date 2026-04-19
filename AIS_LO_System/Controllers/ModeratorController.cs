using AIS_LO_System.Data;
using AIS_LO_System.Models;
using AIS_LO_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIS_LO_System.Controllers
{
    [Authorize(Roles = "Lecturer,Admin")]
    public class ModeratorController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SubmissionService _submissions;
        private readonly ModerationDraftService _moderationDrafts;
        private readonly IWebHostEnvironment _env;

        public ModeratorController(
            ApplicationDbContext context,
            SubmissionService submissions,
            ModerationDraftService moderationDrafts,
            IWebHostEnvironment env)
        {
            _context = context;
            _submissions = submissions;
            _moderationDrafts = moderationDrafts;
            _env = env;
        }

        // ─────────────────────────────────────────────────
        // Helper: get current user ID, verify they are moderator of the course
        // ─────────────────────────────────────────────────
        private int GetUserId()
        {
            int.TryParse(User.FindFirst("UserId")?.Value, out int id);
            return id;
        }

        private bool IsAdmin() => User.IsInRole("Admin");

        private async Task<bool> IsModerator(string courseCode, int year, int trimester)
        {
            if (IsAdmin()) return true; // admin can moderate any course
            var userId = GetUserId();
            var course = await _context.Courses.FirstOrDefaultAsync(c =>
                c.Code == courseCode && c.Year == year && c.Trimester == trimester);
            return course?.ModeratorId == userId;
        }

        // ─────────────────────────────────────────────────
        // INBOX — all pending submissions across all moderated courses
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Inbox(string? filterItemType, string? filterCourse)
        {
            var userId = GetUserId();

            // Admin sees all courses; lecturer sees only their moderated courses
            var moderatedCourses = IsAdmin()
                ? await _context.Courses.ToListAsync()
                : await _context.Courses.Where(c => c.ModeratorId == userId).ToListAsync();

            ViewBag.ModeratedCourses = moderatedCourses;
            ViewBag.IsAdmin = IsAdmin();

            var pending = IsAdmin()
                ? await _submissions.GetAllPendingAsync()
                : await _submissions.GetPendingForModeratorAsync(userId);

            var all = IsAdmin()
                ? await _submissions.GetAllSubmissionsAsync()
                : await _submissions.GetAllForModeratorAsync(userId);

            // Apply filters
            if (!string.IsNullOrEmpty(filterItemType) && Enum.TryParse<SubmissionItemType>(filterItemType, out var itemType))
            {
                pending = pending.Where(s => s.ItemType == itemType).ToList();
                all = all.Where(s => s.ItemType == itemType).ToList();
            }

            if (!string.IsNullOrEmpty(filterCourse))
            {
                var parts = filterCourse.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[1], out int year) && int.TryParse(parts[2].Replace("T", ""), out int trimester))
                {
                    var courseCode = parts[0];
                    pending = pending.Where(s => s.CourseCode == courseCode && s.Year == year && s.Trimester == trimester).ToList();
                    all = all.Where(s => s.CourseCode == courseCode && s.Year == year && s.Trimester == trimester).ToList();
                }
            }

            var inboxItems = await BuildInboxPendingItemsAsync(pending);

            ViewBag.PendingCount = inboxItems.Count;
            ViewBag.AllSubmissions = all;
            ViewBag.FilterItemType = filterItemType;
            ViewBag.FilterCourse = filterCourse;

            return View(inboxItems);
        }

        /// <summary>
        /// Collapses LO Achievement report submissions to one inbox card per assignment;
        /// other item types stay one card per submission.
        /// </summary>
        private async Task<List<ModeratorInboxPendingItem>> BuildInboxPendingItemsAsync(List<CourseSubmission> pending)
        {
            var lo = pending.Where(s => s.ItemType == SubmissionItemType.LOAchievementReport).ToList();
            var sloPerStudent = pending.Where(s =>
                s.ItemType == SubmissionItemType.StudentLOReport && s.ItemRefId != null).ToList();
            var rest = pending.Where(s =>
                s.ItemType != SubmissionItemType.LOAchievementReport
                && !(s.ItemType == SubmissionItemType.StudentLOReport && s.ItemRefId != null)).ToList();

            var items = new List<ModeratorInboxPendingItem>();

            foreach (var g in lo.GroupBy(s => new { s.CourseCode, s.Year, s.Trimester, s.ItemRefId }))
            {
                var list = g.OrderBy(s => s.SubmittedAt).ToList();
                var assignment = await _context.Assignments.FindAsync(g.Key.ItemRefId);
                var assessmentName = assignment?.AssessmentName ?? "Assignment";

                items.Add(new ModeratorInboxPendingItem
                {
                    IsGroupedLoAchievement = true,
                    IsGroupedStudentLoReport = false,
                    Submissions = list,
                    DisplayLabel = $"LO Achievement Report — {assessmentName}",
                    SortDate = list.Max(s => s.SubmittedAt)
                });
            }

            foreach (var g in sloPerStudent.GroupBy(s => new { s.CourseCode, s.Year, s.Trimester }))
            {
                var list = g.OrderBy(s => s.SubmittedAt).ToList();
                items.Add(new ModeratorInboxPendingItem
                {
                    IsGroupedLoAchievement = false,
                    IsGroupedStudentLoReport = true,
                    Submissions = list,
                    DisplayLabel = $"Student LO Report — {g.Key.CourseCode} {g.Key.Year} T{g.Key.Trimester}",
                    SortDate = list.Max(s => s.SubmittedAt)
                });
            }

            foreach (var s in rest)
            {
                items.Add(new ModeratorInboxPendingItem
                {
                    IsGroupedLoAchievement = false,
                    IsGroupedStudentLoReport = false,
                    Submissions = new List<CourseSubmission> { s },
                    DisplayLabel = s.ItemLabel,
                    SortDate = s.SubmittedAt
                });
            }

            return items.OrderBy(i => i.SortDate).ToList();
        }

        // ─────────────────────────────────────────────────
        // REVIEW — approve or deny (single submission or LO Achievement batch)
        // ─────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Review(int? submissionId, [FromForm] int[]? submissionIds, string decision, string? comment)
        {
            var ids = submissionIds != null && submissionIds.Length > 0
                ? submissionIds.Distinct().ToArray()
                : submissionId.HasValue
                    ? new[] { submissionId.Value }
                    : Array.Empty<int>();

            if (ids.Length == 0)
                return BadRequest();

            var subs = await _context.CourseSubmissions
                .Where(s => ids.Contains(s.Id))
                .ToListAsync();

            if (subs.Count != ids.Length)
                return NotFound();

            if (subs.Any(s => s.Status != SubmissionStatus.Pending))
            {
                TempData["Error"] = "One or more submissions are no longer pending.";
                return RedirectToAction(nameof(Inbox));
            }

            if (!await IsModerator(subs[0].CourseCode, subs[0].Year, subs[0].Trimester))
                return Forbid();

            if (ids.Length > 1)
            {
                var t0 = subs[0].ItemType;
                if (t0 == SubmissionItemType.LOAchievementReport)
                {
                    if (subs.Any(s => s.ItemType != SubmissionItemType.LOAchievementReport))
                        return BadRequest();
                    var keys = subs.Select(s => new { s.CourseCode, s.Year, s.Trimester, s.ItemRefId }).Distinct().ToList();
                    if (keys.Count != 1)
                        return BadRequest();
                }
                else if (t0 == SubmissionItemType.StudentLOReport)
                {
                    if (subs.Any(s => s.ItemType != SubmissionItemType.StudentLOReport))
                        return BadRequest();
                    var keys = subs.Select(s => new { s.CourseCode, s.Year, s.Trimester }).Distinct().ToList();
                    if (keys.Count != 1)
                        return BadRequest();
                }
                else
                    return BadRequest();
            }

            var userId = GetUserId();
            var isApproval = decision == "approve";

            foreach (var submission in subs)
            {
                if (isApproval)
                {
                    try
                    {
                        switch (submission.ItemType)
                        {
                            case SubmissionItemType.LearningOutcomes:
                                await _moderationDrafts.ApplyLearningOutcomeDraftAsync(submission.Id);
                                break;
                            case SubmissionItemType.Assessments:
                                await _moderationDrafts.ApplyAssessmentDraftAsync(submission.Id);
                                break;
                            case SubmissionItemType.Rubric:
                                if (submission.ItemRefId.HasValue)
                                    await ValidateAndCleanRubricMappingsAsync(submission.ItemRefId.Value);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        TempData["Error"] = $"Could not approve {submission.ItemLabel}: {ex.Message}";
                        return RedirectToAction(nameof(Inbox));
                    }
                }

                if (!isApproval && submission.ItemType == SubmissionItemType.Rubric && submission.ItemRefId.HasValue)
                {
                    var rubricToDelete = await _context.Rubrics
                        .Include(r => r.Criteria)
                            .ThenInclude(c => c.Levels)
                        .Include(r => r.Criteria)
                            .ThenInclude(c => c.LOMappings)
                        .FirstOrDefaultAsync(r => r.Id == submission.ItemRefId.Value);

                    if (rubricToDelete != null)
                    {
                        foreach (var criterion in rubricToDelete.Criteria)
                        {
                            _context.CriterionLOMappings.RemoveRange(criterion.LOMappings);
                            _context.RubricLevels.RemoveRange(criterion.Levels);
                        }
                        _context.RubricCriteria.RemoveRange(rubricToDelete.Criteria);
                        _context.Rubrics.Remove(rubricToDelete);
                    }
                }

                submission.Status = isApproval ? SubmissionStatus.Approved : SubmissionStatus.Denied;
                submission.ModeratorComment = comment?.Trim();
                submission.ReviewedAt = DateTime.Now;
                submission.ReviewedByUserId = userId;

                if (isApproval)
                {
                    var course = await _context.Courses.FirstOrDefaultAsync(c =>
                        c.Code == submission.CourseCode &&
                        c.Year == submission.Year &&
                        c.Trimester == submission.Trimester);

                    if (course != null)
                    {
                        switch (submission.ItemType)
                        {
                            case SubmissionItemType.CourseOutline:
                                course.CanReuploadOutline = false;
                                course.CanEditLO = false;
                                course.CanEditAssignment = false;
                                break;
                            case SubmissionItemType.Assessments:
                                course.CanEditAssignment = false;
                                break;
                            case SubmissionItemType.LearningOutcomes:
                                course.CanEditLO = false;
                                break;
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            if (isApproval)
            {
                foreach (var submission in subs)
                    await _moderationDrafts.DeleteDraftAsync(submission.Id);
            }

            string successLabel;
            if (subs.Count > 1 && subs[0].ItemType == SubmissionItemType.LOAchievementReport)
            {
                var assignment = await _context.Assignments.FindAsync(subs[0].ItemRefId);
                var name = assignment?.AssessmentName ?? "assignment";
                successLabel = $"LO Achievement Report — {name} ({subs.Count} students)";
            }
            else if (subs.Count > 1 && subs[0].ItemType == SubmissionItemType.StudentLOReport)
            {
                successLabel =
                    $"Student LO Report — {subs[0].CourseCode} {subs[0].Year} T{subs[0].Trimester} ({subs.Count} students)";
            }
            else
                successLabel = subs[0].ItemLabel;

            TempData["Success"] = isApproval
                ? $"✅ Approved: {successLabel}"
                : $"❌ Denied: {successLabel}";

            return RedirectToAction(nameof(Inbox));
        }

        /// <summary>
        /// On rubric approval, removes only the individual mappings where the LO
        /// is not valid for this assessment. Valid mappings (LO exists + allowed) are kept.
        /// </summary>
        private async Task ValidateAndCleanRubricMappingsAsync(int rubricId)
        {
            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.LOMappings)
                        .ThenInclude(m => m.LearningOutcome)
                .Include(r => r.Assignment)
                .FirstOrDefaultAsync(r => r.Id == rubricId);

            if (rubric == null) return;

            // Get the allowed LO IDs for this assignment (from course outline selection)
            var allowedLOIds = new List<int>();
            if (rubric.Assignment != null && !string.IsNullOrEmpty(rubric.Assignment.SelectedLearningOutcomeIds))
            {
                allowedLOIds = rubric.Assignment.SelectedLearningOutcomeIds
                    .Split(',')
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .ToList();
            }

            bool anyRemoved = false;
            foreach (var criterion in rubric.Criteria)
            {
                // Only remove mappings where the LO is not in the allowed list
                // If assignment has no locked LOs, all LOs are valid — keep everything
                if (allowedLOIds.Any())
                {
                    var invalidMappings = criterion.LOMappings
                        .Where(m => !allowedLOIds.Contains(m.LearningOutcomeId))
                        .ToList();

                    if (invalidMappings.Any())
                    {
                        _context.CriterionLOMappings.RemoveRange(invalidMappings);
                        anyRemoved = true;
                    }
                }
            }

            if (anyRemoved)
                await _context.SaveChangesAsync();
        }

        private async Task<List<string>> GetUncoveredLOsForRubricAsync(Rubric rubric)
        {
            var assignment = rubric.Assignment ?? await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == rubric.AssignmentId);

            if (assignment == null || string.IsNullOrEmpty(assignment.SelectedLearningOutcomeIds))
                return new List<string>();

            var allowedLOIds = assignment.SelectedLearningOutcomeIds
                .Split(',')
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .ToList();

            if (!allowedLOIds.Any())
                return new List<string>();

            var coveredLOIds = rubric.Criteria
                .SelectMany(c => c.LOMappings)
                .Select(m => m.LearningOutcomeId)
                .Distinct()
                .ToHashSet();

            var uncoveredIds = allowedLOIds.Where(id => !coveredLOIds.Contains(id)).ToList();
            if (!uncoveredIds.Any())
                return new List<string>();

            var los = await _context.LearningOutcomes
                .Where(lo => uncoveredIds.Contains(lo.Id))
                .OrderBy(lo => lo.OrderNumber)
                .ToListAsync();

            return los.Select(lo => $"LO{lo.OrderNumber}").ToList();
        }

        [HttpGet]
        public async Task<IActionResult> ViewLearningOutcomes(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var draft = await _moderationDrafts.LoadLearningOutcomeDraftAsync(submissionId);
            if (draft == null) return NotFound();

            ViewBag.Submission = submission;
            return View(draft);
        }

        [HttpGet]
        public async Task<IActionResult> ViewAssessments(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var draft = await _moderationDrafts.LoadAssessmentDraftAsync(submissionId);
            if (draft == null) return NotFound();

            var learningOutcomes = await _context.LearningOutcomes
                .Where(lo => lo.CourseCode == submission.CourseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToListAsync();

            ViewBag.Submission = submission;
            ViewBag.LearningOutcomes = learningOutcomes;
            return View(draft);
        }

        // ─────────────────────────────────────────────────
        // VIEW COURSE OUTLINE (read-only for moderator)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewOutline(string courseCode, int year, int trimester, int submissionId)
        {
            if (!await IsModerator(courseCode, year, trimester)) return Forbid();

            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            ViewBag.CourseCode = courseCode;
            ViewBag.Year = year;
            ViewBag.Trimester = trimester;
            ViewBag.Submission = submission;

            var baseName = $"{courseCode}-{year}-T{trimester}";
            var dir = Path.Combine(_env.WebRootPath, "uploads", "outlines");
            string? outlineUrl = null;
            foreach (var ext in new[] { ".pdf", ".docx" })
            {
                if (System.IO.File.Exists(Path.Combine(dir, baseName + ext)))
                {
                    outlineUrl = $"/uploads/outlines/{baseName}{ext}";
                    break;
                }
            }
            ViewBag.OutlineUrl = outlineUrl;

            return View();
        }

        // ─────────────────────────────────────────────────
        // VIEW ASSIGNMENT DOCUMENT (read-only for moderator)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewAssignment(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var assignment = await _context.Assignments
                .Include(a => a.Files)
                .FirstOrDefaultAsync(a => a.Id == submission.ItemRefId);

            if (assignment == null) return NotFound();

            ViewBag.Submission = submission;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;

            return View(assignment);
        }

        // ─────────────────────────────────────────────────
        // VIEW RUBRIC (read-only for moderator)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewRubric(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.Levels)
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.LOMappings)
                        .ThenInclude(m => m.LearningOutcome)
                .Include(r => r.Assignment)
                .FirstOrDefaultAsync(r => r.Id == submission.ItemRefId);

            if (rubric != null)
                foreach (var criterion in rubric.Criteria)
                    criterion.Levels = criterion.Levels.OrderByDescending(l => l.Score).ToList();

            ViewBag.Submission = submission;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;
            ViewBag.AssessmentName = rubric?.Assignment?.AssessmentName ?? submission.ItemLabel;
            ViewBag.HasLOMappings = rubric != null && rubric.Criteria.Any(c => c.LOMappings.Any());

            // Warn moderator if any assessment LOs are not covered by any rubric criterion
            ViewBag.UncoveredLOs = rubric != null
                ? await GetUncoveredLOsForRubricAsync(rubric)
                : new List<string>();

            return View(rubric);
        }

        // ─────────────────────────────────────────────────
        // VIEW LO MAPPING (read-only for moderator, approve/deny)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewLOMapping(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.LOMappings)
                        .ThenInclude(m => m.LearningOutcome)
                .Include(r => r.Assignment)
                .FirstOrDefaultAsync(r => r.AssignmentId == submission.ItemRefId);

            if (rubric == null) return NotFound();

            ViewBag.Submission = submission;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;
            ViewBag.AssessmentName = rubric.Assignment?.AssessmentName ?? submission.ItemLabel;

            return View(rubric);
        }

        // ─────────────────────────────────────────────────
        // VIEW STUDENT MARKS (read-only for moderator) — class list
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewMarks(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == submission.ItemRefId);

            if (assignment == null) return NotFound();

            var marks = await _context.StudentAssessmentMarks
                .Include(m => m.Student)
                .Where(m => m.CourseCode == submission.CourseCode && m.AssessmentName == assignment.AssessmentName)
                .ToListAsync();

            var criterionMarks = await _context.StudentCriterionMarks
                .Where(m => m.AssignmentId == assignment.Id)
                .ToListAsync();

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria).ThenInclude(c => c.Levels)
                .FirstOrDefaultAsync(r => r.AssignmentId == assignment.Id);

            ViewBag.Submission = submission;
            ViewBag.Assignment = assignment;
            ViewBag.Marks = marks;
            ViewBag.CriterionMarks = criterionMarks;
            ViewBag.Rubric = rubric;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;

            return View();
        }

        // ─────────────────────────────────────────────────
        // VIEW INDIVIDUAL STUDENT DETAIL
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewStudentDetail(int submissionId, int studentRefId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == submission.ItemRefId);

            if (assignment == null) return NotFound();

            var student = await _context.Students.FindAsync(studentRefId);
            if (student == null) return NotFound();

            var rubric = await _context.Rubrics
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.Levels)
                .Include(r => r.Criteria)
                    .ThenInclude(c => c.LOMappings)
                        .ThenInclude(m => m.LearningOutcome)
                .FirstOrDefaultAsync(r => r.AssignmentId == assignment.Id);

            if (rubric != null)
                foreach (var c in rubric.Criteria)
                    c.Levels = c.Levels.OrderByDescending(l => l.Score).ToList();

            var criterionMarks = await _context.StudentCriterionMarks
                .Where(m => m.AssignmentId == assignment.Id && m.StudentRefId == studentRefId)
                .ToListAsync();

            var overallMark = await _context.StudentAssessmentMarks
                .FirstOrDefaultAsync(m =>
                    m.StudentRefId == studentRefId &&
                    m.CourseCode == submission.CourseCode &&
                    m.AssessmentName == assignment.AssessmentName);

            ViewBag.Submission = submission;
            ViewBag.Assignment = assignment;
            ViewBag.Student = student;
            ViewBag.Rubric = rubric;
            ViewBag.CriterionMarks = criterionMarks;
            ViewBag.OverallMark = overallMark;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;

            return View();
        }

        // ─────────────────────────────────────────────────
        // VIEW STUDENT LO REPORT — student list (read-only for moderator)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewStudentLOReport(int submissionId, string? searchTerm)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .Include(s => s.ReviewedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var course = await _context.Courses.FirstOrDefaultAsync(c =>
                c.Code == submission.CourseCode && c.Year == submission.Year && c.Trimester == submission.Trimester);

            var enrolledQuery = _context.StudentCourseEnrolments
                .Where(e => e.CourseId == course!.Id)
                .Include(e => e.Student)
                .Select(e => e.Student)
                .Distinct()
                .AsQueryable();

            var totalEnrolled = await enrolledQuery.CountAsync();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                enrolledQuery = enrolledQuery.Where(s => s.StudentId.ToLower().Contains(term));
            }

            var students = await enrolledQuery
                .OrderBy(s => s.StudentId)
                .Select(s => new { s.Id, s.StudentId, s.FullName })
                .ToListAsync();

            var allSloSubs = await _context.CourseSubmissions
                .Where(s =>
                    s.CourseCode == submission.CourseCode &&
                    s.Year == submission.Year &&
                    s.Trimester == submission.Trimester &&
                    s.ItemType == SubmissionItemType.StudentLOReport &&
                    s.ItemRefId != null)
                .OrderByDescending(s => s.SubmittedAt)
                .ToListAsync();

            var rows = students.Select(st =>
            {
                var last = allSloSubs.FirstOrDefault(x => x.ItemRefId == st.Id);
                return new StudentLoModeratorRowViewModel
                {
                    StudentInternalId = st.Id,
                    StudentId = st.StudentId,
                    StudentName = st.FullName,
                    LatestSubmissionId = last?.Id,
                    ModerationStatus = last?.Status
                };
            }).ToList();

            var pendingForCourse = allSloSubs.Where(s => s.Status == SubmissionStatus.Pending).ToList();
            ViewBag.PendingSubmissionIds = pendingForCourse.Select(s => s.Id).ToList();
            ViewBag.PendingStudentReportCount = rows.Count(r => r.ModerationStatus == SubmissionStatus.Pending);
            ViewBag.LatestPendingSubmittedAt = pendingForCourse.Count > 0
                ? pendingForCourse.Max(s => s.SubmittedAt)
                : (DateTime?)null;

            ViewBag.Submission = submission;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.CourseTitle = course?.Title ?? "";
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;
            ViewBag.TotalEnrolled = totalEnrolled;
            ViewBag.SearchTerm = searchTerm;

            return View(rows);
        }

        // ─────────────────────────────────────────────────
        // VIEW STUDENT LO OVERVIEW — per-student detail (read-only for moderator)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewStudentLOOverview(int submissionId, int studentId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var course = await _context.Courses.FirstOrDefaultAsync(c =>
                c.Code == submission.CourseCode && c.Year == submission.Year && c.Trimester == submission.Trimester);

            var student = await _context.Students.FindAsync(studentId);
            if (student == null) return NotFound();

            var assignments = await _context.Assignments
                .Where(a => a.CourseCode == submission.CourseCode && a.Year == submission.Year && a.Trimester == submission.Trimester)
                .OrderBy(a => a.AssessmentName)
                .ToListAsync();

            var assessmentStatuses = new List<AIS_LO_System.Models.Reports.StudentAssessmentStatusItemViewModel>();
            var gradedAssignments = new List<Assignment>();

            foreach (var assignment in assignments)
            {
                var isGraded = await _context.StudentAssessmentMarks.AnyAsync(m =>
                    m.StudentRefId == studentId &&
                    m.CourseCode == submission.CourseCode &&
                    m.AssessmentName == assignment.AssessmentName &&
                    m.IsMarked);

                assessmentStatuses.Add(new AIS_LO_System.Models.Reports.StudentAssessmentStatusItemViewModel
                {
                    AssignmentId = assignment.Id,
                    AssessmentName = assignment.AssessmentName,
                    IsGraded = isGraded
                });

                if (isGraded) gradedAssignments.Add(assignment);
            }

            var gradedIds = gradedAssignments.Select(a => a.Id).ToList();

            var learningOutcomes = await _context.LearningOutcomes
                .Where(lo => lo.CourseCode == submission.CourseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToListAsync();

            var mappings = await _context.CriterionLOMappings
                .Include(m => m.RubricCriterion).ThenInclude(c => c.Levels)
                .Include(m => m.RubricCriterion).ThenInclude(c => c.Rubric)
                .Include(m => m.LearningOutcome)
                .Where(m => m.LearningOutcome.CourseCode == submission.CourseCode && gradedIds.Contains(m.RubricCriterion.Rubric.AssignmentId))
                .ToListAsync();

            var savedMarks = await _context.StudentCriterionMarks
                .Where(x => gradedIds.Contains(x.AssignmentId) && x.StudentRefId == studentId)
                .ToListAsync();

            var loSummaries = learningOutcomes.Select(lo =>
            {
                var loMappings = mappings.Where(m => m.LearningOutcomeId == lo.Id).ToList();
                decimal achieved = 0, max = 0;
                foreach (var mapping in loMappings)
                {
                    var saved = savedMarks.FirstOrDefault(x => x.AssignmentId == mapping.RubricCriterion.Rubric.AssignmentId && x.RubricCriterionId == mapping.RubricCriterionId);
                    if (saved != null) achieved += saved.CalculatedScore;
                    var maxLevel = mapping.RubricCriterion?.Levels?.OrderByDescending(l => l.Score).FirstOrDefault();
                    if (maxLevel != null) max += maxLevel.Score * mapping.Weight;
                }
                var pct = max > 0 ? Math.Round((achieved / max) * 100, 2) : 0;
                return new AIS_LO_System.Models.Reports.StudentCourseLOSummaryItemViewModel
                {
                    LearningOutcomeId = lo.Id,
                    Label = $"LO{lo.OrderNumber}",
                    LearningOutcomeText = lo.LearningOutcomeText,
                    AchievedScore = achieved,
                    MaxScore = max,
                    Percentage = pct,
                    Status = pct >= 50 ? "Achieved" : "Not Achieved"
                };
            }).ToList();

            var loAnalyses = learningOutcomes.Select(lo =>
            {
                var summary = loSummaries.First(s => s.LearningOutcomeId == lo.Id);
                var breakdown = assignments.Select(a =>
                {
                    var status = assessmentStatuses.FirstOrDefault(x => x.AssignmentId == a.Id);
                    if (!(status?.IsGraded ?? false)) return $"{a.AssessmentName}: Not Graded";
                    var am = mappings.Where(m => m.LearningOutcomeId == lo.Id && m.RubricCriterion.Rubric.AssignmentId == a.Id).ToList();
                    if (!am.Any()) return $"{a.AssessmentName}: LO not assessed";
                    decimal ach = 0, mx = 0;
                    foreach (var m in am)
                    {
                        var sm = savedMarks.FirstOrDefault(x => x.AssignmentId == a.Id && x.RubricCriterionId == m.RubricCriterionId);
                        if (sm != null) ach += sm.CalculatedScore;
                        var ml = m.RubricCriterion?.Levels?.OrderByDescending(l => l.Score).FirstOrDefault();
                        if (ml != null) mx += ml.Score * m.Weight;
                    }
                    var p = mx > 0 ? Math.Round((ach / mx) * 100, 2) : 0;
                    return $"{a.AssessmentName}: {p:0.##}%";
                }).ToList();
                return new AIS_LO_System.Models.Reports.StudentCourseLOAnalysisItemViewModel
                {
                    LearningOutcomeId = lo.Id,
                    Label = summary.Label,
                    LearningOutcomeText = lo.LearningOutcomeText,
                    Percentage = summary.Percentage,
                    Status = summary.Status,
                    AssessmentBreakdown = breakdown
                };
            }).ToList();

            var contributions = assignments.Select(a =>
            {
                var status = assessmentStatuses.FirstOrDefault(x => x.AssignmentId == a.Id);
                var item = new AIS_LO_System.Models.Reports.StudentCourseLOContributionItemViewModel
                {
                    AssignmentId = a.Id,
                    AssessmentName = a.AssessmentName,
                    IsGraded = status?.IsGraded ?? false,
                    StatusText = (status?.IsGraded ?? false) ? "Graded" : "Not Graded"
                };
                if (!item.IsGraded) { item.Contributions.Add("Not Graded"); item.Achievements.Add("Not Graded"); }
                else
                {
                    foreach (var lo in loSummaries)
                    {
                        var am = mappings.Where(m => m.LearningOutcomeId == lo.LearningOutcomeId && m.RubricCriterion.Rubric.AssignmentId == a.Id).ToList();
                        if (!am.Any()) { item.Contributions.Add($"{lo.Label}: LO not assessed"); item.Achievements.Add($"{lo.Label}: LO not assessed"); continue; }
                        decimal ach = 0, mx = 0;
                        foreach (var m in am)
                        {
                            var sm = savedMarks.FirstOrDefault(x => x.AssignmentId == a.Id && x.RubricCriterionId == m.RubricCriterionId);
                            if (sm != null) ach += sm.CalculatedScore;
                            var ml = m.RubricCriterion?.Levels?.OrderByDescending(l => l.Score).FirstOrDefault();
                            if (ml != null) mx += ml.Score * m.Weight;
                        }
                        var p = mx > 0 ? Math.Round((ach / mx) * 100, 2) : 0;
                        item.Contributions.Add($"{lo.Label}: {mx:0.##}");
                        item.Achievements.Add($"{lo.Label}: {p:0.##}%");
                    }
                }
                return item;
            }).ToList();

            var vm = new AIS_LO_System.Models.Reports.StudentCourseLOOverviewViewModel
            {
                StudentInternalId = student.Id,
                StudentId = student.StudentId,
                StudentName = student.FullName,
                CourseCode = submission.CourseCode,
                CourseTitle = course?.Title ?? "",
                Year = submission.Year,
                Trimester = submission.Trimester,
                Assessments = assessmentStatuses,
                LOSummaries = loSummaries,
                LOAnalyses = loAnalyses,
                Contributions = contributions,
                AchievedCount = loSummaries.Count(x => x.Status == "Achieved"),
                NotAchievedCount = loSummaries.Count(x => x.Status == "Not Achieved"),
                TotalLOCount = loSummaries.Count
            };

            ViewBag.Submission = submission;
            ViewBag.SubmissionId = submissionId;
            return View(vm);
        }

        // ======================================================
        // VIEW STUDENT LO REPORT PDF (inline, for moderators)
        // ======================================================
        [HttpGet]
        public async Task<IActionResult> ViewStudentLOReportPdf(int submissionId, int studentId)
        {
            var submission = await _context.CourseSubmissions
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null)
                return NotFound();

            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester))
                return Forbid();

            var student = await _context.Students.FindAsync(studentId);
            if (student == null)
                return NotFound();

            var pdfPath = Path.Combine(_env.WebRootPath, "uploads", "reports", "student-lo",
                $"{student.StudentId}_{submission.CourseCode}_{submission.Year}_T{submission.Trimester}_StudentLOReport.pdf");

            if (System.IO.File.Exists(pdfPath))
            {
                var pdfBytes = await System.IO.File.ReadAllBytesAsync(pdfPath);
                return File(pdfBytes, "application/pdf");
            }

            TempData["Error"] = "PDF report not found. The lecturer may not have generated this report yet.";
            return RedirectToAction(nameof(ViewStudentLOReport), new { submissionId });
        }

        // ======================================================
        // DOWNLOAD STUDENT LO REPORT PDF (force download, for moderators)
        // ======================================================
        [HttpGet]
        public async Task<IActionResult> DownloadStudentLOReportPdf(int submissionId, int studentId)
        {
            var submission = await _context.CourseSubmissions
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null)
                return NotFound();

            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester))
                return Forbid();

            var student = await _context.Students.FindAsync(studentId);
            if (student == null)
                return NotFound();

            var pdfPath = Path.Combine(_env.WebRootPath, "uploads", "reports", "student-lo",
                $"{student.StudentId}_{submission.CourseCode}_{submission.Year}_T{submission.Trimester}_StudentLOReport.pdf");

            if (System.IO.File.Exists(pdfPath))
            {
                var pdfBytes = await System.IO.File.ReadAllBytesAsync(pdfPath);
                return File(pdfBytes, "application/pdf",
                    $"{student.StudentId}_{submission.CourseCode}_{submission.Year}_T{submission.Trimester}_StudentLOReport.pdf");
            }

            TempData["Error"] = "PDF report not found. The lecturer may not have generated this report yet.";
            return RedirectToAction(nameof(ViewStudentLOReport), new { submissionId });
        }

        // ======================================================
        // VIEW COURSE LO REPORT (PDF)
        // ======================================================
        public async Task<IActionResult> ViewCourseLOReport(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null)
                return NotFound();

            var course = await _context.Courses.FirstOrDefaultAsync(c =>
                c.Code == submission.CourseCode &&
                c.Year == submission.Year &&
                c.Trimester == submission.Trimester);

            if (course == null)
                return NotFound();

            int.TryParse(User.FindFirst("UserId")?.Value, out int userId);
            bool isAdmin = User.IsInRole("Admin");

            if (!isAdmin && course.ModeratorId != userId)
            {
                TempData["Error"] = "You do not have permission to review this submission.";
                return RedirectToAction(nameof(Inbox));
            }

            var pdfPath = $"/uploads/reports/course-lo/{submission.CourseCode}_{submission.Year}_T{submission.Trimester}_CourseLOReport.pdf";

            var assignments = await _context.Assignments
                .Where(a => a.CourseCode == submission.CourseCode &&
                           a.Year == submission.Year &&
                           a.Trimester == submission.Trimester)
                .OrderBy(a => a.AssessmentName)
                .ToListAsync();

            ViewBag.Submission = submission;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.CourseTitle = course.Title;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;
            ViewBag.PdfUrl = pdfPath;
            ViewBag.Assignments = assignments;

            return View();
        }

        // ======================================================
        // VIEW ASSIGNMENT REPORT (Interactive view for moderators)
        // ======================================================
        [HttpGet]
        public async Task<IActionResult> ViewAssignmentReport(
            int assignmentId,
            string courseCode,
            string courseTitle,
            string assessmentName,
            int year,
            int trimester,
            int submissionId)
        {
            if (!await IsModerator(courseCode, year, trimester))
            {
                TempData["Error"] = "You do not have permission to access this report.";
                return RedirectToAction(nameof(Inbox));
            }

            TempData["ModeratorSubmissionId"] = submissionId;

            return RedirectToAction("AssignmentReport", "CourseLOReport", new
            {
                assignmentId,
                courseCode,
                courseTitle,
                assessmentName,
                year,
                trimester
            });
        }

        // ======================================================
        // VIEW ASSESSMENT LO REPORT (PDF submission for moderator review)
        // ======================================================
        [HttpGet]
        public async Task<IActionResult> ViewAssessmentLOReport(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null)
                return NotFound();

            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester))
            {
                TempData["Error"] = "You do not have permission to review this submission.";
                return RedirectToAction(nameof(Inbox));
            }

            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == submission.ItemRefId);

            if (assignment == null)
                return NotFound();

            var pdfPath = $"/uploads/reports/assessment-lo/{submission.CourseCode}_{submission.Year}_T{submission.Trimester}_{assignment.AssessmentName.Replace(" ", "_")}_AssessmentLOReport.pdf";

            ViewBag.Submission = submission;
            ViewBag.AssessmentName = assignment.AssessmentName;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;
            ViewBag.PdfUrl = pdfPath;

            return View();
        }

        // ======================================================
        // VIEW LO ACHIEVEMENT REPORT — grouped student list
        //
        // Loads ALL LOAchievementReport submissions for the same
        // assignment and renders them as a student table, so the
        // moderator sees every student in one place regardless of
        // which inbox card they clicked "View" on.
        // ======================================================
        [HttpGet]
        public async Task<IActionResult> ViewLOAchievementReport(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .Include(s => s.ReviewedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null)
                return NotFound();

            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester))
            {
                TempData["Error"] = "You do not have permission to review this submission.";
                return RedirectToAction(nameof(Inbox));
            }

            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == submission.ItemRefId);

            if (assignment == null)
                return NotFound();

            // Load every LOAchievementReport submission for this assignment
            var allForAssignment = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .Where(s =>
                    s.CourseCode == submission.CourseCode &&
                    s.Year == submission.Year &&
                    s.Trimester == submission.Trimester &&
                    s.ItemType == SubmissionItemType.LOAchievementReport &&
                    s.ItemRefId == submission.ItemRefId)
                .OrderBy(s => s.ItemLabel)
                .ToListAsync();

            // Resolve student for each submission, then one table row per student (merge duplicate DB rows
            // e.g. legacy vs new ItemLabel) — prefer Pending for status; PDF links use a representative submission id.
            // New label format: "LO Achievement Report — {StudentInternalId} — {StudentName} — {AssessmentName}"
            // Legacy format:    "LO Achievement Report — {StudentName} — {AssessmentName}"
            var rowCandidates = new List<(CourseSubmission Sub, Student? Student, string StudentName)>();

            foreach (var sub in allForAssignment)
            {
                var parts = sub.ItemLabel.Split(" — ");
                Student? student = null;
                string studentName;

                if (parts.Length >= 3 && int.TryParse(parts[1], out int studentInternalId))
                {
                    student = await _context.Students.FindAsync(studentInternalId);
                    studentName = parts[2];
                }
                else if (parts.Length >= 2)
                {
                    studentName = parts[1];
                    student = await _context.Students
                        .FirstOrDefaultAsync(s => s.FullName == studentName);
                }
                else
                {
                    studentName = "Unknown Student";
                }

                rowCandidates.Add((sub, student, studentName));
            }

            string StudentRowKey(Student? student, string studentName) =>
                student != null ? $"id:{student.Id}" : $"name:{studentName}";

            var rows = new List<LOAchievementStudentRowViewModel>();
            foreach (var g in rowCandidates.GroupBy(x => StudentRowKey(x.Student, x.StudentName)))
            {
                var ordered = g.OrderByDescending(x => x.Sub.SubmittedAt).ToList();
                var hasPending = ordered.Any(x => x.Sub.Status == SubmissionStatus.Pending);
                var pendingTuple = ordered.FirstOrDefault(x => x.Sub.Status == SubmissionStatus.Pending);
                var linkPick = pendingTuple.Sub != null
                    ? pendingTuple
                    : ordered.OrderByDescending(x => x.Sub.SubmittedAt).First();
                var student = linkPick.Student;
                var studentName = linkPick.StudentName;

                var status = hasPending
                    ? SubmissionStatus.Pending
                    : ordered.OrderByDescending(x => x.Sub.SubmittedAt).First().Sub.Status;

                string? pdfUrl = student != null
                    ? $"/uploads/reports/lo-achievement/{student.StudentId}_{assignment.AssessmentName.Replace(" ", "_")}_LOAchievementReport.pdf"
                    : null;

                rows.Add(new LOAchievementStudentRowViewModel
                {
                    SubmissionId = linkPick.Sub.Id,
                    StudentInternalId = student?.Id ?? 0,
                    StudentId = student?.StudentId ?? "—",
                    StudentName = studentName,
                    Status = status,
                    PdfUrl = pdfUrl
                });
            }

            rows = rows.OrderBy(r => r.StudentName).ToList();

            ViewBag.Submission = submission;
            ViewBag.AssessmentName = assignment.AssessmentName;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;
            ViewBag.TotalStudents = rows.Count;

            var pendingForAssignment = allForAssignment.Where(s => s.Status == SubmissionStatus.Pending).ToList();
            ViewBag.PendingSubmissionIds = pendingForAssignment.Select(s => s.Id).ToList();
            ViewBag.PendingStudentReportCount = rows.Count(r => r.Status == SubmissionStatus.Pending);
            ViewBag.LatestPendingSubmittedAt = pendingForAssignment.Count > 0
                ? pendingForAssignment.Max(s => s.SubmittedAt)
                : (DateTime?)null;

            return View(rows);
        }

        // ======================================================
        // VIEW LO ACHIEVEMENT REPORT PDF — serves one student's PDF inline
        // ======================================================
        [HttpGet]
        public async Task<IActionResult> ViewLOAchievementReportPdf(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == submission.ItemRefId);

            if (assignment == null) return NotFound();

            var parts = submission.ItemLabel.Split(" — ");
            Student? student = null;

            if (parts.Length >= 3 && int.TryParse(parts[1], out int studentInternalId))
                student = await _context.Students.FindAsync(studentInternalId);
            else if (parts.Length >= 2)
                student = await _context.Students.FirstOrDefaultAsync(s => s.FullName == parts[1]);

            if (student == null) return NotFound();

            var pdfPath = Path.Combine(
                _env.WebRootPath, "uploads", "reports", "lo-achievement",
                $"{student.StudentId}_{assignment.AssessmentName.Replace(" ", "_")}_LOAchievementReport.pdf");

            if (!System.IO.File.Exists(pdfPath))
            {
                TempData["Error"] = "PDF not found for this student.";
                return RedirectToAction(nameof(ViewLOAchievementReport), new { submissionId });
            }

            var pdfBytes = await System.IO.File.ReadAllBytesAsync(pdfPath);
            return File(pdfBytes, "application/pdf");
        }

        // ======================================================
        // DOWNLOAD LO ACHIEVEMENT REPORT PDF — force-downloads one student's PDF
        // ======================================================
        [HttpGet]
        public async Task<IActionResult> DownloadLOAchievementReportPdf(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.Id == submission.ItemRefId);

            if (assignment == null) return NotFound();

            var parts = submission.ItemLabel.Split(" — ");
            Student? student = null;

            if (parts.Length >= 3 && int.TryParse(parts[1], out int studentInternalId))
                student = await _context.Students.FindAsync(studentInternalId);
            else if (parts.Length >= 2)
                student = await _context.Students.FirstOrDefaultAsync(s => s.FullName == parts[1]);

            if (student == null) return NotFound();

            var filename = $"{student.StudentId}_{assignment.AssessmentName.Replace(" ", "_")}_LOAchievementReport.pdf";
            var pdfPath = Path.Combine(_env.WebRootPath, "uploads", "reports", "lo-achievement", filename);

            if (!System.IO.File.Exists(pdfPath))
            {
                TempData["Error"] = "PDF not found for this student.";
                return RedirectToAction(nameof(ViewLOAchievementReport), new { submissionId });
            }

            var pdfBytes = await System.IO.File.ReadAllBytesAsync(pdfPath);
            return File(pdfBytes, "application/pdf", filename);
        }
    }

    // ──────────────────────────────────────────────────────────
    // ViewModel used by the ViewLOAchievementReport grouped list view
    // ──────────────────────────────────────────────────────────
    public class LOAchievementStudentRowViewModel
    {
        public int SubmissionId { get; set; }
        public int StudentInternalId { get; set; }
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public SubmissionStatus Status { get; set; }
        public string? PdfUrl { get; set; }
    }

    /// <summary>One enrolled student row on the moderator Student LO Report list.</summary>
    public class StudentLoModeratorRowViewModel
    {
        public int StudentInternalId { get; set; }
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        /// <summary>Latest CourseSubmission for this student's LO report, if submitted.</summary>
        public int? LatestSubmissionId { get; set; }
        public SubmissionStatus? ModerationStatus { get; set; }
    }
}
