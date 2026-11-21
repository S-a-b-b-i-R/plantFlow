using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerOwnershipDays",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<DateTime>(
                name: "OpenCustomersSeenUtc",
                table: "SalesReps",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OwnershipStartedUtc",
                table: "Customers",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Existing customers start their clock on release day, so no rep loses a customer straight away.
            // Customers without a rep count as open from today, and every rep's badge starts from today.
            migrationBuilder.Sql("UPDATE Customers SET OwnershipStartedUtc = SYSUTCDATETIME();");
            migrationBuilder.Sql("UPDATE SalesReps SET OpenCustomersSeenUtc = SYSUTCDATETIME();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerOwnershipDays",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "OpenCustomersSeenUtc",
                table: "SalesReps");

            migrationBuilder.DropColumn(
                name: "OwnershipStartedUtc",
                table: "Customers");
        }
    }
}
