using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentMethod_CardExpiration",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod_CardHolderName",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod_CardNumber",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentTransactionId",
                table: "Orders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentMethod_CardExpiration",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod_CardHolderName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod_CardNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentTransactionId",
                table: "Orders");
        }
    }
}
