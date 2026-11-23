using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOneQuestionPerPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing quizzes and exams keep all questions on one page; new ones default to one per screen (in the model)
            migrationBuilder.AddColumn<bool>(
                name: "OneQuestionPerPage",
                table: "Quizzes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OneQuestionPerPage",
                table: "ExamAssignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OneQuestionPerPage",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "OneQuestionPerPage",
                table: "ExamAssignments");
        }
    }
}
