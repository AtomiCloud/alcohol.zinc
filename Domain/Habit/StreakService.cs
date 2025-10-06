using CSharp_Result;

namespace Domain.Habit;

public class StreakService(IStreakRepository repo) : IStreakService
{
  public async Task<Result<HabitStreakStatus>> GetStatusForHabit(
    string userId,
    Guid habitId,
    string userTimezone,
    string habitTimezone,
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
      var weekMap = new Dictionary<DayOfWeek, bool>
      {
        [DayOfWeek.Sunday] = false,
        [DayOfWeek.Monday] = false,
        [DayOfWeek.Tuesday] = false,
        [DayOfWeek.Wednesday] = false,
        [DayOfWeek.Thursday] = false,
        [DayOfWeek.Friday] = false,
        [DayOfWeek.Saturday] = false,
      };
      foreach (var ts in completedUtc)
      {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ts, DateTimeKind.Utc), userTz);
        var d = DateOnly.FromDateTime(local);
        if (d >= weekStart && d <= weekEnd)
        {
          weekMap[local.DayOfWeek] = true;
        }
      }

      // Time left until habit local EOD 23:59
      var habitTz = TimeZoneInfo.FindSystemTimeZoneById(habitTimezone);
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
        weekMap,
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
