namespace Domain.Check;

public static class CheckWindow
{
  public static readonly TimeOnly DefaultDeadline = new(23, 59);

  // Late evidence that happened before the deadline can still land this long after it,
  // and CloseDue waits this long before settling a check as missed.
  public static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);

  public static TimeZoneInfo Resolve(string? timezoneId)
  {
    if (!string.IsNullOrWhiteSpace(timezoneId)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timezoneId, out var tz))
      return tz;
    return TimeZoneInfo.Utc;
  }

  public static DateOnly LocalDate(DateTime utc, TimeZoneInfo tz) =>
    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz));

  // A check opens at local midnight and its deadline is the end of the deadline minute
  // (23:59 means "until midnight"), both in the given timezone.
  public static (DateTime OpensAtUtc, DateTime DeadlineUtc) For(DateOnly date, TimeZoneInfo tz, TimeOnly deadline)
  {
    var opens = ToUtc(date.ToDateTime(TimeOnly.MinValue), tz);
    var due = ToUtc(date.ToDateTime(deadline), tz).AddMinutes(1);
    if (due <= opens) due = opens.AddMinutes(1);
    return (opens, due);
  }

  private static DateTime ToUtc(DateTime local, TimeZoneInfo tz)
  {
    // Local times inside a DST gap don't exist; move forward to the first valid time.
    var l = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    while (tz.IsInvalidTime(l)) l = l.AddMinutes(15);
    return TimeZoneInfo.ConvertTimeToUtc(l, tz);
  }
}
