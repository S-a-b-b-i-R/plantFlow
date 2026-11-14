using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVatOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VatOnConcrete",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "VatApplies",
                table: "ServiceCharges",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "VatExempt",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "VatOnConcrete",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "VatApplies",
                table: "OrderCharges",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "VatExempt",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VatOnConcrete",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "VatApplies",
                table: "ServiceCharges");

            migrationBuilder.DropColumn(
                name: "VatExempt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VatOnConcrete",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VatApplies",
                table: "OrderCharges");

            migrationBuilder.DropColumn(
                name: "VatExempt",
                table: "Customers");
        }
    }
}
