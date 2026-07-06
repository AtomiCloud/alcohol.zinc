using Domain.Payment;
using Domain.Subscription;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTest.Subscription;

// Renewal state machine: roll / grace / grace-expiry / cancel-at-period-end /
// scheduled downgrade / cross-period idempotency (R3).
public class SubscriptionRenewalTests
{
  private static readonly DateTime Now = new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

  private static SubscriptionManagementService Svc(
    FakeSubscriptionRepository repo,
    FakeSubscriptionPaymentService payment)
    => new(repo, FakePlanProvider.Default(), payment,
      NullLogger<SubscriptionManagementService>.Instance);

  private static UserSubscriptionPrincipal DueRow(
    string userId, string tier, SubscriptionStatus status, DateTime periodEnd,
    bool cancelAtPeriodEnd = false, string? nextTier = null, string? intentId = null,
    string? chargeKey = null)
    => new()
    {
      Id = Guid.NewGuid(),
      Record = new UserSubscriptionRecord
      {
        UserId = userId,
        Tier = tier,
        Status = status,
        PeriodStart = periodEnd.AddMonths(-1),
        PeriodEnd = periodEnd,
        CancelAtPeriodEnd = cancelAtPeriodEnd,
        NextTier = nextTier,
        LastChargeIntentId = intentId,
        LastChargeKey = chargeKey
      },
      CreatedAt = periodEnd.AddMonths(-1),
      UpdatedAt = periodEnd.AddMonths(-1)
    };

  [Fact]
  public async Task Renewal_DueActive_ChargesAndRollsPeriod()
  {
    var periodEnd = Now.AddDays(-1);
    var repo = new FakeSubscriptionRepository(DueRow("u1", "pro", SubscriptionStatus.Active, periodEnd));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_r1");

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    ((int)res).Should().Be(1);
    var row = repo.Row("u1")!;
    row.Record.Status.Should().Be(SubscriptionStatus.Active);
    row.Record.PeriodStart.Should().Be(periodEnd, "the new period starts where the old one ended");
    row.Record.PeriodEnd.Should().Be(periodEnd.AddMonths(1));
    row.Record.LastChargeIntentId.Should().BeNull("R3: a settled renewal must clear the intent id");
    payment.ChargeCalls.Should().ContainSingle();
    payment.ChargeCalls[0].IdempotencyKey.Should().Be($"sub-u1-pro-{periodEnd:yyyyMMdd}");
    payment.ChargeCalls[0].Purpose.Should().Be(ConsentPurpose.Subscription);
  }

  [Fact]
  public async Task Renewal_NotDue_NoCharge()
  {
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "pro", SubscriptionStatus.Active, Now.AddDays(10)));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_r1");

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    payment.ChargeCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task Renewal_ChargeFails_EntersGraceKeepingTier()
  {
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "pro", SubscriptionStatus.Active, Now.AddDays(-1)));
    var payment = FakeSubscriptionPaymentService.WithStatus("int_r1", "REQUIRES_PAYMENT_METHOD");

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    var row = repo.Row("u1")!;
    row.Record.Status.Should().Be(SubscriptionStatus.Grace);
    row.Record.Tier.Should().Be("pro", "grace retains paid entitlements during the retry window");
  }

  [Fact]
  public async Task Renewal_GraceWithStoredIntent_ReconcilesItFirst()
  {
    // "Charge succeeded but the response was lost" recovery: the stored intent
    // reconciles as SUCCEEDED and the period rolls without a second charge.
    var periodEnd = Now.AddDays(-3); // inside the 7-day grace window
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "pro", SubscriptionStatus.Grace, periodEnd,
        intentId: "int_prev", chargeKey: $"sub-u1-pro-{periodEnd:yyyyMMdd}"));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_prev");

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    payment.ChargeCalls.Should().ContainSingle("a reconciled success must not trigger a fresh charge");
    payment.ChargeCalls[0].ExistingIntentId.Should().Be("int_prev",
      "an in-flight intent from a previous attempt must be reconciled, not re-created");
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Active);
  }

  [Fact]
  public async Task Renewal_ForeignTierIntent_IsNeverReconciled()
  {
    // A crashed upgrade left an ultimate ($15) intent on an Active pro row.
    // The pro renewal must NOT confirm it — that would charge the wrong amount.
    var periodEnd = Now.AddDays(-1);
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "pro", SubscriptionStatus.Active, periodEnd,
        intentId: "int_ult", chargeKey: $"sub-u1-ultimate-{Now.AddDays(-2):yyyyMMdd}"));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_fresh");

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    payment.ChargeCalls.Should().ContainSingle();
    payment.ChargeCalls[0].ExistingIntentId.Should().BeNull(
      "an intent from another tier's charge key must be ignored, not confirmed");
    payment.ChargeCalls[0].IdempotencyKey.Should().Be($"sub-u1-pro-{periodEnd:yyyyMMdd}");
    payment.ChargeCalls[0].Amount.Amount.Should().Be(4.99m, "the renewal charges the pro price");
  }

  [Fact]
  public async Task Renewal_InconclusiveReconcile_DoesNotMintASecondIntent()
  {
    // The stored intent's status is unknown (gateway error): a fresh attempt
    // beside an intent that may later settle would charge the period twice.
    // The pass must give up and retry tomorrow.
    var periodEnd = Now.AddDays(-3);
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "pro", SubscriptionStatus.Grace, periodEnd,
        intentId: "int_limbo", chargeKey: $"sub-u1-pro-{periodEnd:yyyyMMdd}"));
    var payment = FakeSubscriptionPaymentService.Fails(new Exception("airwallex 5xx"));

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    payment.ChargeCalls.Should().ContainSingle("no fresh intent may be minted while the old one is in limbo");
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Grace);
    repo.Row("u1")!.Record.LastChargeIntentId.Should().Be("int_limbo",
      "the limbo intent must stay recorded for tomorrow's reconcile");
  }

  [Fact]
  public async Task Renewal_PendingReconcileStatus_DoesNotMintASecondIntent()
  {
    // A reconcile that returns a status which may still settle asynchronously
    // (e.g. PENDING) must also wait — only definitive declines get a fresh intent.
    var periodEnd = Now.AddDays(-3);
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "pro", SubscriptionStatus.Grace, periodEnd,
        intentId: "int_pending", chargeKey: $"sub-u1-pro-{periodEnd:yyyyMMdd}"));
    var payment = FakeSubscriptionPaymentService.WithStatus("int_pending", "PENDING");

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    payment.ChargeCalls.Should().ContainSingle();
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Grace);
  }

  [Fact]
  public async Task Renewal_ReleasesLeaseAfterProcessing()
  {
    var periodEnd = Now.AddDays(-1);
    var repo = new FakeSubscriptionRepository(DueRow("u1", "pro", SubscriptionStatus.Active, periodEnd));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_r1");

    await Svc(repo, payment).ProcessRenewals(Now, 100);

    repo.Row("u1")!.Record.RenewingUntil.Should().BeNull("the lease must be released after the pass");
    repo.Calls.Should().Contain("TryClaimRenewal");
    repo.Calls.Should().Contain("ReleaseRenewal");
  }

  [Fact]
  public async Task Renewal_GraceRetry_MintsFreshDailyIntentSoTheCardIsActuallyRetried()
  {
    // Airwallex dedupes a declined intent's confirm request_id, so re-confirming
    // it can only replay the decline. A grace-day retry must therefore mint a
    // NEW intent under a day-scoped key or the card never gets a real retry.
    var periodEnd = Now.AddDays(-3);
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "pro", SubscriptionStatus.Grace, periodEnd,
        intentId: "int_declined", chargeKey: $"sub-u1-pro-{periodEnd:yyyyMMdd}"));
    var payment = FakeSubscriptionPaymentService.WithStatus("int_new", "REQUIRES_PAYMENT_METHOD");

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    payment.ChargeCalls.Should().HaveCount(2, "reconcile the old intent, then attempt fresh");
    payment.ChargeCalls[0].ExistingIntentId.Should().Be("int_declined");
    payment.ChargeCalls[1].ExistingIntentId.Should().BeNull("the fresh attempt must mint a new intent");
    payment.ChargeCalls[1].IdempotencyKey.Should().Be($"sub-u1-pro-{periodEnd:yyyyMMdd}-r{Now:yyyyMMdd}",
      "each grace day gets its own key so the create isn't deduped to the dead intent");
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Grace, "still unpaid");
  }

  [Fact]
  public async Task Renewal_GraceExpired_CancelsToFree()
  {
    var periodEnd = Now.AddDays(-10); // past the 7-day grace window
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "pro", SubscriptionStatus.Grace, periodEnd));
    var payment = FakeSubscriptionPaymentService.Fails(new Exception("still declined"));

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    ((int)res).Should().Be(1);
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Cancelled);
    payment.ChargeCalls.Should().BeEmpty("a lapsed grace must not be charged");
  }

  [Fact]
  public async Task Renewal_CancelAtPeriodEnd_CancelsWithoutCharge()
  {
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "pro", SubscriptionStatus.Active, Now.AddDays(-1), cancelAtPeriodEnd: true));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_r1");

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Cancelled);
    payment.ChargeCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task Renewal_ScheduledDowngrade_AppliedAtRoll()
  {
    var periodEnd = Now.AddDays(-1);
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "ultimate", SubscriptionStatus.Active, periodEnd, nextTier: "pro"));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_r1");

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    var row = repo.Row("u1")!;
    row.Record.Tier.Should().Be("pro");
    row.Record.NextTier.Should().BeNull();
    payment.ChargeCalls.Should().ContainSingle();
    payment.ChargeCalls[0].Amount.Amount.Should().Be(4.99m, "the downgraded tier's price is charged");
  }

  [Fact]
  public async Task Renewal_R3_ConsecutivePeriods_SecondRenewalActuallyCharges()
  {
    var firstPeriodEnd = Now.AddDays(-1);
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "pro", SubscriptionStatus.Active, firstPeriodEnd));
    var payment1 = FakeSubscriptionPaymentService.Succeeds("int_p1");
    await Svc(repo, payment1).ProcessRenewals(Now, 100);
    repo.Row("u1")!.Record.LastChargeIntentId.Should().BeNull();

    // One month later the next renewal is due.
    var nextNow = Now.AddMonths(1).AddDays(1);
    var payment2 = FakeSubscriptionPaymentService.Succeeds("int_p2");
    var res = await Svc(repo, payment2).ProcessRenewals(nextNow, 100);

    res.IsSuccess().Should().BeTrue();
    payment2.ChargeCalls.Should().ContainSingle();
    payment2.ChargeCalls[0].ExistingIntentId.Should().BeNull(
      "R3: last period's settled intent must never be reconciled as this period's payment");
    payment2.ChargeCalls[0].IdempotencyKey.Should().Be(
      $"sub-u1-pro-{firstPeriodEnd.AddMonths(1):yyyyMMdd}",
      "each period gets a distinct idempotency key");
  }

  [Fact]
  public async Task Renewal_OneRowThrows_OthersStillProcessed()
  {
    // u1's row has an unknown tier -> plan lookup fails; u2 must still renew.
    var repo = new FakeSubscriptionRepository(
      DueRow("u1", "nonexistent", SubscriptionStatus.Active, Now.AddDays(-1)),
      DueRow("u2", "pro", SubscriptionStatus.Active, Now.AddDays(-1)));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_r1");

    var res = await Svc(repo, payment).ProcessRenewals(Now, 100);

    res.IsSuccess().Should().BeTrue();
    ((int)res).Should().Be(1);
    repo.Row("u2")!.Record.PeriodEnd.Should().BeAfter(Now, "u2 renewed despite u1's failure");
  }
}
