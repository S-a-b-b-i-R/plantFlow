using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pours_Drivers_OperatorId",
                table: "Pours");

            migrationBuilder.CreateTable(
                name: "PumpOperators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PumpId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PumpOperators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PumpOperators_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PumpOperators_Vehicles_PumpId",
                        column: x => x.PumpId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PumpOperators_PumpId",
                table: "PumpOperators",
                column: "PumpId");

            migrationBuilder.CreateIndex(
                name: "IX_PumpOperators_TenantId",
                table: "PumpOperators",
                column: "TenantId");

            // The old crew names become pump operators on the same pump. They have no phone yet,
            // and the screens show it as missing until someone adds it.
            migrationBuilder.Sql(@"
                INSERT INTO PumpOperators (Id, TenantId, Name, Phone, PumpId, Status, CreatedAtUtc)
                SELECT Id, TenantId, Name, NULL, VehicleId, 'Active', SYSUTCDATETIME()
                FROM PumpCrewMembers;");

            migrationBuilder.DropTable(
                name: "PumpCrewMembers");

            // A pour's operator was a driver; now it comes from the pump's crew, so the old links go.
            migrationBuilder.Sql("UPDATE Pours SET OperatorId = NULL WHERE OperatorId IS NOT NULL;");

            // Drivers go with transit mixers only.
            migrationBuilder.Sql("UPDATE Vehicles SET DefaultDriverId = NULL WHERE Type = 'Pump' AND DefaultDriverId IS NOT NULL;");

            // fleet.manage for existing companies' Admin and Plant Manager roles.
            migrationBuilder.Sql(@"
                INSERT INTO RolePermissions (TenantId, RoleId, PermissionKey)
                SELECT r.TenantId, r.Id, 'fleet.manage'
                FROM Roles r
                WHERE r.Name IN ('Admin', 'Plant Manager')
                AND NOT EXISTS (
                    SELECT 1 FROM RolePermissions rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionKey = 'fleet.manage');");

            migrationBuilder.AddForeignKey(
                name: "FK_Pours_PumpOperators_OperatorId",
                table: "Pours",
                column: "OperatorId",
                principalTable: "PumpOperators",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pours_PumpOperators_OperatorId",
                table: "Pours");

            migrationBuilder.Sql("UPDATE Pours SET OperatorId = NULL WHERE OperatorId IS NOT NULL;");
            migrationBuilder.Sql("DELETE FROM RolePermissions WHERE PermissionKey = 'fleet.manage';");

            migrationBuilder.CreateTable(
                name: "PumpCrewMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_PumpCrewMembers_TenantId",
                table: "PumpCrewMembers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PumpCrewMembers_VehicleId_Name",
                table: "PumpCrewMembers",
                columns: new[] { "VehicleId", "Name" },
                unique: true);

            migrationBuilder.Sql(@"
                INSERT INTO PumpCrewMembers (Id, VehicleId, Name, TenantId)
                SELECT o.Id, o.PumpId, o.Name, o.TenantId FROM PumpOperators o
                WHERE o.PumpId IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM PumpOperators d
                    WHERE d.PumpId = o.PumpId AND d.Name = o.Name AND d.Id < o.Id);");

            migrationBuilder.DropTable(
                name: "PumpOperators");

            migrationBuilder.AddForeignKey(
                name: "FK_Pours_Drivers_OperatorId",
                table: "Pours",
                column: "OperatorId",
                principalTable: "Drivers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
