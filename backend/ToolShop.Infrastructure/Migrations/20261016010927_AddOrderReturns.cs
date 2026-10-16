using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ToolShop.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "return_number_seq");

            migrationBuilder.CreateTable(
                name: "OrderReturns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReturnNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    ReturnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReturnedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    GoodsValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CostReversed = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    WriteOffCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundMethod = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderReturns", x => x.Id);
                    table.CheckConstraint("CK_OrderReturns_GoodsValue", "\"GoodsValue\" >= 0");
                    table.CheckConstraint("CK_OrderReturns_RefundAmount", "\"RefundAmount\" >= 0");
                    table.CheckConstraint("CK_OrderReturns_RefundMethod", "\"RefundAmount\" = 0 OR \"RefundMethod\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_OrderReturns_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderReturnItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderReturnId = table.Column<int>(type: "integer", nullable: false),
                    OrderItemId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    RestockedQuantity = table.Column<int>(type: "integer", nullable: false),
                    WrittenOffQuantity = table.Column<int>(type: "integer", nullable: false),
                    NetValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CostAtSaleSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderReturnItems", x => x.Id);
                    table.CheckConstraint("CK_OrderReturnItems_NetValue", "\"NetValue\" >= 0");
                    table.CheckConstraint("CK_OrderReturnItems_Quantity", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_OrderReturnItems_Split", "\"RestockedQuantity\" >= 0 AND \"WrittenOffQuantity\" >= 0 AND \"RestockedQuantity\" + \"WrittenOffQuantity\" = \"Quantity\"");
                    table.ForeignKey(
                        name: "FK_OrderReturnItems_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderReturnItems_OrderReturns_OrderReturnId",
                        column: x => x.OrderReturnId,
                        principalTable: "OrderReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderReturnItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderReturnItems_OrderItemId",
                table: "OrderReturnItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderReturnItems_OrderReturnId",
                table: "OrderReturnItems",
                column: "OrderReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderReturnItems_ProductId",
                table: "OrderReturnItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderReturns_OrderId",
                table: "OrderReturns",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderReturns_ReturnedAt",
                table: "OrderReturns",
                column: "ReturnedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrderReturns_ReturnNumber",
                table: "OrderReturns",
                column: "ReturnNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderReturnItems");

            migrationBuilder.DropTable(
                name: "OrderReturns");

            migrationBuilder.DropSequence(
                name: "return_number_seq");
        }
    }
}
