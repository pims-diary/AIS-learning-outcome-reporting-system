using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIS_LO_System.Models
{
    public class StudentCriterionMark
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AssignmentId { get; set; }

        [Required]
        public int StudentRefId { get; set; }

        [Required]
        public int RubricCriterionId { get; set; }

        [Required]
        public int SelectedLevel { get; set; }

        [Required]
        public decimal Weight { get; set; }

        [Required]
        public decimal CalculatedScore { get; set; }

        public string? Comment { get; set; }

        [ForeignKey("StudentRefId")]
        public Student Student { get; set; } = null!;

        [ForeignKey("RubricCriterionId")]
        public RubricCriterion RubricCriterion { get; set; } = null!;
    }
}