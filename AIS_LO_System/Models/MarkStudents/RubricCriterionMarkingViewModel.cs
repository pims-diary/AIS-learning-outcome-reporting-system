using System.Collections.Generic;

namespace AIS_LO_System.Models.MarkStudents
{
    public class RubricCriterionMarkingViewModel
    {
        public int CriterionId { get; set; }
        public string CriterionTitle { get; set; } = string.Empty;
        public decimal Weight { get; set; }
        public string LOs { get; set; } = string.Empty;

        public List<int> AvailableLevels { get; set; } = new();

        public int? SelectedLevel { get; set; }
        public decimal CalculatedMarks { get; set; }
    }
}