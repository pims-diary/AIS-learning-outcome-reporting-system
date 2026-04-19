using System.Collections.Generic;

namespace AIS_LO_System.Models.Reports
{
    public class StudentCourseLOOverviewViewModel
    {
        public int StudentInternalId { get; set; }
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;

        public string CourseCode { get; set; } = string.Empty;
        public string CourseTitle { get; set; } = string.Empty;

        public int Year { get; set; }
        public int Trimester { get; set; }

        public List<StudentAssessmentStatusItemViewModel> Assessments { get; set; } = new();
        public List<StudentCourseLOSummaryItemViewModel> LOSummaries { get; set; } = new();
        public List<StudentCourseLOAnalysisItemViewModel> LOAnalyses { get; set; } = new();
        public List<StudentCourseLOContributionItemViewModel> Contributions { get; set; } = new();

        public int AchievedCount { get; set; }
        public int NotAchievedCount { get; set; }
        public int TotalLOCount { get; set; }

        public string TrimesterLabel =>
            Trimester switch
            {
                1 => $"{Year} - Trimester 1",
                2 => $"{Year} - Trimester 2",
                3 => $"{Year} - Trimester 3",
                _ => Year.ToString()
            };
    }

    public class StudentAssessmentStatusItemViewModel
    {
        public int AssignmentId { get; set; }
        public string AssessmentName { get; set; } = string.Empty;
        public bool IsGraded { get; set; }
    }

    public class StudentCourseLOSummaryItemViewModel
    {
        public int LearningOutcomeId { get; set; }
        public string Label { get; set; } = string.Empty;
        public string LearningOutcomeText { get; set; } = string.Empty;

        public decimal AchievedScore { get; set; }
        public decimal MaxScore { get; set; }
        public decimal Percentage { get; set; }

        public string Status { get; set; } = "Not Achieved";
    }

    public class StudentCourseLOAnalysisItemViewModel
    {
        public int LearningOutcomeId { get; set; }
        public string Label { get; set; } = string.Empty;
        public string LearningOutcomeText { get; set; } = string.Empty;
        public decimal Percentage { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<string> AssessmentBreakdown { get; set; } = new();
    }

    public class StudentCourseLOContributionItemViewModel
    {
        public int AssignmentId { get; set; }
        public string AssessmentName { get; set; } = string.Empty;
        public bool IsGraded { get; set; }
        public string StatusText { get; set; } = string.Empty;

        public int AssignmentWeight { get; set; }

        public List<string> Contributions { get; set; } = new();
        public List<string> Achievements { get; set; } = new();

        public string ContributionTooltip { get; set; } = string.Empty;
        public string AchievementTooltip { get; set; } = string.Empty;
    }
}