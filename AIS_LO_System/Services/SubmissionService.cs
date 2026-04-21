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
            // Build the base duplicate check query
            var query = _context.CourseSubmissions
                .Where(s => s.CourseCode == courseCode
                         && s.Year == year
                         && s.Trimester == trimester
                         && s.ItemType == itemType
                         && s.ItemRefId == itemRefId);

            var existing = await query
                .OrderByDescending(s => s.SubmittedAt)
                .FirstOrDefaultAsync();

            if (existing != null && existing.Status == SubmissionStatus.Approved)
            {
                // Any item type can be updated after a prior approval (e.g. rubric edited,
                // outline re-uploaded, more marks added). Reset to Pending so the moderator
                // reviews the updated content.
                existing.Status = SubmissionStatus.Pending;
                existing.SubmittedAt = DateTime.Now;
                existing.SubmittedByUserId = submittedByUserId;
                existing.ModeratorComment = null;
                existing.ReviewedAt = null;
                existing.ReviewedByUserId = null;
                await _context.SaveChangesAsync();
                return existing;
            }

            if (existing != null && existing.Status == SubmissionStatus.Pending)
            {
                existing.ItemLabel = itemLabel;
                existing.SubmittedAt = DateTime.Now;
                existing.SubmittedByUserId = submittedByUserId;
                await _context.SaveChangesAsync();
                return existing; // still awaiting review, but draft content was refreshed
            }

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

        /// <summary>Returns all pending submissions across all courses (admin use).</summary>
        public async Task<List<CourseSubmission>> GetAllPendingAsync()
        {
            return await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .Where(s => s.Status == SubmissionStatus.Pending)
                .OrderBy(s => s.SubmittedAt)
                .ToListAsync();
        }

        /// <summary>Returns all submissions across all courses (admin use).</summary>
        public async Task<List<CourseSubmission>> GetAllSubmissionsAsync()
        {
            return await _context.CourseSubmissions
                .Include(s => s.SubmittedBy)
                .Include(s => s.ReviewedBy)
                .OrderByDescending(s => s.SubmittedAt)
                .ToListAsync();
        }

        /// <summary>Returns all pending submissions for courses where the given user is the moderator.</summary>
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
