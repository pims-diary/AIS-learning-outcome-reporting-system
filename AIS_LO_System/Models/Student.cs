using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public class Student
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string StudentId { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(20)]
        public string? Programme { get; set; }

        public StudentStatus Status { get; set; } = StudentStatus.Active;

        [StringLength(500)]
        public string? StatusReason { get; set; }

        public ICollection<StudentCourseEnrolment> CourseEnrolments { get; set; } = new List<StudentCourseEnrolment>();
    }

    public enum StudentStatus
    {
        Active,
        Inactive
    }
}