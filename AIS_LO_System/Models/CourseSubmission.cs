using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public enum SubmissionItemType
    {
        CourseOutline,
        AssignmentDocument,
        Rubric,
        StudentMarks,
        StudentLOReport,
        CourseLOReport
    }

    public enum SubmissionStatus
    {
        Pending,
        Approved,
        Denied
    }

    public class CourseSubmission
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(20)]
        public string CourseCode { get; set; } = string.Empty;

        [Required]
        public int Year { get; set; }

        [Required]
        public int Trimester { get; set; }

        [Required]
        public SubmissionItemType ItemType { get; set; }

        /// <summary>
        /// FK to the relevant item — AssignmentId for AssignmentDocument/Rubric,
        /// null for CourseOutline / CourseLOReport.
        /// For StudentMarks: the Assignment.Id whose marks were submitted.
        /// For StudentLOReport: null (course-level).
        /// </summary>
        public int? ItemRefId { get; set; }

        /// <summary>Human-readable label shown in the moderator inbox.</summary>
        [StringLength(200)]
        public string ItemLabel { get; set; } = string.Empty;

        [Required]
        public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;

        public DateTime SubmittedAt { get; set; } = DateTime.Now;

        public DateTime? ReviewedAt { get; set; }

        [StringLength(2000)]
        public string? ModeratorComment { get; set; }

        // Who submitted
        public int SubmittedByUserId { get; set; }
        public AppUser? SubmittedBy { get; set; }

        // Who reviewed (moderator)
        public int? ReviewedByUserId { get; set; }
        public AppUser? ReviewedBy { get; set; }
    }
}