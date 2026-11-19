using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCostApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "OverheadCosts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "OverheadCosts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "OverheadCosts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "OverheadCosts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "MaterialCosts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "MaterialCosts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "MaterialCosts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "MaterialCosts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "DeliveryChargeBands",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "DeliveryChargeBands",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "DeliveryChargeBands",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "DeliveryChargeBands",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            foreach (var table in new[] { "MaterialCosts", "OverheadCosts", "DeliveryChargeBands" })
            {
                migrationBuilder.Sql($"UPDATE {table} SET Status = 'Approved', ApprovedAtUtc = CreatedAtUtc WHERE Status = '';");
            }

            migrationBuilder.Sql(@"
                            INSERT INTO RolePermissions (TenantId, RoleId, PermissionKey)
                            SELECT r.TenantId, r.Id, 'costs.approve'
                            FROM Roles r
                            WHERE r.Name IN ('Admin', 'Management')
                            AND NOT EXISTS (
                                SELECT 1 FROM RolePermissions rp
                                WHERE rp.RoleId = r.Id AND rp.PermissionKey = 'costs.approve');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "OverheadCosts");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "OverheadCosts");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "OverheadCosts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OverheadCosts");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "MaterialCosts");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "MaterialCosts");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "MaterialCosts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "MaterialCosts");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "DeliveryChargeBands");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "DeliveryChargeBands");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "DeliveryChargeBands");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "DeliveryChargeBands");
        }
    }
}
