using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public class StudentCourseEnrolment
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        public int CourseId { get; set; }
        public Course Course { get; set; } = null!;
    }
}