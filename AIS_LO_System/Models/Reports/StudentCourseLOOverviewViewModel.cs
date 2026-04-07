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
}