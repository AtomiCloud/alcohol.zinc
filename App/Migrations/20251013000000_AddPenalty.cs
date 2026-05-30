using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class AddPenalty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharityBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CharityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccruedCents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharityBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharityBalances_Charities_CharityId",
                        column: x => x.CharityId,
                        principalTable: "Charities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Penalties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HabitExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountCents = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaymentIntentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CharityId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Penalties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Penalties_Charities_CharityId",
                        column: x => x.CharityId,
                        principalTable: "Charities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Penalties_HabitExecutions_HabitExecutionId",
                        column: x => x.HabitExecutionId,
                        principalTable: "HabitExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Penalties_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharityBalances_CharityId",
                table: "CharityBalances",
                column: "CharityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_CharityId",
                table: "Penalties",
                column: "CharityId");

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_HabitExecutionId",
                table: "Penalties",
                column: "HabitExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_Status_Attempts",
                table: "Penalties",
                columns: [ "Status", "Attempts" ]);

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_UserId",
                table: "Penalties",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharityBalances");

            migrationBuilder.DropTable(
                name: "Penalties");
        }
    }
}
