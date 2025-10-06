using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class CauseAndCharity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "Charities");

            migrationBuilder.RenameColumn(
                name: "Address",
                table: "Charities",
                newName: "WebsiteUrl");

            migrationBuilder.AddColumn<string[]>(
                name: "Countries",
                table: "Charities",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Charities",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DonationEnabled",
                table: "Charities",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "Charities",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastVerifiedAt",
                table: "Charities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Charities",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mission",
                table: "Charities",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryRegistrationCountry",
                table: "Charities",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryRegistrationNumber",
                table: "Charities",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Charities",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationSource",
                table: "Charities",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Causes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Causes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CharityCauses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CharityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CauseId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharityCauses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharityCauses_Causes_CauseId",
                        column: x => x.CauseId,
                        principalTable: "Causes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CharityCauses_Charities_CharityId",
                        column: x => x.CharityId,
                        principalTable: "Charities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Charities_Countries",
                table: "Charities",
                column: "Countries")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Charities_DonationEnabled",
                table: "Charities",
                column: "DonationEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_Charities_IsVerified",
                table: "Charities",
                column: "IsVerified");

            migrationBuilder.CreateIndex(
                name: "IX_Charities_Name",
                table: "Charities",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Charities_PrimaryRegistrationCountry_PrimaryRegistrationNum~",
                table: "Charities",
                columns: ["PrimaryRegistrationCountry", "PrimaryRegistrationNumber"]);

            migrationBuilder.CreateIndex(
                name: "IX_Causes_Key",
                table: "Causes",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CharityCauses_CauseId",
                table: "CharityCauses",
                column: "CauseId");

            migrationBuilder.CreateIndex(
                name: "IX_CharityCauses_CharityId_CauseId",
                table: "CharityCauses",
                columns: ["CharityId", "CauseId"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharityCauses");

            migrationBuilder.DropTable(
                name: "Causes");

            migrationBuilder.DropIndex(
                name: "IX_Charities_Countries",
                table: "Charities");

            migrationBuilder.DropIndex(
                name: "IX_Charities_DonationEnabled",
                table: "Charities");

            migrationBuilder.DropIndex(
                name: "IX_Charities_IsVerified",
                table: "Charities");

            migrationBuilder.DropIndex(
                name: "IX_Charities_Name",
                table: "Charities");

            migrationBuilder.DropIndex(
                name: "IX_Charities_PrimaryRegistrationCountry_PrimaryRegistrationNum~",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "Countries",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "DonationEnabled",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "LastVerifiedAt",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "Mission",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "PrimaryRegistrationCountry",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "PrimaryRegistrationNumber",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Charities");

            migrationBuilder.DropColumn(
                name: "VerificationSource",
                table: "Charities");

            migrationBuilder.RenameColumn(
                name: "WebsiteUrl",
                table: "Charities",
                newName: "Address");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Charities",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");
        }
    }
}
