using CSharp_Result;

namespace Domain.Strava;

public interface IStravaConnectionRepository
{
  Task<Result<StravaConnectionPrincipal?>> Get(string userId);

  Task<Result<StravaConnectionPrincipal?>> GetByAthlete(long athleteId);

  // Links (or relinks) the user's Strava. Fails with a conflict when the athlete is linked to
  // another user (AthleteId is unique).
  Task<Result<StravaConnectionPrincipal>> Upsert(string userId, StravaConnectionRecord record);

  // Runs refresh with the connection row locked, so two workers never spend the same refresh
  // token (Strava invalidates the old one as soon as it issues a new one). refresh returns the
  // new tokens to save, or null to keep the current ones. Null when the user isn't connected.
  Task<Result<StravaConnectionPrincipal?>> RefreshLocked(string userId,
    Func<StravaConnectionPrincipal, Task<Result<StravaTokens?>>> refresh);

  // Deletes the connection, the user's Strava bindings, cached activities and the Strava evidence
  // on their habits (Strava's terms: delete Strava data on disconnect). Check results stay. Null
  // when not connected.
  Task<Result<Unit?>> Purge(string userId);

  // Removes the evidence of an activity deleted in Strava. The day it proved stays done.
  Task<Result<int>> DeleteActivityEvidence(long activityId);
}

public interface IStravaActivityRepository
{
  Task<Result<StravaActivityPrincipal?>> Get(string userId, long activityId);

  Task<Result<List<StravaActivityPrincipal>>> ListForDate(string userId, DateOnly localDate);

  // Saves a fetched activity. An existing one keeps its assignment unless its date changed.
  Task<Result<StravaActivityPrincipal>> Upsert(string userId, long activityId, StravaActivityRecord record,
    DateTime fetchedAtUtc);

  // Sets the assignment of each given activity of the user.
  Task<Result<Unit>> SetAssignments(string userId, Dictionary<long, StravaAssignment> assignments);

  Task<Result<int>> Delete(long activityId);

  // Strava's 7-day cache limit: deletes activities fetched before the cutoff.
  Task<Result<int>> DeleteFetchedBefore(DateTime cutoffUtc);

  // Runs work while holding the user's Strava day lock, so two writers (a webhook and the
  // user) never match the same user's days at once. UserBusyException when it stays taken.
  Task<Result<T>> Exclusive<T>(string userId, Func<Task<Result<T>>> work);
}

public interface IStravaInboxRepository
{
  Task<Result<Unit>> Enqueue(StravaWebhookEvent e);

  // Claims up to batchSize events (oldest first) for lease; an unfinished claim is retried
  // once its lease runs out.
  Task<Result<List<StravaInboxItem>>> Claim(int batchSize, TimeSpan lease);

  Task<Result<Unit>> Delete(Guid id);
}

// Strava's HTTP API.
public interface IStravaApi
{
  Task<Result<StravaGrant>> ExchangeCode(string code);

  // Null when Strava rejects the refresh token (the user deauthorized LazyTax).
  Task<Result<StravaTokens?>> Refresh(string refreshToken);

  // Null when the activity doesn't exist (deleted) or the token can't see it.
  Task<Result<StravaActivity?>> GetActivity(string accessToken, long activityId);

  Task<Result<Unit>> Revoke(string token);
}
