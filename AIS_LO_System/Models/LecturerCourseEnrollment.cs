using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public class LecturerCourseEnrolment
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        public AppUser User { get; set; } = null!;

        public int CourseId { get; set; }
        public Course Course { get; set; } = null!;
    }
}