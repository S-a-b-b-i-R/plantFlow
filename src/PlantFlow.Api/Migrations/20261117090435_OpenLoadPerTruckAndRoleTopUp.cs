using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class OpenLoadPerTruckAndRoleTopUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TruckLoads_TruckId",
                table: "TruckLoads");

            migrationBuilder.CreateIndex(
                name: "UX_TruckLoads_OneOpenLoadPerTruck",
                table: "TruckLoads",
                column: "TruckId",
                unique: true,
                filter: "[CancelledAtUtc] IS NULL AND [ReturnedAtUtc] IS NULL");

            migrationBuilder.Sql(@"
                                INSERT INTO RolePermissions (TenantId, RoleId, PermissionKey)
                                SELECT r.TenantId, r.Id, v.PermissionKey
                                FROM Roles r
                                JOIN (VALUES
                                    ('Batching Plant Operator', 'dashboards.view'),
                                    ('Batching Plant Operator', 'returns.manage')
                                ) AS v(RoleName, PermissionKey) ON v.RoleName = r.Name
                                WHERE NOT EXISTS (
                                    SELECT 1 FROM RolePermissions rp
                                    WHERE rp.RoleId = r.Id AND rp.PermissionKey = v.PermissionKey);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TruckLoads_OneOpenLoadPerTruck",
                table: "TruckLoads");

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoads_TruckId",
                table: "TruckLoads",
                column: "TruckId");
        }
    }
}
