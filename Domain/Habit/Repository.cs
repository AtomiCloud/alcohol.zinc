using CSharp_Result;

namespace Domain.Habit
{
    public interface IHabitRepository
    {
        // Habit Methods (Main Entity + Version Management)
        Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersions(string userId, DateOnly date);
        Task<Result<List<HabitVersionPrincipal>>> GetAllUserHabits(string userId);
        Task<Result<HabitPrincipal?>> GetHabit(Guid habitId);
        Task<Result<HabitVersionPrincipal?>> GetCurrentVersion(string userId, Guid habitId);
        Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord);  // Creates habit + first version
        Task<Result<HabitVersionPrincipal?>> Update(Guid habitId, HabitVersionRecord versionRecord, bool enabled);  // Creates new version + updates enabled status
        Task<Result<Unit?>> Delete(Guid habitId, string userId);                              // Soft delete habit
        Task<Result<int>> CreateFailedExecutions(List<string> userIds, DateOnly date);        // Batch create failed executions

        // Habit Execution Methods
        Task<Result<DateOnly>> GetUserCurrentDate(string userId);                    // Get current date in user's timezone
        Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitId, DateOnly date, string? notes);
        Task<Result<List<HabitExecutionPrincipal>>> GetDailyExecutions(string userId, DateOnly date);
    }
}
