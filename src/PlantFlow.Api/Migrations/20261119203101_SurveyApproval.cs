using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class SurveyApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewComment",
                table: "SiteSurveys",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "SiteSurveys",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                table: "SiteSurveys",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "SiteSurveys",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SiteSurveys_Status",
                table: "SiteSurveys",
                column: "Status");

            // Surveys recorded before approval existed count as approved and stay taggable.
            migrationBuilder.Sql("UPDATE SiteSurveys SET Status = 'Approved';");

            // Plant managers approve surveys. DefaultRoles only seeds new companies, so top up existing ones.
            migrationBuilder.Sql(@"
                INSERT INTO RolePermissions (TenantId, RoleId, PermissionKey)
                SELECT r.TenantId, r.Id, p.PermissionKey
                FROM Roles r
                CROSS JOIN (VALUES ('sitesurveys.approve'), ('sitesurveys.view')) p(PermissionKey)
                WHERE r.Name IN ('Admin', 'Plant Manager')
                AND NOT EXISTS (
                    SELECT 1 FROM RolePermissions rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionKey = p.PermissionKey);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SiteSurveys_Status",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "ReviewComment",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SiteSurveys");
        }
    }
}
