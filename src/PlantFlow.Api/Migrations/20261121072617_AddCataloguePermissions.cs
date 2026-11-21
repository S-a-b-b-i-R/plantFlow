using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCataloguePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // products.manage for Technical, charges.manage for Accounts, both for Admin, in existing companies.
            migrationBuilder.Sql(@"
                INSERT INTO RolePermissions (TenantId, RoleId, PermissionKey)
                SELECT r.TenantId, r.Id, v.PermissionKey
                FROM Roles r
                JOIN (VALUES ('Technical', 'products.manage'), ('Admin', 'products.manage'),
                             ('Accounts', 'charges.manage'), ('Admin', 'charges.manage')) v(RoleName, PermissionKey)
                    ON v.RoleName = r.Name
                WHERE NOT EXISTS (
                    SELECT 1 FROM RolePermissions rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionKey = v.PermissionKey);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM RolePermissions WHERE PermissionKey IN ('products.manage', 'charges.manage');");
        }
    }
}
