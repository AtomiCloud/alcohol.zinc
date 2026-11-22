using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class CharityDeleteSwitchesOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CharityBalances_Charities_CharityId",
                table: "CharityBalances");

            migrationBuilder.DropForeignKey(
                name: "FK_Configurations_Charities_DefaultCharityId",
                table: "Configurations");

            migrationBuilder.DropForeignKey(
                name: "FK_Disbursements_Charities_CharityId",
                table: "Disbursements");

            migrationBuilder.DropForeignKey(
                name: "FK_HabitVersions_Charities_CharityId",
                table: "HabitVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_Penalties_Charities_CharityId",
                table: "Penalties");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Charities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CharityBalances_Charities_CharityId",
                table: "CharityBalances",
                column: "CharityId",
                principalTable: "Charities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Configurations_Charities_DefaultCharityId",
                table: "Configurations",
                column: "DefaultCharityId",
                principalTable: "Charities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Disbursements_Charities_CharityId",
                table: "Disbursements",
                column: "CharityId",
                principalTable: "Charities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HabitVersions_Charities_CharityId",
                table: "HabitVersions",
                column: "CharityId",
                principalTable: "Charities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Penalties_Charities_CharityId",
                table: "Penalties",
                column: "CharityId",
                principalTable: "Charities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse to roll back while any charity is switched off: dropping DeletedAt would put deleted
            // charities back into search and restore ON DELETE CASCADE, so a later hard delete would
            // erase their penalties, balances, payouts and habit history. The rollback changes nothing.
            migrationBuilder.Sql(@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM ""Charities"" WHERE ""DeletedAt"" IS NOT NULL) THEN
    RAISE EXCEPTION 'Cannot roll back CharityDeleteSwitchesOff: switched-off charities exist. Their money records must be kept.';
  END IF;
END $$;");

            migrationBuilder.DropForeignKey(
                name: "FK_CharityBalances_Charities_CharityId",
                table: "CharityBalances");

            migrationBuilder.DropForeignKey(
                name: "FK_Configurations_Charities_DefaultCharityId",
                table: "Configurations");

            migrationBuilder.DropForeignKey(
                name: "FK_Disbursements_Charities_CharityId",
                table: "Disbursements");

            migrationBuilder.DropForeignKey(
                name: "FK_HabitVersions_Charities_CharityId",
                table: "HabitVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_Penalties_Charities_CharityId",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Charities");

            migrationBuilder.AddForeignKey(
                name: "FK_CharityBalances_Charities_CharityId",
                table: "CharityBalances",
                column: "CharityId",
                principalTable: "Charities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Configurations_Charities_DefaultCharityId",
                table: "Configurations",
                column: "DefaultCharityId",
                principalTable: "Charities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Disbursements_Charities_CharityId",
                table: "Disbursements",
                column: "CharityId",
                principalTable: "Charities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HabitVersions_Charities_CharityId",
                table: "HabitVersions",
                column: "CharityId",
                principalTable: "Charities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Penalties_Charities_CharityId",
                table: "Penalties",
                column: "CharityId",
                principalTable: "Charities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
