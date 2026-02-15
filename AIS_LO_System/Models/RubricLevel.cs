public class RubricLevel
{
    public int Id { get; set; }

    public int RubricCriterionId { get; set; }
    public RubricCriterion RubricCriterion { get; set; }

    public int Score { get; set; }          // 4,3,2,1,0
    public string ScaleName { get; set; }   // Excellent, Good, etc.
    public string Description { get; set; }
}

