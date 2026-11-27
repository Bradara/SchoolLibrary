using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManualAnswerPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManualComment",
                table: "AttemptAnswers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManualGradedOn",
                table: "AttemptAnswers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ManualPoints",
                table: "AttemptAnswers",
                type: "numeric(7,2)",
                precision: 7,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttemptAnswers_ManualPoints",
                table: "AttemptAnswers",
                sql: "\"ManualPoints\" IS NULL OR \"ManualPoints\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AttemptAnswers_ManualPoints",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "ManualComment",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "ManualGradedOn",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "ManualPoints",
                table: "AttemptAnswers");
        }
    }
}
