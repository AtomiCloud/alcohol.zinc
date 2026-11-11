using App.Error.V1;
using App.Modules.System;
using App.StartUp.Database;
using App.Utility;
using CSharp_Result;
using Domain.Strava;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;

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
      var connections = await db.StravaConnections.Where(x => x.UserId == userId).ExecuteDeleteAsync();
      await tx.CommitAsync();

      logger.LogInformation(
        "Purged Strava data for UserId={UserId}: {Connections} connection, {Bindings} bindings, {Evidence} evidence",
        userId, connections, bindings, evidence);
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
