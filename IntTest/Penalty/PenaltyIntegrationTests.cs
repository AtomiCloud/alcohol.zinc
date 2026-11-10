using App.Modules.Charities.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Penalty.Data;
using App.Modules.Protection.Data;
using App.Modules.Users.Data;
using App.Modules.Vacation.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Habit;
using Domain.Payment;
using Domain.Penalty;
using IntTest.Check;
using IntTest.Notification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaMoney;

namespace IntTest.Penalty;

// DB-backed integration tests proving exactly-once charge + accrual credit +
// no double-charge + no-consent Skipped against a REAL MainDbContext.
//
// The repository's MarkCharged uses a real DB transaction (BeginTransactionAsync)
// and the schema is Postgres-specific (MainDbContext hardcodes UseNpgsql +
// UseExceptionProcessor), so these tests run against a real Postgres instance.
// The connection is taken from the PENALTY_TEST_DB env var
// (e.g. "Host=localhost;Port=5432;Database=penalty_test;Username=postgres;Password=postgres;").
// When it is not set the tests Skip rather than fail, so the suite is green in
// environments without a database while still exercising the full L3->L6 path
// wherever Postgres is available (CI / the later build phase).
[Collection(DbIntegrationCollection.Name)]
public class PenaltyIntegrationTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed penalty integration test.";
  private readonly string? _conn = Environment.GetEnvironmentVariable("PENALTY_TEST_DB");
  private MainDbContext _db = null!;

  public async Task InitializeAsync()
  {
    if (_conn == null) return;
    _db = NewContext(_conn);
    await _db.Database.EnsureCreatedAsync();
  }

  public async Task DisposeAsync()
  {
    if (_conn == null) return;
    await _db.Database.EnsureDeletedAsync();
    await _db.DisposeAsync();
  }

  internal static MainDbContext NewContext(string conn)
  {
    var parts = conn.Split(';', StringSplitOptions.RemoveEmptyEntries)
      .Select(p => p.Split('=', 2))
      .Where(kv => kv.Length == 2)
      .ToDictionary(kv => kv[0].Trim().ToLowerInvariant(), kv => kv[1].Trim());

    var opt = new DatabaseOption
    {
      Host = parts.GetValueOrDefault("host", "localhost"),
      Port = int.TryParse(parts.GetValueOrDefault("port", "5432"), out var p) ? p : 5432,
      Database = parts.GetValueOrDefault("database", "penalty_test"),
      User = parts.GetValueOrDefault("username", parts.GetValueOrDefault("user", "postgres")),
      Password = parts.GetValueOrDefault("password", "postgres"),
      AutoMigrate = false,
      Timeout = 30
    };
    var monitor = new StaticOptionsMonitor<Dictionary<string, DatabaseOption>>(
      new Dictionary<string, DatabaseOption> { [MainDbContext.Key] = opt });
    return new MainDbContext(monitor, NullLoggerFactory.Instance);
  }

  private static PenaltyRepository Repo(MainDbContext db)
    => new(db, NullLogger<PenaltyRepository>.Instance);

  private static PenaltyService Service(IPenaltyRepository repo, IPaymentService payment)
    => new(repo, payment, new NoopEmailNotifier(), NullLogger<PenaltyService>.Instance);

  private async Task<(string userId, Guid charityId)> SeedUserAndCharity()
  {
    var userId = $"user-{Guid.NewGuid():N}";
    var charityId = Guid.NewGuid();
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    await _db.SaveChangesAsync();
    return (userId, charityId);
  }

  // Seeds the parent Habit -> HabitVersion -> HabitExecution graph required by the
  // FK_Penalties_HabitExecutions_HabitExecutionId constraint, returning the execution Id
  // to use as PenaltyRecord.HabitExecutionId.
  private async Task<Guid> SeedExecution(string userId, Guid charityId)
  {
    var habitId = Guid.NewGuid();
    var versionId = Guid.NewGuid();
    var executionId = Guid.NewGuid();
    _db.Habits.Add(new HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
    _db.HabitVersions.Add(new HabitVersionData
    {
      Id = versionId,
      HabitId = habitId,
      CharityId = charityId,
      Version = 1,
      Task = $"task-{versionId:N}",
      DaysOfWeek = ["Monday"],
      Timezone = "Asia/Singapore"
    });
    _db.HabitExecutions.Add(new HabitExecutionData
    {
      Id = executionId,
      HabitVersionId = versionId,
      Date = DateOnly.FromDateTime(DateTime.UtcNow),
      Status = HabitExecutionStatusData.Failed
    });
    await _db.SaveChangesAsync();
    return executionId;
  }

  private static PenaltyRecord PendingRecord(
    Guid executionId, string userId, Guid charityId, long amountCents, string currency = "USD")
    => new()
    {
      HabitExecutionId = executionId,
      UserId = userId,
      CharityId = charityId,
      Amount = new Money(amountCents / 100m, Currency.FromCode(currency)),
      Status = PenaltyStatus.Pending,
      PaymentIntentId = null,
      Attempts = 0,
      LastError = null
    };

  [SkippableTheory]
  [InlineData(null)]
  [InlineData("another-user")]
  public async Task HabitSearch_UsesGuardedRouteOwner_NotQueryOwner(string? queryOwner)
  {
    Skip.If(_conn == null, SkipReason);
    var (owner, charityId) = await SeedUserAndCharity();
    var (other, otherCharity) = await SeedUserAndCharity();
    var ownExecution = await SeedExecution(owner, charityId);
    await SeedExecution(other, otherCharity);
    var ownVersionId = await _db.HabitExecutions.Where(x => x.Id == ownExecution)
      .Select(x => x.HabitVersionId).SingleAsync();
    var service = new HabitService(new HabitRepository(_db, NullLogger<HabitRepository>.Instance),
      null!, null!, null!, NullLogger<HabitService>.Instance);
    var controller = new App.Modules.Habit.API.V1.HabitController(service, null!, null!, null!,
      null!, null!, new(), new(), new(), new())
    {
      ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
      {
        HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
          User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, owner)], "test"))
        }
      }
    };
    var query = new SearchHabitQuery(null, queryOwner == null ? null : other, null, null, 100, 0);

    var response = await controller.SearchHabits(owner, query);
    var ok = response.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.OkObjectResult>().Subject;
    var habits = ok.Value.Should().BeOfType<List<HabitVersionRes>>().Subject;
    habits.Should().ContainSingle().Which.Id.Should().Be(ownVersionId);

    var forbidden = await controller.SearchHabits(other, query);
    forbidden.Result.Should().BeAssignableTo<Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult>()
      .Which.StatusCode.Should().Be(403);
  }

  [SkippableTheory]
  [InlineData(HabitExecutionStatusData.Completed, 0)]
  [InlineData(HabitExecutionStatusData.Completed, 2)]
  [InlineData(HabitExecutionStatusData.Skipped, 2)]
  [InlineData(HabitExecutionStatusData.Frozen, 2)]
  [InlineData(HabitExecutionStatusData.Vacation, 2)]
  public async Task SettledHabit_Edit_DoesNotCreateAnotherObligation(
    HabitExecutionStatusData settledStatus, int freezeBalance)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    var execution = await _db.HabitExecutions.SingleAsync(x => x.Id == executionId);
    var original = await _db.HabitVersions.SingleAsync(x => x.Id == execution.HabitVersionId);
    var date = new DateOnly(2026, 1, 5); // Monday
    execution.Date = date;
    execution.Status = settledStatus;
    _db.UserProtections.Add(new UserProtectionData { UserId = userId, FreezeCurrent = freezeBalance });
    await _db.SaveChangesAsync();
    var repo = new HabitRepository(_db, NullLogger<HabitRepository>.Instance);
    var updated = (await repo.Update(original.HabitId, userId,
      original.ToRecord() with { Task = "Renamed", NotificationTime = new TimeOnly(13, 15) }, true)).Get()!;
    updated.Version.Should().Be(2);

    // The day was settled before checks existed (or by an older pod): its check only adopts it.
    var checks = Checks.Service(_db);
    var monday = new DateTime(2026, 1, 5, 2, 0, 0, DateTimeKind.Utc); // 10:00 Monday in Singapore
    (await checks.OpenUpcoming(monday)).IsSuccess().Should().BeTrue();
    (await checks.Report(userId, original.HabitId, Checks.Manual(monday), monday)).Get().Should().BeNull();
    (await checks.Skip(userId, original.HabitId, null, monday)).Get().Should().BeNull();
    (await checks.CloseDue(monday.AddDays(2))).Get().Should().Be(0);
    (await _db.HabitChecks.AsNoTracking().SingleAsync(x => x.HabitId == original.HabitId))
      .SettledBy.Should().Be("adopted");
    (await checks.DispatchEvents(monday.AddDays(2))).IsSuccess().Should().BeTrue();

    await using var verify = NewContext(_conn!);
    var executions = await verify.HabitExecutions.Where(x => x.HabitVersion.HabitId == original.HabitId)
      .AsNoTracking().ToListAsync();
    executions.Should().ContainSingle().Which.Id.Should().Be(executionId);
    executions[0].Status.Should().Be(settledStatus);
    executions[0].HabitVersionId.Should().Be(original.Id);
    (await verify.Penalties.AnyAsync(x => x.UserId == userId)).Should().BeFalse();
    (await verify.UserProtections.SingleAsync(x => x.UserId == userId)).FreezeCurrent.Should().Be(freezeBalance);

    // A different date is a new obligation, despite the older version's history.
    (await checks.OpenUpcoming(monday.AddDays(7))).Get().Should().Be(1);
  }

  [SkippableFact]
  public async Task MonthlySkipUsage_SurvivesHabitDeletion_AndRemainsOwnerAndMonthScoped()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    var execution = await _db.HabitExecutions.SingleAsync(x => x.Id == executionId);
    var version = await _db.HabitVersions.SingleAsync(x => x.Id == execution.HabitVersionId);
    execution.Status = HabitExecutionStatusData.Skipped;
    execution.Date = new DateOnly(2026, 1, 5);
    _db.HabitExecutions.Add(new HabitExecutionData
    {
      HabitVersionId = version.Id, Date = new DateOnly(2025, 12, 31), Status = HabitExecutionStatusData.Skipped
    });
    var (other, otherCharity) = await SeedUserAndCharity();
    var otherExecution = await SeedExecution(other, otherCharity);
    var stranger = await _db.HabitExecutions.SingleAsync(x => x.Id == otherExecution);
    stranger.Status = HabitExecutionStatusData.Skipped;
    stranger.Date = execution.Date;
    await _db.SaveChangesAsync();
    var repo = new HabitRepository(_db, NullLogger<HabitRepository>.Instance);

    (await repo.Delete(version.HabitId, userId)).IsSuccess().Should().BeTrue();
    (await repo.CountUserSkipsForMonth(userId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)))
      .Get().Should().Be(1);
  }

  [SkippableTheory]
  [InlineData(HabitExecutionStatusData.Completed, "succeeded")]
  [InlineData(HabitExecutionStatusData.Skipped, "skip")]
  [InlineData(HabitExecutionStatusData.Frozen, "frozen")]
  [InlineData(HabitExecutionStatusData.Vacation, "vacation")]
  public async Task OverviewToday_UsesUserTimezone_AndEachDaysOwnDate(
    HabitExecutionStatusData executionStatus, string expectedStatus)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    var execution = await _db.HabitExecutions.SingleAsync(x => x.Id == executionId);
    var version = await _db.HabitVersions.SingleAsync(x => x.Id == execution.HabitVersionId);
    var now = new DateTime(2026, 1, 4, 16, 30, 0, DateTimeKind.Utc); // Sunday 08:30 in LA, Monday in Singapore
    execution.Date = new DateOnly(2026, 1, 4);
    execution.Status = executionStatus;
    execution.CompletedAt = executionStatus == HabitExecutionStatusData.Completed ? now : null;
    await _db.SaveChangesAsync();
    var service = new StreakService(new StreakRepository(_db, NullLogger<StreakRepository>.Instance));

    // The habit version still says Asia/Singapore; the user's timezone (LA) decides today.
    var status = (await service.GetStatusForHabit(userId, version.HabitId,
      "America/Los_Angeles", ["Sunday"], new TimeOnly(23, 59), now)).Get();
    var response = App.Modules.Habit.API.V1.HabitOverviewMapper.ToRes(new HabitOverviewSummary(
      [new HabitOverviewItem(version.HabitId, "Read", "22:00", "America/Los_Angeles", ["Sunday"],
        3m, "USD", true, new Domain.Charity.CharityPrincipal
        {
          Id = charityId, Record = new Domain.Charity.CharityRecord { Name = "Charity" }
        }, status, status.TimeLeftToEodMinutes, new HabitVersionMeta(version.Id, 1, true), 0m,
        new TimeOnly(23, 59), null, "manual", null)],
      0m, 0, 3));

    response.Habits.Single().Status.Today.Should().Be("04-01-2026");
    response.Habits.Single().Status.TodayStatus.Should().Be(expectedStatus);
    status.IsCompleteToday.Should().Be(executionStatus == HabitExecutionStatusData.Completed);
    status.WeekStart.Should().Be(new DateOnly(2026, 1, 4));
    status.WeekStatuses[DayOfWeek.Sunday].Should().Be(MapDay(executionStatus));
    status.TimeLeftToEodMinutes.Should().Be(15 * 60 + 30); // until Monday 00:00 LA

    // Yesterday's completion that happened during the user's current calendar day
    // (CompletedAt is today in LA) still belongs to yesterday: its own date decides.
    execution.Date = execution.Date.AddDays(-1);
    execution.Status = HabitExecutionStatusData.Completed;
    execution.CompletedAt = now.AddHours(-1);
    await _db.SaveChangesAsync();
    var nextDay = (await service.GetStatusForHabit(userId, version.HabitId,
      "America/Los_Angeles", ["Sunday"], new TimeOnly(23, 59), now)).Get();
    nextDay.IsCompleteToday.Should().BeFalse();
    nextDay.TodayStatus.Should().Be(HabitDayStatus.NotApplicable);
    nextDay.WeekStatuses[DayOfWeek.Sunday].Should().Be(HabitDayStatus.NotApplicable);
  }

  private static HabitDayStatus MapDay(HabitExecutionStatusData s) => s switch
  {
    HabitExecutionStatusData.Completed => HabitDayStatus.Succeeded,
    HabitExecutionStatusData.Skipped => HabitDayStatus.Skip,
    HabitExecutionStatusData.Frozen => HabitDayStatus.Frozen,
    HabitExecutionStatusData.Vacation => HabitDayStatus.Vacation,
    _ => HabitDayStatus.Failed
  };

  [SkippableFact]
  public async Task MixedDay_FreezesOnlyRemainingMisses_WithoutPenaltiesOrExtraConsumption()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUserAndCharity();
    var date = new DateOnly(2026, 1, 5); // Monday
    var habits = new List<Guid>();
    var versions = new List<Guid>();
    var completedAt = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
    _db.UserProtections.Add(new UserProtectionData { UserId = userId, FreezeCurrent = 3 });
    for (var i = 0; i < 4; i++)
    {
      var habitId = Guid.NewGuid();
      var versionId = Guid.NewGuid();
      habits.Add(habitId);
      versions.Add(versionId);
      _db.Habits.Add(new HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
      _db.HabitVersions.Add(new HabitVersionData
      {
        Id = versionId, HabitId = habitId, CharityId = charityId, Version = 1,
        Task = $"task-{i}", DaysOfWeek = ["Monday"], Timezone = "UTC",
        StakeCents = 1000, StakeCurrency = "USD", RatioBasisPoints = 5000
      });
    }
    _db.HabitExecutions.AddRange(
      new HabitExecutionData
      {
        HabitVersionId = versions[0], Date = date, Status = HabitExecutionStatusData.Completed,
        CompletedAt = completedAt, Notes = "Finished"
      },
      new HabitExecutionData
      {
        HabitVersionId = versions[1], Date = date, Status = HabitExecutionStatusData.Skipped,
        Notes = "Resting"
      },
      // An execution on a different date must not prevent today's freeze.
      new HabitExecutionData
      {
        HabitVersionId = versions[2], Date = date.AddDays(-1), Status = HabitExecutionStatusData.Completed,
        CompletedAt = completedAt.AddDays(-1)
      });
    await _db.SaveChangesAsync();

    var checks = Checks.Service(_db);
    var monday = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);
    (await checks.OpenUpcoming(monday)).IsSuccess().Should().BeTrue();
    var afterGrace = new DateTime(2026, 1, 6, 1, 0, 0, DateTimeKind.Utc);
    (await checks.CloseDue(afterGrace)).Get().Should().Be(2);
    (await checks.CloseDue(afterGrace)).Get().Should().Be(0);
    (await checks.DispatchEvents(afterGrace)).IsSuccess().Should().BeTrue();

    await using var verify = NewContext(_conn!);
    var executions = await verify.HabitExecutions.AsNoTracking()
      .Where(x => versions.Contains(x.HabitVersionId) && x.Date == date).ToListAsync();
    executions.Should().HaveCount(4);
    var completed = executions.Single(x => x.HabitVersionId == versions[0]);
    completed.Status.Should().Be(HabitExecutionStatusData.Completed);
    completed.CompletedAt.Should().Be(completedAt);
    completed.Notes.Should().Be("Finished");
    var skipped = executions.Single(x => x.HabitVersionId == versions[1]);
    skipped.Status.Should().Be(HabitExecutionStatusData.Skipped);
    skipped.Notes.Should().Be("Resting");
    executions.Where(x => versions.Skip(2).Contains(x.HabitVersionId))
      .Should().OnlyContain(x => x.Status == HabitExecutionStatusData.Frozen);
    (await verify.UserProtections.AsNoTracking().SingleAsync(x => x.UserId == userId))
      .FreezeCurrent.Should().Be(2);
    (await verify.FreezeConsumptions.CountAsync(x => x.UserId == userId)).Should().Be(1);
    (await verify.Penalties.AnyAsync(x => x.UserId == userId)).Should().BeFalse();
  }

  [SkippableFact]
  public async Task OneFailedExecution_ChargedOnce_CreditsCharity()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    var repo = Repo(_db);

    // Enqueue a Pending penalty (mirrors L5 HabitService step 6 output).
    (await repo.EnqueuePending(PendingRecord(executionId, userId, charityId, 500))).Get().Should().BeTrue();

    var payment = StubPaymentService.Succeeds("pi_ok");
    var drained = await Service(repo, payment).ProcessPending(batchSize: 100, maxAttempts: 5);
    ((int)drained).Should().Be(1);

    // Read back through a fresh context to avoid tracking artefacts.
    await using var verify = NewContext(_conn!);
    var penalties = await verify.Penalties.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();
    penalties.Should().ContainSingle();
    penalties[0].Status.Should().Be((int)PenaltyStatus.Charged);
    penalties[0].PaymentIntentId.Should().Be("pi_ok");

    var balance = await verify.CharityBalances.AsNoTracking().SingleAsync(x => x.CharityId == charityId);
    balance.AccruedCents.Should().Be(500);
  }

  [SkippableFact]
  public async Task SetIntentId_PersistsIntentId_LeavesStatusAndAttemptsUntouched()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    var repo = Repo(_db);
    (await repo.EnqueuePending(PendingRecord(executionId, userId, charityId, 100))).Get().Should().BeTrue();

    var id = await _db.Penalties.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.Id).SingleAsync();

    (await repo.SetIntentId(id, "int_persisted")).Get();

    await using var verify = NewContext(_conn!);
    var p = await verify.Penalties.AsNoTracking().SingleAsync(x => x.UserId == userId);
    p.PaymentIntentId.Should().Be("int_persisted");          // id written
    p.Status.Should().Be((int)PenaltyStatus.Pending);        // status untouched
    p.Attempts.Should().Be(0);                               // attempts untouched
  }

  [SkippableFact]
  public async Task ConfirmFailThenRetry_ReusesPersistedIntent_NoRecreate_ThenCharges()
  {
    Skip.If(_conn == null, SkipReason);

    // Full round-trip regression for the duplicate_request fix:
    //   pass 1: create-ok + confirm-fail -> intent id persisted, row bumped & still Pending
    //   pass 2: drain re-reads the persisted id and passes it as existingIntentId
    //           (reconcile, NOT a re-create) -> charge succeeds, charity credited.
    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    (await Repo(_db).EnqueuePending(PendingRecord(executionId, userId, charityId, 100))).Get().Should().BeTrue();

    var payment = StubPaymentService.CreatesThenFailsThenSucceeds("int_rt");

    // Pass 1: confirm fails -> not charged, but intent id must be persisted. Each pass uses
    // its own DbContext, mirroring the per-drain scoped context in the hosted service.
    await using (var c1 = NewContext(_conn!))
      ((int)await Service(Repo(c1), payment).ProcessPending(batchSize: 100, maxAttempts: 5)).Should().Be(0);
    await using (var v1 = NewContext(_conn!))
    {
      var p1 = await v1.Penalties.AsNoTracking().SingleAsync(x => x.UserId == userId);
      p1.PaymentIntentId.Should().Be("int_rt");                 // survived the confirm failure
      p1.Status.Should().Be((int)PenaltyStatus.Pending);
      p1.Attempts.Should().Be(1);
    }

    // Pass 2: reconcile the SAME intent and settle.
    await using (var c2 = NewContext(_conn!))
      ((int)await Service(Repo(c2), payment).ProcessPending(batchSize: 100, maxAttempts: 5)).Should().Be(1);

    // The retry passed the persisted id as existingIntentId (no re-create with a fresh id).
    payment.ExistingIntentIdArgs.Should().Equal([null, "int_rt"]);

    await using var v2 = NewContext(_conn!);
    var p2 = await v2.Penalties.AsNoTracking().SingleAsync(x => x.UserId == userId);
    p2.Status.Should().Be((int)PenaltyStatus.Charged);
    p2.PaymentIntentId.Should().Be("int_rt");
    var bal = await v2.CharityBalances.AsNoTracking().SingleAsync(x => x.CharityId == charityId);
    bal.AccruedCents.Should().Be(100);
  }

  [SkippableFact]
  public async Task MarkCharged_ClearsStaleLastError()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    (await Repo(_db).EnqueuePending(PendingRecord(executionId, userId, charityId, 100))).Get().Should().BeTrue();
    var id = await _db.Penalties.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.Id).SingleAsync();

    // A prior failed attempt left a stale error; a successful charge must clear it.
    await using (var c1 = NewContext(_conn!))
      (await Repo(c1).Bump(id, "stale 401 Unauthorized")).Get();
    await using (var c2 = NewContext(_conn!))
      (await Repo(c2).MarkCharged(id, "pi_ok")).Get();

    await using var verify = NewContext(_conn!);
    var p = await verify.Penalties.AsNoTracking().SingleAsync(x => x.UserId == userId);
    p.Status.Should().Be((int)PenaltyStatus.Charged);
    p.LastError.Should().BeNull(); // cleared on success
  }

  [SkippableFact]
  public async Task RunningTwice_DoesNotDoubleCharge()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    var repo = Repo(_db);
    var payment = StubPaymentService.Succeeds("pi_ok");

    // Enqueue twice (idempotent via unique HabitExecutionId): 2nd is a no-op.
    (await repo.EnqueuePending(PendingRecord(executionId, userId, charityId, 500))).Get().Should().BeTrue();
    (await repo.EnqueuePending(PendingRecord(executionId, userId, charityId, 500))).Get().Should().BeFalse();

    // Drain twice: 2nd run sees nothing Pending (row already Charged).
    ((int)await Service(repo, payment).ProcessPending(100, 5)).Should().Be(1);
    ((int)await Service(repo, payment).ProcessPending(100, 5)).Should().Be(0);

    await using var verify = NewContext(_conn!);
    var penalties = await verify.Penalties.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();
    penalties.Should().ContainSingle();
    penalties[0].Status.Should().Be((int)PenaltyStatus.Charged);

    var balance = await verify.CharityBalances.AsNoTracking().SingleAsync(x => x.CharityId == charityId);
    balance.AccruedCents.Should().Be(500); // credited exactly once
  }

  [SkippableFact]
  public async Task MarkCharged_CalledTwice_CreditsCharityOnce()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    var repo = Repo(_db);

    (await repo.EnqueuePending(PendingRecord(executionId, userId, charityId, 500))).Get().Should().BeTrue();

    Guid id;
    await using (var read = NewContext(_conn!))
      id = (await read.Penalties.AsNoTracking().SingleAsync(x => x.UserId == userId)).Id;

    // Stale-claim race: the lease can hand an in-flight Processing row to a second
    // worker, so MarkCharged may run twice for ONE real (idempotent) charge. The
    // charity credit must still apply exactly once, not double.
    (await repo.MarkCharged(id, "pi_ok")).Get();
    (await repo.MarkCharged(id, "pi_ok")).Get();

    await using var verify = NewContext(_conn!);
    var penalties = await verify.Penalties.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();
    penalties.Should().ContainSingle();
    penalties[0].Status.Should().Be((int)PenaltyStatus.Charged);

    var balance = await verify.CharityBalances.AsNoTracking().SingleAsync(x => x.CharityId == charityId);
    balance.AccruedCents.Should().Be(500); // credited exactly once despite double MarkCharged
  }

  [SkippableFact]
  public async Task MarkCharged_BlocksOnHeldRowLock_ThenCreditsOnce()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    (await Repo(_db).EnqueuePending(PendingRecord(executionId, userId, charityId, 500))).Get().Should().BeTrue();

    Guid id;
    await using (var read = NewContext(_conn!))
      id = (await read.Penalties.AsNoTracking().SingleAsync(x => x.UserId == userId)).Id;

    // Worker A holds the penalty row lock — the SAME lock MarkCharged acquires first
    // (the stale-claim lease scenario). Real MarkCharged on a second connection
    // (worker B) must BLOCK behind it. This distinguishes the fix from the bug: the
    // pre-fix code used a plain non-locking SELECT which, under Postgres MVCC, would
    // NOT wait on A's lock and would complete immediately — failing this assertion.
    await using var ctxA = NewContext(_conn!);
    await using var txA = await ctxA.Database.BeginTransactionAsync();
    await ctxA.Penalties
      .FromSqlRaw(@"SELECT * FROM ""Penalties"" WHERE ""Id"" = {0} FOR UPDATE", id)
      .ToListAsync();

    await using var ctxB = NewContext(_conn!);
    var bTask = Repo(ctxB).MarkCharged(id, "pi_ok");
    // MarkCharged normally finishes in ms; still pending after 1.5s means it is blocked.
    var winner = await Task.WhenAny(bTask, Task.Delay(1500));
    winner.Should().NotBeSameAs(bTask, "MarkCharged must block on the penalty row lock held by tx A");

    // Release A; B unblocks, takes the row, and credits exactly once.
    await txA.RollbackAsync();
    (await bTask).IsSuccess().Should().BeTrue();

    await using var verify = NewContext(_conn!);
    var balance = await verify.CharityBalances.AsNoTracking().SingleAsync(x => x.CharityId == charityId);
    balance.AccruedCents.Should().Be(500);
  }

  [SkippableFact]
  public async Task MarkCharged_ConcurrentSameCharityCurrency_NeverLosesUpdate()
  {
    Skip.If(_conn == null, SkipReason);

    const int reps = 50;
    var (userId, charityId) = await SeedUserAndCharity();
    // Pre-create the (charity, USD) row so every credit takes the UPDATE (lock) path —
    // isolates the lost-update race from the first-insert race.
    _db.CharityBalances.Add(new CharityBalanceData
    {
      Id = Guid.NewGuid(), CharityId = charityId, Currency = "USD", AccruedCents = 0
    });
    await _db.SaveChangesAsync();

    // Fuzz the lost-update race: many rounds of two DIFFERENT penalties for the SAME
    // (charity, currency) charged concurrently. Without the balance-row FOR UPDATE a
    // round reads a stale AccruedCents and lost-updates one credit, so the running
    // total ends below reps*(3+2). Task.WhenAll may not overlap on a single run;
    // repeating makes a missing lock reliably flake red.
    for (var i = 0; i < reps; i++)
    {
      var e1 = await SeedExecution(userId, charityId);
      var e2 = await SeedExecution(userId, charityId);
      (await Repo(_db).EnqueuePending(PendingRecord(e1, userId, charityId, 3))).Get().Should().BeTrue();
      (await Repo(_db).EnqueuePending(PendingRecord(e2, userId, charityId, 2))).Get().Should().BeTrue();

      Guid id1, id2;
      await using (var read = NewContext(_conn!))
      {
        id1 = (await read.Penalties.AsNoTracking().SingleAsync(x => x.HabitExecutionId == e1)).Id;
        id2 = (await read.Penalties.AsNoTracking().SingleAsync(x => x.HabitExecutionId == e2)).Id;
      }

      await using var c1 = NewContext(_conn!);
      await using var c2 = NewContext(_conn!);
      (await Task.WhenAll(Repo(c1).MarkCharged(id1, "pi_1"), Repo(c2).MarkCharged(id2, "pi_2")))
        .Should().OnlyContain(r => r.IsSuccess());
    }

    await using var verify = NewContext(_conn!);
    var balance = await verify.CharityBalances.AsNoTracking().SingleAsync(x => x.CharityId == charityId);
    balance.AccruedCents.Should().Be(reps * 5L); // every credit landed; no lost update
  }

  [SkippableFact]
  public async Task DifferentCurrenciesSameCharity_AccrueIntoSeparateRows()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    var eSgd = await SeedExecution(userId, charityId);
    var eUsd = await SeedExecution(userId, charityId);

    // Same charity, two currencies — the multi-currency scenario. With the
    // (CharityId, Currency) key these accrue into two separate rows instead of the
    // second one mismatching and stranding forever.
    (await Repo(_db).EnqueuePending(PendingRecord(eSgd, userId, charityId, 500, "SGD"))).Get().Should().BeTrue();
    (await Repo(_db).EnqueuePending(PendingRecord(eUsd, userId, charityId, 300, "USD"))).Get().Should().BeTrue();

    var payment = StubPaymentService.Succeeds("pi_ok");
    ((int)await Service(Repo(_db), payment).ProcessPending(batchSize: 100, maxAttempts: 5)).Should().Be(2);

    await using var verify = NewContext(_conn!);
    var balances = await verify.CharityBalances.AsNoTracking()
      .Where(x => x.CharityId == charityId).ToListAsync();
    balances.Should().HaveCount(2); // one row per (charity, currency)
    balances.Single(b => b.Currency == "SGD").AccruedCents.Should().Be(500);
    balances.Single(b => b.Currency == "USD").AccruedCents.Should().Be(300);

    // Both penalties reached the terminal Charged state — neither stranded.
    var penalties = await verify.Penalties.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();
    penalties.Should().HaveCount(2).And.OnlyContain(p => p.Status == (int)PenaltyStatus.Charged);
  }

  [SkippableFact]
  public async Task SameCurrencySameCharity_AccrueIntoOneRow()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    var e1 = await SeedExecution(userId, charityId);
    var e2 = await SeedExecution(userId, charityId);

    // Two penalties, same charity, same currency (the common path — stakes are USD):
    // the second must UPDATE the existing (charity, currency) row, not insert a new one.
    (await Repo(_db).EnqueuePending(PendingRecord(e1, userId, charityId, 300))).Get().Should().BeTrue();
    (await Repo(_db).EnqueuePending(PendingRecord(e2, userId, charityId, 200))).Get().Should().BeTrue();

    var payment = StubPaymentService.Succeeds("pi_ok");
    ((int)await Service(Repo(_db), payment).ProcessPending(batchSize: 100, maxAttempts: 5)).Should().Be(2);

    await using var verify = NewContext(_conn!);
    var balances = await verify.CharityBalances.AsNoTracking()
      .Where(x => x.CharityId == charityId).ToListAsync();
    balances.Should().ContainSingle(); // one row for the single (charity, USD) pair
    balances[0].AccruedCents.Should().Be(500); // 300 + 200 summed in place
  }

  [SkippableFact]
  public async Task NoConsent_MarksSkipped_NoCredit()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    var executionId = await SeedExecution(userId, charityId);
    var repo = Repo(_db);

    (await repo.EnqueuePending(PendingRecord(executionId, userId, charityId, 500))).Get().Should().BeTrue();

    // No verified consent -> ChargeStoredConsentAsync surfaces NotFoundException -> Skipped.
    var payment = StubPaymentService.Fails(
      new NotFoundException("No verified payment consent", typeof(PaymentCustomer), userId));
    ((int)await Service(repo, payment).ProcessPending(100, 5)).Should().Be(1);

    await using var verify = NewContext(_conn!);
    var penalties = await verify.Penalties.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();
    penalties.Should().ContainSingle();
    penalties[0].Status.Should().Be((int)PenaltyStatus.Skipped);
    penalties[0].PaymentIntentId.Should().BeNull();

    var hasBalance = await verify.CharityBalances.AsNoTracking().AnyAsync(x => x.CharityId == charityId);
    hasBalance.Should().BeFalse(); // no row created -> nothing accrued
  }
}
