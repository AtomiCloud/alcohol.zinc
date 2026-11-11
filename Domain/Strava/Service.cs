using System.Text.Json;
using CSharp_Result;
using Domain.Proof;
using Microsoft.Extensions.Logging;

namespace Domain.Strava;

public interface IStravaService
{
  // Swaps the callback's code for tokens and links the user's Strava.
  Task<Result<StravaConnectOutcome>> Connect(string userId, string code, string grantedScope);

  Task<Result<StravaConnectionPrincipal?>> Get(string userId);

  // Revokes LazyTax in Strava (best effort), then deletes the connection, the Strava bindings and
  // the Strava evidence. Null when not connected.
  Task<Result<Unit?>> Disconnect(string userId);

  // One webhook event, from the inbox.
  Task<Result<Unit>> Process(StravaWebhookEvent e, DateTime? nowUtc = null);
}

public class StravaService(
  IStravaApi api,
  IStravaConnectionRepository connections,
  IProofRouter router,
  ILogger<StravaService> logger
) : IStravaService
{
  private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

  // Refresh a little early so a token never expires between the check and the call.
  private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

  public async Task<Result<StravaConnectOutcome>> Connect(string userId, string code, string grantedScope)
  {
    var scopes = grantedScope.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
    if (!scopes.Contains(StravaScopes.Required)) return StravaConnectOutcome.ScopeMissing;

    var grant = await api.ExchangeCode(code);
    if (grant.IsFailure()) return grant.FailureOrDefault();
    var g = grant.Get();

    var existing = await connections.GetByAthlete(g.AthleteId);
    if (existing.IsFailure()) return existing.FailureOrDefault();
    if (existing.Get() is { } other && other.UserId != userId)
    {
      // Don't leave LazyTax authorized on a Strava account we refuse to link.
      await RevokeQuietly(g.Tokens.RefreshToken);
      return StravaConnectOutcome.AthleteTaken;
    }

    var saved = await connections.Upsert(userId, new StravaConnectionRecord
    {
      AthleteId = g.AthleteId,
      Tokens = g.Tokens,
      Scope = string.Join(',', scopes)
    });
    if (saved.IsFailure()) return saved.FailureOrDefault();
    logger.LogInformation("Strava connected for UserId={UserId}", userId);
    return StravaConnectOutcome.Connected;
  }

  public Task<Result<StravaConnectionPrincipal?>> Get(string userId) => connections.Get(userId);

  public async Task<Result<Unit?>> Disconnect(string userId)
  {
    var connection = await connections.Get(userId);
    if (connection.IsFailure()) return connection.FailureOrDefault();
    if (connection.Get() is not { } c) return (Unit?)null;

    // Revoking the refresh token revokes its access tokens too.
    await RevokeQuietly(c.Record.Tokens.RefreshToken);
    return await connections.Purge(userId);
  }

  public async Task<Result<Unit>> Process(StravaWebhookEvent e, DateTime? nowUtc = null)
  {
    var now = nowUtc ?? DateTime.UtcNow;

    if (e.ObjectType == "athlete")
    {
      if (!e.Deauthorized) return new Unit();
      // The user removed LazyTax in Strava's settings: treat it like a disconnect, minus the revoke.
      var athlete = await connections.GetByAthlete(e.OwnerId);
      if (athlete.IsFailure()) return athlete.FailureOrDefault();
      if (athlete.Get() is not { } a) return new Unit();
      logger.LogInformation("Strava deauthorized for UserId={UserId}", a.UserId);
      var purged = await connections.Purge(a.UserId);
      return purged.IsFailure() ? purged.FailureOrDefault() : new Unit();
    }

    if (e.ObjectType != "activity") return new Unit();

    if (e.AspectType == "delete")
    {
      var deleted = await connections.DeleteActivityEvidence(e.ObjectId);
      return deleted.IsFailure() ? deleted.FailureOrDefault() : new Unit();
    }

    // create, or update (the sport type may have changed): fetch and route. Replays are
    // idempotent on the activity id.
    var connection = await connections.GetByAthlete(e.OwnerId);
    if (connection.IsFailure()) return connection.FailureOrDefault();
    if (connection.Get() is not { } conn) return new Unit();

    var token = await AccessToken(conn, now);
    if (token.IsFailure()) return token.FailureOrDefault();
    if (token.Get() is not { } accessToken) return new Unit();

    var activity = await api.GetActivity(accessToken, e.ObjectId);
    if (activity.IsFailure()) return activity.FailureOrDefault();
    if (activity.Get() is not { } act) return new Unit();

    var routed = await router.Route(new ProofEvent
    {
      PluginKey = StravaProofPlugin.Key,
      UserId = conn.UserId,
      ExternalId = e.ObjectId.ToString(),
      OccurredAtUtc = DateTime.SpecifyKind(act.StartDate, DateTimeKind.Utc),
      Payload = JsonSerializer.Serialize(act, PayloadJson)
    }, now);
    return routed.IsFailure() ? routed.FailureOrDefault() : new Unit();
  }

  // A valid access token, refreshing under the connection's lock when it is about to expire.
  // Null when Strava rejected the refresh token: the connection is purged.
  private async Task<Result<string?>> AccessToken(StravaConnectionPrincipal conn, DateTime now)
  {
    if (conn.Record.Tokens.ExpiresAtUtc - RefreshMargin > now) return conn.Record.Tokens.AccessToken;

    var rejected = false;
    var refreshed = await connections.RefreshLocked(conn.UserId, async locked =>
    {
      // Another worker may have refreshed while this one waited for the lock.
      if (locked.Record.Tokens.ExpiresAtUtc - RefreshMargin > now) return (StravaTokens?)null;
      var tokens = await api.Refresh(locked.Record.Tokens.RefreshToken);
      if (tokens.IsSuccess() && tokens.Get() == null) rejected = true;
      return tokens;
    });
    if (refreshed.IsFailure()) return refreshed.FailureOrDefault();

    if (rejected)
    {
      logger.LogWarning("Strava refresh token rejected for UserId={UserId}; disconnecting", conn.UserId);
      var purged = await connections.Purge(conn.UserId);
      if (purged.IsFailure()) return purged.FailureOrDefault();
      return (string?)null;
    }

    return refreshed.Get()?.Record.Tokens.AccessToken;
  }

  private async Task RevokeQuietly(string token)
  {
    var revoked = await api.Revoke(token);
    if (revoked.IsFailure())
      logger.LogWarning(revoked.FailureOrDefault(), "Strava revoke failed; continuing");
  }
}
