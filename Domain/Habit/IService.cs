using CSharp_Result;

namespace Domain.Habit;

public interface IHabitService
{
  Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch);
  Task<Result<HabitVersionPrincipal?>> GetCurrentHabitVersion(string userId, Guid habitId);
  Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord, Guid? routineId);
  Task<Result<HabitVersionPrincipal?>> Update(string userId, Guid habitId, HabitVersionRecord versionRecord, bool enabled);
  Task<Result<Unit?>> Delete(Guid habitId, string userId);
  // Moves the listed habits to the top in the given order; returns the user's final order.
  Task<Result<List<Guid>>> Reorder(string userId, List<Guid> habitIds);
  Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitVersionId, string? notes);
  // month: the user's local month, which the monthly skip limit counts in.
  Task<Result<HabitExecutionPrincipal>> SkipHabit(string userId, Guid habitVersionId, string? notes,
    Allowance.UserMonthWindow month);
  Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, HabitExecutionSearch habitExecutionSearch);
}
