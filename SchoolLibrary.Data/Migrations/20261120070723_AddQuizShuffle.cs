using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizShuffle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing quizzes keep their order; new quizzes switch shuffling on in the form
            migrationBuilder.AddColumn<bool>(
                name: "ShuffleOptions",
                table: "Quizzes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShuffleQuestions",
                table: "Quizzes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShuffleOptions",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "ShuffleQuestions",
                table: "Quizzes");
        }
    }
}
