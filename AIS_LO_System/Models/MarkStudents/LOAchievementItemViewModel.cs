namespace AIS_LO_System.Models.MarkStudents
{
    public class LOAchievementItemViewModel
    {
        public int LearningOutcomeId { get; set; }
        public string Label { get; set; } = string.Empty;
        public string LearningOutcomeText { get; set; } = string.Empty;

        public decimal AchievedScore { get; set; }
        public decimal MaxScore { get; set; }
        public decimal Percentage { get; set; }

        public string Status { get; set; } = "Not Calculated";
    }
}