using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class HabitExecutionToPaymentIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentProcessed",
                table: "HabitExecutions");

            migrationBuilder.AlterColumn<byte>(
                name: "Status",
                table: "HabitExecutions",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "PaymentIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AirwallexPaymentIntentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AirwallexCustomerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CapturedAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MerchantOrderId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIntents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentIntentExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    HabitExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIntentExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentIntentExecutions_HabitExecutions_HabitExecutionId",
                        column: x => x.HabitExecutionId,
                        principalTable: "HabitExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentIntentExecutions_PaymentIntents_PaymentIntentId",
                        column: x => x.PaymentIntentId,
                        principalTable: "PaymentIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntentExecutions_HabitExecutionId",
                table: "PaymentIntentExecutions",
                column: "HabitExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntentExecutions_PaymentIntentId_HabitExecutionId",
                table: "PaymentIntentExecutions",
                columns: ["PaymentIntentId", "HabitExecutionId"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_AirwallexPaymentIntentId",
                table: "PaymentIntents",
                column: "AirwallexPaymentIntentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_MerchantOrderId",
                table: "PaymentIntents",
                column: "MerchantOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Status",
                table: "PaymentIntents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_UserId",
                table: "PaymentIntents",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentIntentExecutions");

            migrationBuilder.DropTable(
                name: "PaymentIntents");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "HabitExecutions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AddColumn<bool>(
                name: "PaymentProcessed",
                table: "HabitExecutions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
