using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PumpArrivedAtUtc",
                table: "Pours",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PumpAssembledAtUtc",
                table: "Pours",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PumpDispatchedAtUtc",
                table: "Pours",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TruckLoads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PourId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TruckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    QuantityCft = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    QuantityReason = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    ProducedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProducedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DispatchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DispatchedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ArrivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PouringStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PouringEndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TruckLoads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TruckLoads_Pours_PourId",
                        column: x => x.PourId,
                        principalTable: "Pours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TruckLoads_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TruckLoads_Vehicles_TruckId",
                        column: x => x.TruckId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoads_PourId",
                table: "TruckLoads",
                column: "PourId");

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoads_TenantId",
                table: "TruckLoads",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoads_TruckId",
                table: "TruckLoads",
                column: "TruckId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TruckLoads");

            migrationBuilder.DropColumn(
                name: "PumpArrivedAtUtc",
                table: "Pours");

            migrationBuilder.DropColumn(
                name: "PumpAssembledAtUtc",
                table: "Pours");

            migrationBuilder.DropColumn(
                name: "PumpDispatchedAtUtc",
                table: "Pours");
        }
    }
}
