using AIS_LO_System.Models;

public class RubricCriterion
{
    public int Id { get; set; }

    public int RubricId { get; set; }
    public Rubric Rubric { get; set; }

    public string CriterionName { get; set; }

    public List<RubricLevel> Levels { get; set; } = new();
}
