using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AirwallexCustomerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PaymentConsentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PaymentConsentStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCustomers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCustomers_AirwallexCustomerId",
                table: "PaymentCustomers",
                column: "AirwallexCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCustomers_UserId",
                table: "PaymentCustomers",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentCustomers");
        }
    }
}
