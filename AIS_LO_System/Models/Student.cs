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

        public ICollection<StudentCourseEnrolment> CourseEnrolments { get; set; } = new List<StudentCourseEnrolment>();
    }
}