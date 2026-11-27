using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGradeScales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GradeScaleId",
                table: "ExamAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GradingMethod",
                table: "ExamAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GradeScales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Three = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Four = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Five = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Six = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeScales", x => x.Id);
                    table.CheckConstraint("CK_GradeScales_Range", "\"Three\" >= 0 AND \"Six\" <= 100");
                    table.CheckConstraint("CK_GradeScales_Rising", "\"Three\" < \"Four\" AND \"Four\" < \"Five\" AND \"Five\" < \"Six\"");
                    table.ForeignKey(
                        name: "FK_GradeScales_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAssignments_GradeScaleId",
                table: "ExamAssignments",
                column: "GradeScaleId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExamAssignments_GradingMethod",
                table: "ExamAssignments",
                sql: "\"GradingMethod\" BETWEEN 0 AND 2");

            migrationBuilder.CreateIndex(
                name: "IX_GradeScales_OwnerId_Name",
                table: "GradeScales",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ExamAssignments_GradeScales_GradeScaleId",
                table: "ExamAssignments",
                column: "GradeScaleId",
                principalTable: "GradeScales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamAssignments_GradeScales_GradeScaleId",
                table: "ExamAssignments");

            migrationBuilder.DropTable(
                name: "GradeScales");

            migrationBuilder.DropIndex(
                name: "IX_ExamAssignments_GradeScaleId",
                table: "ExamAssignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ExamAssignments_GradingMethod",
                table: "ExamAssignments");

            migrationBuilder.DropColumn(
                name: "GradeScaleId",
                table: "ExamAssignments");

            migrationBuilder.DropColumn(
                name: "GradingMethod",
                table: "ExamAssignments");
        }
    }
}
