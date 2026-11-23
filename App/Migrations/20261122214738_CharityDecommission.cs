using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class CharityDecommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DecommissionedAt",
                table: "Charities",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse to roll back once Decommission has been used: dropping DecommissionedAt would let payouts
            // send held money to decommissioned charities, and older code does not know the Held penalty
            // status (5). The rollback changes nothing.
            migrationBuilder.Sql(@"
LOCK TABLE ""Charities"", ""Penalties"" IN SHARE MODE; -- nothing can be decommissioned or held between this check and the DDL
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM ""Charities"" WHERE ""DecommissionedAt"" IS NOT NULL)
     OR EXISTS (SELECT 1 FROM ""Penalties"" WHERE ""Status"" = 5) THEN
    RAISE EXCEPTION 'Cannot roll back CharityDecommission: decommissioned charities or held penalties exist. Their money must stay held.';
  END IF;
END $$;");

            migrationBuilder.DropColumn(
                name: "DecommissionedAt",
                table: "Charities");
        }
    }
}
