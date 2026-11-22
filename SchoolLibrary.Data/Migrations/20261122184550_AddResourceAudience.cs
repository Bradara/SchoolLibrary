using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Audience",
                table: "Resources",
                type: "integer",
                nullable: false,
                // Existing resources stay visible to everyone (ResourceAudience.Everyone)
                defaultValue: 15);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Resources_Audience",
                table: "Resources",
                sql: "\"Audience\" BETWEEN 0 AND 15");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Resources_Audience",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "Resources");
        }
    }
}
