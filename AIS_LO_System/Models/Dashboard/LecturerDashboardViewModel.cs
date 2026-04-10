using System.Collections.Generic;

namespace LOARS.Web.Models.Dashboard
{
    public class LecturerDashboardViewModel
    {
        public int SelectedYear { get; set; }
        public int SelectedTrimester { get; set; }

        // For hamburger menu
        public List<int> Years { get; set; } = new();
        public Dictionary<int, List<int>> TrimestersByYear { get; set; } = new();

        // Courses for selected year/trimester
        public List<CourseCard> Courses { get; set; } = new();

        /// <summary>True when this user is the assigned moderator of at least one course.</summary>
        public bool IsModeratorOfAnyCourse { get; set; }

        public string ContextLabel => $"{SelectedYear} - Trimester {SelectedTrimester}";
    }
}