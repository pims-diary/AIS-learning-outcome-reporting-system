using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

            // ── Is this user a moderator of any course? ──
            bool isModerator = await _context.Courses
                .AnyAsync(c => c.ModeratorId == userId);

            var vm = new NotificationViewModel();

            if (isModerator)
            {
                // Moderator sees PENDING submissions across all their courses.
                // LO Achievement reports are grouped per assignment (same as moderator inbox).
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
                        Href = $"/Moderator/ViewLOAchievementReport?submissionId={rep.Id}"
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
                        Href = $"/Moderator/ViewStudentLOReport?submissionId={rep.Id}"
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
                        Href = "/Moderator/Inbox"
                    });
                }

                vm.IsModerator = true;
                vm.Count = loGroups.Count + sloGroups.Count + rest.Count;
                vm.Items = notifItems
                    .OrderByDescending(i => i.Date)
                    .Take(8)
                    .ToList();
            }
            else
            {
                // Lecturer sees their submissions that were reviewed in the last 30 days
                var cutoff = DateTime.Now.AddDays(-30);

                var reviewed = await _context.CourseSubmissions
                    .Where(s => s.SubmittedByUserId == userId
                             && s.Status != SubmissionStatus.Pending
                             && s.ReviewedAt >= cutoff)
                    .OrderByDescending(s => s.ReviewedAt)
                    .Take(8)
                    .ToListAsync();

                vm.IsModerator = false;
                vm.Count = reviewed.Count;
                vm.Items = reviewed.Select(s => new NotificationItem
                {
                    Label = s.ItemLabel,
                    CourseCode = s.CourseCode,
                    Date = s.ReviewedAt ?? s.SubmittedAt,
                    StatusCss = s.Status == SubmissionStatus.Approved
                                     ? "notif-approved"
                                     : "notif-denied",
                    StatusText = s.Status == SubmissionStatus.Approved ? "Approved" : "Denied",
                    Comment = s.ModeratorComment,
                    Href = "#"   // lecturer has no dedicated inbox page; extend as needed
                }).ToList();
            }

            return View(vm);
        }
    }

    // ── View-model classes kept in the same file for simplicity ──

    public class NotificationViewModel
    {
        public bool IsModerator { get; set; }
        public int Count { get; set; }
        public List<NotificationItem> Items { get; set; } = new();
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
    }
}