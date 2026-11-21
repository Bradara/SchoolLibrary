using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeQuizScoresDecimal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Score",
                table: "QuizAttempts",
                type: "numeric(7,2)",
                precision: 7,
                scale: 2,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "MaxScore",
                table: "QuizAttempts",
                type: "numeric(7,2)",
                precision: 7,
                scale: 2,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Score",
                table: "QuizAttempts",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,2)",
                oldPrecision: 7,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "MaxScore",
                table: "QuizAttempts",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,2)",
                oldPrecision: 7,
                oldScale: 2);
        }
    }
}
