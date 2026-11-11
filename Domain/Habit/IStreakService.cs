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
  int TimeLeftToEodMinutes,
  DateOnly Today,
  HabitDayStatus TodayStatus
);

public interface IStreakService
{
  Task<Result<HabitStreakStatus>> GetStatusForHabit(string userId, Guid habitId, string timezone, string[] daysOfWeek, TimeOnly deadlineTime, DateTime? nowUtc = null);
  Task<Result<Dictionary<Guid, HabitStreakStatus>>> GetStatusForHabits(string timezone, IReadOnlyCollection<HabitVersionPrincipal> versions, DateTime? nowUtc = null);
}
