namespace Domain.Strava;

// The user's Strava link. Tokens are plain here; the repository encrypts them at rest.
public record StravaConnectionRecord
{
  public required long AthleteId { get; init; }
  public required StravaTokens Tokens { get; init; }
  public required string Scope { get; init; }
}

public record StravaConnectionPrincipal
{
  public required Guid Id { get; init; }
  public required string UserId { get; init; }
  public required DateTime ConnectedAtUtc { get; init; }
  public required StravaConnectionRecord Record { get; init; }
}

public record StravaTokens(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);

// What Strava returns for an authorization code.
public record StravaGrant(long AthleteId, StravaTokens Tokens);

// The only activity fields zinc reads. Strava's terms allow showing Strava data only to its
// owner and caching it for at most 7 days (StravaActivityRecord, StravaDayService.Retention).
public class StravaActivity
{
  public string Name { get; set; } = "";
  public string SportType { get; set; } = "";
  public DateTime StartDate { get; set; }
  public bool Manual { get; set; }
}

// A fetched activity, cached so a day can be matched again and its owner can see which habit
// each activity proved. Deleted after StravaDayService.Retention, on disconnect and when the
// activity is deleted in Strava.
public record StravaActivityRecord
{
  public required string Name { get; init; }
  public required string SportType { get; init; }
  public required DateTime StartUtc { get; init; }
  public required bool Manual { get; init; }

  // The start's date in the user's timezone: the day the activity counts for.
  public required DateOnly LocalDate { get; init; }
}

// The habit an activity proves (null: not used). Pinned: the user chose it, so matching keeps
// it and fills the other habits around it.
public record StravaAssignment(Guid? HabitId, bool Pinned);

public record StravaActivityPrincipal
{
  public required Guid Id { get; init; }
  public required long ActivityId { get; init; }
  public required string UserId { get; init; }
  public required StravaActivityRecord Record { get; init; }
  public required StravaAssignment Assignment { get; init; }
}

// An activity as its owner sees it: Locked once its day has settled.
public record StravaDayActivity(StravaActivityPrincipal Principal, bool Locked);

// The user tried to change a day that has already settled.
public class StravaDayLockedException(DateOnly localDate)
  : Exception($"Strava activities of {localDate:yyyy-MM-dd} can no longer change: the day has settled")
{
  public DateOnly LocalDate { get; } = localDate;
}

// A webhook event as Strava sends it: IDs only.
public record StravaWebhookEvent
{
  public required string ObjectType { get; init; } // activity | athlete
  public required string AspectType { get; init; } // create | update | delete
  public required long ObjectId { get; init; }
  public required long OwnerId { get; init; }
  public required DateTime EventTimeUtc { get; init; }

  // athlete update with updates.authorized = "false": the user removed LazyTax in Strava.
  public bool Deauthorized { get; init; }
}

// An inbox row claimed by the worker; Attempts counts this claim.
public record StravaInboxItem(Guid Id, int Attempts, StravaWebhookEvent Event);

// The connect callback's result, which the app gets in its deep link.
public enum StravaConnectOutcome
{
  Connected,
  ScopeMissing, // the user unticked "View data about your private activities" or activity access
  AthleteTaken, // that Strava account is linked to another LazyTax user
}

public static class StravaScopes
{
  // read_all so runs set to "Only You" count too (decided 2026-11-11).
  public const string Required = "activity:read_all";
}
