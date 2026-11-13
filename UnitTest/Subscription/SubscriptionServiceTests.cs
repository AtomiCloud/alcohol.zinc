using Domain.Subscription;

namespace UnitTest.Subscription;

// ISubscriptionService semantics: which statuses grant the tier, and the
// R1 pin that the free tier must never drop below the legacy stub defaults.
public class SubscriptionServiceTests
{
  private static SubscriptionService Svc(FakeSubscriptionRepository repo)
    => new(repo, FakePlanProvider.Default());

  private static UserSubscriptionPrincipal Row(
    string userId, string tier, SubscriptionStatus status,
    DateTime? periodEnd = null, bool cancelAtPeriodEnd = false)
    => new()
    {
      Id = Guid.NewGuid(),
      Record = new UserSubscriptionRecord
      {
        UserId = userId,
        Tier = tier,
        Status = status,
        PeriodStart = DateTime.UtcNow.AddDays(-15),
        PeriodEnd = periodEnd ?? DateTime.UtcNow.AddDays(15),
        CancelAtPeriodEnd = cancelAtPeriodEnd,
        NextTier = null,
        LastChargeIntentId = null
      },
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

  [Fact]
  public async Task GetUserTier_NoRow_IsFree()
  {
    var tier = await Svc(new FakeSubscriptionRepository()).GetUserTier("u1");
    tier.IsSuccess().Should().BeTrue();
    ((string)tier).Should().Be("free");
  }

  [Theory]
  [InlineData(SubscriptionStatus.Active, "pro")]
  [InlineData(SubscriptionStatus.Grace, "pro")]
  public async Task GetUserTier_ActiveOrGrace_GrantsTier(SubscriptionStatus status, string expected)
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", status));
    var tier = await Svc(repo).GetUserTier("u1");
    ((string)tier).Should().Be(expected);
  }

  [Theory]
  [InlineData(SubscriptionStatus.PendingActivation)]
  [InlineData(SubscriptionStatus.Cancelled)]
  public async Task GetUserTier_NonGranting_IsFree(SubscriptionStatus status)
  {
    var repo = new FakeSubscriptionRepository(Row("u1", "pro", status));
    var tier = await Svc(repo).GetUserTier("u1");
    ((string)tier).Should().Be("free");
  }

  // Time backstop: even with the renewal worker disabled or down, entitlements
  // stop when the paid-for time (plus grace) is over.
  [Fact]
  public async Task GetUserTier_ActivePastGraceWindow_IsFree()
  {
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, periodEnd: DateTime.UtcNow.AddDays(-10)));
    ((string)await Svc(repo).GetUserTier("u1")).Should().Be("free",
      "10 days past PeriodEnd is beyond the 7-day grace window");
  }

  [Fact]
  public async Task GetUserTier_ActiveWithinGraceWindow_KeepsTier()
  {
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active, periodEnd: DateTime.UtcNow.AddDays(-3)));
    ((string)await Svc(repo).GetUserTier("u1")).Should().Be("pro",
      "3 days past PeriodEnd is inside the 7-day grace window the renewal worker uses");
  }

  [Fact]
  public async Task GetUserTier_CancelledAtPeriodEndElapsed_IsFree()
  {
    var repo = new FakeSubscriptionRepository(
      Row("u1", "pro", SubscriptionStatus.Active,
        periodEnd: DateTime.UtcNow.AddDays(-1), cancelAtPeriodEnd: true));
    ((string)await Svc(repo).GetUserTier("u1")).Should().Be("free",
      "a cancelled subscription must stop granting the moment its period ends");
  }

  [Fact]
  public async Task GetLimitForTier_ReadsPlanCaps()
  {
    var svc = Svc(new FakeSubscriptionRepository());
    ((int)await svc.GetLimitForTier("pro", "ent.skips.monthly")).Should().Be(20);
    ((int)await svc.GetLimitForTier("ultimate", "ent.habits.max")).Should().Be(int.MaxValue);
  }

  [Fact]
  public async Task GetLimitForTier_UnknownKey_Permissive()
  {
    var svc = Svc(new FakeSubscriptionRepository());
    ((int)await svc.GetLimitForTier("pro", "ent.brand.new.key")).Should().Be(int.MaxValue,
      "an entitlement key without config must never lock users out");
  }

  [Fact]
  public async Task GetLimitForTier_UnknownTier_Fails()
  {
    var svc = Svc(new FakeSubscriptionRepository());
    var res = await svc.GetLimitForTier("platinum", "ent.habits.max");
    res.IsSuccess().Should().BeFalse();
    res.FailureOrDefault().Should().BeOfType<InvalidSubscriptionTierException>();
  }

  [Theory]
  [InlineData(0, 0, true, null, "pro")]
  [InlineData(0, 1, true, null, "free")]
  [InlineData(0, 1, false, null, "pro")]
  [InlineData(7, 0, false, null, "pro")]
  [InlineData(7, 1, false, null, "free")]
  [InlineData(2, 0, false, "short", "pro")]
  [InlineData(2, 1, false, "short", "free")]
  [InlineData(0, 0, false, "removed", "pro")]
  [InlineData(0, 1, false, "removed", "free")]
  public void EffectiveState_FixedTimeBoundaries_UseRenewalTargetsGrace(
    int daysAfterEnd, int ticksAfterEnd, bool cancelled, string? nextTier, string expectedTier)
  {
    var end = new DateTime(2030, 5, 10, 12, 0, 0, DateTimeKind.Utc);
    var row = Row("u1", "pro", SubscriptionStatus.Active, end, cancelled);
    row = row with { Record = row.Record with { NextTier = nextTier } };
    var plans = new FakePlanProvider(new()
    {
      ["pro"] = FakePlanProvider.Plan("pro", 4.99m, graceDays: 7),
      ["short"] = FakePlanProvider.Plan("short", 7.99m, graceDays: 2)
    });

    var resolved = EffectiveSubscription.Resolve(row, plans, end.AddDays(daysAfterEnd).AddTicks(ticksAfterEnd));

    (resolved?.Record.Tier ?? "free").Should().Be(expectedTier);
  }
}
