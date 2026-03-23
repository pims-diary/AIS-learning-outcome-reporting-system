using System.Collections.Generic;

namespace AIS_LO_System.Models.MarkStudents
{
    public class MarkStudentViewModel
    {
        public int StudentInternalId { get; set; }
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;

        public int AssignmentId { get; set; }
        public string CourseCode { get; set; } = string.Empty;
        public string CourseTitle { get; set; } = string.Empty;
        public string AssessmentName { get; set; } = string.Empty;

        public int Year { get; set; }
        public int Trimester { get; set; }

        public decimal TotalWeight { get; set; }
        public decimal TotalScore { get; set; }

        public string? Comment { get; set; }

        public List<int> Levels { get; set; } = new();
        public List<RubricCriterionMarkingViewModel> Criteria { get; set; } = new();
    }
}