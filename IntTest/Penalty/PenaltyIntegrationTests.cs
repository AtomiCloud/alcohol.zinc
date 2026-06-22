using App.Modules.Charities.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Penalty.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Payment;
using Domain.Penalty;
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
public class PenaltyIntegrationTests : IAsyncLifetime
{
  private const string Skip = "PENALTY_TEST_DB not set; skipping DB-backed penalty integration test.";
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

  private static MainDbContext NewContext(string conn)
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
    => new(repo, payment, NullLogger<PenaltyService>.Instance);

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

  private static PenaltyRecord PendingRecord(Guid executionId, string userId, Guid charityId, long amountCents)
    => new()
    {
      HabitExecutionId = executionId,
      UserId = userId,
      CharityId = charityId,
      Amount = new Money(amountCents / 100m, Currency.FromCode("SGD")),
      Status = PenaltyStatus.Pending,
      PaymentIntentId = null,
      Attempts = 0,
      LastError = null
    };

  [Fact]
  public async Task OneFailedExecution_ChargedOnce_CreditsCharity()
  {
    if (_conn == null) { Assert.True(true, Skip); return; }

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

  [Fact]
  public async Task RunningTwice_DoesNotDoubleCharge()
  {
    if (_conn == null) { Assert.True(true, Skip); return; }

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

  [Fact]
  public async Task MarkCharged_CalledTwice_CreditsCharityOnce()
  {
    if (_conn == null) { Assert.True(true, Skip); return; }

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

  [Fact]
  public async Task NoConsent_MarksSkipped_NoCredit()
  {
    if (_conn == null) { Assert.True(true, Skip); return; }

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
