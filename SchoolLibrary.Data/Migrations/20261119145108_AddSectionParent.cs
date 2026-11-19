using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSectionParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "Sections",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sections_ParentId",
                table: "Sections",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sections_Sections_ParentId",
                table: "Sections",
                column: "ParentId",
                principalTable: "Sections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sections_Sections_ParentId",
                table: "Sections");

            migrationBuilder.DropIndex(
                name: "IX_Sections_ParentId",
                table: "Sections");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Sections");
        }
    }
}
