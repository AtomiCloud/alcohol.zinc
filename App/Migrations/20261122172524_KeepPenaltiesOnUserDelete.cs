using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class KeepPenaltiesOnUserDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Penalties_HabitExecutions_HabitExecutionId",
                table: "Penalties");

            migrationBuilder.DropForeignKey(
                name: "FK_Penalties_Users_UserId",
                table: "Penalties");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "Penalties",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "HabitExecutionId",
                table: "Penalties",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_Penalties_HabitExecutions_HabitExecutionId",
                table: "Penalties",
                column: "HabitExecutionId",
                principalTable: "HabitExecutions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Penalties_Users_UserId",
                table: "Penalties",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse to roll back once any penalty has been detached: making the columns NOT NULL again
            // would need either deleting those money records or pointing them at a fake user/execution.
            // Neither is acceptable, so the rollback stops here and changes nothing.
            migrationBuilder.Sql(@"
LOCK TABLE ""Penalties"" IN SHARE MODE; -- no detach can commit between this check and the DDL
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM ""Penalties"" WHERE ""UserId"" IS NULL OR ""HabitExecutionId"" IS NULL) THEN
    RAISE EXCEPTION 'Cannot roll back KeepPenaltiesOnUserDelete: detached penalty records exist (deleted users). They must be kept.';
  END IF;
END $$;");

            migrationBuilder.DropForeignKey(
                name: "FK_Penalties_HabitExecutions_HabitExecutionId",
                table: "Penalties");

            migrationBuilder.DropForeignKey(
                name: "FK_Penalties_Users_UserId",
                table: "Penalties");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "Penalties",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "HabitExecutionId",
                table: "Penalties",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Penalties_HabitExecutions_HabitExecutionId",
                table: "Penalties",
                column: "HabitExecutionId",
                principalTable: "HabitExecutions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Penalties_Users_UserId",
                table: "Penalties",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
