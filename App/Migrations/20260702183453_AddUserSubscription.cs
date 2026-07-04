using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    NextTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    KonnectCustomerId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    KonnectSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastChargeIntentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastChargeKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RenewingUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_KonnectSyncedAt",
                table: "UserSubscriptions",
                column: "KonnectSyncedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_Status_PeriodEnd",
                table: "UserSubscriptions",
                columns: ["Status", "PeriodEnd"]);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId",
                table: "UserSubscriptions",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSubscriptions");
        }
    }
}
