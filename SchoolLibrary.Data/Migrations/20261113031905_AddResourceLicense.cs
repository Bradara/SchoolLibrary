using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceLicense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LicenseId",
                table: "Resources",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Resources_LicenseId",
                table: "Resources",
                column: "LicenseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Resources_Licenses_LicenseId",
                table: "Resources",
                column: "LicenseId",
                principalTable: "Licenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Resources_Licenses_LicenseId",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_LicenseId",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "LicenseId",
                table: "Resources");
        }
    }
}
