using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RoleSeeder only creates missing roles, so existing companies get the new permissions here.
            migrationBuilder.Sql(@"
                INSERT INTO RolePermissions (TenantId, RoleId, PermissionKey)
                SELECT r.TenantId, r.Id, p.PermissionKey
                FROM Roles r
                JOIN (VALUES
                    ('Admin', 'customers.manage'), ('Admin', 'customers.assign'), ('Admin', 'customers.vat'),
                    ('Sales Representative', 'customers.manage'),
                    ('Sales Manager', 'customers.manage'), ('Sales Manager', 'customers.assign'),
                    ('Accounts', 'customers.vat')) p(RoleName, PermissionKey) ON p.RoleName = r.Name
                WHERE NOT EXISTS (
                    SELECT 1 FROM RolePermissions rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionKey = p.PermissionKey);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM RolePermissions WHERE PermissionKey IN ('customers.manage', 'customers.assign', 'customers.vat');");
        }
    }
}
