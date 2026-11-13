using CSharp_Result;
using Domain.Entitlement;
using Domain.Habit;

namespace Domain.Protection;

public class FreezeCapService(
  IProtectionRepository protectionRepo,
  IStreakRepository streakRepo,
  IEntitlementService entitlement
) : IFreezeCapService
{
  public Task<Result<FreezeBalance>> Reconcile(string userId)
  {
    return protectionRepo.UpsertProtection(userId)
      .ThenAwait(p => streakRepo.GetUserMaxStreakAcrossHabits(userId)
        .ThenAwait(maxStreak => entitlement.GetFreezeCapForUser(userId, maxStreak))
        .ThenAwait(cap => p.Record.FreezeCurrent > cap
          ? protectionRepo.ClampFreezeToCap(userId, cap)
            .Then(_ => new FreezeBalance(cap, cap), Errors.MapNone)
          : Task.FromResult<Result<FreezeBalance>>(new FreezeBalance(p.Record.FreezeCurrent, cap))));
  }
}
