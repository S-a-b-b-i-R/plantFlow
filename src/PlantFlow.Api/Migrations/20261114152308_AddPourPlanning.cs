using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPourPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssemblyMinutes",
                table: "Vehicles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AverageSpeedKmh",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "PumpAssemblyMinutes",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<decimal>(
                name: "TruckLoadCft",
                table: "Tenants",
                type: "decimal(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 100m);

            migrationBuilder.CreateTable(
                name: "PumpCrewMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PumpCrewMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PumpCrewMembers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PumpCrewMembers_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReturnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftPlans_PlantShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "PlantShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftPlans_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftPlans_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SiteSurveys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SurveyedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SurveyedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSurveys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteSurveys_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteSurveys_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlannedCft = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    PumpId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OperatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CrewNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PumpLeaveAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    FirstTruckLeaveAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    EstimatedTruckLoads = table.Column<int>(type: "int", nullable: false),
                    RestOverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pours_Drivers_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pours_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pours_ShiftPlans_ShiftPlanId",
                        column: x => x.ShiftPlanId,
                        principalTable: "ShiftPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pours_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pours_Vehicles_PumpId",
                        column: x => x.PumpId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pours_OperatorId",
                table: "Pours",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Pours_OrderLineId",
                table: "Pours",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Pours_PumpId",
                table: "Pours",
                column: "PumpId");

            migrationBuilder.CreateIndex(
                name: "IX_Pours_ShiftPlanId",
                table: "Pours",
                column: "ShiftPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Pours_TenantId",
                table: "Pours",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PumpCrewMembers_TenantId",
                table: "PumpCrewMembers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PumpCrewMembers_VehicleId_Name",
                table: "PumpCrewMembers",
                columns: new[] { "VehicleId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPlans_PlantId_Date_ShiftId",
                table: "ShiftPlans",
                columns: new[] { "PlantId", "Date", "ShiftId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPlans_ShiftId",
                table: "ShiftPlans",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPlans_TenantId",
                table: "ShiftPlans",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteSurveys_SiteId_SurveyedOn",
                table: "SiteSurveys",
                columns: new[] { "SiteId", "SurveyedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteSurveys_TenantId",
                table: "SiteSurveys",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pours");

            migrationBuilder.DropTable(
                name: "PumpCrewMembers");

            migrationBuilder.DropTable(
                name: "SiteSurveys");

            migrationBuilder.DropTable(
                name: "ShiftPlans");

            migrationBuilder.DropColumn(
                name: "AssemblyMinutes",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AverageSpeedKmh",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PumpAssemblyMinutes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TruckLoadCft",
                table: "Tenants");
        }
    }
}
