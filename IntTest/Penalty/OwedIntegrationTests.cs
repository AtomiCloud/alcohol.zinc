using App.Modules.Charities.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Payment.Airwallex;
using App.Modules.Payment.Data;
using App.Modules.Penalty.API.V1;
using App.Modules.Penalty.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using App.StartUp.Services.Auth;
using CSharp_Result;
using Domain.Payment;
using Domain.Penalty;
using IntTest.Notification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IntTest.Penalty;

// Penalties that could not be charged (no card: Skipped; every attempt failed: Failed) are owed until
// the user adds or fixes their card, which re-queues them. Real Postgres (PENALTY_TEST_DB).
[Collection(DbIntegrationCollection.Name)]
public class OwedIntegrationTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed owed tests.";
  private const string WebhookSecret = "whsec-test";
  private readonly string? _conn = Environment.GetEnvironmentVariable("PENALTY_TEST_DB");
  private MainDbContext _db = null!;

  public async Task InitializeAsync()
  {
    if (_conn == null) return;
    _db = PenaltyIntegrationTests.NewContext(_conn);
    await _db.Database.EnsureCreatedAsync();
  }

  public async Task DisposeAsync()
  {
    if (_conn == null) return;
    await _db.Database.EnsureDeletedAsync();
    await _db.DisposeAsync();
  }

  private PenaltyRepository Repo(MainDbContext? db = null) =>
    new(db ?? _db, NullLogger<PenaltyRepository>.Instance);

  private async Task<string> SeedUser()
  {
    var userId = $"user-{Guid.NewGuid():N}";
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    await _db.SaveChangesAsync();
    return userId;
  }

  private async Task<Guid> SeedCharity(string name, bool decommissioned = false)
  {
    var id = Guid.NewGuid();
    _db.Charities.Add(new CharityData
    {
      Id = id, Name = name, DecommissionedAt = decommissioned ? DateTime.UtcNow : null,
      DeletedAt = decommissioned ? DateTime.UtcNow : null
    });
    await _db.SaveChangesAsync();
    return id;
  }

  // A failed day with a $5 stake, and (when status is set) its penalty.
  private async Task<(Guid ExecutionId, Guid? PenaltyId)> SeedFailedDay(string userId, Guid charityId, string task,
    DateOnly date, PenaltyStatus? status, string? intentId = null, int attempts = 0, int stakeCents = 500)
  {
    var habitId = Guid.NewGuid();
    var versionId = Guid.NewGuid();
    var executionId = Guid.NewGuid();
    _db.Habits.Add(new HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
    _db.HabitVersions.Add(new HabitVersionData
    {
      Id = versionId, HabitId = habitId, CharityId = charityId, Version = 1, Task = task,
      DaysOfWeek = ["Monday"], Timezone = "UTC", StakeCents = stakeCents, StakeCurrency = "USD",
      RatioBasisPoints = 10000
    });
    _db.HabitExecutions.Add(new HabitExecutionData
    {
      Id = executionId, HabitVersionId = versionId, Date = date, Status = HabitExecutionStatusData.Failed
    });
    Guid? penaltyId = null;
    if (status is { } s)
    {
      penaltyId = Guid.NewGuid();
      _db.Penalties.Add(new PenaltyData
      {
        Id = penaltyId.Value, HabitExecutionId = executionId, UserId = userId, CharityId = charityId,
        AmountCents = stakeCents, Currency = "USD", Status = (int)s, PaymentIntentId = intentId, Attempts = attempts,
        LastError = s is PenaltyStatus.Failed or PenaltyStatus.Skipped ? "old error" : null
      });
    }

    await _db.SaveChangesAsync();
    return (executionId, penaltyId);
  }

  private async Task<PenaltyData> Penalty(Guid id)
  {
    await using var v = PenaltyIntegrationTests.NewContext(_conn!);
    return await v.Penalties.AsNoTracking().SingleAsync(p => p.Id == id);
  }

  private static readonly DateOnly Day = new(2026, 11, 2);

  // The Airwallex webhook for a verified penalty consent, end to end through the real services.
  private async Task VerifyCard(string userId)
  {
    var customerId = $"cus_{Guid.NewGuid():N}";
    _db.PaymentCustomers.Add(new PaymentCustomerData
    {
      Id = Guid.NewGuid(), UserId = userId, AirwallexCustomerId = customerId,
      CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    });
    await _db.SaveChangesAsync();

    var hmac = new AirwallexHmacCalculator(Options.Create(new PaymentOption
    {
      Airwallex = new AirwallexOption { Id = "id", Key = "key", Webhook = WebhookSecret }
    }));
    var webhook = new AirwallexWebhookService(
      new PaymentService(new PaymentCustomerRepository(_db, NullLogger<PaymentCustomerRepository>.Instance), null!),
      new NoClaims(), new AirwallexEventAdapter(), hmac, new NoopEmailNotifier(), Repo(),
      NullLogger<AirwallexWebhookService>.Instance);
    var evt = new AirwallexEvent
    {
      Name = "payment_consent.verified",
      Data = new AirwallexEventData
      {
        Object = new AirwallexEventDataObject { Id = "cst_new", Status = "VERIFIED", CustomerId = customerId }
      }
    };
    const string payload = "{}";
    (await webhook.ProcessEvent(evt, "1", payload, hmac.Compute("1", payload).Get())).IsSuccess().Should().BeTrue();
  }

  [SkippableFact]
  public async Task VerifiedCard_RequeuesOnlyUnchargedPenalties_OfThatUser()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var other = await SeedUser();
    var charity = await SeedCharity("Red Cross");
    var gone = await SeedCharity("Closed", decommissioned: true);
    var (_, skipped) = await SeedFailedDay(userId, charity, "Run", Day, PenaltyStatus.Skipped, attempts: 1);
    var (_, failed) = await SeedFailedDay(userId, charity, "Read", Day, PenaltyStatus.Failed, "pi_old", attempts: 5);
    var (_, charged) = await SeedFailedDay(userId, charity, "Swim", Day, PenaltyStatus.Charged, "pi_paid", 1);
    var (_, held) = await SeedFailedDay(userId, gone, "Walk", Day, PenaltyStatus.Held);
    var (_, failedForGone) = await SeedFailedDay(userId, gone, "Yoga", Day, PenaltyStatus.Failed, attempts: 5);
    var (_, othersSkipped) = await SeedFailedDay(other, charity, "Run", Day, PenaltyStatus.Skipped);

    await VerifyCard(userId);

    var s = await Penalty(skipped!.Value);
    (s.Status, s.Attempts, s.PaymentIntentId).Should().Be(((int)PenaltyStatus.Pending, 0, (string?)null));
    var f = await Penalty(failed!.Value);
    (f.Status, f.Attempts, f.PaymentIntentId).Should().Be(((int)PenaltyStatus.Pending, 0, "pi_old"),
      "the intent stays so the drain reconciles it");
    (await Penalty(charged!.Value)).Status.Should().Be((int)PenaltyStatus.Charged);
    (await Penalty(held!.Value)).Status.Should().Be((int)PenaltyStatus.Held);
    (await Penalty(failedForGone!.Value)).Status.Should().Be((int)PenaltyStatus.Held,
      "its charity was decommissioned, so it must never be charged");
    (await Penalty(othersSkipped!.Value)).Status.Should().Be((int)PenaltyStatus.Skipped);
  }

  // A Failed penalty whose intent already took the money: after the re-queue the drain looks the
  // intent up and records it, instead of creating a second charge.
  [SkippableFact]
  public async Task RequeuedFailedPenalty_WithAnIntent_IsReconciled_NotChargedAgain()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var charity = await SeedCharity("Red Cross");
    var (_, failed) = await SeedFailedDay(userId, charity, "Read", Day, PenaltyStatus.Failed, "pi_old", attempts: 5);

    (await Repo().RequeueUncharged(userId)).Get().Should().Be(1);
    (await Repo().RequeueUncharged(userId)).Get().Should().Be(0, "a second webhook replay changes nothing");
    var payment = StubPaymentService.Succeeds("pi_old");
    await using (var c = PenaltyIntegrationTests.NewContext(_conn!))
      await new PenaltyService(Repo(c), payment, new NoopEmailNotifier(), NullLogger<PenaltyService>.Instance)
        .ProcessPending(batchSize: 100, maxAttempts: 5);

    payment.ExistingIntentIdArgs.Should().Equal("pi_old");
    var p = await Penalty(failed!.Value);
    (p.Status, p.PaymentIntentId).Should().Be(((int)PenaltyStatus.Charged, "pi_old"));
    await using var v = PenaltyIntegrationTests.NewContext(_conn!);
    (await v.CharityBalances.AsNoTracking().SingleAsync(b => b.CharityId == charity)).AccruedCents.Should().Be(500);
  }

  [SkippableFact]
  public async Task Owed_ListsUnpaidDays_WithTheirState_AndTotal()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var charity = await SeedCharity("Red Cross");
    var gone = await SeedCharity("Closed", decommissioned: true);
    await SeedFailedDay(userId, charity, "Run", Day, null);
    await SeedFailedDay(userId, charity, "Read", Day.AddDays(1), PenaltyStatus.Processing, stakeCents: 250);
    await SeedFailedDay(userId, charity, "Swim", Day.AddDays(2), PenaltyStatus.Skipped);
    await SeedFailedDay(userId, charity, "Walk", Day.AddDays(3), PenaltyStatus.Failed, "pi_x", 5);
    await SeedFailedDay(userId, charity, "Paid", Day, PenaltyStatus.Charged, "pi_paid");
    await SeedFailedDay(userId, gone, "Held", Day, PenaltyStatus.Held);
    await SeedFailedDay(userId, charity, "Free", Day, null, stakeCents: 0);

    var res = (await Repo().ListOwed(userId)).Get().ToRes();

    res.Items.Select(i => (i.HabitName, i.LocalDate, i.Amount, i.State, i.Reason)).Should().Equal(
      ("Run", "2026-11-02", "5.00", "charging", (string?)null),
      ("Read", "2026-11-03", "2.50", "charging", (string?)null),
      ("Swim", "2026-11-04", "5.00", "couldNotCharge", "noCard"),
      ("Walk", "2026-11-05", "5.00", "couldNotCharge", "chargeFailed"));
    res.Items.Should().AllSatisfy(i => (i.CharityId, i.CharityName, i.Currency).Should().Be((charity, "Red Cross", "USD")));
    (res.Total, res.Currency).Should().Be(("17.50", "USD"));
  }

  private sealed class NoClaims : IAuthManagement
  {
    public Task<Result<Unit>> SetClaim(string userId, string claimKey, string claimValue) =>
      Task.FromResult<Result<Unit>>(new Unit());

    public Task<Result<Unit>> AssignRole(string userId, string roleId) => throw new NotImplementedException();
    public Task<Result<Unit>> RemoveRole(string userId, string roleId) => throw new NotImplementedException();
    public Task<Result<Unit>> RemoveClaim(string userId, string claimKey) => throw new NotImplementedException();
    public Task<Result<Unit>> DeleteUser(string userId) => throw new NotImplementedException();
    public Task<Result<string>> CreateOneTimeToken(string email, int expiresInSeconds) => throw new NotImplementedException();
  }
}
