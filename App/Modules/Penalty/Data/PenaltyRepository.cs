using System.Linq.Expressions;
using System.Transactions;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Penalty;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace App.Modules.Penalty.Data;

public class PenaltyRepository(MainDbContext db, ILogger<PenaltyRepository> logger) : IPenaltyRepository
{
  public const string HeldReason = "held: charity decommissioned";

  public async Task<Result<bool>> EnqueuePending(PenaltyRecord record)
  {
    PenaltyData? data = null;
    // Own transaction only when the caller has none (an ambient TransactionScope cannot nest one), so
    // the charity lock below is held until the insert commits.
    await using var ownTx = Transaction.Current == null && db.Database.CurrentTransaction == null
      ? await db.Database.BeginTransactionAsync()
      : null;
    try
    {
      // A decommissioned charity takes no more money: the penalty is still recorded, but as Held, so
      // the drain never charges it. The charity row is read FOR SHARE, so a concurrent decommission
      // (FOR UPDATE) either commits first and is seen here, or waits for this insert and then holds it.
      var decommissionedAt = await db.Database
        .SqlQuery<DateTime?>(
          $@"SELECT ""DecommissionedAt"" AS ""Value"" FROM ""Charities"" WHERE ""Id"" = {record.CharityId} FOR SHARE")
        .ToListAsync();
      if (decommissionedAt is [not null])
        record = record with { Status = PenaltyStatus.Held, LastError = HeldReason };

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
      if (ownTx != null) await ownTx.CommitAsync();
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

      await db.SaveChangesAsync();

      // Credit the (charity, currency) accrual ledger. The balance is keyed by (CharityId, Currency): a
      // charity can receive penalties in several currencies, each in its own row, so money is never
      // summed across currencies. ONE atomic upsert that adds to the value in the database: two
      // workers crediting the same charity can never lose a credit, whatever either has cached (a
      // read-modify-write through a tracked entity could add to a stale copy), and a first credit racing
      // another first credit cannot fail on the unique (CharityId, Currency) index.
      var now = DateTime.UtcNow;
      await db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO ""CharityBalances"" (""Id"", ""CharityId"", ""Currency"", ""AccruedCents"", ""UpdatedAt"")
        VALUES ({Guid.NewGuid()}, {penalty.CharityId}, {penalty.Currency}, {(long)penalty.AmountCents}, {now})
        ON CONFLICT (""CharityId"", ""Currency"") DO UPDATE
        SET ""AccruedCents"" = ""CharityBalances"".""AccruedCents"" + EXCLUDED.""AccruedCents"",
            ""UpdatedAt"" = EXCLUDED.""UpdatedAt""");
      // Any copy of that balance this context tracks is now stale: forget it so a later read is fresh.
      foreach (var tracked in db.CharityBalances.Local
                 .Where(b => b.CharityId == penalty.CharityId && b.Currency == penalty.Currency).ToList())
        db.Entry(tracked).State = EntityState.Detached;

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
  //
  // Held is final in the same way: a decommission moved the row on hold while a worker had it claimed,
  // and that worker's later writes (retry, fail, skip) must not put it back in the charge queue.
  private async Task<int> UpdateUnlessFinal(
    Guid id,
    Expression<Func<SetPropertyCalls<PenaltyData>,
      SetPropertyCalls<PenaltyData>>> set)
  {
    var updated = await db.Penalties
      .Where(x => x.Id == id
                  && x.Status != (int)PenaltyStatus.Charged
                  && x.Status != (int)PenaltyStatus.Held)
      .ExecuteUpdateAsync(set);
    this.ForgetTracked(id);
    return updated;
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
      await this.UpdateUnlessFinal(id, s => s
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
      //
      // Likewise a penalty moved on hold by a charity decommission while in flight: no money may be
      // taken for it, so stop BEFORE the confirm with a PenaltyHeldException the drain recognises.
      var now = DateTime.UtcNow;
      var updated = await db.Penalties
        .Where(x => x.Id == id && x.UserId != null && x.HabitExecutionId != null
                    && x.Status != (int)PenaltyStatus.Held)
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.PaymentIntentId, paymentIntentId)
          .SetProperty(x => x.UpdatedAt, now));
      this.ForgetTracked(id);
      if (updated == 0)
      {
        var status = await db.Penalties.AsNoTracking().Where(x => x.Id == id).Select(x => (int?)x.Status)
          .FirstOrDefaultAsync();
        if (status == (int)PenaltyStatus.Held) throw new PenaltyHeldException(id);
        var exists = status != null;
        throw new InvalidOperationException(exists
          ? $"Cannot persist payment intent: penalty {id} was detached by an account deletion."
          : $"Cannot persist payment intent: penalty {id} was not found.");
      }
      return new Unit();
    }
    catch (PenaltyHeldException)
    {
      throw; // expected when a decommission lands mid-charge; the drain handles it
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
      await this.UpdateUnlessFinal(id, s => s
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

  public async Task<Result<bool>> MarkFailed(Guid id, string error)
  {
    try
    {
      var now = DateTime.UtcNow;
      var updated = await this.UpdateUnlessFinal(id, s => s
        .SetProperty(x => x.Status, (int)PenaltyStatus.Failed)
        .SetProperty(x => x.LastError, error)
        .SetProperty(x => x.UpdatedAt, now));
      return updated > 0;
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkFailed failed for Id={Id}", id);
      throw;
    }
  }

  public async Task<Result<List<PenaltyPrincipal>>> GetHeldToReconcile(int batchSize, int maxChecks, TimeSpan olderThan)
  {
    try
    {
      var cutoff = DateTime.UtcNow - olderThan;
      var rows = await db.Penalties.AsNoTracking()
        .Where(x => x.Status == (int)PenaltyStatus.Held && x.PaymentIntentId != null
                    && x.Attempts < maxChecks && x.UpdatedAt < cutoff)
        .OrderBy(x => x.UpdatedAt)
        .Take(batchSize)
        .ToListAsync();
      return rows.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetHeldToReconcile failed for BatchSize={BatchSize}", batchSize);
      throw;
    }
  }

  public async Task<Result<Unit>> NoteHeldCheck(Guid id, string note)
  {
    try
    {
      var now = DateTime.UtcNow;
      await db.Penalties
        .Where(x => x.Id == id && x.Status == (int)PenaltyStatus.Held)
        .ExecuteUpdateAsync(s => s
          .SetProperty(x => x.Attempts, x => x.Attempts + 1)
          .SetProperty(x => x.LastError, note)
          .SetProperty(x => x.UpdatedAt, now));
      this.ForgetTracked(id);
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "NoteHeldCheck failed for Id={Id}", id);
      throw;
    }
  }

  public async Task<Result<Unit>> MarkSkipped(Guid id)
  {
    try
    {
      var now = DateTime.UtcNow;
      await this.UpdateUnlessFinal(id, s => s
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
