using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.EntityFrameworkCore;

namespace AIS_LO_System.Services
{
    public class SubmissionService
    {
        private readonly ApplicationDbContext _context;

        public SubmissionService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Creates a new Pending submission (or resets an existing Denied one back to Pending).
        /// If an identical item is already Pending or Approved, this is a no-op.
        /// Returns the submission.
        /// </summary>
        public async Task<CourseSubmission> SubmitAsync(
            string courseCode, int year, int trimester,
            SubmissionItemType itemType, int? itemRefId,
            string itemLabel, int submittedByUserId)
        {
            // Check for existing
            var existing = await _context.CourseSubmissions
                .Where(s => s.CourseCode == courseCode
                         && s.Year == year
                         && s.Trimester == trimester
                         && s.ItemType == itemType
                         && s.ItemRefId == itemRefId)
                .OrderByDescending(s => s.SubmittedAt)
                .FirstOrDefaultAsync();

            if (existing != null && existing.Status == SubmissionStatus.Approved)
            {
                // Student marks are incremental — new students may be marked after a prior approval.
                // Reset to Pending so the moderator reviews the updated set.
                // All other item types (outline, rubric, etc.) are whole-document replacements,
                // so an existing approval stands until the document is re-uploaded.
                if (itemType == SubmissionItemType.StudentMarks)
                {
                    existing.Status = SubmissionStatus.Pending;
                    existing.SubmittedAt = DateTime.Now;
                    existing.SubmittedByUserId = submittedByUserId;
                    existing.ModeratorComment = null;
                    existing.ReviewedAt = null;
                    existing.ReviewedByUserId = null;
                    await _context.SaveChangesAsync();
                    return existing;
                }
                return existing; // already approved, don't re-submit
            }

            if (existing != null && existing.Status == SubmissionStatus.Pending)
                return existing; // already awaiting review

            // Denied or no record → create fresh submission
            var submission = new CourseSubmission
            {
                CourseCode = courseCode,
                Year = year,
                Trimester = trimester,
                ItemType = itemType,
                ItemRefId = itemRefId,
                ItemLabel = itemLabel,
                Status = SubmissionStatus.Pending,
                SubmittedAt = DateTime.Now,
                SubmittedByUserId = submittedByUserId,
                ModeratorComment = null,
                ReviewedAt = null,
                ReviewedByUserId = null
            };

            _context.CourseSubmissions.Add(submission);
            await _context.SaveChangesAsync();
            return submission;
        }

        /// <summary>
        /// Returns the latest submission for a given item, or null.
        /// </summary>
        public async Task<CourseSubmission?> GetLatestAsync(
            string courseCode, int year, int trimester,
            SubmissionItemType itemType, int? itemRefId = null)
        {
            return await _context.CourseSubmissions
                .Where(s => s.CourseCode == courseCode
                         && s.Year == year
                         && s.Trimester == trimester
                         && s.ItemType == itemType
                         && s.ItemRefId == itemRefId)
                .OrderByDescending(s => s.SubmittedAt)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Returns true if the item has been approved by the moderator.
        /// </summary>
        public async Task<bool> IsApprovedAsync(
            string courseCode, int year, int trimester,
            SubmissionItemType itemType, int? itemRefId = null)
        {
            var sub = await GetLatestAsync(courseCode, year, trimester, itemType, itemRefId);
            return sub?.Status == SubmissionStatus.Approved;
        }

        /// <summary>
        /// Returns all pending submissions for courses where the given user is the moderator.
        /// </summary>
        public async Task<List<CourseSubmission>> GetPendingForModeratorAsync(int moderatorUserId)
        {
            var moderatedCourseCodes = await _context.Courses
                .Where(c => c.ModeratorId == moderatorUserId)
                .Select(c => new { c.Code, c.Year, c.Trimester })
                .ToListAsync();

            var submissions = new List<CourseSubmission>();
            foreach (var c in moderatedCourseCodes)
            {
                var items = await _context.CourseSubmissions
                    .Include(s => s.SubmittedBy)
                    .Where(s => s.CourseCode == c.Code
                             && s.Year == c.Year
                             && s.Trimester == c.Trimester
                             && s.Status == SubmissionStatus.Pending)
                    .OrderBy(s => s.SubmittedAt)
                    .ToListAsync();
                submissions.AddRange(items);
            }
            return submissions;
        }

        /// <summary>
        /// Returns all submissions (any status) for courses where the given user is the moderator.
        /// </summary>
        public async Task<List<CourseSubmission>> GetAllForModeratorAsync(int moderatorUserId)
        {
            var moderatedCourseCodes = await _context.Courses
                .Where(c => c.ModeratorId == moderatorUserId)
                .Select(c => new { c.Code, c.Year, c.Trimester })
                .ToListAsync();

            var submissions = new List<CourseSubmission>();
            foreach (var c in moderatedCourseCodes)
            {
                var items = await _context.CourseSubmissions
                    .Include(s => s.SubmittedBy)
                    .Include(s => s.ReviewedBy)
                    .Where(s => s.CourseCode == c.Code
                             && s.Year == c.Year
                             && s.Trimester == c.Trimester)
                    .OrderByDescending(s => s.SubmittedAt)
                    .ToListAsync();
                submissions.AddRange(items);
            }
            return submissions.OrderByDescending(s => s.SubmittedAt).ToList();
        }
    }
}