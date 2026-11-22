using App.Modules.Charities.Data;
using App.Modules.Check.Data;
using App.Modules.Configurations.Data;
using App.Modules.Disbursement.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Penalty.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using CSharp_Result;
using Domain.Charity;
using Domain.Configuration;
using Domain.Entitlement;
using Domain.Exceptions;
using Domain.Habit;
using Domain.Penalty;
using IntTest.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;
using Npgsql;

namespace IntTest.Charity;

/// <summary>
/// Real-database tests for deleting a charity. A charity is referenced by the money ledger (penalties,
/// accrued balances, payouts) and by users' habit history and settings, so "delete" must switch the
/// charity off and never erase those rows; the database itself must refuse a hard delete.
///
/// Gated by ZINC_DELETE_TEST_DB (the Postgres host), like DeleteAllRemnantsIntegrationTests, but on its
/// own database so the two classes never race. Run locally:
///   docker run -d --name zinc-deltest -e POSTGRES_USER=test -e POSTGRES_PASSWORD=test \
///     -e POSTGRES_DB=zinctest -p 55432:5432 postgres:16
///   ZINC_DELETE_TEST_DB=localhost dotnet test IntTest/IntTest.csproj --filter CharityDelete
/// </summary>
public class CharityDeleteIntegrationTests : IAsyncLifetime
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
      Database = "zinc_charity_delete_test",
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

  private CharityRepository Charities() => new(_db, NullLogger<CharityRepository>.Instance);

  [SkippableFact]
  public async Task Delete_SwitchesCharityOff_AndKeepsMoneyHistoryAndSettings()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var (target, other) = await SeedTwoCharitiesWithHistory();

    (await Charities().Delete(target)).Get().Should().NotBeNull("the charity existed");
    _db.ChangeTracker.Clear();

    // Nothing that points at the charity is erased.
    (await _db.Charities.CountAsync(c => c.Id == target)).Should().Be(1, "the charity row is kept, switched off");
    (await _db.Charities.AsNoTracking().SingleAsync(c => c.Id == target)).DeletedAt.Should().NotBeNull();
    (await _db.Charities.AsNoTracking().SingleAsync(c => c.Id == other)).DeletedAt.Should().BeNull();
    (await _db.Penalties.CountAsync(p => p.CharityId == target)).Should().Be(1);
    (await _db.CharityBalances.CountAsync(b => b.CharityId == target)).Should().Be(1);
    (await _db.Disbursements.CountAsync(d => d.CharityId == target)).Should().Be(1);
    (await _db.HabitVersions.CountAsync(v => v.CharityId == target)).Should().Be(1);
    (await _db.HabitExecutions.CountAsync()).Should().Be(2);
    (await _db.HabitChecks.CountAsync()).Should().Be(2);
    (await _db.Configurations.CountAsync()).Should().Be(2, "a user's settings row must never be erased");
    (await _db.CharityBalances.Where(b => b.CharityId == target).Select(b => b.AccruedCents).SingleAsync())
      .Should().Be(500);

    // A setting that pointed at the switched-off charity is cleared, the rest of the row kept.
    var cfg = await _db.Configurations.AsNoTracking().SingleAsync(c => c.UserId == "user-target");
    cfg.DefaultCharityId.Should().BeNull();
    cfg.Timezone.Should().Be("Asia/Singapore");
    (await _db.Configurations.AsNoTracking().SingleAsync(c => c.UserId == "user-other"))
      .DefaultCharityId.Should().Be(other);

    // Switched off: hidden from search, still readable by id (for history, emails, payouts).
    var found = (await Charities().Search(new CharitySearch { Limit = 50, Skip = 0 })).Get().Select(c => c.Id).ToList();
    found.Should().NotContain(target);
    found.Should().Contain(other);
    (await Charities().Get(target)).Get().Should().NotBeNull();

    // Deleting again is a harmless no-op success.
    (await Charities().Delete(target)).Get().Should().NotBeNull();
  }

  [SkippableFact]
  public async Task Database_RefusesToEraseCharityThatHasMoneyRecords()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var (target, _) = await SeedTwoCharitiesWithHistory();
    _db.ChangeTracker.Clear();

    var act = () => _db.Database.ExecuteSqlRawAsync(@"DELETE FROM ""Charities"" WHERE ""Id"" = {0}", target);
    (await act.Should().ThrowAsync<PostgresException>())
      .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);

    (await _db.Charities.CountAsync(c => c.Id == target)).Should().Be(1);
    (await _db.Penalties.CountAsync(p => p.CharityId == target)).Should().Be(1);
    (await _db.Configurations.CountAsync()).Should().Be(2);
  }

  [SkippableFact]
  public async Task SwitchedOffCharity_CannotBeChosen_ButExistingHabitsKeepWorking()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var (target, other) = await SeedTwoCharitiesWithHistory();
    (await Charities().Delete(target)).Get().Should().NotBeNull();
    _db.ChangeTracker.Clear();

    var habits = new HabitRepository(_db, NullLogger<HabitRepository>.Instance);
    var quota = new Quota("free", "habits", 100);

    // New habit on the switched-off charity: refused as a bad request.
    var created = await habits.Create("user-other", Version(target), null, quota);
    created.IsSuccess().Should().BeFalse();
    created.FailureOrDefault().Should().BeOfType<InvalidRequestException>();
    _db.ChangeTracker.Clear();

    // Editing a habit that already uses the switched-off charity (keeping it) still works.
    var existingHabit = await _db.HabitVersions.Where(v => v.CharityId == target).Select(v => v.HabitId).SingleAsync();
    var kept = await habits.Update(existingHabit, "user-target", Version(target) with { Task = "run 5k" }, true);
    kept.IsSuccess().Should().BeTrue();
    kept.Get().Should().NotBeNull();
    _db.ChangeTracker.Clear();

    // Moving another habit onto the switched-off charity: refused.
    var otherHabit = await _db.HabitVersions.Where(v => v.CharityId == other).Select(v => v.HabitId).SingleAsync();
    var moved = await habits.Update(otherHabit, "user-other", Version(target), true);
    moved.IsSuccess().Should().BeFalse();
    moved.FailureOrDefault().Should().BeOfType<InvalidRequestException>();
    _db.ChangeTracker.Clear();

    // Choosing it as the default charity: refused.
    var configs = new ConfigurationRepository(_db, NullLogger<ConfigurationRepository>.Instance);
    var cfgId = await _db.Configurations.Where(c => c.UserId == "user-other").Select(c => c.Id).SingleAsync();
    var setDefault = await configs.Update(cfgId, "user-other",
      new ConfigurationRecord { Timezone = "Asia/Singapore", DefaultCharityId = target });
    setDefault.IsSuccess().Should().BeFalse();
    setDefault.FailureOrDefault().Should().BeOfType<InvalidRequestException>();
  }

  [SkippableFact]
  public async Task StaleMetadataSave_AdminEditOrPledgeSync_NeverSwitchesACharityBackOn()
  {
    // A context that read the charity before it was deleted, then saves metadata (admin edit or the
    // Pledge sync), must not write DeletedAt = null back.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var (target, _) = await SeedTwoCharitiesWithHistory();
    _db.ExternalIds.Add(new ExternalIdData { CharityId = target, Source = "pledge", ExternalKey = "org_target" });
    await _db.SaveChangesAsync();
    _db.ChangeTracker.Clear();

    await using var stale = new MainDbContext(_monitor, NullLoggerFactory.Instance);
    (await stale.Charities.SingleAsync(c => c.Id == target)).DeletedAt.Should().BeNull(); // tracked, pre-delete

    (await Charities().Delete(target)).Get().Should().NotBeNull();

    var staleRepo = new CharityRepository(stale, NullLogger<CharityRepository>.Instance);
    (await staleRepo.Update(target, new CharityRecord { Name = "Renamed" })).IsSuccess().Should().BeTrue();
    var synced = await staleRepo.BulkUpsert([
      new BulkUpsertCharity
      {
        Charity = new CharityRecord { Name = "Renamed by sync" },
        ExternalId = new ExternalIdRecord { Source = "pledge", ExternalKey = "org_target" },
        CauseKeys = [],
      }
    ]);
    synced.IsSuccess().Should().BeTrue();
    synced.Get().CharitiesCreated.Should().Be(0, "the sync matches the existing charity, no duplicate");

    _db.ChangeTracker.Clear();
    var row = await _db.Charities.AsNoTracking().SingleAsync(c => c.Id == target);
    row.Name.Should().Be("Renamed by sync");
    row.DeletedAt.Should().NotBeNull("metadata saves never switch a charity back on");
  }

  [SkippableFact]
  public async Task Delete_WaitsForAnInFlightDefaultChoice_ThenClearsIt()
  {
    // A user is choosing the charity as their default (transaction open) when an admin deletes it.
    // The delete must wait for that choice to commit and then clear it, never leave a default that
    // points at a switched-off charity.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var (target, _) = await SeedTwoCharitiesWithHistory();
    _db.ChangeTracker.Clear();

    await using var userCtx = new MainDbContext(_monitor, NullLoggerFactory.Instance);
    await using var userTx = await userCtx.Database.BeginTransactionAsync();
    var configs = new ConfigurationRepository(userCtx, NullLogger<ConfigurationRepository>.Instance);
    var cfgId = await userCtx.Configurations.Where(c => c.UserId == "user-other").Select(c => c.Id).SingleAsync();
    (await configs.Update(cfgId, "user-other", new ConfigurationRecord { Timezone = "UTC", DefaultCharityId = target }))
      .IsSuccess().Should().BeTrue();

    await using var adminCtx = new MainDbContext(_monitor, NullLoggerFactory.Instance);
    var delete = new CharityRepository(adminCtx, NullLogger<CharityRepository>.Instance).Delete(target);
    await Task.Delay(500);
    delete.IsCompleted.Should().BeFalse("the delete waits on the charity row the user's choice holds");

    await userTx.CommitAsync();
    (await delete).Get().Should().NotBeNull();

    _db.ChangeTracker.Clear();
    (await _db.Configurations.AsNoTracking().SingleAsync(c => c.UserId == "user-other")).DefaultCharityId
      .Should().BeNull("the delete ran after the choice committed and cleared it");
  }

  [SkippableFact]
  public async Task Delete_InsideACallersTransactionScope_Works()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var (target, _) = await SeedTwoCharitiesWithHistory();
    _db.ChangeTracker.Clear();

    using (var scope = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
    {
      (await Charities().Delete(target)).Get().Should().NotBeNull();
      scope.Complete();
    }

    _db.ChangeTracker.Clear();
    (await _db.Charities.AsNoTracking().SingleAsync(c => c.Id == target)).DeletedAt.Should().NotBeNull();
  }

  private static HabitVersionRecord Version(Guid charityId) => new()
  {
    CharityId = charityId,
    Task = "run",
    DaysOfWeek = ["Mon"],
    NotificationTime = new TimeOnly(8, 0),
    Stake = new Money(1m, Currency.FromCode("USD")),
    Ratio = 1m,
    Timezone = "Asia/Singapore",
  };

  // Two charities, each with one user whose settings, habit history and money records point at it.
  private async Task<(Guid Target, Guid Other)> SeedTwoCharitiesWithHistory()
  {
    var target = new CharityData { Name = "Target Charity" };
    var other = new CharityData { Name = "Other Charity" };
    _db.Charities.AddRange(target, other);
    await _db.SaveChangesAsync();
    await SeedUserWithHistory("user-target", target.Id);
    await SeedUserWithHistory("user-other", other.Id);
    return (target.Id, other.Id);
  }

  private async Task SeedUserWithHistory(string userId, Guid charityId)
  {
    var habitId = Guid.NewGuid();
    var versionId = Guid.NewGuid();
    var execId = Guid.NewGuid();
    _db.Users.Add(new UserData { Id = userId, Username = $"u_{userId}", Email = $"{userId}@test.local", Active = true });
    _db.Configurations.Add(new ConfigurationData
    {
      Id = Guid.NewGuid(), UserId = userId, Timezone = "Asia/Singapore", EndOfDay = new TimeOnly(0, 0),
      DefaultCharityId = charityId,
    });
    _db.Habits.Add(new HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
    _db.HabitVersions.Add(new HabitVersionData
    {
      Id = versionId, HabitId = habitId, CharityId = charityId, Version = 1, Task = "run", DaysOfWeek = ["Mon"],
      StakeCents = 500, RatioBasisPoints = 10000, Timezone = "Asia/Singapore",
    });
    _db.HabitExecutions.Add(new HabitExecutionData
    {
      Id = execId, HabitVersionId = versionId, Date = new DateOnly(2026, 3, 2),
      Status = HabitExecutionStatusData.Failed, PaymentProcessed = true,
    });
    _db.HabitChecks.Add(new HabitCheckData
    {
      Id = Guid.NewGuid(), HabitId = habitId, ExecutionId = execId, LocalDate = new DateOnly(2026, 3, 2),
      Timezone = "Asia/Singapore", OpensAtUtc = new DateTime(2026, 3, 1, 16, 0, 0, DateTimeKind.Utc),
      DeadlineUtc = new DateTime(2026, 3, 2, 15, 59, 0, DateTimeKind.Utc), Status = HabitCheckStatusData.Missed,
    });
    _db.Penalties.Add(new PenaltyData
    {
      Id = Guid.NewGuid(), HabitExecutionId = execId, UserId = userId, CharityId = charityId, AmountCents = 500,
      Currency = "USD", Status = (int)PenaltyStatus.Charged, PaymentIntentId = $"int_{userId}", Attempts = 1,
    });
    _db.CharityBalances.Add(new CharityBalanceData { CharityId = charityId, Currency = "USD", AccruedCents = 500 });
    _db.Disbursements.Add(new DisbursementData
    {
      CharityId = charityId, Currency = "USD", AmountCents = 0, Status = 0, PledgeOrganizationId = "org_fake",
    });
    await _db.SaveChangesAsync();
  }
}
