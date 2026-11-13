namespace Domain.Subscription;

// The same read-only decision for entitlements, responses and interactive management.
// Null means no effective paid plan; the stored row is never rewritten by this resolver.
public static class EffectiveSubscription
{
  public static UserSubscriptionPrincipal? Resolve(
    UserSubscriptionPrincipal? sub, ISubscriptionPlanProvider plans, DateTime nowUtc)
  {
    if (sub?.Record.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Grace))
      return null;

    var rec = sub.Record;
    if (rec.Tier == SubscriptionService.FreeTier || !plans.GetPlan(rec.Tier).IsSuccess())
      return null;

    if (nowUtc > rec.PeriodEnd)
    {
      if (rec.CancelAtPeriodEnd) return null;

      // Renewal charges NextTier, so its grace window is the one that applies.
      var plan = plans.GetPlan(rec.NextTier ?? rec.Tier);
      if (!plan.IsSuccess() || nowUtc > rec.PeriodEnd.AddDays(plan.Get().GracePeriodDays))
        return null;
    }

    return sub;
  }
}
