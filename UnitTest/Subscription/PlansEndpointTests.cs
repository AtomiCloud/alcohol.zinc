using App.Modules.Subscription.API.V1;
using App.StartUp.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace UnitTest.Subscription;

// The public plan catalog: what the pricing page renders. Shape and the
// unlimited-flag mapping are pinned so a config or mapper change that would
// break the page (or leak the int.MaxValue sentinel) fails here first.
public class PlansEndpointTests
{
  private static SubscriptionOption Options() => new()
  {
    RenewalEnabled = false,
    GracePeriodDays = 7,
    Tiers = new Dictionary<string, SubscriptionTierOption>
    {
      ["ultimate"] = new()
      {
        PriceCents = 799,
        Currency = "USD",
        HabitsMax = int.MaxValue,
        SkipsMonthly = 60,
        VacationWindowsYearly = 12,
        FreezeBase = 30
      },
      ["free"] = new()
      {
        PriceCents = 0,
        Currency = "USD",
        HabitsMax = 2,
        SkipsMonthly = 10,
        VacationWindowsYearly = 0,
        FreezeBase = 0
      },
      ["pro"] = new()
      {
        PriceCents = 499,
        Currency = "USD",
        HabitsMax = 10,
        SkipsMonthly = 20,
        VacationWindowsYearly = 6,
        FreezeBase = 14
      }
    }
  };

  // Product decision (2026-11-13): free = 2 habits, 3 skips a month, no vacation
  // mode, no freezes. Reads the shipped App/Config/settings.yaml, so a config
  // edit that changes what free users get fails here.
  [Fact]
  public void ShippedConfig_FreeTier_IsTwoHabitsThreeSkipsNoVacationNoFreezes()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "App", "Config", "settings.yaml")))
      dir = dir.Parent;
    dir.Should().NotBeNull("the test must find the repository's App/Config/settings.yaml");

    var shipped = new SubscriptionOption();
    new ConfigurationBuilder()
      .AddYamlFile(Path.Combine(dir!.FullName, "App", "Config", "settings.yaml"), optional: false)
      .Build()
      .GetSection("Subscription")
      .Bind(shipped);

    var free = shipped.ToPlansRes().Single(p => p.Key == "free");
    free.PriceCents.Should().Be(0);
    free.HabitsMax.Should().Be(2);
    free.SkipsMonthly.Should().Be(3);
    free.VacationWindowsYearly.Should().Be(0);
    free.FreezeBase.Should().Be(0);
  }

  [Fact]
  public void Plans_OrderedCheapestFirst()
  {
    var plans = Options().ToPlansRes();
    plans.Select(p => p.Key).Should().ContainInOrder("free", "pro", "ultimate");
  }

  [Fact]
  public void Plans_MapAllCapsAndPrices()
  {
    var plans = Options().ToPlansRes();
    var pro = plans.Single(p => p.Key == "pro");
    pro.PriceCents.Should().Be(499);
    pro.Currency.Should().Be("USD");
    pro.HabitsMax.Should().Be(10);
    pro.HabitsUnlimited.Should().BeFalse();
    pro.SkipsMonthly.Should().Be(20);
    pro.VacationWindowsYearly.Should().Be(6);
    pro.FreezeBase.Should().Be(14);
  }

  [Fact]
  public void Plans_UnlimitedHabits_ExposedAsFlagNotSentinel()
  {
    var ultimate = Options().ToPlansRes().Single(p => p.Key == "ultimate");
    ultimate.HabitsUnlimited.Should().BeTrue();
    ultimate.HabitsMax.Should().BeNull("no client should ever render int.MaxValue");
  }

  [Fact]
  public void Plans_Endpoint_IsAnonymous()
  {
    // The pricing page is public; this endpoint must never require auth.
    var method = typeof(SubscriptionController).GetMethod(nameof(SubscriptionController.Plans))!;
    method.GetCustomAttributes(typeof(AllowAnonymousAttribute), false)
      .Should().NotBeEmpty("the plan catalog powers the public pricing page");
  }
}
