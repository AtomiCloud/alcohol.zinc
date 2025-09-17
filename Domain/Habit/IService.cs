using CSharp_Result;

namespace Domain.Habit;

public interface IHabitService
{
  Task<Result<List<HabitVersionPrincipal>>> ListActiveHabits(string userId, DateOnly date);
  Task<Result<HabitVersionPrincipal?>> GetCurrentHabitVersion(string userId, Guid habitId);
  Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord);
  Task<Result<HabitVersionPrincipal?>> Update(string userId, Guid habitId, HabitVersionRecord versionRecord, bool enabled);
  Task<Result<Unit?>> Delete(Guid habitId, string userId);
  Task<Result<int>> MarkDailyFailures(List<string> userIds, DateOnly date);
  Task<Result<DateOnly>> GetUserCurrentDate(string userId);
  Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitId, string? notes);
  Task<Result<List<HabitExecutionPrincipal>>> GetDailyExecutions(string userId, DateOnly date);
}
