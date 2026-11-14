using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AccuracyMeters",
                table: "SiteSurveys",
                type: "decimal(8,1)",
                precision: 8,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "SiteSurveys",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationNote",
                table: "SiteSurveys",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "SiteSurveys",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PipingMetres",
                table: "SiteSurveys",
                type: "decimal(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PumpPosition",
                table: "SiteSurveys",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TruckConnectionPoint",
                table: "SiteSurveys",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteSurveyId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SiteSurveyImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SurveyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSurveyImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteSurveyImages_SiteSurveys_SurveyId",
                        column: x => x.SurveyId,
                        principalTable: "SiteSurveys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteSurveyImages_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_SiteSurveyId",
                table: "Orders",
                column: "SiteSurveyId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteSurveyImages_SurveyId",
                table: "SiteSurveyImages",
                column: "SurveyId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteSurveyImages_TenantId",
                table: "SiteSurveyImages",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_SiteSurveys_SiteSurveyId",
                table: "Orders",
                column: "SiteSurveyId",
                principalTable: "SiteSurveys",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_SiteSurveys_SiteSurveyId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "SiteSurveyImages");

            migrationBuilder.DropIndex(
                name: "IX_Orders_SiteSurveyId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AccuracyMeters",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "LocationNote",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "PipingMetres",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "PumpPosition",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "TruckConnectionPoint",
                table: "SiteSurveys");

            migrationBuilder.DropColumn(
                name: "SiteSurveyId",
                table: "Orders");
        }
    }
}
