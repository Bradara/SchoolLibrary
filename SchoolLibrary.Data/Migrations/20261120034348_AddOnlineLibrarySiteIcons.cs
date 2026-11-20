using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineLibrarySiteIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SiteIconContentType",
                table: "OnlineLibraries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteIconKey",
                table: "OnlineLibraries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteIconSourceUrl",
                table: "OnlineLibraries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SiteIconContentType",
                table: "OnlineLibraries");

            migrationBuilder.DropColumn(
                name: "SiteIconKey",
                table: "OnlineLibraries");

            migrationBuilder.DropColumn(
                name: "SiteIconSourceUrl",
                table: "OnlineLibraries");
        }
    }
}
