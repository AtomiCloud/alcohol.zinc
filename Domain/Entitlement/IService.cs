using CSharp_Result;

namespace Domain.Entitlement;

public interface IEntitlementService
{
  Task<Result<Unit>> EnsureVacationWindowAllowed(string userId, DateOnly startDate);
  Task<Result<Unit>> EnsureSkipsAllowed(string userId, DateOnly monthStart, DateOnly monthEnd);
  Task<Result<int>> GetFreezeCapForUser(string userId, int userMaxStreak);
  Task<Result<Unit>> EnsureHabitsAllowed(string userId);
}
