using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceFileCovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverContentType",
                table: "ResourceFiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverKey",
                table: "ResourceFiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceFiles_CoverKey",
                table: "ResourceFiles",
                column: "CoverKey",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResourceFiles_Cover",
                table: "ResourceFiles",
                sql: "(\"CoverKey\" IS NULL) = (\"CoverContentType\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResourceFiles_CoverContentType",
                table: "ResourceFiles",
                sql: "\"CoverContentType\" IN ('image/png', 'image/jpeg', 'image/gif', 'image/webp')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResourceFiles_CoverKey",
                table: "ResourceFiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResourceFiles_Cover",
                table: "ResourceFiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResourceFiles_CoverContentType",
                table: "ResourceFiles");

            migrationBuilder.DropColumn(
                name: "CoverContentType",
                table: "ResourceFiles");

            migrationBuilder.DropColumn(
                name: "CoverKey",
                table: "ResourceFiles");
        }
    }
}
