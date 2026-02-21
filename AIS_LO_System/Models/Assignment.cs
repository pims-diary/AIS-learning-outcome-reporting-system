using System.ComponentModel.DataAnnotations;

namespace AIS_LO_System.Models
{
    public class Assignment
    {
        public int Id { get; set; }

        [Required]
        public string AssessmentName { get; set; }

        [Required]
        public string CourseCode { get; set; }

        public string CourseTitle { get; set; }

        public int Year { get; set; }
        public int Trimester { get; set; }

        // ✅ NEW: Store selected LO IDs as comma-separated string (e.g., "1,2,3,4")
        public string? SelectedLearningOutcomeIds { get; set; }

        public ICollection<AssignmentFile> Files { get; set; }
    }
}
