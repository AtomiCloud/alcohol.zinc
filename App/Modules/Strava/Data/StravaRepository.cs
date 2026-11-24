using App.Error.V1;
using App.Modules.System;
using App.StartUp.Database;
using App.Utility;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Strava;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace App.Modules.Strava.Data;

public class StravaConnectionRepository(
  MainDbContext db,
  IEncryptor encryptor,
  ILogger<StravaConnectionRepository> logger
) : IStravaConnectionRepository
{
  public async Task<Result<StravaConnectionPrincipal?>> Get(string userId)
  {
    try
    {
      var data = await db.StravaConnections.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
      return data?.ToPrincipal(encryptor);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Get Strava connection failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<StravaConnectionPrincipal?>> GetByAthlete(long athleteId)
  {
    try
    {
      var data = await db.StravaConnections.AsNoTracking().FirstOrDefaultAsync(x => x.AthleteId == athleteId);
      return data?.ToPrincipal(encryptor);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Get Strava connection failed for AthleteId={AthleteId}", athleteId);
      throw;
    }
  }

  public async Task<Result<StravaConnectionPrincipal>> Upsert(string userId, StravaConnectionRecord record)
  {
    try
    {
      var data = await db.StravaConnections.FirstOrDefaultAsync(x => x.UserId == userId);
      if (data == null) db.StravaConnections.Add(data = record.ToData(userId, encryptor));
      else
      {
        data.ToData(record, encryptor);
        data.ConnectedAtUtc = DateTime.UtcNow;
      }

      await db.SaveChangesAsync();
      return data.ToPrincipal(encryptor);
    }
    catch (UniqueConstraintException e)
    {
      // Another user linked the same Strava account first.
      logger.LogWarning(e, "Strava athlete already linked; UserId={UserId}", userId);
      return new EntityConflict("Strava account is linked to another user", typeof(StravaConnectionPrincipal))
        .ToException();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Upsert Strava connection failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<StravaConnectionPrincipal?>> RefreshLocked(string userId,
    Func<StravaConnectionPrincipal, Task<Result<StravaTokens?>>> refresh)
  {
    try
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      // ToListAsync so EF runs FOR UPDATE verbatim instead of wrapping it in a subquery.
      var data = (await db.StravaConnections
          .FromSqlRaw(@"SELECT * FROM ""StravaConnections"" WHERE ""UserId"" = {0} FOR UPDATE", userId)
          .ToListAsync())
        .FirstOrDefault();
      if (data == null) return (StravaConnectionPrincipal?)null;

      var tokens = await refresh(data.ToPrincipal(encryptor));
      if (tokens.IsFailure()) return tokens.FailureOrDefault();
      if (tokens.Get() is { } t)
      {
        data.ToData(t, encryptor);
        await db.SaveChangesAsync();
      }

      await tx.CommitAsync();
      return data.ToPrincipal(encryptor);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Refresh Strava tokens failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<Unit?>> Purge(string userId)
  {
    try
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      var habitIds = db.Habits.Where(h => h.UserId == userId).Select(h => h.Id);
      var evidence = await db.HabitEvidence
        .Where(x => x.ProofKey == StravaProofPlugin.Key && habitIds.Contains(x.HabitId))
        .ExecuteDeleteAsync();
      var bindings = await db.ProofBindings
        .Where(x => x.PluginKey == StravaProofPlugin.Key && habitIds.Contains(x.HabitId))
        .ExecuteDeleteAsync();
      var activities = await db.StravaActivities.Where(x => x.UserId == userId).ExecuteDeleteAsync();
      var connections = await db.StravaConnections.Where(x => x.UserId == userId).ExecuteDeleteAsync();
      await tx.CommitAsync();

      logger.LogInformation(
        "Purged Strava data for UserId={UserId}: {Connections} connection, {Bindings} bindings, {Evidence} evidence, {Activities} activities",
        userId, connections, bindings, evidence, activities);
      return connections == 0 ? (Unit?)null : new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Purge Strava data failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<int>> DeleteActivityEvidence(long activityId)
  {
    try
    {
      var externalId = activityId.ToString();
      return await db.HabitEvidence
        .Where(x => x.ProofKey == StravaProofPlugin.Key && x.ExternalId == externalId)
        .ExecuteDeleteAsync();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Delete Strava evidence failed for ActivityId={ActivityId}", activityId);
      throw;
    }
  }
}

public class StravaInboxRepository(MainDbContext db, ILogger<StravaInboxRepository> logger) : IStravaInboxRepository
{
  public async Task<Result<Unit>> Enqueue(StravaWebhookEvent e)
  {
    try
    {
      db.StravaEvents.Add(e.ToData());
      await db.SaveChangesAsync();
      return new Unit();
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Enqueue Strava event failed for ObjectId={ObjectId}", e.ObjectId);
      throw;
    }
  }

  public async Task<Result<List<StravaInboxItem>>> Claim(int batchSize, TimeSpan lease)
  {
    try
    {
      // Claim and return in one statement; SKIP LOCKED hands each row to one worker across pods.
      var now = DateTime.UtcNow;
      var claimed = await db.StravaEvents
        .FromSqlRaw(@"
          UPDATE ""StravaEvents"" SET ""Attempts"" = ""Attempts"" + 1, ""ClaimedUntilUtc"" = {0}
          WHERE ""Id"" IN (
            SELECT ""Id"" FROM ""StravaEvents""
            WHERE ""ClaimedUntilUtc"" IS NULL OR ""ClaimedUntilUtc"" < {1}
            ORDER BY ""ReceivedAtUtc""
            LIMIT {2}
            FOR UPDATE SKIP LOCKED
          )
          RETURNING *",
          now + lease, now, batchSize)
        .ToListAsync();
      return claimed.OrderBy(x => x.ReceivedAtUtc).Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Claim Strava events failed");
      throw;
    }
  }

  public async Task<Result<Unit>> Delete(Guid id)
  {
    try
    {
      await db.StravaEvents.Where(x => x.Id == id).ExecuteDeleteAsync();
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Delete Strava event failed for Id={Id}", id);
      throw;
    }
  }
}

public class StravaActivityRepository(MainDbContext db, ILogger<StravaActivityRepository> logger)
  : IStravaActivityRepository
{
  // How long Exclusive waits for another writer of the same user's Strava days.
  private const string LockTimeout = "10s";

  public async Task<Result<StravaActivityPrincipal?>> Get(string userId, long activityId)
  {
    try
    {
      var data = await db.StravaActivities.AsNoTracking()
        .FirstOrDefaultAsync(x => x.UserId == userId && x.ActivityId == activityId);
      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Get Strava activity failed for UserId={UserId} ActivityId={ActivityId}", userId, activityId);
      throw;
    }
  }

  public async Task<Result<List<StravaActivityPrincipal>>> ListForDate(string userId, DateOnly localDate)
  {
    try
    {
      var data = await db.StravaActivities.AsNoTracking()
        .Where(x => x.UserId == userId && x.LocalDate == localDate)
        .OrderBy(x => x.StartUtc).ThenBy(x => x.ActivityId)
        .ToListAsync();
      return data.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "List Strava activities failed for UserId={UserId} Date={Date}", userId, localDate);
      throw;
    }
  }

  public async Task<Result<StravaActivityPrincipal>> Upsert(string userId, long activityId,
    StravaActivityRecord record, DateTime fetchedAtUtc)
  {
    try
    {
      var data = await db.StravaActivities.FirstOrDefaultAsync(x => x.UserId == userId && x.ActivityId == activityId);
      if (data == null) db.StravaActivities.Add(data = record.ToData(userId, activityId, fetchedAtUtc));
      else
      {
        // Moved to another day: the old day's choice no longer applies.
        if (data.LocalDate != record.LocalDate)
        {
          data.HabitId = null;
          data.Pinned = false;
        }

        data.ToData(record, fetchedAtUtc);
      }

      await db.SaveChangesAsync();
      db.ChangeTracker.Clear();
      return data.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Upsert Strava activity failed for UserId={UserId} ActivityId={ActivityId}", userId,
        activityId);
      db.ChangeTracker.Clear();
      throw;
    }
  }

  public async Task<Result<Unit>> SetAssignments(string userId, Dictionary<long, StravaAssignment> assignments)
  {
    try
    {
      if (assignments.Count == 0) return new Unit();
      var ids = assignments.Keys.ToList();
      var rows = await db.StravaActivities.Where(x => x.UserId == userId && ids.Contains(x.ActivityId)).ToListAsync();
      foreach (var row in rows)
      {
        var a = assignments[row.ActivityId];
        row.HabitId = a.HabitId;
        row.Pinned = a.Pinned;
      }

      await db.SaveChangesAsync();
      db.ChangeTracker.Clear();
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Set Strava assignments failed for UserId={UserId}", userId);
      db.ChangeTracker.Clear();
      throw;
    }
  }

  public async Task<Result<int>> Delete(long activityId)
  {
    try
    {
      return await db.StravaActivities.Where(x => x.ActivityId == activityId).ExecuteDeleteAsync();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Delete Strava activity failed for ActivityId={ActivityId}", activityId);
      throw;
    }
  }

  public async Task<Result<int>> DeleteFetchedBefore(DateTime cutoffUtc)
  {
    try
    {
      var cutoff = DateTime.SpecifyKind(cutoffUtc, DateTimeKind.Utc);
      return await db.StravaActivities.Where(x => x.FetchedAtUtc < cutoff).ExecuteDeleteAsync();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Delete expired Strava activities failed");
      throw;
    }
  }

  // The lock lives in its own connection's transaction, so the work can run (and commit) its own
  // transactions on db, such as check settles, which take the user's lock after this one.
  public async Task<Result<T>> Exclusive<T>(string userId, Func<Task<Result<T>>> work)
  {
    await using var conn = new NpgsqlConnection(db.Database.GetConnectionString());
    await conn.OpenAsync();
    await using var tx = await conn.BeginTransactionAsync();
    try
    {
      await using var timeout = new NpgsqlCommand($"SET LOCAL lock_timeout = '{LockTimeout}'", conn, tx);
      await timeout.ExecuteNonQueryAsync();
      await using var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", conn, tx);
      lockCmd.Parameters.AddWithValue("key", "strava-day:" + userId);
      await lockCmd.ExecuteNonQueryAsync();
    }
    catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.LockNotAvailable)
    {
      logger.LogWarning("Strava day lock of UserId={UserId} stayed taken", userId);
      return new UserBusyException(userId);
    }

    var result = await work();
    await tx.CommitAsync();
    return result;
  }
}
