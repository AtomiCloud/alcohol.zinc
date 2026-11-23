using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class RetainFriendRequestOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FriendRequests_FromUserId_ToUserId",
                table: "FriendRequests");

            migrationBuilder.AddColumn<Guid>(
                name: "FriendRequestId",
                table: "InboxNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "FriendRequests",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "FriendRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "FriendRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_InboxNotifications_FriendRequestId",
                table: "InboxNotifications",
                column: "FriendRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_FriendRequests_FromUserId_ToUserId",
                table: "FriendRequests",
                columns: ["FromUserId", "ToUserId"],
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FriendRequests_Outcome",
                table: "FriendRequests",
                sql: "\"Status\" BETWEEN 0 AND 3 AND ((\"Status\" = 0 AND \"ResolvedAtUtc\" IS NULL) OR (\"Status\" <> 0 AND \"ResolvedAtUtc\" IS NOT NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_InboxNotifications_FriendRequests_FriendRequestId",
                table: "InboxNotifications",
                column: "FriendRequestId",
                principalTable: "FriendRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Friend request outcomes must be retained. Recover forward; do not downgrade this migration.");
        }
    }
}
