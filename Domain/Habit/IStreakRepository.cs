using CSharp_Result;

namespace Domain.Habit;

public interface IStreakRepository
{
  Task<Result<int>> GetCurrentStreak(Guid habitId, DateOnly today);
  Task<Result<int>> GetMaxStreak(Guid habitId);
  Task<Result<bool>> IsCompleteOn(Guid habitId, DateOnly date);
  Task<Result<HashSet<DateOnly>>> GetCompletedInRange(Guid habitId, DateOnly start, DateOnly end);
  Task<Result<bool>> HasCompletionBetweenUtc(Guid habitId, DateTime startUtc, DateTime endUtc);
  Task<Result<List<DateTime>>> GetCompletionsBetweenUtc(Guid habitId, DateTime startUtc, DateTime endUtc);
  Task<Result<List<HabitExecutionRecord>>> GetExecutionsInHabitDateRange(Guid habitId, DateOnly start, DateOnly end);
  Task<Result<List<HabitDebtItem>>> GetOpenDebtsForUser(string userId);
}
