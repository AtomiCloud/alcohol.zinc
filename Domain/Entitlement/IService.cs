using CSharp_Result;
using Domain.Proof;

namespace Domain.Entitlement;

public interface IEntitlementService
{
  Task<Result<Unit>> EnsureVacationWindowAllowed(string userId, DateOnly startDate);
  Task<Result<Unit>> EnsureSkipsAllowed(string userId, DateOnly monthStart, DateOnly monthEnd);
  Task<Result<int>> GetFreezeCapForUser(string userId, int userMaxStreak);
  Task<Result<Unit>> EnsureHabitsAllowed(string userId);

  // The user's plan must be the plugin's MinTier or a pricier one.
  Task<Result<Unit>> EnsureProofPluginAllowed(string userId, PluginManifest plugin);
}
