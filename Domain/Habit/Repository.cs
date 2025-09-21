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
        // Creates habit + first version
        Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord);
        // Creates new version + updates enabled status
        Task<Result<HabitVersionPrincipal?>> Update(Guid habitId, string userId, HabitVersionRecord versionRecord, bool enabled);
        // Soft delete habit
        Task<Result<Unit?>> Delete(Guid habitId, string userId);

        // Habit Execution Methods
        // Get current date in user's timezone
        Task<Result<DateOnly>> GetUserCurrentDate(string userId);                    
        Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitId, DateOnly date, string? notes);
        Task<Result<List<HabitExecutionPrincipal>>> GetDailyExecutions(string userId, DateOnly date);
        // Batch create failed executions
        Task<Result<int>> CreateFailedExecutions(List<string> userIds, DateOnly date);
    }
}
