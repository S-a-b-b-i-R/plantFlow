using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LedgerCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginalCft = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    BalanceCft = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerCredits_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCredits_OrderLines_SourceOrderLineId",
                        column: x => x.SourceOrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCredits_Orders_SourceOrderId",
                        column: x => x.SourceOrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCredits_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCredits_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerCloses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityCft = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerCloses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerCloses_LedgerCredits_CreditId",
                        column: x => x.CreditId,
                        principalTable: "LedgerCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCloses_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityCft = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerCorrections_LedgerCredits_CreditId",
                        column: x => x.CreditId,
                        principalTable: "LedgerCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCorrections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerCreditUses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityCft = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerCreditUses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerCreditUses_LedgerCredits_CreditId",
                        column: x => x.CreditId,
                        principalTable: "LedgerCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCreditUses_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCreditUses_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerCreditUses_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCloses_CreditId",
                table: "LedgerCloses",
                column: "CreditId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCloses_TenantId_Status",
                table: "LedgerCloses",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCorrections_CreditId",
                table: "LedgerCorrections",
                column: "CreditId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCorrections_TenantId",
                table: "LedgerCorrections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCredits_CustomerId",
                table: "LedgerCredits",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCredits_ProductId",
                table: "LedgerCredits",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCredits_SourceOrderId",
                table: "LedgerCredits",
                column: "SourceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCredits_SourceOrderLineId",
                table: "LedgerCredits",
                column: "SourceOrderLineId",
                unique: true,
                filter: "[SourceOrderLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCredits_TenantId_CustomerId_ProductId",
                table: "LedgerCredits",
                columns: new[] { "TenantId", "CustomerId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCreditUses_CreditId",
                table: "LedgerCreditUses",
                column: "CreditId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCreditUses_OrderId",
                table: "LedgerCreditUses",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCreditUses_OrderLineId",
                table: "LedgerCreditUses",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerCreditUses_TenantId",
                table: "LedgerCreditUses",
                column: "TenantId");

            // ledger.approve for Sales Manager in existing companies. Admin always has every permission.
            migrationBuilder.Sql(@"
                INSERT INTO RolePermissions (TenantId, RoleId, PermissionKey)
                SELECT r.TenantId, r.Id, 'ledger.approve'
                FROM Roles r
                WHERE r.SystemKey IN ('sales-manager')
                AND NOT EXISTS (
                    SELECT 1 FROM RolePermissions rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionKey = 'ledger.approve');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM RolePermissions WHERE PermissionKey = 'ledger.approve';");

            migrationBuilder.DropTable(
                name: "LedgerCloses");

            migrationBuilder.DropTable(
                name: "LedgerCorrections");

            migrationBuilder.DropTable(
                name: "LedgerCreditUses");

            migrationBuilder.DropTable(
                name: "LedgerCredits");
        }
    }
}
