using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnwantedCharityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Charities_DonationEnabled",
                table: "Charities");

            migrationBuilder.DropIndex(
                name: "IX_Charities_IsVerified",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "DonationEnabled",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "LastVerifiedAt",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "VerificationSource",
                table: "Charities");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Charities",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DonationEnabled",
                table: "Charities",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "Charities",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastVerifiedAt",
                table: "Charities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationSource",
                table: "Charities",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Charities_DonationEnabled",
                table: "Charities",
                column: "DonationEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_Charities_IsVerified",
                table: "Charities",
                column: "IsVerified");
        }
    }
}
