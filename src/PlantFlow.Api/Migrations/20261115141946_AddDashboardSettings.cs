using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LateThresholdMinutes",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "UtcOffsetMinutes",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 360);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LateThresholdMinutes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "UtcOffsetMinutes",
                table: "Tenants");
        }
    }
}
