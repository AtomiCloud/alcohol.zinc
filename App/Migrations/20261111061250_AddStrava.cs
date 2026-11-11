using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class AddStrava : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StravaConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AthleteId = table.Column<long>(type: "bigint", nullable: false),
                    AccessToken = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    RefreshToken = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Scope = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StravaConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StravaConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StravaEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AspectType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ObjectId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    EventTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Deauthorized = table.Column<bool>(type: "boolean", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StravaEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HabitEvidence_ProofKey_ExternalId",
                table: "HabitEvidence",
                columns: ["ProofKey", "ExternalId"]);

            migrationBuilder.CreateIndex(
                name: "IX_StravaConnections_AthleteId",
                table: "StravaConnections",
                column: "AthleteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StravaConnections_UserId",
                table: "StravaConnections",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StravaEvents_ReceivedAtUtc",
                table: "StravaEvents",
                column: "ReceivedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StravaConnections");

            migrationBuilder.DropTable(
                name: "StravaEvents");

            migrationBuilder.DropIndex(
                name: "IX_HabitEvidence_ProofKey_ExternalId",
                table: "HabitEvidence");
        }
    }
}
