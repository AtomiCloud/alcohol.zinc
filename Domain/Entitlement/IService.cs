using CSharp_Result;

namespace Domain.Entitlement;

public interface IEntitlementService
{
  Task<Result<Unit>> EnsureVacationWindowAllowed(string userId, DateOnly startDate);
  Task<Result<Unit>> EnsureSkipsAllowed(string userId, DateOnly monthStart, DateOnly monthEnd);
  Task<Result<int>> GetFreezeCapForUser(string userId, int userMaxStreak);
  Task<Result<Unit>> EnsureHabitsAllowed(string userId);

  // Over-cap pausing: block writes to a paused habit (read-only until the user
  // upgrades or frees a slot).
  Task<Result<Unit>> EnsureHabitNotPaused(string userId, Guid habitId);
  Task<Result<Unit>> EnsureHabitVersionNotPaused(string userId, Guid habitVersionId);
  // Re-align paused flags with the given tier's habit cap: oldest habits stay
  // active, the rest pause; upgrades thaw. Returns how many habits changed.
  Task<Result<int>> ReconcileHabitPause(string userId, string tier);
}
