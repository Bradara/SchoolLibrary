using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnswerPreload",
                table: "Questions",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeLanguage",
                table: "Questions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CodeTestCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Stdin = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Expected = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    IsExample = table.Column<bool>(type: "boolean", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeTestCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeTestCases_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeTestCases_QuestionId",
                table: "CodeTestCases",
                column: "QuestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeTestCases");

            migrationBuilder.DropColumn(
                name: "AnswerPreload",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "CodeLanguage",
                table: "Questions");
        }
    }
}
