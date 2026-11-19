using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class SurveysFromOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "SiteSurveys",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SurveyRequested",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SurveyRequestedAtUtc",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SurveyRequestedBy",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteSurveys_OrderId",
                table: "SiteSurveys",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_SurveyRequested",
                table: "Orders",
                column: "SurveyRequested",
                filter: "[SurveyRequested] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_SiteSurveys_Orders_OrderId",
                table: "SiteSurveys",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Planning now uses only the order's tagged survey. Open orders that relied on the site's latest
            // survey are tagged with it, so they plan the same way they did before.
            migrationBuilder.Sql(@"
                UPDATE o SET o.SiteSurveyId = latest.Id
                FROM Orders o
                CROSS APPLY (
                    SELECT TOP 1 s.Id FROM SiteSurveys s
                    WHERE s.SiteId = o.SiteId AND s.TenantId = o.TenantId
                    ORDER BY s.SurveyedOn DESC, s.CreatedAtUtc DESC) latest
                WHERE o.SiteSurveyId IS NULL
                AND o.Status IN ('Draft', 'Submitted', 'InApproval', 'Approved', 'Confirmed', 'OnHold', 'InProgress');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SiteSurveys_Orders_OrderId",
                table: "SiteSurveys");

            migrationBuilder.DropIndex(
                name: "IX_SiteSurveys_OrderId",
                table: "SiteSurveys");

            migrationBuilder.DropIndex(
                name: "IX_Orders_SurveyRequested",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "SurveyRequested",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SurveyRequestedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SurveyRequestedBy",
                table: "Orders");
        }
    }
}
