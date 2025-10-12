using CSharp_Result;

namespace Domain.Habit;

public interface IHabitService
{
  Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch);
  Task<Result<HabitVersionPrincipal?>> GetCurrentHabitVersion(string userId, Guid habitId);
  Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord);
  Task<Result<HabitVersionPrincipal?>> Update(string userId, Guid habitId, HabitVersionRecord versionRecord, bool enabled);
  Task<Result<Unit?>> Delete(Guid habitId, string userId);
  Task<Result<int>> MarkDailyFailures(List<Guid> habitIds, DateOnly date);
  Task<Result<int>> MarkDailyFailuresForTimezonesNearMidnight(DateTime? nowUtc = null);
  Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitId, string? notes);
  Task<Result<HabitExecutionPrincipal>> SkipHabit(string userId, Guid habitVersionId, string? notes);
  Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, HabitExecutionSearch habitExecutionSearch);
}
