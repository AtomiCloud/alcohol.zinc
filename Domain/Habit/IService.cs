using CSharp_Result;

namespace Domain.Habit;

public interface IHabitService
{
  Task<Result<List<HabitVersionPrincipal>>> ListActiveHabits(string userId, DateOnly date);
  Task<Result<HabitVersionPrincipal?>> GetCurrentHabitVersion(string userId, Guid habitId);
  Task<Result<HabitPrincipal>> Create(string userId, HabitVersionRecord versionRecord);
  Task<Result<HabitPrincipal?>> Update(string userId, Guid habitId, HabitVersionRecord versionRecord);
  Task<Result<Unit?>> Delete(Guid habitId, string userId);
  Task<Result<int>> MarkDailyFailures(List<string> userIds, DateOnly date);
  Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitId, DateOnly date, string? notes);
  Task<Result<List<HabitExecutionPrincipal>>> GetDailyExecutions(string userId, DateOnly date);
}
