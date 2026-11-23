using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryAppearance : Migration
    {
        // A frozen copy of the standard looks (SubjectAppearance), so that the migration does not change
        // when the list in the code changes later. Subjects with other names keep the default look.
        private static readonly (string Name, string Icon, string Color)[] StandardLooks =
        {
            ("Български език и литература", "bi-book", "#e03131"),
            ("Английски език", "bi-translate", "#6741d9"),
            ("Немски език", "bi-translate", "#b7791f"),
            ("Френски език", "bi-translate", "#2f6fed"),
            ("Руски език", "bi-translate", "#d6336c"),
            ("Испански език", "bi-translate", "#e8590c"),
            ("Италиански език", "bi-translate", "#2f9e44"),
            ("Математика", "bi-plus-slash-minus", "#2f6fed"),
            ("Информатика", "bi-cpu", "#0891b2"),
            ("Информационни технологии", "bi-laptop", "#0891b2"),
            ("Компютърно моделиране", "bi-code-slash", "#0f9d8a"),
            ("Компютърно моделиране и информационни технологии", "bi-code-slash", "#0f9d8a"),
            ("Роден край", "bi-house-heart", "#2f9e44"),
            ("Околен свят", "bi-tree", "#2f9e44"),
            ("Човекът и обществото", "bi-people", "#b7791f"),
            ("Човекът и природата", "bi-flower1", "#5c940d"),
            ("История и цивилизации", "bi-hourglass-split", "#b7791f"),
            ("География и икономика", "bi-globe-europe-africa", "#2f9e44"),
            ("Философия", "bi-lightbulb", "#6741d9"),
            ("Гражданско образование", "bi-bank", "#2f6fed"),
            ("Биология и здравно образование", "bi-heart-pulse", "#5c940d"),
            ("Физика и астрономия", "bi-lightning-charge", "#e8590c"),
            ("Химия и опазване на околната среда", "bi-droplet", "#9c36b5"),
            ("Музика", "bi-music-note-beamed", "#d6336c"),
            ("Изобразително изкуство", "bi-palette", "#e03131"),
            ("Технологии и предприемачество", "bi-tools", "#6c757d"),
            ("Предприемачество", "bi-briefcase", "#b7791f"),
            ("Физическо възпитание и спорт", "bi-trophy", "#e8590c"),
            ("Религия", "bi-sun", "#b7791f"),
            ("Хореография", "bi-person-arms-up", "#d6336c")
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The defaults fill the existing rows, so the check constraints below hold for all of them
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Categories",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#6c757d");

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "Categories",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "bi-journal-bookmark");

            // The production database was seeded before this migration: give its standard subjects their look
            foreach (var (name, icon, color) in StandardLooks)
            {
                migrationBuilder.UpdateData(
                    table: "Categories",
                    keyColumn: "Name",
                    keyValue: name,
                    columns: new[] { "Icon", "Color" },
                    values: new object[] { icon, color });
            }

            migrationBuilder.AddCheckConstraint(
                name: "CK_Categories_Color",
                table: "Categories",
                sql: "\"Color\" ~ '^#[0-9a-f]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Categories_Icon",
                table: "Categories",
                sql: "\"Icon\" ~ '^bi-[a-z0-9-]+$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Categories_Color",
                table: "Categories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Categories_Icon",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "Categories");
        }
    }
}
