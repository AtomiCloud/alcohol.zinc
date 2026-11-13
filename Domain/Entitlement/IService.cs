using CSharp_Result;
using Domain.Proof;

namespace Domain.Entitlement;

public interface IEntitlementService
{
  // The counted limits; the writing repository enforces each one (see Quota).
  Task<Result<Quota>> HabitQuota(string userId);
  Task<Result<Quota>> SkipQuota(string userId);
  Task<Result<Quota>> VacationWindowQuota(string userId);

  Task<Result<int>> GetFreezeCapForUser(string userId, int userMaxStreak);

  // Re-enabling a habit doesn't change the habit count (disabled habits count), so it only checks.
  Task<Result<Unit>> EnsureHabitsAllowed(string userId);

  // The user's plan must be the plugin's MinTier or a pricier one.
  Task<Result<Unit>> EnsureProofPluginAllowed(string userId, PluginManifest plugin);
}
