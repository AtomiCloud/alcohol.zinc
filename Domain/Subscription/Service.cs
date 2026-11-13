using CSharp_Result;

namespace Domain.Subscription;

// Real ISubscriptionService: tier from the local subscription table, limits from
// the local plan catalog (config) — zinc is the single source of truth for
// plans and usage. Consumers (EntitlementService, HabitOverviewService) are
// unchanged.
public class SubscriptionService(
  ISubscriptionRepository repo,
  ISubscriptionPlanProvider plans
) : ISubscriptionService
{
  public const string FreeTier = "free";

  public Task<Result<string>> GetUserTier(string userId)
  {
    var now = DateTime.UtcNow;
    return repo.GetByUserId(userId)
      .Then(sub => EffectiveSubscription.Resolve(sub, plans, now)?.Record.Tier ?? FreeTier, Errors.MapNone);
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
