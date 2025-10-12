using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "Charities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Mission = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    Countries = table.Column<string[]>(type: "text[]", nullable: false),
                    PrimaryRegistrationNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PrimaryRegistrationCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    WebsiteUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LogoUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Charities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Habits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Habits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AirwallexCustomerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PaymentConsentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PaymentConsentStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    Scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
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

            migrationBuilder.CreateTable(
                name: "Configurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EndOfDay = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DefaultCharityId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Configurations_Charities_DefaultCharityId",
                        column: x => x.DefaultCharityId,
                        principalTable: "Charities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CharityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIds_Charities_CharityId",
                        column: x => x.CharityId,
                        principalTable: "Charities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FreezeAwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    AwardedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HabitId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreezeAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FreezeAwards_Habits_HabitId",
                        column: x => x.HabitId,
                        principalTable: "Habits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HabitVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HabitId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Task = table.Column<string>(type: "text", nullable: false),
                    DaysOfWeek = table.Column<string[]>(type: "text[]", nullable: false),
                    NotificationTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    StakeCents = table.Column<int>(type: "integer", nullable: false),
                    StakeCurrency = table.Column<string>(type: "text", nullable: false),
                    RatioBasisPoints = table.Column<int>(type: "integer", nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HabitVersions_Charities_CharityId",
                        column: x => x.CharityId,
                        principalTable: "Charities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HabitVersions_Habits_HabitId",
                        column: x => x.HabitId,
                        principalTable: "Habits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FreezeConsumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreezeConsumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FreezeConsumptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProtections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FreezeCurrent = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProtections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProtections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VacationPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Timezone = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacationPeriods_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HabitExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HabitVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    PaymentProcessed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HabitExecutions_HabitVersions_HabitVersionId",
                        column: x => x.HabitVersionId,
                        principalTable: "HabitVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Causes_Key",
                table: "Causes",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Charities_Countries",
                table: "Charities",
                column: "Countries")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Charities_Name",
                table: "Charities",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Charities_PrimaryRegistrationCountry_PrimaryRegistrationNum~",
                table: "Charities",
                columns: [ "PrimaryRegistrationCountry", "PrimaryRegistrationNumber" ]);

            migrationBuilder.CreateIndex(
                name: "IX_CharityCauses_CauseId",
                table: "CharityCauses",
                column: "CauseId");

            migrationBuilder.CreateIndex(
                name: "IX_CharityCauses_CharityId_CauseId",
                table: "CharityCauses",
                columns: [ "CharityId", "CauseId" ],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Configurations_DefaultCharityId",
                table: "Configurations",
                column: "DefaultCharityId");

            migrationBuilder.CreateIndex(
                name: "IX_Configurations_UserId",
                table: "Configurations",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIds_CharityId",
                table: "ExternalIds",
                column: "CharityId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIds_Source_ExternalKey",
                table: "ExternalIds",
                columns: [ "Source", "ExternalKey" ],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FreezeAwards_HabitId_WeekStart",
                table: "FreezeAwards",
                columns: [ "HabitId", "WeekStart" ],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FreezeConsumptions_UserId_Date",
                table: "FreezeConsumptions",
                columns: [ "UserId", "Date" ],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HabitExecutions_HabitVersionId_Date",
                table: "HabitExecutions",
                columns: [ "HabitVersionId", "Date" ],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Habits_UserId",
                table: "Habits",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Habits_Version",
                table: "Habits",
                column: "Version");

            migrationBuilder.CreateIndex(
                name: "IX_HabitVersions_CharityId",
                table: "HabitVersions",
                column: "CharityId");

            migrationBuilder.CreateIndex(
                name: "IX_HabitVersions_HabitId_Version",
                table: "HabitVersions",
                columns: [ "HabitId", "Version" ],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCustomers_AirwallexCustomerId",
                table: "PaymentCustomers",
                column: "AirwallexCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCustomers_UserId",
                table: "PaymentCustomers",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProtections_UserId",
                table: "UserProtections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VacationPeriods_UserId",
                table: "VacationPeriods",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VacationPeriods_UserId_StartDate",
                table: "VacationPeriods",
                columns: [ "UserId", "StartDate" ]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharityCauses");

            migrationBuilder.DropTable(
                name: "Configurations");

            migrationBuilder.DropTable(
                name: "ExternalIds");

            migrationBuilder.DropTable(
                name: "FreezeAwards");

            migrationBuilder.DropTable(
                name: "FreezeConsumptions");

            migrationBuilder.DropTable(
                name: "HabitExecutions");

            migrationBuilder.DropTable(
                name: "PaymentCustomers");

            migrationBuilder.DropTable(
                name: "UserProtections");

            migrationBuilder.DropTable(
                name: "VacationPeriods");

            migrationBuilder.DropTable(
                name: "Causes");

            migrationBuilder.DropTable(
                name: "HabitVersions");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Charities");

            migrationBuilder.DropTable(
                name: "Habits");
        }
    }
}
