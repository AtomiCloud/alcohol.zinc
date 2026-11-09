using CSharp_Result;

namespace Domain.Habit;

public class StreakService(IStreakRepository repo) : IStreakService
{
  // One timezone (the user's) drives today, the week and the time left. Each day's status
  // comes from its execution's own date, so past days never move when the user travels.
  public Task<Result<HabitStreakStatus>> GetStatusForHabit(
    string userId,
    Guid habitId,
    string timezone,
    string[] daysOfWeek,
    TimeOnly deadlineTime,
    DateTime? nowUtc = null)
  {
      var utcNow = nowUtc ?? DateTime.UtcNow;
      var today = StreakCalculator.TodayFor(timezone, utcNow);
      var (weekStart, weekEnd) = StreakCalculator.WeekSundayBounds(today);

      return repo.GetCurrentStreak(habitId, today)
        .ThenAwait(current => repo.GetMaxStreak(habitId)
          .Then(max => new { current, max }, Errors.MapNone))
        .ThenAwait(ctx => repo.GetExecutionsInHabitDateRange(habitId, weekStart, weekEnd)
          .Then(execs =>
          {
            var weekStatuses = InitWeekStatuses(daysOfWeek);
            foreach (var e in execs)
              weekStatuses[e.Date.DayOfWeek] = MapStatus(e.Status);

            var todayExecution = execs.FirstOrDefault(e => e.Date == today);
            var doneToday = todayExecution?.Status == ExecutionStatus.Completed;
            var todayStatus = todayExecution == null ? HabitDayStatus.NotApplicable : MapStatus(todayExecution.Status);

            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var (_, deadlineUtc) = Check.CheckWindow.For(today, tz, deadlineTime);
            var minutesLeft = (int)Math.Max(0, Math.Round((deadlineUtc - utcNow).TotalMinutes));

            return new HabitStreakStatus(
              ctx.current,
              ctx.max,
              doneToday,
              weekStatuses,
              weekStart,
              weekEnd,
              minutesLeft,
              today,
              todayStatus
            );
          }, Errors.MapNone));

      static HabitDayStatus MapStatus(ExecutionStatus status) => status switch
      {
        ExecutionStatus.Completed => HabitDayStatus.Succeeded,
        ExecutionStatus.Failed => HabitDayStatus.Failed,
        ExecutionStatus.Skipped => HabitDayStatus.Skip,
        ExecutionStatus.Freeze => HabitDayStatus.Frozen,
        ExecutionStatus.Vacation => HabitDayStatus.Vacation,
        _ => HabitDayStatus.NotApplicable
      };

      static Dictionary<DayOfWeek, HabitDayStatus> InitWeekStatuses(string[] daysOfWeek)
      {
        var weekStatuses = new Dictionary<DayOfWeek, HabitDayStatus>
        {
          [DayOfWeek.Sunday] = HabitDayStatus.NotApplicable,
          [DayOfWeek.Monday] = HabitDayStatus.NotApplicable,
          [DayOfWeek.Tuesday] = HabitDayStatus.NotApplicable,
          [DayOfWeek.Wednesday] = HabitDayStatus.NotApplicable,
          [DayOfWeek.Thursday] = HabitDayStatus.NotApplicable,
          [DayOfWeek.Friday] = HabitDayStatus.NotApplicable,
          [DayOfWeek.Saturday] = HabitDayStatus.NotApplicable,
        };

        var scheduled = new HashSet<string>(daysOfWeek.Select(x => x.ToLowerInvariant()));
        foreach (var dow in Enum.GetValues<DayOfWeek>())
        {
          var key = dow.ToString().ToLowerInvariant();
          if (scheduled.Contains(key))
          {
            weekStatuses[dow] = HabitDayStatus.NotApplicable;
          }
        }
        return weekStatuses;
      }
  }
}
