using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class AddHabitPausedByLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Habits",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "PausedByLimit",
                table: "Habits",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Over-cap pausing keeps the OLDEST habits active, so pre-existing rows
            // need a plausible age: their earliest recorded execution date, falling
            // back to migration time for habits never executed. Ranking ties are
            // broken by Id, so equal timestamps stay deterministic.
            migrationBuilder.Sql(@"
                UPDATE ""Habits"" h SET ""CreatedAt"" = COALESCE(
                    (SELECT MIN(he.""Date"")::timestamptz
                     FROM ""HabitVersions"" hv
                     JOIN ""HabitExecutions"" he ON he.""HabitVersionId"" = hv.""Id""
                     WHERE hv.""HabitId"" = h.""Id""),
                    now())
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "PausedByLimit",
                table: "Habits");
        }
    }
}
