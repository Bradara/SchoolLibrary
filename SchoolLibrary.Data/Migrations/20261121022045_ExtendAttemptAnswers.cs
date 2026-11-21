using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtendAttemptAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AwardedPoints",
                table: "AttemptAnswers",
                type: "numeric(7,2)",
                precision: 7,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Answers submitted before points were stored per answer: a correct one earned the question's points
            migrationBuilder.Sql(
                "UPDATE \"AttemptAnswers\" AS a SET \"AwardedPoints\" = q.\"Points\" " +
                "FROM \"Questions\" AS q WHERE a.\"QuestionId\" = q.\"Id\" AND a.\"IsCorrect\";");

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "AttemptAnswers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ResponseText",
                table: "AttemptAnswers",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttemptAnswerChoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AttemptAnswerId = table.Column<int>(type: "integer", nullable: false),
                    OptionId = table.Column<int>(type: "integer", nullable: false),
                    MatchedOptionId = table.Column<int>(type: "integer", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttemptAnswerChoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttemptAnswerChoices_AnswerOptions_MatchedOptionId",
                        column: x => x.MatchedOptionId,
                        principalTable: "AnswerOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttemptAnswerChoices_AnswerOptions_OptionId",
                        column: x => x.OptionId,
                        principalTable: "AnswerOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttemptAnswerChoices_AttemptAnswers_AttemptAnswerId",
                        column: x => x.AttemptAnswerId,
                        principalTable: "AttemptAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttemptAnswerChoices_AttemptAnswerId_OptionId",
                table: "AttemptAnswerChoices",
                columns: new[] { "AttemptAnswerId", "OptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttemptAnswerChoices_MatchedOptionId",
                table: "AttemptAnswerChoices",
                column: "MatchedOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttemptAnswerChoices_OptionId",
                table: "AttemptAnswerChoices",
                column: "OptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttemptAnswerChoices");

            migrationBuilder.DropColumn(
                name: "AwardedPoints",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "ResponseText",
                table: "AttemptAnswers");
        }
    }
}
