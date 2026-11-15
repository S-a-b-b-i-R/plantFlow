using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "TruckLoads",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAtUtc",
                table: "TruckLoads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedCft",
                table: "TruckLoads",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceReturnId",
                table: "TruckLoads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReturnedConcretes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLoadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Fault = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    QuantityCft = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    RemainingCft = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DumpedCft = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnedConcretes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnedConcretes_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnedConcretes_ShiftPlans_ShiftPlanId",
                        column: x => x.ShiftPlanId,
                        principalTable: "ShiftPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnedConcretes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnedConcretes_TruckLoads_SourceLoadId",
                        column: x => x.SourceLoadId,
                        principalTable: "TruckLoads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoads_SourceReturnId",
                table: "TruckLoads",
                column: "SourceReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnedConcretes_PlantId",
                table: "ReturnedConcretes",
                column: "PlantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnedConcretes_ShiftPlanId_Status",
                table: "ReturnedConcretes",
                columns: new[] { "ShiftPlanId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnedConcretes_SourceLoadId",
                table: "ReturnedConcretes",
                column: "SourceLoadId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnedConcretes_TenantId",
                table: "ReturnedConcretes",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_TruckLoads_ReturnedConcretes_SourceReturnId",
                table: "TruckLoads",
                column: "SourceReturnId",
                principalTable: "ReturnedConcretes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TruckLoads_ReturnedConcretes_SourceReturnId",
                table: "TruckLoads");

            migrationBuilder.DropTable(
                name: "ReturnedConcretes");

            migrationBuilder.DropIndex(
                name: "IX_TruckLoads_SourceReturnId",
                table: "TruckLoads");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "TruckLoads");

            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                table: "TruckLoads");

            migrationBuilder.DropColumn(
                name: "ReturnedCft",
                table: "TruckLoads");

            migrationBuilder.DropColumn(
                name: "SourceReturnId",
                table: "TruckLoads");
        }
    }
}
