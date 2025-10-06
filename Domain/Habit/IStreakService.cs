using CSharp_Result;

namespace Domain.Habit;

public record HabitStreakStatus
(
  int CurrentStreak,
  int MaxStreak,
  bool IsCompleteToday,
  Dictionary<DayOfWeek, bool> Week,
  DateOnly WeekStart,
  DateOnly WeekEnd,
  int TimeLeftToEodMinutes
);

public interface IStreakService
{
  Task<Result<HabitStreakStatus>> GetStatusForHabit(string userId, Guid habitId, string userTimezone, string habitTimezone, DateTime? nowUtc = null);
}
