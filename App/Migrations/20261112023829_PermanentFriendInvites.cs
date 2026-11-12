using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class PermanentFriendInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FriendInvites_UserId",
                table: "FriendInvites");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "FriendInvites");

            // One invite per user from now on: keep each user's oldest code, the one most likely shared.
            migrationBuilder.Sql("""
                DELETE FROM "FriendInvites" a
                USING "FriendInvites" b
                WHERE a."UserId" = b."UserId"
                  AND (a."CreatedAtUtc", a."Code") > (b."CreatedAtUtc", b."Code");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FriendInvites_UserId",
                table: "FriendInvites",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FriendInvites_UserId",
                table: "FriendInvites");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "FriendInvites",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_FriendInvites_UserId",
                table: "FriendInvites",
                column: "UserId");
        }
    }
}
