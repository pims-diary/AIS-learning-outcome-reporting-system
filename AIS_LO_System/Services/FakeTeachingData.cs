using LOARS.Web.Models.Dashboard;

namespace LOARS.Web.Services
{
    public static class FakeTeachingData
    {
        // year -> trimester -> courses
        private static readonly Dictionary<int, Dictionary<int, List<CourseCard>>> _data =
            new()
            {
                [2026] = new Dictionary<int, List<CourseCard>>
                {
                    [1] = new List<CourseCard>
                    {
                        new() { Code = "INFO712", Title = "Management Information Systems" },
                        new() { Code = "SOFT703", Title = "Web Applications Development" },
                        new() { Code = "COMP720", Title = "Information Technology Project" },
                    },
                    [2] = new List<CourseCard>
                    {
                        new() { Code = "COMP701", Title = "Software Engineering" },
                        new() { Code = "COMP703", Title = "Web App Dev (ASP.NET)" },
                    },
                    [3] = new List<CourseCard>()
                },
                [2025] = new Dictionary<int, List<CourseCard>>
                {
                    [1] = new List<CourseCard>
                    {
                        new() { Code = "COMP610", Title = "Database Systems" },
                        new() { Code = "COMP611", Title = "Systems Analysis" }
                    },
                    [2] = new List<CourseCard>(),
                    [3] = new List<CourseCard>()
                },
                [2024] = new Dictionary<int, List<CourseCard>>
                {
                    [1] = new List<CourseCard>(),
                    [2] = new List<CourseCard>
                    {
                        new() { Code = "INFO600", Title = "Intro to IT" }
                    },
                    [3] = new List<CourseCard>()
                }
            };

        public static List<int> GetYears() => _data.Keys.OrderDescending().ToList();

        public static Dictionary<int, List<int>> GetTrimestersByYear()
        {
            return _data.ToDictionary(
                y => y.Key,
                y => y.Value.Keys.OrderBy(t => t).ToList()
            );
        }

        public static List<CourseCard> GetCourses(int year, int trimester)
        {
            if (_data.TryGetValue(year, out var triMap) &&
                triMap.TryGetValue(trimester, out var courses))
            {
                return courses;
            }
            return new List<CourseCard>();
        }
    }
}
