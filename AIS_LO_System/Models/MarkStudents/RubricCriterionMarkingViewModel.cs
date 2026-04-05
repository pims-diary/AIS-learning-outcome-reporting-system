using System.Collections.Generic;

namespace AIS_LO_System.Models.MarkStudents
{
    public class RubricCriterionMarkingViewModel
    {
        public int CriterionId { get; set; }
        public string CriterionTitle { get; set; } = string.Empty;
        public decimal Weight { get; set; }

        // Show LO numbers only, e.g. "1, 4"
        public string LOs { get; set; } = string.Empty;

        public List<int> AvailableLevels { get; set; } = new();

        // Full rubric level data for display on Mark Student page
        public List<RubricLevelDisplayViewModel> LevelDescriptions { get; set; } = new();

        public int? SelectedLevel { get; set; }
        public decimal CalculatedMarks { get; set; }
    }

    public class RubricLevelDisplayViewModel
    {
        public int Score { get; set; }
        public string ScaleName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}