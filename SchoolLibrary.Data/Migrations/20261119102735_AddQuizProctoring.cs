using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizProctoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxLeaves",
                table: "Quizzes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Flagged",
                table: "QuizAttempts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LeaveCount",
                table: "QuizAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxLeaves",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "Flagged",
                table: "QuizAttempts");

            migrationBuilder.DropColumn(
                name: "LeaveCount",
                table: "QuizAttempts");
        }
    }
}
