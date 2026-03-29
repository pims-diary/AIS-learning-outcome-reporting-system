using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AIS_LO_System.Migrations
{
    /// <inheritdoc />
    public partial class SeedCoursesFromFakeData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Courses",
                columns: new[] { "Id", "CanEditLO", "CanReuploadOutline", "Code", "LecturerId", "ModeratorId", "School", "Title", "Trimester", "Year" },
                values: new object[,]
                {
                    { 1, true, true, "INFO712", null, null, "Information Technology", "Management Information Systems", 1, 2026 },
                    { 2, true, true, "SOFT703", null, null, "Information Technology", "Web Applications Development", 1, 2026 },
                    { 3, true, true, "COMP720", null, null, "Information Technology", "Information Technology Project", 1, 2026 },
                    { 4, true, true, "COMP701", null, null, "Information Technology", "Software Engineering", 2, 2026 },
                    { 5, true, true, "COMP703", null, null, "Information Technology", "Web App Dev (ASP.NET)", 2, 2026 },
                    { 6, true, true, "COMP610", null, null, "Information Technology", "Database Systems", 1, 2025 },
                    { 7, true, true, "COMP611", null, null, "Information Technology", "Systems Analysis", 1, 2025 },
                    { 8, true, true, "INFO600", null, null, "Information Technology", "Intro to IT", 2, 2024 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Courses",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Courses",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Courses",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Courses",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Courses",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Courses",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Courses",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Courses",
                keyColumn: "Id",
                keyValue: 8);
        }
    }
}
