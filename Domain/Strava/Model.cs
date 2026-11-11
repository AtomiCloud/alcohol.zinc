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
// owner and caching it for at most 7 days, so this lives in memory and is never stored.
public class StravaActivity
{
  public string SportType { get; set; } = "";
  public DateTime StartDate { get; set; }
  public bool Manual { get; set; }
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
