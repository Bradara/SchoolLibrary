using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceIsCorrectWithFraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the new column first, copy the existing answer keys into it, then drop the old one
            migrationBuilder.AddColumn<decimal>(
                name: "Fraction",
                table: "AnswerOptions",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("UPDATE \"AnswerOptions\" SET \"Fraction\" = 100 WHERE \"IsCorrect\";");

            migrationBuilder.DropColumn(
                name: "IsCorrect",
                table: "AnswerOptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCorrect",
                table: "AnswerOptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE \"AnswerOptions\" SET \"IsCorrect\" = TRUE WHERE \"Fraction\" >= 100;");

            migrationBuilder.DropColumn(
                name: "Fraction",
                table: "AnswerOptions");
        }
    }
}
