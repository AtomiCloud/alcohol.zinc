using CSharp_Result;

namespace Domain.Habit
{
    // Stake of a failed execution, so the penalty stake binding can enqueue its penalty.
    // Carries persisted int cents + basis points so the amount is computed exactly
    // as StreakRepository does (cents * bps / 10000).
    public record FailedExecutionRow(Guid ExecutionId, string UserId, Guid? CharityId, int StakeCents, string StakeCurrency, int RatioBasisPoints);

    public interface IHabitRepository
    {
        // Habit Methods (Main Entity + Version Management)
        Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersions(string userId, DateOnly date);
        Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch);
        Task<Result<HabitPrincipal?>> GetHabit(Guid habitId);
        Task<Result<List<HabitPrincipal>>> GetHabits(List<Guid> habitIds);  // Non-deleted only
        Task<Result<HabitVersionPrincipal?>> GetCurrentVersion(string userId, Guid habitId);
        // Creates habit + first version. routineId null => a new one-habit routine is created;
        // otherwise it must be the user's own non-deleted routine (NotFoundException failure).
        Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord, Guid? routineId);
        // Creates new version + updates enabled status
        Task<Result<HabitVersionPrincipal?>> Update(Guid habitId, string userId, HabitVersionRecord versionRecord, bool enabled);
        Task<Result<Unit?>> Delete(Guid habitId, string userId);                              // Soft delete habit

        // Habit Execution Methods
        // Habit task name for an execution (penalty emails); null when the execution is gone.
        Task<Result<string?>> GetTaskNameByExecutionId(Guid executionId);
        Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, 
          HabitExecutionSearch habitExecutionSearch);

        // Additional helpers for overview
        Task<Result<List<HabitVersionPrincipal>>> GetVersions(string userId, Guid habitId);
        Task<Result<HabitVersionPrincipal?>> GetVersion(Guid habitVersionId);

        // Auxiliary for entitlements & protections
        Task<Result<int>> CountHabitsForUser(string userId);
        Task<Result<int>> CountUserSkipsForMonth(string userId, DateOnly monthStart, DateOnly monthEnd);
    }
}
