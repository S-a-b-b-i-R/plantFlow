using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMixDesigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MixDesigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MixDesigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MixDesigns_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MixDesigns_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MixDesigns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MixDesignItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MixDesignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityPerCft = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MixDesignItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MixDesignItems_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MixDesignItems_MixDesigns_MixDesignId",
                        column: x => x.MixDesignId,
                        principalTable: "MixDesigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MixDesignItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MixDesignItems_MaterialId",
                table: "MixDesignItems",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MixDesignItems_MixDesignId_MaterialId",
                table: "MixDesignItems",
                columns: new[] { "MixDesignId", "MaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MixDesignItems_TenantId",
                table: "MixDesignItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MixDesigns_OneOpen",
                table: "MixDesigns",
                columns: new[] { "TenantId", "ProductId", "PlantId" },
                unique: true,
                filter: "[Kind] = 'Standard' AND [Status] IN ('Draft', 'PendingApproval')");

            migrationBuilder.CreateIndex(
                name: "IX_MixDesigns_PlantId",
                table: "MixDesigns",
                column: "PlantId");

            migrationBuilder.CreateIndex(
                name: "IX_MixDesigns_ProductId",
                table: "MixDesigns",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_MixDesigns_Version",
                table: "MixDesigns",
                columns: new[] { "TenantId", "ProductId", "PlantId", "Version" },
                unique: true,
                filter: "[Kind] = 'Standard'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MixDesignItems");

            migrationBuilder.DropTable(
                name: "MixDesigns");
        }
    }
}
