namespace AIS_LO_System.Models.MarkStudents
{
    public class StudentListItemViewModel
    {
        public int InternalId { get; set; }

        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;

        public string Status { get; set; } = "Not Marked";

        public bool IsMarked => Status == "Marked";
    }
}