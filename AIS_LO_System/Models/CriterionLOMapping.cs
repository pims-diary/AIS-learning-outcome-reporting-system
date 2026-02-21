using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public class CriterionLOMapping
    {
        public int Id { get; set; }

        // Foreign Keys
        [Required]
        public int RubricCriterionId { get; set; }

        [Required]
        public int LearningOutcomeId { get; set; }

        // Weight of this criterion (in marks, not percentage)
        // Example: Criterion 1 = 4 marks, Criterion 2 = 6 marks
        [Required]
        [Range(0.01, 1000)]
        public decimal Weight { get; set; }

        // Navigation properties
        public RubricCriterion RubricCriterion { get; set; }
        public LearningOutcome LearningOutcome { get; set; }
    }
}
