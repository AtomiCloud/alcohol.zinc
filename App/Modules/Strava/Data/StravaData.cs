using System.ComponentModel.DataAnnotations;
using App.Modules.Users.Data;

namespace App.Modules.Strava.Data;

// A user's Strava link. Tokens are encrypted (IEncryptor). Deleted on disconnect, deauthorize
// and account purge (cascade), as Strava's terms require.
public class StravaConnectionData
{
  [Key]
  public Guid Id { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }
  public virtual UserData? User { get; set; }

  public required long AthleteId { get; set; }

  [MaxLength(1024)]
  public required string AccessToken { get; set; }

  [MaxLength(1024)]
  public required string RefreshToken { get; set; }

  public required DateTime ExpiresAtUtc { get; set; }

  [MaxLength(256)]
  public required string Scope { get; set; }

  public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Inbox: webhook events waiting for the worker, so the webhook answers Strava within its
// 2-second limit. A row is deleted once processed (IDs only, no Strava data).
public class StravaEventData
{
  [Key]
  public Guid Id { get; set; }

  [MaxLength(16)]
  public required string ObjectType { get; set; }

  [MaxLength(16)]
  public required string AspectType { get; set; }

  public required long ObjectId { get; set; }
  public required long OwnerId { get; set; }
  public required DateTime EventTimeUtc { get; set; }
  public required bool Deauthorized { get; set; }

  public int Attempts { get; set; }

  // Set while a worker holds the event; a claim past this time can be taken again.
  public DateTime? ClaimedUntilUtc { get; set; }

  public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
