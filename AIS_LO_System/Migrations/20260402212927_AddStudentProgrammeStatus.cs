using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIS_LO_System.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentProgrammeStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Programme",
                table: "Students",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Students",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "Students",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Programme", "Status", "StatusReason" },
                values: new object[] { null, 0, null });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Programme", "Status", "StatusReason" },
                values: new object[] { null, 0, null });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Programme", "Status", "StatusReason" },
                values: new object[] { null, 0, null });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Programme", "Status", "StatusReason" },
                values: new object[] { null, 0, null });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Programme", "Status", "StatusReason" },
                values: new object[] { null, 0, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Programme",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "Students");
        }
    }
}
