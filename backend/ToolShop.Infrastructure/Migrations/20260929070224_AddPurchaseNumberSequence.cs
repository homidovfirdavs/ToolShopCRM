using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToolShop.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS purchase_number_seq START WITH 1 INCREMENT BY 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS purchase_number_seq;");
        }
    }
}