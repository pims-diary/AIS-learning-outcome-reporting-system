public class RubricLevel
{
    public int Id { get; set; }

    public int RubricCriterionId { get; set; }
    public RubricCriterion RubricCriterion { get; set; }

    public int Score { get; set; }
    public string Description { get; set; }
}
