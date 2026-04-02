using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public enum UserRole { Admin, Lecturer }

    public class AppUser
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        public UserRole Role { get; set; }

        [StringLength(150)]
        public string? Email { get; set; }

        /// <summary>Manually toggled by Admin. Inactive users cannot log in.</summary>
        public bool IsActive { get; set; } = true;

        public ICollection<LecturerCourseEnrolment> CourseEnrolments { get; set; } = new List<LecturerCourseEnrolment>();
    }
}