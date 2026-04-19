using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public enum SubmissionItemType
    {
        CourseOutline,
        AssignmentDocument,
        Rubric,
        StudentMarks,
        StudentLOReport,        // Student LO Report for all assessments (course-level)
        CourseLOReport,         // Course LO Report for all students
        AssessmentLOReport,     // Assessment LO Report for all students (single assignment)
        LOMapping,              // Manually-assigned LO mapping sent for moderator review
        LOAchievementReport     // LO Achievement Report for a single student on single assignment
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
        /// FK to the relevant item:
        /// - AssignmentDocument/Rubric/LOMapping/AssessmentLOReport: AssignmentId
        /// - StudentMarks: Assignment.Id whose marks were submitted
        /// - CourseOutline/CourseLOReport/StudentLOReport: null (course-level)
        /// </summary>
        public int? ItemRefId { get; set; }

        /// <summary>Human-readable label shown in the moderator inbox.</summary>
        [StringLength(200)]
        public string ItemLabel { get; set; } = string.Empty;

        [Required]
        public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;

        [Required]
        public DateTime SubmittedAt { get; set; } = DateTime.Now;

        public int? SubmittedByUserId { get; set; }
        public AppUser? SubmittedBy { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public int? ReviewedByUserId { get; set; }
        public AppUser? ReviewedBy { get; set; }

        [StringLength(1000)]
        public string? ModeratorComment { get; set; }
    }
}