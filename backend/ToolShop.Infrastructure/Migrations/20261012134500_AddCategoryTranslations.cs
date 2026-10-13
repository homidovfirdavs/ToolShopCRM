using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToolShop.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive only: three nullable columns. Existing categories keep their Name untouched
            // (an empty translation falls back to Name), so no data is changed or removed.
            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                table: "Categories",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameRu",
                table: "Categories",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameTj",
                table: "Categories",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "NameEn", table: "Categories");
            migrationBuilder.DropColumn(name: "NameRu", table: "Categories");
            migrationBuilder.DropColumn(name: "NameTj", table: "Categories");
        }
    }
}
