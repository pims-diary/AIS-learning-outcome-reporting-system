
namespace AIS_LO_System.Models
{
    public class Rubric
    {
        public int Id { get; set; }

        public int AssignmentId { get; set; }
        public Assignment Assignment { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public List<RubricCriterion> Criteria { get; set; } = new();
    }
}