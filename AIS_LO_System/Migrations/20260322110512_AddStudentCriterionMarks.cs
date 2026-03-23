using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIS_LO_System.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentCriterionMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentCriterionMarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssignmentId = table.Column<int>(type: "int", nullable: false),
                    StudentRefId = table.Column<int>(type: "int", nullable: false),
                    RubricCriterionId = table.Column<int>(type: "int", nullable: false),
                    SelectedLevel = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CalculatedScore = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentCriterionMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentCriterionMarks_RubricCriteria_RubricCriterionId",
                        column: x => x.RubricCriterionId,
                        principalTable: "RubricCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentCriterionMarks_Students_StudentRefId",
                        column: x => x.StudentRefId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentCriterionMarks_RubricCriterionId",
                table: "StudentCriterionMarks",
                column: "RubricCriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentCriterionMarks_StudentRefId",
                table: "StudentCriterionMarks",
                column: "StudentRefId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentCriterionMarks");
        }
    }
}
