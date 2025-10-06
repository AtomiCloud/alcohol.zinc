using CSharp_Result;

namespace Domain.Habit
{
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
        Task<Result<int>> CreateFailedExecutions(List<Guid> habitIds, DateOnly date);        // Batch create failed executions

        // Habit Execution Methods
        Task<Result<DateOnly>> GetUserCurrentDate(string userId, Guid habitVersionId);                    // Get current date in user's timezone
        Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitVersionId, DateOnly date, string? notes);
        Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, 
          HabitExecutionSearch habitExecutionSearch);

        // Additional helpers for overview
        Task<Result<List<HabitVersionPrincipal>>> GetVersions(string userId, Guid habitId);
    }
}
