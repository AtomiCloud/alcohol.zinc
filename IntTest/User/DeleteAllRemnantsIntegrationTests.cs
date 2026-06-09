using App.Modules.Charities.Data;
using App.Modules.Configurations.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Payment.Data;
using App.Modules.Protection.Data;
using App.Modules.Users.Data;
using App.Modules.Vacation.Data;
using App.StartUp.Database;
using App.StartUp.Options;
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
    var monitor = new StaticOptionsMonitor<Dictionary<string, DatabaseOption>>(
      new Dictionary<string, DatabaseOption> { [MainDbContext.Key] = opt });
    _db = new MainDbContext(monitor, NullLoggerFactory.Instance);
    await _db.Database.EnsureDeletedAsync();
    await _db.Database.EnsureCreatedAsync();
  }

  public async Task DisposeAsync()
  {
    if (!_enabled) return;
    await _db.Database.EnsureDeletedAsync();
    await _db.DisposeAsync();
  }

  [Fact]
  public async Task DeleteAllRemnants_PurgesEveryUserTable_NoOrphans_AndLeavesOtherUserIntact()
  {
    if (!_enabled) return; // skipped without a provisioned DB

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
    (await _db.HabitVersions.CountAsync()).Should().Be(1);     // orphan version check
    (await _db.HabitExecutions.CountAsync()).Should().Be(1);   // orphan execution check
    (await _db.FreezeAwards.CountAsync()).Should().Be(1);      // orphan award check (keyed by HabitId)
    (await _db.VacationPeriods.CountAsync()).Should().Be(1);
    (await _db.UserProtections.CountAsync()).Should().Be(1);
    (await _db.FreezeConsumptions.CountAsync()).Should().Be(1);
    (await _db.PaymentCustomers.CountAsync()).Should().Be(1);

    // None of user-A's rows remain anywhere.
    (await _db.Users.CountAsync(x => x.Id == "user-A")).Should().Be(0);
    (await _db.Configurations.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.Habits.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.VacationPeriods.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.UserProtections.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.FreezeConsumptions.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
    (await _db.PaymentCustomers.CountAsync(x => x.UserId == "user-A")).Should().Be(0);

    // The shared charity catalog is untouched.
    (await _db.Charities.CountAsync()).Should().Be(1);
  }

  [Fact]
  public async Task DeleteAllRemnants_IsIdempotent_SecondCallNoOps()
  {
    if (!_enabled) return;

    var charity = new CharityData { Name = "Test Charity" };
    _db.Charities.Add(charity);
    await _db.SaveChangesAsync();
    await SeedFullUser("user-A", charity.Id);

    var repo = new UserRepository(_db, NullLogger<UserRepository>.Instance);

    (await repo.DeleteAllRemnants("user-A")).Get().Should().NotBeNull("first delete removes the real user");
    (await repo.DeleteAllRemnants("user-A")).Get().Should().BeNull("a second delete of an absent user is an idempotent no-op");
    (await repo.DeleteAllRemnants("never-existed")).Get().Should().BeNull("deleting an unknown user is also a no-op");
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
    _db.Habits.Add(new HabitData { Id = habitId, UserId = id, Version = 1, Enabled = true });
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
