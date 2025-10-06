using CSharp_Result;

namespace Domain.Habit;

public enum HabitDayStatus
{
  NotApplicable,
  Succeeded,
  Failed,
  Vacation,
  Skip,
  Frozen
}

public record HabitStreakStatus
(
  int CurrentStreak,
  int MaxStreak,
  bool IsCompleteToday,
  Dictionary<DayOfWeek, HabitDayStatus> WeekStatuses,
  DateOnly WeekStart,
  DateOnly WeekEnd,
  int TimeLeftToEodMinutes
);

public interface IStreakService
{
  Task<Result<HabitStreakStatus>> GetStatusForHabit(string userId, Guid habitId, string userTimezone, string habitTimezone, string[] daysOfWeek, DateTime? nowUtc = null);
}
