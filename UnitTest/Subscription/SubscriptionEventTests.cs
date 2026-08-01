using Domain.Notification;
using Domain.Subscription;
using Microsoft.Extensions.Logging.Abstractions;
using UnitTest.Notification;

namespace UnitTest.Subscription;

// The append-only lifecycle history: every state/money transition writes exactly
// the expected event, and a failed append never fails the underlying flow.
public class SubscriptionEventTests
{
  private static readonly DateTime Now = new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

  private static SubscriptionManagementService Svc(
    FakeSubscriptionRepository repo,
    FakeSubscriptionPaymentService payment,
    FakeSubscriptionEventRepository events,
    IEmailNotifier? notifier = null)
    => new(repo, FakePlanProvider.Default(), payment,
      notifier ?? new RecordingEmailNotifier(),
      new Protection.FakeEntitlementService(),
      events,
      NullLogger<SubscriptionManagementService>.Instance);

  private static UserSubscriptionPrincipal Row(
    string userId, string tier, SubscriptionStatus status,
    DateTime? periodEnd = null, bool cancelAtPeriodEnd = false, string? nextTier = null)
    => new()
    {
      Id = Guid.NewGuid(),
      Record = new UserSubscriptionRecord
      {
        UserId = userId,
        Tier = tier,
        Status = status,
        PeriodStart = Now.AddMonths(-1),
        PeriodEnd = periodEnd ?? Now.AddDays(15),
        CancelAtPeriodEnd = cancelAtPeriodEnd,
        NextTier = nextTier,
        LastChargeIntentId = null,
        LastChargeKey = null,
        RenewingUntil = null
      },
      CreatedAt = Now.AddMonths(-1),
      UpdatedAt = Now.AddMonths(-1)
    };

  [Fact]
  public async Task Subscribe_LogsActivatedWithAmount()
  {
    var repo = new FakeSubscriptionRepository();
    var events = new FakeSubscriptionEventRepository();

    await Svc(repo, FakeSubscriptionPaymentService.Succeeds("int_1"), events).Subscribe("u1", "pro");

    var e = events.Appended.Should().ContainSingle().Subject;
    e.EventType.Should().Be(SubscriptionEventType.Activated);
    e.Tier.Should().Be("pro");
    e.AmountCents.Should().Be(499);
    e.ChargeIntentId.Should().Be("int_1");
  }

  [Fact]
  public async Task Upgrade_LogsUpgradedWithProratedAmount()
  {
    // Interactive upgrades prorate against the REAL clock, so the period must
    // straddle actual UtcNow (unlike ProcessRenewals, which takes nowUtc).
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, periodEnd: DateTime.UtcNow.AddDays(15)));
    var events = new FakeSubscriptionEventRepository();

    await Svc(repo, FakeSubscriptionPaymentService.Succeeds("int_2"), events).Subscribe("u1", "ultimate");

    var e = events.Appended.Should().ContainSingle().Subject;
    e.EventType.Should().Be(SubscriptionEventType.Upgraded);
    e.Tier.Should().Be("ultimate");
    e.AmountCents.Should().BeGreaterThan(0).And.BeLessThan(300, "only the prorated difference is charged");
  }

  [Fact]
  public async Task SubscribeChargeFails_LogsChargeFailed()
  {
    var repo = new FakeSubscriptionRepository();
    var events = new FakeSubscriptionEventRepository();

    await Svc(repo, FakeSubscriptionPaymentService.WithStatus("int_1", "REQUIRES_PAYMENT_METHOD"), events)
      .Subscribe("u1", "pro");

    var e = events.Appended.Should().ContainSingle().Subject;
    e.EventType.Should().Be(SubscriptionEventType.ChargeFailed);
    e.Detail.Should().Be("REQUIRES_PAYMENT_METHOD");
  }

  [Fact]
  public async Task CancelThenResume_LogsBoth()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", SubscriptionStatus.Active));
    var events = new FakeSubscriptionEventRepository();
    var svc = Svc(repo, FakeSubscriptionPaymentService.Succeeds("int_x"), events);

    await svc.Cancel("u1");
    await svc.Subscribe("u1", "pro"); // same tier while pending-cancel = resume

    events.Appended.Select(e => e.EventType).Should().Equal(
      SubscriptionEventType.CancelScheduled, SubscriptionEventType.Resumed);
  }

  [Fact]
  public async Task ScheduleDowngradeThenUndo_LogsBoth()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "ultimate", SubscriptionStatus.Active));
    var events = new FakeSubscriptionEventRepository();
    var svc = Svc(repo, FakeSubscriptionPaymentService.Succeeds("int_x"), events);

    await svc.ChangeTier("u1", "pro");       // schedule (cheaper tier)
    await svc.ChangeTier("u1", "ultimate");  // undo (same active tier)

    events.Appended.Select(e => e.EventType).Should().Equal(
      SubscriptionEventType.DowngradeScheduled, SubscriptionEventType.DowngradeUndone);
    events.Appended[0].NextTier.Should().Be("pro");
  }

  [Fact]
  public async Task RenewalRoll_LogsRenewed()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", SubscriptionStatus.Active, Now.AddDays(-1)));
    var events = new FakeSubscriptionEventRepository();

    await Svc(repo, FakeSubscriptionPaymentService.Succeeds("int_r1"), events).ProcessRenewals(Now, 100);

    var e = events.Appended.Should().ContainSingle().Subject;
    e.EventType.Should().Be(SubscriptionEventType.Renewed);
    e.AmountCents.Should().Be(499);
  }

  [Fact]
  public async Task RenewalFailure_LogsChargeFailedAndGraceEntered_Once()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", SubscriptionStatus.Active, Now.AddDays(-1)));
    var events = new FakeSubscriptionEventRepository();

    await Svc(repo, FakeSubscriptionPaymentService.WithStatus("int_r1", "REQUIRES_PAYMENT_METHOD"), events)
      .ProcessRenewals(Now, 100);

    events.Appended.Select(e => e.EventType).Should().Equal(
      SubscriptionEventType.ChargeFailed, SubscriptionEventType.GraceEntered);
  }

  [Fact]
  public async Task GraceExpiry_LogsLapsed()
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", SubscriptionStatus.Grace, Now.AddDays(-10)));
    var events = new FakeSubscriptionEventRepository();

    await Svc(repo, FakeSubscriptionPaymentService.Fails(new Exception("still declined")), events)
      .ProcessRenewals(Now, 100);

    var e = events.Appended.Should().ContainSingle().Subject;
    e.EventType.Should().Be(SubscriptionEventType.Lapsed);
  }

  [Fact]
  public async Task CancelAtPeriodEndRoll_LogsCancelled()
  {
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, Now.AddDays(-1), cancelAtPeriodEnd: true));
    var events = new FakeSubscriptionEventRepository();

    await Svc(repo, FakeSubscriptionPaymentService.Succeeds("int_r1"), events).ProcessRenewals(Now, 100);

    var e = events.Appended.Should().ContainSingle().Subject;
    e.EventType.Should().Be(SubscriptionEventType.Cancelled);
  }

  [Fact]
  public async Task AppendFailure_DoesNotFailTheFlow()
  {
    var repo = new FakeSubscriptionRepository();
    var events = new FakeSubscriptionEventRepository { FailWith = new Exception("event db down") };

    var res = await Svc(repo, FakeSubscriptionPaymentService.Succeeds("int_1"), events).Subscribe("u1", "pro");

    res.IsSuccess().Should().BeTrue("history is best-effort; the money flow already committed");
    repo.Row("u1")!.Record.Status.Should().Be(SubscriptionStatus.Active);
  }
}
