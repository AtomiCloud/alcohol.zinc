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

    // A misconfigured currency code must surface through the Result path, not
    // escape as an unhandled FormatException from Currency.FromCode.
    Currency currency;
    try
    {
      currency = Currency.FromCode(t.Currency);
    }
    catch (Exception e)
    {
      return new InvalidOperationException($"Invalid currency '{t.Currency}' for tier '{tier}'", e);
    }

    return new SubscriptionPlan
    {
      Tier = tier,
      Price = new Money(t.PriceCents / 100m, currency),
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
