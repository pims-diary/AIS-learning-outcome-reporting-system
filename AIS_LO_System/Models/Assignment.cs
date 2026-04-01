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

        // Marks percentage from course outline (e.g. 70)
        public int MarksPercentage { get; set; }

        // If true, LOs were extracted from course outline and cannot be edited
        public bool LOsLockedByOutline { get; set; }

        public ICollection<AssignmentFile> Files { get; set; }
    }
}
