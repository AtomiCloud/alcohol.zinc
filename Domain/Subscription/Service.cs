using CSharp_Result;

namespace Domain.Subscription;

// Real ISubscriptionService: tier from the local subscription table, limits from
// the local plan catalog (config). Konnect is a mirror and is never consulted on
// this hot path. Consumers (EntitlementService, HabitOverviewService) are
// unchanged.
public class SubscriptionService(
  ISubscriptionRepository repo,
  ISubscriptionPlanProvider plans
) : ISubscriptionService
{
  public const string FreeTier = "free";

  public Task<Result<string>> GetUserTier(string userId)
  {
    return repo.GetByUserId(userId)
      .Then(sub => this.EffectiveTier(sub, DateTime.UtcNow), Errors.MapNone);
  }

  // Grace keeps paid entitlements (the user is inside the retry window);
  // PendingActivation grants nothing until its charge succeeds. Time acts as a
  // backstop for the renewal worker: even if the worker is disabled or down, an
  // expired period (past its grace window) or an elapsed cancel stops granting
  // the paid tier — users must never keep entitlements they stopped paying for.
  private string EffectiveTier(UserSubscriptionPrincipal? sub, DateTime nowUtc)
  {
    if (sub?.Record.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Grace))
      return FreeTier;

    var rec = sub.Record;
    if (rec.CancelAtPeriodEnd && nowUtc > rec.PeriodEnd)
      return FreeTier;

    if (nowUtc > rec.PeriodEnd)
    {
      // Past the period: only the grace window still grants the tier. An
      // unknown tier has no plan, hence no grace to stand on.
      var planRes = plans.GetPlan(rec.Tier);
      if (!planRes.IsSuccess()) return FreeTier;
      if (nowUtc > rec.PeriodEnd.AddDays(planRes.Get().GracePeriodDays))
        return FreeTier;
    }

    return rec.Tier;
  }

  public Task<Result<int>> GetLimitForTier(string tier, string key)
  {
    // Unknown keys stay permissive, matching the previous stub's behaviour, so
    // adding a new entitlement key can never lock users out before config ships.
    return Task.FromResult(
      plans.GetPlan(tier)
        .Then(p => p.Caps.TryGetValue(key, out var cap) ? cap : int.MaxValue, Errors.MapNone));
  }
}
