using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public class LearningOutcome
    {
        public int Id { get; set; }

        [Required]
        public string CourseCode { get; set; }

        [Required]
        public string LearningOutcomeText { get; set; }

        public int OrderNumber { get; set; } // For displaying LOs in correct order (LO1, LO2, etc.)

        // Navigation property
        public ICollection<CriterionLOMapping> CriterionMappings { get; set; } = new List<CriterionLOMapping>();
    }
}
