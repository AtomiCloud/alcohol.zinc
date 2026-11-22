using App.Modules.Charities.Data;
using App.Modules.Configurations.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Payment.Data;
using App.Modules.Penalty.Data;
using App.Modules.Proof.Data;
using App.Modules.Protection.Data;
using App.Modules.Routine.Data;
using App.Modules.Strava.Data;
using App.Modules.System;
using App.Modules.Users.Data;
using App.Modules.Vacation.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using CSharp_Result;
using Domain.Payment;
using Domain.Penalty;
using Domain.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IntTest.User;

/// <summary>
/// Real-database integration tests for the account-deletion purge (UserRepository.DeleteAllRemnants).
/// Gated by the ZINC_DELETE_TEST_DB env var (the Postgres host) so it is skipped where no DB is
/// provisioned; set it to run against a throwaway Postgres. Each test gets a fresh schema.
///
/// Run locally:
///   docker run -d --name zinc-deltest -e POSTGRES_USER=test -e POSTGRES_PASSWORD=test \
///     -e POSTGRES_DB=zinctest -p 55432:5432 postgres:16
///   ZINC_DELETE_TEST_DB=localhost dotnet test IntTest/IntTest.csproj --filter DeleteAllRemnants
/// </summary>
public class DeleteAllRemnantsIntegrationTests : IAsyncLifetime
{
  private MainDbContext _db = null!;
  private StaticOptionsMonitor<Dictionary<string, DatabaseOption>> _monitor = null!;
  private bool _enabled;

  public async Task InitializeAsync()
  {
    var host = Environment.GetEnvironmentVariable("ZINC_DELETE_TEST_DB");
    _enabled = !string.IsNullOrWhiteSpace(host);
    if (!_enabled) return;

    var opt = new DatabaseOption
    {
      Host = host!,
      Port = int.Parse(Environment.GetEnvironmentVariable("ZINC_DELETE_TEST_PORT") ?? "55432"),
      User = Environment.GetEnvironmentVariable("ZINC_DELETE_TEST_USER") ?? "test",
      Password = Environment.GetEnvironmentVariable("ZINC_DELETE_TEST_PASS") ?? "test",
      Database = Environment.GetEnvironmentVariable("ZINC_DELETE_TEST_DBNAME") ?? "zinctest",
      AutoMigrate = false,
      Timeout = 30,
    };
    _monitor = new StaticOptionsMonitor<Dictionary<string, DatabaseOption>>(
      new Dictionary<string, DatabaseOption> { [MainDbContext.Key] = opt });
    _db = new MainDbContext(_monitor, NullLoggerFactory.Instance);
    await _db.Database.EnsureDeletedAsync();
    await _db.Database.EnsureCreatedAsync();
  }

  public async Task DisposeAsync()
  {
    if (!_enabled) return;
    await _db.Database.EnsureDeletedAsync();
    await _db.DisposeAsync();
  }

  [SkippableFact]
  public async Task DeleteAllRemnants_PurgesEveryUserTable_NoOrphans_AndLeavesOtherUserIntact()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    // Shared, global charity catalog row — must survive deletion (it is not personal data).
    var charity = new CharityData { Name = "Test Charity" };
    _db.Charities.Add(charity);
    await _db.SaveChangesAsync();

    await SeedFullUser("user-A", charity.Id);
    await SeedFullUser("user-B", charity.Id);

    var repo = new UserRepository(_db, NullLogger<UserRepository>.Instance);
    var result = await repo.DeleteAllRemnants("user-A");

    result.IsSuccess().Should().BeTrue();
    result.Get().Should().NotBeNull("user-A existed, so deletion is a real success (not the null no-op)");

    // Every table now holds exactly ONE row — user-B's. If any of user-A's rows leaked (especially the
    // habit-chain tables keyed by HabitId, not UserId), these counts would be 2. This is the no-orphan proof.
    (await _db.Users.CountAsync()).Should().Be(1);
    (await _db.Users.CountAsync(x => x.Id == "user-B")).Should().Be(1);
    (await _db.Configurations.CountAsync()).Should().Be(1);
    (await _db.Configurations.CountAsync(x => x.UserId == "user-B")).Should().Be(1);
    (await _db.Habits.CountAsync()).Should().Be(1);
    (await _db.Habits.CountAsync(x => x.UserId == "user-B")).Should().Be(1);
    (await _db.Routines.CountAsync()).Should().Be(1);
    (await _db.Routines.CountAsync(x => x.UserId == "user-B")).Should().Be(1);
    (await _db.HabitVersions.CountAsync()).Should().Be(1);     // orphan version check
    (await _db.HabitExecutions.CountAsync()).Should().Be(1);   // orphan execution check
    (await _db.FreezeAwards.CountAsync()).Should().Be(1);      // orphan award check (keyed by HabitId)
    (await _db.VacationPeriods.CountAsync()).Should().Be(1);
    (await _db.UserProtections.CountAsync()).Should().Be(1);
    (await _db.FreezeConsumptions.CountAsync()).Should().Be(1);
    (await _db.PaymentCustomers.CountAsync()).Should().Be(1);
    (await _db.ProofBindings.CountAsync()).Should().Be(1);
    (await _db.StravaConnections.CountAsync()).Should().Be(1, "Strava's terms: the link and tokens go with the account");

    // None of user-A's rows remain anywhere.
    (await _db.Users.CountAsync(x => x.Id == "user-A")).Should().Be(0);
    (await _db.Configurations.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.Habits.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.Routines.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.VacationPeriods.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.UserProtections.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.FreezeConsumptions.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.PaymentCustomers.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.StravaConnections.CountAsync(x => x.UserId == "user-A")).Should().Be(0);

    // The shared charity catalog is untouched.
    (await _db.Charities.CountAsync()).Should().Be(1);
  }

  [SkippableFact]
  public async Task DeleteAllRemnants_IsIdempotent_SecondCallNoOps()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var charity = new CharityData { Name = "Test Charity" };
    _db.Charities.Add(charity);
    await _db.SaveChangesAsync();
    await SeedFullUser("user-A", charity.Id);

    var repo = new UserRepository(_db, NullLogger<UserRepository>.Instance);

    (await repo.DeleteAllRemnants("user-A")).Get().Should().NotBeNull("first delete removes the real user");
    (await repo.DeleteAllRemnants("user-A")).Get().Should().BeNull("a second delete of an absent user is an idempotent no-op");
    (await repo.DeleteAllRemnants("never-existed")).Get().Should().BeNull("deleting an unknown user is also a no-op");
  }

  [SkippableFact]
  public async Task DeleteAccount_RealTransactionManager_DbWritingCallback_DoesNotPromoteOrThrow()
  {
    // Regression guard for the bug where onBeforePurge (the Airwallex consent revoke, which does its
    // own DB write) ran INSIDE the purge's TransactionScope: a second DB op + the purge's
    // SaveChangesAsync in one scope can force an unsupported Npgsql distributed-transaction promotion.
    // This uses the REAL TransactionManager and a DB-writing callback; it throws against the old code
    // and passes only when onBeforePurge runs OUTSIDE the transaction.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    _db.Users.Add(new UserData { Id = "user-tx", Username = "u_tx", Email = "tx@test.local", Active = true });
    await _db.SaveChangesAsync();

    var service = new UserService(
      new UserRepository(_db, NullLogger<UserRepository>.Instance),
      new StreakRepository(_db, NullLogger<StreakRepository>.Instance),
      new TransactionManager(NullLogger<TransactionManager>.Instance));

    var result = await service.DeleteAccount("user-tx", blockOnDebt: false, onBeforePurge: async () =>
    {
      // Mirrors what PaymentService.DisablePaymentConsentAsync does: a real DB write.
      await _db.Users.Where(u => u.Id == "user-tx").ExecuteUpdateAsync(s => s.SetProperty(u => u.Active, false));
      return new Unit();
    });

    result.IsSuccess().Should().BeTrue("the real-transaction delete path must not throw");
    (await _db.Users.CountAsync(u => u.Id == "user-tx")).Should().Be(0);
  }

  [SkippableFact]
  public async Task DeleteAllRemnants_KeepsPenaltyRecords_DetachedFromUserAndExecution()
  {
    // Penalties are the money ledger (what was charged, for which charity, which payment intent).
    // Deleting the person must DETACH them from it, not erase it. There are two cascade paths into
    // Penalties — from the user (UserId) and from the habit execution (HabitExecutionId), and the purge
    // deletes executions explicitly — so both must be cut.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var charity = new CharityData { Name = "Test Charity" };
    _db.Charities.Add(charity);
    await _db.SaveChangesAsync();
    await SeedFullUser("user-A", charity.Id);
    await SeedFullUser("user-B", charity.Id);
    var (chargedA, pendingA) = await SeedPenalties("user-A", charity.Id);
    var (chargedB, pendingB) = await SeedPenalties("user-B", charity.Id);
    _db.ChangeTracker.Clear();

    var repo = new UserRepository(_db, NullLogger<UserRepository>.Instance);
    (await repo.DeleteAllRemnants("user-A")).Get().Should().NotBeNull();
    _db.ChangeTracker.Clear();

    (await _db.Penalties.CountAsync()).Should().Be(4, "no penalty row may be deleted with the user");

    var charged = await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == chargedA);
    charged.UserId.Should().BeNull("the person is detached from the ledger");
    charged.HabitExecutionId.Should().BeNull("the execution is gone; the penalty must not cascade with it");
    charged.AmountCents.Should().Be(500);
    charged.Currency.Should().Be("USD");
    charged.CharityId.Should().Be(charity.Id);
    charged.PaymentIntentId.Should().Be("int_charged_user-A");
    charged.Status.Should().Be((int)PenaltyStatus.Charged, "a settled charge stays a settled charge");

    var pending = await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == pendingA);
    pending.UserId.Should().BeNull();
    pending.HabitExecutionId.Should().BeNull();
    pending.Status.Should().Be((int)PenaltyStatus.Pending,
      "the purge only detaches; it never writes a status a concurrent drain could be settling");
    pending.AmountCents.Should().Be(300);

    // The other user's penalties are untouched.
    var otherCharged = await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == chargedB);
    otherCharged.UserId.Should().Be("user-B");
    otherCharged.HabitExecutionId.Should().NotBeNull();
    var otherPending = await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == pendingB);
    otherPending.UserId.Should().Be("user-B");
    otherPending.Status.Should().Be((int)PenaltyStatus.Pending);

    // The next penalty drain closes the detached unpaid penalty as Skipped WITHOUT calling the payment
    // gateway, and still charges the other user's penalty as normal.
    var payment = IntTest.Penalty.StubPaymentService.Succeeds("int_drain");
    var drained = await NewPenaltyService(_db, payment).ProcessPending(batchSize: 10, maxAttempts: 5);
    drained.IsSuccess().Should().BeTrue();
    _db.ChangeTracker.Clear();
    payment.ExistingIntentIdArgs.Should().ContainSingle("only user-B's penalty reaches the gateway");
    (await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == pendingA)).Status
      .Should().Be((int)PenaltyStatus.Skipped);
    (await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == pendingB)).Status
      .Should().Be((int)PenaltyStatus.Charged);
  }

  [SkippableFact]
  public async Task DetachedPenaltyWithPaymentIntent_IsFlaggedForReconciliation_NeverSkippedOrCharged()
  {
    // A penalty whose earlier attempt already created a payment intent may have moved money. Once the
    // user is deleted it must not be charged again, and must not be quietly marked Skipped either.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var charity = new CharityData { Name = "Test Charity" };
    _db.Charities.Add(charity);
    await _db.SaveChangesAsync();
    await SeedFullUser("user-A", charity.Id);
    var (_, pendingA) = await SeedPenalties("user-A", charity.Id);
    await _db.Penalties.Where(p => p.Id == pendingA)
      .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaymentIntentId, "int_earlier_attempt").SetProperty(p => p.Attempts, 1));
    _db.ChangeTracker.Clear();

    (await new UserRepository(_db, NullLogger<UserRepository>.Instance).DeleteAllRemnants("user-A"))
      .Get().Should().NotBeNull();
    _db.ChangeTracker.Clear();

    var payment = IntTest.Penalty.StubPaymentService.Succeeds("int_must_not_happen");
    (await NewPenaltyService(_db, payment).ProcessPending(batchSize: 10, maxAttempts: 5)).IsSuccess().Should().BeTrue();
    _db.ChangeTracker.Clear();

    payment.ExistingIntentIdArgs.Should().BeEmpty("a detached penalty never reaches the gateway");
    var row = await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == pendingA);
    row.Status.Should().Be((int)PenaltyStatus.Failed);
    row.PaymentIntentId.Should().Be("int_earlier_attempt");
    row.LastError.Should().Contain("reconcile");
  }

  [SkippableFact]
  public async Task UserDeletedWhileChargeInFlight_NoNewIntentIsSaved_AndSettledChargeIsKept()
  {
    // A drain worker claims a penalty, then the user is deleted (in another request) mid-charge.
    //  - Saving a freshly created payment intent must now fail, so the worker never confirms it.
    //  - A charge that the worker had already confirmed still settles: Charged, charity credited once,
    //    and the worker's write does not re-attach the deleted user.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var charity = new CharityData { Name = "Test Charity" };
    _db.Charities.Add(charity);
    await _db.SaveChangesAsync();
    await SeedFullUser("user-A", charity.Id);
    var (_, pendingA) = await SeedPenalties("user-A", charity.Id);
    _db.ChangeTracker.Clear();

    // Worker context claims the pending penalty (Pending -> Processing) and keeps it tracked.
    var worker = new PenaltyRepository(_db, NullLogger<PenaltyRepository>.Instance);
    (await worker.GetPending(10)).Get().Select(p => p.Id).Should().Contain(pendingA);

    // The account is deleted through a separate context (a separate request).
    await using (var other = new MainDbContext(_monitor, NullLoggerFactory.Instance))
      (await new UserRepository(other, NullLogger<UserRepository>.Instance).DeleteAllRemnants("user-A"))
        .Get().Should().NotBeNull();

    var save = () => worker.SetIntentId(pendingA, "int_created_after_delete");
    (await save.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*detached*");

    (await worker.MarkCharged(pendingA, "int_confirmed_before_delete")).IsSuccess().Should().BeTrue();
    _db.ChangeTracker.Clear();

    var row = await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == pendingA);
    row.Status.Should().Be((int)PenaltyStatus.Charged);
    row.PaymentIntentId.Should().Be("int_confirmed_before_delete");
    row.UserId.Should().BeNull("the worker's write must not re-attach the deleted user");
    row.HabitExecutionId.Should().BeNull();
    (await _db.CharityBalances.AsNoTracking().SingleAsync(b => b.CharityId == charity.Id)).AccruedCents
      .Should().Be(300);
  }

  private static PenaltyService NewPenaltyService(MainDbContext db, IPaymentService payment) =>
    new(new PenaltyRepository(db, NullLogger<PenaltyRepository>.Instance), payment,
      new IntTest.Notification.NoopEmailNotifier(), NullLogger<PenaltyService>.Instance);

  [SkippableFact]
  public async Task DeletingUserOrExecutionRows_InTheDatabase_NeverDeletesPenalties()
  {
    // Database backstop: any path that removes a user or an execution row (not just DeleteAllRemnants)
    // must leave the penalty rows in place, detached.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var charity = new CharityData { Name = "Test Charity" };
    _db.Charities.Add(charity);
    await _db.SaveChangesAsync();
    await SeedFullUser("user-A", charity.Id);
    await SeedFullUser("user-B", charity.Id);
    var (chargedA, _) = await SeedPenalties("user-A", charity.Id);
    var (chargedB, _) = await SeedPenalties("user-B", charity.Id);
    _db.ChangeTracker.Clear();

    // Execution path: delete user-B's executions only.
    await _db.Database.ExecuteSqlRawAsync(@"
      DELETE FROM ""HabitExecutions"" WHERE ""HabitVersionId"" IN (
        SELECT v.""Id"" FROM ""HabitVersions"" v JOIN ""Habits"" h ON h.""Id"" = v.""HabitId""
        WHERE h.""UserId"" = 'user-B')");
    // User path: delete user-A's user row only (habits have no FK to Users, so the execution stays).
    await _db.Database.ExecuteSqlRawAsync(@"DELETE FROM ""Users"" WHERE ""Id"" = 'user-A'");

    (await _db.Penalties.CountAsync()).Should().Be(4);
    var a = await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == chargedA);
    a.UserId.Should().BeNull();
    a.HabitExecutionId.Should().NotBeNull("only the user row was deleted; the execution still exists");
    a.AmountCents.Should().Be(500);
    var b = await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == chargedB);
    b.UserId.Should().Be("user-B");
    b.HabitExecutionId.Should().BeNull();
  }

  // Seeds one Charged and one Pending penalty for the user, each on its own failed execution of the
  // user's habit version (HabitExecutionId is unique per penalty).
  private async Task<(Guid Charged, Guid Pending)> SeedPenalties(string userId, Guid charityId)
  {
    var versionId = await _db.HabitVersions
      .Where(v => _db.Habits.Any(h => h.Id == v.HabitId && h.UserId == userId))
      .Select(v => v.Id)
      .SingleAsync();
    var exec1 = new HabitExecutionData
    {
      Id = Guid.NewGuid(), HabitVersionId = versionId, Date = new DateOnly(2026, 2, 1),
      Status = HabitExecutionStatusData.Failed, PaymentProcessed = true,
    };
    var exec2 = new HabitExecutionData
    {
      Id = Guid.NewGuid(), HabitVersionId = versionId, Date = new DateOnly(2026, 2, 2),
      Status = HabitExecutionStatusData.Failed, PaymentProcessed = true,
    };
    _db.HabitExecutions.AddRange(exec1, exec2);
    var charged = new PenaltyData
    {
      Id = Guid.NewGuid(), HabitExecutionId = exec1.Id, UserId = userId, CharityId = charityId,
      AmountCents = 500, Currency = "USD", Status = (int)PenaltyStatus.Charged,
      PaymentIntentId = $"int_charged_{userId}", Attempts = 1,
    };
    var pending = new PenaltyData
    {
      Id = Guid.NewGuid(), HabitExecutionId = exec2.Id, UserId = userId, CharityId = charityId,
      AmountCents = 300, Currency = "USD", Status = (int)PenaltyStatus.Pending, Attempts = 0,
    };
    _db.Penalties.AddRange(charged, pending);
    await _db.SaveChangesAsync();
    return (charged.Id, pending.Id);
  }

  private async Task SeedFullUser(string id, Guid charityId)
  {
    var habitId = Guid.NewGuid();
    var versionId = Guid.NewGuid();

    _db.Users.Add(new UserData { Id = id, Username = $"u_{id}", Email = $"{id}@test.local", Active = true });
    _db.Configurations.Add(new ConfigurationData
    {
      Id = Guid.NewGuid(),
      UserId = id,
      Timezone = "Asia/Singapore",
      EndOfDay = new TimeOnly(0, 0),
      DefaultCharityId = charityId,
    });
    var routineId = Guid.NewGuid();
    _db.Routines.Add(new RoutineData { Id = routineId, UserId = id, Name = "run" });
    _db.Habits.Add(new HabitData { Id = habitId, UserId = id, Version = 1, Enabled = true, RoutineId = routineId });
    _db.HabitVersions.Add(new HabitVersionData
    {
      Id = versionId,
      HabitId = habitId,
      CharityId = charityId,
      Version = 1,
      Task = "run",
      DaysOfWeek = ["Mon"],
      StakeCents = 100,
      RatioBasisPoints = 10000,
      Timezone = "Asia/Singapore",
    });
    _db.HabitExecutions.Add(new HabitExecutionData
    {
      Id = Guid.NewGuid(),
      HabitVersionId = versionId,
      Date = new DateOnly(2026, 1, 1),
      Status = HabitExecutionStatusData.Failed,
      PaymentProcessed = false,
    });
    _db.FreezeAwards.Add(new FreezeAwardData { Id = Guid.NewGuid(), HabitId = habitId, WeekStart = new DateOnly(2026, 1, 1) });
    _db.VacationPeriods.Add(new VacationPeriodData
    {
      Id = Guid.NewGuid(),
      UserId = id,
      StartDate = new DateOnly(2026, 1, 1),
      EndDate = new DateOnly(2026, 1, 5),
      Timezone = "Asia/Singapore",
    });
    _db.UserProtections.Add(new UserProtectionData { Id = Guid.NewGuid(), UserId = id, FreezeCurrent = 3 });
    _db.FreezeConsumptions.Add(new FreezeConsumptionData { Id = Guid.NewGuid(), UserId = id, Date = new DateOnly(2026, 1, 2) });
    _db.PaymentCustomers.Add(new PaymentCustomerData
    {
      Id = Guid.NewGuid(),
      UserId = id,
      AirwallexCustomerId = $"awx_{id}",
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow,
    });
    _db.ProofBindings.Add(new ProofBindingData
    {
      HabitId = habitId, PluginKey = "strava", PluginVersion = 1, Settings = """{"sportTypes":["Run"]}"""
    });
    _db.StravaConnections.Add(new StravaConnectionData
    {
      UserId = id, AthleteId = Math.Abs(id.GetHashCode()), AccessToken = "enc", RefreshToken = "enc",
      ExpiresAtUtc = DateTime.UtcNow, Scope = "read,activity:read_all"
    });
    await _db.SaveChangesAsync();
  }
}

/// <summary>Minimal IOptionsMonitor that always returns a fixed value (for test DbContext wiring).</summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
  public T CurrentValue { get; } = value;
  public T Get(string? name) => this.CurrentValue;
  public IDisposable? OnChange(Action<T, string?> listener) => null;
}
