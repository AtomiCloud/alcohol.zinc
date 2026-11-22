using App.Modules.Charities.Data;
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
using Domain.Notification;
using Domain.Penalty;
using IntTest.Notification;
using IntTest.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;
using StubPaymentService = IntTest.Penalty.StubPaymentService;

namespace IntTest.Charity;

/// <summary>
/// Real-database tests for decommissioning a charity: hidden like a delete (deprecate), and no more
/// money flows to it. Charges not yet taken from a card go on hold, later missed days are recorded on
/// hold, payouts to it stop, and every record is kept.
///
/// Gated by ZINC_DELETE_TEST_DB (the Postgres host), on its own database so it never races the other
/// classes. Run locally:
///   docker run -d --name zinc-deltest -e POSTGRES_USER=test -e POSTGRES_PASSWORD=test \
///     -e POSTGRES_DB=zinctest -p 55432:5432 postgres:16
///   ZINC_DELETE_TEST_DB=localhost dotnet test IntTest/IntTest.csproj --filter CharityDecommission
/// </summary>
public class CharityDecommissionIntegrationTests : IAsyncLifetime
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
      Database = "zinc_charity_decommission_test",
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

  private CharityRepository Charities(MainDbContext? db = null) => new(db ?? _db, NullLogger<CharityRepository>.Instance);
  private PenaltyRepository Penalties(MainDbContext? db = null) => new(db ?? _db, NullLogger<PenaltyRepository>.Instance);

  private CharityService Service() => new(Charities(), NullLogger<CharityService>.Instance);

  private static InboxNotificationRecord TestNotice(string name) => new()
  {
    Kind = InboxKinds.CharityDecommissioned, Title = "t", Body = name, DedupeKey = "test-notice",
  };

  private Task<Result<CharityDecommissionResult?>> Decommission(Guid id, MainDbContext? db = null)
    => Charities(db).Decommission(id, TestNotice);

  [SkippableFact]
  public async Task Decommission_HoldsChargesNotYetTaken_KeepsEverythingElse()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var target = await SeedCharity("Target Charity");
    var other = await SeedCharity("Other Charity");
    await SeedUser("user-a", target);
    await SeedUser("user-b", other);

    var queued = await SeedPenalty("user-a", target, PenaltyStatus.Pending);
    var claimedNoIntent = await SeedPenalty("user-a", target, PenaltyStatus.Processing);
    var retryWithIntent = await SeedPenalty("user-a", target, PenaltyStatus.Pending, intent: "int_started");
    var charged = await SeedPenalty("user-a", target, PenaltyStatus.Charged, intent: "int_paid");
    var failed = await SeedPenalty("user-a", target, PenaltyStatus.Failed);
    var otherQueued = await SeedPenalty("user-b", other, PenaltyStatus.Pending);
    _db.CharityBalances.Add(new CharityBalanceData { CharityId = target, Currency = "USD", AccruedCents = 700 });
    await _db.SaveChangesAsync();
    _db.ChangeTracker.Clear();

    var result = (await Decommission(target)).Get();
    _db.ChangeTracker.Clear();

    result.Should().NotBeNull();
    result!.HeldPenalties.Should().Be(3);
    result.NoticesPosted.Should().Be(1);
    result.CharityName.Should().Be("Target Charity");
    result.AffectedUserIds.Should().Equal("user-a");

    async Task<PenaltyData> Row(Guid id) => await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == id);
    (await Row(queued)).Status.Should().Be((int)PenaltyStatus.Held, "a queued charge is put on hold, not cancelled");
    (await Row(queued)).LastError.Should().Be(PenaltyRepository.HeldReason);
    (await Row(claimedNoIntent)).Status.Should().Be((int)PenaltyStatus.Held, "no payment was started for it yet");
    (await Row(retryWithIntent)).Status.Should().Be((int)PenaltyStatus.Held,
      "a created-but-unconfirmed payment is never confirmed after a decommission");
    (await Row(charged)).Status.Should().Be((int)PenaltyStatus.Charged);
    (await Row(failed)).Status.Should().Be((int)PenaltyStatus.Failed);
    (await Row(otherQueued)).Status.Should().Be((int)PenaltyStatus.Pending, "other charities are untouched");

    var c = await _db.Charities.AsNoTracking().SingleAsync(x => x.Id == target);
    c.DeletedAt.Should().NotBeNull("decommissioned charities are hidden too");
    c.DecommissionedAt.Should().NotBeNull();
    (await _db.Charities.AsNoTracking().SingleAsync(x => x.Id == other)).DecommissionedAt.Should().BeNull();

    // Nothing is erased; collected money stays recorded under the charity.
    (await _db.Penalties.CountAsync(p => p.CharityId == target)).Should().Be(5);
    (await _db.CharityBalances.AsNoTracking().SingleAsync(b => b.CharityId == target)).AccruedCents.Should().Be(700);
    (await _db.HabitVersions.CountAsync(v => v.CharityId == target)).Should().Be(1);
    var cfg = await _db.Configurations.AsNoTracking().SingleAsync(x => x.UserId == "user-a");
    cfg.DefaultCharityId.Should().BeNull("a default pointing at it is cleared, as with a delete");
    cfg.Timezone.Should().Be("Asia/Singapore");

    // Hidden from search.
    (await Charities().Search(new CharitySearch { Limit = 50, Skip = 0 })).Get().Select(x => x.Id)
      .Should().NotContain(target);

    // Repeating it is a harmless success that keeps the first times.
    var again = (await Decommission(target)).Get();
    again!.HeldPenalties.Should().Be(0);
    again.NoticesPosted.Should().Be(0, "each user is told once");
    _db.ChangeTracker.Clear();
    var c2 = await _db.Charities.AsNoTracking().SingleAsync(x => x.Id == target);
    c2.DecommissionedAt.Should().Be(c.DecommissionedAt);
    c2.DeletedAt.Should().Be(c.DeletedAt);
  }

  [SkippableFact]
  public async Task Decommission_AfterDeprecate_StillStopsTheMoney()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var target = await SeedCharity("Target Charity");
    await SeedUser("user-a", target);
    var queued = await SeedPenalty("user-a", target, PenaltyStatus.Pending);
    _db.ChangeTracker.Clear();

    (await Charities().Delete(target)).Get().Should().NotBeNull();
    _db.ChangeTracker.Clear();
    var deprecatedAt = (await _db.Charities.AsNoTracking().SingleAsync(x => x.Id == target)).DeletedAt;
    (await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == queued)).Status
      .Should().Be((int)PenaltyStatus.Pending, "deprecate alone keeps money flowing");

    (await Decommission(target)).Get()!.HeldPenalties.Should().Be(1);
    _db.ChangeTracker.Clear();

    var c = await _db.Charities.AsNoTracking().SingleAsync(x => x.Id == target);
    c.DeletedAt.Should().Be(deprecatedAt);
    c.DecommissionedAt.Should().NotBeNull();
    (await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == queued)).Status.Should().Be((int)PenaltyStatus.Held);
  }

  [SkippableFact]
  public async Task Decommission_UnknownCharity_ReturnsNull()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    (await Decommission(Guid.NewGuid())).Get().Should().BeNull();
    (await Service().Decommission(Guid.NewGuid())).Get().Should().BeNull();
  }

  [SkippableFact]
  public async Task MissedDay_OnDecommissionedCharity_IsRecordedOnHold()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var target = await SeedCharity("Target Charity");
    var other = await SeedCharity("Other Charity");
    await SeedUser("user-a", target);
    var execTarget = await SeedExecution("user-a");
    var execOther = await SeedExecution("user-a");
    _db.ChangeTracker.Clear();
    (await Decommission(target)).Get().Should().NotBeNull();
    _db.ChangeTracker.Clear();

    (await Penalties().EnqueuePending(Record(execTarget, "user-a", target))).Get().Should().BeTrue();
    (await Penalties().EnqueuePending(Record(execOther, "user-a", other))).Get().Should().BeTrue();
    _db.ChangeTracker.Clear();

    var held = await _db.Penalties.AsNoTracking().SingleAsync(p => p.HabitExecutionId == execTarget);
    held.Status.Should().Be((int)PenaltyStatus.Held, "the missed day is still recorded, but never charged");
    held.LastError.Should().Be(PenaltyRepository.HeldReason);
    held.AmountCents.Should().Be(300);
    (await _db.Penalties.AsNoTracking().SingleAsync(p => p.HabitExecutionId == execOther)).Status
      .Should().Be((int)PenaltyStatus.Pending);

    // The drain never claims a held penalty.
    var claimed = (await Penalties().GetPending(10)).Get();
    claimed.Select(p => p.Record.HabitExecutionId).Should().Equal(execOther);
  }

  [SkippableFact]
  public async Task DecommissionWhileChargeInFlight_NoIntentIsSaved_AndLateWritesKeepItOnHold()
  {
    // A drain worker claims a queued charge, then the charity is decommissioned (another request)
    // before the worker records a payment intent. Recording the intent is refused BEFORE any confirm,
    // so no money moves, and the worker's follow-up writes (retry, fail, skip) leave it on hold.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var target = await SeedCharity("Target Charity");
    await SeedUser("user-a", target);
    var queued = await SeedPenalty("user-a", target, PenaltyStatus.Pending);
    _db.ChangeTracker.Clear();

    var worker = Penalties();
    (await worker.GetPending(10)).Get().Select(p => p.Id).Should().Equal(queued);

    await using (var other = new MainDbContext(_monitor, NullLoggerFactory.Instance))
      (await Decommission(target, other)).Get()!.HeldPenalties.Should().Be(1);

    var save = () => worker.SetIntentId(queued, "int_created_after_decommission");
    await save.Should().ThrowAsync<PenaltyHeldException>();

    await worker.Bump(queued, "retry");
    await worker.MarkPending(queued, "int_x", 2);
    (await worker.MarkFailed(queued, "gave up")).Get().Should().BeFalse("nothing was marked, so no failure email");
    await worker.MarkSkipped(queued);
    _db.ChangeTracker.Clear();

    var row = await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == queued);
    row.Status.Should().Be((int)PenaltyStatus.Held);
    row.PaymentIntentId.Should().BeNull();
    row.Attempts.Should().Be(0);
    row.LastError.Should().Be(PenaltyRepository.HeldReason);
  }

  [SkippableFact]
  public async Task Payouts_SkipDecommissionedCharities_TheirMoneyStaysHeld()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var target = await SeedCharity("Target Charity");
    var other = await SeedCharity("Other Charity");
    _db.ExternalIds.Add(new ExternalIdData { Source = "pledge", ExternalKey = "org_target", CharityId = target });
    _db.ExternalIds.Add(new ExternalIdData { Source = "pledge", ExternalKey = "org_other", CharityId = other });
    await SeedUser("user-a", target);
    var collected = await SeedPenalty("user-a", target, PenaltyStatus.Charged, intent: "int_a");
    var otherCollected = await SeedPenalty("user-a", other, PenaltyStatus.Charged, intent: "int_b");
    await _db.SaveChangesAsync();
    _db.ChangeTracker.Clear();

    (await Decommission(target)).Get().Should().NotBeNull();
    _db.ChangeTracker.Clear();

    var payouts = new DisbursementRepository(_db, NullLogger<DisbursementRepository>.Instance);
    (await payouts.IsPayoutStopped(target)).Get().Should().BeTrue();
    (await payouts.IsPayoutStopped(other)).Get().Should().BeFalse();

    var claimed = (await payouts.ClaimPendingPayouts(minPayoutCents: 0, maxGroups: 10)).Get();
    _db.ChangeTracker.Clear();

    claimed.Select(c => c.CharityId).Should().Equal(other);
    (await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == collected)).DisbursementId
      .Should().BeNull("collected money for a decommissioned charity is never paid out");
    (await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == otherCollected)).DisbursementId
      .Should().NotBeNull();
    (await _db.Disbursements.CountAsync(d => d.CharityId == target)).Should().Be(0);
  }

  [SkippableFact]
  public async Task StaleMetadataSaves_AdminEditOrPledgeSync_NeverUndoADecommission()
  {
    // A context that read the charity before it was decommissioned, then saves metadata (admin edit or
    // the Pledge sync), must not write DecommissionedAt = null back.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var target = await SeedCharity("Target Charity");
    _db.ExternalIds.Add(new ExternalIdData { CharityId = target, Source = "pledge", ExternalKey = "org_target" });
    await _db.SaveChangesAsync();
    _db.ChangeTracker.Clear();

    await using var stale = new MainDbContext(_monitor, NullLoggerFactory.Instance);
    (await stale.Charities.SingleAsync(c => c.Id == target)).DecommissionedAt.Should().BeNull(); // tracked, before

    (await Decommission(target)).Get().Should().NotBeNull();

    var staleRepo = Charities(stale);
    (await staleRepo.Update(target, new CharityRecord { Name = "Renamed" })).IsSuccess().Should().BeTrue();
    _db.ChangeTracker.Clear();
    (await _db.Charities.AsNoTracking().SingleAsync(c => c.Id == target)).DecommissionedAt
      .Should().NotBeNull("an admin edit never undoes a decommission");

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
    var c = await _db.Charities.AsNoTracking().SingleAsync(x => x.Id == target);
    c.Name.Should().Be("Renamed by sync");
    c.DecommissionedAt.Should().NotBeNull("the Pledge sync never undoes a decommission");
    c.DeletedAt.Should().NotBeNull();
  }

  [SkippableFact]
  public async Task Service_TellsEachAffectedUserOnce_InTheApp()
  {
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var target = await SeedCharity("Save the Owls");
    var other = await SeedCharity("Other Charity");
    await SeedUser("user-habit", target);              // a live habit gives to it (and it is the default)
    await SeedUser("user-default", other);             // habit elsewhere, but default charity is the target
    await SeedUser("user-unrelated", other);
    await SeedUser("user-deleted-habit", target);      // the only habit giving to it is deleted
    var cfg = await _db.Configurations.SingleAsync(x => x.UserId == "user-default");
    cfg.DefaultCharityId = target;
    var cfgDeleted = await _db.Configurations.SingleAsync(x => x.UserId == "user-deleted-habit");
    cfgDeleted.DefaultCharityId = other;
    var deletedHabit = await _db.Habits.SingleAsync(h => h.UserId == "user-deleted-habit");
    deletedHabit.DeletedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync();
    _db.ChangeTracker.Clear();

    (await Service().Decommission(target)).Get().Should().NotBeNull();
    (await Service().Decommission(target)).Get().Should().NotBeNull(); // a repeat does not post twice
    _db.ChangeTracker.Clear();

    var notes = await _db.InboxNotifications.AsNoTracking().ToListAsync();
    notes.Select(n => n.UserId).Should().BeEquivalentTo("user-habit", "user-default");
    notes.Should().AllSatisfy(n =>
    {
      n.Kind.Should().Be(InboxKinds.CharityDecommissioned);
      n.Title.Should().Be("Pick a new charity");
      n.Body.Should().Contain("Save the Owls");
      n.Data.Should().Contain(target.ToString());
    });
  }

  [SkippableFact]
  public async Task MissedDayBeingRecorded_WhenDecommissionStarts_IsHeldOnceBothFinish()
  {
    // A missed day is being recorded (its transaction is open, the charity looked live) when an admin
    // decommissions the charity. The decommission must wait for it, then hold the new charge; it must
    // never end with a Pending charge for a decommissioned charity.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var target = await SeedCharity("Target Charity");
    await SeedUser("user-a", target);
    var exec = await SeedExecution("user-a");
    _db.ChangeTracker.Clear();

    await using var cronCtx = new MainDbContext(_monitor, NullLoggerFactory.Instance);
    await using var cronTx = await cronCtx.Database.BeginTransactionAsync();
    (await Penalties(cronCtx).EnqueuePending(Record(exec, "user-a", target))).Get().Should().BeTrue();

    await using var adminCtx = new MainDbContext(_monitor, NullLoggerFactory.Instance);
    var decommission = Decommission(target, adminCtx);
    await Task.Delay(500);
    decommission.IsCompleted.Should().BeFalse("the decommission waits on the charity row the enqueue holds");

    await cronTx.CommitAsync();
    (await decommission).Get()!.HeldPenalties.Should().Be(1, "it ran after the enqueue committed and held it");

    _db.ChangeTracker.Clear();
    (await _db.Penalties.AsNoTracking().SingleAsync(p => p.HabitExecutionId == exec)).Status
      .Should().Be((int)PenaltyStatus.Held);
  }

  [SkippableTheory]
  [InlineData("SUCCEEDED", true)]
  [InlineData("REQUIRES_PAYMENT_METHOD", false)]
  [InlineData("REQUIRES_CUSTOMER_ACTION", false)]
  [InlineData(null, false)] // the look-up itself failed
  public async Task HeldChargeWithAPayment_IsOnlyLookedUp_NeverConfirmed(string? providerStatus, bool taken)
  {
    // A charge that had a payment created (e.g. waiting to retry) is put on hold by a decommission.
    // The drain never confirms it. It only asks the provider whether an earlier confirm took the money:
    // if so it is recorded as Charged (the money is held: no payout to this charity), else it stays Held.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var target = await SeedCharity("Target Charity");
    await SeedUser("user-a", target);
    var queued = await SeedPenalty("user-a", target, PenaltyStatus.Pending, intent: "int_created");
    _db.ChangeTracker.Clear();
    (await Decommission(target)).Get()!.HeldPenalties.Should().Be(1);
    await _db.Database.ExecuteSqlRawAsync(
      @"UPDATE ""Penalties"" SET ""UpdatedAt"" = now() - interval '1 hour' WHERE ""Id"" = {0}", queued);
    _db.ChangeTracker.Clear();

    var payment = StubPaymentService.Succeeds("int_created");
    payment.Retrieve = id => providerStatus == null
      ? new Exception("provider unavailable")
      : new Domain.Payment.PaymentIntentResult
      {
        Id = id, Status = providerStatus, Amount = 1.00m, Currency = "USD", CustomerId = "cus", MerchantOrderId = "mo",
      };
    var drain = new PenaltyService(Penalties(), payment, new NoopEmailNotifier(), NullLogger<PenaltyService>.Instance);
    (await drain.ProcessPending(batchSize: 10, maxAttempts: 5)).IsSuccess().Should().BeTrue();
    _db.ChangeTracker.Clear();

    payment.RetrieveCalls.Should().Equal("int_created");
    payment.ExistingIntentIdArgs.Should().BeEmpty("a held charge is never sent to be charged or confirmed");
    var row = await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == queued);
    if (taken)
    {
      row.Status.Should().Be((int)PenaltyStatus.Charged);
      (await _db.CharityBalances.AsNoTracking().SingleAsync(b => b.CharityId == target)).AccruedCents.Should().Be(100);
      var claimed = (await new DisbursementRepository(_db, NullLogger<DisbursementRepository>.Instance)
        .ClaimPendingPayouts(minPayoutCents: 0, maxGroups: 10)).Get();
      claimed.Should().BeEmpty("the money is held: payouts to a decommissioned charity stay stopped");
    }
    else
    {
      row.Status.Should().Be((int)PenaltyStatus.Held);
      row.Attempts.Should().Be(1, "one look-up recorded");
      row.LastError.Should().Contain("int_created");
      // Not looked up again until it is due.
      (await drain.ProcessPending(batchSize: 10, maxAttempts: 5)).IsSuccess().Should().BeTrue();
      payment.RetrieveCalls.Should().HaveCount(1);
    }
  }

  [SkippableFact]
  public async Task Decommission_FailingPartWay_ChangesNothing_IncludingNotices()
  {
    // The notices are written in the decommission's own transaction. If anything fails, nothing is
    // committed: the charity is not decommissioned, nothing is held, nobody is told, defaults are kept.
    Skip.IfNot(_enabled, "requires ZINC_DELETE_TEST_DB (a Postgres host)");

    var target = await SeedCharity("Target Charity");
    await SeedUser("user-a", target);
    var queued = await SeedPenalty("user-a", target, PenaltyStatus.Pending);
    _db.ChangeTracker.Clear();

    var act = () => Charities().Decommission(target, _ => throw new InvalidOperationException("boom"));
    await act.Should().ThrowAsync<InvalidOperationException>();
    _db.ChangeTracker.Clear();

    var c = await _db.Charities.AsNoTracking().SingleAsync(x => x.Id == target);
    c.DecommissionedAt.Should().BeNull();
    c.DeletedAt.Should().BeNull();
    (await _db.Penalties.AsNoTracking().SingleAsync(p => p.Id == queued)).Status.Should().Be((int)PenaltyStatus.Pending);
    (await _db.Configurations.AsNoTracking().SingleAsync(x => x.UserId == "user-a")).DefaultCharityId.Should().Be(target);
    (await _db.InboxNotifications.CountAsync()).Should().Be(0);
  }

  // ---- seeding ----

  private async Task<Guid> SeedCharity(string name)
  {
    var c = new CharityData { Name = name };
    _db.Charities.Add(c);
    await _db.SaveChangesAsync();
    return c.Id;
  }

  // A user with settings (default charity) and one live habit giving to `charityId`.
  private async Task SeedUser(string userId, Guid charityId)
  {
    var habitId = Guid.NewGuid();
    _db.Users.Add(new UserData { Id = userId, Username = $"u_{userId}", Email = $"{userId}@test.local", Active = true });
    _db.Configurations.Add(new ConfigurationData
    {
      Id = Guid.NewGuid(), UserId = userId, Timezone = "Asia/Singapore", EndOfDay = new TimeOnly(0, 0),
      DefaultCharityId = charityId,
    });
    _db.Habits.Add(new HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
    _db.HabitVersions.Add(new HabitVersionData
    {
      Id = Guid.NewGuid(), HabitId = habitId, CharityId = charityId, Version = 1, Task = "run", DaysOfWeek = ["Mon"],
      StakeCents = 300, RatioBasisPoints = 10000, Timezone = "Asia/Singapore",
    });
    await _db.SaveChangesAsync();
  }

  private int _day;

  private async Task<Guid> SeedExecution(string userId)
  {
    var versionId = await _db.HabitVersions.Where(v => v.Habit!.UserId == userId).Select(v => v.Id).FirstAsync();
    var execId = Guid.NewGuid();
    _db.HabitExecutions.Add(new HabitExecutionData
    {
      Id = execId, HabitVersionId = versionId, Date = new DateOnly(2026, 3, 2).AddDays(_day++),
      Status = HabitExecutionStatusData.Failed, PaymentProcessed = false,
    });
    await _db.SaveChangesAsync();
    return execId;
  }

  private async Task<Guid> SeedPenalty(string userId, Guid charityId, PenaltyStatus status, string? intent = null)
  {
    var execId = await SeedExecution(userId);
    var id = Guid.NewGuid();
    _db.Penalties.Add(new PenaltyData
    {
      Id = id, HabitExecutionId = execId, UserId = userId, CharityId = charityId, AmountCents = 100,
      Currency = "USD", Status = (int)status, PaymentIntentId = intent, Attempts = 0,
    });
    await _db.SaveChangesAsync();
    return id;
  }

  private static PenaltyRecord Record(Guid execId, string userId, Guid charityId) => new()
  {
    HabitExecutionId = execId,
    UserId = userId,
    CharityId = charityId,
    Amount = new Money(3.00m, Currency.FromCode("USD")),
    Status = PenaltyStatus.Pending,
    PaymentIntentId = null,
    Attempts = 0,
    LastError = null,
  };
}
