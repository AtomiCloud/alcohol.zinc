using CSharp_Result;

namespace Domain.Habit;

public interface IStreakRepository
{
  Task<Result<HashSet<DateOnly>>> GetCompletedInRange(Guid habitId, DateOnly start, DateOnly end);
  // Every execution of each habit (all versions), oldest first; habits without any are absent.
  Task<Result<Dictionary<Guid, List<HabitExecutionRecord>>>> GetExecutionsForHabits(IReadOnlyCollection<Guid> habitIds);
  Task<Result<List<HabitDebtItem>>> GetOpenDebtsForHabit(Guid habitId);
  Task<Result<List<HabitDebtItem>>> GetOpenDebtsForUser(string userId);
  Task<Result<int>> GetUserMaxStreakAcrossHabits(string userId);
}
