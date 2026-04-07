using System.Collections.Generic;

namespace AIS_LO_System.Models.Reports
{
    public class StudentLOReportListViewModel
    {
        public string CourseCode { get; set; } = string.Empty;
        public string CourseTitle { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Trimester { get; set; }

        public string? SearchTerm { get; set; }

        public int TotalStudentsEnrolled { get; set; }

        public List<StudentLOReportListItemViewModel> Students { get; set; } = new();
    }
}