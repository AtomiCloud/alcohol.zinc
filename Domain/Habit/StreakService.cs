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
    return repo.GetExecutionsForHabits([habitId])
      .Then(byHabit => Status(byHabit.GetValueOrDefault(habitId) ?? [], timezone, daysOfWeek, deadlineTime, utcNow),
        Errors.MapNone);
  }

  // The same status for many habits from one read of their executions.
  public Task<Result<Dictionary<Guid, HabitStreakStatus>>> GetStatusForHabits(
    string timezone,
    IReadOnlyCollection<HabitVersionPrincipal> versions,
    DateTime? nowUtc = null)
  {
    var utcNow = nowUtc ?? DateTime.UtcNow;
    return repo.GetExecutionsForHabits(versions.Select(v => v.HabitId).Distinct().ToList())
      .Then(byHabit => versions
        .DistinctBy(v => v.HabitId)
        .ToDictionary(v => v.HabitId, v => Status(byHabit.GetValueOrDefault(v.HabitId) ?? [], timezone,
          v.Record.DaysOfWeek, v.Record.DeadlineTime, utcNow)), Errors.MapNone);
  }

  // executions: every execution of the habit, oldest first.
  static HabitStreakStatus Status(
    List<HabitExecutionRecord> executions,
    string timezone,
    string[] daysOfWeek,
    TimeOnly deadlineTime,
    DateTime utcNow)
  {
      var today = StreakCalculator.TodayFor(timezone, utcNow);
      var (weekStart, weekEnd) = StreakCalculator.WeekSundayBounds(today);

      // Current: Completed days after the last Failed day, up to today.
      var since = executions
        .Where(e => e.Date <= today && e.Status == ExecutionStatus.Failed)
        .Select(e => (DateOnly?)e.Date)
        .Max() ?? DateOnly.MinValue;
      var current = executions.Count(e =>
        e.Status == ExecutionStatus.Completed && e.Date > since && e.Date <= today);

      // Max: the longest run of Completed days; a Failed day resets it, other statuses don't.
      int run = 0, max = 0;
      foreach (var e in executions)
      {
        if (e.Status == ExecutionStatus.Completed) max = Math.Max(max, ++run);
        else if (e.Status == ExecutionStatus.Failed) run = 0;
      }

      var execs = executions.Where(e => e.Date >= weekStart && e.Date <= weekEnd).ToList();
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
        current,
        max,
        doneToday,
        weekStatuses,
        weekStart,
        weekEnd,
        minutesLeft,
        today,
        todayStatus
      );

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
