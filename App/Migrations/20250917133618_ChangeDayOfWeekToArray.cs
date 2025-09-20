using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class ChangeDayOfWeekToArray : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the new DaysOfWeek array column first
            migrationBuilder.AddColumn<string[]>(
                name: "DaysOfWeek",
                table: "HabitVersions",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            // Migrate existing data: convert single DayOfWeek to array
            migrationBuilder.Sql(@"
                UPDATE ""HabitVersions""
                SET ""DaysOfWeek"" = ARRAY[""DayOfWeek""]
                WHERE ""DayOfWeek"" IS NOT NULL AND ""DayOfWeek"" != ''
            ");

            // Drop the old DayOfWeek column
            migrationBuilder.DropColumn(
                name: "DayOfWeek",
                table: "HabitVersions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DaysOfWeek",
                table: "HabitVersions");

            migrationBuilder.AddColumn<string>(
                name: "DayOfWeek",
                table: "HabitVersions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
