using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
  /// <inheritdoc />
  public partial class AddDisbursement : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<Guid>(
          name: "DisbursementId",
          table: "Penalties",
          type: "uuid",
          nullable: true);

      migrationBuilder.CreateTable(
          name: "Disbursements",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            CharityId = table.Column<Guid>(type: "uuid", nullable: false),
            Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
            AmountCents = table.Column<long>(type: "bigint", nullable: false),
            Status = table.Column<int>(type: "integer", nullable: false),
            PledgeOrganizationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            ProviderDonationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
            Attempts = table.Column<int>(type: "integer", nullable: false),
            LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Disbursements", x => x.Id);
            table.ForeignKey(
                      name: "FK_Disbursements_Charities_CharityId",
                      column: x => x.CharityId,
                      principalTable: "Charities",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
          });

      migrationBuilder.CreateIndex(
          name: "IX_Penalties_DisbursementId",
          table: "Penalties",
          column: "DisbursementId");

      migrationBuilder.CreateIndex(
          name: "IX_Penalties_Status_DisbursementId",
          table: "Penalties",
          columns: ["Status", "DisbursementId"]);

      migrationBuilder.CreateIndex(
          name: "IX_Disbursements_CharityId",
          table: "Disbursements",
          column: "CharityId");

      migrationBuilder.CreateIndex(
          name: "IX_Disbursements_Status",
          table: "Disbursements",
          column: "Status");

      migrationBuilder.AddForeignKey(
          name: "FK_Penalties_Disbursements_DisbursementId",
          table: "Penalties",
          column: "DisbursementId",
          principalTable: "Disbursements",
          principalColumn: "Id",
          onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropForeignKey(
          name: "FK_Penalties_Disbursements_DisbursementId",
          table: "Penalties");

      migrationBuilder.DropTable(
          name: "Disbursements");

      migrationBuilder.DropIndex(
          name: "IX_Penalties_DisbursementId",
          table: "Penalties");

      migrationBuilder.DropIndex(
          name: "IX_Penalties_Status_DisbursementId",
          table: "Penalties");

      migrationBuilder.DropColumn(
          name: "DisbursementId",
          table: "Penalties");
    }
  }
}
