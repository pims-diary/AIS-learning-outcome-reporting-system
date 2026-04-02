using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIS_LO_System.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AppUsers",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AppUsers");
        }
    }
}
