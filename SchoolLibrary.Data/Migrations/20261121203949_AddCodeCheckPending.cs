using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeCheckPending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CodeCheckPending",
                table: "AttemptAnswers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_AttemptAnswers_CodeCheckPending",
                table: "AttemptAnswers",
                column: "CodeCheckPending");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttemptAnswers_CodeCheckPending",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "CodeCheckPending",
                table: "AttemptAnswers");
        }
    }
}
