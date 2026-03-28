using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public enum UserRole { Admin, Lecturer, Moderator }

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

        public ICollection<LecturerCourseEnrolment> CourseEnrolments { get; set; } = new List<LecturerCourseEnrolment>();
    }
}