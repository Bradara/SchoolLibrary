using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeNormalizedUrlUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Resources_NormalizedUrl",
                table: "Resources");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_NormalizedUrl",
                table: "Resources",
                column: "NormalizedUrl",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Resources_NormalizedUrl",
                table: "Resources");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_NormalizedUrl",
                table: "Resources",
                column: "NormalizedUrl");
        }
    }
}
