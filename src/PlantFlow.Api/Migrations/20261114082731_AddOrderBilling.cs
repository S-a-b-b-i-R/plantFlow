using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedBy",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceSubtotal",
                table: "Orders",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceTotal",
                table: "Orders",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceVatAmount",
                table: "Orders",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceVatPercent",
                table: "Orders",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BilledCft",
                table: "OrderLines",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingBasis",
                table: "OrderLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingNote",
                table: "OrderLines",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BillingResolvedAtUtc",
                table: "OrderLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BillingResolvedBy",
                table: "OrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveredCft",
                table: "OrderLines",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "OrderAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    QuantityCft = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderAdjustments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderAdjustments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderAdjustments_OrderId",
                table: "OrderAdjustments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderAdjustments_TenantId",
                table: "OrderAdjustments",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderAdjustments");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceSubtotal",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceTotal",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceVatAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceVatPercent",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BilledCft",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "BillingBasis",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "BillingNote",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "BillingResolvedAtUtc",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "BillingResolvedBy",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "DeliveredCft",
                table: "OrderLines");
        }
    }
}
