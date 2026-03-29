using System.Collections.Generic;

namespace AIS_LO_System.Models.MarkStudents
{
    public class LOAchievementReportViewModel
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

        public decimal AssessmentWeight { get; set; }

        public int AchievedCount { get; set; }
        public int PartialCount { get; set; }
        public int NotAchievedCount { get; set; }
        public int TotalLOCount { get; set; }

        public string OverallStatusText { get; set; } = string.Empty;

        public List<LOAchievementItemViewModel> LearningOutcomes { get; set; } = new();
    }
}