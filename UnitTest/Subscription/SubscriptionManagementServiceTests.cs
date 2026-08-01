using Domain.Notification;
using Domain.Payment;
using Domain.Subscription;
using Microsoft.Extensions.Logging.Abstractions;
using UnitTest.Notification;

namespace UnitTest.Subscription;

// Subscribe / Cancel / ChangeTier state-transition matrix.
public class SubscriptionManagementServiceTests
{
  private static SubscriptionManagementService Svc(
    FakeSubscriptionRepository repo,
    FakeSubscriptionPaymentService payment,
    FakePlanProvider? plans = null,
    IEmailNotifier? notifier = null,
    Protection.FakeEntitlementService? entitlements = null)
    => new(repo, plans ?? FakePlanProvider.Default(), payment,
      notifier ?? new RecordingEmailNotifier(),
      entitlements ?? new Protection.FakeEntitlementService(),
      NullLogger<SubscriptionManagementService>.Instance);

  private static UserSubscriptionPrincipal Row(
    string userId, string tier, SubscriptionStatus status,
    DateTime? periodEnd = null, bool cancelAtPeriodEnd = false, string? nextTier = null,
    string? intentId = null, string? chargeKey = null, DateTime? renewingUntil = null,
    DateTime? periodStart = null)
    => new()
    {
      Id = Guid.NewGuid(),
      Record = new UserSubscriptionRecord
      {
        UserId = userId,
        Tier = tier,
        Status = status,
        PeriodStart = periodStart ?? DateTime.UtcNow.AddMonths(-1),
        PeriodEnd = periodEnd ?? DateTime.UtcNow.AddDays(15),
        CancelAtPeriodEnd = cancelAtPeriodEnd,
        NextTier = nextTier,
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

    var res = await Svc(repo, payment).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeTrue();
    var row = repo.Row("u1")!;
    row.Record.Status.Should().Be(SubscriptionStatus.Active);
    row.Record.Tier.Should().Be("pro");
    row.Record.LastChargeIntentId.Should().BeNull("settled charges must not linger as reconcilable intents");

    payment.ChargeCalls.Should().HaveCount(1);
    payment.ChargeCalls[0].Amount.Amount.Should().Be(4.99m);
    payment.ChargeCalls[0].IdempotencyKey.Should().StartWith("sub-u1-pro-");
    payment.ChargeCalls[0].Purpose.Should().Be(ConsentPurpose.Subscription,
      "subscription fees must ride the recurring consent, never the penalty one");
    payment.HasConsentCalls.Should().ContainSingle().Which.Should().Be(ConsentPurpose.Subscription);
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
  public async Task Subscribe_Upgrade_ChargesProratedDiffAndKeepsAnniversary()
  {
    // pro -> ultimate with 20 of 30 days remaining: the user already paid pro
    // for this period, so the charge is (7.99 - 4.99) * 20/30 ≈ $2.00 and the
    // billing anniversary must NOT move.
    var periodStart = DateTime.UtcNow.AddDays(-10);
    var periodEnd = DateTime.UtcNow.AddDays(20);
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, periodStart: periodStart, periodEnd: periodEnd));
    var payment = FakeSubscriptionPaymentService.Succeeds("int_2");

    var res = await Svc(repo, payment).Subscribe("u1", "ultimate");

    res.IsSuccess().Should().BeTrue();
    var row = repo.Row("u1")!;
    row.Record.Tier.Should().Be("ultimate");
    row.Record.PeriodStart.Should().Be(periodStart, "an upgrade must not move the anniversary");
    row.Record.PeriodEnd.Should().Be(periodEnd, "an upgrade must not move the anniversary");

    payment.ChargeCalls.Should().ContainSingle();
    var expected = Math.Floor((7.99m - 4.99m) * 20m / 30m * 100m) / 100m; // rounded down
    payment.ChargeCalls[0].Amount.Amount.Should().BeApproximately(expected, 0.01m,
      "only the prorated price difference is charged (the seconds between test and service clocks shift it by far less than a cent)");
    payment.ChargeCalls[0].Amount.Amount.Should().BeLessThan(7.99m - 4.99m);
    payment.ChargeCalls[0].IdempotencyKey.Should().StartWith("sub-u1-ultimate-upg-",
      "the -upg- namespace keeps upgrade charges from colliding with subscribe/renewal intents");
  }

  [Fact]
  public async Task Subscribe_UpgradeMomentsBeforeRenewal_FlipsFreeOfCharge()
  {
    // Remaining fraction prorates to $0: the tier flips without a charge — the
    // imminent renewal bills the full new price anyway.
    var periodStart = DateTime.UtcNow.AddDays(-30);
    var periodEnd = DateTime.UtcNow.AddSeconds(5);
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, periodStart: periodStart, periodEnd: periodEnd));
    var payment = FakeSubscriptionPaymentService.Succeeds("unused");

    var res = await Svc(repo, payment).Subscribe("u1", "ultimate");

    res.IsSuccess().Should().BeTrue();
    repo.Row("u1")!.Record.Tier.Should().Be("ultimate");
    repo.Row("u1")!.Record.PeriodEnd.Should().Be(periodEnd);
    payment.ChargeCalls.Should().BeEmpty("a $0 proration must not attempt a charge");
  }

  [Fact]
  public async Task Subscribe_UpgradeRetryAfterConfirmFailure_ReconcilesSameIntent()
  {
    var periodStart = DateTime.UtcNow.AddDays(-10);
    var periodEnd = DateTime.UtcNow.AddDays(20);
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, periodStart: periodStart, periodEnd: periodEnd));
    var failing = FakeSubscriptionPaymentService.CreatesThenFails("int_u1", new Exception("confirm blip"));
    await Svc(repo, failing).Subscribe("u1", "ultimate");
    repo.Row("u1")!.Record.Tier.Should().Be("pro", "a failed upgrade must not change the tier");

    var succeeding = FakeSubscriptionPaymentService.Succeeds("int_u1");
    var res = await Svc(repo, succeeding).Subscribe("u1", "ultimate");

    res.IsSuccess().Should().BeTrue();
    succeeding.ChargeCalls.Should().ContainSingle();
    succeeding.ChargeCalls[0].ExistingIntentId.Should().Be("int_u1",
      "a same-day upgrade retry must reconcile the recorded intent, not mint a new one");
    repo.Row("u1")!.Record.Tier.Should().Be("ultimate");
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
  public async Task EmailMatrix_SubscribeEmailsReceipt_CancelAndDowngradeEmailConfirmation()
  {
    // First subscribe -> one purchase receipt with the charged amount.
    var notifier = new RecordingEmailNotifier();
    var repo = new FakeSubscriptionRepository();
    await Svc(repo, FakeSubscriptionPaymentService.Succeeds("int_1"), notifier: notifier).Subscribe("u1", "pro");
    notifier.SubscriptionPurchased.Should().ContainSingle();
    notifier.SubscriptionPurchased[0].UserId.Should().Be("u1");
    notifier.SubscriptionPurchased[0].Tier.Should().Be("pro");
    notifier.SubscriptionPurchased[0].Amount.Amount.Should().Be(4.99m);

    // Cancel -> one change confirmation, effective at period end, target free.
    notifier = new RecordingEmailNotifier();
    var row = Row("u2", "pro", SubscriptionStatus.Active);
    repo = new FakeSubscriptionRepository(row);
    await Svc(repo, FakeSubscriptionPaymentService.Succeeds("unused"), notifier: notifier).Cancel("u2");
    notifier.SubscriptionChanged.Should().ContainSingle()
      .Which.Should().Be(("u2", "pro", SubscriptionService.FreeTier, row.Record.PeriodEnd));

    // Scheduled downgrade -> one change confirmation ultimate -> pro.
    notifier = new RecordingEmailNotifier();
    row = Row("u3", "ultimate", SubscriptionStatus.Active);
    repo = new FakeSubscriptionRepository(row);
    await Svc(repo, FakeSubscriptionPaymentService.Succeeds("unused"), notifier: notifier).ChangeTier("u3", "pro");
    notifier.SubscriptionChanged.Should().ContainSingle()
      .Which.Should().Be(("u3", "ultimate", "pro", row.Record.PeriodEnd));
    notifier.SubscriptionPurchased.Should().BeEmpty("a scheduled downgrade charges nothing");

    // Failed subscribe (charge declined) -> no receipt.
    notifier = new RecordingEmailNotifier();
    repo = new FakeSubscriptionRepository();
    await Svc(repo, FakeSubscriptionPaymentService.WithStatus("int_2", "REQUIRES_PAYMENT_METHOD"), notifier: notifier)
      .Subscribe("u4", "pro");
    notifier.SubscriptionPurchased.Should().BeEmpty();
  }

  [Fact]
  public async Task ThrowingNotifier_MoneyStatePersistsBeforeTheEmailAttempt()
  {
    // Interactive call sites rely on the notifier's never-throw contract (the
    // real EmailNotifier swallows everything). If that contract were violated,
    // the activation must already be durable — the email always runs after the
    // money write.
    var repo = new FakeSubscriptionRepository();
    var act = async () => await Svc(
        repo, FakeSubscriptionPaymentService.Succeeds("int_1"), notifier: new ThrowingEmailNotifier())
      .Subscribe("u1", "pro");

    await act.Should().ThrowAsync<InvalidOperationException>();
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Active);
  }

  // ---------------------------------------------------------------------------
  // OVER-CAP HABIT PAUSING — interactive tier changes reconcile immediately;
  // failed or rejected changes must not touch the user's habits.
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task Subscribe_Success_ReconcilesHabitPauseToNewTier()
  {
    var entitlements = new Protection.FakeEntitlementService();
    var repo = new FakeSubscriptionRepository();

    var res = await Svc(repo, FakeSubscriptionPaymentService.Succeeds("int_1"), entitlements: entitlements)
      .Subscribe("u1", "pro");

    res.IsSuccess().Should().BeTrue();
    entitlements.ReconcileHabitPauseCalls.Should().ContainSingle()
      .Which.Should().Be(("u1", "pro"), "an upgrade may thaw previously paused habits");
  }

  [Fact]
  public async Task Subscribe_ChargeFails_DoesNotReconcileHabitPause()
  {
    var entitlements = new Protection.FakeEntitlementService();
    var repo = new FakeSubscriptionRepository();

    var res = await Svc(repo, FakeSubscriptionPaymentService.WithStatus("int_1", "REQUIRES_PAYMENT_METHOD"),
        entitlements: entitlements)
      .Subscribe("u1", "pro");

    res.IsFailure().Should().BeTrue();
    entitlements.ReconcileHabitPauseCalls.Should().BeEmpty("no tier changed, so no habit may move");
  }

  [Fact]
  public async Task ReconcileFailure_DoesNotFailTheSubscribe()
  {
    // Pausing is best-effort by contract: the charge has settled, so a pause
    // hiccup must never surface as a failed subscribe.
    var entitlements = new Protection.FakeEntitlementService
    {
      ReconcileHabitPauseResult = new Exception("transient db error"),
    };
    var repo = new FakeSubscriptionRepository();

    var res = await Svc(repo, FakeSubscriptionPaymentService.Succeeds("int_1"), entitlements: entitlements)
      .Subscribe("u1", "pro");

    res.IsSuccess().Should().BeTrue();
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Active);
  }
}
