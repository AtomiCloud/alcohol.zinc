using App.Error;
using App.Error.V1;
using App.StartUp.Registry;
using CSharp_Result;
using Domain.Entitlement;
using Domain.Habit;
using Domain.Subscription;
using Domain.Vacation;

namespace App.Modules.Entitlement;

public class EntitlementService(
  ISubscriptionService subscription,
  IVacationRepository vacationRepository,
  IHabitRepository habitRepository
) : IEntitlementService
{
  public Task<Result<Unit>> EnsureVacationWindowAllowed(string userId, DateOnly startDate)
  {
    return EnsureVacationWindowAllowedImpl(userId, startDate);
  }

  private async Task<Result<Unit>> EnsureVacationWindowAllowedImpl(string userId, DateOnly startDate)
  {
    var tierRes = await subscription.GetUserTier(userId);
    if (tierRes.IsFailure()) return tierRes.FailureOrDefault()!;
    var tier = tierRes.Get();

    var limitRes = await subscription.GetLimitForTier(tier, EntitlementKeys.VacationWindowsYearly);
    if (limitRes.IsFailure()) return limitRes.FailureOrDefault()!;
    var limit = limitRes.Get();

    var countRes = await vacationRepository.CountWindowsForYear(userId, startDate.Year);
    if (countRes.IsFailure()) return countRes.FailureOrDefault()!;
    var count = countRes.Get();

    if (count >= limit)
      return new DomainProblemException(new TierInsufficient(tier, EntitlementKeys.VacationWindowsYearly, limit));
    return new Unit();
  }

  public Task<Result<Unit>> EnsureSkipsAllowed(string userId, DateOnly monthStart, DateOnly monthEnd)
  {
    return EnsureSkipsAllowedImpl(userId, monthStart, monthEnd);
  }

  private async Task<Result<Unit>> EnsureSkipsAllowedImpl(string userId, DateOnly monthStart, DateOnly monthEnd)
  {
    var tierRes = await subscription.GetUserTier(userId);
    if (tierRes.IsFailure()) return tierRes.FailureOrDefault()!;
    var tier = tierRes.Get();

    var limitRes = await subscription.GetLimitForTier(tier, EntitlementKeys.SkipsMonthly);
    if (limitRes.IsFailure()) return limitRes.FailureOrDefault()!;
    var limit = limitRes.Get();

    var usedRes = await habitRepository.CountUserSkipsForMonth(userId, monthStart, monthEnd);
    if (usedRes.IsFailure()) return usedRes.FailureOrDefault()!;
    var used = usedRes.Get();

    if (used >= limit)
      return new DomainProblemException(new TierInsufficient(tier, EntitlementKeys.SkipsMonthly, limit));
    return new Unit();
  }
}
