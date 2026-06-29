using App.Modules.Charities.Data;
using App.Modules.Disbursement.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.Penalty.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using CSharp_Result;
using Domain.Disbursement;
using Domain.Penalty;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;

namespace IntTest.Disbursement;

// DB-backed integration tests proving the payout claim/mark/release ledger against a REAL
// MainDbContext: grouped-and-stamped claim, no-org-id skip, min-payout threshold, idempotent
// MarkDisbursed, MarkFailed-releases-penalties, and a full claim->donate->mark round trip.
//
// Like the penalty suite, these require a real Postgres (raw FOR UPDATE + transactions). The
// connection comes from PENALTY_TEST_DB; when unset the tests Skip rather than fail.
[Collection(DbIntegrationCollection.Name)]
public class DisbursementIntegrationTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed disbursement integration test.";
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

  private static DisbursementRepository Repo(MainDbContext db)
    => new(db, NullLogger<DisbursementRepository>.Instance);

  private static DisbursementService Service(IDisbursementRepository repo, IDonationGateway gw)
    => new(repo, gw, NullLogger<DisbursementService>.Instance);

  private static readonly DonorIdentity Donor = new()
  {
    FirstName = "LazyTax", LastName = "Donations", Email = "donations@lazytax.club"
  };

  private async Task<(string userId, Guid charityId)> SeedUserAndCharity(string? pledgeOrgId = "org_pledge_1")
  {
    var userId = $"user-{Guid.NewGuid():N}";
    var charityId = Guid.NewGuid();
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    if (pledgeOrgId != null)
      _db.ExternalIds.Add(new ExternalIdData { Source = "pledge", ExternalKey = pledgeOrgId, CharityId = charityId });
    await _db.SaveChangesAsync();
    return (userId, charityId);
  }

  private async Task<Guid> SeedExecution(string userId, Guid charityId)
  {
    var habitId = Guid.NewGuid();
    var versionId = Guid.NewGuid();
    var executionId = Guid.NewGuid();
    _db.Habits.Add(new App.Modules.Habit.Data.HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
    _db.HabitVersions.Add(new App.Modules.HabitVersion.Data.HabitVersionData
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

  // Insert a Charged, not-yet-disbursed penalty (the input to the payout pipeline).
  private async Task<Guid> SeedChargedPenalty(string userId, Guid charityId, long amountCents, string currency = "USD")
  {
    var executionId = await SeedExecution(userId, charityId);
    var p = new PenaltyData
    {
      Id = Guid.NewGuid(),
      HabitExecutionId = executionId,
      UserId = userId,
      CharityId = charityId,
      AmountCents = (int)amountCents,
      Currency = currency,
      Status = (int)PenaltyStatus.Charged,
      PaymentIntentId = "pi_test",
      Attempts = 1,
      DisbursementId = null
    };
    _db.Penalties.Add(p);
    await _db.SaveChangesAsync();
    return p.Id;
  }

  [SkippableFact]
  public async Task Claim_GroupsByCharityCurrency_CreatesDisbursement_StampsPenalties()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    var id1 = await SeedChargedPenalty(userId, charityId, 300);
    var id2 = await SeedChargedPenalty(userId, charityId, 200);

    var claimed = (await Repo(_db).ClaimPendingPayouts(minPayoutCents: 0, maxGroups: 100)).Get();

    claimed.Should().ContainSingle();
    claimed[0].CharityId.Should().Be(charityId);
    claimed[0].PledgeOrganizationId.Should().Be("org_pledge_1");
    claimed[0].Amount.Amount.Should().Be(5.00m); // 300 + 200 cents summed

    await using var verify = NewContext(_conn!);
    var disb = await verify.Disbursements.AsNoTracking().SingleAsync(d => d.CharityId == charityId);
    disb.AmountCents.Should().Be(500);
    disb.Status.Should().Be((int)DisbursementStatus.Pending);
    disb.PledgeOrganizationId.Should().Be("org_pledge_1");

    // Both penalties stamped with the disbursement id (claimed, excluded from re-selection).
    var penalties = await verify.Penalties.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();
    penalties.Should().OnlyContain(x => x.DisbursementId == disb.Id);
    penalties.Select(x => x.Id).Should().BeEquivalentTo(new[] { id1, id2 });
  }

  [SkippableFact]
  public async Task Claim_DifferentCurrenciesSameCharity_ProduceSeparateDisbursements()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    await SeedChargedPenalty(userId, charityId, 500, "USD");
    await SeedChargedPenalty(userId, charityId, 300, "SGD");

    var claimed = (await Repo(_db).ClaimPendingPayouts(0, 100)).Get();

    claimed.Should().HaveCount(2); // one per (charity, currency)
    await using var verify = NewContext(_conn!);
    var disbs = await verify.Disbursements.AsNoTracking().Where(d => d.CharityId == charityId).ToListAsync();
    disbs.Should().HaveCount(2);
    disbs.Single(d => d.Currency == "USD").AmountCents.Should().Be(500);
    disbs.Single(d => d.Currency == "SGD").AmountCents.Should().Be(300);
  }

  [SkippableFact]
  public async Task Claim_NoPledgeOrgId_LeavesPenaltiesUnclaimed()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity(pledgeOrgId: null); // charity has no pledge link
    await SeedChargedPenalty(userId, charityId, 500);

    var claimed = (await Repo(_db).ClaimPendingPayouts(0, 100)).Get();

    claimed.Should().BeEmpty();
    await using var verify = NewContext(_conn!);
    (await verify.Disbursements.AsNoTracking().AnyAsync(d => d.CharityId == charityId)).Should().BeFalse();
    // Penalty stays pending payout (DisbursementId still null) for once the charity is synced.
    (await verify.Penalties.AsNoTracking().SingleAsync(x => x.UserId == userId)).DisbursementId.Should().BeNull();
  }

  [SkippableFact]
  public async Task Claim_BelowMinPayoutThreshold_IsSkipped()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    await SeedChargedPenalty(userId, charityId, 50); // $0.50, below a $1.00 threshold

    var claimed = (await Repo(_db).ClaimPendingPayouts(minPayoutCents: 100, maxGroups: 100)).Get();

    claimed.Should().BeEmpty();
    await using var verify = NewContext(_conn!);
    (await verify.Penalties.AsNoTracking().SingleAsync(x => x.UserId == userId)).DisbursementId.Should().BeNull();
  }

  [SkippableFact]
  public async Task Claim_DoesNotReSelectAlreadyClaimedPenalties()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    await SeedChargedPenalty(userId, charityId, 500);

    (await Repo(_db).ClaimPendingPayouts(0, 100)).Get().Should().ContainSingle();
    // Second claim finds nothing pending (the penalties are stamped) -> no double-donate.
    await using var c2 = NewContext(_conn!);
    (await Repo(c2).ClaimPendingPayouts(0, 100)).Get().Should().BeEmpty();
  }

  [SkippableFact]
  public async Task MarkDisbursed_SetsCompleted_AndIsIdempotent()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    await SeedChargedPenalty(userId, charityId, 500);
    var disbId = (await Repo(_db).ClaimPendingPayouts(0, 100)).Get().Single().DisbursementId;

    await using (var c1 = NewContext(_conn!)) (await Repo(c1).MarkDisbursed(disbId, "don_1")).Get();
    // Second call (e.g. a reconcile racing the original) must be a no-op, not overwrite.
    await using (var c2 = NewContext(_conn!)) (await Repo(c2).MarkDisbursed(disbId, "don_2")).Get();

    await using var verify = NewContext(_conn!);
    var d = await verify.Disbursements.AsNoTracking().SingleAsync(x => x.Id == disbId);
    d.Status.Should().Be((int)DisbursementStatus.Completed);
    d.ProviderDonationId.Should().Be("don_1"); // first winner kept
  }

  [SkippableFact]
  public async Task MarkFailed_ReleasesPenalties_ForRetry()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    await SeedChargedPenalty(userId, charityId, 500);
    var disbId = (await Repo(_db).ClaimPendingPayouts(0, 100)).Get().Single().DisbursementId;

    await using (var c1 = NewContext(_conn!)) (await Repo(c1).MarkFailed(disbId, "pledge 422")).Get();

    await using var verify = NewContext(_conn!);
    var d = await verify.Disbursements.AsNoTracking().SingleAsync(x => x.Id == disbId);
    d.Status.Should().Be((int)DisbursementStatus.Failed);
    d.LastError.Should().Be("pledge 422");
    d.Attempts.Should().Be(1);
    // Penalty released back to pending payout (DisbursementId null) so a later pass re-claims it.
    (await verify.Penalties.AsNoTracking().SingleAsync(x => x.UserId == userId)).DisbursementId.Should().BeNull();
  }

  [SkippableFact]
  public async Task FullRoundTrip_ClaimDonateMark_CompletesAndStaysClaimed()
  {
    Skip.If(_conn == null, SkipReason);

    var (userId, charityId) = await SeedUserAndCharity();
    await SeedChargedPenalty(userId, charityId, 500);

    var gw = StubDonationGateway.Succeeds("don_rt");
    var count = (int)await Service(Repo(_db), gw).ProcessPending(Donor, 0, 100);
    count.Should().Be(1);

    gw.CreateCalls.Should().ContainSingle();
    gw.CreateCalls[0].OrganizationId.Should().Be("org_pledge_1");
    gw.CreateCalls[0].Amount.Amount.Should().Be(5.00m);

    await using var verify = NewContext(_conn!);
    var d = await verify.Disbursements.AsNoTracking().SingleAsync(x => x.CharityId == charityId);
    d.Status.Should().Be((int)DisbursementStatus.Completed);
    d.ProviderDonationId.Should().Be("don_rt");
    // The penalty remains stamped to the completed disbursement (the audit trail).
    (await verify.Penalties.AsNoTracking().SingleAsync(x => x.UserId == userId)).DisbursementId.Should().Be(d.Id);

    // A second pass has nothing left to do.
    await using var c2 = NewContext(_conn!);
    ((int)await Service(Repo(c2), gw).ProcessPending(Donor, 0, 100)).Should().Be(0);
  }
}
