using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToolShop.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS payment_number_seq START WITH 1 INCREMENT BY 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS payment_number_seq;");
        }
    }
}