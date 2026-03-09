using System.Collections.Generic;

namespace AIS_LO_System.Models.MarkStudents
{
    public class MarkStudentsViewModel
    {
        public string CourseCode { get; set; } = string.Empty;
        public string AssessmentName { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Trimester { get; set; }

        public string? SearchTerm { get; set; }

        public List<StudentListItemViewModel> Students { get; set; } = new();
    }
}