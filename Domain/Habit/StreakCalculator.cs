namespace Domain.Habit;

public static class StreakCalculator
{
  public static DateOnly TodayFor(string timezoneId, DateTime? nowUtc = null)
  {
    var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
    var now = nowUtc ?? DateTime.UtcNow;
    var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), tz);
    return DateOnly.FromDateTime(local);
  }

  public static (DateOnly Start, DateOnly End) WeekSundayBounds(DateOnly today)
  {
    // Sunday = 0
    var daysSinceSunday = ((int)today.DayOfWeek + 7 - (int)DayOfWeek.Sunday) % 7;
    var start = today.AddDays(-daysSinceSunday);
    var end = start.AddDays(6);
    return (start, end);
  }
}
