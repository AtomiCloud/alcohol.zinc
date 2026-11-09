using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Migrations
{
    /// <inheritdoc />
    public partial class CoreChecksAndRoutines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "DeadlineTime",
                table: "HabitVersions",
                type: "time without time zone",
                nullable: false,
                // Existing versions, and versions written by pods still on the previous
                // release during the rollout, keep today's rule: the day ends at midnight.
                defaultValue: new TimeOnly(23, 59, 0));

            migrationBuilder.AddColumn<Guid>(
                name: "RoutineId",
                table: "Habits",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HabitChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HabitId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpensAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SettledBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HabitChecks_HabitExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "HabitExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HabitChecks_Habits_HabitId",
                        column: x => x.HabitId,
                        principalTable: "Habits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Routines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CheckEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckId = table.Column<Guid>(type: "uuid", nullable: false),
                    HabitId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Outcome = table.Column<byte>(type: "smallint", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DispatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckEvents_HabitChecks_CheckId",
                        column: x => x.CheckId,
                        principalTable: "HabitChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HabitEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HabitId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProofKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Data = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HabitEvidence_HabitChecks_CheckId",
                        column: x => x.CheckId,
                        principalTable: "HabitChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckEventDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    BindingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckEventDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckEventDeliveries_CheckEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "CheckEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Habits_RoutineId",
                table: "Habits",
                column: "RoutineId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckEventDeliveries_EventId_BindingId",
                table: "CheckEventDeliveries",
                columns: ["EventId", "BindingId"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckEvents_CheckId",
                table: "CheckEvents",
                column: "CheckId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckEvents_DispatchedAtUtc_NextAttemptAtUtc",
                table: "CheckEvents",
                columns: ["DispatchedAtUtc", "NextAttemptAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_HabitChecks_ExecutionId",
                table: "HabitChecks",
                column: "ExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HabitChecks_HabitId_LocalDate",
                table: "HabitChecks",
                columns: ["HabitId", "LocalDate"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HabitChecks_Status_DeadlineUtc",
                table: "HabitChecks",
                columns: ["Status", "DeadlineUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_HabitEvidence_CheckId",
                table: "HabitEvidence",
                column: "CheckId");

            migrationBuilder.CreateIndex(
                name: "IX_HabitEvidence_HabitId_ProofKey_ExternalId",
                table: "HabitEvidence",
                columns: ["HabitId", "ProofKey", "ExternalId"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routines_UserId",
                table: "Routines",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Habits_Routines_RoutineId",
                table: "Habits",
                column: "RoutineId",
                principalTable: "Routines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Every existing habit becomes its own one-habit routine (the routine reuses the
            // habit's id, so this is idempotent). Deleted habits get deleted routines.
            migrationBuilder.Sql(@"
                INSERT INTO ""Routines"" (""Id"", ""UserId"", ""Name"", ""CreatedAt"", ""DeletedAt"")
                SELECT h.""Id"", h.""UserId"", LEFT(COALESCE(hv.""Task"", 'Routine'), 256), now(), h.""DeletedAt""
                FROM ""Habits"" h
                LEFT JOIN ""HabitVersions"" hv ON hv.""HabitId"" = h.""Id"" AND hv.""Version"" = h.""Version""
                WHERE h.""RoutineId"" IS NULL
                ON CONFLICT (""Id"") DO NOTHING;

                UPDATE ""Habits"" SET ""RoutineId"" = ""Id"" WHERE ""RoutineId"" IS NULL;
            ");

            // Hand over from the midnight failure job: open yesterday's and today's checks in
            // the user's timezone, so a day the old job hadn't settled yet is still settled
            // (days it already recorded are only adopted: no second penalty, no freeze).
            migrationBuilder.Sql(@"
                INSERT INTO ""HabitChecks"" (""Id"", ""HabitId"", ""LocalDate"", ""Timezone"", ""OpensAtUtc"", ""DeadlineUtc"", ""Status"")
                SELECT gen_random_uuid(), x.habit_id, x.local_date, x.tz,
                       (x.local_date::timestamp AT TIME ZONE x.tz),
                       ((x.local_date + x.deadline)::timestamp AT TIME ZONE x.tz) + interval '1 minute',
                       0
                FROM (
                    SELECT h.""Id"" AS habit_id, z.tz, hv.""DeadlineTime"" AS deadline, hv.""DaysOfWeek"" AS days,
                           (now() AT TIME ZONE z.tz)::date - d.back AS local_date
                    FROM ""Habits"" h
                    JOIN ""HabitVersions"" hv ON hv.""HabitId"" = h.""Id"" AND hv.""Version"" = h.""Version""
                    LEFT JOIN ""Configurations"" c ON c.""UserId"" = h.""UserId""
                    CROSS JOIN LATERAL (
                        SELECT CASE WHEN c.""Timezone"" IN (SELECT name FROM pg_timezone_names)
                                    THEN c.""Timezone"" ELSE hv.""Timezone"" END AS tz
                    ) z
                    CROSS JOIN (VALUES (0), (1)) AS d(back)
                    WHERE h.""Enabled"" AND h.""DeletedAt"" IS NULL
                      AND z.tz IN (SELECT name FROM pg_timezone_names)
                ) x
                WHERE x.days @> ARRAY[trim(to_char(x.local_date, 'FMDay'))]
                ON CONFLICT (""HabitId"", ""LocalDate"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Habits_Routines_RoutineId",
                table: "Habits");

            migrationBuilder.DropTable(
                name: "CheckEventDeliveries");

            migrationBuilder.DropTable(
                name: "HabitEvidence");

            migrationBuilder.DropTable(
                name: "Routines");

            migrationBuilder.DropTable(
                name: "CheckEvents");

            migrationBuilder.DropTable(
                name: "HabitChecks");

            migrationBuilder.DropIndex(
                name: "IX_Habits_RoutineId",
                table: "Habits");

            migrationBuilder.DropColumn(
                name: "DeadlineTime",
                table: "HabitVersions");

            migrationBuilder.DropColumn(
                name: "RoutineId",
                table: "Habits");
        }
    }
}
