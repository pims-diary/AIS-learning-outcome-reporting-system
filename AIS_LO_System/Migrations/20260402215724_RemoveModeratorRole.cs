using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIS_LO_System.Migrations
{
    /// <inheritdoc />
    public partial class RemoveModeratorRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE AppUsers SET Role = 1 WHERE Role = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
