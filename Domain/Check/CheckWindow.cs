namespace Domain.Check;

public static class CheckWindow
{
  // How long after a day ends (local midnight) its checks settle, unless the habit's proof
  // needs longer (ICheckSettlePolicy). Late evidence for that day can still land until then.
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

  // A habit's day runs from local midnight to the next local midnight, in the given timezone.
  public static (DateTime OpensAtUtc, DateTime DeadlineUtc) For(DateOnly date, TimeZoneInfo tz)
  {
    var opens = ToUtc(date.ToDateTime(TimeOnly.MinValue), tz);
    var due = ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), tz);
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
