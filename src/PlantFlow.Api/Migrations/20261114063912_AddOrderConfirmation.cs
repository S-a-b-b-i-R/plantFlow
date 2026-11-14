using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmationMethod",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationReference",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAtUtc",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedBy",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConfirmedTotal",
                table: "Orders",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ForceManagement",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "HeldFromStatus",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HoldReason",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmationMethod",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConfirmationReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConfirmedBy",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConfirmedTotal",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ForceManagement",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "HeldFromStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "HoldReason",
                table: "Orders");
        }
    }
}
