using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class ResourceManyToMany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Resources_Categories_CategoryId",
                table: "Resources");

            migrationBuilder.DropForeignKey(
                name: "FK_Resources_GradeLevels_GradeLevelId",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_CategoryId",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_Resources_GradeLevelId",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "GradeLevelId",
                table: "Resources");

            migrationBuilder.CreateTable(
                name: "CategoryResource",
                columns: table => new
                {
                    CategoriesId = table.Column<int>(type: "integer", nullable: false),
                    ResourcesId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryResource", x => new { x.CategoriesId, x.ResourcesId });
                    table.ForeignKey(
                        name: "FK_CategoryResource_Categories_CategoriesId",
                        column: x => x.CategoriesId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CategoryResource_Resources_ResourcesId",
                        column: x => x.ResourcesId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradeLevelResource",
                columns: table => new
                {
                    GradeLevelsId = table.Column<int>(type: "integer", nullable: false),
                    ResourcesId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeLevelResource", x => new { x.GradeLevelsId, x.ResourcesId });
                    table.ForeignKey(
                        name: "FK_GradeLevelResource_GradeLevels_GradeLevelsId",
                        column: x => x.GradeLevelsId,
                        principalTable: "GradeLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GradeLevelResource_Resources_ResourcesId",
                        column: x => x.ResourcesId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryResource_ResourcesId",
                table: "CategoryResource",
                column: "ResourcesId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeLevelResource_ResourcesId",
                table: "GradeLevelResource",
                column: "ResourcesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryResource");

            migrationBuilder.DropTable(
                name: "GradeLevelResource");

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Resources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GradeLevelId",
                table: "Resources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Resources_CategoryId",
                table: "Resources",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_GradeLevelId",
                table: "Resources",
                column: "GradeLevelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Resources_Categories_CategoryId",
                table: "Resources",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Resources_GradeLevels_GradeLevelId",
                table: "Resources",
                column: "GradeLevelId",
                principalTable: "GradeLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
