using App.Error;
using App.Error.V1;
using App.StartUp.Registry;
using App.Utility;
using CSharp_Result;
using Domain.Entitlement;
using Domain.Habit;
using Domain.Proof;
using Domain.Protection;
using Domain.Subscription;

namespace App.Modules.Entitlement;

public class EntitlementService(
  ISubscriptionService subscription,
  IHabitRepository habitRepository,
  IFreezePolicy freezePolicy,
  ISubscriptionPlanProvider plans
) : IEntitlementService
{
  public Task<Result<Quota>> HabitQuota(string userId) => this.Quota(userId, EntitlementKeys.HabitsMax);
  public Task<Result<Quota>> SkipQuota(string userId) => this.Quota(userId, EntitlementKeys.SkipsMonthly);

  public Task<Result<Quota>> VacationWindowQuota(string userId) =>
    this.Quota(userId, EntitlementKeys.VacationWindowsYearly);

  private Task<Result<Quota>> Quota(string userId, string key) =>
    subscription.GetUserTier(userId)
      .ThenAwait(tier => subscription.GetLimitForTier(tier, key)
        .Then(limit => new Quota(tier, key, limit), Errors.MapNone));

  public async Task<Result<int>> GetFreezeCapForUser(string userId, int userMaxStreak)
  {
    // Base cap from subscription tier key, then apply policy with max streak
    var baseCapRes = await subscription.GetUserTier(userId)
      .ThenAwait(tier => subscription.GetLimitForTier(tier, EntitlementKeys.FreezeBase));

    return baseCapRes.Then(baseCap => freezePolicy.ComputeFreezeMax(baseCap, userMaxStreak), Errors.MapNone);
  }

  public async Task<Result<Unit>> EnsureHabitsAllowed(string userId)
  {
    var quota = await this.HabitQuota(userId);
    if (quota.IsFailure()) return quota.FailureOrDefault();
    var count = await habitRepository.CountHabitsForUser(userId);
    if (count.IsFailure()) return count.FailureOrDefault();

    var (tier, key, limit) = quota.Get();
    if (count.Get() >= limit) return new TierInsufficient(tier, key, limit).ToException();
    return new Unit();
  }

  public async Task<Result<Unit>> EnsureProofPluginAllowed(string userId, PluginManifest plugin)
  {
    if (plugin.MinTier == null) return new Unit();

    var tierRes = await subscription.GetUserTier(userId);
    if (tierRes.IsFailure()) return tierRes.FailureOrDefault();
    var tier = tierRes.Get();
    var have = plans.GetPlan(tier);
    if (have.IsFailure()) return have.FailureOrDefault();
    var need = plans.GetPlan(plugin.MinTier);
    if (need.IsFailure()) return need.FailureOrDefault();

    // Tiers have no explicit order; a plan is "at least" another when it costs as much or more.
    if (have.Get().Price.Amount >= need.Get().Price.Amount) return new Unit();
    return new TierInsufficient(tier, $"proof.{plugin.Key}", 0,
      $"The {plugin.Name} proof needs the {plugin.MinTier} plan or higher").ToException();
  }
}
