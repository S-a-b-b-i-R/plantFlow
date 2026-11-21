using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyJoining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JoinedVia",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Assigned");

            migrationBuilder.AddColumn<string>(
                name: "JoinCode",
                table: "Tenants",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "JoinCodeRenewedUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JoinMethod",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_JoinCode",
                table: "Tenants",
                column: "JoinCode",
                unique: true,
                filter: "[JoinCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_JoinCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "JoinedVia",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "JoinCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "JoinCodeRenewedUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "JoinMethod",
                table: "Tenants");
        }
    }
}
