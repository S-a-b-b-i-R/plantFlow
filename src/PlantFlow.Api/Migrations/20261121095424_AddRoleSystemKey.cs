using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleSystemKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SystemKey",
                table: "Roles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // Every built-in role still has its original name, because renaming only arrives with this change.
            migrationBuilder.Sql(@"
                UPDATE Roles SET SystemKey = LOWER(REPLACE(Name, ' ', '-'))
                WHERE Name IN ('Admin', 'Sales Representative', 'Sales Manager', 'Accounts', 'Technical', 'Credit',
                               'Planner', 'Dispatcher', 'Batching Plant Operator', 'Plant Manager', 'Management');");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_SystemKey",
                table: "Roles",
                columns: new[] { "TenantId", "SystemKey" },
                unique: true,
                filter: "[SystemKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roles_TenantId_SystemKey",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "SystemKey",
                table: "Roles");
        }
    }
}
