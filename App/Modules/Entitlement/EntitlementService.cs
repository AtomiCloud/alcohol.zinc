using App.Error;
using App.Error.V1;
using App.StartUp.Registry;
using App.Utility;
using CSharp_Result;
using Domain.Entitlement;
using Domain.Habit;
using Domain.Protection;
using Domain.Subscription;
using Domain.Vacation;

namespace App.Modules.Entitlement;

public class EntitlementService(
  ISubscriptionService subscription,
  IVacationRepository vacationRepository,
  IHabitRepository habitRepository,
  IFreezePolicy freezePolicy
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

  public Task<Result<Unit>> EnsureHabitNotPaused(string userId, Guid habitId)
    => this.EnsureNotPaused(userId, habitRepository.GetPausedByHabitId(userId, habitId));

  public Task<Result<Unit>> EnsureHabitVersionNotPaused(string userId, Guid habitVersionId)
    => this.EnsureNotPaused(userId, habitRepository.GetPausedByVersionId(userId, habitVersionId));

  // A missing habit (null) passes here: the downstream repository write resolves
  // it to its usual not-found error, which stays the single source of that truth.
  private async Task<Result<Unit>> EnsureNotPaused(string userId, Task<Result<bool?>> pausedLookup)
  {
    var pausedRes = await pausedLookup;
    if (!pausedRes.IsSuccess()) return pausedRes.FailureOrDefault()!;
    if (pausedRes.Get() is not true) return new Unit();

    var tierLimitRes = await subscription.GetUserTier(userId)
      .ThenAwait(tier => subscription.GetLimitForTier(tier, EntitlementKeys.HabitsMax)
        .Then(limit => (tier, limit), Errors.MapNone));

    return tierLimitRes.Then<Unit>(x =>
    {
      var (tier, limit) = x;
      return new TierInsufficient(tier, EntitlementKeys.HabitsMax, limit).ToException();
    });
  }

  public async Task<Result<int>> ReconcileHabitPause(string userId, string tier)
  {
    return await subscription.GetLimitForTier(tier, EntitlementKeys.HabitsMax)
      .ThenAwait(cap => habitRepository.SetPausedOverCap(userId, cap));
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
}
