using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class AddProofBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProofBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HabitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PluginVersion = table.Column<int>(type: "integer", nullable: false),
                    Settings = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProofBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProofBindings_Habits_HabitId",
                        column: x => x.HabitId,
                        principalTable: "Habits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProofBindings_HabitId",
                table: "ProofBindings",
                column: "HabitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProofBindings_PluginKey",
                table: "ProofBindings",
                column: "PluginKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProofBindings");
        }
    }
}
