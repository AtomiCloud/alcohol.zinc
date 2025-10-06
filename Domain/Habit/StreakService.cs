using CSharp_Result;

namespace Domain.Habit;

public class StreakService(IStreakRepository repo) : IStreakService
{
  public async Task<Result<HabitStreakStatus>> GetStatusForHabit(
    string userId,
    Guid habitId,
    string userTimezone,
    string habitTimezone,
    string[] daysOfWeek,
    DateTime? nowUtc = null)
  {
    try
    {
      var utcNow = nowUtc ?? DateTime.UtcNow;
      var userToday = StreakCalculator.TodayFor(userTimezone, utcNow);
      var (weekStart, weekEnd) = StreakCalculator.WeekSundayBounds(userToday);

      // Streaks use habit-local dates (as executions are recorded per habit TZ date)
      var habitToday = StreakCalculator.TodayFor(habitTimezone, utcNow);
      var current = await repo.GetCurrentStreak(habitId, habitToday);
      if (!current.IsSuccess()) return current.FailureOrDefault();

      var max = await repo.GetMaxStreak(habitId);
      if (!max.IsSuccess()) return max.FailureOrDefault();

      // IsCompleteToday relative to user local day window
      var userTz = TimeZoneInfo.FindSystemTimeZoneById(userTimezone);
      var userStart = new DateTime(userToday.Year, userToday.Month, userToday.Day, 0, 0, 0, DateTimeKind.Unspecified);
      var userEnd = new DateTime(userToday.Year, userToday.Month, userToday.Day, 23, 59, 59, DateTimeKind.Unspecified);
      var startUtc = TimeZoneInfo.ConvertTimeToUtc(userStart, userTz);
      var endUtc = TimeZoneInfo.ConvertTimeToUtc(userEnd, userTz);
      var doneToday = await repo.HasCompletionBetweenUtc(habitId, startUtc, endUtc);
      if (!doneToday.IsSuccess()) return doneToday.FailureOrDefault();

      // Week map over user's local week using CompletedAt timestamps
      var weekStartUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(weekStart.Year, weekStart.Month, weekStart.Day, 0, 0, 0, DateTimeKind.Unspecified), userTz);
      var weekEndUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(weekEnd.Year, weekEnd.Month, weekEnd.Day, 23, 59, 59, DateTimeKind.Unspecified), userTz);
      var completions = await repo.GetCompletionsBetweenUtc(habitId, weekStartUtc, weekEndUtc);
      if (!completions.IsSuccess()) return completions.FailureOrDefault();
      var completedUtc = completions.Get();

      // Initialize week statuses
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
          weekStatuses[dow] = HabitDayStatus.NotApplicable; // will be set if any events exist
        }
      }

      // Mark successes by CompletedAt mapped to user local day
      foreach (var ts in completedUtc)
      {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ts, DateTimeKind.Utc), userTz);
        var d = DateOnly.FromDateTime(local);
        if (d >= weekStart && d <= weekEnd)
        {
          weekStatuses[local.DayOfWeek] = HabitDayStatus.Succeeded;
        }
      }

      // Get non-completion statuses within approximate habit date range corresponding to the user's week
      var habitTz = TimeZoneInfo.FindSystemTimeZoneById(habitTimezone);
      var habitApproxStart = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(weekStartUtc, DateTimeKind.Utc), habitTz).Date.AddDays(-1);
      var habitApproxEnd = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(weekEndUtc, DateTimeKind.Utc), habitTz).Date.AddDays(1);
      var execsRes = await repo.GetExecutionsInHabitDateRange(habitId, DateOnly.FromDateTime(habitApproxStart), DateOnly.FromDateTime(habitApproxEnd));
      if (!execsRes.IsSuccess()) return execsRes.FailureOrDefault();
      var execs = execsRes.Get();

      static HabitDayStatus MapStatus(ExecutionStatus s) => s switch
      {
        ExecutionStatus.Failed => HabitDayStatus.Failed,
        ExecutionStatus.Skipped => HabitDayStatus.Skip,
        ExecutionStatus.Freeze => HabitDayStatus.Frozen,
        ExecutionStatus.Vacation => HabitDayStatus.Vacation,
        ExecutionStatus.Completed => HabitDayStatus.Succeeded,
        _ => HabitDayStatus.NotApplicable
      };

      foreach (var e in execs)
      {
        if (e.Status == ExecutionStatus.Completed)
        {
          // Completed already accounted via CompletedAt mapping
          continue;
        }

        // Map habit EOD to user's local day
        var eodLocalHabit = new DateTime(e.Date.Year, e.Date.Month, e.Date.Day, 23, 59, 0);
        var eodUtc = TimeZoneInfo.ConvertTimeToUtc(eodLocalHabit, habitTz);
        var userLocalAtEod = TimeZoneInfo.ConvertTimeFromUtc(eodUtc, userTz);
        var userLocalDate = DateOnly.FromDateTime(userLocalAtEod);
        if (userLocalDate >= weekStart && userLocalDate <= weekEnd)
        {
          var dow = userLocalAtEod.DayOfWeek;
          // Do not override success; otherwise, set by priority (Vacation/Frozen/Skip over Failed)
          if (weekStatuses[dow] != HabitDayStatus.Succeeded)
          {
            var mapped = MapStatus(e.Status);
            if (mapped == HabitDayStatus.Vacation || mapped == HabitDayStatus.Frozen || mapped == HabitDayStatus.Skip)
            {
              weekStatuses[dow] = mapped;
            }
            else if (weekStatuses[dow] == HabitDayStatus.NotApplicable)
            {
              weekStatuses[dow] = mapped;
            }
          }
        }
      }

      // Time left until habit local EOD 23:59
      var habitNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), habitTz);
      var eodLocal = new DateTime(habitNow.Year, habitNow.Month, habitNow.Day, 23, 59, 0);
      if (habitNow > eodLocal)
      {
        eodLocal = eodLocal.AddDays(1);
      }
      var minutesLeft = (int)Math.Max(0, Math.Round((eodLocal - habitNow).TotalMinutes));

      return new HabitStreakStatus(
        current.Get(),
        max.Get(),
        doneToday.Get(),
        weekStatuses,
        weekStart,
        weekEnd,
        minutesLeft
      );
    }
    catch (Exception e)
    {
      return e;
    }
  }
}
