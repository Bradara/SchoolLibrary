using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeAnswerResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllOrNothing",
                table: "Questions",
                type: "boolean",
                nullable: false,
                defaultValue: true); // the CodeRunner default, as for new questions

            migrationBuilder.AlterColumn<string>(
                name: "ResponseText",
                table: "AttemptAnswers",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeResults",
                table: "AttemptAnswers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllOrNothing",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "CodeResults",
                table: "AttemptAnswers");

            migrationBuilder.AlterColumn<string>(
                name: "ResponseText",
                table: "AttemptAnswers",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);
        }
    }
}
