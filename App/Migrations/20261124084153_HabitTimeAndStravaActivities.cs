using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class HabitTimeAndStravaActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeadlineTime",
                table: "HabitVersions");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "NotificationTime",
                table: "HabitVersions",
                type: "time without time zone",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "HabitTime",
                table: "HabitVersions",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StravaActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SportType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Manual = table.Column<bool>(type: "boolean", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HabitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Pinned = table.Column<bool>(type: "boolean", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StravaActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StravaActivities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StravaActivities_ActivityId_UserId",
                table: "StravaActivities",
                columns: ["ActivityId", "UserId"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StravaActivities_FetchedAtUtc",
                table: "StravaActivities",
                column: "FetchedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StravaActivities_UserId_LocalDate",
                table: "StravaActivities",
                columns: ["UserId", "LocalDate"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StravaActivities");

            migrationBuilder.DropColumn(
                name: "HabitTime",
                table: "HabitVersions");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "NotificationTime",
                table: "HabitVersions",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0),
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DeadlineTime",
                table: "HabitVersions",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));
        }
    }
}
