using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    /// Moves stored Airwallex consents out of the PaymentCustomers columns into a
    /// purpose-scoped PaymentConsents table (penalty = 0, subscription = 1).
    /// DATA MIGRATION: existing consents are copied into the table as penalty
    /// consents BEFORE the old columns are dropped; Down() copies them back.
    public partial class SeparatePaymentConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. New table first — the copy needs both shapes to exist.
            migrationBuilder.CreateTable(
                name: "PaymentConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    ConsentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentCustomerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentConsents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentConsents_PaymentCustomers_PaymentCustomerId",
                        column: x => x.PaymentCustomerId,
                        principalTable: "PaymentCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentConsents_PaymentCustomerId_Purpose",
                table: "PaymentConsents",
                columns: ["PaymentCustomerId", "Purpose"],
                unique: true);

            // 2. Copy every existing consent as the PENALTY (unscheduled MIT)
            //    consent — that is what the single pre-split consent was used as.
            //    The customer's UpdatedAt is the best available consent timestamp.
            migrationBuilder.Sql("""
                INSERT INTO "PaymentConsents" ("Id", "PaymentCustomerId", "Purpose", "ConsentId", "Status", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), "Id", 0, "PaymentConsentId", "PaymentConsentStatus", "UpdatedAt", "UpdatedAt"
                FROM "PaymentCustomers"
                WHERE "PaymentConsentId" IS NOT NULL;
                """);

            // 3. Only now is it safe to drop the old columns.
            migrationBuilder.DropColumn(
                name: "PaymentConsentId",
                table: "PaymentCustomers");

            migrationBuilder.DropColumn(
                name: "PaymentConsentStatus",
                table: "PaymentCustomers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentConsentId",
                table: "PaymentCustomers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentConsentStatus",
                table: "PaymentCustomers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Restore the penalty consent into the legacy columns before the
            // table (and with it any subscription consents) is dropped.
            migrationBuilder.Sql("""
                UPDATE "PaymentCustomers" pc
                SET "PaymentConsentId" = c."ConsentId",
                    "PaymentConsentStatus" = c."Status"
                FROM "PaymentConsents" c
                WHERE c."PaymentCustomerId" = pc."Id" AND c."Purpose" = 0;
                """);

            migrationBuilder.DropTable(
                name: "PaymentConsents");
        }
    }
}
