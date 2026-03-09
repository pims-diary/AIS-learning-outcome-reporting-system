using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AIS_LO_System.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentAssessmentMarkTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentAssessmentMarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentRefId = table.Column<int>(type: "int", nullable: false),
                    AssessmentName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CourseCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsMarked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentAssessmentMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentAssessmentMarks_Students_StudentRefId",
                        column: x => x.StudentRefId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "StudentAssessmentMarks",
                columns: new[] { "Id", "AssessmentName", "CourseCode", "IsMarked", "StudentRefId" },
                values: new object[,]
                {
                    { 1, "Assignment 1", "COMP720", true, 2 },
                    { 2, "Assignment 1", "COMP720", true, 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentAssessmentMarks_StudentRefId",
                table: "StudentAssessmentMarks",
                column: "StudentRefId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentAssessmentMarks");
        }
    }
}
