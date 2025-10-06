using Microsoft.EntityFrameworkCore.Migrations;

namespace App.Migrations
{
    public partial class ConfigurationsUserIdUnique : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fail fast if duplicates exist
            migrationBuilder.Sql(@"
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM (
      SELECT ""UserId"", COUNT(*) FROM ""Configurations"" GROUP BY ""UserId"" HAVING COUNT(*) > 1
    ) dup
  ) THEN
    RAISE EXCEPTION 'Duplicate user configurations exist. Please deduplicate before applying unique index.';
  END IF;
END
$$;
");

            migrationBuilder.CreateIndex(
                name: "IX_Configurations_UserId",
                table: "Configurations",
                column: "UserId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Configurations_UserId",
                table: "Configurations");
        }
    }
}
