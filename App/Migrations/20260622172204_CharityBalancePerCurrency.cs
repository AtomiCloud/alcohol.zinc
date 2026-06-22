using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class CharityBalancePerCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CharityBalances_CharityId",
                table: "CharityBalances");

            migrationBuilder.CreateIndex(
                name: "IX_CharityBalances_CharityId_Currency",
                table: "CharityBalances",
                columns: ["CharityId", "Currency"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CharityBalances_CharityId_Currency",
                table: "CharityBalances");

            migrationBuilder.CreateIndex(
                name: "IX_CharityBalances_CharityId",
                table: "CharityBalances",
                column: "CharityId",
                unique: true);
        }
    }
}
