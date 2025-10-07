using CSharp_Result;

namespace Domain.Habit;

public class StreakService(IStreakRepository repo) : IStreakService
{
  public Task<Result<HabitStreakStatus>> GetStatusForHabit(
    string userId,
    Guid habitId,
    string userTimezone,
    string habitTimezone,
    string[] daysOfWeek,
    DateTime? nowUtc = null)
  {
      var utcNow = nowUtc ?? DateTime.UtcNow;
      var userToday = StreakCalculator.TodayFor(userTimezone, utcNow);
      var (weekStart, weekEnd) = StreakCalculator.WeekSundayBounds(userToday);

      var userTz = TimeZoneInfo.FindSystemTimeZoneById(userTimezone);
      var habitTz = TimeZoneInfo.FindSystemTimeZoneById(habitTimezone);

      // windows for today/week in UTC (user-local)
      var userStart = new DateTime(userToday.Year, userToday.Month, userToday.Day, 0, 0, 0, DateTimeKind.Unspecified);
      var userEnd = new DateTime(userToday.Year, userToday.Month, userToday.Day, 23, 59, 59, DateTimeKind.Unspecified);
      var startUtc = TimeZoneInfo.ConvertTimeToUtc(userStart, userTz);
      var endUtc = TimeZoneInfo.ConvertTimeToUtc(userEnd, userTz);
      var weekStartUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(weekStart.Year, weekStart.Month, weekStart.Day, 0, 0, 0, DateTimeKind.Unspecified), userTz);
      var weekEndUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(weekEnd.Year, weekEnd.Month, weekEnd.Day, 23, 59, 59, DateTimeKind.Unspecified), userTz);

      // streaks in habit-local
      var habitToday = StreakCalculator.TodayFor(habitTimezone, utcNow);

      return repo.GetCurrentStreak(habitId, habitToday)
        .ThenAwait(current => repo.GetMaxStreak(habitId)
          .Then(max => new { current, max }, Errors.MapNone))
        .ThenAwait(ctx => repo.HasCompletionBetweenUtc(habitId, startUtc, endUtc)
          .Then(doneToday => new { ctx.current, ctx.max, doneToday }, Errors.MapNone))
        .ThenAwait(ctx => repo.GetCompletionsBetweenUtc(habitId, weekStartUtc, weekEndUtc)
          .Then(completedUtc => new { ctx.current, ctx.max, ctx.doneToday, completedUtc }, Errors.MapNone))
        .ThenAwait(ctx =>
        {
          // Expand user week into statuses
          var habitApproxStart = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(weekStartUtc, DateTimeKind.Utc), habitTz).Date.AddDays(-1);
          var habitApproxEnd = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(weekEndUtc, DateTimeKind.Utc), habitTz).Date.AddDays(1);
          return repo.GetExecutionsInHabitDateRange(habitId, DateOnly.FromDateTime(habitApproxStart), DateOnly.FromDateTime(habitApproxEnd))
            .Then(execs =>
            {
              var weekStatuses = InitWeekStatuses(daysOfWeek);

              // successes by CompletedAt mapped to user local day
              foreach (var ts in ctx.completedUtc)
              {
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ts, DateTimeKind.Utc), userTz);
                var d = DateOnly.FromDateTime(local);
                if (d >= weekStart && d <= weekEnd)
                {
                  weekStatuses[local.DayOfWeek] = HabitDayStatus.Succeeded;
                }
              }

              foreach (var e in execs)
              {
                if (e.Status == ExecutionStatus.Completed) continue;

                var eodLocalHabit = new DateTime(e.Date.Year, e.Date.Month, e.Date.Day, 23, 59, 0);
                var eodUtc = TimeZoneInfo.ConvertTimeToUtc(eodLocalHabit, habitTz);
                var userLocalAtEod = TimeZoneInfo.ConvertTimeFromUtc(eodUtc, userTz);
                var userLocalDate = DateOnly.FromDateTime(userLocalAtEod);
                if (userLocalDate < weekStart || userLocalDate > weekEnd) continue;

                var dow = userLocalAtEod.DayOfWeek;
                if (weekStatuses[dow] == HabitDayStatus.Succeeded) continue;

                var mapped = e.Status switch
                {
                  ExecutionStatus.Failed => HabitDayStatus.Failed,
                  ExecutionStatus.Skipped => HabitDayStatus.Skip,
                  ExecutionStatus.Freeze => HabitDayStatus.Frozen,
                  ExecutionStatus.Vacation => HabitDayStatus.Vacation,
                  _ => HabitDayStatus.NotApplicable
                };

                if (mapped == HabitDayStatus.Vacation || mapped == HabitDayStatus.Frozen || mapped == HabitDayStatus.Skip
                  || weekStatuses[dow] == HabitDayStatus.NotApplicable)
                {
                  weekStatuses[dow] = mapped;
                }
              }

              // habit-local time left until EOD 23:59
              var habitNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), habitTz);
              var eodLocal = new DateTime(habitNow.Year, habitNow.Month, habitNow.Day, 23, 59, 0);
              if (habitNow > eodLocal) eodLocal = eodLocal.AddDays(1);
              var minutesLeft = (int)Math.Max(0, Math.Round((eodLocal - habitNow).TotalMinutes));

              return new HabitStreakStatus(
                ctx.current,
                ctx.max,
                ctx.doneToday,
                weekStatuses,
                weekStart,
                weekEnd,
                minutesLeft
              );
            }, Errors.MapNone);
        });

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
