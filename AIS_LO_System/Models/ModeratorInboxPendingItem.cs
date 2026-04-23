namespace AIS_LO_System.Models
{
    /// <summary>
    /// One row in the moderator pending inbox. Either a single submission or
    /// a grouped batch (LO Achievement per assignment, or per-student Student LO reports per course).
    /// </summary>
    public class ModeratorInboxPendingItem
    {
        public IReadOnlyList<CourseSubmission> Submissions { get; init; } = Array.Empty<CourseSubmission>();

        /// <summary>True when this card represents all LO Achievement reports for one assignment.</summary>
        public bool IsGroupedLoAchievement { get; init; }

        /// <summary>True when this card groups per-student Student LO Report rows for one course/term.</summary>
        public bool IsGroupedStudentLoReport { get; init; }

        public string DisplayLabel { get; init; } = "";

        /// <summary>Used to sort cards in the inbox (latest activity in the group).</summary>
        public DateTime SortDate { get; init; }

        /// <summary>Number of enrolled students for grouped LO Achievement batches.</summary>
        public int EnrolledStudentCount { get; init; }
    }
}