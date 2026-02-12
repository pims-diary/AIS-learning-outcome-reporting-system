namespace AIS_LO_System.Models
{
    public class Assignment
    {
        public int Id { get; set; }
        public string AssessmentName { get; set; }  // Assignment 1 / Midterm
        public string CourseCode { get; set; }

        public ICollection<AssignmentFile> Files { get; set; }
    }
}
