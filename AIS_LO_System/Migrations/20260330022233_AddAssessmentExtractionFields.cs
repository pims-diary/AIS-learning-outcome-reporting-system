using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AIS_LO_System.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessmentExtractionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
          name: "LOsLockedByOutline",
          table: "Assignments",
          type: "bit",
          nullable: false,
          defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MarksPercentage",
                table: "Assignments",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            
        }
    }
}
