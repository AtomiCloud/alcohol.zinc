using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class HabitSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Habits",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Number each user's live habits 0..n-1 by their current task name, then Id.
            // Deleted habits keep 0; no list reads them.
            migrationBuilder.Sql("""
                UPDATE "Habits" h SET "SortOrder" = s.rn
                FROM (
                    SELECT h2."Id",
                           ROW_NUMBER() OVER (PARTITION BY h2."UserId" ORDER BY lower(v."Task"), h2."Id") - 1 AS rn
                    FROM "Habits" h2
                    LEFT JOIN "HabitVersions" v ON v."HabitId" = h2."Id" AND v."Version" = h2."Version"
                    WHERE h2."DeletedAt" IS NULL
                ) s
                WHERE h."Id" = s."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Habits_UserId_SortOrder",
                table: "Habits",
                columns: ["UserId", "SortOrder"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Habits_UserId_SortOrder",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Habits");
        }
    }
}
