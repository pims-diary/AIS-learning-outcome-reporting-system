using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Web;

namespace AIS_LO_System.ViewComponents
{
    /// <summary>
    /// Powers the bell notification icon in the topbar.
    ///
    /// • Moderator: shows count of items PENDING their review.
    /// • Lecturer:  shows recently reviewed (Approved/Denied) submissions from
    ///              the last 30 days so they know the moderator has responded.
    /// </summary>
    public class NotificationViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public NotificationViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            int.TryParse(HttpContext.User.FindFirst("UserId")?.Value, out int userId);
            if (userId == 0)
                return View(new NotificationViewModel());

            var currentController = ViewContext.RouteData.Values["controller"]?.ToString();
            var isModeratorPage = string.Equals(currentController, "Moderator", StringComparison.OrdinalIgnoreCase);

            bool moderatesAnyCourse = await _context.Courses
                .AnyAsync(c => c.ModeratorId == userId);

            var vm = new NotificationViewModel();
            var moderatorItems = new List<NotificationItem>();

            if (moderatesAnyCourse)
            {
                var moderatedKeys = await _context.Courses
                    .Where(c => c.ModeratorId == userId)
                    .Select(c => new { c.Code, c.Year, c.Trimester })
                    .ToListAsync();

                var pendingRaw = new List<CourseSubmission>();
                foreach (var k in moderatedKeys)
                {
                    var batch = await _context.CourseSubmissions
                        .Include(s => s.SubmittedBy)
                        .Where(s => s.CourseCode == k.Code
                                 && s.Year == k.Year
                                 && s.Trimester == k.Trimester
                                 && s.Status == SubmissionStatus.Pending)
                        .OrderByDescending(s => s.SubmittedAt)
                        .ToListAsync();
                    pendingRaw.AddRange(batch);
                }

                var lo = pendingRaw.Where(s => s.ItemType == SubmissionItemType.LOAchievementReport).ToList();
                var sloStudent = pendingRaw.Where(s =>
                    s.ItemType == SubmissionItemType.StudentLOReport && s.ItemRefId != null).ToList();
                var rest = pendingRaw.Where(s =>
                    s.ItemType != SubmissionItemType.LOAchievementReport
                    && !(s.ItemType == SubmissionItemType.StudentLOReport && s.ItemRefId != null)).ToList();

                var loGroups = lo.GroupBy(s => new { s.CourseCode, s.Year, s.Trimester, s.ItemRefId }).ToList();
                var sloGroups = sloStudent.GroupBy(s => new { s.CourseCode, s.Year, s.Trimester }).ToList();
                var notifItems = new List<NotificationItem>();

                foreach (var g in loGroups)
                {
                    var list = g.OrderByDescending(s => s.SubmittedAt).ToList();
                    var assignment = await _context.Assignments.FindAsync(g.Key.ItemRefId);
                    var assessmentName = assignment?.AssessmentName ?? "Assignment";
                    var rep = list[0];
                    var n = list.Count;
                    notifItems.Add(new NotificationItem
                    {
                        Label = $"LO Achievement Report — {assessmentName} ({n} student{(n == 1 ? "" : "s")})",
                        CourseCode = rep.CourseCode,
                        Date = list.Max(s => s.SubmittedAt),
                        StatusCss = "notif-pending",
                        StatusText = "Pending",
                        Href = $"/Moderator/ViewLOAchievementReport?submissionId={rep.Id}",
                        ClientKey = $"submission_{rep.Id}_{rep.Status}_{rep.SubmittedAt.Ticks}"
                    });
                }

                foreach (var g in sloGroups)
                {
                    var list = g.OrderByDescending(s => s.SubmittedAt).ToList();
                    var rep = list[0];
                    var n = list.Count;
                    notifItems.Add(new NotificationItem
                    {
                        Label = $"Student LO Report — {rep.CourseCode} {rep.Year} T{rep.Trimester} ({n} student{(n == 1 ? "" : "s")})",
                        CourseCode = rep.CourseCode,
                        Date = list.Max(s => s.SubmittedAt),
                        StatusCss = "notif-pending",
                        StatusText = "Pending",
                        Href = $"/Moderator/ViewStudentLOReport?submissionId={rep.Id}",
                        ClientKey = $"submission_{rep.Id}_{rep.Status}_{rep.SubmittedAt.Ticks}"
                    });
                }

                foreach (var s in rest.OrderByDescending(s => s.SubmittedAt))
                {
                    notifItems.Add(new NotificationItem
                    {
                        Label = s.ItemLabel,
                        CourseCode = s.CourseCode,
                        Date = s.SubmittedAt,
                        StatusCss = "notif-pending",
                        StatusText = "Pending",
                        Href = "/Moderator/Inbox",
                        ClientKey = $"submission_{s.Id}_{s.Status}_{s.SubmittedAt.Ticks}"
                    });
                }

                moderatorItems = notifItems
                    .OrderByDescending(i => i.Date)
                    .Take(8)
                    .ToList();
            }

            var lecturerItems = new List<NotificationItem>();
            var cutoff = DateTime.Now.AddDays(-30);

            var reviewed = await _context.CourseSubmissions
                .Where(s => s.SubmittedByUserId == userId
                         && s.Status != SubmissionStatus.Pending
                         && s.ReviewedAt >= cutoff)
                .OrderByDescending(s => s.ReviewedAt)
                .Take(8)
                .ToListAsync();

            foreach (var submission in reviewed)
            {
                lecturerItems.Add(new NotificationItem
                {
                    Label = submission.ItemLabel,
                    CourseCode = submission.CourseCode,
                    Date = submission.ReviewedAt ?? submission.SubmittedAt,
                    StatusCss = submission.Status == SubmissionStatus.Approved
                        ? "notif-approved"
                        : "notif-denied",
                    StatusText = submission.Status == SubmissionStatus.Approved ? "Approved" : "Denied",
                    Comment = submission.ModeratorComment,
                    Href = await BuildLecturerHrefAsync(submission),
                    ClientKey = $"submission_{submission.Id}_{submission.Status}_{(submission.ReviewedAt ?? submission.SubmittedAt).Ticks}"
                });
            }

            vm.IsModerator = moderatesAnyCourse;
            vm.ModeratorItems = moderatorItems;
            vm.LecturerItems = lecturerItems;
            vm.Count = moderatorItems.Count + lecturerItems.Count;
            vm.LecturerPopupItems = (!isModeratorPage && lecturerItems.Any()) ? lecturerItems : new List<NotificationItem>();

            return View(vm);
        }

        private async Task<string> BuildLecturerHrefAsync(CourseSubmission submission)
        {
            string encode(string value) => HttpUtility.UrlEncode(value);

            switch (submission.ItemType)
            {
                case SubmissionItemType.AssignmentDocument:
                    if (submission.ItemRefId.HasValue)
                        return $"/Assignment/Information?id={submission.ItemRefId.Value}";
                    break;

                case SubmissionItemType.Rubric:
                case SubmissionItemType.LOMapping:
                case SubmissionItemType.StudentMarks:
                case SubmissionItemType.LOAchievementReport:
                case SubmissionItemType.AssessmentLOReport:
                    if (submission.ItemRefId.HasValue)
                    {
                        var assignment = await _context.Assignments
                            .AsNoTracking()
                            .FirstOrDefaultAsync(a => a.Id == submission.ItemRefId.Value);

                        if (assignment != null)
                        {
                            return submission.ItemType switch
                            {
                                SubmissionItemType.Rubric =>
                                    $"/Rubric/Index?assignmentId={assignment.Id}&assessmentName={encode(assignment.AssessmentName)}&courseCode={encode(assignment.CourseCode)}&courseTitle={encode(assignment.CourseTitle)}&year={assignment.Year}&trimester={assignment.Trimester}",
                                SubmissionItemType.LOMapping =>
                                    $"/LOMapping/Index?assignmentId={assignment.Id}&assessmentName={encode(assignment.AssessmentName)}&courseCode={encode(assignment.CourseCode)}&courseTitle={encode(assignment.CourseTitle)}&year={assignment.Year}&trimester={assignment.Trimester}",
                                SubmissionItemType.StudentMarks or SubmissionItemType.LOAchievementReport =>
                                    $"/MarkStudents/Index?assignmentId={assignment.Id}&courseCode={encode(assignment.CourseCode)}&courseTitle={encode(assignment.CourseTitle)}&assessmentName={encode(assignment.AssessmentName)}&year={assignment.Year}&trimester={assignment.Trimester}",
                                SubmissionItemType.AssessmentLOReport =>
                                    $"/CourseLOReport/AssignmentReport?assignmentId={assignment.Id}&courseCode={encode(assignment.CourseCode)}&courseTitle={encode(assignment.CourseTitle)}&assessmentName={encode(assignment.AssessmentName)}&year={assignment.Year}&trimester={assignment.Trimester}",
                                _ => "#"
                            };
                        }
                    }
                    break;

                case SubmissionItemType.StudentLOReport:
                    return $"/StudentLOReport/Index?courseCode={encode(submission.CourseCode)}&courseTitle={encode(await GetCourseTitleAsync(submission.CourseCode, submission.Year, submission.Trimester))}&year={submission.Year}&trimester={submission.Trimester}";

                case SubmissionItemType.CourseLOReport:
                    return $"/CourseLOReport/Index?courseCode={encode(submission.CourseCode)}&courseTitle={encode(await GetCourseTitleAsync(submission.CourseCode, submission.Year, submission.Trimester))}&year={submission.Year}&trimester={submission.Trimester}";

                case SubmissionItemType.CourseOutline:
                    return $"/CourseInformation/Outline?courseCode={encode(submission.CourseCode)}&year={submission.Year}&trimester={submission.Trimester}";

                case SubmissionItemType.LearningOutcomes:
                    return $"/CourseInformation/LearningOutcomes?courseCode={encode(submission.CourseCode)}&year={submission.Year}&trimester={submission.Trimester}";

                case SubmissionItemType.Assessments:
                    return $"/CourseInformation/Assessments?courseCode={encode(submission.CourseCode)}&year={submission.Year}&trimester={submission.Trimester}";
            }

            return "#";
        }

        private async Task<string> GetCourseTitleAsync(string courseCode, int year, int trimester)
        {
            var title = await _context.Courses
                .AsNoTracking()
                .Where(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester)
                .Select(c => c.Title)
                .FirstOrDefaultAsync();

            return title ?? courseCode;
        }
    }

    public class NotificationViewModel
    {
        public bool IsModerator { get; set; }
        public int Count { get; set; }
        public List<NotificationItem> ModeratorItems { get; set; } = new();
        public List<NotificationItem> LecturerItems { get; set; } = new();
        public List<NotificationItem> LecturerPopupItems { get; set; } = new();
    }

    public class NotificationItem
    {
        public string Label { get; set; } = string.Empty;
        public string CourseCode { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string StatusCss { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string? Comment { get; set; }
        public string Href { get; set; } = "#";
        public string ClientKey { get; set; } = string.Empty;
    }
}
