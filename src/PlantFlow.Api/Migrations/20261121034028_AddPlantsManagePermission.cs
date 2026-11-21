using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantsManagePermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RoleSeeder only creates missing roles, so existing companies get the new permission here.
            migrationBuilder.Sql(@"
                INSERT INTO RolePermissions (TenantId, RoleId, PermissionKey)
                SELECT r.TenantId, r.Id, 'plants.manage'
                FROM Roles r
                WHERE r.Name IN ('Admin', 'Plant Manager')
                AND NOT EXISTS (
                    SELECT 1 FROM RolePermissions rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionKey = 'plants.manage');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM RolePermissions WHERE PermissionKey = 'plants.manage';");
        }
    }
}
