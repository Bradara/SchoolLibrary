using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CheckPenaltyPercent",
                table: "Questions",
                type: "integer",
                nullable: false,
                defaultValue: 10); // as for new questions

            migrationBuilder.CreateTable(
                name: "CodeChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AttemptId = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    CheckedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsWrong = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeChecks_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CodeChecks_QuizAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "QuizAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeChecks_AttemptId_QuestionId",
                table: "CodeChecks",
                columns: new[] { "AttemptId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeChecks_QuestionId",
                table: "CodeChecks",
                column: "QuestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeChecks");

            migrationBuilder.DropColumn(
                name: "CheckPenaltyPercent",
                table: "Questions");
        }
    }
}
