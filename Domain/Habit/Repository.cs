using CSharp_Result;

namespace Domain.Habit
{
    // Row returned by CreateFailedExecutions so the service can enqueue penalties.
    // Carries persisted int cents + basis points so the amount is computed exactly
    // as StreakRepository does (cents * bps / 10000).
    public record FailedExecutionRow(Guid ExecutionId, string UserId, Guid CharityId, int StakeCents, string StakeCurrency, int RatioBasisPoints);

    public interface IHabitRepository
    {
        // Habit Methods (Main Entity + Version Management)
        Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersions(string userId, DateOnly date);
        Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch);
        Task<Result<HabitPrincipal?>> GetHabit(Guid habitId);
        Task<Result<HabitVersionPrincipal?>> GetCurrentVersion(string userId, Guid habitId);
        // Creates habit + first version
        Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord);
        // Creates new version + updates enabled status
        Task<Result<HabitVersionPrincipal?>> Update(Guid habitId, string userId, HabitVersionRecord versionRecord, bool enabled);
        Task<Result<Unit?>> Delete(Guid habitId, string userId);                              // Soft delete habit
        Task<Result<List<FailedExecutionRow>>> CreateFailedExecutions(List<Guid> habitIds, DateOnly date);        // Batch create failed executions; returns the rows just failed
        Task<Result<int>> CreateExecutionsForVersionsWithStatus(List<Guid> habitVersionIds, DateOnly date, ExecutionStatus status);

        // Habit Execution Methods
        // Habit task name for an execution (penalty emails); null when the execution is gone.
        Task<Result<string?>> GetTaskNameByExecutionId(Guid executionId);
        Task<Result<DateOnly>> GetUserCurrentDate(string userId, Guid habitVersionId);                    // Get current date in user's timezone
        Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitVersionId, DateOnly date, string? notes);
        Task<Result<HabitExecutionPrincipal>> SkipHabit(string userId, Guid habitVersionId, DateOnly date, string? notes);
        Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, 
          HabitExecutionSearch habitExecutionSearch);

        // Additional helpers for overview
        Task<Result<List<HabitVersionPrincipal>>> GetVersions(string userId, Guid habitId);

        // Auxiliary for entitlements & protections
        Task<Result<int>> CountHabitsForUser(string userId);
        Task<Result<int>> CountUserSkipsForMonth(string userId, DateOnly monthStart, DateOnly monthEnd);
        Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersionsByIds(List<Guid> habitIds, DateOnly date);
        Task<Result<List<HabitPrincipal>>> GetHabitsByIds(List<Guid> habitIds);

        // Helpers for protections
        Task<Result<bool>> HasAnyCompletedOrSkippedForVersions(List<Guid> habitVersionIds, DateOnly date);

        // Helpers for end-of-day failure marking
        Task<Result<List<string>>> GetDistinctTimezonesForEnabledHabits();
        Task<Result<List<Guid>>> GetEnabledHabitIdsByTimezone(string timezone);

        // Over-cap pausing (tier downgrades). Reconciles the whole user in one
        // statement: the oldest `cap` habits become unpaused, the rest paused.
        // Idempotent — safe to call on every tier change in either direction.
        // Returns the number of habits whose paused flag actually changed.
        Task<Result<int>> SetPausedOverCap(string userId, int cap);
        // Paused flag lookups; null = habit not found / not owned by user.
        Task<Result<bool?>> GetPausedByHabitId(string userId, Guid habitId);
        Task<Result<bool?>> GetPausedByVersionId(string userId, Guid habitVersionId);
    }
}
