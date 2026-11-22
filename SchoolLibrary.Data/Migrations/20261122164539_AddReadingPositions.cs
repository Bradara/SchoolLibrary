using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReadingPositions",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<int>(type: "integer", nullable: false),
                    Cfi = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Percent = table.Column<int>(type: "integer", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingPositions", x => new { x.UserId, x.ResourceId });
                    table.CheckConstraint("CK_ReadingPositions_Percent", "\"Percent\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_ReadingPositions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingPositions_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingPositions_ResourceId",
                table: "ReadingPositions",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingPositions_UserId_UpdatedOn",
                table: "ReadingPositions",
                columns: new[] { "UserId", "UpdatedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingPositions");
        }
    }
}
