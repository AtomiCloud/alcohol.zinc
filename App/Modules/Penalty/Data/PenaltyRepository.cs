using System.Linq.Expressions;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Penalty;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace App.Modules.Penalty.Data;

public class PenaltyRepository(MainDbContext db, ILogger<PenaltyRepository> logger) : IPenaltyRepository
{
  public async Task<Result<bool>> EnqueuePending(PenaltyRecord record)
  {
    PenaltyData? data = null;
    try
    {
      // Idempotent insert via unique(HabitExecutionId): no-op on conflict.
      // The AnyAsync pre-check collapses the common case, but it is NOT atomic
      // with the insert: two overlapping enqueue passes can both observe false
      // and both Add the same HabitExecutionId. The unique index is the real
      // guard, so a 23505 unique_violation on SaveChanges is treated as a no-op
      // (return false) rather than rethrown, so one conflict cannot abort the
      // caller's enqueue loop and silently drop the remaining failed rows.
      var exists = await db.Penalties.AsNoTracking()
        .AnyAsync(x => x.HabitExecutionId == record.HabitExecutionId);
      if (exists) return false;
      data = record.ToData();
      db.Penalties.Add(data);
      await db.SaveChangesAsync();
      return true;
    }
    catch (UniqueConstraintException)
    {
      // Lost the race: another pass already enqueued this execution. Detach the
      // failed entity (still tracked as Added) so the caller's enqueue loop, which
      // reuses this scoped DbContext, does not keep re-attempting the same insert
      // on the next SaveChangesAsync and silently drop subsequent legitimate rows.
      if (data != null) db.Entry(data).State = EntityState.Detached;
      return false;
    }
    catch (Exception e)
    {
      if (data != null) db.Entry(data).State = EntityState.Detached;
      logger.LogError(e, "EnqueuePending failed for HabitExecutionId={HabitExecutionId}", record.HabitExecutionId);
      throw;
    }
  }

  // Stale-claim lease: a row claimed (Processing) more than this long ago is
  // assumed to belong to a crashed/hung worker and is eligible for re-claim, so a
  // failure mid-charge cannot strand the penalty forever. Must comfortably exceed
  // the per-row charge latency (a couple of Airwallex round-trips).
  private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(10);

  public async Task<Result<List<PenaltyPrincipal>>> GetPending(int batchSize)
  {
    try
    {
      // Atomically CLAIM a batch: flip Pending (or stale Processing) -> Processing
      // and return the claimed rows in one statement. FOR UPDATE SKIP LOCKED hands
      // each row to exactly one worker, so overlapping drains never select the same
      // Pending row and double-charge. Only rows returned here are charged.
      var staleCutoff = DateTime.UtcNow - ClaimLease;
      var claimed = await db.Penalties
        .FromSqlRaw(@"
          UPDATE ""Penalties"" SET ""Status"" = {0}, ""UpdatedAt"" = now()
          WHERE ""Id"" IN (
            SELECT ""Id"" FROM ""Penalties""
            WHERE ""Status"" = {1}
               OR (""Status"" = {0} AND ""UpdatedAt"" < {2})
            ORDER BY ""CreatedAt""
            LIMIT {3}
            FOR UPDATE SKIP LOCKED
          )
          RETURNING *",
          (int)PenaltyStatus.Processing,
          (int)PenaltyStatus.Pending,
          staleCutoff,
          batchSize)
        .ToListAsync();
      return claimed.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetPending failed for BatchSize={BatchSize}", batchSize);
      throw;
    }
  }

  public async Task<Result<Unit>> MarkCharged(Guid id, string paymentIntentId)
  {
    try
    {
      // Atomic: set Charged + intent id AND credit charity balance in ONE transaction.
      await using var tx = await db.Database.BeginTransactionAsync();

      // Lock the penalty row (SELECT ... FOR UPDATE) so two workers that each
      // claimed it serialize here. The stale-claim lease can hand an in-flight row
      // to a second worker; without the lock the already-charged check below is a
      // TOCTOU hole — under READ COMMITTED both transactions read Status=Processing,
      // both pass the check, both credit, and the ledger double-counts. With the
      // lock the second waits for the first to commit, then sees Charged -> no-op.
      // ToListAsync (not FirstOrDefaultAsync) so EF runs the raw SQL verbatim and
      // does not wrap FOR UPDATE in an unsupported subquery.
      var penalty = (await db.Penalties
          .FromSqlRaw(@"SELECT * FROM ""Penalties"" WHERE ""Id"" = {0} FOR UPDATE", id)
          .ToListAsync())
        .FirstOrDefault();
      if (penalty == null)
      {
        // Idempotent no-op: nothing to charge.
        await tx.RollbackAsync();
        return new Unit();
      }
      // Re-read the locked row: an entity already tracked by this context (e.g. from the GetPending
      // claim) would otherwise keep its earlier values, and a stale Status would bypass the
      // already-charged check below even though the row lock is held.
      await db.Entry(penalty).ReloadAsync();

      if (penalty.Status == (int)PenaltyStatus.Charged)
      {
        // Already charged (a concurrent or earlier MarkCharged won the row lock and
        // committed). The credit below is not idempotent, so re-running it would
        // over-accrue the charity ledger. Treat a repeat as a no-op.
        await tx.RollbackAsync();
        return new Unit();
      }

      penalty.Status = (int)PenaltyStatus.Charged;
      penalty.PaymentIntentId = paymentIntentId;
      penalty.LastError = null; // clear any stale failure from earlier retries now that it settled
      penalty.UpdatedAt = DateTime.UtcNow;

      // Upsert the (charity, currency) accrual ledger in place. The balance is keyed
      // by (CharityId, Currency) — a charity can receive penalties in multiple
      // currencies, each accruing into its own row, so money is never summed across
      // currencies. Lock the row too: two DIFFERENT penalties for the SAME (charity,
      // currency), drained concurrently across workers, would otherwise both read the
      // same AccruedCents and lost-update one credit. FOR UPDATE serializes the
      // read-modify-write. (When the row does not exist yet FOR UPDATE locks nothing;
      // the unique (CharityId, Currency) index is the backstop — a losing concurrent
      // insert raises a unique violation, the tx rolls back, and the penalty is
      // re-claimed by the stale-claim lease on a later drain.)
      var bal = (await db.CharityBalances
          .FromSqlRaw(
            @"SELECT * FROM ""CharityBalances"" WHERE ""CharityId"" = {0} AND ""Currency"" = {1} FOR UPDATE",
            penalty.CharityId, penalty.Currency)
          .ToListAsync())
        .FirstOrDefault();
      if (bal == null)
      {
        bal = new CharityBalanceData
        {
          CharityId = penalty.CharityId,
          Currency = penalty.Currency,
          AccruedCents = 0
        };
        db.CharityBalances.Add(bal);
      }
      bal.AccruedCents += penalty.AmountCents;
      bal.UpdatedAt = DateTime.UtcNow;

      await db.SaveChangesAsync();
      await tx.CommitAsync();
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkCharged failed for Id={Id} PaymentIntentId={PaymentIntentId}", id, paymentIntentId);
      throw;
    }
  }

  // Charged is final. MarkCharged has credited the charity for a Charged row, and payouts only pick
  // Charged rows, so no later write may move a row out of Charged: not a worker whose claim went stale
  // and was re-claimed, and not a worker closing a penalty detached by an account deletion. Each status
  // write below is ONE conditional UPDATE read fresh from the database (Status <> Charged), never a
  // tracked entity that may hold values from an earlier read. Afterwards any tracked copy is detached
  // so the next read in this context re-reads the row.
  private async Task UpdateUnlessCharged(
    Guid id,
    Expression<Func<SetPropertyCalls<PenaltyData>,
      SetPropertyCalls<PenaltyData>>> set)
  {
    await db.Penalties
      .Where(x => x.Id == id && x.Status != (int)PenaltyStatus.Charged)
      .ExecuteUpdateAsync(set);
    this.ForgetTracked(id);
  }

  private void ForgetTracked(Guid id)
  {
    var tracked = db.Penalties.Local.FirstOrDefault(x => x.Id == id);
    if (tracked != null) db.Entry(tracked).State = EntityState.Detached;
  }

  public async Task<Result<Unit>> MarkPending(Guid id, string paymentIntentId, int attempts)
  {
    try
    {
      var now = DateTime.UtcNow;
      await this.UpdateUnlessCharged(id, s => s
        .SetProperty(x => x.Status, (int)PenaltyStatus.Pending)
        .SetProperty(x => x.PaymentIntentId, paymentIntentId)
        .SetProperty(x => x.Attempts, attempts)
        .SetProperty(x => x.UpdatedAt, now));
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkPending failed for Id={Id} PaymentIntentId={PaymentIntentId}", id, paymentIntentId);
      throw;
    }
  }

  public async Task<Result<Unit>> SetIntentId(Guid id, string paymentIntentId)
  {
    try
    {
      // Fail loud if the row is gone: returning success here would let the caller proceed to
      // confirm without a durable PaymentIntentId, reopening the duplicate_request hole.
      // Persist only the intent id from a successful create; status/attempts stay as-is
      // (the row remains claimed/Processing for the rest of this pass). This makes the
      // intent id durable across a confirm failure so the retry reconciles it.
      //
      // Also fail loud if the penalty was detached by an account deletion while this charge was in
      // flight (UserId/HabitExecutionId now NULL): throwing here stops the caller BEFORE it confirms
      // the intent, so no money moves for a deleted user. One conditional UPDATE, read fresh from the
      // database (not a tracked entity, which may still hold the pre-deletion values).
      var now = DateTime.UtcNow;
      var updated = await db.Penalties
        .Where(x => x.Id == id && x.UserId != null && x.HabitExecutionId != null)
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.PaymentIntentId, paymentIntentId)
          .SetProperty(x => x.UpdatedAt, now));
      this.ForgetTracked(id);
      if (updated == 0)
      {
        var exists = await db.Penalties.AsNoTracking().AnyAsync(x => x.Id == id);
        throw new InvalidOperationException(exists
          ? $"Cannot persist payment intent: penalty {id} was detached by an account deletion."
          : $"Cannot persist payment intent: penalty {id} was not found.");
      }
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "SetIntentId failed for Id={Id} PaymentIntentId={PaymentIntentId}", id, paymentIntentId);
      throw;
    }
  }

  public async Task<Result<Unit>> Bump(Guid id, string error)
  {
    try
    {
      // Release the claim back to Pending so a later drain retries this row.
      var now = DateTime.UtcNow;
      await this.UpdateUnlessCharged(id, s => s
        .SetProperty(x => x.Attempts, x => x.Attempts + 1)
        .SetProperty(x => x.LastError, error)
        .SetProperty(x => x.Status, (int)PenaltyStatus.Pending)
        .SetProperty(x => x.UpdatedAt, now));
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Bump failed for Id={Id}", id);
      throw;
    }
  }

  public async Task<Result<Unit>> MarkFailed(Guid id, string error)
  {
    try
    {
      var now = DateTime.UtcNow;
      await this.UpdateUnlessCharged(id, s => s
        .SetProperty(x => x.Status, (int)PenaltyStatus.Failed)
        .SetProperty(x => x.LastError, error)
        .SetProperty(x => x.UpdatedAt, now));
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkFailed failed for Id={Id}", id);
      throw;
    }
  }

  public async Task<Result<Unit>> MarkSkipped(Guid id)
  {
    try
    {
      var now = DateTime.UtcNow;
      await this.UpdateUnlessCharged(id, s => s
        .SetProperty(x => x.Status, (int)PenaltyStatus.Skipped)
        .SetProperty(x => x.UpdatedAt, now));
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkSkipped failed for Id={Id}", id);
      throw;
    }
  }

  public async Task<Result<List<PenaltyPrincipal>>> Search(PenaltySearch search)
  {
    try
    {
      var q = db.Penalties.AsNoTracking().AsQueryable();
      if (!string.IsNullOrEmpty(search.UserId)) q = q.Where(x => x.UserId == search.UserId);
      if (search.Status != null)
      {
        var status = (int)search.Status.Value;
        q = q.Where(x => x.Status == status);
      }
      var items = await q
        .OrderByDescending(x => x.CreatedAt)
        .Skip(search.Skip)
        .Take(search.Limit)
        .ToListAsync();
      return items.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Search penalty failed for UserId={UserId} Status={Status}", search.UserId, search.Status);
      throw;
    }
  }
}
