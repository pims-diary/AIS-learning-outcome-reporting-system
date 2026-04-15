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
        public async Task<IActionResult> Inbox()
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

            ViewBag.PendingCount = pending.Count;
            ViewBag.AllSubmissions = all;

            return View(pending);
        }

        // ─────────────────────────────────────────────────
        // REVIEW — approve or deny a submission
        // ─────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Review(int submissionId, string decision, string? comment)
        {
            var submission = await _context.CourseSubmissions.FindAsync(submissionId);
            if (submission == null) return NotFound();

            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester))
                return Forbid();

            var userId = GetUserId();
            var isApproval = decision == "approve";

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
                    }
                }
                catch (Exception ex)
                {
                    TempData["Error"] = $"Could not approve {submission.ItemLabel}: {ex.Message}";
                    return RedirectToAction(nameof(Inbox));
                }
            }

            submission.Status = isApproval ? SubmissionStatus.Approved : SubmissionStatus.Denied;
            submission.ModeratorComment = comment?.Trim();
            submission.ReviewedAt = DateTime.Now;
            submission.ReviewedByUserId = userId;

            // After approval, revert admin permission back to disabled (one-time allowance)
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

            await _context.SaveChangesAsync();

            if (isApproval)
                await _moderationDrafts.DeleteDraftAsync(submission.Id);

            TempData["Success"] = isApproval
                ? $"✅ Approved: {submission.ItemLabel}"
                : $"❌ Denied: {submission.ItemLabel}";

            return RedirectToAction(nameof(Inbox));
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
        // FIX #1: Now also loads LO Mappings so they appear in the table
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewRubric(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            // FIX #1: Include LOMappings → LearningOutcome so the view can display them
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

            // FIX #2: Pass a flag so the view knows if LO mapping has been done yet
            ViewBag.HasLOMappings = rubric != null && rubric.Criteria.Any(c => c.LOMappings.Any());

            return View(rubric);
        }

        // ─────────────────────────────────────────────────
        // FIX #2: VIEW LO MAPPING (read-only for moderator, approve/deny)
        // ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ViewLOMapping(int submissionId)
        {
            var submission = await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null) return NotFound();
            if (!await IsModerator(submission.CourseCode, submission.Year, submission.Trimester)) return Forbid();

            // submission.ItemRefId == AssignmentId for LOMapping submissions
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
        // FIX #3: VIEW INDIVIDUAL STUDENT DETAIL
        // Shows one student's per-criterion scores in full
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

            // Get the rubric with criteria + levels + LO mappings
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

            // Get this student's criterion marks
            var criterionMarks = await _context.StudentCriterionMarks
                .Where(m => m.AssignmentId == assignment.Id && m.StudentRefId == studentRefId)
                .ToListAsync();

            // Overall mark row
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
                .Select(s => new AIS_LO_System.Models.Reports.StudentLOReportListItemViewModel
                {
                    InternalId = s.Id,
                    StudentId = s.StudentId,
                    StudentName = s.FullName
                })
                .ToListAsync();

            ViewBag.Submission = submission;
            ViewBag.CourseCode = submission.CourseCode;
            ViewBag.CourseTitle = course?.Title ?? "";
            ViewBag.Year = submission.Year;
            ViewBag.Trimester = submission.Trimester;
            ViewBag.TotalEnrolled = totalEnrolled;
            ViewBag.SearchTerm = searchTerm;

            return View(students);
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
    }
}
