using Domain.Payment;
using Domain.Subscription;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTest.Subscription;

// Subscribe / Cancel / ChangeTier state-transition matrix, including the
// Konnect-mirror-must-never-fail-a-purchase guarantee.
public class SubscriptionManagementServiceTests
{
  private static SubscriptionManagementService Svc(
    FakeSubscriptionRepository repo,
    FakeSubscriptionPaymentService payment,
    FakeKonnectGateway? konnect = null,
    FakePlanProvider? plans = null)
    => new(repo, plans ?? FakePlanProvider.Default(), payment, konnect ?? new FakeKonnectGateway(),
      NullLogger<SubscriptionManagementService>.Instance);

  private static UserSubscriptionPrincipal Row(
    string userId, string tier, SubscriptionStatus status,
    DateTime? periodEnd = null, bool cancelAtPeriodEnd = false, string? nextTier = null,
    string? intentId = null, string? chargeKey = null, DateTime? renewingUntil = null)
    => new()
    {
      Id = Guid.NewGuid(),
      Record = new UserSubscriptionRecord
      {
        UserId = userId,
        Tier = tier,
        Status = status,
        PeriodStart = DateTime.UtcNow.AddMonths(-1),
        PeriodEnd = periodEnd ?? DateTime.UtcNow.AddDays(15),
        CancelAtPeriodEnd = cancelAtPeriodEnd,
        NextTier = nextTier,
        KonnectCustomerId = null,
        KonnectSyncedAt = null,
        LastChargeIntentId = intentId,
        LastChargeKey = chargeKey,
        RenewingUntil = renewingUntil
      },
      CreatedAt = DateTime.UtcNow.AddMonths(-1),
      UpdatedAt = DateTime.UtcNow.AddMonths(-1)
    };

  [Fact]
  public async Task Subscribe_HappyPath_ActivatesAndCharges()
  {
    var repo = new FakeSubscriptionRepository();
    var payment = FakeSubscriptionPaymentService.Succeeds("int_1");
    var konnect = new FakeKonnectGateway();

    var res = await Svc(repo, payment, konnect).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeTrue();
    var row = repo.Row("u1")!;
    row.Record.Status.Should().Be(SubscriptionStatus.Active);
    row.Record.Tier.Should().Be("pro");
    row.Record.LastChargeIntentId.Should().BeNull("settled charges must not linger as reconcilable intents");

    payment.ChargeCalls.Should().HaveCount(1);
    payment.ChargeCalls[0].Amount.Amount.Should().Be(5m);
    payment.ChargeCalls[0].IdempotencyKey.Should().StartWith("sub-u1-pro-");
    payment.ChargeCalls[0].Purpose.Should().Be(ConsentPurpose.Subscription,
      "subscription fees must ride the recurring consent, never the penalty one");
    payment.HasConsentCalls.Should().ContainSingle().Which.Should().Be(ConsentPurpose.Subscription);

    konnect.UpsertSubscriptionCalls.Should().ContainSingle(x => x.Tier == "pro");
  }

  [Fact]
  public async Task Subscribe_FreeTier_Rejected()
  {
    var repo = new FakeSubscriptionRepository();
    var payment = FakeSubscriptionPaymentService.Succeeds("int_1");

    var res = await Svc(repo, payment).Subscribe("u1", "free");

    res.IsSuccess().Should().BeFalse();
    res.FailureOrDefault().Should().BeOfType<InvalidSubscriptionTierException>();
    payment.ChargeCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task Subscribe_NoConsent_RejectedWithoutCharge()
  {
    var repo = new FakeSubscriptionRepository();
    var payment = FakeSubscriptionPaymentService.NoConsent();

    var res = await Svc(repo, payment).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeFalse();
    res.FailureOrDefault().Should().BeOfType<NoPaymentConsentException>();
    payment.ChargeCalls.Should().BeEmpty();
    repo.Row("u1").Should().BeNull("no row should be parked when consent is missing");
  }

  [Fact]
  public async Task Subscribe_SameTierActive_Rejected()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", SubscriptionStatus.Active));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_1");

    var res = await Svc(repo, payment).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeFalse();
    res.FailureOrDefault().Should().BeOfType<AlreadySubscribedException>();
    payment.ChargeCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task Subscribe_SameTierWithPendingCancel_UncancelsWithoutCharge()
  {
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, cancelAtPeriodEnd: true));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_1");

    var res = await Svc(repo, payment).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeTrue();
    repo.Row("u1")!.Record.CancelAtPeriodEnd.Should().BeFalse();
    payment.ChargeCalls.Should().BeEmpty("un-cancelling must be free");
  }

  [Fact]
  public async Task Subscribe_ChargeNotSucceeded_StaysPendingActivation()
  {
    var repo = new FakeSubscriptionRepository();
    var payment = FakeSubscriptionPaymentService.WithStatus("int_1", "REQUIRES_PAYMENT_METHOD");

    var res = await Svc(repo, payment).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeFalse();
    res.FailureOrDefault().Should().BeOfType<SubscriptionChargeFailedException>();
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.PendingActivation,
      "a failed first charge must not grant the tier");
  }

  [Fact]
  public async Task Subscribe_CreateThenConfirmFails_PersistsIntentForRetry()
  {
    var repo = new FakeSubscriptionRepository();
    var payment = FakeSubscriptionPaymentService.CreatesThenFails("int_1", new Exception("confirm blip"));

    var res = await Svc(repo, payment).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeFalse();
    repo.Row("u1")!.Record.LastChargeIntentId.Should().Be("int_1",
      "the retry must reconcile this intent instead of creating a new one");
  }

  [Fact]
  public async Task Subscribe_RetryAfterConfirmFailure_PassesExistingIntent()
  {
    var repo = new FakeSubscriptionRepository();
    var failing = FakeSubscriptionPaymentService.CreatesThenFails("int_1", new Exception("confirm blip"));
    await Svc(repo, failing).Subscribe("u1", "pro");

    var succeeding = FakeSubscriptionPaymentService.Succeeds("int_1");
    var res = await Svc(repo, succeeding).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeTrue();
    succeeding.ChargeCalls.Should().ContainSingle();
    succeeding.ChargeCalls[0].ExistingIntentId.Should().Be("int_1");
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Active);
  }

  [Fact]
  public async Task Subscribe_KonnectFailure_PurchaseStillSucceeds()
  {
    var repo = new FakeSubscriptionRepository();
    var payment = FakeSubscriptionPaymentService.Succeeds("int_1");
    var konnect = new FakeKonnectGateway { FailCustomer = true };

    var res = await Svc(repo, payment, konnect).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeTrue("the Konnect mirror must never fail a purchase");
    var row = repo.Row("u1")!;
    row.Record.Status.Should().Be(SubscriptionStatus.Active);
    row.Record.KonnectSyncedAt.Should().BeNull("the stale watermark lets the worker retry the mirror");
  }

  [Fact]
  public async Task Subscribe_UpgradeWhileActive_ChargesFullAndResetsPeriod()
  {
    var oldPeriodEnd = DateTime.UtcNow.AddDays(3);
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, periodEnd: oldPeriodEnd));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_2");

    var res = await Svc(repo, payment).Subscribe("u1", "ultimate");

    res.IsSuccess().Should().BeTrue();
    var row = repo.Row("u1")!;
    row.Record.Tier.Should().Be("ultimate");
    row.Record.PeriodEnd.Should().BeAfter(oldPeriodEnd, "an upgrade resets the period from now");
    payment.ChargeCalls.Should().ContainSingle();
    payment.ChargeCalls[0].Amount.Amount.Should().Be(15m, "no proration: the full new-tier price");
    payment.ChargeCalls[0].IdempotencyKey.Should().StartWith("sub-u1-ultimate-",
      "the tier in the key prevents the old tier's settled intent from paying for the upgrade");
  }

  [Fact]
  public async Task Subscribe_LowerTierWhileActive_SchedulesDowngradeWithoutCharge()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "ultimate", SubscriptionStatus.Active));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_x");

    var res = await Svc(repo, payment).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeTrue();
    var row = repo.Row("u1")!;
    row.Record.Tier.Should().Be("ultimate", "the paid remainder must not be destroyed");
    row.Record.NextTier.Should().Be("pro");
    payment.ChargeCalls.Should().BeEmpty("a downgrade is never charged immediately");
  }

  [Fact]
  public async Task Subscribe_StaleCrossTierIntent_IsNotReconciled()
  {
    // A declined ultimate attempt ($15) left its intent behind; subscribing to
    // pro ($5) must NOT hand that intent to the gateway — reconciling it would
    // charge the wrong amount (or grant pro against the $15 payment).
    var today = DateTime.UtcNow;
    var repo = new FakeSubscriptionRepository(
      Row("u1", "ultimate", SubscriptionStatus.PendingActivation,
        intentId: "int_ult", chargeKey: $"sub-u1-ultimate-{today:yyyyMMdd}"));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_pro");

    var res = await Svc(repo, payment).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeTrue();
    payment.ChargeCalls.Should().ContainSingle();
    payment.ChargeCalls[0].ExistingIntentId.Should().BeNull(
      "an intent created under a different tier's key must never be reused");
    payment.ChargeCalls[0].IdempotencyKey.Should().Be($"sub-u1-pro-{today:yyyyMMdd}");
  }

  [Fact]
  public async Task Subscribe_UpgradeDuringRenewalLease_RejectedAsBusy()
  {
    // The renewal worker holds the charge lease: an upgrade Activate now would
    // strand the renewal charge it may be settling.
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, renewingUntil: DateTime.UtcNow.AddMinutes(3)));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_x");

    var res = await Svc(repo, payment).Subscribe("u1", "ultimate");

    res.IsSuccess().Should().BeFalse();
    res.FailureOrDefault().Should().BeOfType<SubscriptionBusyException>();
    payment.ChargeCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task Subscribe_ExpiredRenewalLease_ProceedsNormally()
  {
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, renewingUntil: DateTime.UtcNow.AddMinutes(-1)));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_x");

    var res = await Svc(repo, payment).Subscribe("u1", "ultimate");

    res.IsSuccess().Should().BeTrue("a stale lease from a crashed pass must not block upgrades forever");
  }

  [Fact]
  public async Task Subscribe_UpgradeChargeFails_KeepsCurrentTierActive()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", SubscriptionStatus.Active));
    var payment = FakeSubscriptionPaymentService.WithStatus("int_2", "REQUIRES_PAYMENT_METHOD");

    var res = await Svc(repo, payment).Subscribe("u1", "ultimate");

    res.IsSuccess().Should().BeFalse();
    var row = repo.Row("u1")!;
    row.Record.Status.Should().Be(SubscriptionStatus.Active, "a failed upgrade must not downgrade the user");
    row.Record.Tier.Should().Be("pro");
  }

  [Fact]
  public async Task Cancel_ActiveSubscription_SetsCancelAtPeriodEnd()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", SubscriptionStatus.Active));
    var payment = FakeSubscriptionPaymentService.Succeeds("unused");

    var res = await Svc(repo, payment).Cancel("u1");

    res.IsSuccess().Should().BeTrue();
    var row = repo.Row("u1")!;
    row.Record.CancelAtPeriodEnd.Should().BeTrue();
    row.Record.Status.Should().Be(SubscriptionStatus.Active, "the tier is retained until the period rolls");
  }

  [Fact]
  public async Task Cancel_NoSubscription_Rejected()
  {
    var repo = new FakeSubscriptionRepository();
    var payment = FakeSubscriptionPaymentService.Succeeds("unused");

    var res = await Svc(repo, payment).Cancel("u1");

    res.IsSuccess().Should().BeFalse();
    res.FailureOrDefault().Should().BeOfType<NoActiveSubscriptionException>();
  }

  [Fact]
  public async Task ChangeTier_Downgrade_SchedulesNextTierWithoutCharge()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "ultimate", SubscriptionStatus.Active));
    var payment = FakeSubscriptionPaymentService.Succeeds("unused");

    var res = await Svc(repo, payment).ChangeTier("u1", "pro");

    res.IsSuccess().Should().BeTrue();
    var row = repo.Row("u1")!;
    row.Record.Tier.Should().Be("ultimate", "the downgrade only applies when the period rolls");
    row.Record.NextTier.Should().Be("pro");
    payment.ChargeCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task ChangeTier_Free_DelegatesToCancel()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", SubscriptionStatus.Active));
    var payment = FakeSubscriptionPaymentService.Succeeds("unused");

    var res = await Svc(repo, payment).ChangeTier("u1", "free");

    res.IsSuccess().Should().BeTrue();
    repo.Row("u1")!.Record.CancelAtPeriodEnd.Should().BeTrue();
  }

  [Fact]
  public async Task ChangeTier_NotSubscribed_DelegatesToSubscribe()
  {
    var repo = new FakeSubscriptionRepository();
    var payment = FakeSubscriptionPaymentService.Succeeds("int_1");

    var res = await Svc(repo, payment).ChangeTier("u1", "pro");

    res.IsSuccess().Should().BeTrue();
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Active);
    payment.ChargeCalls.Should().ContainSingle();
  }

  [Fact]
  public async Task ProcessKonnectMirror_RetriesUnsyncedRows()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", SubscriptionStatus.Active));
    var payment = FakeSubscriptionPaymentService.Succeeds("unused");
    var konnect = new FakeKonnectGateway();

    var res = await Svc(repo, payment, konnect).ProcessKonnectMirror(10);

    res.IsSuccess().Should().BeTrue();
    ((int)res).Should().Be(1);
    konnect.UpsertCustomerCalls.Should().ContainSingle(x => x == "u1");
    repo.Row("u1")!.Record.KonnectSyncedAt.Should().NotBeNull();
    repo.Row("u1")!.Record.KonnectCustomerId.Should().Be("kc-u1");
  }
}
