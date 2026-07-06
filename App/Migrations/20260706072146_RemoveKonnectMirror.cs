using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class RemoveKonnectMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptions_KonnectSyncedAt",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "KonnectCustomerId",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "KonnectSyncedAt",
                table: "UserSubscriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KonnectCustomerId",
                table: "UserSubscriptions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KonnectSyncedAt",
                table: "UserSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_KonnectSyncedAt",
                table: "UserSubscriptions",
                column: "KonnectSyncedAt");
        }
    }
}
