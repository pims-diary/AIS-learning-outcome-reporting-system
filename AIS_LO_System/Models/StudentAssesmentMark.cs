using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIS_LO_System.Models
{
    public class StudentAssessmentMark
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int StudentRefId { get; set; }

        [ForeignKey("StudentRefId")]
        public Student Student { get; set; } = null!;

        [Required]
        public string AssessmentName { get; set; } = string.Empty;

        [Required]
        public string CourseCode { get; set; } = string.Empty;

        public bool IsMarked { get; set; } = false;
    }
}