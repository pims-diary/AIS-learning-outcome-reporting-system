using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIS_LO_System.Migrations
{
    /// <inheritdoc />
    public partial class FixCourseCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courses_AppUsers_LecturerId",
                table: "Courses");

            migrationBuilder.DropForeignKey(
                name: "FK_Courses_AppUsers_ModeratorId",
                table: "Courses");

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_AppUsers_LecturerId",
                table: "Courses",
                column: "LecturerId",
                principalTable: "AppUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_AppUsers_ModeratorId",
                table: "Courses",
                column: "ModeratorId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courses_AppUsers_LecturerId",
                table: "Courses");

            migrationBuilder.DropForeignKey(
                name: "FK_Courses_AppUsers_ModeratorId",
                table: "Courses");

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_AppUsers_LecturerId",
                table: "Courses",
                column: "LecturerId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_AppUsers_ModeratorId",
                table: "Courses",
                column: "ModeratorId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
