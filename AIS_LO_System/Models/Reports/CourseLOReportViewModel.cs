using System.Collections.Generic;

namespace AIS_LO_System.Models.Reports
{
    public class CourseLOReportViewModel
    {
        public string CourseCode { get; set; } = string.Empty;
        public string CourseTitle { get; set; } = string.Empty;

        public int Year { get; set; }
        public int Trimester { get; set; }

        public int TotalStudentsEnrolled { get; set; }
        public int TotalAssessments { get; set; }

        public int TotalAchievedLOs { get; set; }
        public int TotalNotAchievedLOs { get; set; }

        public List<CourseLOStudentItemViewModel> Students { get; set; } = new();
        public List<CourseLOAssessmentItemViewModel> Assessments { get; set; } = new();
        public List<CourseLOSummaryItemViewModel> LOSummaries { get; set; } = new();
        public List<CourseLOStudentResultItemViewModel> StudentResults { get; set; } = new();
        public List<CourseLOAnalysisItemViewModel> LOAnalyses { get; set; } = new();

        public List<string> Messages { get; set; } = new();

        public string TrimesterLabel =>
            Trimester switch
            {
                1 => $"{Year} - Trimester 1",
                2 => $"{Year} - Trimester 2",
                3 => $"{Year} - Trimester 3",
                _ => Year.ToString()
            };
    }

    public class CourseLOStudentItemViewModel
    {
        public int StudentInternalId { get; set; }
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
    }

    public class CourseLOAssessmentItemViewModel
    {
        public int AssignmentId { get; set; }
        public string AssessmentName { get; set; } = string.Empty;
        public int MarksPercentage { get; set; }
        public bool HasAnyGradedStudent { get; set; }
    }

    public class CourseLOSummaryItemViewModel
    {
        public int LearningOutcomeId { get; set; }
        public string Label { get; set; } = string.Empty;
        public string LearningOutcomeText { get; set; } = string.Empty;

        public decimal AveragePercentage { get; set; }

        public int AchievedStudentsCount { get; set; }
        public int NotAchievedStudentsCount { get; set; }

        public string Status { get; set; } = "Not Achieved";
    }

    public class CourseLOStudentResultItemViewModel
    {
        public int StudentInternalId { get; set; }
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;

        public int AchievedLOCount { get; set; }
        public int NotAchievedLOCount { get; set; }
    }

    public class CourseLOAnalysisItemViewModel
    {
        public int LearningOutcomeId { get; set; }
        public string Label { get; set; } = string.Empty;
        public string LearningOutcomeText { get; set; } = string.Empty;

        public decimal AveragePercentage { get; set; }
        public string Status { get; set; } = string.Empty;

        public List<string> AssessmentBreakdown { get; set; } = new();
    }
}