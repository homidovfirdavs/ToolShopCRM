using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ToolShop.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "cash_shift_number_seq");

            migrationBuilder.CreateTable(
                name: "CashRegisters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashRegisters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShiftNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CashRegisterId = table.Column<int>(type: "integer", nullable: false),
                    CashierUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OpenedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ClosingCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ExpectedCashAtClose = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CashDifference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CloseNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ClosedByOther = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresReview = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShifts", x => x.Id);
                    table.CheckConstraint("CK_CashShifts_Closed", "\"Status\" = 1 OR (\"ClosedAt\" IS NOT NULL AND \"ClosingCash\" IS NOT NULL AND \"ExpectedCashAtClose\" IS NOT NULL AND \"CashDifference\" IS NOT NULL)");
                    table.CheckConstraint("CK_CashShifts_OpeningCash", "\"OpeningCash\" >= 0");
                    table.ForeignKey(
                        name: "FK_CashShifts_CashRegisters_CashRegisterId",
                        column: x => x.CashRegisterId,
                        principalTable: "CashRegisters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CashShiftId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExpenseId = table.Column<int>(type: "integer", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsVoided = table.Column<bool>(type: "boolean", nullable: false),
                    VoidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.CheckConstraint("CK_CashMovements_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CashMovements_CashShifts_CashShiftId",
                        column: x => x.CashShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashShiftPayments",
                columns: table => new
                {
                    PaymentId = table.Column<int>(type: "integer", nullable: false),
                    CashShiftId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShiftPayments", x => x.PaymentId);
                    table.ForeignKey(
                        name: "FK_CashShiftPayments_CashShifts_CashShiftId",
                        column: x => x.CashShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashShiftPayments_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashShiftReturns",
                columns: table => new
                {
                    OrderReturnId = table.Column<int>(type: "integer", nullable: false),
                    CashShiftId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShiftReturns", x => x.OrderReturnId);
                    table.ForeignKey(
                        name: "FK_CashShiftReturns_CashShifts_CashShiftId",
                        column: x => x.CashShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashShiftReturns_OrderReturns_OrderReturnId",
                        column: x => x.OrderReturnId,
                        principalTable: "OrderReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashShiftId",
                table: "CashMovements",
                column: "CashShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ExpenseId",
                table: "CashMovements",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisters_Name",
                table: "CashRegisters",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashShiftPayments_CashShiftId",
                table: "CashShiftPayments",
                column: "CashShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShiftReturns_CashShiftId",
                table: "CashShiftReturns",
                column: "CashShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_CashierUserId",
                table: "CashShifts",
                column: "CashierUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_OpenedAt",
                table: "CashShifts",
                column: "OpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_ShiftNumber",
                table: "CashShifts",
                column: "ShiftNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CashShifts_OneOpenPerRegister",
                table: "CashShifts",
                column: "CashRegisterId",
                unique: true,
                filter: "\"Status\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropTable(
                name: "CashShiftPayments");

            migrationBuilder.DropTable(
                name: "CashShiftReturns");

            migrationBuilder.DropTable(
                name: "CashShifts");

            migrationBuilder.DropTable(
                name: "CashRegisters");

            migrationBuilder.DropSequence(
                name: "cash_shift_number_seq");
        }
    }
}
