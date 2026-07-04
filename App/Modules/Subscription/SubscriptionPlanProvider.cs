using App.StartUp.Options;
using App.StartUp.Registry;
using CSharp_Result;
using Domain.Subscription;
using Microsoft.Extensions.Options;
using NodaMoney;

namespace App.Modules.Subscription;

// Plan catalog over SubscriptionOption config: the enforcement source of truth.
// Konnect mirrors these definitions (docs/KONNECT_SETUP.md) but is never read here.
public class SubscriptionPlanProvider(IOptionsMonitor<SubscriptionOption> options)
  : ISubscriptionPlanProvider
{
  public Result<SubscriptionPlan> GetPlan(string tier)
  {
    var opt = options.CurrentValue;
    if (!opt.Tiers.TryGetValue(tier, out var t))
      return new InvalidSubscriptionTierException(tier);

    return new SubscriptionPlan
    {
      Tier = tier,
      Price = new Money(t.PriceCents / 100m, Currency.FromCode(t.Currency)),
      Caps = new Dictionary<string, int>
      {
        [EntitlementKeys.HabitsMax] = t.HabitsMax,
        [EntitlementKeys.SkipsMonthly] = t.SkipsMonthly,
        [EntitlementKeys.VacationWindowsYearly] = t.VacationWindowsYearly,
        [EntitlementKeys.FreezeBase] = t.FreezeBase
      },
      GracePeriodDays = opt.GracePeriodDays
    };
  }
}
