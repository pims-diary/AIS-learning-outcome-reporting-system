using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public class Course
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(20)]
        public string Code { get; set; } = string.Empty;

        [Required, StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public int Year { get; set; }

        [Required]
        public int Trimester { get; set; }

        [StringLength(100)]
        public string School { get; set; } = "Information Technology";

        // FK to AppUser (Lecturer)
        public int? LecturerId { get; set; }
        public AppUser? Lecturer { get; set; }

        // FK to AppUser (Moderator)
        public int? ModeratorId { get; set; }
        public AppUser? Moderator { get; set; }

        // Permissions
        public bool CanEditLO { get; set; } = true;
        public bool CanReuploadOutline { get; set; } = true;
        public bool CanEditAssignment { get; set; } = true;

        public ICollection<StudentCourseEnrolment> StudentEnrolments { get; set; } = new List<StudentCourseEnrolment>();
        public ICollection<LecturerCourseEnrolment> LecturerEnrolments { get; set; } = new List<LecturerCourseEnrolment>();
    }
}