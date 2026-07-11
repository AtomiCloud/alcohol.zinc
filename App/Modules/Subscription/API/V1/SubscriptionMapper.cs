using App.StartUp.Options;
using Domain.Subscription;

namespace App.Modules.Subscription.API.V1;

public static class SubscriptionMapper
{
  // Public plan catalog, cheapest first. The int.MaxValue sentinel is exposed
  // as an unlimited flag so no client ever renders 2147483647.
  public static List<PlanRes> ToPlansRes(this SubscriptionOption opt)
  {
    return opt.Tiers
      .OrderBy(t => t.Value.PriceCents)
      .Select(t => new PlanRes(
        t.Key,
        t.Value.PriceCents,
        t.Value.Currency,
        t.Value.HabitsMax == int.MaxValue ? null : t.Value.HabitsMax,
        t.Value.HabitsMax == int.MaxValue,
        t.Value.SkipsMonthly,
        t.Value.VacationWindowsYearly,
        t.Value.FreezeBase))
      .ToList();
  }

  // No subscription row (or one that grants nothing) presents as the free tier.
  public static SubscriptionRes ToFreeRes(string userId)
  {
    return new SubscriptionRes(
      userId,
      SubscriptionService.FreeTier,
      "none",
      null,
      null,
      false,
      null
    );
  }

  public static SubscriptionRes ToRes(this UserSubscriptionPrincipal p)
  {
    var rec = p.Record;
    if (rec.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.PendingActivation)
      return ToFreeRes(rec.UserId);

    return new SubscriptionRes(
      rec.UserId,
      rec.Tier,
      rec.Status.ToString().ToLowerInvariant(),
      rec.PeriodStart.ToString("O"),
      rec.PeriodEnd.ToString("O"),
      rec.CancelAtPeriodEnd,
      rec.NextTier
    );
  }
}
