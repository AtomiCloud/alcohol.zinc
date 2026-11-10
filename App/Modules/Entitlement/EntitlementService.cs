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
using Domain.Vacation;

namespace App.Modules.Entitlement;

public class EntitlementService(
  ISubscriptionService subscription,
  IVacationRepository vacationRepository,
  IHabitRepository habitRepository,
  IFreezePolicy freezePolicy,
  ISubscriptionPlanProvider plans
) : IEntitlementService
{
  public async Task<Result<Unit>> EnsureVacationWindowAllowed(string userId, DateOnly startDate)
  {
    var tierLimitRes = await subscription.GetUserTier(userId)
      .ThenAwait(tier =>
        subscription.GetLimitForTier(tier, EntitlementKeys.VacationWindowsYearly)
          .Then(limit => (tier, limit), Errors.MapNone)
      );

    var countRes = await vacationRepository.CountWindowsForYear(userId, startDate.Year);

    var r = from tierLimit in tierLimitRes
            from count in countRes
            select (count, tierLimit);
    return r.Then<Unit>(x =>
    {
      var (count, (tier, limit)) = x;
      if (count >= limit)
        return new TierInsufficient(tier, EntitlementKeys.VacationWindowsYearly, limit)
          .ToException();
      return new Unit();
    });
  }

  public async Task<Result<Unit>> EnsureSkipsAllowed(string userId, DateOnly monthStart, DateOnly monthEnd)
  {
    var tierLimitRes = await subscription.GetUserTier(userId)
      .ThenAwait(tier =>
        subscription.GetLimitForTier(tier, EntitlementKeys.SkipsMonthly)
          .Then(limit => (tier, limit), Errors.MapNone));

    var usedRes = await habitRepository.CountUserSkipsForMonth(userId, monthStart, monthEnd);

    var r = from tierLimit in tierLimitRes
            from used in usedRes
            select (tierLimit, used);
    return r
      .Then<Unit>(x =>
      {
        var ((tier, limit), used) = x;
        if (used >= limit)
          return new DomainProblemException(new TierInsufficient(tier, EntitlementKeys.SkipsMonthly, limit));
        return new Unit();
      });
  }

  public async Task<Result<int>> GetFreezeCapForUser(string userId, int userMaxStreak)
  {
    // Base cap from subscription tier key, then apply policy with max streak
    var baseCapRes = await subscription.GetUserTier(userId)
      .ThenAwait(tier => subscription.GetLimitForTier(tier, EntitlementKeys.FreezeBase));

    return baseCapRes.Then(baseCap => freezePolicy.ComputeFreezeMax(baseCap, userMaxStreak), Errors.MapNone);
  }

  public async Task<Result<Unit>> EnsureHabitsAllowed(string userId)
  {
    var tierLimitRes = await subscription.GetUserTier(userId)
      .ThenAwait(tier => subscription.GetLimitForTier(tier, EntitlementKeys.HabitsMax)
        .Then(limit => (tier, limit), Errors.MapNone));

    var countRes = await habitRepository.CountHabitsForUser(userId);

    var r = from tierLimit in tierLimitRes
            from count in countRes
            select (tierLimit, count);

    return r.Then<Unit>(x =>
    {
      var ((tier, limit), count) = x;
      if (count >= limit)
        return new TierInsufficient(tier, EntitlementKeys.HabitsMax, limit).ToException();
      return new Unit();
    });
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
