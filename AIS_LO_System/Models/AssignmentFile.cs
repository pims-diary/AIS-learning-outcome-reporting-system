namespace AIS_LO_System.Models
{
    public class AssignmentFile
    {
        public int Id { get; set; }

        public int AssignmentId { get; set; }
        public Assignment Assignment { get; set; }

        public string OriginalFileName { get; set; }
        public string StoredFileName { get; set; }
        public string FilePath { get; set; }

        public int VersionNumber { get; set; }
        public DateTime UploadDate { get; set; }
    }

}
