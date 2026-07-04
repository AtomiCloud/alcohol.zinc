using App.StartUp.Database;
using CSharp_Result;
using Domain.Subscription;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Subscription.Data;

public class SubscriptionRepository(MainDbContext db, ILogger<SubscriptionRepository> logger)
  : ISubscriptionRepository
{
  public async Task<Result<UserSubscriptionPrincipal?>> GetByUserId(string userId)
  {
    try
    {
      var data = await db.UserSubscriptions
        .AsNoTracking()
        .Where(x => x.UserId == userId)
        .FirstOrDefaultAsync();

      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed retrieving UserSubscription by UserId: {UserId}", userId);
      return e;
    }
  }

  public async Task<Result<UserSubscriptionPrincipal>> Upsert(UserSubscriptionRecord record)
  {
    try
    {
      var existing = await db.UserSubscriptions
        .Where(x => x.UserId == record.UserId)
        .FirstOrDefaultAsync();

      if (existing == null)
      {
        var data = record.ToData();
        db.UserSubscriptions.Add(data);
        try
        {
          await db.SaveChangesAsync();
          return data.ToPrincipal();
        }
        catch (UniqueConstraintException)
        {
          // Lost an insert race on the unique UserId index: fall through to the
          // update path against the row the winner created.
          db.Entry(data).State = EntityState.Detached;
          existing = await db.UserSubscriptions
            .Where(x => x.UserId == record.UserId)
            .FirstAsync();
        }
      }

      existing.Tier = record.Tier;
      existing.Status = (int)record.Status;
      existing.PeriodStart = record.PeriodStart;
      existing.PeriodEnd = record.PeriodEnd;
      existing.CancelAtPeriodEnd = record.CancelAtPeriodEnd;
      existing.NextTier = record.NextTier;
      existing.KonnectCustomerId = record.KonnectCustomerId;
      existing.KonnectSyncedAt = record.KonnectSyncedAt;
      existing.LastChargeIntentId = record.LastChargeIntentId;
      existing.LastChargeKey = record.LastChargeKey;
      existing.RenewingUntil = record.RenewingUntil;
      existing.UpdatedAt = DateTime.UtcNow;
      await db.SaveChangesAsync();
      return existing.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed upserting UserSubscription for UserId: {UserId}", record.UserId);
      return e;
    }
  }

  public async Task<Result<Unit>> SetIntentId(Guid id, string intentId, string chargeKey)
  {
    try
    {
      var now = DateTime.UtcNow;
      var rows = await db.UserSubscriptions
        .Where(x => x.Id == id)
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.LastChargeIntentId, intentId)
          .SetProperty(x => x.LastChargeKey, chargeKey)
          .SetProperty(x => x.UpdatedAt, now));

      if (rows == 0) return new InvalidOperationException($"UserSubscription {id} not found");
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed setting intent id for UserSubscription {Id}", id);
      return e;
    }
  }

  public async Task<Result<bool>> Activate(Guid id, string tier, DateTime periodStart, DateTime periodEnd, DateTime nowUtc)
  {
    try
    {
      var now = DateTime.UtcNow;
      // Guarded on no live renewal lease: an interactive activation racing a
      // renewal charge must lose and let the client retry (the retry reconciles
      // the recorded intent, so no money is lost or re-charged).
      var rows = await db.UserSubscriptions
        .Where(x => x.Id == id && (x.RenewingUntil == null || x.RenewingUntil < nowUtc))
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.Tier, tier)
          .SetProperty(x => x.Status, (int)SubscriptionStatus.Active)
          .SetProperty(x => x.PeriodStart, periodStart)
          .SetProperty(x => x.PeriodEnd, periodEnd)
          .SetProperty(x => x.CancelAtPeriodEnd, false)
          .SetProperty(x => x.NextTier, (string?)null)
          .SetProperty(x => x.LastChargeIntentId, (string?)null)
          .SetProperty(x => x.LastChargeKey, (string?)null)
          .SetProperty(x => x.UpdatedAt, now));

      return rows > 0;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed activating UserSubscription {Id}", id);
      return e;
    }
  }

  public async Task<Result<bool>> RollPeriod(Guid id, string tier, DateTime periodStart, DateTime periodEnd)
  {
    try
    {
      var now = DateTime.UtcNow;
      // Guarded on PeriodEnd == periodStart (the old period end): a roll computed
      // from a stale snapshot must no-op rather than clobber a period an
      // interactive upgrade just reset.
      var rows = await db.UserSubscriptions
        .Where(x => x.Id == id && x.PeriodEnd == periodStart)
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.Tier, tier)
          .SetProperty(x => x.Status, (int)SubscriptionStatus.Active)
          .SetProperty(x => x.PeriodStart, periodStart)
          .SetProperty(x => x.PeriodEnd, periodEnd)
          // Clear NextTier only when it is the value this roll applied; a
          // downgrade scheduled while the renewal was in flight is preserved.
          .SetProperty(x => x.NextTier, x => x.NextTier == tier ? null : x.NextTier)
          .SetProperty(x => x.LastChargeIntentId, (string?)null)
          .SetProperty(x => x.LastChargeKey, (string?)null)
          .SetProperty(x => x.UpdatedAt, now));

      return rows > 0;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed rolling period for UserSubscription {Id}", id);
      return e;
    }
  }

  public async Task<Result<Unit>> SetCancelAtPeriodEnd(Guid id, bool value)
  {
    try
    {
      var now = DateTime.UtcNow;
      var rows = await db.UserSubscriptions
        .Where(x => x.Id == id)
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.CancelAtPeriodEnd, value)
          .SetProperty(x => x.UpdatedAt, now));

      if (rows == 0) return new InvalidOperationException($"UserSubscription {id} not found");
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed setting CancelAtPeriodEnd for UserSubscription {Id}", id);
      return e;
    }
  }

  public async Task<Result<Unit>> SetNextTier(Guid id, string? nextTier)
  {
    try
    {
      var now = DateTime.UtcNow;
      var rows = await db.UserSubscriptions
        .Where(x => x.Id == id)
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.NextTier, nextTier)
          .SetProperty(x => x.UpdatedAt, now));

      if (rows == 0) return new InvalidOperationException($"UserSubscription {id} not found");
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed setting NextTier for UserSubscription {Id}", id);
      return e;
    }
  }

  public async Task<Result<Unit>> MarkGrace(Guid id)
  {
    try
    {
      var now = DateTime.UtcNow;
      var rows = await db.UserSubscriptions
        .Where(x => x.Id == id)
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.Status, (int)SubscriptionStatus.Grace)
          .SetProperty(x => x.UpdatedAt, now));

      if (rows == 0) return new InvalidOperationException($"UserSubscription {id} not found");
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed marking Grace for UserSubscription {Id}", id);
      return e;
    }
  }

  public async Task<Result<Unit>> MarkCancelled(Guid id)
  {
    try
    {
      var now = DateTime.UtcNow;
      var rows = await db.UserSubscriptions
        .Where(x => x.Id == id)
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.Status, (int)SubscriptionStatus.Cancelled)
          .SetProperty(x => x.CancelAtPeriodEnd, false)
          .SetProperty(x => x.NextTier, (string?)null)
          .SetProperty(x => x.LastChargeIntentId, (string?)null)
          .SetProperty(x => x.LastChargeKey, (string?)null)
          .SetProperty(x => x.UpdatedAt, now));

      if (rows == 0) return new InvalidOperationException($"UserSubscription {id} not found");
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed marking Cancelled for UserSubscription {Id}", id);
      return e;
    }
  }

  public async Task<Result<bool>> TryClaimRenewal(Guid id, DateTime expectedPeriodEnd, DateTime until, DateTime nowUtc)
  {
    try
    {
      // Atomic claim: only when the period is still the expected one and no
      // live lease exists. Deliberately does NOT touch UpdatedAt (not a
      // state change the Konnect mirror cares about).
      var rows = await db.UserSubscriptions
        .Where(x => x.Id == id
                    && x.PeriodEnd == expectedPeriodEnd
                    && (x.RenewingUntil == null || x.RenewingUntil < nowUtc))
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RenewingUntil, until));

      return rows > 0;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed claiming renewal for UserSubscription {Id}", id);
      return e;
    }
  }

  public async Task<Result<Unit>> ReleaseRenewal(Guid id, DateTime until)
  {
    try
    {
      // Only release our own lease: a pass that overran its window must not
      // null a lease a newer claimant now owns.
      await db.UserSubscriptions
        .Where(x => x.Id == id && x.RenewingUntil == until)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RenewingUntil, (DateTime?)null));

      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed releasing renewal lease for UserSubscription {Id}", id);
      return e;
    }
  }

  public async Task<Result<List<UserSubscriptionPrincipal>>> GetDue(DateTime nowUtc, int batchSize)
  {
    try
    {
      var data = await db.UserSubscriptions
        .AsNoTracking()
        .Where(x => (x.Status == (int)SubscriptionStatus.Active || x.Status == (int)SubscriptionStatus.Grace)
                    && x.PeriodEnd <= nowUtc)
        .OrderBy(x => x.PeriodEnd)
        .Take(batchSize)
        .ToListAsync();

      return data.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed querying due UserSubscriptions");
      return e;
    }
  }

  public async Task<Result<List<UserSubscriptionPrincipal>>> GetUnsynced(int batchSize)
  {
    try
    {
      var data = await db.UserSubscriptions
        .AsNoTracking()
        .Where(x => x.KonnectSyncedAt == null || x.KonnectSyncedAt < x.UpdatedAt)
        .OrderBy(x => x.UpdatedAt)
        .Take(batchSize)
        .ToListAsync();

      return data.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed querying unsynced UserSubscriptions");
      return e;
    }
  }

  public async Task<Result<Unit>> MarkKonnectSynced(Guid id, string konnectCustomerId, DateTime at)
  {
    try
    {
      // Deliberately does NOT touch UpdatedAt: the watermark comparison
      // (KonnectSyncedAt < UpdatedAt) is what detects staleness.
      var rows = await db.UserSubscriptions
        .Where(x => x.Id == id)
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.KonnectCustomerId, konnectCustomerId)
          .SetProperty(x => x.KonnectSyncedAt, at));

      if (rows == 0) return new InvalidOperationException($"UserSubscription {id} not found");
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed marking Konnect synced for UserSubscription {Id}", id);
      return e;
    }
  }
}
